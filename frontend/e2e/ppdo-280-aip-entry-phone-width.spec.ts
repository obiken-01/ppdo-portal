import { expect, test } from "./fixtures";
import { expectSignedIn, open, pathname, personaOf } from "./helpers";
import { PORTAL_ROUTES } from "./routes";

// PPDO-280: at phone width (390 px) the AIP Entry page scrolled sideways for some users.
// Wide content may scroll inside its own box; the page itself must not.
//
// The cause was the department head's Divisions table: its "Actions" header is an sr-only span
// (position: absolute), and with no positioned ancestor it escaped the table's scroll box and
// stretched the page. So the check waits for the record and its panels, not just the heading.
//
// Read-only. Personas who may not open the page are skipped.

const PATH = "/budget-planning/aip/entry";
const ROUTE = PORTAL_ROUTES.find((r) => r.path === PATH)!;

const SIZES = [
  { name: "phone (390 px)", width: 390, height: 844 },
  { name: "desktop (1280 px)", width: 1280, height: 800 },
];

test.describe("PPDO-280 AIP Entry width", () => {
  test.beforeEach(({}, info) => {
    const persona = personaOf(info.project.name);
    test.skip(ROUTE.access[persona] !== "allow", `${persona} may not open ${PATH}`);
  });

  for (const size of SIZES) {
    test(`the page does not scroll sideways at ${size.name}`, async ({ page }) => {
      await page.setViewportSize({ width: size.width, height: size.height });
      const record = page.waitForResponse((r) => /\/api\/budget-planning\/aip\/\d+$/.test(new URL(r.url()).pathname), { timeout: 30_000 }).catch(() => null);
      await open(page, PATH);
      await record;
      expectSignedIn(page, PATH);
      expect(pathname(page)).toBe(PATH);
      await expect(page.getByRole("heading", { name: "AIP Entry", level: 1 })).toBeVisible();
      // Loaded: no skeleton left in the page.
      await expect(page.locator("main .animate-pulse")).toHaveCount(0, { timeout: 30_000 });
      await page.waitForLoadState("networkidle").catch(() => {});

      const width = await page.evaluate(() => ({
        scroll: document.documentElement.scrollWidth,
        client: document.documentElement.clientWidth,
      }));
      expect(width.scroll, "the page scrolls sideways (scrollWidth > clientWidth)").toBeLessThanOrEqual(width.client);
    });
  }
});
