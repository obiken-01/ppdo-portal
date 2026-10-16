import { describe, expect, it } from "vitest";
import type { AipCeilingStatus, FundingSourceResponse } from "@/types";
import {
  ceilingFlagControl, ceilingFlagPayload, ceilingFundTabs, encodedForFund, fundsForOffice, officesForFundTab,
} from "./fund-ceilings";

function fund(id: number, code: string, enforce = false, officeId: number | null = null): FundingSourceResponse {
  return {
    id, code, name: `${code} fund`, description: null, color: null, isActive: true, aliases: null,
    officeId, officeCode: null, officeName: null, isShared: officeId == null, enforceAipCeiling: enforce,
  } as FundingSourceResponse;
}

const GF = fund(1, "GF", true);
const PH = fund(9, "TF-PH", true);
const SEF = fund(4, "SEF");
const OWN = fund(12, "OWN-7", true, 7);

describe("ceilingFundTabs (PPDO-217)", () => {
  it("is the General Fund first, then each enforced fund by code", () => {
    expect(ceilingFundTabs([SEF, PH, OWN, GF]).map((f) => f.code)).toEqual(["GF", "OWN-7", "TF-PH"]);
  });

  // Ralph, 2026-10-14: funds that do not need a ceiling stay off the page.
  it("leaves out every fund that is not enforced", () => {
    expect(ceilingFundTabs([GF, SEF]).map((f) => f.code)).toEqual(["GF"]);
  });

  it("is empty when there is no General Fund and nothing enforced", () => {
    expect(ceilingFundTabs([SEF])).toEqual([]);
  });
});

describe("ceilingFlagPayload", () => {
  // A department head's own-fund edit must never send the flag, or it could trip the 403.
  it("sends nothing when the flag did not change", () => {
    expect(ceilingFlagPayload(true, true)).toEqual({});
    expect(ceilingFlagPayload(false, false)).toEqual({});
  });

  it("sends the new value when it changed", () => {
    expect(ceilingFlagPayload(false, true)).toEqual({ enforceAipCeiling: true });
    expect(ceilingFlagPayload(true, false)).toEqual({ enforceAipCeiling: false });
  });
});

describe("ceilingFlagControl", () => {
  it("locks the General Fund on, for everyone", () => {
    expect(ceilingFlagControl(GF, true)).toEqual({ disabled: true, note: "Always enforced" });
  });

  it("is read-only without the office-ceiling grant, and says why", () => {
    expect(ceilingFlagControl(PH, false)).toEqual({
      disabled: true,
      note: "Only PPDO finance (the office-ceiling grant) can change this.",
    });
  });

  it("is editable for finance on any other fund, new ones included", () => {
    expect(ceilingFlagControl(SEF, true)).toEqual({ disabled: false, note: null });
    expect(ceilingFlagControl(null, true)).toEqual({ disabled: false, note: null });
  });
});

describe("encodedForFund", () => {
  const usage = {
    generalFundId: 1, ceilingSet: true, ceiling: 1_000_000, encodedBaseRounded: 400_000, remaining: 600_000,
    withinCeiling: true,
    otherFunds: [{
      fundingSourceId: 9, code: "TF-PH", name: "PhilHealth", ceilingSet: true, ceiling: 500_000,
      encodedBaseRounded: 450_000, remaining: 50_000, withinCeiling: true,
    }],
  } as AipCeilingStatus;

  it("reads the General Fund's figure for the General Fund tab", () => {
    expect(encodedForFund(usage, 1)).toBe(400_000);
  });

  it("reads the fund's own figure for another fund's tab", () => {
    expect(encodedForFund(usage, 9)).toBe(450_000);
  });

  // The server lists a fund only once the office uses it or has a ceiling: absent means nothing encoded.
  it("is zero for an enforced fund the office has not encoded on", () => {
    expect(encodedForFund(usage, 12)).toBe(0);
  });

  it("is unknown when the office has no AIP to compare against", () => {
    expect(encodedForFund(null, 9)).toBeNull();
  });
});

describe("officesForFundTab", () => {
  const offices = [{ officeId: 3, code: "PTO" }, { officeId: 7, code: "PPDO" }, { officeId: 1, code: "PGO" }];

  it("lists every office for a province-wide fund", () => {
    expect(officesForFundTab(offices, PH).map((o) => o.code)).toEqual(["PTO", "PPDO", "PGO"]);
  });

  // Found live: another office cannot encode on an office's own fund, so its row could only collect
  // a meaningless ceiling.
  it("lists only the owning office for an office-owned fund", () => {
    expect(officesForFundTab(offices, OWN).map((o) => o.code)).toEqual(["PPDO"]);
  });

  it("lists every office while no fund is chosen", () => {
    expect(officesForFundTab(offices, null)).toHaveLength(3);
  });
});

describe("fundsForOffice (card per office)", () => {
  it("is the General Fund, then each province-wide enforced fund and the office's own enforced fund", () => {
    expect(fundsForOffice([SEF, PH, OWN, GF], 7).map((f) => f.code)).toEqual(["GF", "OWN-7", "TF-PH"]);
  });

  it("leaves out another office's own fund, so that office's card can be General Fund only", () => {
    expect(fundsForOffice([GF, SEF, OWN], 3).map((f) => f.code)).toEqual(["GF"]);
  });
});
