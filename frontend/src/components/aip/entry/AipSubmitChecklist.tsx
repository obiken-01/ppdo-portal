"use client";

/**
 * The submit checklist and the ceiling strip (V18-49 / PPDO-59, V18-46 / PPDO-56).
 *
 * ⚠️ **This is a gate, not a summary.** Over-ceiling encoding is allowed and expected; submit is
 * where it is blocked (DECISION C). So there is deliberately **no "submit anyway"** — if this is
 * ever built as a dismissible warning, there is no ceiling enforcement anywhere in the system.
 *
 * ⚠️ **The remaining figure may be NEGATIVE and is rendered signed.** That is not an error state to
 * tidy away: after PBO cuts a ceiling below what an office has already encoded, the negative is the
 * only signal the office gets (A5-b). Nothing here clamps it.
 */

import { useState } from "react";
import type { AipReadiness, AipReadinessIssue } from "@/types";
import { fmt } from "@/lib/aip-units";

/** Issue kinds grouped for display. The slug is switched on, never the message. */
const KIND_LABELS: Record<string, string> = {
  "no-lines": "Not costed",
  "costed-at-zero": "Costing removed",
  "zero-total": "Totals ₱0",
  "missing-fund": "No funding source",
  "missing-esre": "Missing eSRE code",
  "missing-cc-typology": "Missing CC typology",
  ceiling: "Over ceiling",
  empty: "Nothing to submit",
};

export default function AipSubmitChecklist({
  readiness,
  onSubmit,
  submitting,
  readOnlyReason,
}: {
  readiness: AipReadiness;
  onSubmit: () => void;
  submitting: boolean;
  /** Non-null when the office has already been submitted — names the state holding the work. */
  readOnlyReason: string | null;
}) {
  // ⚠️ Collapsed by default. The button already carries the count, so the summary an encoder
  // needs is visible without the list; expanded, an office with 80 uncosted activities pushed its
  // own tree off the screen behind a wall of issues it had not asked to read yet.
  const [expanded, setExpanded] = useState(false);
  const { ceiling, issues, canSubmit } = readiness;

  // Group by kind so an office with 80 uncosted activities shows one heading and a count rather
  // than 80 identical-looking lines.
  const byKind = issues.reduce<Record<string, AipReadinessIssue[]>>((acc, i) => {
    (acc[i.kind] ??= []).push(i);
    return acc;
  }, {});

  return (
    <div className="border border-slate-200 bg-white">
      <div className="flex items-center justify-between border-b border-slate-200 px-4 py-3">
        <div>
          <h2 className="text-sm font-semibold uppercase tracking-wide text-slate-800">
            Submit for department review
          </h2>
          <p className="mt-0.5 text-xs text-slate-600">
            {readiness.activityCount} activit{readiness.activityCount === 1 ? "y" : "ies"} in this
            office
          </p>
        </div>

        {readOnlyReason ? (
          // ⚠️ Names the state rather than just disabling the button. An encoder who is told
          // "read-only" has no idea who holds their work or how to get it back.
          <span className="border border-slate-300 bg-slate-50 px-3 py-1.5 text-xs text-slate-600">
            {readOnlyReason}
          </span>
        ) : (
          <button
            type="button"
            onClick={onSubmit}
            disabled={!canSubmit || submitting}
            className="bg-green-700 px-4 py-2 text-sm font-medium text-white transition-colors hover:bg-green-800 disabled:cursor-not-allowed disabled:bg-slate-300"
          >
            {submitting ? "Submitting…" : "Submit"}
          </button>
        )}
      </div>

      {/* ── Ceiling strip ─────────────────────────────────────────────────── */}
      {ceiling && (
        <div className="grid grid-cols-1 gap-px border-b border-slate-200 bg-slate-200 sm:grid-cols-3">
          <Figure label="General Fund ceiling"
            value={ceiling.ceilingSet ? fmt(ceiling.ceiling) : "Not set"}
            // ⚠️ An unset ceiling is ZERO, not unlimited. Saying so here stops an encoder reading
            // a blank as headroom.
            hint={ceiling.ceilingSet ? undefined : "PBO has not set your ceiling — treated as ₱0"} />
          <Figure label="Encoded (MOOE + CO)" value={fmt(ceiling.encodedBaseRounded)}
            hint="Rounded up to the thousand per activity, PS exempt" />
          <Figure
            label="Remaining"
            value={fmt(ceiling.remaining)}
            negative={ceiling.remaining < 0}
            hint={ceiling.remaining < 0 ? "Over ceiling — this blocks submit" : undefined}
          />
        </div>
      )}

      {/* ── Issues ───────────────────────────────────────────────────────── */}
      {canSubmit ? (
        <p className="px-4 py-3 text-sm text-slate-600">
          Everything checks out. Submitting hands this office&rsquo;s whole AIP to the department
          head in one action.
        </p>
      ) : (
        <div className="px-4 py-3">
          <button
            type="button"
            onClick={() => setExpanded((v) => !v)}
            className="text-sm font-medium text-slate-800 hover:underline"
          >
            {issues.length} item{issues.length === 1 ? "" : "s"} to fix before submitting
            {expanded ? " ▾" : " ▸"}
          </button>

          {expanded && (
            <ul className="mt-3 space-y-3">
              {Object.entries(byKind).map(([kind, group]) => (
                <li key={kind}>
                  <p className="text-xs font-semibold uppercase tracking-wide text-slate-800">
                    {KIND_LABELS[kind] ?? kind} · {group.length}
                  </p>
                  <ul className="mt-1 space-y-1">
                    {group.slice(0, 8).map((issue, i) => (
                      <li key={`${issue.activityId ?? "office"}-${i}`} className="text-xs text-slate-600">
                        {issue.refCode && (
                          <span className="mr-2 font-mono text-slate-800">{issue.refCode}</span>
                        )}
                        {issue.message}
                      </li>
                    ))}
                    {group.length > 8 && (
                      <li className="text-xs text-slate-600">
                        …and {group.length - 8} more.
                      </li>
                    )}
                  </ul>
                </li>
              ))}
            </ul>
          )}
        </div>
      )}
    </div>
  );
}

function Figure({
  label, value, hint, negative = false,
}: {
  label: string;
  value: string;
  hint?: string;
  negative?: boolean;
}) {
  return (
    <div className="bg-white px-4 py-3">
      <p className="text-xs font-medium uppercase tracking-wide text-slate-600">{label}</p>
      <p
        className={`mt-1 text-lg font-semibold tabular-nums ${
          negative ? "text-red-600" : "text-slate-800"
        }`}
      >
        {value}
      </p>
      {hint && <p className="mt-0.5 text-xs text-slate-600">{hint}</p>}
    </div>
  );
}
