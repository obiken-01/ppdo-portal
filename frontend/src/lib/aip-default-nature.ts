import type { DefaultNature } from "@/types/config";

/**
 * Whether picking an account should switch an AIP expenditure line to procurement-item entry
 * (PPDO-209) — the AIP twin of the WFP wizard's `Account.DefaultNature` pre-fill.
 *
 * ⚠️ **A default, never a gate.** The encoder can still untick "Cost this line from the Price Index";
 * nothing server-side reads this.
 *
 * ⚠️ **Only an empty, new line.** A saved line's mode is never flipped by opening it or changing its
 * account, and items or amounts already entered are never replaced silently. AIP has no combined
 * mode, so "Combined" and "Non-Procurement" and no default all leave amount entry as it was.
 */
export function shouldDefaultToProcurement(
  defaultNature: DefaultNature | null | undefined,
  line: { isNewLine: boolean; hasItems: boolean; hasAmount: boolean },
): boolean {
  return defaultNature === "Procurement" && line.isNewLine && !line.hasItems && !line.hasAmount;
}
