import { describe, expect, it } from "vitest";
import { remainingAfterRun } from "./toast-timer";

describe("remainingAfterRun", () => {
  it("subtracts the time the timer ran", () => {
    expect(remainingAfterRun(5000, 1000, 3000)).toBe(3000);
  });

  it("returns the full time when paused instantly", () => {
    expect(remainingAfterRun(5000, 1000, 1000)).toBe(5000);
  });

  it("never goes below zero", () => {
    expect(remainingAfterRun(5000, 1000, 9000)).toBe(0);
  });

  it("works across repeated pauses", () => {
    const afterFirst = remainingAfterRun(5000, 0, 2000);
    expect(remainingAfterRun(afterFirst, 10000, 11500)).toBe(1500);
  });
});
