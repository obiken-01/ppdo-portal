import { AIP_MONTHS } from "@/lib/aipConstants";

/**
 * PPDO-289 — the entry page's own check that the end month is not before the start month, shown
 * next to the End field before anything is sent. Mirrors `AipService.CheckActivityMonths`:
 *
 * ⚠️ A pair the activity already holds is left alone. FY 2027 rows carry reversed pairs, and the
 * API lets them keep saving so a rename is never blocked by a range nobody touched.
 *
 * @param held the months as the activity is saved now, to tell an untouched pair from a new one.
 */
export function monthOrderError(
  start: string,
  end: string,
  held: { startDate: string | null; endDate: string | null },
): string | null {
  if (start === (held.startDate ?? "") && end === (held.endDate ?? "")) return null;
  const from = AIP_MONTHS.indexOf(start);
  const to = AIP_MONTHS.indexOf(end);
  return from >= 0 && to >= 0 && to < from ? "The end month cannot be before the start month." : null;
}
