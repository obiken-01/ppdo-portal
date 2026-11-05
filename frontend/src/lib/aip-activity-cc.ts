/**
 * PPDO-290 — the warning shown beside the CC fields when CC adaptation + mitigation is more than
 * the activity total. All three are pesos (see lib/aip-units).
 *
 * ⚠️ A WARNING, never a refusal (Ralph, 2026-11-04): the save still goes through and the API does
 * not check it. A hard rule may come in v1.10 once PPDC or finance confirms it.
 *
 * Null when there is nothing to compare: no total yet (no expenditure lines), or the sum is within it.
 */
export function ccOverTotalWarning(
  ccAdaptation: number | null,
  ccMitigation: number | null,
  total: number | null,
): string | null {
  if (total == null) return null;
  const cc = (ccAdaptation ?? 0) + (ccMitigation ?? 0);
  if (cc <= total) return null;
  const shown = total.toLocaleString("en-PH", { minimumFractionDigits: 2, maximumFractionDigits: 2 });
  return `CC adaptation and mitigation add up to more than the activity total (₱${shown}). Check the amounts.`;
}
