import { expect, test } from "./fixtures";
import { expectSignedIn, marker, open, pathname, personaOf, watch } from "./helpers";
import { PORTAL_ROUTES, REDIRECT_ROUTES } from "./routes";

// PPDO-258 — read-only smoke: each signed-in persona opens every route in routes.ts.
// Per route: no uncaught exception, no 5xx, no console error, no "Application error" screen,
// and the access rule holds (allow → renders with its marker, deny → redirected away).
// Runs once per configured as-<KEY> project; see e2e/README.md for credentials.

test.describe.configure({ timeout: 60_000 }); // a cold `next dev` compiles each route on first visit

test.describe("smoke", () => {
  for (const route of PORTAL_ROUTES) {
    test(route.path, async ({ page }, info) => {
      const expected = route.access[personaOf(info.project.name)];
      const problems = watch(page);

      await open(page, route.path);
      const shown = marker(page, route);

      // A guard redirects only after /auth/me answers, which can be after "networkidle".
      // So wait for one of the two outcomes, then judge it.
      await expect
        .poll(async () => pathname(page) !== route.path || (await shown.isVisible()), { timeout: 20_000 })
        .toBe(true);
      expectSignedIn(page, route.path);

      if (expected === "deny") {
        // The page can draw (marker and all) before the gate's redirect lands; on a cold
        // `next dev` that redirect waits for its target route to compile. So wait for it.
        await expect
          .poll(() => pathname(page), { message: `should be redirected away from ${route.path}`, timeout: 20_000 })
          .not.toBe(route.path);
      } else if (expected === "allow") {
        // Settle once more, so a late redirect isn't mistaken for "allowed".
        await page.waitForLoadState("networkidle").catch(() => {});
        expect(pathname(page), `should be allowed on ${route.path}`).toBe(route.path);
        await expect(shown).toBeVisible();
      }

      await expect(page.getByText("Application error")).toHaveCount(0);
      expect(problems, "errors while loading the page").toEqual([]);
    });
  }

  for (const route of REDIRECT_ROUTES) {
    test(`${route.path} redirects`, async ({ page }) => {
      const problems = watch(page);

      await open(page, route.path);

      await expect.poll(() => pathname(page), { timeout: 20_000 }).not.toBe(route.path);
      expectSignedIn(page, route.path);
      if (route.to) expect(pathname(page)).toMatch(route.to);
      expect(problems, "errors while loading the page").toEqual([]);
    });
  }
});
