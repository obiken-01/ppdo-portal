import { describe, expect, it } from "vitest";
import {
  AIP_AMOUNT_MAX,
  AIP_ITEM_NAME_MAX,
  AIP_ITEM_UNIT_MAX,
  aipLineOverMaxMessage,
  anyItemMissingUnit,
} from "./aip-expenditure-limits";
import type { SaveAipProcurementItemRequest } from "@/types";

const item = (unit: string): SaveAipProcurementItemRequest => ({
  periodNo: 1, priceIndexItemId: null, name: "Bond paper", unit, unitPrice: 100, qty: 1, numberOfDays: 1,
});

describe("AIP expenditure limits (PPDO-286/287/293)", () => {
  it("match the server's figures", () => {
    // Mirrors AipAmountLimit.Max and AipProcurementItem's column limits — see the module header.
    expect(AIP_AMOUNT_MAX).toBe(999_999_999_999.99);
    expect(AIP_ITEM_NAME_MAX).toBe(300);
    expect(AIP_ITEM_UNIT_MAX).toBe(50);
  });

  it("stays exact in a JS number, to the centavo", () => {
    expect(Math.round(AIP_AMOUNT_MAX * 100)).toBeLessThan(Number.MAX_SAFE_INTEGER);
    expect(AIP_AMOUNT_MAX.toFixed(2)).toBe("999999999999.99");
  });

  it("names the cap in pesos", () => {
    expect(aipLineOverMaxMessage()).toBe("The line total can be at most ₱999,999,999,999.99.");
  });

  it("flags an item whose unit is blank or spaces", () => {
    expect(anyItemMissingUnit([item("ream")])).toBe(false);
    expect(anyItemMissingUnit([item("ream"), item("")])).toBe(true);
    expect(anyItemMissingUnit([item("   ")])).toBe(true);
    expect(anyItemMissingUnit([])).toBe(false);
  });
});
