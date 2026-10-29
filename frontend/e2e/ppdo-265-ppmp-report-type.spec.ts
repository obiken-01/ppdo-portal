import { expect, test } from "./fixtures";

// PPDO-265: the PPMP report is back, for PPDO (the host office) only. It was hidden once in
// v1.9.1 (PPDO-236) while a PPDO colleague still used it, so this checks it stays on the
// Report page for PPDO readers and stays closed to guest offices.
//
// Read-only. Runs once per persona project. The rule comes from the user's own /auth/me answer:
// a host-office Budget Planning reader is offered PPMP; a guest-office user is refused by the API
// (403), whether or not the Report page lets them in.

interface Me { isHostOffice: boolean; canAccessBudgetPlanning: boolean }

const PPMP_LABEL = "Project Procurement Management Plan (PPMP)";

test.describe("PPDO-265 PPMP report", () => {
  test("offered to PPDO readers, refused to guest offices", async ({ page }) => {
    const meResponse = page.waitForResponse((r) => /\/api\/auth\/me$/.test(r.url()) && r.ok());
    await page.goto("/budget-planning/report");
    const meRes = await meResponse;
    const body = (await meRes.json()) as Me | { data: Me };
    const me = "data" in body ? body.data : body;
    test.skip(!me.canAccessBudgetPlanning, "persona has no Budget Planning");

    if (me.isHostOffice) {
      await page.waitForLoadState("networkidle").catch(() => {});
      const typeSelect = page.locator("select").filter({ has: page.locator("option", { hasText: "Work and Financial Plan" }) });
      await expect(typeSelect.locator("option", { hasText: PPMP_LABEL })).toHaveCount(1);
      return;
    }

    // Guest office: ask the API directly, with the token the page itself just used for /auth/me.
    const authorization = await meRes.request().headerValue("authorization");
    test.skip(authorization == null, "no bearer token seen on /auth/me");
    const apiBase = meRes.url().replace(/\/auth\/me$/, "");
    for (const route of ["preview", "export"]) {
      const res = await page.request.get(
        `${apiBase}/budget-planning/ppmp/report/${route}?officeId=1&fiscalYear=2027`,
        { headers: { Authorization: authorization! } },
      );
      expect(res.status(), `guest office on ppmp/report/${route}`).toBe(403);
    }
  });
});
