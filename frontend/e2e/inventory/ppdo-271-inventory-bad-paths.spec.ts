import { expect, test, type Page } from "@playwright/test";
import { Api } from "../aip/seed";
import { signInAs } from "../aip/session";
import { readWorkbook, hasAmount, hasText } from "../xlsx";
import { readInventoryWorld } from "./seed";

// PPDO-271 items 1 and 3 — inventory bad paths, as Staff in the seeded "E2E Inventory (test)"
// division (writes stay there, under unique E2E-<timestamp> names):
//   • Create PR with no line items: refused next to the items, no request sent, no PR saved;
//   • PR report → Export to Excel: an .xlsx arrives and holds the total the screen shows.

test.describe.configure({ mode: "serial", timeout: 120_000 });

const RUN = `E2E-${Date.now()}`;

async function signIn(browser: Parameters<typeof signInAs>[0], baseURL: string | undefined) {
  return signInAs(browser, readInventoryWorld().account, baseURL);
}

test("Create PR with header filled but no items is refused, and no PR is saved", async ({ browser, baseURL }) => {
  const { page, close } = await signIn(browser, baseURL);
  await page.goto("/inventory/create-pr");
  await expect(page.getByRole("heading", { level: 1, name: "Create PR" })).toBeVisible();
  await page.waitForLoadState("networkidle");

  const posts: string[] = [];
  page.on("request", (r) => { if (r.method() === "POST" && /\/purchase-requests$/.test(r.url())) posts.push(r.url()); });

  // Fund left empty: it says so under itself. (Requested By and Position are prefilled from the
  // signed-in user's profile, so Fund is the required field an empty form really lacks.)
  await page.getByRole("button", { name: "Submit Purchase Request" }).click();
  await expect(page.getByText("Fund is required.")).toBeVisible();

  // Header filled, items left blank: the items line is what stops it.
  await page.getByPlaceholder("e.g. General Fund").fill("General Fund");
  await page.getByPlaceholder("Full name").fill(`${RUN} requester`);
  await page.getByPlaceholder("e.g. Planning Officer II").fill("Tester");
  await page.getByRole("button", { name: "Submit Purchase Request" }).click();
  await expect(page.getByRole("main").getByText("At least one line item is required.")).toBeVisible();
  await expect(page.getByRole("heading", { name: "PR Submitted" })).toHaveCount(0);

  expect(posts, "a PR with no items must not be sent").toEqual([]);
  await close();
});

test("PR report exports an .xlsx whose workbook holds the total on screen", async ({ browser, baseURL, request }) => {
  const world = readInventoryWorld();
  const login = await Api.login(request, world.account.username, world.account.password);
  expect(login, "inventory account login").not.toBeNull();
  const created = await login!.call<{ id: string; prNo: string; totalAmount: number }>("post", "/purchase-requests", {
    prDate: new Date().toISOString().slice(0, 10), prNo: null, department: "PPDO", division: world.divisionName,
    fund: "General Fund", requestedBy: `${RUN} requester`, position: "Tester",
    approvedBy: null, approvingPosition: null, aipCode: null, accountNo: null, accountTitle: null,
    program: null, project: null, activity: `${RUN} export check`, saiNo: null, alobsNo: null,
    items: [{ stockNo: null, description: `${RUN} item`, unit: "pc", quantity: 3, unitCost: 123.45, itemType: null }],
  });
  expect(created.totalAmount).toBeCloseTo(370.35, 2);

  const { page, close } = await signIn(browser, baseURL);
  await page.goto(`/inventory/pr-report?id=${encodeURIComponent(created.id)}`);
  await expect(page.getByText(`Total: ₱370.35`)).toBeVisible({ timeout: 20_000 });

  const download = page.waitForEvent("download");
  await page.getByRole("button", { name: "Export to Excel" }).click();
  const file = await download;
  expect(file.suggestedFilename()).toMatch(/\.xlsx$/);

  const path = await file.path();
  const fs = await import("node:fs");
  const sheets = readWorkbook(fs.readFileSync(path));
  expect(sheets.length).toBeGreaterThan(0);
  expect(hasText(sheets, created.prNo), "the workbook names the PR").toBe(true);
  expect(hasAmount(sheets, 370.35), "the workbook holds the total the screen shows").toBe(true);
  await close();
});
