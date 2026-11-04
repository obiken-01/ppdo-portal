import type { Page } from "@playwright/test";
import { expect, test } from "./fixtures";
import { personaOf } from "./helpers";
import { PORTAL_ROUTES } from "./routes";

// PPDO-299: a resource link whose URL is not http(s) (javascript:, data:, a relative path) is
// shown as text, never as an href, on the Resource Links page and in the dashboard widget.
// The API now refuses such URLs; this covers rows saved before that check.
//
// Read-only. GET /api/resource-links is answered by the test, so nothing is saved. Personas
// the route table denies the page to are skipped.

const SAFE = { title: "E2E safe link", url: "https://example.com/ppdo-299" };
const BAD = [
  { title: "E2E javascript link", url: "javascript:alert(document.domain)" },
  { title: "E2E padded upper link", url: "  JAVASCRIPT:alert(1)" },
  { title: "E2E data link", url: "data:text/html,<script>alert(1)</script>" },
  { title: "E2E relative link", url: "/dashboard" },
];

async function stubLinks(page: Page) {
  const links = [SAFE, ...BAD].map((l, i) => ({
    id: `0e2e0299-0000-4000-8000-00000000000${i}`, title: l.title, url: l.url,
    category: "E2E", categoryOrder: 1, linkOrder: i + 1,
    isActive: true, isAdminCreated: false, submittedById: null,
  }));
  await page.route(/\/api\/resource-links$/, (route) =>
    route.request().method() === "GET"
      ? route.fulfill({ json: [{ category: "E2E", categoryOrder: 1, links }] })
      : route.fallback());
}

/** Every href under `scope` must be http(s) or an in-app path the page itself rendered. */
async function expectOnlyWebLinks(page: Page, scope: string) {
  for (const bad of BAD) {
    const row = page.locator(scope).getByText(bad.title, { exact: true });
    await expect(row).toBeVisible();
    await expect(page.locator(scope).locator("a", { hasText: bad.title })).toHaveCount(0);
  }
  await expect(page.locator(scope).locator("a", { hasText: SAFE.title })).toHaveAttribute("href", SAFE.url);
  const hrefs = await page.locator(`${scope} a[href]`).evaluateAll((as) => as.map((a) => a.getAttribute("href") ?? ""));
  for (const href of hrefs) expect(href.trim().toLowerCase()).not.toMatch(/^(javascript|data):/);
}

for (const path of ["/resource-links", "/dashboard"]) {
  test(`PPDO-299 ${path} never renders a non-http resource link as a link`, async ({ page }) => {
    const access = PORTAL_ROUTES.find((r) => r.path === path)?.access[personaOf(test.info().project.name)];
    test.skip(access !== "allow", `persona may not open ${path}`);

    await stubLinks(page);
    await page.goto(path);
    await page.waitForLoadState("networkidle");

    // The list page and the widget each wrap their links in a different container; `main`
    // holds both, and the stubbed rows are the only ones on the page.
    await expectOnlyWebLinks(page, "main");
  });
}
