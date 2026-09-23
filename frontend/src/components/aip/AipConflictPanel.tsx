"use client";

import type { AipConflict } from "@/types";

/**
 * Shown when a save is refused because somebody else changed the row first
 * (V18-71 / PPDO-120). Spec: `docs/v1.8/AIP_Concurrent_Edit_Spec.md` §6.
 *
 * ⚠️ **Renders inline, on the row that conflicted — not as a toast and not as a modal.**
 * A toast auto-dismisses, and this one carries the only copy of a decision the user has to make;
 * letting it time out would leave rejected money edits on screen with nothing saying they were
 * rejected, which is worse than the bug this feature closes. A modal covers the values being
 * compared against. Both were considered and both are wrong here.
 *
 * ⚠️ **The caller must keep the user's input in the form while this is open.** The panel offers a
 * choice; it does not own the data. Clearing the form on 409 defeats the entire feature — the work
 * this exists to protect is exactly what would be lost.
 */
export interface AipConflictField {
  label: string;
  /** What the user typed. Pre-formatted — the panel does not know pesos from dates. */
  mine: string;
  /** What the row says now. */
  theirs: string;
}

export interface AipConflictPanelProps {
  conflict: AipConflict;
  /** The thing that changed, for the sentence — "activity", "expenditure line". */
  noun: string;
  /**
   * Only the fields that actually differ. An empty list is legitimate: the other edit may have
   * touched fields this form does not show, and the panel still has to explain the refusal.
   */
  fields: AipConflictField[];
  busy?: boolean;
  /** Resubmit the user's values against `conflict.currentRowVersion`. */
  onOverwrite: () => void;
  /** Drop local edits and reload the row. */
  onDiscard: () => void;
}

/**
 * The API returns UTC; Manila (UTC+8) is what a reader here expects (`CLAUDE.md`).
 * Returns null rather than a placeholder when the timestamp is absent — the sentence reads fine
 * without it, and "changed at Invalid Date" reads like a crash.
 */
function manilaTime(utc: string | null): string | null {
  if (!utc) return null;
  const parsed = new Date(utc);
  if (Number.isNaN(parsed.getTime())) return null;
  return parsed.toLocaleTimeString("en-PH", {
    timeZone: "Asia/Manila",
    hour: "numeric",
    minute: "2-digit",
  });
}

export default function AipConflictPanel({
  conflict,
  noun,
  fields,
  busy = false,
  onOverwrite,
  onDiscard,
}: AipConflictPanelProps) {
  const at = manilaTime(conflict.changedAtUtc);

  // ⚠️ A null name is a real case, not a defect: the row may never have been edited since the
  // guard shipped, or the user may no longer resolve. Naming nobody still beats saying nothing.
  const who = conflict.changedByName ?? "Someone else";
  const when = at ? ` at ${at}` : "";

  return (
    <div
      className="border border-amber-300 bg-amber-50 px-4 py-3"
      role="alert"
      aria-live="assertive"
    >
      <p className="text-sm font-medium text-slate-800">
        {who} changed this {noun}{when}, while you were editing it.
      </p>
      <p className="mt-1 text-xs text-slate-600">
        Nothing was saved. Your changes are still on screen — choose which version to keep.
      </p>

      {fields.length > 0 && (
        <table className="mt-3 w-full text-sm">
          <thead>
            <tr className="text-left text-xs font-medium text-slate-600">
              <th className="py-1 pr-4 font-medium">Field</th>
              <th className="py-1 pr-4 font-medium">Your value</th>
              <th className="py-1 font-medium">Their value</th>
            </tr>
          </thead>
          <tbody>
            {fields.map((f) => (
              <tr key={f.label} className="border-t border-amber-200">
                <td className="py-1 pr-4 text-slate-600">{f.label}</td>
                <td className="py-1 pr-4 text-slate-800">{f.mine}</td>
                <td className="py-1 text-slate-800">{f.theirs}</td>
              </tr>
            ))}
          </tbody>
        </table>
      )}

      <div className="mt-3 flex flex-wrap gap-2">
        <button
          type="button"
          onClick={onOverwrite}
          disabled={busy}
          className="px-3 py-2 bg-danger-500 hover:opacity-90 text-white text-sm font-medium transition-colors disabled:opacity-50 disabled:cursor-not-allowed"
        >
          {busy ? "Saving…" : "Overwrite with mine"}
        </button>
        <button
          type="button"
          onClick={onDiscard}
          disabled={busy}
          className="px-3 py-2 bg-white border border-slate-200 hover:bg-slate-50 text-slate-800 text-sm font-medium transition-colors disabled:opacity-50 disabled:cursor-not-allowed"
        >
          Discard mine and reload
        </button>
      </div>
    </div>
  );
}
