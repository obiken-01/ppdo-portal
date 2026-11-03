import { describe, expect, it } from "vitest";
import { monthOrderError } from "./aip-activity-months";

// PPDO-289 — the entry page refuses an end month before the start month, next to the field.
describe("monthOrderError", () => {
  const unchanged = { startDate: null, endDate: null };

  it("refuses an end month before the start month", () => {
    expect(monthOrderError("December", "January", unchanged)).toBe(
      "The end month cannot be before the start month.");
  });

  it("accepts the same month, a forward range, and either month alone", () => {
    expect(monthOrderError("March", "March", unchanged)).toBeNull();
    expect(monthOrderError("January", "December", unchanged)).toBeNull();
    expect(monthOrderError("July", "", unchanged)).toBeNull();
    expect(monthOrderError("", "June", unchanged)).toBeNull();
    expect(monthOrderError("", "", unchanged)).toBeNull();
  });

  it("leaves a reversed pair the activity already holds alone, as the API does", () => {
    expect(monthOrderError("February", "January", { startDate: "February", endDate: "January" })).toBeNull();
  });

  it("checks the order again once either month changes", () => {
    expect(monthOrderError("June", "March", { startDate: "January", endDate: "March" })).not.toBeNull();
  });
});
