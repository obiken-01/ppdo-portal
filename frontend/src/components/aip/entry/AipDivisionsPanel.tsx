"use client";

/**
 * The department head's divisions panel on AIP Entry (PPDO-152, `Division_Submit_Spec.md` §6.2).
 *
 * Every division of the office with where it stands, who handed it up and when, and a **Return**
 * on each submitted one. Above the list, the untagged-activities callout: until those are assigned
 * no division can submit (spec §3.2), so it is the first thing a department head needs to act on.
 *
 * ⚠️ **Actions come from the server's per-division flags** (`canReturn`), never from the reader's
 * role here. A plain Admin is refused the return (PPDO-149 deviation 1), and a flag computed there
 * is the only way this panel can agree with that.
 *
 * ⚠️ Shown on AIP Entry only. The spec says "Entry and Review", but AIP Review is the PPDO
 * reviewer's read-only surface; the department head reviews their own office here.
 */

import { useState } from "react";
import type { AipActivityDetail, AipDivisionStatus, AipDivisionStatusList } from "@/types";
import { AIP_WORKFLOW } from "@/lib/aip-workflow";
import ConfirmDialog from "@/components/ui/ConfirmDialog";
import { fmtDivisionStamp } from "./AipDivisionParts";

export default function AipDivisionsPanel({
  list, untagged, busy, onReturn, onSelectActivity,
}: {
  list: AipDivisionStatusList;
  /** The office's untagged activities, from the loaded tree — what the callout's list shows. */
  untagged: AipActivityDetail[];
  busy: boolean;
  onReturn: (division: AipDivisionStatus) => void;
  onSelectActivity: (activityId: number) => void;
}) {
  const [returning, setReturning] = useState<AipDivisionStatus | null>(null);
  const [showUntagged, setShowUntagged] = useState(false);

  const required = list.divisions.filter((d) => d.activityCount > 0);
  const submitted = required.filter((d) => d.status === "Submitted").length;
  const inReview = list.officeWorkflowStatus === AIP_WORKFLOW.departmentReview;
  // The server's count, not `untagged.length`: the tree this page holds is read-scoped, and the
  // count must not shrink because of what the reader happens to see.
  const untaggedCount = list.untaggedActivityCount;

  return (
    <section className="border border-slate-200 bg-white">
      <div className="flex flex-wrap items-center justify-between gap-2 border-b border-slate-200 px-4 py-3">
        <h2 className="text-sm font-semibold uppercase tracking-wide text-slate-800">Divisions</h2>
        {required.length > 0 && (
          <span className="rounded-full bg-slate-100 px-2 py-0.5 text-xs font-medium tabular-nums text-slate-600">
            {submitted} of {required.length} submitted
          </span>
        )}
      </div>

      {untaggedCount > 0 && (
        <div role="status" className="border-b border-amber-200 bg-amber-50 px-4 py-3 text-sm text-amber-800">
          <div className="flex flex-wrap items-center justify-between gap-2">
            <span className="flex gap-2">
              <span aria-hidden>⚠️</span>
              <span>
                {untaggedCount} {untaggedCount === 1 ? "activity has" : "activities have"} no division. No
                division can submit until {untaggedCount === 1 ? "it is" : "they are"} assigned.
              </span>
            </span>
            {untagged.length > 0 && (
              <button
                type="button"
                onClick={() => setShowUntagged((v) => !v)}
                className="text-xs font-medium text-amber-900 underline underline-offset-2"
              >
                {showUntagged ? "Hide them" : "Show them"}
              </button>
            )}
          </div>
          {showUntagged && (
            // ⚠️ A list of links, not a filtered tree: AIP Entry shows one node at a time (PPDO-89),
            // so "filter the tree" means taking the reader to each one, where the division select is.
            <ul className="mt-2 space-y-1">
              {untagged.map((a) => (
                <li key={a.id}>
                  <button
                    type="button"
                    onClick={() => onSelectActivity(a.id)}
                    className="text-left text-xs hover:bg-amber-100"
                  >
                    <span className="mr-2 font-mono text-slate-800 underline">{a.refCode}</span>
                    <span className="underline">{a.name}</span>
                  </button>
                </li>
              ))}
            </ul>
          )}
        </div>
      )}

      <table className="w-full text-sm">
        <thead>
          <tr className="border-b border-slate-200 bg-slate-50 text-left text-xs font-semibold uppercase tracking-wide text-slate-600">
            <th className="px-4 py-2">Division</th>
            <th className="px-4 py-2">Status</th>
            <th className="px-4 py-2 text-right">Activities</th>
            <th className="px-4 py-2">Submitted</th>
            <th className="px-4 py-2"><span className="sr-only">Actions</span></th>
          </tr>
        </thead>
        <tbody>
          {list.divisions.map((d) => {
            const isSubmitted = d.status === "Submitted";
            return (
              <tr key={d.divisionId} className="border-b border-slate-100 last:border-b-0">
                <td className="px-4 py-2 text-slate-800">
                  {d.name}
                  {!d.isActive && <span className="ml-1 text-xs text-slate-600">(inactive)</span>}
                </td>
                <td className="px-4 py-2">
                  <span
                    className={`rounded-full px-2 py-0.5 text-xs font-medium ${
                      isSubmitted ? "bg-green-100 text-green-800"
                        : d.activityCount === 0 ? "bg-slate-100 text-slate-600"
                          : "bg-amber-100 text-amber-800"
                    }`}
                  >
                    {isSubmitted ? "Submitted" : d.activityCount === 0 ? "Nothing to submit" : "Draft"}
                  </span>
                </td>
                <td className="px-4 py-2 text-right tabular-nums text-slate-800">{d.activityCount}</td>
                <td className="px-4 py-2 text-xs text-slate-600">
                  {isSubmitted
                    ? `${fmtDivisionStamp(d.submittedAt)}${d.submittedByName ? ` · ${d.submittedByName}` : ""}`
                    : "—"}
                </td>
                <td className="px-4 py-2 text-right">
                  {/* Hidden, not disabled, for anyone the server says cannot return (spec §6.2). */}
                  {d.canReturn && (
                    <button
                      type="button"
                      disabled={busy}
                      onClick={() => setReturning(d)}
                      className="border border-slate-300 bg-white px-3 py-1 text-xs font-medium text-slate-800 hover:bg-slate-50 disabled:cursor-not-allowed disabled:opacity-60"
                    >
                      Return
                    </button>
                  )}
                </td>
              </tr>
            );
          })}
        </tbody>
      </table>

      {returning && (
        <ConfirmDialog
          title={`Return ${returning.name}?`}
          message={
            `Its encoders can edit again.${inReview ? " The office leaves department review until it submits again." : ""}` +
            " Leave a comment on the activities that need changes — the return itself carries no note."
          }
          confirmLabel={`Return ${returning.name}`}
          cancelLabel="Go back"
          variant="warning"
          onConfirm={() => onReturn(returning)}
          onClose={() => setReturning(null)}
        />
      )}
    </section>
  );
}
