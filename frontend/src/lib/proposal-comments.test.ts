import { describe, expect, it } from "vitest";
import type { ProposalComment } from "@/types";
import { COMMENT_MAX, commentProblem, groupThreads, openCommentsNote, openThreadCount } from "./proposal-comments";

function c(id: number, parentCommentId: number | null = null, resolvedAt: string | null = null): ProposalComment {
  return {
    id, parentCommentId, body: `c${id}`, side: "Office", authorName: "A", createdAt: "2026-10-15T01:00:00Z",
    resolvedAt, resolvedByName: resolvedAt ? "B" : null, canResolve: parentCommentId == null && resolvedAt == null,
  };
}

describe("groupThreads (PPDO-220)", () => {
  it("puts replies under their head, in order, and splits open from resolved", () => {
    const { open, resolved } = groupThreads([
      c(1), c(2, 1), c(3), c(4, 3), c(5, 1), c(6, null, "2026-10-15T02:00:00Z"), c(7, 6),
    ]);

    expect(open.map((t) => t.head.id)).toEqual([1, 3]);
    expect(open[0].replies.map((r) => r.id)).toEqual([2, 5]);
    expect(open[1].replies.map((r) => r.id)).toEqual([4]);
    expect(resolved.map((t) => t.head.id)).toEqual([6]);
    expect(resolved[0].replies.map((r) => r.id)).toEqual([7]);
  });

  it("drops a reply whose head is missing rather than showing it as a thread", () => {
    const { open, resolved } = groupThreads([c(2, 99)]);
    expect(open).toEqual([]);
    expect(resolved).toEqual([]);
  });

  it("is empty for no comments", () => {
    expect(groupThreads([])).toEqual({ open: [], resolved: [] });
  });
});

describe("openThreadCount", () => {
  it("counts open heads only: replies and resolved heads never block Finalize", () => {
    expect(openThreadCount([c(1), c(2, 1), c(3, 1), c(4, null, "2026-10-15T02:00:00Z"), c(5)])).toBe(2);
    expect(openThreadCount([])).toBe(0);
  });
});

describe("openCommentsNote", () => {
  it("names the count, singular for one, and says nothing for none", () => {
    expect(openCommentsNote(1)).toBe("1 open comment.");
    expect(openCommentsNote(3)).toBe("3 open comments.");
    expect(openCommentsNote(0)).toBe("");
  });
});

describe("commentProblem", () => {
  it("refuses blank text and over-long text, accepts the limit", () => {
    expect(commentProblem("   ")).toBe("Write a comment.");
    expect(commentProblem("x".repeat(COMMENT_MAX + 1))).toMatch(/at most 2,000/);
    expect(commentProblem("x".repeat(COMMENT_MAX))).toBeNull();
    expect(commentProblem(" Fix it ")).toBeNull();
  });
});
