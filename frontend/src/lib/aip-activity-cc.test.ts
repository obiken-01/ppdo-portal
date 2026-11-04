import { describe, expect, it } from "vitest";
import { ccOverTotalWarning } from "./aip-activity-cc";

// PPDO-290 — CC adaptation + mitigation above the activity total is a warning on the page, never a
// refusal (Ralph, 2026-11-04). A hard rule may come in v1.10 once PPDC or finance confirms it.
describe("ccOverTotalWarning", () => {
  it("warns when adaptation plus mitigation is above the activity total", () => {
    expect(ccOverTotalWarning(6_000, 5_000, 10_000)).toBe(
      "CC adaptation and mitigation add up to more than the activity total (₱10,000.00). Check the amounts.");
  });

  it("counts either amount alone", () => {
    expect(ccOverTotalWarning(10_001, null, 10_000)).not.toBeNull();
    expect(ccOverTotalWarning(null, 10_001, 10_000)).not.toBeNull();
  });

  it("is quiet at or below the total", () => {
    expect(ccOverTotalWarning(5_000, 5_000, 10_000)).toBeNull();
    expect(ccOverTotalWarning(1, 2, 10_000)).toBeNull();
    expect(ccOverTotalWarning(null, null, 10_000)).toBeNull();
  });

  it("is quiet when the activity has no total yet, or no CC amount", () => {
    expect(ccOverTotalWarning(5_000, null, null)).toBeNull();
    expect(ccOverTotalWarning(0, 0, 0)).toBeNull();
  });
});
