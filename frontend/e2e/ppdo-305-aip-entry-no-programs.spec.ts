import { expect, test } from "./fixtures";
import { expectSignedIn, open, personaOf } from "./helpers";
import { PORTAL_ROUTES } from "./routes";

// PPDO-305: AIP Entry said "Could not load division status. Editing is paused until it loads." for
// an office with no programs yet in that year's AIP. The division-status GET answered 404 for an
// office that had simply not started, and the page reads any failure as a failed load. With the
// load failed, the "Nothing here yet" empty state also lost its "+ Add programs" button.
//
// Read-only. Runs on each entry year. A year with no open AIP has no division call and passes
// trivially; a year where the persona's office has no programs is the case the bug was about.

const PATH = "/budget-planning/aip/entry";
const ROUTE = PORTAL_ROUTES.find((r) => r.path === PATH)!;
const DIVISIONS = /\/api\/budget-planning\/aip\/\d+\/offices\/\d+\/divisions$/;
const ENTRY_YEARS = [2028, 2029, 2030];

test.describe("PPDO-305 AIP Entry division status", () => {
  test.beforeEach(({}, info) => {
    const persona = personaOf(info.project.name);
    test.skip(ROUTE.access[persona] !== "allow", `${persona} may not open ${PATH}`);
  });

  for (const fiscalYear of ENTRY_YEARS) {
    test(`FY ${fiscalYear}: division status loads, and an office with no programs can add them`, async ({ page }) => {
      const answers: number[] = [];
      page.on("response", (res) => {
        if (DIVISIONS.test(new URL(res.url()).pathname) && res.request().method() === "GET")
          answers.push(res.status());
      });

      await open(page, `${PATH}?fiscalYear=${fiscalYear}`);
      expectSignedIn(page, PATH);
      await expect(page.getByRole("heading", { name: "AIP Entry", level: 1 })).toBeVisible();

      expect(answers.filter((s) => s >= 400), "division-status GET failed").toEqual([]);
      await expect(page.getByText("Could not load division status.")).toHaveCount(0);

      const empty = page.getByText("Nothing here yet");
      if (await empty.isVisible()) {
        test.info().annotations.push({ type: "PPDO-305", description: `office has no programs in FY ${fiscalYear}` });
        await expect(page.getByRole("button", { name: /Add programs/ })).toBeVisible();
      }
    });
  }
});
