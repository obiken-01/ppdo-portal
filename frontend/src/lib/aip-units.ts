/**
 * AIP amount formatting and the storage/display unit conversion.
 *
 * Extracted verbatim from `aip/detail/page.tsx` (PPDO-64). No behaviour change —
 * the page was the only caller, and the rules below are unchanged from where they lived.
 */

/** An amount as the AIP prints it. Zero and null both render as an em dash, not "0.00". */
export function fmt(n: number | null | undefined): string {
  if (n == null || n === 0) return "—";
  return n.toLocaleString("en-PH", { minimumFractionDigits: 2, maximumFractionDigits: 2 });
}

// ── Units: pesos in storage, ₱000 on the AIP surfaces (V18-35 / PPDO-34, decision P2-a) ───────
//
// AIP amounts are stored in PESOS for every fiscal year. The province's AIP form is denominated
// in thousands and the encoders read and type it that way, so the AIP pages — and only they —
// convert at their edge: divide when drawing a figure, multiply when saving one. The "(in ₱000)"
// column headers stay true, and nothing a user sees or types changed when storage did.
//
// ⚠️ Both directions or neither. Converting only the display leaves every subsequent edit
// dividing the record by a thousand, because the input would post back what it was shown.
//
// ⚠️ That is why these two live in one file and are exported together. Importing one and
// inlining the other is the exact failure the rule above is written against, and it looks
// entirely plausible on screen.
const PESOS_PER_DISPLAY_UNIT = 1000;

/** Pesos as stored → the ₱000 figure the AIP pages show and accept. */
export function toDisplayUnits(pesos: number | null | undefined): number | null {
  return pesos == null ? null : pesos / PESOS_PER_DISPLAY_UNIT;
}

/** A ₱000 figure typed on an AIP page → pesos for storage. */
export function toStorageUnits(displayed: number | null | undefined): number | null {
  return displayed == null ? null : displayed * PESOS_PER_DISPLAY_UNIT;
}
