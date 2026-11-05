import { expect, test } from "./fixtures";
import { expectSignedIn, open, pathname, personaOf } from "./helpers";
import { PORTAL_ROUTES } from "./routes";

// PPDO-277: on AIP Review search, a failed fiscal-years load used to leave the year picker on a
// fallback year with no message. It must say so and offer Retry, like the PPDO-260 pages.
//
// Read-only. The fiscal-years GET is answered with a 500 (a server error, so the app's
// network-failure path stays out of it) until the test lets it through for Retry.
// Personas who may not open the page are skipped.

const PATH = "/budget-planning/aip/review/search";
const FISCAL_YEARS = /\/api\/budget-planning\/fiscal-years(\?.*)?$/;
const ROUTE = PORTAL_ROUTES.find((r) => r.path === PATH)!;

test.describe("PPDO-277 AIP Review search fiscal years", () => {
  test.beforeEach(({}, info) => {
    const persona = personaOf(info.project.name);
    test.skip(ROUTE.access[persona] !== "allow", `${persona} may not open ${PATH}`);
  });

  test("a failed load shows an error with Retry, and Retry recovers", async ({ page }) => {
    let failing = true;
    await page.route(FISCAL_YEARS, (route) =>
      failing && route.request().method() === "GET"
        ? route.fulfill({ status: 500, body: "E2E: simulated failure" })
        : route.fallback(),
    );
    await open(page, PATH);
    expectSignedIn(page, PATH);
    expect(pathname(page)).toBe(PATH);

    const error = page.getByText("Couldn't load the fiscal years.");
    await expect(error).toBeVisible();
    // The page keeps its layout: the heading and the year picker are still there.
    await expect(page.getByRole("heading", { name: "AIP Review", level: 1 })).toBeVisible();

    failing = false;
    await page.getByRole("alert").filter({ has: error }).getByRole("button", { name: "Retry" }).click();
    await expect(error).toBeHidden();
    await expect(page.locator("main select").first()).toHaveValue(/^20\d\d$/);
  });

  test("a year named in the link does not depend on the load, so no error shows", async ({ page }) => {
    await page.route(FISCAL_YEARS, (route) =>
      route.request().method() === "GET"
        ? route.fulfill({ status: 500, body: "E2E: simulated failure" })
        : route.fallback(),
    );
    await open(page, `${PATH}?fiscalYear=2029`);
    expectSignedIn(page, PATH);

    await expect(page.getByRole("heading", { name: "AIP Review", level: 1 })).toBeVisible();
    await expect(page.locator("main select").first()).toHaveValue("2029");
    await expect(page.getByText("Couldn't load the fiscal years.")).toHaveCount(0);
  });
});
