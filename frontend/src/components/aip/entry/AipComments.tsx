"use client";

/**
 * Inline review comments on AIP rows (v1.8.0 Phase 4 — V18-53 / PPDO-71).
 *
 * ⚠️ **Collapsed by default, and that is a decision rather than a default.** A comment renders as a
 * marker in the row's gutter; the body opens deliberately. The AIP tree is already dense, and
 * inlining every remark would push the work off the screen behind commentary about it (spec §6.2).
 *
 * ⚠️ **Only the authoring side may resolve.** This file reads `canResolve` off each comment and
 * never re-derives it from who the reader is — the server decides, and it says no to the side a
 * comment is addressed to. If the control is ever shown on a comment the reader cannot resolve,
 * they get a 403 and no explanation.
 *
 * ⚠️ **The unresolved tally is two numbers, never one.** The reader can resolve neither set
 * themselves, so a merged total would imply an action that does not exist for them — and
 * "3 unresolved from PPDO" is the sentence that actually changes whether someone re-submits.
 */

import { createContext, useCallback, useContext, useEffect, useMemo, useState } from "react";
import type {
  AipCommentNodeType,
  AipCommentSide,
  AipReviewComment,
  AipReviewComments,
  AipUnresolvedCounts,
} from "@/types";
import { getAipComments, addAipComment, resolveAipComment } from "@/lib/aip-comments";
import { aipErrorMessage } from "@/lib/aip";

const SIDE_LABEL: Record<AipCommentSide, string> = {
  DepartmentHead: "department head",
  Ppdo: "PPDO",
};

/** `Activity-980` — node ids are unique per table, so the type is part of the key. */
const keyOf = (nodeType: AipCommentNodeType, nodeId: number) => `${nodeType}-${nodeId}`;

interface CommentsState {
  byNode: Map<string, AipReviewComment[]>;
  data: AipReviewComments | null;
  /** The side currently being hunted for, or null. Auto-opens matching threads. */
  filter: AipCommentSide | null;
  setFilter: (side: AipCommentSide | null) => void;
  reload: () => Promise<void>;
  add: (nodeType: AipCommentNodeType, nodeId: number, body: string) => Promise<void>;
  resolve: (commentId: number) => Promise<void>;
}

const Ctx = createContext<CommentsState | null>(null);

/**
 * Fetches an office's comments once and shares them with every row.
 *
 * ⚠️ One fetch for the whole tree, not one per row. A gutter control per activity fetching its own
 * comments is the N+1 that `PERFORMANCE_GUIDELINES.md` exists to prevent, and it would be invisible
 * until an office had fifty activities.
 */
export function AipCommentsProvider({
  aipRecordId,
  officeId,
  children,
}: {
  aipRecordId: number | null;
  officeId: number | null;
  children: React.ReactNode;
}) {
  const [data, setData] = useState<AipReviewComments | null>(null);
  const [filter, setFilter] = useState<AipCommentSide | null>(null);

  const reload = useCallback(async () => {
    if (aipRecordId == null || officeId == null) return;
    try {
      setData(await getAipComments(aipRecordId, officeId));
    } catch {
      // The tree is the page's job; comments failing must not take the work surface with them.
      setData(null);
    }
  }, [aipRecordId, officeId]);

  useEffect(() => { void reload(); }, [reload]);

  const byNode = useMemo(() => {
    const map = new Map<string, AipReviewComment[]>();
    for (const c of data?.comments ?? []) {
      const k = keyOf(c.nodeType, c.nodeId);
      const list = map.get(k);
      if (list) list.push(c);
      else map.set(k, [c]);
    }
    return map;
  }, [data]);

  const add = useCallback(
    async (nodeType: AipCommentNodeType, nodeId: number, body: string) => {
      if (aipRecordId == null || officeId == null) return;
      await addAipComment(aipRecordId, officeId, { nodeType, nodeId, body });
      await reload();
    },
    [aipRecordId, officeId, reload]
  );

  const resolve = useCallback(
    async (commentId: number) => {
      await resolveAipComment(commentId);
      await reload();
    },
    [reload]
  );

  const value = useMemo<CommentsState>(
    () => ({ byNode, data, filter, setFilter, reload, add, resolve }),
    [byNode, data, filter, reload, add, resolve]
  );

  return <Ctx.Provider value={value}>{children}</Ctx.Provider>;
}

function useComments(): CommentsState | null {
  return useContext(Ctx);
}

/**
 * The unresolved tally for the office this provider is loaded for, or null when there is no
 * provider or the fetch failed (PPDO-72).
 *
 * ⚠️ **Reads what the provider already holds — it does not fetch.** The whole reason the tree's
 * comments are loaded once at the top is that a per-consumer fetch is an N+1 nobody notices until
 * an office has fifty activities; a second request just for the re-submit warning would reopen
 * that on the one page encoders use most.
 *
 * ⚠️ **Null is not zero.** A failed comment fetch must not be rendered as "nothing outstanding" —
 * the caller decides what to do with not-knowing, and for the re-submit gate that means letting
 * the re-submit through rather than inventing a warning or blocking on a request that failed.
 */
export function useUnresolvedCounts(): AipUnresolvedCounts | null {
  return useComments()?.data?.unresolved ?? null;
}

// ── The tally, as filter buttons ────────────────────────────────────────────

/**
 * The unresolved counts, split by who wrote them, as toggles.
 *
 * ⚠️ Renders nothing when there is nothing outstanding — an encoder building a Draft has no
 * reviewer and no comments, and a permanent "0 unresolved" strip is noise on the page they use
 * most.
 */
export function AipCommentFilterBar() {
  const ctx = useComments();
  if (!ctx?.data) return null;

  const { fromDepartmentHead, fromPpdo } = ctx.data.unresolved;
  if (fromDepartmentHead + fromPpdo === 0) return null;

  return (
    <div className="flex flex-wrap items-center gap-2 border border-slate-200 bg-white px-4 py-3">
      <span className="text-xs font-semibold uppercase tracking-wide text-slate-800">
        Unresolved comments
      </span>
      <FilterChip side="Ppdo" count={fromPpdo} />
      <FilterChip side="DepartmentHead" count={fromDepartmentHead} />
      {ctx.filter && (
        <button
          type="button"
          onClick={() => ctx.setFilter(null)}
          className="text-xs text-slate-600 underline hover:text-slate-800"
        >
          Clear
        </button>
      )}
    </div>
  );
}

function FilterChip({ side, count }: { side: AipCommentSide; count: number }) {
  const ctx = useComments()!;
  if (count === 0) return null;

  const active = ctx.filter === side;
  return (
    <button
      type="button"
      onClick={() => ctx.setFilter(active ? null : side)}
      aria-pressed={active}
      className={`rounded-full px-3 py-1 text-xs font-medium transition-colors ${
        active
          ? "bg-green-700 text-white"
          : "border border-slate-300 bg-slate-50 text-slate-800 hover:bg-slate-100"
      }`}
    >
      {count} from {SIDE_LABEL[side]}
    </button>
  );
}

// ── The per-row gutter ──────────────────────────────────────────────────────

/**
 * One row's comment marker and thread.
 *
 * Renders nothing at all when there is nothing to show and the reader cannot comment — which is
 * every row for an encoder in Draft, i.e. the overwhelmingly common case.
 */
export function AipCommentAnchor({
  nodeType,
  nodeId,
}: {
  nodeType: AipCommentNodeType;
  nodeId: number;
}) {
  const ctx = useComments();
  const [open, setOpen] = useState(false);
  const [draft, setDraft] = useState("");
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);

  const comments = ctx?.byNode.get(keyOf(nodeType, nodeId)) ?? [];
  const unresolved = comments.filter((c) => !c.resolvedAt);

  // A filter selection opens the threads it matches, so the reader is taken to the rows rather
  // than left to hunt for a marker.
  const matchesFilter =
    ctx?.filter != null && unresolved.some((c) => c.authorSide === ctx.filter);
  const expanded = open || matchesFilter;

  if (!ctx?.data) return null;
  if (comments.length === 0 && !ctx.data.canComment) return null;

  async function submit() {
    const body = draft.trim();
    if (!body || !ctx) return;
    setBusy(true);
    setError(null);
    try {
      await ctx.add(nodeType, nodeId, body);
      setDraft("");
      setOpen(true);
    } catch (e) {
      setError(aipErrorMessage(e, "Could not save this comment."));
    } finally {
      setBusy(false);
    }
  }

  async function markResolved(id: number) {
    if (!ctx) return;
    setError(null);
    try {
      await ctx.resolve(id);
    } catch (e) {
      setError(aipErrorMessage(e, "Could not resolve this comment."));
    }
  }

  return (
    <div className="mt-1">
      <button
        type="button"
        onClick={() => setOpen((v) => !v)}
        className={`inline-flex items-center gap-1 rounded-full px-2 py-0.5 text-[11px] font-medium transition-colors ${
          unresolved.length > 0
            ? "bg-amber-100 text-amber-900 hover:bg-amber-200"
            : "border border-slate-300 bg-white text-slate-600 hover:bg-slate-50"
        }`}
      >
        💬
        {comments.length === 0
          ? "Comment"
          : unresolved.length > 0
            ? `${unresolved.length} unresolved`
            : `${comments.length} resolved`}
      </button>

      {expanded && (
        <div className="mt-2 space-y-2 border-l-2 border-slate-200 pl-3">
          {comments.map((c) => (
            <CommentRow key={c.id} comment={c} onResolve={() => markResolved(c.id)} />
          ))}

          {ctx.data.canComment && (
            <div className="pt-1">
              <textarea
                value={draft}
                onChange={(e) => setDraft(e.target.value)}
                maxLength={2000}
                rows={2}
                placeholder="Ask for a change on this row…"
                className="w-full border border-slate-300 px-2 py-1 text-xs text-slate-800"
              />
              <div className="mt-1 flex items-center gap-2">
                <button
                  type="button"
                  onClick={() => void submit()}
                  disabled={busy || draft.trim().length === 0}
                  className="bg-green-700 px-3 py-1 text-xs font-medium text-white hover:bg-green-800 disabled:cursor-not-allowed disabled:bg-slate-300"
                >
                  {busy ? "Saving…" : "Comment"}
                </button>
                <span className="text-[11px] text-slate-600">{draft.length}/2000</span>
              </div>
            </div>
          )}

          {error && <p className="text-[11px] text-red-600">{error}</p>}
        </div>
      )}
    </div>
  );
}

function CommentRow({
  comment,
  onResolve,
}: {
  comment: AipReviewComment;
  onResolve: () => void;
}) {
  const resolved = comment.resolvedAt != null;

  return (
    <div className={`text-xs ${resolved ? "opacity-60" : ""}`}>
      <div className="flex flex-wrap items-center gap-2">
        <span className="font-medium text-slate-800">{comment.authorName}</span>
        <span className="rounded-full bg-slate-100 px-1.5 py-0.5 text-[10px] font-semibold uppercase tracking-wide text-slate-600">
          {SIDE_LABEL[comment.authorSide]}
        </span>
        {resolved && (
          <span className="text-[10px] text-slate-600">
            resolved{comment.resolvedByName ? ` by ${comment.resolvedByName}` : ""}
          </span>
        )}
        {/* ⚠️ Shown only when the server says this reader may resolve it. Never inferred here. */}
        {comment.canResolve && (
          <button
            type="button"
            onClick={onResolve}
            className="text-[11px] text-green-800 underline hover:text-green-900"
          >
            Mark as resolved
          </button>
        )}
      </div>
      {/* ⚠️ slate-600, not slate-700 — the latter is not a PPDO token and silently falls back to
          stock Tailwind's blue-tinted slate (DESIGN_SYSTEM.md §1). Fixed in PPDO-74, where the
          same comment bodies render on a second page. */}
      <p className="mt-0.5 whitespace-pre-wrap text-slate-600">{comment.body}</p>
      {/* ⚠️ An orphaned comment is kept and labelled, never dropped: it is part of the record of
          why the work changed, and the row it questioned being deleted is often the answer. */}
      {comment.isOrphaned && (
        <p className="mt-0.5 text-[10px] italic text-slate-600">
          The row this referred to has since been removed.
        </p>
      )}
    </div>
  );
}
