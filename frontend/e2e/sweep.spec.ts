import type { Page } from "@playwright/test";
import { expect, test } from "./fixtures";
import { expectSignedIn, marker, open, pathname, personaOf, watch } from "./helpers";
import { PORTAL_ROUTES, type PortalRoute } from "./routes";

// PPDO-272 — automated sweep of UAT Polish Plan §3.2 items 2 (loading state), 4 (error state) and
// 10 (phone width) on every route a persona may open, plus the item-1 extras the smoke run skips
// (no console error at 390 px, no "Application error" after a reload).
// Read-only: it only holds back, blocks or reads GETs. Runs once per configured as-<KEY> project.
//
// A failing page is a bug, not a test to loosen. File it under PPDO-252 and list it in KNOWN below;
// test.fail() keeps the suite green until the fix lands, and then fails loudly ("expected to fail")
// so the entry gets removed.

test.describe.configure({ timeout: 90_000 });

type Check = "loading" | "error" | "phone";

/** "<check> <path>" or "<check> <path> <persona>" → the bug ticket that explains the failure. */
const KNOWN: Record<string, string> = {
  "error /dashboard": "PPDO-273",
  "error /budget-planning/wfp": "PPDO-274",
  "error /budget-planning/wfp/entry": "PPDO-275",
  "error /budget-planning/report": "PPDO-276",
  // AD has a placeholder ("FY …") while loading; RV's picker is pre-filled, so only RV fails.
  "loading /budget-planning/report RV": "PPDO-276",
  "error /budget-planning/aip/review/search": "PPDO-277",
  "error /config": "PPDO-278",
  "error /inventory/create-pr": "PPDO-279",
  // EN does not overflow here, so the entry is per persona.
  "phone /budget-planning/aip/entry AD": "PPDO-280",
  "phone /budget-planning/aip/entry RV": "PPDO-280",
  // PPDO-281: the primary button is enabled while the page's data loads.
  "loading /announcements": "PPDO-281",
  "loading /admin/users": "PPDO-281",
  "loading /config/accounts": "PPDO-281",
  "loading /config/cc-typologies": "PPDO-281",
  "loading /config/divisions": "PPDO-281",
  "loading /config/esre-codes": "PPDO-281",
  "loading /config/funding-sources": "PPDO-281",
  "loading /config/investment-planning": "PPDO-281",
  "loading /config/offices": "PPDO-281",
  "loading /config/price-index": "PPDO-281",
  "loading /config/procurement-presets": "PPDO-281",
  "loading /inventory/create-pr": "PPDO-281",
  "loading /inventory/items-master": "PPDO-281",
};

function knownBug(check: Check, path: string, persona: string): string | undefined {
  return KNOWN[`${check} ${path} ${persona}`] ?? KNOWN[`${check} ${path}`];
}

const PHONE = { width: 390, height: 844 };
const FIFTEEN_SECONDS = 15_000;

// What a page in flight looks like inside <main> (the shell's own spinner is outside it):
// pulsing skeleton blocks, a spinner, or a "Loading…" line.
const LOADING = "main .animate-pulse, main .animate-spin, main [aria-busy='true'], main [role='status']";
const LOADING_TEXT = /loading|please wait/i;
// What a failed load says: the "Couldn't load …" pattern of PPDO-260 and the older inline
// messages ("Failed to …", "Unable to …").
const ERROR_TEXT = /couldn['’]t|could not|can['’]t load|cannot load|failed|unable to|error|try again|went wrong|unavailable/i;

const apiPath = (url: URL) => url.pathname.includes("/api/");

/** Lets matching GETs through only after `release()`; `seen` resolves when the first one arrives. */
async function holdBack(page: Page, pattern: RegExp) {
  let release!: () => void;
  const gate = new Promise<void>((resolve) => (release = resolve));
  let sawIt!: () => void;
  const seen = new Promise<void>((resolve) => (sawIt = resolve));
  await page.route(
    (url) => apiPath(url) && pattern.test(url.pathname),
    async (route) => {
      if (route.request().method() !== "GET") return route.fallback();
      sawIt();
      await gate;
      await route.fallback();
    },
  );
  return { release, seen };
}

/** Fails matching GETs the way a dropped connection does (DevTools → Block request URL). */
async function blockRequests(page: Page, pattern: RegExp) {
  await page.route(
    (url) => apiPath(url) && pattern.test(url.pathname),
    (route) => (route.request().method() === "GET" ? route.abort("failed") : route.fallback()),
  );
}

const main = (page: Page) => page.locator("main").first();

async function spinnerShown(page: Page): Promise<boolean> {
  return (
    (await page.locator(LOADING).first().isVisible()) ||
    (await main(page).getByText(LOADING_TEXT).first().isVisible())
  );
}

// Stat tiles keep their layout and show "—" until the numbers arrive (PPDO-260), and a picker can
// show "FY …" until its options arrive. Both are a fair loading state for the loading check. After
// a failed load the same placeholders need a message beside them.
async function placeholderShown(page: Page): Promise<boolean> {
  if (await main(page).getByText("—", { exact: true }).first().isVisible()) return true;
  const picked = await main(page)
    .locator("select")
    .evaluateAll((selects) => selects.map((s) => (s as HTMLSelectElement).selectedOptions[0]?.textContent?.trim() ?? ""));
  return picked.some((text) => /…$/.test(text));
}

const loadingShown = async (page: Page) => (await spinnerShown(page)) || (await placeholderShown(page));

async function errorShown(page: Page): Promise<boolean> {
  return (
    (await main(page).getByRole("alert").first().isVisible()) ||
    (await main(page).getByText(ERROR_TEXT).first().isVisible())
  );
}

/** Waits up to 15 s for the page to say something went wrong; says what it did instead. */
async function outcomeOfFailedLoad(page: Page): Promise<string> {
  const deadline = Date.now() + FIFTEEN_SECONDS;
  while (Date.now() < deadline) {
    if (pathname(page).startsWith("/reconnecting")) return "ok: sent to the Reconnecting page";
    if (await errorShown(page)) return "ok: shows an error message";
    await page.waitForTimeout(250);
  }
  if (await spinnerShown(page)) return "an endless skeleton: still loading after 15 s, no error message";
  if (await placeholderShown(page)) return "placeholders (— or …) with no error message";
  const text = (await main(page).innerText().catch(() => "")).trim();
  if (text.length < 20) return "a blank main area, no error message";
  return "content with no error message (zeros or empty tables read as real data)";
}

function allowed(route: PortalRoute, persona: string) {
  test.skip(route.access[persona as keyof PortalRoute["access"]] !== "allow", `${persona} may not open ${route.path}`);
}

async function arrive(page: Page, route: PortalRoute) {
  await open(page, route.path);
  expectSignedIn(page, route.path);
  expect(pathname(page), `should be allowed on ${route.path}`).toBe(route.path);
}

test.describe("sweep", () => {
  for (const route of PORTAL_ROUTES) {
    test(`${route.path} · error state`, async ({ page }, info) => {
      const persona = personaOf(info.project.name);
      allowed(route, persona);
      test.skip(!route.mainApi, "the page makes no data call on load");
      const ticket = knownBug("error", route.path, persona);
      if (ticket) test.fail(true, ticket);

      await blockRequests(page, route.mainApi!);
      await page.goto(route.path);

      const outcome = await outcomeOfFailedLoad(page);
      expect(outcome, `with ${route.mainApi} blocked, the page shows`).toMatch(/^ok:/);
    });

    test(`${route.path} · loading state`, async ({ page }, info) => {
      const persona = personaOf(info.project.name);
      allowed(route, persona);
      test.skip(!route.mainApi, "the page makes no data call on load");
      const ticket = knownBug("loading", route.path, persona);
      if (ticket) test.fail(true, ticket);

      const held = await holdBack(page, route.mainApi!);
      await page.goto(route.path, { waitUntil: "domcontentloaded" });
      await Promise.race([
        held.seen,
        page.waitForTimeout(30_000).then(() => {
          throw new Error(`${route.mainApi} was never requested: the mainApi pattern is stale`);
        }),
      ]);

      try {
        await expect
          .poll(() => loadingShown(page), { message: "a skeleton or loading line while the data is held back", timeout: 2_000 })
          .toBe(true);
        if (route.primary) {
          const buttons = main(page).getByRole("button", route.primary);
          for (const button of await buttons.all()) await expect(button).toBeDisabled();
        }
      } finally {
        held.release();
      }
      await page.waitForLoadState("networkidle").catch(() => {});
    });

    test(`${route.path} · phone width`, async ({ page }, info) => {
      const persona = personaOf(info.project.name);
      allowed(route, persona);
      const ticket = knownBug("phone", route.path, persona);
      if (ticket) test.fail(true, ticket);

      await page.setViewportSize(PHONE);
      const problems = watch(page);
      await arrive(page, route);
      await expect(marker(page, route)).toBeVisible();

      const overflow = await page.evaluate(() => ({
        scroll: document.documentElement.scrollWidth,
        inner: window.innerWidth,
      }));
      expect(overflow.scroll, "the page scrolls sideways (scrollWidth > innerWidth)").toBeLessThanOrEqual(overflow.inner);

      // Off-canvas: the sidebar sits entirely left of the screen until the menu button is pressed.
      const sidebar = page.locator("aside").first();
      const closed = await sidebar.boundingBox();
      expect(closed && closed.x + closed.width, "the sidebar should be off-canvas").toBeLessThanOrEqual(0);
      await page.getByRole("button", { name: "Open menu" }).click();
      await expect
        .poll(async () => (await sidebar.boundingBox())?.x ?? -999, { message: "the menu button should open the sidebar" })
        .toBeGreaterThanOrEqual(0);
      await page.reload();
      await page.waitForLoadState("networkidle").catch(() => {});

      await expect(page.getByText("Application error")).toHaveCount(0);
      expect(problems, "errors while loading the page at 390 px").toEqual([]);
    });
  }
});
