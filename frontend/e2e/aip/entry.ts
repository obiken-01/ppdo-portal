import { expect, type Locator, type Page } from "@playwright/test";
import { FISCAL_YEAR, readWorld } from "./seed";

// Page helpers for AIP Entry (/budget-planning/aip/entry), used by the PPDO-259 journeys.
// Locators follow what an encoder sees: placeholders, button names, field labels.

export interface ActivitySpec {
  program: RegExp;
  project: string;
  title: string;
  /** Account search text and the option to pick, e.g. "Office Supplies". */
  account: string;
  /** Amounts in pesos, as typed into the line. */
  mooe?: number;
  co?: number;
}

export async function openEntry(page: Page) {
  await page.goto("/budget-planning/aip/entry");
  await expect(page.getByRole("heading", { level: 1, name: "AIP Entry" })).toBeVisible();
  await page.waitForLoadState("networkidle");
}

/** The division panel's figure under one of its labels, e.g. "Encoded (MOOE + CO)". */
export function divisionFigure(page: Page, label: RegExp): Locator {
  return page.getByText(label).locator("xpath=following-sibling::p[1]");
}

export async function pickProgram(page: Page, program: RegExp) {
  await page.getByPlaceholder("Search program…").click();
  await page.getByRole("button", { name: program }).click();
}

/** Drills the picker down to one activity (AIP Entry and AIP Review share it). */
export async function pickActivity(page: Page, program: RegExp, project: RegExp, activity: RegExp) {
  await pickProgram(page, program);
  await page.getByPlaceholder("Search project…").click();
  await page.getByRole("button", { name: project }).first().click();
  await page.getByPlaceholder("Search activity…").click();
  await page.getByRole("button", { name: activity }).first().click();
}

/**
 * Opens an inline "+ Add …" box, types the name and adds it. The panel re-renders right after the
 * previous add, which can swallow the first click, so opening the box is retried until it shows.
 */
async function inlineAdd(page: Page, button: string, placeholder: string, name: string) {
  const box = page.getByPlaceholder(placeholder);
  await expect(async () => {
    if (!(await box.isVisible())) await page.getByRole("button", { name: button }).click();
    await expect(box).toBeVisible({ timeout: 2_000 });
  }).toPass({ timeout: 15_000 });
  await box.fill(name);
  await page.getByRole("button", { name: "Add", exact: true }).click();
  await expect(box).toBeHidden();
}

/** Adds a project, an activity with an eSRE code, the GF fund, and one expenditure line. */
export async function addActivity(page: Page, a: ActivitySpec) {
  await pickProgram(page, a.program);

  await inlineAdd(page, "+ Add project", "Project name", a.project);
  await inlineAdd(page, "+ Add activity", "Activity title", a.title);
  // The activity panel replaces the project panel; its eSRE row says whether it has loaded.
  await expect(page.getByRole("term").filter({ hasText: "eSRE code" })).toBeVisible();

  // eSRE code: the only detail the submit checklist requires.
  await page.getByRole("button", { name: "Edit details" }).click();
  await page.getByRole("combobox", { name: "eSRE code" }).selectOption({ label: "ID — Institutional Development" });
  await page.getByRole("button", { name: "Save details" }).click();
  await expect(page.getByRole("button", { name: "Save details" })).toBeHidden();

  // The activity's fund, then one account line.
  const fund = page.getByRole("textbox", { name: "Funding source" });
  await fund.click();
  await fund.fill("General");
  await page.getByRole("button", { name: "GF (ceiling applies)" }).click();

  await page.getByRole("button", { name: "+ Add Account" }).click();
  const row = page.getByRole("row").filter({ has: page.getByPlaceholder("Search accounts…") });
  await row.getByPlaceholder("Search accounts…").click();
  await row.getByPlaceholder("Search accounts…").fill(a.account);
  await page.getByRole("button", { name: new RegExp(a.account, "i") }).first().click();
  const amounts = row.getByRole("textbox", { name: "0.00" });
  if (a.mooe) await amounts.nth(1).fill(String(a.mooe));
  if (a.co) await amounts.nth(2).fill(String(a.co));
  await row.getByRole("button", { name: "Save" }).click();
  await expect(page.getByPlaceholder("Search accounts…")).toBeHidden();
}

/**
 * Submits the encoder's division to the department head and waits for the server's answer.
 * `warnings` are lines the confirm must list (it always lists missing proposals, for example).
 */
export async function submitDivision(page: Page, division: "A" | "B", warnings: (string | RegExp)[] = []) {
  await page.getByRole("button", { name: `Submit E2E Division ${division} (test)` }).click();
  const dialog = page.getByRole("dialog", { name: `Submit E2E Division ${division} (test)?` });
  for (const w of warnings) await expect(dialog.getByRole("listitem").filter({ hasText: w })).toBeVisible();
  await dialog.getByRole("button", { name: /^Submit/ }).click();
  // ⚠️ Wait for the confirmed state, not for the button to disappear: it hides at once, and
  // closing the browser then would cancel the request.
  await expect(page.getByRole("status").filter({ hasText: "your department head can return it" }).first()).toBeVisible();
  await expect(page.getByText("With your department head")).toBeVisible();
}

/** The department head sends the office's AIP to PPDO. The confirm opens after a proposal check. */
export async function sendToPpdo(page: Page) {
  await page.getByRole("button", { name: "Send to PPDO" }).click();
  // The confirm (it lists projects still without a proposal) offers "Send anyway".
  const dialog = page.getByRole("dialog", { name: "Send to PPDO?" });
  await expect(dialog).toBeVisible({ timeout: 15_000 });
  await dialog.getByRole("button", { name: /^Send/ }).click();
  await expect(page.getByText("This office’s AIP is with PPDO and cannot be changed here.")).toBeVisible();
}

/** AIP Review for the E2E office (what the search's office link opens). */
export async function openReview(page: Page) {
  const w = readWorld();
  await page.goto(`/budget-planning/aip/review?officeId=${w.officeId}&fiscalYear=${FISCAL_YEAR}`);
  await expect(page.getByRole("heading", { level: 1, name: "AIP Review" })).toBeVisible();
  await page.waitForLoadState("networkidle");
}

/**
 * Changes the MOOE of the open activity's first expenditure line. `scope` is the page, or the
 * reviewer's "Edit amounts" dialog. Amounts are typed in pesos.
 */
export async function editFirstLineMooe(scope: Page | Locator, mooe: number) {
  await scope.getByRole("button", { name: "Edit", exact: true }).first().click();
  // A `has:` locator must come from the page, not from `scope`, or it is resolved twice over.
  const page = "goto" in scope ? scope : scope.page();
  const row = scope.getByRole("row").filter({ has: page.getByPlaceholder("Search accounts…") });
  await row.getByRole("textbox", { name: "0.00" }).nth(1).fill(String(mooe));
  await row.getByRole("button", { name: "Save" }).click();
  await expect(scope.getByRole("button", { name: "Edit", exact: true }).first()).toBeVisible();
}
