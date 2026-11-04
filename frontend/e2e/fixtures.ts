import { test as base, type BrowserContext, type Request } from "@playwright/test";

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
// Tracing still works: a context made from the `browser` fixture is recorded like any other
// (checked with --trace on: page snapshots and screencast frames are in the trace).

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
      const { baseURL, viewport, userAgent, deviceScaleFactor, isMobile, hasTouch, serviceWorkers } = workerInfo.project.use;
      const context = await browser.newContext({
        baseURL, viewport, userAgent, deviceScaleFactor, isMobile, hasTouch, serviceWorkers,
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
    const refreshes = trackRefreshes(personaContext);
    const page = await personaContext.newPage();
    await use(page);
    // PPDO-300: never close the page while a refresh is in flight. The server rotates the token as
    // soon as it answers; if the page is gone before the answer lands, the browser keeps the old
    // cookie, the next line saves it, and every later test for this persona is "token_superseded".
    // Under a loaded full run `networkidle` can settle before the app's silent refresh even starts,
    // so a test can end just as one goes out. Hold back new refreshes, let sent ones land, then close.
    await personaContext.route(REFRESH_URL, (route) => route.abort("aborted"));
    await refreshes.settled();
    await page.close();
    await personaContext.unroute(REFRESH_URL);
    refreshes.stop();
    // Save the cookie this test's refreshes rotated to (point 3 above).
    await personaContext.storageState({ path: statePath });
  },
});

const REFRESH_URL = /\/api\/auth\/refresh$/;

/** Counts /auth/refresh calls that reached the network and have not answered yet. */
function trackRefreshes(context: BrowserContext) {
  let inFlight = 0;
  const isRefresh = (r: Request) => REFRESH_URL.test(r.url());
  const onStart = (r: Request) => { if (isRefresh(r)) inFlight++; };
  const onEnd = (r: Request) => { if (isRefresh(r)) inFlight = Math.max(0, inFlight - 1); };
  context.on("request", onStart);
  context.on("requestfinished", onEnd);
  context.on("requestfailed", onEnd);
  return {
    async settled() {
      // A refresh answers in well under a second locally; 30 s covers a cold host.
      const deadline = Date.now() + 30_000;
      while (inFlight > 0 && Date.now() < deadline) await new Promise((r) => setTimeout(r, 50));
    },
    stop() {
      context.off("request", onStart);
      context.off("requestfinished", onEnd);
      context.off("requestfailed", onEnd);
    },
  };
}

export { expect } from "@playwright/test";
