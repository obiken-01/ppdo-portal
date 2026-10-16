/**
 * AIP activity comments as threads (v1.9.0 — PPDO-223, `docs/v1.9/AIP_Comment_Threads_Spec.md`).
 *
 * The server returns a node's comments flat; a head has `parentCommentId == null`, a reply names its
 * head. Pure helpers, so the counting rule is tested on its own (`aip-comment-threads.test.ts`).
 *
 * ⚠️ **Every count is of open HEADS.** A reply is stored with `resolvedAt` null for good, so a
 * count written as `!c.resolvedAt` would show one encoder reply as "1 unresolved" forever and keep the
 * row's badge amber after the thread was resolved. Use `isOpenHead`, never `!c.resolvedAt`.
 */

import type { AipReviewComment } from "@/types";

export interface AipCommentThread {
  head: AipReviewComment;
  /** Oldest first, in the order the server returned them. */
  replies: AipReviewComment[];
}

/** A thread's first comment, still open: the only kind of comment any count counts. */
export function isOpenHead(c: AipReviewComment): boolean {
  return c.parentCommentId == null && c.resolvedAt == null;
}

/** How many open threads are in a list (one node's, or the office's). */
export function openThreadCount(comments: readonly AipReviewComment[]): number {
  return comments.filter(isOpenHead).length;
}

/**
 * Nests replies under their head, heads in the order given (oldest first). A reply whose head is not
 * in the list is dropped: the server writes replies under a head of the same node, so one can only
 * appear mid-reload.
 */
export function groupAipThreads(comments: readonly AipReviewComment[]): AipCommentThread[] {
  const threads = new Map<number, AipCommentThread>();
  for (const c of comments)
    if (c.parentCommentId == null) threads.set(c.id, { head: c, replies: [] });
  for (const c of comments)
    if (c.parentCommentId != null) threads.get(c.parentCommentId)?.replies.push(c);
  return Array.from(threads.values());
}

/**
 * A resolved request thread — collapsed behind "N resolved". ⚠️ Not a reviewer-edit note (PPDO-214):
 * it is created resolved but is a record of a change, so it stays in view, as before PPDO-223.
 */
export function isCollapsedThread(t: AipCommentThread): boolean {
  return t.head.resolvedAt != null && !t.head.isReviewerEdit;
}
