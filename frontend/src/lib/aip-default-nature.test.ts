import { describe, expect, it } from "vitest";
import type { DefaultNature } from "@/types/config";
import { shouldDefaultToProcurement } from "./aip-default-nature";

const fresh = { isNewLine: true, hasItems: false, hasAmount: false };

describe("shouldDefaultToProcurement (PPDO-209)", () => {
  it("defaults a new, empty line to procurement when the account's default nature is Procurement", () => {
    expect(shouldDefaultToProcurement("Procurement", fresh)).toBe(true);
  });

  it.each<DefaultNature | null | undefined>(["Non-Procurement", "Combined", null, undefined])(
    "leaves amount entry alone for default nature %s (AIP has no combined mode)",
    (nature) => {
      expect(shouldDefaultToProcurement(nature, fresh)).toBe(false);
    }
  );

  it("never flips a saved line's mode", () => {
    expect(shouldDefaultToProcurement("Procurement", { ...fresh, isNewLine: false })).toBe(false);
  });

  it("never replaces items already entered", () => {
    expect(shouldDefaultToProcurement("Procurement", { ...fresh, hasItems: true })).toBe(false);
  });

  it("never discards an amount the encoder already typed", () => {
    expect(shouldDefaultToProcurement("Procurement", { ...fresh, hasAmount: true })).toBe(false);
  });
});
