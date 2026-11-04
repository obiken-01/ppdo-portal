import { expect, test } from "./fixtures";
import { expectSignedIn, pathname } from "./helpers";

// PPDO-300: a persona's tests share one live refresh token (see fixtures.ts). A test that ended
// while the app's silent refresh was in flight closed its page before the rotated cookie came back,
// so the fixture saved the old cookie and every later test for that persona landed on
// /login?reason=token_superseded. In a loaded full run that took out 40+ tests at once.
//
// The first test ends on purpose the moment a refresh is sent; the second must still be signed in.
// Read-only. Runs once per persona project, in order.

test.describe.configure({ mode: "serial" });

test.describe("PPDO-300 session hand-off between tests", () => {
  test("a test that ends just as a refresh goes out", async ({ page }) => {
    const sent = page.waitForRequest(/\/api\/auth\/refresh$/);
    await page.goto("/account", { waitUntil: "commit" });
    await sent;
    // End here: the server has rotated (or is rotating) the token, and its answer is still on the way.
  });

  test("the next test is still signed in", async ({ page }) => {
    await page.goto("/account");
    await expect(page.getByRole("heading", { level: 1, name: "My Account" })).toBeVisible({ timeout: 30_000 });
    expectSignedIn(page, "/account");
    expect(pathname(page)).toBe("/account");
  });
});
