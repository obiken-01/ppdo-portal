import { test as base, type BrowserContext } from "@playwright/test";

// Signed-in specs import `test` and `expect` from here, not from "@playwright/test".
//
// Why: the backend keeps ONE refresh token per user and rotates it on every refresh
// (AuthService.RefreshAsync). Each page load starts with a silent refresh, so the cookie
// saved by auth.setup.ts is dead after the first test that uses it: the next test's refresh
// gets "token_superseded" and lands on /login. So a persona's tests:
//   1. share one browser context per worker, which always holds the latest cookie, and
//   2. run one at a time (`workers: 1` on each as-<KEY> project in playwright.config.ts), and
//   3. write the rotated cookie back to e2e/.auth/<KEY>.json after every test, so a retry or a
//      restarted worker starts from the live token.
//
// Trade-off: Playwright's built-in trace recording doesn't cover this hand-made context.
// Use the HTML report's screenshots, or run with --ui, to debug a failure.

export const test = base.extend<object, { personaContext: BrowserContext; statePath: string }>({
  statePath: [
    async ({}, use, workerInfo) => {
      const state = workerInfo.project.use.storageState;
      if (typeof state !== "string")
        throw new Error(`Project ${workerInfo.project.name} has no storageState file; signed-in specs need one.`);
      await use(state);
    },
    { scope: "worker" },
  ],

  personaContext: [
    async ({ browser, statePath }, use, workerInfo) => {
      const { baseURL, viewport, userAgent, deviceScaleFactor, isMobile, hasTouch } = workerInfo.project.use;
      const context = await browser.newContext({
        baseURL, viewport, userAgent, deviceScaleFactor, isMobile, hasTouch,
        storageState: statePath,
      });
      await use(context);
      await context.close();
    },
    { scope: "worker" },
  ],

  context: async ({ personaContext }, use) => {
    await use(personaContext);
  },

  page: async ({ personaContext, statePath }, use) => {
    const page = await personaContext.newPage();
    await use(page);
    await page.close();
    // Save the cookie this test's refreshes rotated to (point 3 above).
    await personaContext.storageState({ path: statePath });
  },
});

export { expect } from "@playwright/test";
