"use client";

/**
 * "Show History" — one office's AIP hand-off chain, with the comments written along the way
 * (v1.8.0 Phase 4 — V18-77 / PPDO-77, `AIP_Review_Spec.md` §5.2 and §6.2a).
 *
 * ⚠️ **One component for both surfaces** — the PPDO review screen and the office's own AIP Entry —
 * so the two cannot drift in what the history says. Who may read it is the server's call (the same
 * scope as the comments read); nothing here re-derives it.
 *
 * ⚠️ **Read-only.** Resolving stays on the tree and the activity modal, where the row is in front of
 * the reader. A resolve control here would clear a remark about a row nobody is looking at.
 *
 * ⚠️ **Comments are grouped under the hand-off that was in force when they were written, collapsed
 * to a count.** Agreed on the wireframe: a flat list interleaving every comment gets long fast on an
 * office with eighty activities and buries the submit / return chain this exists to show.
 */

import { useCallback, useEffect, useState } from "react";
import Modal from "@/components/ui/Modal";
import { getAipHistory } from "@/lib/aip-comments";
import { aipErrorMessage } from "@/lib/aip";
import { AIP_WORKFLOW } from "@/lib/aip-workflow";
import type {
  AipCommentSide,
  AipHistoryActorSide,
  AipHistoryEntry,
  AipOfficeHistory,
  AipReviewComment,
} from "@/types";

export default function AipHistoryButton({
  aipRecordId,
  officeId,
  tall = false,
}: {
  aipRecordId: number;
  officeId: number;
  /** Match the taller buttons of the AIP Entry submit panel. */
  tall?: boolean;
}) {
  const [open, setOpen] = useState(false);

  return (
    <>
      <button
        type="button"
        onClick={() => setOpen(true)}
        className={`inline-flex items-center gap-1.5 border border-slate-300 bg-white px-3 ${
          tall ? "py-2" : "py-1.5"
        } text-sm font-medium text-slate-800 transition-colors hover:bg-slate-50`}
      >
        <svg
          viewBox="0 0 16 16"
          aria-hidden="true"
          className="h-4 w-4 shrink-0 fill-none stroke-current"
          strokeWidth={1.5}
          strokeLinecap="round"
          strokeLinejoin="round"
        >
          <circle cx="8" cy="8" r="6" />
          <path d="M8 4.5V8l2.25 1.5" />
        </svg>
        History
      </button>

      {open && (
        <AipHistoryModal aipRecordId={aipRecordId} officeId={officeId} onClose={() => setOpen(false)} />
      )}
    </>
  );
}

// ── The modal ───────────────────────────────────────────────────────────────

function AipHistoryModal({
  aipRecordId,
  officeId,
  onClose,
}: {
  aipRecordId: number;
  officeId: number;
  onClose: () => void;
}) {
  const [history, setHistory] = useState<AipOfficeHistory | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);

  const load = useCallback(async () => {
    setLoading(true);
    setError(null);
    try {
      setHistory(await getAipHistory(aipRecordId, officeId));
    } catch (e) {
      setError(aipErrorMessage(e, "The history could not be loaded."));
    } finally {
      setLoading(false);
    }
  }, [aipRecordId, officeId]);

  useEffect(() => { void load(); }, [load]);

  return (
    <Modal
      title="History"
      size="lg"
      onClose={onClose}
      footer={<Modal.SecondaryButton onClick={onClose}>Close</Modal.SecondaryButton>}
    >
      <p className="mb-5 text-xs text-slate-600">Newest first · Manila time</p>

      {loading ? (
        <HistorySkeleton />
      ) : error ? (
        <div className="flex flex-wrap items-center justify-between gap-3 border border-red-200 bg-red-50 px-4 py-3 text-sm text-red-700">
          <span>{error}</span>
          <button
            type="button"
            onClick={() => void load()}
            className="border border-slate-300 bg-white px-3 py-1 text-sm font-medium text-slate-800 hover:bg-slate-50"
          >
            Try again
          </button>
        </div>
      ) : history ? (
        <HistoryBody history={history} />
      ) : null}
    </Modal>
  );
}

function HistoryBody({ history }: { history: AipOfficeHistory }) {
  const { entries, beforeFirstSubmission, workflowStatus } = history;

  return (
    <div>
      {entries.length === 0 && (
        <div className="mb-5 border border-slate-200 bg-slate-50 px-6 py-8 text-center">
          <p className="text-sm font-semibold text-slate-800">No hand-offs yet</p>
          <p className="mx-auto mt-2 max-w-md text-sm text-slate-600">
            {workflowStatus === AIP_WORKFLOW.draft
              ? "This office’s AIP is still a draft. The history starts when it is submitted for department review."
              // ⚠️ Not "still a draft". An office can be past Draft with no recorded hand-off — work
              // that moved before submissions were logged — and saying "draft" would contradict the
              // state chip on the same screen.
              : `This office’s AIP is ${statusLabel(workflowStatus).toLowerCase()}, but no hand-offs were recorded for it.`}
          </p>
        </div>
      )}

      <ol>
        {entries.map((entry, i) => (
          <HandOffItem
            key={entry.id}
            entry={entry}
            isCurrent={i === 0 && entry.toStatus === workflowStatus}
            isLast={i === entries.length - 1 && beforeFirstSubmission.length === 0}
          />
        ))}

        {beforeFirstSubmission.length > 0 && (
          <li className="grid grid-cols-[24px_minmax(0,1fr)] gap-x-3">
            <div className="flex justify-center">
              <span className="mt-1 h-3 w-3 rounded-full border-2 border-slate-300 bg-white" />
            </div>
            <div>
              <p className="text-sm font-semibold text-slate-800">Before first submission</p>
              <CommentGroup comments={beforeFirstSubmission} period="before the first submission" />
            </div>
          </li>
        )}
      </ol>
    </div>
  );
}

function HandOffItem({
  entry,
  isCurrent,
  isLast,
}: {
  entry: AipHistoryEntry;
  isCurrent: boolean;
  isLast: boolean;
}) {
  return (
    <li className="grid grid-cols-[24px_minmax(0,1fr)] gap-x-3">
      <div className="flex flex-col items-center">
        <span className={`mt-1 h-3 w-3 shrink-0 rounded-full ${DOT_CLASS[entry.action]}`} />
        {!isLast && <span className="w-0.5 flex-1 bg-slate-200" />}
      </div>

      <div className={isLast ? "" : "pb-5"}>
        <div className="flex flex-wrap items-baseline justify-between gap-x-3 gap-y-1">
          <span className="text-sm font-semibold text-slate-800">{describeHandOff(entry)}</span>
          <span className="text-xs tabular-nums text-slate-600">{formatWhen(entry.at)}</span>
        </div>

        <div className="mt-0.5 flex flex-wrap items-center gap-1.5 text-sm text-slate-800">
          {entry.actorName}
          <SidePill label={ACTOR_SIDE_LABEL[entry.actorSide]} />
        </div>

        <p className="mt-0.5 flex flex-wrap items-center gap-1.5 text-xs text-slate-600">
          {entry.fromStatus ? `${statusLabel(entry.fromStatus)} → ` : ""}
          {statusLabel(entry.toStatus)}
          {isCurrent && (
            <span className="rounded-full bg-amber-100 px-2 py-0.5 font-medium text-amber-900">
              Where it is now
            </span>
          )}
        </p>

        {entry.comments.length > 0 && (
          <CommentGroup comments={entry.comments} period={PERIOD[entry.toStatus] ?? "in this state"} />
        )}
      </div>
    </li>
  );
}

/**
 * The comments written during one stretch, collapsed to a count.
 *
 * ⚠️ Collapsed by default, like the tree's gutter: the chain is what someone opens History to read,
 * and the conversations open when they ask for them.
 */
function CommentGroup({ comments, period }: { comments: AipReviewComment[]; period: string }) {
  const [open, setOpen] = useState(false);
  const unresolved = comments.filter((c) => c.resolvedAt == null).length;

  return (
    <div className="mt-2">
      <button
        type="button"
        onClick={() => setOpen((v) => !v)}
        aria-expanded={open}
        className="text-xs font-medium text-slate-800 hover:underline"
      >
        {comments.length} comment{comments.length === 1 ? "" : "s"} {period}
        {unresolved > 0 ? ` · ${unresolved} still unresolved` : ""}
        {open ? " ▾" : " ▸"}
      </button>

      {open && (
        <ul className="mt-2 space-y-3 border-l-2 border-slate-200 pl-3">
          {comments.map((c) => (
            <HistoryComment key={c.id} comment={c} />
          ))}
        </ul>
      )}
    </div>
  );
}

function HistoryComment({ comment }: { comment: AipReviewComment }) {
  const resolved = comment.resolvedAt != null;

  return (
    <li className="text-xs">
      <div className="flex flex-wrap items-center gap-1.5">
        <span className="font-medium text-slate-800">{comment.authorName}</span>
        <SidePill label={COMMENT_SIDE_LABEL[comment.authorSide]} />
        {!resolved && (
          <span className="rounded-full bg-amber-100 px-2 py-0.5 text-[11px] font-medium text-amber-900">
            Unresolved
          </span>
        )}
        <span className="ml-auto tabular-nums text-slate-600">{formatWhen(comment.createdAt)}</span>
      </div>

      {/* ⚠️ An orphaned comment is kept and said so — the row it questioned being deleted is often
          the answer to it (spec §5.1). */}
      <p className="mt-0.5 text-slate-600">
        {comment.isOrphaned || comment.nodeRefCode == null
          ? "On a row that has since been removed"
          : <>On <span className="font-mono text-[11px]">{comment.nodeRefCode}</span></>}
      </p>

      <p className="mt-1 whitespace-pre-wrap text-slate-800">{comment.body}</p>

      {resolved && (
        <p className="mt-1 text-slate-600">
          Resolved{comment.resolvedByName ? ` by ${comment.resolvedByName}` : ""} ·{" "}
          {formatWhen(comment.resolvedAt!)}
        </p>
      )}
    </li>
  );
}

function SidePill({ label }: { label: string }) {
  return (
    <span className="rounded-full bg-slate-100 px-1.5 py-0.5 text-[10px] font-semibold uppercase tracking-wide text-slate-600">
      {label}
    </span>
  );
}

/** Shaped like three hand-offs, so the modal does not jump when the chain lands (CLS). */
function HistorySkeleton() {
  return (
    <div className="space-y-6" aria-hidden>
      {[0, 1, 2].map((i) => (
        <div key={i} className="grid grid-cols-[24px_minmax(0,1fr)] gap-x-3">
          <span className="mx-auto mt-1 h-3 w-3 animate-pulse rounded-full bg-slate-200" />
          <div className="space-y-2">
            <div className="flex justify-between gap-3">
              <div className="h-4 w-48 animate-pulse bg-slate-200" />
              <div className="h-3 w-32 animate-pulse bg-slate-200" />
            </div>
            <div className="h-3 w-40 animate-pulse bg-slate-200" />
            <div className="h-3 w-56 animate-pulse bg-slate-200" />
          </div>
        </div>
      ))}
    </div>
  );
}

// ── Wording ─────────────────────────────────────────────────────────────────

/**
 * ⚠️ **A neutral voice**, deliberately none of the three in `aip-workflow.ts`. This modal is read by
 * the office and by PPDO alike, and "with you" or "awaiting your decision" would be false for one of
 * them — the exact bug `describeAipHolderForReviewer` was written to fix.
 */
function statusLabel(status: string): string {
  switch (status) {
    case AIP_WORKFLOW.draft:
      return "Draft";
    case AIP_WORKFLOW.departmentReview:
      return "With the department head";
    case AIP_WORKFLOW.submittedToPpdo:
      return "With PPDO";
    case AIP_WORKFLOW.returnedByPpdo:
      return "Returned to the office";
    case AIP_WORKFLOW.consolidated:
      return "Accepted into the consolidated AIP";
    default:
      return status;
  }
}

function describeHandOff(entry: AipHistoryEntry): string {
  switch (entry.action) {
    case "SUBMIT_DH":
      return "Submitted for department review";
    case "SUBMIT_PPD":
      // ⚠️ Only a known ReturnedByPpdo makes it a re-submit. A null from-status says "Sent", never
      // guesses — the server leaves it null precisely when it could not read the payload.
      return entry.fromStatus === AIP_WORKFLOW.returnedByPpdo ? "Re-submitted to PPDO" : "Sent to PPDO";
    case "RETURN_PPD":
      return "Sent back by PPDO";
    case "ACCEPT_PPD":
      return "Accepted by PPDO";
    case "REOPEN_PPD":
      return "Re-opened and sent back by PPDO";
    case "RETURN_DH":
      return "Returned to the encoders";
  }
}

const DOT_CLASS: Record<AipHistoryEntry["action"], string> = {
  SUBMIT_DH: "border-2 border-green-700 bg-white",
  SUBMIT_PPD: "bg-green-700",
  RETURN_PPD: "bg-amber-500",
  ACCEPT_PPD: "bg-green-700",
  REOPEN_PPD: "bg-amber-500",
  RETURN_DH: "bg-amber-500",
};

/** Completes "N comments …" for the state a hand-off opened. */
const PERIOD: Record<string, string> = {
  [AIP_WORKFLOW.draft]: "while with the encoders",
  [AIP_WORKFLOW.departmentReview]: "while with the department head",
  [AIP_WORKFLOW.submittedToPpdo]: "while with PPDO",
  [AIP_WORKFLOW.returnedByPpdo]: "while with the office",
  [AIP_WORKFLOW.consolidated]: "after it was accepted",
};

const ACTOR_SIDE_LABEL: Record<AipHistoryActorSide, string> = {
  Office: "Office",
  DepartmentHead: "Department head",
  Ppdo: "PPDO",
};

const COMMENT_SIDE_LABEL: Record<AipCommentSide, string> = {
  DepartmentHead: "Department head",
  Ppdo: "PPDO",
};

/**
 * Manila time, always.
 *
 * ⚠️ Appends `Z` when the server's timestamp carries no zone. The values are UTC, and a zone-less ISO
 * string is parsed by `Date` as the *browser's* local time — which on a machine not set to UTC+8
 * would shift every entry by the difference and could reorder a return and a re-submit on screen.
 */
function formatWhen(iso: string): string {
  const zoned = /(?:Z|[+-]\d{2}:\d{2})$/i.test(iso) ? iso : `${iso}Z`;
  return new Date(zoned).toLocaleString("en-PH", {
    year: "numeric",
    month: "short",
    day: "numeric",
    hour: "numeric",
    minute: "2-digit",
    timeZone: "Asia/Manila",
  });
}
