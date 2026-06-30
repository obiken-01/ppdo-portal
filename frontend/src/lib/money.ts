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
