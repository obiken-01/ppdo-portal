import type { Page } from "@playwright/test";
import { expect, test } from "./fixtures";

// PPDO-260 S1/S2: when an Inventory load fails, the page says so and offers Retry. It must
// not fall back to zeros, an empty PR picker, "all items are sufficiently stocked", or the
// previous PR's data, all of which read as real answers.
//
// Read-only. Each test answers one GET with a 500 (a server error, so the app's
// network-failure path to /reconnecting stays out of it), then lets it through for Retry.
// Personas without inventory access are redirected away and the test is skipped.

const PR_LIST = /\/api\/purchase-requests(\?.*)?$/;

async function openOrSkip(page: Page, path: string) {
  await page.goto(path);
  await page.waitForLoadState("networkidle");
  test.skip(!page.url().includes(path), `persona has no inventory access (landed on ${page.url()})`);
}

/** Answers matching GETs with a 500 until the returned function is called. */
async function failUntilReleased(page: Page, url: RegExp | string) {
  let failing = true;
  await page.route(url, (route) =>
    failing && route.request().method() === "GET"
      ? route.fulfill({ status: 500, body: "E2E: simulated failure" })
      : route.fallback(),
  );
  return () => { failing = false; };
}

test.describe("PPDO-260 Inventory dashboard", () => {
  test("failed stats show dashes and Retry, not zeros", async ({ page }) => {
    const release = await failUntilReleased(page, /\/api\/inventory\/stats$/);
    await openOrSkip(page, "/inventory");

    const error = page.getByText("Couldn't load the inventory stats.");
    await expect(error).toBeVisible();
    await expect(page.getByText("Total PRs").locator("..")).toContainText("—");

    release();
    await page.getByRole("alert").filter({ has: error }).getByRole("button", { name: "Retry" }).click();
    await expect(error).toBeHidden();
  });

  test("failed purchase requests show an error, not 'No purchase requests found'", async ({ page }) => {
    const release = await failUntilReleased(page, PR_LIST);
    await openOrSkip(page, "/inventory");

    const error = page.getByText("Couldn't load the purchase requests.");
    await expect(error).toBeVisible();
    await expect(page.getByText("No purchase requests found.")).toBeHidden();

    release();
    await page.getByRole("alert").filter({ has: error }).getByRole("button", { name: "Retry" }).click();
    await expect(error).toBeHidden();
  });

  test("failed ledger shows an error, not 'All items are sufficiently stocked'", async ({ page }) => {
    const release = await failUntilReleased(page, /\/api\/inventory\/ledger$/);
    await openOrSkip(page, "/inventory");

    const error = page.getByText("Couldn't load the stock levels, so alerts can't be checked.");
    await expect(error).toBeVisible();
    await expect(page.getByText("All items are sufficiently stocked.", { exact: false })).toBeHidden();

    release();
    await page.getByRole("alert").filter({ has: error }).getByRole("button", { name: "Retry" }).click();
    await expect(error).toBeHidden();
  });
});

test.describe("PPDO-260 PR Report", () => {
  test("failed PR list shows an error instead of the empty picker prompt", async ({ page }) => {
    const release = await failUntilReleased(page, PR_LIST);
    await openOrSkip(page, "/inventory/pr-report");

    const error = page.getByText("Couldn't load the purchase requests.");
    await expect(error).toBeVisible();
    await expect(page.getByText("Select a Purchase Request to view its report")).toBeHidden();

    release();
    await page.getByRole("button", { name: "Retry" }).click();
    await expect(error).toBeHidden();
    await expect(page.getByText("Select a Purchase Request to view its report")).toBeVisible();
  });

  test("failed report shows an error, never the previous PR's report", async ({ page }) => {
    await openOrSkip(page, "/inventory/pr-report");
    const picker = page.getByPlaceholder("Search PR number, division, status…");
    await picker.click();
    const options = page.locator("ul li[class*='cursor-pointer']");
    test.skip((await options.count()) < 2, "needs at least two PRs in the database");
    const secondPrNo = await options.nth(1).locator(".font-mono").innerText();

    // Load the first PR's report normally.
    await options.nth(0).click();
    await expect(page.getByText("PR Details")).toBeVisible();

    // The second PR's report fails: the first one's sections must go away. Type its number
    // (clearing the box would also clear the report, which is not the case under test).
    const release = await failUntilReleased(page, /\/api\/purchase-requests\/[^/]+\/report$/);
    await picker.fill(secondPrNo);
    await options.filter({ hasText: secondPrNo }).first().click();
    const error = page.getByText("Couldn't load this PR's report.");
    await expect(error).toBeVisible();
    await expect(page.getByText("PR Details")).toBeHidden();

    release();
    await page.getByRole("button", { name: "Retry" }).click();
    await expect(error).toBeHidden();
    await expect(page.getByText("PR Details")).toBeVisible();
  });
});

test.describe("PPDO-260 Receive Delivery", () => {
  test("failed PR list shows an error under the picker", async ({ page }) => {
    const release = await failUntilReleased(page, PR_LIST);
    await openOrSkip(page, "/inventory/receive-delivery");

    const error = page.getByText("Couldn't load the purchase requests.");
    await expect(error).toBeVisible();

    release();
    await page.getByRole("button", { name: "Retry" }).click();
    await expect(error).toBeHidden();
  });

  test("failed delivered totals block the form instead of showing nothing received", async ({ page }) => {
    await openOrSkip(page, "/inventory/receive-delivery");
    await page.getByPlaceholder("Type PR number, division, or status…").click();
    const options = page.locator("ul li[class*='cursor-pointer']");
    test.skip((await options.count()) < 1, "needs at least one open PR in the database");

    const release = await failUntilReleased(page, /\/api\/deliveries\/totals/);
    await options.nth(0).click();

    const error = page.getByText("Couldn't load this PR's items and what was already received.");
    await expect(error).toBeVisible();
    await expect(page.getByRole("columnheader", { name: "Already Received" })).toBeHidden();
    for (const submit of await page.getByRole("button", { name: /Submit Delivery/ }).all()) {
      await expect(submit).toBeDisabled();
    }

    release();
    await page.getByRole("button", { name: "Retry" }).click();
    await expect(error).toBeHidden();
    await expect(page.getByRole("columnheader", { name: "Already Received" })).toBeVisible();
  });
});
