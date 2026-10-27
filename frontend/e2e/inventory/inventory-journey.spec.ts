import { expect, test, type Page } from "@playwright/test";
import { signInAs } from "../aip/session";
import { ITEM, readInventoryWorld } from "./seed";

// PPDO-259 — inventory journey, as a Staff user in the seeded "E2E Inventory (test)" division:
//   create a PR for 10 × the test item → receive 4 → the PR is PartiallyDelivered →
//   receive the other 6 → FullyDelivered.
// Status changes on delivery are automatic (CLAUDE.md: Open → PartiallyDelivered → FullyDelivered).
// Each run raises a new PR in that division; nothing is shared with a real division.

test.describe.configure({ mode: "serial", timeout: 120_000 });

const RUN = `E2E ${new Date().toISOString().slice(5, 19).replace("T", " ")} ${Math.random().toString(36).slice(2, 6)}`;
let prNo = "";

async function signIn(browser: Parameters<typeof signInAs>[0], baseURL: string | undefined) {
  return signInAs(browser, readInventoryWorld().account, baseURL);
}

/** Receives `qty` of the test item against the run's PR, and waits for the delivery ref. */
async function receive(page: Page, qty: number) {
  await page.goto("/inventory/receive-delivery");
  await expect(page.getByRole("heading", { level: 1, name: "Receive Delivery" })).toBeVisible();
  const picker = page.getByPlaceholder("Type PR number, division, or status…");
  await expect(picker).toBeEnabled({ timeout: 20_000 });
  await picker.fill(prNo);
  await page.getByRole("listitem").filter({ hasText: prNo }).first().click();

  const row = page.getByRole("row").filter({ hasText: ITEM.stockNo });
  await expect(row).toBeVisible();
  await row.getByRole("spinbutton").last().fill(String(qty));
  await page.getByRole("button", { name: /Submit Delivery/ }).first().click();
  await expect(page.getByRole("heading", { name: "Delivery Recorded" })).toBeVisible({ timeout: 20_000 });
  // DEL-YYYYMMDD-XXXXX (Manila date; the 5-character suffix is letters and digits).
  await expect(page.getByText(/DEL-\d{8}-[A-Z0-9]{5}/).first()).toBeVisible();
}

/** The run's PR status on PR List, as the user reads it. */
async function prStatus(page: Page): Promise<string> {
  await page.goto("/inventory/pr-register");
  const row = page.getByRole("row").filter({ hasText: prNo });
  await expect(row).toBeVisible({ timeout: 20_000 });
  return row.innerText();
}

test("staff creates a PR for 10 of the test item", async ({ browser, baseURL }) => {
  const { page, close } = await signIn(browser, baseURL);
  await page.goto("/inventory/create-pr");
  await expect(page.getByRole("heading", { level: 1, name: "Create PR" })).toBeVisible();
  await page.waitForLoadState("networkidle");

  // Staff raise PRs for their own division only: it's filled in and locked.
  const division = page.getByRole("combobox").first();
  await expect(division).toBeDisabled();
  await expect(division).toHaveValue(/E2E Inventory/);

  await page.getByPlaceholder("e.g. General Fund").fill("General Fund");
  await page.getByPlaceholder("Activity description (long text supported)").fill(RUN);

  // Picking a Stock No. fills the description, unit and cost from Items Master (bidirectional lookup).
  const row = page.getByRole("row").filter({ has: page.getByPlaceholder("Stock No.") }).first();
  await row.getByPlaceholder("Stock No.").fill(ITEM.stockNo);
  // Picking the suggestion is what fills the row; typing alone leaves it free text.
  await page.getByRole("listitem").filter({ hasText: ITEM.description }).first().click();
  await expect(row.getByPlaceholder("Item description *")).toHaveValue(ITEM.description);
  await row.getByRole("spinbutton").first().fill("10");

  await page.getByRole("button", { name: "Submit Purchase Request" }).click();
  await expect(page.getByRole("heading", { name: "PR Submitted" })).toBeVisible({ timeout: 20_000 });
  // 101-1041-GF-YYYY-MM-DD-XXX: the sequence runs across all PRs and is padded to *at least* 3
  // digits (PurchaseRequestService.GeneratePRNoAsync), so a busy database shows 4.
  const PR_NO = /^101-1041-GF-\d{4}-\d{2}-\d{2}-\d{3,}$/;
  prNo = (await page.getByText(PR_NO).first().innerText()).trim();

  expect(await prStatus(page)).toMatch(/\bOpen\b/);
  await close();
});

test("receiving 4 of 10 makes the PR Partially Delivered", async ({ browser, baseURL }) => {
  const { page, close } = await signIn(browser, baseURL);
  await receive(page, 4);
  expect(await prStatus(page)).toMatch(/Partially Delivered/);
  await close();
});

test("receiving the other 6 makes it Fully Delivered", async ({ browser, baseURL }) => {
  const { page, close } = await signIn(browser, baseURL);
  await receive(page, 6);
  expect(await prStatus(page)).toMatch(/Fully Delivered/);
  await close();
});
