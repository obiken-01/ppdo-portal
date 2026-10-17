import { describe, expect, it } from "vitest";
import type { AipCommentSide, AipReviewComment } from "@/types";
import { groupAipThreads, isCollapsedThread, isOpenHead, openThreadCount } from "./aip-comment-threads";

const RESOLVED = "2026-10-16T02:00:00Z";

function c(
  id: number,
  parentCommentId: number | null = null,
  resolvedAt: string | null = null,
  extra: Partial<AipReviewComment> = {},
): AipReviewComment {
  const side: AipCommentSide = parentCommentId == null ? "Ppdo" : "Encoder";
  return {
    id, aipOfficeId: 1, nodeType: "Activity", nodeId: 980, nodeRefCode: "…-001", body: `c${id}`,
    authorId: "a", authorName: "A", authorSide: side, createdAt: "2026-10-16T01:00:00Z",
    resolvedAt, resolvedByName: resolvedAt ? "B" : null,
    canResolve: parentCommentId == null && resolvedAt == null, isOrphaned: false,
    isReviewerEdit: false, acknowledgedAt: null, acknowledgedByName: null,
    parentCommentId, canReply: parentCommentId == null && resolvedAt == null,
    ...extra,
  };
}

describe("groupAipThreads (PPDO-223)", () => {
  it("puts replies under their head, oldest first, heads in order", () => {
    const threads = groupAipThreads([c(1), c(2, 1), c(3), c(4, 3), c(5, 1)]);

    expect(threads.map((t) => t.head.id)).toEqual([1, 3]);
    expect(threads[0].replies.map((r) => r.id)).toEqual([2, 5]);
    expect(threads[1].replies.map((r) => r.id)).toEqual([4]);
  });

  it("drops a reply whose head is missing rather than showing it as a thread", () => {
    expect(groupAipThreads([c(2, 99)])).toEqual([]);
  });

  it("reads comments written before threads as heads with no replies", () => {
    const [only] = groupAipThreads([c(1, null, RESOLVED)]);
    expect(only.replies).toEqual([]);
  });
});

describe("open counts ignore replies", () => {
  /**
   * ⚠️ The row badge and the delete warning count this. One encoder reply on an open PPDO thread is
   * ONE open thread, and none once PPDO resolves the head — a `!c.resolvedAt` count says 2, then 1.
   */
  it("counts one open thread with an encoder reply as one, and zero once the head is resolved", () => {
    expect(openThreadCount([c(1), c(2, 1)])).toBe(1);
    expect(openThreadCount([c(1, null, RESOLVED), c(2, 1)])).toBe(0);
  });

  it("isOpenHead is false on every reply and on a resolved head", () => {
    expect(isOpenHead(c(1))).toBe(true);
    expect(isOpenHead(c(2, 1))).toBe(false);
    expect(isOpenHead(c(3, null, RESOLVED))).toBe(false);
  });
});

describe("isCollapsedThread", () => {
  it("collapses a resolved request thread but never a reviewer-edit note (PPDO-214)", () => {
    const [open, resolved, edit] = groupAipThreads([
      c(1), c(2, null, RESOLVED), c(3, null, RESOLVED, { isReviewerEdit: true }),
    ]);
    expect(isCollapsedThread(open)).toBe(false);
    expect(isCollapsedThread(resolved)).toBe(true);
    expect(isCollapsedThread(edit)).toBe(false);
  });
});
