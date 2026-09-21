"use client";

import Link from "next/link";
import StatusPill, { type StatusRisk, type StatusStage } from "./StatusPill";

/**
 * PipelineRail — the ordered stages of budget planning, each naming its owner (PPDO-20).
 *
 * Replaces the 2×2 readiness hub. Two things the hub could not say:
 *
 *   1. **Order.** Ceiling → division allocation → PPA assignment → AIP → AIP submission is the
 *      real sequence of work, and it already exists in the code as a chain of gates. A 2×2 grid
 *      hides that, and showed four of the five.
 *   2. **Ownership.** Roughly half the support traffic on this feature is "why can't I edit
 *      this?", and the answer is almost always that the stage belongs to somebody else. So every
 *      stage states its owner, whether or not the viewer is that owner.
 *
 * The rail is a LIST of stages, not a fixed five: a guest office gets three (ceiling → AIP → AIP
 * submission) because division allocation and PPA assignment are host-office-only. It renders
 * three boxes, not five with two struck through — an earlier draft did the latter and it was
 * reversed. A guest office does not need to be told about stages that will never apply to it.
 *
 * Layout: horizontal on `md`+ with connectors between stages, stacked on a phone. Every stage box
 * is the same height in both states so the skeleton can match it exactly (CLS —
 * `docs/PERFORMANCE_GUIDELINES.md` §6).
 */

export interface PipelineStage {
  key: string;
  /** Short stage name, e.g. "Ceiling". Not a sentence. */
  label: string;
  stage: StatusStage;
  /** Whose job this stage is, e.g. "PBO" or "Your office". Always shown. */
  owner: string;
  /** An exception on top of the stage — rendered as a second pill, never instead of the first. */
  risk?: StatusRisk;
  /** Makes the box a link. Omit for a stage this viewer cannot open. */
  href?: string;
  /** One short line under the pills, e.g. "₱4.2M published". */
  detail?: string;
}

const BOX =
  "flex-1 min-w-0 bg-white border border-slate-200 p-3 flex flex-col gap-1.5 min-h-[104px]";

function StageBox({ stage, index }: { stage: PipelineStage; index: number }) {
  const body = (
    <>
      <div className="flex items-center gap-2">
        <span className="inline-flex items-center justify-center w-5 h-5 rounded-full bg-slate-100 text-[11px] font-semibold text-slate-600 shrink-0 tabular-nums">
          {index + 1}
        </span>
        <span className="text-sm font-semibold text-slate-800 truncate">{stage.label}</span>
      </div>
      <div className="flex flex-wrap items-center gap-1">
        <StatusPill stage={stage.stage} />
        {stage.risk && <StatusPill risk={stage.risk} />}
      </div>
      <p className="text-xs text-slate-600 truncate">
        {stage.detail ?? <span className="text-slate-500">{stage.owner}</span>}
      </p>
      {stage.detail && <p className="text-xs text-slate-500 truncate">{stage.owner}</p>}
    </>
  );

  if (!stage.href) return <div className={BOX}>{body}</div>;

  return (
    <Link href={stage.href} className={`${BOX} hover:border-green-300 hover:bg-green-25 transition-colors`}>
      {body}
    </Link>
  );
}

export default function PipelineRail({ stages }: { stages: PipelineStage[] }) {
  return (
    <div className="flex flex-col md:flex-row md:items-stretch gap-2">
      {stages.map((stage, i) => (
        <div key={stage.key} className="flex flex-1 min-w-0 items-stretch gap-2">
          <StageBox stage={stage} index={i} />
          {i < stages.length - 1 && (
            <span
              aria-hidden
              className="hidden md:flex items-center text-slate-300 text-lg shrink-0"
            >
              ›
            </span>
          )}
        </div>
      ))}
    </div>
  );
}

/** Skeleton matching the real rail's box count and height, so first paint does not jump. */
export function PipelineRailSkeleton({ stages = 5 }: { stages?: number }) {
  return (
    <div className="flex flex-col md:flex-row gap-2">
      {Array.from({ length: stages }).map((_, i) => (
        <div key={i} className="flex-1 bg-white border border-slate-200 p-3 min-h-[104px]">
          <div className="h-4 w-24 bg-slate-100 animate-pulse mb-2" />
          <div className="h-5 w-20 bg-slate-100 rounded-full animate-pulse mb-2" />
          <div className="h-3 w-16 bg-slate-100 animate-pulse" />
        </div>
      ))}
    </div>
  );
}
