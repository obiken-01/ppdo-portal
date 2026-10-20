"use client";

/**
 * The submit checklist and the ceiling strip (V18-49 / PPDO-59, V18-46 / PPDO-56).
 *
 * ⚠️ **This is a gate, not a summary.** Over-ceiling encoding is allowed and expected; submit is
 * where it is blocked (DECISION C). There is deliberately **no "submit anyway"** for completeness.
 *
 * ↩️ **The ceiling blocks one hop later since PPDO-146** (`Division_Submit_Spec.md` decision 13).
 * Over the ceiling, the encoder's submit to the department head goes through after a confirm that
 * names the overage. The send to PPDO stays blocked (`canSubmitToPpdo`), and that is still the one
 * place the ceiling is enforced. Do not make the PPDO hop dismissible too.
 *
 * ⚠️ **The remaining figure may be NEGATIVE and is rendered signed.** That is not an error state to
 * tidy away: after PBO cuts a ceiling below what an office has already encoded, the negative is the
 * only signal the office gets (A5-b). Nothing here clamps it.
 */

import { useState } from "react";
import type { AipDivisionStatus, AipReadiness, AipReadinessIssue, AipUnresolvedCounts } from "@/types";
import { fmtThousandsReadout } from "@/lib/aip-units";
import { ceilingStaleCaption } from "@/lib/ceiling-staleness";
import ConfirmDialog from "@/components/ui/ConfirmDialog";
import { useUnresolvedCounts } from "./AipComments";
import AipHistoryButton from "@/components/aip/review/AipHistoryButton";
import { fmtDivisionStamp } from "./AipDivisionParts";
import { getProposalProjects } from "@/lib/investment-proposals";
import type { ProposalProjectOption } from "@/types";
import type { DivisionSummary } from "@/types";
import {
  allocationOverageAdvice, allocationOverageLine, buildAipSubmitConfirmation, divisionAllocationOverages,
  type AipSubmitConfirmation, type DivisionAllocationOverage,
} from "@/lib/aip-submit-confirm";

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
  // PPDO-151 — a division's refusal lines arrive as plain sentences (`AipDivisionStatus.blockers`),
  // already in the words the submit would refuse with, so they share one heading.
  division: "Before this division can submit",
  waiting: "Divisions still working",
  untagged: "Activities with no division",
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
      /**
       * PPDO-151 — in an office that submits by division, what else holds the send besides the
       * checklist: divisions with activities still in Draft, and untagged activities. Without them
       * the button sat disabled over "0 items to fix".
       */
      waitingDivisions?: string[];
      untaggedCount?: number;
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
  | { kind: "locked"; holder: string }
  /**
   * PPDO-151 — an encoder in an office that submits by division. The office-level submit is closed
   * there (`readiness.canSubmit` is false); their own division submits instead, and the strip names
   * it. Also used once it has submitted, so the reader sees whose it now is.
   */
  | { kind: "division"; division: AipDivisionStatus; onSubmit: () => void }
  /**
   * PPDO-151 — the department head (or an Admin) while the divisions are still working. Nothing to
   * submit yet at office level; it names who it is waiting on. T5 adds the per-division panel.
   */
  | { kind: "awaitingDivisions"; waiting: string[] }
  /** PPDO-151 — Staff in a divisioned office with no division of it. Read-only; says whom to ask. */
  | { kind: "noDivision" };

const STAGE_COPY = {
  encoder: {
    heading: "Submit for department review",
    button: "Submit",
    busy: "Submitting…",
    ready: "Submitting hands this office’s whole AIP to the department head in one action.",
  },
  toPpdo: {
    heading: "Send to PPDO",
    button: "Send to PPDO",
    busy: "Sending…",
    // ⚠️ Says what becomes true, not just what the button does. This is the hop after which
    // nobody in the office can edit anything (spec decision 5), and an encoder who discovers
    // that by finding their fields disabled has been told too late.
    ready: "Sending locks this office’s AIP — nobody here can edit it while PPDO has it.",
  },
  resubmit: {
    heading: "Re-submit to PPDO",
    button: "Re-submit to PPDO",
    busy: "Sending…",
    ready: "Re-sending locks this office’s AIP again while PPDO reviews the changes.",
  },
} as const;

export default function AipSubmitChecklist({
  readiness,
  stage,
  submitting,
  history,
  onSelectActivity,
  proposalCheck,
  staleSince = null,
  onRefresh,
  allocation,
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
  /**
   * The office and year whose investment proposals to check before a submit (Ralph, 2026-10-03).
   * Omitted before FY 2028, where there are no proposals.
   */
  proposalCheck?: { fiscalYear: number; officeId: number };
  /**
   * PPDO-113 — ISO time of the last successful readiness read, set only when a later refresh
   * failed. The figures on screen are then that old, and say so. Null while they are current.
   */
  staleSince?: string | null;
  /** Re-reads the readiness; offered beside the stale caption. */
  onRefresh?: () => void;
  /**
   * PPDO-237 — the office's division rows, for the over-allocation warning. `rows` is what the page
   * already holds (the panel line); `refresh` re-reads them at the click, like the proposals.
   * Omitted before FY 2028, where "used" is the WFP ledger rather than the AIP.
   */
  allocation?: { rows: DivisionSummary[]; refresh: () => Promise<DivisionSummary[]> };
}) {
  // ⚠️ Collapsed by default. The button already carries the count, so the summary an encoder
  // needs is visible without the list; expanded, an office with 80 uncosted activities pushed its
  // own tree off the screen behind a wall of issues it had not asked to read yet.
  const [expanded, setExpanded] = useState(false);
  const { ceiling, ceilingWarning } = readiness;

  // ↩️ PPDO-146: which gate applies depends on the hop. The encoder's needs completeness only; the
  // send to PPDO also needs the office within its ceiling. On that hop the ceiling is shown as a
  // blocking item again, first, so the reader sees what is actually stopping them.
  const canSubmit =
    stage.kind === "toPpdo" ? readiness.canSubmitToPpdo
      : stage.kind === "division" ? stage.division.canSubmit
        : readiness.canSubmit;
  // ⚠️ A division's gate is its OWN blockers, never the office's issue list: another division's
  // uncosted activity is not this division's to fix and does not hold its submit (spec §3.2).
  const issues: AipReadinessIssue[] =
    stage.kind === "division"
      ? stage.division.blockers.map((message) => ({ kind: "division", activityId: null, refCode: null, message }))
      : stage.kind === "toPpdo"
        ? [
            ...(stage.waitingDivisions?.length
              ? [{ kind: "waiting", activityId: null, refCode: null, message: `Waiting on: ${stage.waitingDivisions.join(", ")}` }]
              : []),
            ...(stage.untaggedCount
              ? [{
                  kind: "untagged", activityId: null, refCode: null,
                  message: `${stage.untaggedCount} ${stage.untaggedCount === 1 ? "activity has" : "activities have"} no division yet.`,
                }]
              : []),
            ...(ceilingWarning ? [{ kind: "ceiling", activityId: null, refCode: null, message: ceilingWarning }] : []),
            ...readiness.issues,
          ]
        : readiness.issues;
  const divisionSubmitted = stage.kind === "division" && stage.division.status === "Submitted";
  // The encoder-side warning: over the ceiling, but not what stops them. Only where a submit to
  // the department head is still ahead of this reader — its copy says "you can still submit".
  const showCeilingWarning =
    ceilingWarning != null &&
    (stage.kind === "encoder" || stage.kind === "awaitingReviewer" ||
      (stage.kind === "division" && !divisionSubmitted));

  // ── Division over its allocation (PPDO-237) ──
  //
  // ⚠️ A warning, never a gate — on both hops, and it never enters `issues` or touches `canSubmit`.
  // Whose rows count depends on the hop: a division's encoder sees their own division; the
  // department head (and an office-level encoder, whose rows the server already scopes) sees all.
  const allocationScope: number | null | undefined =
    stage.kind === "division" ? (divisionSubmitted ? undefined : stage.division.divisionId)
      : stage.kind === "toPpdo" || stage.kind === "awaitingDivisions" ||
        stage.kind === "encoder" || stage.kind === "awaitingReviewer" ? null
        : undefined;
  const allocationReader: "encoder" | "head" =
    stage.kind === "toPpdo" || stage.kind === "awaitingDivisions" ? "head" : "encoder";
  const allocationOverages =
    allocation && allocationScope !== undefined ? divisionAllocationOverages(allocation.rows, allocationScope) : [];
  // "Everything … checks out" only when nothing at all is being warned about (PPDO-237).
  const warned = showCeilingWarning || allocationOverages.length > 0;

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
  const heading =
    hasAction ? copy.heading
      : stage.kind === "division" ? stage.division.name
        : stage.kind === "awaitingDivisions" ? "Division submissions"
          : "Submit";

  // ── The one confirmation (PPDO-206) ──────────────────────────────────────
  //
  // ↩️ Every submit opens ONE dialog listing every warning that applies — no final investment
  // proposal, over the ceiling (department-head hops only), unresolved comments — with a single
  // confirm button. It used to be up to three dialogs in a row, two of them saying "Submit anyway".
  // The rules live in `lib/aip-submit-confirm.ts`.
  //
  // ⚠️ **Every warning is soft, and must stay soft** (PPDO-72). An office may legitimately send with
  // a comment outstanding or a proposal unfinished; the dialog stops an *accidental* send, it does
  // not enforce that every remark was actioned. Do not turn any of these into a disabled button.
  //
  // ⚠️ **Proposals are read fresh at the click**, not from the page's strip data: a proposal
  // finalized in another tab a minute ago must not be reported missing. The list is the caller's
  // own scope. **A failed read never holds the submit** — the dialog simply omits that warning.
  const unresolved: AipUnresolvedCounts | null = useUnresolvedCounts();
  const [confirmation, setConfirmation] = useState<AipSubmitConfirmation | null>(null);
  const [confirmingReturn, setConfirmingReturn] = useState(false);
  const [checkingProposals, setCheckingProposals] = useState(false);

  async function onActionClick() {
    if (stage.kind !== "encoder" && stage.kind !== "toPpdo" && stage.kind !== "division") return;
    let proposalGaps: ProposalProjectOption[] | null = null;
    if (proposalCheck) {
      setCheckingProposals(true);
      try {
        const projects = await getProposalProjects(proposalCheck.fiscalYear, proposalCheck.officeId);
        proposalGaps = projects.filter((p) => p.status !== "Final");
      } catch {
        // Skip the warning; see above.
      } finally {
        setCheckingProposals(false);
      }
    }
    // Read fresh, like the proposals; a failed read falls back to the rows the panel is showing —
    // stale rather than silent, and it never holds the submit.
    let freshOverages: DivisionAllocationOverage[] | null = null;
    if (allocation && allocationScope !== undefined) {
      const rows = await allocation.refresh().catch(() => allocation.rows);
      freshOverages = divisionAllocationOverages(rows, allocationScope);
    }
    setConfirmation(buildAipSubmitConfirmation({
      hop: stage.kind === "division" ? { kind: "division", divisionName: stage.division.name }
        : stage.kind === "toPpdo" ? { kind: "toPpdo", resubmit: stage.resubmit }
          : { kind: "encoder" },
      ceilingWarning,
      unresolved,
      proposalGaps,
      allocationOverages: freshOverages,
    }));
  }

  function confirmSubmit() {
    if (stage.kind === "encoder" || stage.kind === "toPpdo" || stage.kind === "division") stage.onSubmit();
  }

  return (
    <div className="border border-slate-200 bg-white">
      <div className="flex items-center justify-between border-b border-slate-200 px-4 py-3">
        <div>
          <h2 className="text-sm font-semibold uppercase tracking-wide text-slate-800">
            {heading}
          </h2>
          <p className="mt-0.5 text-xs text-slate-600">
            {stage.kind === "division"
              ? divisionSubmitted
                ? `Submitted ${fmtDivisionStamp(stage.division.submittedAt)}${
                    stage.division.submittedByName ? ` by ${stage.division.submittedByName}` : ""}`
                : `Draft · ${stage.division.activityCount} activit${stage.division.activityCount === 1 ? "y" : "ies"}`
              : `${readiness.activityCount} activit${readiness.activityCount === 1 ? "y" : "ies"} in this office`}
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
          {stage.kind === "encoder" || stage.kind === "toPpdo" ||
            (stage.kind === "division" && !divisionSubmitted) ? (
            <button
              type="button"
              onClick={() => void onActionClick()}
              disabled={!canSubmit || submitting || checkingProposals}
              className="bg-green-700 px-4 py-2 text-sm font-medium text-white transition-colors hover:bg-green-800 disabled:cursor-not-allowed disabled:bg-slate-300"
            >
              {submitting
                ? copy.busy
                : checkingProposals ? "Checking proposals…"
                : stage.kind === "division" ? `Submit ${stage.division.name}` : copy.button}
            </button>
          ) : stage.kind === "division" ? (
            <span className="border border-slate-300 bg-slate-50 px-3 py-1.5 text-xs text-slate-600">
              With your department head
            </span>
          ) : stage.kind === "awaitingDivisions" || stage.kind === "noDivision" ? null : (
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
            hint={ceiling.ceilingSet ? undefined : "PPDO has not set your ceiling — treated as ₱0"} />
          <Figure label="Encoded (MOOE + CO) (in thousand pesos)" value={fmtThousandsReadout(ceiling.encodedBaseRounded)}
            // ⚠️ Keep this next to the figure. It is the only thing explaining why the strip does
            // not reconcile with the tree: DECISION 9 rounds each activity UP to the thousand
            // before summing, so three ₱1,200 activities read 3.60 below and 6.00 here.
            hint="Rounded up to the thousand per activity, PS exempt" />
          <Figure
            label="Remaining (in thousand pesos)"
            value={fmtThousandsReadout(ceiling.remaining)}
            negative={ceiling.remaining < 0}
            hint={ceiling.remaining < 0 ? "Over ceiling — blocks sending to PPDO" : undefined}
          />
        </div>
      )}

      {/* PPDO-217 — one row per other fund finance flagged "Enforce AIP ceiling", in the General
          Fund's shape. The server lists a fund only once this office has encoded on it or has a
          ceiling for it, so an enforced fund the office never uses adds nothing here. */}
      {ceiling?.otherFunds?.map((fund) => (
        <div key={fund.fundingSourceId}
          className="grid grid-cols-1 gap-px border-b border-slate-200 bg-slate-200 sm:grid-cols-3">
          <Figure label={`${fund.code} ceiling (in thousand pesos)`}
            value={fund.ceilingSet ? fmtThousandsReadout(fund.ceiling) : "Not set"}
            hint={fund.ceilingSet ? undefined : "PPDO has not set this ceiling — treated as ₱0"} />
          <Figure label={`${fund.code} encoded (MOOE + CO) (in thousand pesos)`}
            value={fmtThousandsReadout(fund.encodedBaseRounded)}
            hint="Rounded up to the thousand per activity, PS exempt" />
          <Figure
            label={`${fund.code} remaining (in thousand pesos)`}
            value={fmtThousandsReadout(fund.remaining)}
            negative={fund.remaining < 0}
            hint={fund.remaining < 0 ? "Over ceiling — blocks sending to PPDO" : undefined}
          />
        </div>
      ))}

      {/* PPDO-113 — the figures above are from the last good read, not now. Amber caution, the
          token AipActivityNameCounter uses for non-blocking warnings. Never presented as current. */}
      {ceiling && staleSince && (
        <p role="status" className="flex flex-wrap items-center gap-x-3 border-b border-slate-200 px-4 py-2 text-xs text-amber-700">
          <span>{ceilingStaleCaption(staleSince)}</span>
          {onRefresh && (
            <button type="button" onClick={onRefresh} className="font-medium underline">
              Try again
            </button>
          )}
        </p>
      )}

      {/* ── Ceiling warning (PPDO-146) ───────────────────────────────────── */}
      {/* ⚠️ A warning, not an item to fix: it does not stop the submit to the department head, so
          it stays out of the "items to fix" count. It names the consequence, because "over the
          ceiling" alone would read as either harmless or blocking. */}
      {showCeilingWarning && (
        <p className="flex gap-2 border-b border-amber-200 bg-amber-50 px-4 py-3 text-sm text-amber-800">
          <span aria-hidden>⚠️</span>
          <span>
            {ceilingWarning} You can still submit to your department head, but it cannot be sent to
            PPDO until it is within the ceiling.
          </span>
        </p>
      )}

      {/* ── Division allocation warning (PPDO-237) ──────────────────────────── */}
      {/* ⚠️ Styled as the ceiling warning above, never as an item to fix: nothing here stops a
          submit on either hop. One line per division and fund that is over. */}
      {allocationOverages.length > 0 && (
        <div role="status" className="flex gap-2 border-b border-amber-200 bg-amber-50 px-4 py-3 text-sm text-amber-800">
          <span aria-hidden>⚠️</span>
          <div>
            <ul className="space-y-0.5">
              {allocationOverages.map((o) => (
                <li key={`${o.divisionLabel}-${o.fundCode}`}>{allocationOverageLine(o)}</li>
              ))}
            </ul>
            <p className="mt-1">{allocationOverageAdvice(allocationReader)}</p>
          </div>
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
      ) : stage.kind === "noDivision" ? (
        <p role="status" className="flex gap-2 bg-amber-50 px-4 py-3 text-sm text-amber-800">
          <span aria-hidden>⚠️</span>
          <span>You are not assigned to a division. Ask your department head to assign you one.</span>
        </p>
      ) : stage.kind === "awaitingDivisions" ? (
        <p className="px-4 py-3 text-sm text-slate-600">
          {stage.waiting.length > 0 ? (
            <>
              Each division submits its own work to you.{" "}
              <strong className="text-slate-800">Waiting on: {stage.waiting.join(", ")}</strong>.
            </>
          ) : "Every division with activities has submitted."}
        </p>
      ) : divisionSubmitted ? (
        <p role="status" className="flex gap-2 px-4 py-3 text-sm text-slate-600">
          <span aria-hidden>🔒</span>
          <span>Submitted — your department head can return it if changes are needed.</span>
        </p>
      ) : stage.kind === "division" && stage.division.activityCount === 0 ? (
        // ⚠️ Said plainly rather than as a blocker line: an empty division is not something to
        // fix, and "1 item to fix" over "has no activities to submit" reads as an error.
        <p className="px-4 py-3 text-sm text-slate-600">
          No activities yet in {stage.division.name}.
        </p>
      ) : stage.kind === "division" && canSubmit ? (
        <p className="px-4 py-3 text-sm text-slate-600">
          {warned ? "" : `Everything in ${stage.division.name} checks out. `}Submitting hands it to
          your department head, and its activities lock for you until they return it.
        </p>
      ) : canSubmit ? (
        <p className="px-4 py-3 text-sm text-slate-600">
          {stage.kind === "awaitingReviewer"
            // ⚠️ Says what is still possible, not only what is not. The office CAN keep editing
            // here — only sending it on is someone else's to do.
            ? `Your department head has this office’s AIP. You can still make changes; only they can send it on to PPDO.`
            : `${warned ? "" : "Everything checks out. "}${copy.ready}`}
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

      {confirmation && (
        <ConfirmDialog
          title={confirmation.title}
          message={
            <>
              <p>{confirmation.intro}</p>
              {confirmation.warnings.length > 0 && (
                <ul className="mt-2 space-y-1.5">
                  {confirmation.warnings.map((w) => (
                    <li key={w} className="flex gap-2 text-amber-800">
                      <span aria-hidden>⚠️</span>
                      <span>{w}</span>
                    </li>
                  ))}
                </ul>
              )}
            </>
          }
          confirmLabel={confirmation.confirmLabel}
          cancelLabel="Go back"
          variant={confirmation.variant}
          onConfirm={confirmSubmit}
          onClose={() => setConfirmation(null)}
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
