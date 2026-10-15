import { describe, expect, it } from "vitest";
import { FIRST_ENTERED_FISCAL_YEAR } from "./aip-fiscal-years";
import {
  canOpenBudgetPlanningReport, canOpenWfpReport, canOpenWfpReportForYear, wfpDivisionLocked,
} from "./budget-planning-access";
import type { MeResponse } from "@/types";

// The break year is only ever written in aip-fiscal-years.ts (AipFiscalYearsTests scans frontend/src),
// so every year here is derived from it.
const ENTERED = FIRST_ENTERED_FISCAL_YEAR;
const UPLOADED = FIRST_ENTERED_FISCAL_YEAR - 1;
const PPDO_OFFICE = 1;
const GUEST_OFFICE = 7;

function user(overrides: Partial<MeResponse> = {}): MeResponse {
  return {
    role: "Staff",
    officeId: GUEST_OFFICE,
    divisionId: null,
    isHostOffice: false,
    canAccessBudgetPlanning: true,
    canReviewAllOffices: false,
    canReviewBudgetPlanning: false,
    ...overrides,
  } as MeResponse;
}

const ppdoStaff = (o: Partial<MeResponse> = {}) =>
  user({ officeId: PPDO_OFFICE, isHostOffice: true, divisionId: 2, ...o });

describe("canOpenWfpReportForYear (PPDO-225)", () => {
  it("offers a guest office the WFP for an entered year", () => {
    expect(canOpenWfpReportForYear(user(), ENTERED)).toBe(true);
  });

  it("keeps FY <= 2027 host-office only", () => {
    expect(canOpenWfpReportForYear(user(), UPLOADED)).toBe(false);
    expect(canOpenWfpReportForYear(ppdoStaff(), UPLOADED)).toBe(true);
  });

  it("offers nothing without Budget Planning, in any year", () => {
    const none = user({ canAccessBudgetPlanning: false });
    expect(canOpenWfpReportForYear(none, ENTERED)).toBe(false);
    expect(canOpenWfpReportForYear(none, UPLOADED)).toBe(false);
  });

  it("agrees with canOpenWfpReport for an uploaded year", () => {
    for (const me of [user(), ppdoStaff(), user({ canAccessBudgetPlanning: false })]) {
      expect(canOpenWfpReportForYear(me, UPLOADED)).toBe(canOpenWfpReport(me));
    }
  });
});

describe("canOpenBudgetPlanningReport (page and sidebar share it)", () => {
  it("opens for a guest office with Budget Planning, because the initial WFP is theirs", () => {
    expect(canOpenBudgetPlanningReport(user())).toBe(true);
  });

  it("stays closed without Budget Planning", () => {
    expect(canOpenBudgetPlanningReport(user({ canAccessBudgetPlanning: false }))).toBe(false);
  });
});

describe("wfpDivisionLocked (PPDO-225, spec decision 12)", () => {
  it("keeps the old rule for FY <= 2027", () => {
    expect(wfpDivisionLocked(ppdoStaff(), UPLOADED, PPDO_OFFICE, false)).toBe(true);
    expect(wfpDivisionLocked(ppdoStaff(), UPLOADED, PPDO_OFFICE, true)).toBe(false);
  });

  it("never narrows a PPDO reader on a guest office by their own division", () => {
    expect(wfpDivisionLocked(ppdoStaff(), ENTERED, GUEST_OFFICE, false)).toBe(false);
  });

  it("keeps the host rule on PPDO's own report", () => {
    expect(wfpDivisionLocked(ppdoStaff(), ENTERED, PPDO_OFFICE, false)).toBe(true);
    expect(wfpDivisionLocked(ppdoStaff(), ENTERED, PPDO_OFFICE, true)).toBe(false);
  });

  it("locks guest-office Staff with a division to it", () => {
    expect(wfpDivisionLocked(user({ divisionId: 5 }), ENTERED, GUEST_OFFICE, false)).toBe(true);
  });

  it("frees guest-office Staff with no division, and a guest Admin", () => {
    expect(wfpDivisionLocked(user({ divisionId: null }), ENTERED, GUEST_OFFICE, false)).toBe(false);
    expect(wfpDivisionLocked(user({ role: "Admin", divisionId: 5 }), ENTERED, GUEST_OFFICE, true)).toBe(false);
  });
});
