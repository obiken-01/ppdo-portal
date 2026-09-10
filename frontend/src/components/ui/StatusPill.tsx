"use client";

/**
 * StatusPill — the Budget Planning dashboard's status vocabulary, rendered (PPDO-20).
 *
 * Two kinds of pill live here on purpose, because the distinction is the point:
 *
 *   <StatusPill stage="In progress" />   — one of the four STAGES a thing can be at.
 *   <StatusPill risk="Over ceiling" />   — an EXCEPTION, which coexists with any stage.
 *
 * The dashboard had grown five overlapping status sets (Draft/Final, Met/Not yet, Set/Not set,
 * Submitted, Not started) that a reader had to hold in their head at once. Decision 9 of
 * `docs/v1.8/Budget_Planning_Dashboard_Requirements.md` collapses them into Linear's four.
 * `Over ceiling` / `Behind` / `Cannot submit` deliberately stayed OUT of those four — folding a
 * warning into a stage loses the warning behind a word the reader skims past. Hence one component
 * with two mutually exclusive props rather than one nine-value union.
 *
 * Colour follows the PPDO tokens: green for done, amber for in-flight and for warnings the user
 * can still act on, danger only for the two states that actually block. Slate for "not started",
 * which is a neutral fact rather than a problem.
 */

/** The four stages. Mirrors `PlanningStage` in the API types and `PlanningStage.cs`. */
export type StatusStage = "Todo" | "In progress" | "Review" | "Done";

/**
 * The exceptions. Not stages — see the component doc comment.
 * `Behind` is reserved for a schedule comparison Phase 4 introduces; it is accepted here so the
 * vocabulary is complete at its one definition rather than growing a fourth value later.
 */
export type StatusRisk = "Over ceiling" | "Behind" | "Cannot submit";

const STAGE_CLS: Record<StatusStage, string> = {
  Todo: "bg-slate-100 text-slate-600",
  "In progress": "bg-info-100 text-info-500",
  Review: "bg-amber-100 text-amber-500",
  Done: "bg-green-100 text-green-700",
};

const RISK_CLS: Record<StatusRisk, string> = {
  // Over ceiling is real money already committed past the limit — it blocks.
  "Over ceiling": "bg-danger-100 text-danger-500",
  // Behind is a warning the office can still act on.
  Behind: "bg-amber-100 text-amber-500",
  // Nobody can submit for this office at all — nothing the office itself can fix.
  "Cannot submit": "bg-danger-100 text-danger-500",
};

const BASE = "inline-flex items-center px-2 py-0.5 rounded-full text-xs font-medium whitespace-nowrap";

export default function StatusPill(
  props: ({ stage: StatusStage; risk?: never } | { risk: StatusRisk; stage?: never }) & {
    /** Optional leading glyph, e.g. a count. Kept short — this is a pill, not a label. */
    prefix?: string;
  }
) {
  const isRisk = props.risk != null;
  const label = isRisk ? props.risk! : props.stage!;
  const cls = isRisk ? RISK_CLS[props.risk!] : STAGE_CLS[props.stage!];

  return (
    <span className={`${BASE} ${cls}`} title={label}>
      {props.prefix ? `${props.prefix} ` : ""}
      {label}
    </span>
  );
}
