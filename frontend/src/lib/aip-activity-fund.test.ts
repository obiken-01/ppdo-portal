import { describe, expect, it } from "vitest";
import type { FundingSourceResponse } from "@/types";
import { activityFundNames } from "./aip-activity-fund";

const funds = [
  { id: 1, code: "GF", name: "General Fund" },
  { id: 2, code: "TAX", name: "Tax Collection" },
] as FundingSourceResponse[];

describe("activityFundNames (PPDO-218)", () => {
  it("names each fund the activity's lines draw on, code first", () => {
    expect(activityFundNames({ fundCodes: ["GF", "TAX"] }, funds)).toEqual([
      "GF — General Fund", "TAX — Tax Collection",
    ]);
  });

  it("keeps a code it cannot name, rather than dropping the fund", () => {
    expect(activityFundNames({ fundCodes: ["OLD"] }, funds)).toEqual(["OLD"]);
  });

  // FY2027 and earlier: no lines, so the uploaded snapshot is the only answer (activityFundLabel).
  it("falls back to the uploaded snapshot when there are no lines", () => {
    expect(activityFundNames({ fundCodes: [], fundingSourceSnapshot: " 5% CF/ NGA " }, funds)).toEqual(["5% CF/ NGA"]);
  });

  it("is empty when there is no fund at all", () => {
    expect(activityFundNames({ fundCodes: [], fundingSourceSnapshot: null }, funds)).toEqual([]);
  });
});
