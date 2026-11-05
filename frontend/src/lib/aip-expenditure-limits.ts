/**
 * Limits on an AIP expenditure line and its procurement items, as the entry page checks them
 * (PPDO-286 / 287 / 293). The server checks the same ones and is the guard; these exist so the
 * encoder sees the problem next to the field instead of after a round trip.
 *
 * ⚠️ Each figure mirrors one on the server. Change one and change the other:
 *   - `AIP_AMOUNT_MAX` ↔ `AipAmountLimit.Max` (`PPDO.Application/Common/AipAmountLimit.cs`)
 *   - `AIP_ITEM_NAME_MAX` / `AIP_ITEM_UNIT_MAX` ↔ `AipProcurementItem.NameMaxLength` / `UnitMaxLength`
 */

import { formatMoney } from "@/lib/money";
import type { SaveAipProcurementItemRequest } from "@/types";

/**
 * ₱999,999,999,999.99 — just under one trillion (Ralph, 2026-11-03). Money columns are
 * `decimal(18,2)`, and past 2^53 centavos the browser starts rounding; this sits well inside both.
 */
export const AIP_AMOUNT_MAX = 999_999_999_999.99;

/** Column limits on a procurement item's name and unit. */
export const AIP_ITEM_NAME_MAX = 300;
export const AIP_ITEM_UNIT_MAX = 50;

/** Same wording as the server's refusal. */
export const AIP_UNIT_REQUIRED = "Unit is required.";

export const aipLineOverMaxMessage = () =>
  `The line total can be at most ₱${formatMoney(AIP_AMOUNT_MAX)}.`;

export const aipItemOverMaxMessage = () =>
  `Over the ₱${formatMoney(AIP_AMOUNT_MAX)} limit.`;

/** True when any item has no unit once trimmed — the server refuses it. */
export function anyItemMissingUnit(items: SaveAipProcurementItemRequest[]): boolean {
  return items.some((i) => !(i.unit ?? "").trim());
}
