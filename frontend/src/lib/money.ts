/**
 * Shared money formatting utilities.
 * Single source of truth for display — used by MoneyInput and read-only table cells.
 */

/** Format a number as a Philippine peso amount with thousands commas: "1,234,567.50" */
export function formatMoney(n: number | null | undefined): string {
  if (n == null) return "";
  return n.toLocaleString("en-PH", { minimumFractionDigits: 2, maximumFractionDigits: 2 });
}

/**
 * Abbreviated peso amount for tight spaces — "1.12M", "10M", "850K", "550" (PPDO-78, the readiness
 * board's cards). No currency sign, matching `formatMoney`; the caller prefixes "₱".
 *
 * ⚠️ Display only, and only where the exact figure is one click away — the office table beside the
 * board keeps full pesos. Thresholds sit just below each unit so 999,999 reads "1M", never "1000K".
 */
export function formatMoneyShort(n: number): string {
  const abs = Math.abs(n);
  const sign = n < 0 ? "-" : "";
  if (abs >= 999_950) {
    const m = abs / 1e6;
    return `${sign}${parseFloat(m.toFixed(m >= 10 ? 1 : 2))}M`;
  }
  if (abs >= 999.5) return `${sign}${parseFloat((abs / 1e3).toFixed(1))}K`;
  return `${sign}${Math.round(abs)}`;
}

/**
 * Parse a display string (possibly with thousands commas) back to a number.
 * Returns null when the string is empty or unparseable.
 * Result is rounded to 2 decimal places.
 */
export function parseMoney(str: string): number | null {
  const cleaned = str.replace(/,/g, "").trim();
  if (!cleaned || cleaned === ".") return null;
  const n = parseFloat(cleaned);
  if (isNaN(n)) return null;
  return Math.round(n * 100) / 100;
}
