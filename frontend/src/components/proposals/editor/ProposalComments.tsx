"use client";

/**
 * The comments panel on the proposal editor (v1.9.0 — PPDO-220, spec §6 and "Revised 2026-10-15").
 *
 * Comments are THREADS: a head comment plus replies one level deep, and a proposal can have several.
 * The server returns them flat; `groupThreads` nests replies under their head.
 *
 * ⚠️ **Resolve and Reply are the server's calls.** Resolve shows only where `canResolve` is true (an
 * open head, on the side that started the thread) and is never re-derived here. A resolved thread
 * collapses under "N resolved" and has no Reply box; the server answers 400 to a reply on one.
 * ⚠️ **The composer is shown to every reader**, a Final proposal and a PPDO reviewer included:
 * commenting is a read-scope action, not an edit (spec §3).
 *
 * Modelled on the AIP review comments, but a panel rather than row gutter markers: a proposal's
 * thread is not anchored to a field.
 */

import { useCallback, useEffect, useMemo, useState } from "react";
import {
  COMMENT_MAX, addProposalComment, commentProblem, formatCommentTime, getProposalComments, groupThreads,
  openThreadCount, resolveProposalComment, type CommentThread,
} from "@/lib/proposal-comments";
import { proposalErrorMessage } from "@/lib/investment-proposals";
import type { ProposalComment, ProposalCommentSide } from "@/types";
import { primaryBtn, secondaryBtn } from "./ProposalSectionCard";

export const COMMENTS_PANEL_ID = "proposal-comments";

const SIDE_LABEL: Record<ProposalCommentSide, string> = { Ppdo: "PPDO", Office: "Office" };
const SIDE_CLS: Record<ProposalCommentSide, string> = {
  Ppdo: "bg-info-100 text-slate-800",
  Office: "bg-green-100 text-green-700",
};

function SideTag({ side }: { side: ProposalCommentSide }) {
  return (
    <span className={`inline-flex items-center rounded-full px-2 py-0.5 text-xs font-medium whitespace-nowrap ${SIDE_CLS[side]}`}>
      {SIDE_LABEL[side]}
    </span>
  );
}

function Composer({
  label, placeholder, submitLabel, busy, error, onSubmit, onCancel, autoFocus,
}: {
  label: string;
  placeholder: string;
  submitLabel: string;
  busy: boolean;
  error: string | null;
  onSubmit: (text: string) => Promise<boolean>;
  onCancel?: () => void;
  autoFocus?: boolean;
}) {
  const [text, setText] = useState("");
  const problem = commentProblem(text);
  const id = `${label.replace(/\s+/g, "-").toLowerCase()}-box`;

  async function submit() {
    if (problem || busy) return;
    if (await onSubmit(text.trim())) setText("");
  }

  return (
    <div className="space-y-1">
      <label htmlFor={id} className="sr-only">{label}</label>
      <textarea
        id={id}
        value={text}
        onChange={(e) => setText(e.target.value)}
        placeholder={placeholder}
        rows={3}
        autoFocus={autoFocus}
        disabled={busy}
        className="w-full border border-slate-300 bg-white px-3 py-2 text-sm text-slate-800 placeholder:text-slate-500 focus:outline-none focus:ring-1 focus:ring-green-600 disabled:bg-slate-50"
      />
      <div className="flex flex-wrap items-center justify-between gap-2">
        <span className={`text-xs tabular-nums ${text.length > COMMENT_MAX ? "text-danger-500" : "text-slate-600"}`}>
          {text.length}/{COMMENT_MAX}
        </span>
        <div className="flex items-center gap-2">
          {onCancel && <button type="button" onClick={onCancel} disabled={busy} className={secondaryBtn}>Cancel</button>}
          <button type="button" onClick={() => void submit()} disabled={busy || problem !== null || text.trim().length === 0} className={primaryBtn}>
            {busy ? "Sending…" : submitLabel}
          </button>
        </div>
      </div>
      {error && <p role="alert" className="text-xs text-danger-500">{error}</p>}
    </div>
  );
}

function CommentBody({ c }: { c: ProposalComment }) {
  return (
    <div>
      <div className="flex flex-wrap items-center gap-x-2 gap-y-0.5">
        <SideTag side={c.side} />
        <span className="text-sm font-medium text-slate-800">{c.authorName}</span>
        <span className="text-xs text-slate-600">{formatCommentTime(c.createdAt)}</span>
      </div>
      <p className="mt-1 whitespace-pre-wrap break-words text-sm text-slate-600">{c.body}</p>
    </div>
  );
}

function Thread({
  thread, replying, busy, replyError, resolveError,
  onResolve, onStartReply, onCancelReply, onReply,
}: {
  thread: CommentThread;
  replying: boolean;
  busy: boolean;
  replyError: string | null;
  resolveError: string | null;
  onResolve: () => void;
  onStartReply: () => void;
  onCancelReply: () => void;
  onReply: (text: string) => Promise<boolean>;
}) {
  const { head, replies } = thread;
  const resolved = head.resolvedAt != null;

  return (
    <li className="border border-slate-200 bg-white p-3" data-thread={head.id}>
      <CommentBody c={head} />
      {replies.length > 0 && (
        <ul className="mt-2 space-y-2 border-l-2 border-slate-200 pl-3">
          {replies.map((r) => <li key={r.id}><CommentBody c={r} /></li>)}
        </ul>
      )}

      {resolved ? (
        <p className="mt-2 text-xs text-slate-600">
          Resolved{head.resolvedByName ? ` by ${head.resolvedByName}` : ""}{head.resolvedAt ? ` · ${formatCommentTime(head.resolvedAt)}` : ""}
        </p>
      ) : (
        <div className="mt-2">
          {!replying && (
            <div className="flex flex-wrap items-center gap-3">
              <button type="button" onClick={onStartReply} className="text-xs font-medium text-green-700 hover:underline">Reply</button>
              {head.canResolve && (
                <button type="button" onClick={onResolve} disabled={busy} className="text-xs font-medium text-green-700 hover:underline disabled:text-slate-400">
                  Resolve
                </button>
              )}
            </div>
          )}
          {resolveError && <p role="alert" className="mt-1 text-xs text-danger-500">{resolveError}</p>}
          {replying && (
            <Composer
              label={`Reply to ${head.authorName}`} placeholder="Write a reply…" submitLabel="Reply"
              busy={busy} error={replyError} onSubmit={onReply} onCancel={onCancelReply} autoFocus
            />
          )}
        </div>
      )}
    </li>
  );
}

function PanelSkeleton() {
  return (
    <div className="space-y-2" aria-hidden="true">
      {[0, 1].map((i) => (
        <div key={i} className="border border-slate-200 bg-white p-3">
          <div className="h-4 w-1/2 animate-pulse bg-slate-100" />
          <div className="mt-2 h-4 w-full animate-pulse bg-slate-100" />
          <div className="mt-1 h-4 w-3/4 animate-pulse bg-slate-100" />
        </div>
      ))}
    </div>
  );
}

export default function ProposalComments({
  proposalId, onOpenCount, reloadSignal,
}: {
  proposalId: number;
  /** Open threads, whenever the list changes: the editor shows "N open comments." under Finalize. */
  onOpenCount: (open: number) => void;
  /** Bumped by the editor to re-read the thread, e.g. after Finalize was refused for open comments. */
  reloadSignal: number;
}) {
  const [comments, setComments] = useState<ProposalComment[] | null>(null);
  const [loadError, setLoadError] = useState(false);
  const [busy, setBusy] = useState(false);
  const [replyingTo, setReplyingTo] = useState<number | null>(null);
  const [showResolved, setShowResolved] = useState(false);
  const [newError, setNewError] = useState<string | null>(null);
  const [replyError, setReplyError] = useState<string | null>(null);
  const [resolveError, setResolveError] = useState<{ id: number; message: string } | null>(null);

  const load = useCallback(async () => {
    setLoadError(false);
    try {
      setComments(await getProposalComments(proposalId));
    } catch {
      setLoadError(true);
    }
  }, [proposalId]);

  useEffect(() => { void load(); }, [load, reloadSignal]);

  useEffect(() => {
    if (comments) onOpenCount(openThreadCount(comments));
  }, [comments, onOpenCount]);

  const grouped = useMemo(() => groupThreads(comments ?? []), [comments]);

  async function post(text: string, parentCommentId?: number): Promise<boolean> {
    const setErr = parentCommentId == null ? setNewError : setReplyError;
    setErr(null);
    setBusy(true);
    try {
      const created = await addProposalComment(proposalId, text, parentCommentId);
      setComments((prev) => [...(prev ?? []), created]);
      if (parentCommentId != null) setReplyingTo(null);
      return true;
    } catch (err) {
      setErr(proposalErrorMessage(err, "Couldn't post the comment."));
      // A refused reply usually means the thread changed under us (resolved meanwhile): show it as it is.
      if (parentCommentId != null) void load();
      return false;
    } finally {
      setBusy(false);
    }
  }

  async function resolve(commentId: number) {
    setResolveError(null);
    setBusy(true);
    try {
      const updated = await resolveProposalComment(proposalId, commentId);
      setComments((prev) => (prev ?? []).map((c) => (c.id === updated.id ? updated : c)));
    } catch (err) {
      setResolveError({ id: commentId, message: proposalErrorMessage(err, "Couldn't resolve the comment.") });
      void load();
    } finally {
      setBusy(false);
    }
  }

  const open = grouped.open.length;

  return (
    <section id={COMMENTS_PANEL_ID} aria-labelledby="proposal-comments-title" className="scroll-mt-28 border border-slate-200 bg-slate-50 p-3">
      <div className="mb-2 flex items-center justify-between gap-2">
        <h2 id="proposal-comments-title" className="text-sm font-semibold text-slate-800">Comments</h2>
        {open > 0 && (
          <span className="rounded-full bg-amber-100 px-2 py-0.5 text-xs font-medium text-amber-500">{open} open</span>
        )}
      </div>

      {comments === null && !loadError && <PanelSkeleton />}

      {loadError && (
        <div className="border border-danger-500 bg-danger-100 px-3 py-2 text-sm text-slate-800" role="alert">
          Could not load comments.{" "}
          <button type="button" onClick={() => void load()} className="font-medium text-green-700 hover:underline">Retry</button>
        </div>
      )}

      {comments !== null && (
        <>
          {comments.length === 0 ? (
            <p className="mb-3 text-sm text-slate-600">No comments yet.</p>
          ) : (
            <>
              {grouped.open.length > 0 && (
                <ul className="mb-3 space-y-2">
                  {grouped.open.map((t) => (
                    <Thread
                      key={t.head.id}
                      thread={t}
                      replying={replyingTo === t.head.id}
                      busy={busy}
                      replyError={replyingTo === t.head.id ? replyError : null}
                      resolveError={resolveError?.id === t.head.id ? resolveError.message : null}
                      onResolve={() => void resolve(t.head.id)}
                      onStartReply={() => { setReplyError(null); setReplyingTo(t.head.id); }}
                      onCancelReply={() => setReplyingTo(null)}
                      onReply={(text) => post(text, t.head.id)}
                    />
                  ))}
                </ul>
              )}
              {grouped.open.length === 0 && <p className="mb-3 text-sm text-slate-600">Nothing open.</p>}

              {grouped.resolved.length > 0 && (
                <div className="mb-3">
                  <button
                    type="button"
                    onClick={() => setShowResolved((v) => !v)}
                    aria-expanded={showResolved}
                    className="text-xs font-semibold uppercase tracking-wide text-slate-600 hover:text-slate-800"
                  >
                    {showResolved ? "▾" : "▸"} {grouped.resolved.length} resolved
                  </button>
                  {showResolved && (
                    <ul className="mt-2 space-y-2">
                      {grouped.resolved.map((t) => (
                        <Thread
                          key={t.head.id}
                          thread={t}
                          replying={false}
                          busy={busy}
                          replyError={null}
                          resolveError={null}
                          onResolve={() => undefined}
                          onStartReply={() => undefined}
                          onCancelReply={() => undefined}
                          onReply={async () => false}
                        />
                      ))}
                    </ul>
                  )}
                </div>
              )}
            </>
          )}

          <Composer
            label="New thread" placeholder="Start a new thread…" submitLabel="Post"
            busy={busy && replyingTo === null} error={newError} onSubmit={(text) => post(text)}
          />
        </>
      )}
    </section>
  );
}
