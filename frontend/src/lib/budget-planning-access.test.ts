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

// ↩️ PPDO-240: a guest office's initial WFP belongs to its department heads.
const guestEncoder = (o: Partial<MeResponse> = {}) => user({ divisionId: 4, ...o });
const guestHeads: [string, MeResponse][] = [
  ["guest Admin", user({ role: "Admin" })],
  ["guest Staff with CanReviewBudgetPlanning", user({ divisionId: 4, canReviewBudgetPlanning: true })],
  ["guest Staff with CanReviewAllOffices", user({ divisionId: 4, canReviewAllOffices: true })],
];

describe("canOpenWfpReportForYear (PPDO-225)", () => {
  it.each(guestHeads)("offers the WFP for an entered year to a %s", (_, me) => {
    expect(canOpenWfpReportForYear(me, ENTERED)).toBe(true);
  });

  it("does not offer it to a guest-office encoder, with or without a division (PPDO-240)", () => {
    expect(canOpenWfpReportForYear(guestEncoder(), ENTERED)).toBe(false);
    expect(canOpenWfpReportForYear(user(), ENTERED)).toBe(false);
  });

  it("leaves PPDO's own Staff and SuperAdmin as they were", () => {
    expect(canOpenWfpReportForYear(ppdoStaff(), ENTERED)).toBe(true);
    expect(canOpenWfpReportForYear(user({ role: "SuperAdmin", officeId: null }), ENTERED)).toBe(true);
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
  it.each(guestHeads)("opens for a %s", (_, me) => {
    expect(canOpenBudgetPlanningReport(me)).toBe(true);
  });

  it("stays closed for a guest-office encoder: no menu item, and the layout guard redirects (PPDO-240)", () => {
    expect(canOpenBudgetPlanningReport(guestEncoder())).toBe(false);
    expect(canOpenBudgetPlanningReport(user())).toBe(false);
  });

  it("opens for PPDO Staff", () => {
    expect(canOpenBudgetPlanningReport(ppdoStaff())).toBe(true);
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
