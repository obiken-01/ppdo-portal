/**
 * AIP amount formatting and the storage/display unit rule.
 *
 * ── The rule ────────────────────────────────────────────────────────────────
 *
 * **Storage is PESOS. Inputs are PESOS. Read-only display is THOUSANDS (₱000).**
 *
 * An encoder types the amount they mean — `1,234,567.89` is stored as `1,234,567.89` — and every
 * read-only figure divides by 1,000 to match the province's form, which is denominated in
 * thousands. The `(in ₱000)` column headers stay true because only *cells* are converted.
 *
 * ↩️ **Inputs used to be in ₱000 too, and that was wrong** (decision P2-a, reversed 2026-09-07).
 * The requirement has always been: *"let them put the value they want whether it's 1,234,567.89
 * and it will be saved as it is, but when displayed in the UI or part of a report, it will be in
 * thousand pesos."* P2-a's symmetric `toDisplayUnits`/`toStorageUnits` pair converted **both**
 * directions, so a value typed as `1,234,567.89` was stored as ₱1.23 **billion**. The archived
 * FY2028 test data still carries one: a line of ₱4,657,655,000 that was typed as `4,657,655`.
 *
 * ⚠️ **`toStorageUnits` is deliberately GONE, not deprecated.** Inputs no longer convert at all —
 * they read and write pesos directly — so any multiplication on the way to the server is the old
 * bug returning. If you find yourself wanting it back, the input you are looking at is showing a
 * divided value and the fix is to stop dividing it, not to multiply it again.
 *
 * ── Rounding ────────────────────────────────────────────────────────────────
 *
 * ⚠️ **Nothing here rounds.** These render the *exact* stored amount in thousands, so an encoder
 * can reconcile a cell against what they typed. DECISION 9's round-**up**-to-the-thousand is a
 * different rule with a narrower scope: it applies to the **ceiling check** (`AipRounding` on the
 * server) and to **Phase 5's printed form**. Settled 2026-09-07.
 *
 * A consequence that is intended and reads like a bug: the tree and the ceiling strip do not
 * reconcile. Three activities at ₱1,200 MOOE show as `1.20 + 1.20 + 1.20 = 3.60` in the tree and
 * `6.00` as "Encoded", because the strip rounds each figure up first. The strip carries a hint
 * saying so — keep it next to the figure.
 *
 * ── Why the formatters name their unit ──────────────────────────────────────
 *
 * ⚠️ **There is no exported unit-less `fmt`, on purpose.** There was, and the ceiling strip called
 * it on raw pesos while the tree below called it on thousands — the two figures on one screen sat
 * 1,000× apart and nothing in either call site looked wrong. Every exported formatter now names
 * the unit it renders, so a call site cannot be silent about which one it means.
 */

/** Pesos in one printed unit. The province's AIP form is denominated in thousands. */
const PESOS_PER_THOUSAND = 1000;

/**
 * The shared number formatter. **Not exported** — see the header: a formatter that does not name
 * its unit is how pesos ended up rendered under a `(in ₱000)` header.
 *
 * Zero and null both render as an em dash, matching the AIP form, which leaves such cells blank.
 */
function format(n: number | null | undefined): string {
  if (n == null || n === 0) return "—";
  return n.toLocaleString("en-PH", { minimumFractionDigits: 2, maximumFractionDigits: 2 });
}

/**
 * Pesos as stored → the exact ₱000 figure, as a number.
 *
 * Prefer {@link fmtThousands} for anything rendered. This exists for the few places that need the
 * value rather than the string — the live hint beside an input, mainly.
 */
export function toDisplayUnits(pesos: number | null | undefined): number | null {
  return pesos == null ? null : pesos / PESOS_PER_THOUSAND;
}

/**
 * A stored peso amount rendered as the AIP form's thousands — the default for every read-only
 * money cell on an AIP surface, under a `(in ₱000)` header.
 *
 * Blank (em dash) for null **and** zero, which is the form's own convention.
 */
export function fmtThousands(pesos: number | null | undefined): string {
  return format(toDisplayUnits(pesos));
}

/**
 * The same figure for a **readout** rather than a form cell — a ceiling, an encoded total, a
 * remaining balance.
 *
 * ⚠️ Differs from {@link fmtThousands} in one way that matters: an exact zero renders as `0.00`,
 * not as a blank. On a grid a blank cell means "nothing here"; on a ceiling readout, "Remaining —"
 * hides the single most important state there is, an office sitting exactly on its ceiling.
 */
export function fmtThousandsReadout(pesos: number | null | undefined): string {
  const v = toDisplayUnits(pesos);
  if (v == null) return "—";
  return v.toLocaleString("en-PH", { minimumFractionDigits: 2, maximumFractionDigits: 2 });
}

/**
 * A peso amount rendered as pesos.
 *
 * ⚠️ Only for figures sitting **beside a ₱ input**, where the surrounding numbers are the pesos
 * the encoder just typed — a running row total mid-edit, chiefly. A saved cell is never pesos;
 * use {@link fmtThousands}.
 */
export function fmtPesos(pesos: number | null | undefined): string {
  return format(pesos);
}

/**
 * The `= 1,234.57 ₱000` echo shown under a peso input, so the two units are never ambiguous at the
 * point of typing — the whole reason a mistyped `4,657,655` could become ₱4.6bn unnoticed.
 *
 * Null when there is nothing worth echoing (empty or zero), so the hint does not flicker under an
 * untouched field.
 */
export function thousandsHint(pesos: number | null | undefined): string | null {
  if (pesos == null || pesos === 0) return null;
  return `= ${fmtThousandsReadout(pesos)} ₱000`;
}
