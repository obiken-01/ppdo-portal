/**
 * Time left on a toast's auto-dismiss timer after it ran for a while.
 * Never negative, so a late pause cannot schedule a timer in the past.
 */
export function remainingAfterRun(
  remainingMs: number,
  startedAtMs: number,
  nowMs: number,
): number {
  return Math.max(0, remainingMs - (nowMs - startedAtMs));
}
