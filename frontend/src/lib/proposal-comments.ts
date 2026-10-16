/**
 * Investment Proposal comment threads (v1.9.0 — PPDO-220,
 * `docs/v1.9/Investment_Proposal_Comments_Spec.md`). API calls plus the pure grouping the panel renders.
 *
 * ⚠️ **Scope, side and every rule are the server's.** `canResolve` is read off each comment, never
 * re-derived from who the reader is: a Resolve shown to the wrong side is a 403 with no explanation.
 */

import api from "./api";
import type { ApiResponse, ProposalComment } from "@/types";

function unwrap<T>(body: ApiResponse<T>): T {
  if (body.data == null) throw new Error(body.error ?? "Unexpected empty response.");
  return body.data;
}

export const COMMENT_MAX = 2000;

/** The proposal's comments, oldest first. Heads and replies come back flat. */
export async function getProposalComments(proposalId: number): Promise<ProposalComment[]> {
  const { data } = await api.get<ApiResponse<ProposalComment[]>>(`/budget-planning/proposals/${proposalId}/comments`);
  return unwrap(data);
}

/** A new thread, or — with `parentCommentId` — a reply under an open thread's head. */
export async function addProposalComment(
  proposalId: number, body: string, parentCommentId?: number,
): Promise<ProposalComment> {
  const { data } = await api.post<ApiResponse<ProposalComment>>(
    `/budget-planning/proposals/${proposalId}/comments`,
    parentCommentId == null ? { body } : { body, parentCommentId });
  return unwrap(data);
}

/** Resolves a thread by its head. The server refuses a reply and the wrong side. */
export async function resolveProposalComment(proposalId: number, commentId: number): Promise<ProposalComment> {
  const { data } = await api.post<ApiResponse<ProposalComment>>(
    `/budget-planning/proposals/${proposalId}/comments/${commentId}/resolve`);
  return unwrap(data);
}

/** A head comment and the replies under it. */
export interface CommentThread {
  head: ProposalComment;
  replies: ProposalComment[];
}

export interface GroupedThreads {
  open: CommentThread[];
  resolved: CommentThread[];
}

/**
 * Groups the flat list into threads, replies under their head in the order given (oldest first).
 * A thread is resolved when its head is. A reply whose head is missing is dropped: the server only
 * writes replies under a head of the same proposal, so one can only appear mid-reload.
 */
export function groupThreads(comments: ProposalComment[]): GroupedThreads {
  const threads = new Map<number, CommentThread>();
  for (const c of comments)
    if (c.parentCommentId == null) threads.set(c.id, { head: c, replies: [] });
  for (const c of comments)
    if (c.parentCommentId != null) threads.get(c.parentCommentId)?.replies.push(c);

  const all = Array.from(threads.values());
  return {
    open: all.filter((t) => t.head.resolvedAt == null),
    resolved: all.filter((t) => t.head.resolvedAt != null),
  };
}

/** Open threads — the number that blocks Finalize (replies never count). */
export function openThreadCount(comments: ProposalComment[]): number {
  return comments.filter((c) => c.parentCommentId == null && c.resolvedAt == null).length;
}

/** "Fix the objective" is fine; blank or over the limit is not. Null when sendable. */
export function commentProblem(text: string): string | null {
  const t = text.trim();
  if (t.length === 0) return "Write a comment.";
  if (t.length > COMMENT_MAX) return `A comment is at most ${COMMENT_MAX.toLocaleString("en-US")} characters.`;
  return null;
}

/** "N open comments." under Finalize; singular for one. Empty when nothing blocks. */
export function openCommentsNote(open: number): string {
  if (open <= 0) return "";
  return `${open} open comment${open === 1 ? "" : "s"}.`;
}

/** Manila time, the portal's convention. */
export function formatCommentTime(iso: string): string {
  return new Date(iso).toLocaleString("en-PH", {
    timeZone: "Asia/Manila", day: "numeric", month: "short", hour: "numeric", minute: "2-digit",
  });
}
