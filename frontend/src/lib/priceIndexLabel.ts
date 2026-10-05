import { formatMoney } from "@/lib/money";
import type { PriceIndexPickerItem } from "@/types";

/**
 * How a price index item is named in a picker. The same name + unit can exist several times
 * under different stock card numbers (PGOM lists an item once per stock card), so the stock card
 * no. is part of the label — without it those rows are indistinguishable in the dropdown.
 */
export const priceIndexItemLabel = (p: PriceIndexPickerItem): string =>
  `${p.name} (${p.unit})${p.stockCardNo ? ` [${p.stockCardNo}]` : ""} — ₱${formatMoney(p.unitPrice)}`;

/** Search matches the stock card no. too, so a user can type the code they were given. */
export const priceIndexItemSearchText = (p: PriceIndexPickerItem): string =>
  `${p.name} ${p.unit} ${p.stockCardNo ?? ""}`;
