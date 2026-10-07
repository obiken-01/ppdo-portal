"use client";

/**
 * The restore prompt for AIP Entry's local drafts: the activity's details (PPDO-112) and the open
 * expenditure line (PPDO-192). Lifted out of `AipActivityFields` once it had a second caller
 * (`AIP_Expenditure_Draft_Spec.md` §6).
 *
 * Inline, never a toast or a modal: it carries the only copy of the user's unsaved typing. Amber: a
 * caution that blocks nothing. The time is Manila time, as everywhere in the portal.
 */

import type { ReactNode } from "react";

/** "Oct 7, 2026, 9:14 AM" in Manila time. */
export function draftTimeLabel(savedAt: string): string {
  return new Date(savedAt).toLocaleString("en-PH", {
    timeZone: "Asia/Manila", dateStyle: "medium", timeStyle: "short",
  });
}

export default function AipDraftBanner({
  message, note, onRestore, onDiscard, restoreDisabledReason = null, className = "mb-3",
}: {
  /** The question, e.g. "A local draft from … was found for this activity. Restore it?" */
  message: ReactNode;
  /** A second, smaller line, e.g. that the row was saved since the draft was made. */
  note?: ReactNode;
  onRestore: () => void;
  onDiscard: () => void;
  /**
   * Set when Restore cannot work yet (PPDO-192: a new line while + Add Account is withheld). The
   * button is disabled and the reason is said beside it, not hidden in a tooltip. Discard still works.
   */
  restoreDisabledReason?: string | null;
  className?: string;
}) {
  return (
    <div role="status" className={`${className} flex flex-wrap items-center justify-between gap-2 border border-amber-200 bg-amber-50 px-3 py-2 text-sm text-amber-800`}>
      <p>
        {message}
        {note && <span className="block text-xs">{note}</span>}
        {restoreDisabledReason && <span className="block text-xs">{restoreDisabledReason}</span>}
      </p>
      <div className="flex gap-2">
        <button type="button" onClick={onDiscard}
          className="border border-slate-300 bg-white px-3 py-1 text-xs text-slate-600 hover:bg-slate-50">
          Discard
        </button>
        <button type="button" onClick={onRestore} disabled={!!restoreDisabledReason}
          className="bg-green-700 px-3 py-1 text-xs font-medium text-white hover:bg-green-800 disabled:cursor-not-allowed disabled:opacity-50">
          Restore
        </button>
      </div>
    </div>
  );
}
