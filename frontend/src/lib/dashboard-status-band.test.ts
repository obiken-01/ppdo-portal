import { describe, expect, it } from "vitest";
import { statusBand, type StatusBandInput } from "./dashboard-status-band";

/** A guest-office encoder on FY2028, nothing encoded yet. Each test overrides what it needs. */
function guestOffice(overrides: Partial<StatusBandInput> = {}): StatusBandInput {
  return {
    fiscalYear: 2028,
    firstEnteredYear: 2028,
    officeCode: "PTO",
    isCrossOfficeReviewer: false,
    canManageOfficeCeilings: false,
    isDepartmentHead: false,
    isHost: false,
    canManageAllocation: false,
    loaded: true,
    ceiling: null,
    costedAgainstCeiling: 0,
    hasAip: true,
    activityCount: 0,
    uncostedActivityCount: 0,
    workflowStatus: "Draft",
    workflowStatusSince: null,
    lastHandOff: null,
    divisionsSubmitted: null,
    divisionsRequired: null,
    divisionsWaiting: null,
    unresolvedComments: null,
    officeCeilingAllFunds: null,
    allocatedToDivisions: 0,
    assignedProgramCount: 0,
    unassignedProgramCount: 0,
    pendingForPpdo: null,
    pendingFiscalYear: null,
    officeColumns: null,
    officesWithoutCeiling: null,
    aipEntryHref: "/budget-planning/aip/entry",
    allocationHref: "/budget-planning/allocation",
    reportHref: "/budget-planning/report",
    ...overrides,
  };
}

// PPDO-196 — ceiling authority moved from PBO to PPDO finance in PPDO-87, so no reader may be told
// that PBO sets the ceiling. Waiting states stay generic; "who set it" says PPDO (Ralph, 2026-10-08).
describe("statusBand ceiling copy (PPDO-196)", () => {
  it("tells an office without a ceiling it is waiting for one, without naming PBO", () => {
    const band = statusBand(guestOffice())!;

    expect(band.headline).toBe("Waiting for the FY 2028 ceiling. You can start encoding now.");
    expect(band.reason).toBe("Sending the AIP to PPDO opens once the ceiling is published.");
    expect(band.steps.find((s) => s.key === "ceiling")?.note).toBe("Waiting for ceiling");
  });

  it("credits PPDO once the ceiling is published", () => {
    const band = statusBand(guestOffice({ ceiling: 1_500_000 }))!;

    expect(band.steps.find((s) => s.key === "ceiling")?.note).toBe("Published by PPDO");
  });

  it("never mentions PBO in any ceiling state", () => {
    for (const ceiling of [null, 1_500_000]) {
      const band = statusBand(guestOffice({ ceiling }))!;
      expect(JSON.stringify(band)).not.toMatch(/PBO|Provincial Budget Office/);
    }
  });
});
