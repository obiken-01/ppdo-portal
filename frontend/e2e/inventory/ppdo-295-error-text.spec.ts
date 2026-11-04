import { expect, test, type Page } from "@playwright/test";
import { Api } from "../aip/seed";
import { signInAs } from "../aip/session";
import { ITEM, readInventoryWorld } from "./seed";

// PPDO-295…298 — a failed save shows readable text, whichever error shape the API sends.
// The older endpoints answer in plain text, but every 500 comes from the exception handler as
// JSON `{ data, error, message }`. Each page used to assume one shape, so the other one put an
// object on screen: "[object Object]", or a React render error and a blank page.
//
// The 500s are routed (the request never reaches the API, so nothing is saved). The Items
// Master duplicate is real: the seeded test item's Stock No. is refused by the API itself.
// Signs in as the seeded "E2E Inventory (test)" Staff account, one context at a time.

test.describe.configure({ mode: "serial", timeout: 120_000 });

const RUN = `E2E-${Date.now()}`;
const REFERENCE = "E2E-PPDO-295";
/** What the exception handler sends for any unhandled error (ExceptionHandlingMiddleware). */
const SERVER_ERROR_TEXT =
  `An unexpected error occurred. Please try again. If it keeps happening, quote reference ${REFERENCE}.`;
const SERVER_ERROR_BODY = JSON.stringify({ data: null, error: SERVER_ERROR_TEXT, message: null });

/** Answers `method` requests to `url` with the exception handler's JSON 500. */
async function failWithJson500(page: Page, method: string, url: RegExp) {
  await page.route(url, (route) =>
    route.request().method() === method
      ? route.fulfill({ status: 500, contentType: "application/json; charset=utf-8", body: SERVER_ERROR_BODY })
      : route.fallback(),
  );
}

/** Collects uncaught page errors (a React render crash lands here). */
function watchPageErrors(page: Page): string[] {
  const errors: string[] = [];
  page.on("pageerror", (e) => errors.push(e.message));
  return errors;
}

/** The message is on screen as text, the page still stands, and nothing crashed. */
async function expectReadable(page: Page, text: string, heading: string | RegExp, errors: string[]) {
  await expect(page.getByText(text)).toBeVisible({ timeout: 20_000 });
  await expect(page.getByText("[object Object]")).toHaveCount(0);
  await expect(page.getByRole("main").getByRole("heading", { level: 1, name: heading })).toBeVisible();
  expect(errors, "no uncaught page error").toEqual([]);
}

async function signIn(browser: Parameters<typeof signInAs>[0], baseURL: string | undefined) {
  return signInAs(browser, readInventoryWorld().account, baseURL);
}

test("PPDO-295 Distribution: a JSON 500 reads as text in the toast, not a blank page", async ({ browser, baseURL }) => {
  const { page, close } = await signIn(browser, baseURL);
  const errors = watchPageErrors(page);

  // The item's breakdown is answered here with stock on hand, so the form opens whatever the
  // test database holds. Only the allocate call fails.
  await page.route(new RegExp(`/api/distributions/item/${ITEM.stockNo}$`), (route) =>
    route.request().method() === "GET"
      ? route.fulfill({
          status: 200, contentType: "application/json",
          body: JSON.stringify({
            stockNo: ITEM.stockNo, description: ITEM.description, category: null, unit: ITEM.unit,
            totalOrdered: 5, totalDelivered: 5, totalDistributed: 0, onHand: 5,
            deliveryItems: [{
              deliveryItemId: "00000000-0000-0000-0000-000000000295", stockBalanceId: null, source: "Delivery",
              deliveryRef: "DEL-20261103-E2E95", deliveryDate: "2026-11-03", prId: "00000000-0000-0000-0000-000000000001",
              prNo: "101-1041-GF-2026-11-03-001", qtyDelivered: 5, qtyDistributed: 0, qtyAvailable: 5, distributions: [],
            }],
          }),
        })
      : route.fallback(),
  );
  await failWithJson500(page, "POST", /\/api\/distributions\/item\/[^/]+\/allocate$/);

  await page.goto("/inventory/distribution");
  await page.getByPlaceholder("Type stock no. or description…").fill(ITEM.stockNo);
  await page.getByRole("listitem").filter({ hasText: ITEM.description }).first().click();
  await page.getByRole("button", { name: "Distribute", exact: true }).click();

  const division = page.getByRole("combobox").first();
  await expect(division.locator("option").nth(1)).toBeAttached({ timeout: 20_000 });
  await division.selectOption({ index: 1 });
  await page.getByPlaceholder("0").fill("1");
  await page.getByPlaceholder("Recipient name").fill(`${RUN} recipient`);
  await page.getByRole("button", { name: "Confirm Distribution" }).click();

  await expectReadable(page, SERVER_ERROR_TEXT, "Distribution", errors);
  await close();
});

test("PPDO-296 Account profile: a JSON 500 reads as text under the form", async ({ browser, baseURL }) => {
  const { page, close } = await signIn(browser, baseURL);
  const errors = watchPageErrors(page);
  await failWithJson500(page, "PUT", /\/api\/users\/me$/);

  await page.goto("/account");
  // Save is enabled before the profile arrives (PPDO-281), and a click then is refused locally with
  // "Full name is required." before any PUT. Wait for the loaded form, as a user would (PPDO-300).
  await expect(page.getByPlaceholder("Full legal name")).not.toHaveValue("", { timeout: 20_000 });
  const save = page.getByRole("button", { name: "Save Changes" });
  await expect(save).toBeEnabled({ timeout: 20_000 });
  await save.click();

  await expectReadable(page, SERVER_ERROR_TEXT, "My Account", errors);
  await close();
});

test("PPDO-296 Change password: a JSON 500 reads as text under the form", async ({ browser, baseURL }) => {
  const { page, close } = await signIn(browser, baseURL);
  const errors = watchPageErrors(page);
  await failWithJson500(page, "PUT", /\/api\/users\/me\/password$/);

  await page.goto("/account");
  await page.getByRole("button", { name: "Security" }).click();
  // Routed, so the real password is never changed.
  const form = page.locator("form").filter({ has: page.locator('input[autocomplete="current-password"]') });
  await form.locator('input[autocomplete="current-password"]').fill("NotTheRealOne1");
  await form.locator('input[autocomplete="new-password"]').nth(0).fill("E2eChanged1");
  await form.locator('input[autocomplete="new-password"]').nth(1).fill("E2eChanged1");
  await form.getByRole("button", { name: "Save Changes" }).click();

  await expectReadable(page, SERVER_ERROR_TEXT, "My Account", errors);
  await close();
});

test("PPDO-297 Create PR: a JSON 500 reads as text, not [object Object]", async ({ browser, baseURL }) => {
  const { page, close } = await signIn(browser, baseURL);
  const errors = watchPageErrors(page);
  await failWithJson500(page, "POST", /\/api\/purchase-requests$/);

  await page.goto("/inventory/create-pr");
  await expect(page.getByRole("heading", { level: 1, name: "Create PR" })).toBeVisible();
  await page.waitForLoadState("networkidle");
  await page.getByPlaceholder("e.g. General Fund").fill("General Fund");
  await page.getByPlaceholder("Full name").fill(`${RUN} requester`);
  await page.getByPlaceholder("e.g. Planning Officer II").fill("Tester");
  await page.getByPlaceholder("Activity description (long text supported)").fill(`${RUN} error text`);
  const row = page.getByRole("row").filter({ has: page.getByPlaceholder("Stock No.") }).first();
  await row.getByPlaceholder("Stock No.").fill(ITEM.stockNo);
  await page.getByRole("listitem").filter({ hasText: ITEM.description }).first().click();
  await row.getByRole("spinbutton").first().fill("1");
  await page.getByRole("button", { name: "Submit Purchase Request" }).click();

  await expectReadable(page, SERVER_ERROR_TEXT, "Create PR", errors);
  await expect(page.getByRole("heading", { name: "PR Submitted" })).toHaveCount(0);
  await close();
});

test("PPDO-297 Receive Delivery: a JSON 500 reads as text, not [object Object]", async ({ browser, baseURL, request }) => {
  // An Open PR in the test division to receive against (the delivery itself never lands).
  const world = readInventoryWorld();
  const login = await Api.login(request, world.account.username, world.account.password);
  expect(login, "inventory account login").not.toBeNull();
  const created = await login!.call<{ prNo: string }>("post", "/purchase-requests", {
    prDate: new Date().toISOString().slice(0, 10), prNo: null, department: "PPDO", division: world.divisionName,
    fund: "General Fund", requestedBy: `${RUN} requester`, position: "Tester",
    approvedBy: null, approvingPosition: null, aipCode: null, accountNo: null, accountTitle: null,
    program: null, project: null, activity: `${RUN} delivery error text`, saiNo: null, alobsNo: null,
    items: [{ stockNo: ITEM.stockNo, description: ITEM.description, unit: ITEM.unit, quantity: 2, unitCost: ITEM.unitCost, itemType: null }],
  });

  const { page, close } = await signIn(browser, baseURL);
  const errors = watchPageErrors(page);
  await failWithJson500(page, "POST", /\/api\/deliveries$/);

  await page.goto("/inventory/receive-delivery");
  const picker = page.getByPlaceholder("Type PR number, division, or status…");
  await expect(picker).toBeEnabled({ timeout: 20_000 });
  await picker.fill(created.prNo);
  await page.getByRole("listitem").filter({ hasText: created.prNo }).first().click();
  const row = page.getByRole("row").filter({ hasText: ITEM.stockNo });
  await expect(row).toBeVisible();
  await row.getByRole("spinbutton").last().fill("1");
  await page.getByRole("button", { name: /Submit Delivery/ }).first().click();

  await expectReadable(page, SERVER_ERROR_TEXT, "Receive Delivery", errors);
  await expect(page.getByRole("heading", { name: "Delivery Recorded" })).toHaveCount(0);
  await close();
});

test("PPDO-298 Items Master: a duplicate Stock No. shows the API's own message", async ({ browser, baseURL }) => {
  const { page, close } = await signIn(browser, baseURL);
  const errors = watchPageErrors(page);

  await page.goto("/inventory/items-master");
  const add = page.getByRole("button", { name: "Add Item" });
  await expect(add).toBeEnabled({ timeout: 20_000 });
  await add.click();
  await page.getByPlaceholder("SUP-001").fill(ITEM.stockNo);
  await page.getByPlaceholder("Full description").fill(`${RUN} duplicate`);
  await page.getByPlaceholder("ream").fill("pc");
  await page.getByRole("button", { name: /^✓?\s*Save$/ }).click();

  // The real API's 409 text (ItemService), not the generic "Failed to save."
  await expectReadable(page, `StockNo '${ITEM.stockNo}' already exists in the catalog.`, "Items Master", errors);
  await expect(page.getByText("Failed to save. Please try again.")).toHaveCount(0);
  await close();
});
