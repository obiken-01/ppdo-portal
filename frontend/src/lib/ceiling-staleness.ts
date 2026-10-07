/**
 * PPDO-113 (V18-66) — the caption shown under AIP Entry's ceiling strip when the figures on screen
 * are no longer current (`docs/v1.8/AIP_Offline_Caching_Spec.md` decision 3, §6).
 *
 * After every edit the page re-reads the submit readiness, which carries the ceiling. When that
 * refresh fails the page keeps the last figures, deliberately, so it stays usable. Before this the
 * old figures looked exactly like current ones; a ceiling is live data that moves when a colleague
 * costs an activity, so an unlabelled stale figure can under-warn.
 *
 * ↩️ Built as an in-session label, not the spec's IndexedDB `ceiling-cache` (Ralph, 2026-10-07).
 * The AIP tree is not cached (decision 6), so a load that cannot reach the server fails before any
 * ceiling is shown, and a cross-session ceiling cache would have nothing to feed. The real case is
 * a refresh failing mid-session.
 *
 * ⚠️ Always labelled once a refresh fails, however recent the last good read (Ralph, 2026-10-07):
 * the caption only exists after a failure, so it is never noise.
 *
 * ⚠️ Informational only. The submit gate is server-side (DECISION C) and never reads this.
 */

/** Manila clock time ("9:14 AM"), or null for an unparseable timestamp. */
function manilaTime(iso: string): string | null {
  const parsed = new Date(iso);
  if (Number.isNaN(parsed.getTime())) return null;
  return parsed.toLocaleTimeString("en-US", {
    timeZone: "Asia/Manila",
    hour: "numeric",
    minute: "2-digit",
  });
}

/**
 * @param staleSince ISO time of the last successful read, when a later refresh failed; null while
 *   the figures are current.
 */
export function ceilingStaleCaption(staleSince: string | null): string | null {
  if (staleSince == null) return null;
  const at = manilaTime(staleSince);
  const rest = "These figures could not be refreshed and may be out of date.";
  return at ? `As of ${at}. ${rest}` : rest;
}
