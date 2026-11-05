import { expect, test, type Page, type Response } from "@playwright/test";
import { Api, readWorld } from "./seed";
import { signIn } from "./session";
import { resetAipOffice } from "./reset";
import { addActivity, openEntry, pickActivity } from "./entry";

// PPDO-271 items 1, 4 and 5 — the AIP expenditure line editor on its bad paths, as encoder A in
// the seeded E2E office (writes stay in division A):
//   • an incomplete line is refused in the editor, next to the field, and nothing is sent;
//   • a negative amount cannot be typed, and the server refuses one that is sent anyway;
//   • a double-click on Save sends exactly one POST (PPDO-244);
//   • a save against a line somebody else changed gets the conflict panel, not a silent overwrite
//     (V18-71 / PPDO-121).
// Encoder B cannot write to division A's line, so the "other writer" in the conflict case is a
// second session of encoder A, made through the API: the guard compares row versions, not people.

test.describe.configure({ mode: "serial", timeout: 120_000 });

const RUN = `E2E ${new Date().toISOString().slice(5, 19).replace("T", " ")} ${Math.random().toString(36).slice(2, 6)}`;
const TITLE = `${RUN} bad paths`;
let lineId = 0;
let lineVersion = "";
let activityId = 0;
let accountId = 0;
let fundingSourceId = 0;

const isLineWrite = (method: string) => (r: Response) =>
  r.request().method() === method && /\/budget-planning\/aip\/(activities\/\d+\/expenditures|expenditures\/\d+)$/.test(r.url());

async function openActivity(page: Page) {
  await openEntry(page);
  await pickActivity(page, /E2E PROGRAM ONE/, new RegExp(`${RUN} project`), new RegExp(TITLE));
}

/** The editor row that "+ Add Account" opens. */
async function openNewLine(page: Page) {
  await page.getByRole("button", { name: "+ Add Account" }).click();
  const row = page.getByRole("row").filter({ has: page.getByPlaceholder("Search accounts…") });
  await expect(row).toBeVisible();
  return row;
}

test.beforeAll(async ({ request, browser, baseURL }) => {
  test.setTimeout(120_000);
  await resetAipOffice(request);
  const { page, close } = await signIn(browser, "encoderA", baseURL);
  await openEntry(page);
  // The first line is saved by addActivity; its response carries the line id and row version.
  const saved = page.waitForResponse(isLineWrite("POST"));
  await addActivity(page, {
    program: /E2E PROGRAM ONE/, project: `${RUN} project`, title: TITLE, account: "Office Supplies", mooe: 10_000,
  });
  type Written = { line: { id: number; accountId: number; fundingSourceId: number; rowVersion: string }; activityId: number };
  const body = (await (await saved).json()) as { data?: Written } & Partial<Written>;
  const written = (body.data ?? body) as Written;
  lineId = written.line.id;
  lineVersion = written.line.rowVersion;
  activityId = written.activityId;
  accountId = written.line.accountId;
  fundingSourceId = written.line.fundingSourceId;
  await close();
});

test("a line with no account or amount is refused in the editor and nothing is sent", async ({ browser, baseURL }) => {
  const { page, close } = await signIn(browser, "encoderA", baseURL);
  await openActivity(page);
  const writes: string[] = [];
  page.on("request", (r) => {
    if (["POST", "PUT"].includes(r.method()) && /expenditures/.test(r.url())) writes.push(`${r.method()} ${r.url()}`);
  });

  const row = await openNewLine(page);
  // Nothing filled in: the account asks for a pick and the amount for a figure, each under its field.
  await expect(row.getByText("Pick an account.")).toBeVisible();
  await expect(row.getByText("Enter an amount.")).toBeVisible();
  const save = row.getByRole("button", { name: "Save" });
  await expect(save).toBeDisabled();

  // An amount alone is not enough: the account message stays.
  await row.getByRole("textbox", { name: "0.00" }).nth(1).fill("1500");
  await expect(row.getByText("Enter an amount.")).toBeHidden();
  await expect(row.getByText("Pick an account.")).toBeVisible();
  await expect(save).toBeDisabled();

  // Cancel leaves no new line behind (only the one beforeAll saved).
  await row.getByRole("button", { name: "Cancel" }).click();
  await expect(page.getByPlaceholder("Search accounts…")).toBeHidden();
  expect(writes, "the editor sent a write for an incomplete line").toEqual([]);
  await close();
});

test("a negative amount cannot be typed, and the server refuses one sent anyway", async ({ browser, baseURL, request }) => {
  const { page, close } = await signIn(browser, "encoderA", baseURL);
  await openActivity(page);

  const row = await openNewLine(page);
  const mooe = row.getByRole("textbox", { name: "0.00" }).nth(1);
  await mooe.fill("-5000");
  // The money input drops the minus sign; the figure is 5,000, never -5,000.
  await expect(mooe).not.toHaveValue(/-/);
  await expect(mooe).toHaveValue("5,000");
  await row.getByRole("button", { name: "Cancel" }).click();
  await close();

  // The same line sent straight to the API with a negative MOOE is refused and stores nothing.
  const w = readWorld();
  const a = w.accounts.encoderA;
  const api = (await Api.login(request, a.username, a.password))!;
  await expect(api.call("post", `/budget-planning/aip/activities/${activityId}/expenditures`,
    { accountId, fundingSourceId, ps: 0, mooe: -5000, co: 0, procurementItems: [] })).rejects.toThrow(/→ 4\d\d/);
});

test("double-clicking Save sends exactly one POST (PPDO-244)", async ({ browser, baseURL }) => {
  const { page, close } = await signIn(browser, "encoderA", baseURL);
  await openActivity(page);
  const posts: string[] = [];
  page.on("request", (r) => { if (r.method() === "POST" && /\/activities\/\d+\/expenditures$/.test(r.url())) posts.push(r.url()); });

  const row = await openNewLine(page);
  await row.getByPlaceholder("Search accounts…").click();
  await row.getByPlaceholder("Search accounts…").fill("Office Supplies");
  await page.getByRole("button", { name: /Office Supplies/i }).first().click();
  await row.getByRole("textbox", { name: "0.00" }).nth(1).fill("2500");

  const answered = page.waitForResponse(isLineWrite("POST"));
  await row.getByRole("button", { name: "Save" }).dblclick();
  expect((await answered).ok()).toBe(true);
  await expect(page.getByPlaceholder("Search accounts…")).toBeHidden();
  // Give a second request time to show itself before counting.
  await page.waitForTimeout(1_000);
  expect(posts, "a double-click must not post the line twice").toHaveLength(1);
  await close();
});

test("saving a line somebody else changed shows the conflict panel and saves nothing (PPDO-121)", async ({ browser, baseURL, request }) => {
  const { page, close } = await signIn(browser, "encoderA", baseURL);
  await openActivity(page);

  // Encoder A opens the first line for editing and types a new MOOE, without saving…
  await page.getByRole("button", { name: "Edit", exact: true }).first().click();
  const row = page.getByRole("row").filter({ has: page.getByPlaceholder("Search accounts…") });
  await row.getByRole("textbox", { name: "0.00" }).nth(1).fill("11111");

  // …meanwhile a second session saves the same line against the version both of them loaded.
  const w = readWorld();
  const a = w.accounts.encoderA;
  const other = (await Api.login(request, a.username, a.password))!;
  await other.call("put", `/budget-planning/aip/expenditures/${lineId}`,
    { accountId, fundingSourceId, ps: 0, mooe: 22_222, co: 0, procurementItems: [], rowVersion: lineVersion });

  // The first session saves its stale copy: refused, with the panel.
  await row.getByRole("button", { name: "Save" }).click();
  const panel = page.getByRole("alert").filter({ hasText: "while you were editing it" });
  await expect(panel).toBeVisible({ timeout: 20_000 });
  await expect(panel).toContainText("Nothing was saved");
  await expect(panel.getByRole("button", { name: /Overwrite/i })).toBeVisible();
  await close();
});
