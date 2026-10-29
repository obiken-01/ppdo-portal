import { expect, test } from "./fixtures";

// PPDO-263: for an entered year (FY 2028+) the PPMP type is the initial PPMP, read off the
// consolidated AIP's procurement items. PPDO readers get it, titled "Initial PPMP (from AIP)" with the
// indicative note; a guest office gets 403 from the API, even its department heads (spec decision 8).
//
// Read-only. Runs once per persona project. Which branch runs comes from the user's own /auth/me.
// What a PPDO reader sees depends on the data: no Consolidated office → the empty picker state; an
// office with no items → the "no procurement items" state; otherwise the titled report.

interface Me { isHostOffice: boolean; canAccessBudgetPlanning: boolean }

const PPMP_LABEL = "Project Procurement Management Plan (PPMP)";
const FY = 2028;

test.describe("PPDO-263 initial PPMP", () => {
  test("PPDO readers get the initial PPMP for FY 2028; guest offices get 403", async ({ page }) => {
    const meResponse = page.waitForResponse((r) => /\/api\/auth\/me$/.test(r.url()) && r.ok());
    await page.goto(`/budget-planning/report?type=PPMP&fiscalYear=${FY}`);
    const meRes = await meResponse;
    const body = (await meRes.json()) as Me | { data: Me };
    const me = "data" in body ? body.data : body;
    test.skip(!me.canAccessBudgetPlanning, "persona has no Budget Planning");

    if (!me.isHostOffice) {
      // Guest office: ask the API directly, with the token the page itself just used for /auth/me.
      const authorization = await meRes.request().headerValue("authorization");
      test.skip(authorization == null, "no bearer token seen on /auth/me");
      const apiBase = meRes.url().replace(/\/auth\/me$/, "");
      // The 403 comes before any office lookup, so any id proves it; the user's own office is the case
      // the initial WFP would allow.
      for (const route of ["preview", "export"]) {
        const res = await page.request.get(
          `${apiBase}/budget-planning/ppmp/report/${route}?officeId=1&fiscalYear=${FY}`,
          { headers: { Authorization: authorization! } },
        );
        expect(res.status(), `guest office on ppmp/report/${route} FY ${FY}`).toBe(403);
      }
      return;
    }

    await page.waitForLoadState("networkidle").catch(() => {});
    const typeSelect = page.locator("select").filter({ has: page.locator("option", { hasText: "Work and Financial Plan" }) });
    await expect(typeSelect).toHaveValue("PPMP");

    const officeSelect = page.locator("select").filter({ has: page.locator("option", { hasText: /select office|No office ready yet|Loading offices/ }) });
    await expect(officeSelect.locator("option", { hasText: "Loading offices" })).toHaveCount(0);
    const offices = officeSelect.locator("option:not([value=''])");
    if ((await offices.count()) === 0) {
      await expect(page.getByText(`No office's FY ${FY} AIP is marked as done yet.`)).toBeVisible();
      return;
    }

    await officeSelect.selectOption({ index: 1 });
    await page.getByRole("button", { name: "Generate Preview" }).click();

    await expect(page.getByRole("heading", { name: "Initial PPMP (from AIP)" })).toBeVisible();
    await expect(page.getByText("The working PPMP is prepared in GSO’s PBO-Connect app.", { exact: false })).toBeVisible();
    const noItems = page.getByText(`AIP has no procurement items.`, { exact: false });
    const formTitle = page.getByText("INITIAL PPMP (FROM AIP) — PROJECT PROCUREMENT MANAGEMENT PLAN (PPMP)", { exact: false });
    await expect(noItems.or(formTitle.first())).toBeVisible();
    await expect(typeSelect.locator("option", { hasText: PPMP_LABEL })).toHaveCount(1);
  });
});
