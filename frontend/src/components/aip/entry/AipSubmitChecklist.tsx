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
import type { AipReadiness, AipReadinessIssue, AipUnresolvedCounts } from "@/types";
import { fmtThousandsReadout } from "@/lib/aip-units";
import ConfirmDialog from "@/components/ui/ConfirmDialog";
import { useUnresolvedCounts } from "./AipComments";
import AipHistoryButton from "@/components/aip/review/AipHistoryButton";

/** Issue kinds grouped for display. The slug is switched on, never the message. */
const KIND_LABELS: Record<string, string> = {
  "no-lines": "Not costed",
  "costed-at-zero": "Costing removed",
  "zero-total": "Totals ₱0",
  "missing-fund": "No funding source",
  "missing-esre": "Missing eSRE code",
  // ↩️ "missing-cc-typology" is gone from the server's gate (PPDO-81) — CC typology is optional.
  // The entry is left OUT rather than kept "just in case": an unknown slug already falls back to
  // the issue's own message, so a stale label here would be worse than none.
  ceiling: "Over ceiling",
  empty: "Nothing to submit",
};

/**
 * Which hop this reader is standing at (PPDO-69).
 *
 * ⚠️ **There are two submits, and they are not the same action.** The encoder's hands the office's
 * work to its own department head; the department head's sends it on to PPDO. They have different
 * authorities, different copy, and different consequences — the second one is the last point at
 * which anything is editable. Rendering one button labelled "Submit" for both would make the
 * irreversible step look identical to the reversible one.
 *
 * The page decides which applies, because it already owns the workflow status and the caller's
 * flags; this component owns only the wording.
 */
export type AipSubmitStage =
  | { kind: "encoder"; onSubmit: () => void }
  | {
      kind: "toPpdo";
      resubmit: boolean;
      onSubmit: () => void;
      /**
       * Hands the work back down to the encoders (added 2026-09-14). Supplied only in department
       * review — the one state the server accepts it from — so its absence hides the button.
       */
      onReturnToEncoder?: () => void;
    }
  /**
   * The office still holds the work and can edit it, but **this** reader cannot send it on —
   * they are an encoder and the second hop is the department head's (PPDO-69).
   *
   * ⚠️ Distinct from `locked`, and the distinction is the whole point: telling an encoder in
   * department review that the page "cannot be changed here" would be false since PPDO-70, and
   * they would stop trying to fix the things their department head just asked them to fix.
   */
  | { kind: "awaitingReviewer"; holder: string }
  /**
   * Nobody in the office can edit — the work is with PPDO or already consolidated. `holder` names
   * **who has it**, never a bare "read-only", which tells a reader nothing about how to get it
   * back.
   */
  | { kind: "locked"; holder: string };

const STAGE_COPY = {
  encoder: {
    heading: "Submit for department review",
    button: "Submit",
    busy: "Submitting…",
    ready: "Everything checks out. Submitting hands this office’s whole AIP to the department head in one action.",
  },
  toPpdo: {
    heading: "Send to PPDO",
    button: "Send to PPDO",
    busy: "Sending…",
    // ⚠️ Says what becomes true, not just what the button does. This is the hop after which
    // nobody in the office can edit anything (spec decision 5), and an encoder who discovers
    // that by finding their fields disabled has been told too late.
    ready: "Everything checks out. Sending locks this office’s AIP — nobody here can edit it while PPDO has it.",
  },
  resubmit: {
    heading: "Re-submit to PPDO",
    button: "Re-submit to PPDO",
    busy: "Sending…",
    ready: "Everything checks out. Re-sending locks this office’s AIP again while PPDO reviews the changes.",
  },
} as const;

export default function AipSubmitChecklist({
  readiness,
  stage,
  submitting,
  history,
  onSelectActivity,
}: {
  readiness: AipReadiness;
  stage: AipSubmitStage;
  submitting: boolean;
  /**
   * The office whose History button to offer (PPDO-77). ⚠️ Shown beside the submit button at every
   * stage, locked included — "who has it and how did it get there" matters most once the office
   * can no longer act.
   */
  history?: { aipRecordId: number; officeId: number };
  /**
   * Takes the reader to the activity an issue is about (PPDO-89).
   *
   * ⚠️ Optional, because the checklist is the same component on a page that shows the whole tree.
   * Where it is supplied the issue line becomes a link: the drill-down shows one activity at a
   * time, so "AIP-001-002-003 is not costed" names a row that is not on screen and that no amount
   * of scrolling will reach.
   */
  onSelectActivity?: (activityId: number) => void;
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

  const copy =
    stage.kind === "toPpdo"
      ? (stage.resubmit ? STAGE_COPY.resubmit : STAGE_COPY.toPpdo)
      : STAGE_COPY.encoder;
  // ⚠️ The JSX below re-tests `stage.kind` rather than reusing this, so TypeScript narrows the
  // union in each branch. A boolean alias reads better but discards the narrowing, and then
  // `stage.holder` and `stage.onSubmit` need non-null assertions that would silently survive a
  // future stage being added to the wrong side.
  const hasAction = stage.kind === "encoder" || stage.kind === "toPpdo";

  // ── The unresolved-comment warning on re-submit (PPDO-72) ──────────────────
  //
  // ⚠️ **Soft, and it must stay soft.** An office may legitimately re-submit with a comment
  // outstanding — the reviewer's remark may have been answered on the phone, or overtaken by a
  // change elsewhere. The gate exists to stop an *accidental* re-submit by someone who never
  // expanded a collapsed comment, not to enforce that every remark was actioned. Do not turn this
  // into a disabled button.
  //
  // ⚠️ **Which hops warn is a decision, not an accident of the condition** (PPDO-133):
  //   • Encoder → department head: WARNS. ↩️ Added 2026-09-24. After a department head returns the
  //     work (the return-to-encoder action) or PPDO sends it back, the encoders re-submit past
  //     comments they cannot resolve themselves — exactly the accidental send this exists for. On a
  //     first submit nothing has been commented yet, so the zero-count check keeps it silent.
  //   • Department head → PPDO, first time: SILENT. The only possible unresolved comments are the
  //     department head's own, which they can resolve themselves (spec decision 10).
  //   • Department head → PPDO, re-submit: WARNS (PPDO-72 decision 10).
  // The division → department head hop (PPDO-130) does not exist yet; decide it there.
  const unresolved: AipUnresolvedCounts | null = useUnresolvedCounts();
  const [confirming, setConfirming] = useState(false);
  const [confirmingReturn, setConfirmingReturn] = useState(false);

  const warnsOnThisHop =
    stage.kind === "encoder" || (stage.kind === "toPpdo" && stage.resubmit);
  const needsUnresolvedWarning = warnsOnThisHop && (unresolved?.total ?? 0) > 0;

  function onActionClick() {
    if (needsUnresolvedWarning) setConfirming(true);
    else if (stage.kind === "encoder" || stage.kind === "toPpdo") stage.onSubmit();
  }

  return (
    <div className="border border-slate-200 bg-white">
      <div className="flex items-center justify-between border-b border-slate-200 px-4 py-3">
        <div>
          <h2 className="text-sm font-semibold uppercase tracking-wide text-slate-800">
            {hasAction ? copy.heading : "Submit"}
          </h2>
          <p className="mt-0.5 text-xs text-slate-600">
            {readiness.activityCount} activit{readiness.activityCount === 1 ? "y" : "ies"} in this
            office
          </p>
        </div>

        <div className="flex flex-wrap items-center justify-end gap-2">
          {history && (
            <AipHistoryButton aipRecordId={history.aipRecordId} officeId={history.officeId} tall />
          )}
          {stage.kind === "toPpdo" && stage.onReturnToEncoder && (
            <button
              type="button"
              onClick={() => setConfirmingReturn(true)}
              disabled={submitting}
              className="border border-slate-300 bg-white px-4 py-2 text-sm font-medium text-slate-800 transition-colors hover:bg-slate-50 disabled:cursor-not-allowed disabled:opacity-60"
            >
              Return to encoders
            </button>
          )}
          {stage.kind === "encoder" || stage.kind === "toPpdo" ? (
            <button
              type="button"
              onClick={onActionClick}
              disabled={!canSubmit || submitting}
              className="bg-green-700 px-4 py-2 text-sm font-medium text-white transition-colors hover:bg-green-800 disabled:cursor-not-allowed disabled:bg-slate-300"
            >
              {submitting ? copy.busy : copy.button}
            </button>
          ) : (
            // ⚠️ Names who holds the work rather than just hiding the button. An encoder told only
            // "read-only" has no idea who has their work or how to get it back.
            <span className="border border-slate-300 bg-slate-50 px-3 py-1.5 text-xs text-slate-600">
              With {stage.holder}
            </span>
          )}
        </div>
      </div>

      {/* ── Ceiling strip ─────────────────────────────────────────────────── */}
      {/* ⚠️ Thousands of pesos, like the tree below it. ↩️ These three rendered raw PESOS while every figure
          beneath them rendered thousands, so the strip and the work it describes sat 1,000× apart
          on one screen and neither call site looked wrong. That is why `fmt` is no longer exported
          unit-less — every formatter now names its unit. */}
      {ceiling && (
        <div className="grid grid-cols-1 gap-px border-b border-slate-200 bg-slate-200 sm:grid-cols-3">
          <Figure label="General Fund ceiling (in thousand pesos)"
            value={ceiling.ceilingSet ? fmtThousandsReadout(ceiling.ceiling) : "Not set"}
            // ⚠️ An unset ceiling is ZERO, not unlimited. Saying so here stops an encoder reading
            // a blank as headroom.
            hint={ceiling.ceilingSet ? undefined : "PBO has not set your ceiling — treated as ₱0"} />
          <Figure label="Encoded (MOOE + CO) (in thousand pesos)" value={fmtThousandsReadout(ceiling.encodedBaseRounded)}
            // ⚠️ Keep this next to the figure. It is the only thing explaining why the strip does
            // not reconcile with the tree: DECISION 9 rounds each activity UP to the thousand
            // before summing, so three ₱1,200 activities read 3.60 below and 6.00 here.
            hint="Rounded up to the thousand per activity, PS exempt" />
          <Figure
            label="Remaining (in thousand pesos)"
            value={fmtThousandsReadout(ceiling.remaining)}
            negative={ceiling.remaining < 0}
            hint={ceiling.remaining < 0 ? "Over ceiling — this blocks submit" : undefined}
          />
        </div>
      )}

      {/* ── Issues ───────────────────────────────────────────────────────── */}
      {/* ⚠️ A LOCKED office never shows the issue list, even when there are issues. It read
          "2 items to fix before submitting" while the work sat with PPDO — naming work the office
          cannot do, for a submit it cannot make. The outstanding items are the reviewer's business
          at that point, and the office's only useful information is who has the document.
          Found by live-testing the state, not by review. */}
      {stage.kind === "locked" ? (
        <p className="px-4 py-3 text-sm text-slate-600">
          This office&rsquo;s AIP is with {stage.holder} and cannot be changed here.
        </p>
      ) : canSubmit ? (
        <p className="px-4 py-3 text-sm text-slate-600">
          {stage.kind === "awaitingReviewer"
            // ⚠️ Says what is still possible, not only what is not. The office CAN keep editing
            // here — only sending it on is someone else's to do.
            ? `Your department head has this office’s AIP. You can still make changes; only they can send it on to PPDO.`
            : copy.ready}
        </p>
      ) : (
        <div className="px-4 py-3">
          <button
            type="button"
            onClick={() => setExpanded((v) => !v)}
            className="inline-flex items-center gap-1.5 text-sm font-medium text-amber-800 hover:underline"
          >
            {/* An emoji, per DESIGN_SYSTEM.md §5 — not a lucide import, which would start a second
                icon system. Amber with it, so the line reads as blocking at a glance. */}
            <span aria-hidden>⚠️</span>
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
                        {/* An office-level issue ("nothing to submit", "over ceiling") carries no
                            activity, so it stays plain text — there is no row to go to. */}
                        {onSelectActivity && issue.activityId != null ? (
                          <button
                            type="button"
                            onClick={() => onSelectActivity(issue.activityId!)}
                            className="text-left hover:bg-green-50"
                          >
                            {issue.refCode && (
                              <span className="mr-2 font-mono text-slate-800 underline">{issue.refCode}</span>
                            )}
                            <span className="underline">{issue.message}</span>
                          </button>
                        ) : (
                          <>
                            {issue.refCode && (
                              <span className="mr-2 font-mono text-slate-800">{issue.refCode}</span>
                            )}
                            {issue.message}
                          </>
                        )}
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

      {confirming && stage.kind === "toPpdo" && unresolved && (
        <ConfirmDialog
          title="Re-submit with unresolved comments?"
          message={unresolvedWarning(unresolved, "PPDO")}
          confirmLabel="Re-submit anyway"
          cancelLabel="Go back"
          variant="warning"
          onConfirm={stage.onSubmit}
          onClose={() => setConfirming(false)}
        />
      )}

      {confirming && stage.kind === "encoder" && unresolved && (
        <ConfirmDialog
          title="Submit with unresolved comments?"
          message={unresolvedWarning(unresolved, "your department head")}
          confirmLabel="Submit anyway"
          cancelLabel="Go back"
          variant="warning"
          onConfirm={stage.onSubmit}
          onClose={() => setConfirming(false)}
        />
      )}

      {confirmingReturn && stage.kind === "toPpdo" && stage.onReturnToEncoder && (
        <ConfirmDialog
          title="Return this AIP to the encoders?"
          message="It moves back to Draft. The encoders can keep editing and submit it to you again. Your comments stay on it."
          confirmLabel="Return to encoders"
          cancelLabel="Go back"
          variant="warning"
          onConfirm={stage.onReturnToEncoder}
          onClose={() => setConfirmingReturn(false)}
        />
      )}
    </div>
  );
}

/**
 * The warning sentence, naming **both** counts separately.
 *
 * ⚠️ **Never one merged total.** There are exactly two authoring sides and the reader can resolve
 * neither of them — only the side that wrote a comment may clear it — so a single figure would
 * imply an action they do not have. The split also carries the information that actually changes
 * whether someone re-submits: "2 still open from PPDO" is a different situation from "2 still open
 * from your own department head", and one merged "4 unresolved" hides which.
 *
 * ⚠️ Says the comments **stay** open rather than that they will be lost — nothing is discarded on
 * re-submit, and telling an office otherwise would push them into resolving comments they have no
 * right to resolve.
 */
function unresolvedWarning(
  { fromPpdo, fromDepartmentHead }: AipUnresolvedCounts,
  /** Who receives the work on this hop — named, because "it goes on anyway" means nothing alone. */
  recipient: "PPDO" | "your department head",
): string {
  // ⚠️ The word "unresolved" sits with the FIRST count, not at the end of the list. Appending it
  // ("2 from PPDO and 1 from your department head unresolved") strands the only word that says
  // what the numbers are, and the sentence has to be re-read to parse.
  const parts: string[] = [];
  if (fromPpdo > 0) {
    parts.push(`${fromPpdo} unresolved comment${fromPpdo === 1 ? "" : "s"} from PPDO`);
  }
  if (fromDepartmentHead > 0) {
    parts.push(
      fromPpdo > 0
        ? `${fromDepartmentHead} from your department head`
        : `${fromDepartmentHead} unresolved comment${fromDepartmentHead === 1 ? "" : "s"} from your department head`
    );
  }

  return (
    `This office still has ${parts.join(" and ")}. ` +
    `They stay on the record, and ${recipient} will see them alongside the submitted work. ` +
    "You can send it on anyway."
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
