import { describe, expect, it } from "vitest";
import { formatPesosSigned } from "./money";

describe("formatPesosSigned (PPDO-206)", () => {
  it("puts a true minus before the peso sign", () => {
    expect(formatPesosSigned(-100000)).toBe("\u2212₱100,000.00");
  });

  it("leaves a positive or zero amount unsigned", () => {
    expect(formatPesosSigned(2500.5)).toBe("₱2,500.50");
    expect(formatPesosSigned(0)).toBe("₱0.00");
  });
});
