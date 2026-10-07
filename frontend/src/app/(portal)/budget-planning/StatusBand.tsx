"use client";

import Link from "next/link";
import type { BandCount, BandStep, BandTone, StatusBand as StatusBandModel } from "@/lib/dashboard-status-band";

/**
 * StatusBand — the top of the Investment Planning dashboard (PPDO-178, design pass F12).
 *
 * One sentence saying where the AIP is and who has it, one line saying why, the one next step, and
 * a thin step track. It replaced the action card plus 3–5 stage cards, which on a phone filled the
 * whole first screen. The words come from `lib/dashboard-status-band.ts`; this only lays them out.
 *
 * Phone: the step track becomes a short list (wireframe board 4), so the status, the button and the
 * progress fit the first screen at 375 px.
 *
 * The tone shows as the left edge only. The headline carries the meaning; colour backs it up and is
 * never the only signal.
 */

const EDGE: Record<BandTone, string> = {
  todo: "border-l-info-500",
  waiting: "border-l-slate-300",
  blocking: "border-l-danger-500",
  returned: "border-l-amber-500",
  done: "border-l-green-600",
  info: "border-l-slate-300",
};

/** The band's height before and after load, so nothing below it moves (CLS). */
const SHELL = "bg-white border border-slate-200 border-l-4 px-4 py-4 sm:px-6 sm:py-5 min-h-[188px]";

const PRIMARY =
  "inline-flex items-center justify-center px-3 py-2 bg-green-600 hover:bg-green-500 text-white text-sm font-medium transition-colors whitespace-nowrap";
const SECONDARY =
  "inline-flex items-center justify-center px-3 py-2 bg-white border border-slate-200 hover:bg-slate-50 text-slate-800 text-sm font-medium transition-colors whitespace-nowrap";

function StepBar({ state }: { state: BandStep["state"] }) {
  const cls =
    state === "done"
      ? "bg-green-500"
      : state === "current"
      ? "bg-green-700 ring-2 ring-green-200"
      : "bg-slate-200";
  return <span aria-hidden className={`block h-1.5 ${cls}`} />;
}

function StepTrack({ steps }: { steps: BandStep[] }) {
  return (
    <ol aria-label="AIP progress" className="flex flex-col gap-1.5 sm:flex-row sm:flex-wrap sm:gap-2">
      {steps.map((s) => (
        <li
          key={s.key}
          aria-current={s.state === "current" ? "step" : undefined}
          className="flex items-center gap-2 sm:flex-1 sm:min-w-[120px] sm:flex-col sm:items-stretch sm:gap-1.5"
        >
          {/* Phone: a short marker beside the text. Wider: a bar across the top of the step. */}
          <span className="w-3 shrink-0 sm:w-auto">
            <StepBar state={s.state} />
          </span>
          <span
            className={`text-[13px] font-semibold whitespace-nowrap ${
              s.state === "current" ? "text-green-700" : s.state === "done" ? "text-slate-800" : "text-slate-600"
            }`}
          >
            {s.label}
            <span className="sr-only">
              {s.state === "done" ? " (done)" : s.state === "current" ? " (current step)" : ""}
            </span>
          </span>
          <span className="text-xs text-slate-600 truncate min-w-0">{s.note}</span>
        </li>
      ))}
    </ol>
  );
}

function CountTrack({ counts }: { counts: BandCount[] }) {
  const total = counts.reduce((n, c) => n + c.count, 0);
  return (
    <ol aria-label="Offices by stage" className="grid grid-cols-5 gap-2">
      {counts.map((c) => (
        <li key={c.key} className="flex flex-col gap-1 min-w-0">
          <span
            aria-hidden
            className={`block h-1.5 ${c.key === "PpdoReview" ? "bg-info-500" : c.count > 0 ? "bg-green-500" : "bg-slate-200"}`}
            style={{ opacity: total > 0 && c.count === 0 ? 0.5 : 1 }}
          />
          <span className="text-lg font-semibold text-slate-800 tabular-nums leading-tight">{c.count}</span>
          <span className="text-xs text-slate-600 truncate">{c.label}</span>
        </li>
      ))}
    </ol>
  );
}

export default function StatusBand({ band }: { band: StatusBandModel }) {
  return (
    <section aria-labelledby="status-band-headline" className={`${SHELL} ${EDGE[band.tone]} flex flex-col gap-4 sm:gap-5`}>
      <div className="flex flex-col gap-3 sm:flex-row sm:items-start sm:justify-between sm:gap-6">
        <div className="min-w-0 flex-1">
          <p className="text-sm text-slate-600">{band.eyebrow}</p>
          <h2
            id="status-band-headline"
            className="mt-1 text-lg sm:text-xl leading-snug font-semibold text-slate-800"
          >
            {band.headline}
          </h2>
          {band.reason && <p className="mt-1.5 text-sm text-slate-600">{band.reason}</p>}
        </div>
        {(band.action || band.secondary) && (
          <div className="flex flex-wrap gap-2 shrink-0">
            {band.secondary && (
              <Link href={band.secondary.href} className={SECONDARY}>
                {band.secondary.label}
              </Link>
            )}
            {band.action && (
              <Link href={band.action.href} className={PRIMARY}>
                {band.action.label}
              </Link>
            )}
          </div>
        )}
      </div>

      {band.counts && band.counts.length > 0 ? (
        <CountTrack counts={band.counts} />
      ) : band.steps.length > 0 ? (
        <StepTrack steps={band.steps} />
      ) : null}
    </section>
  );
}

/** The band's skeleton: same shell and height, so the loaded band replaces it without a shift. */
export function StatusBandSkeleton() {
  return (
    <div aria-hidden className={`${SHELL} border-l-slate-200 flex flex-col gap-3`}>
      <div className="h-3.5 w-44 bg-slate-100 animate-pulse" />
      <div className="h-6 w-3/5 bg-slate-100 animate-pulse" />
      <div className="h-3.5 w-1/2 bg-slate-100 animate-pulse" />
      <div className="mt-auto flex gap-2">
        {Array.from({ length: 5 }).map((_, i) => (
          <div key={i} className="flex-1 h-1.5 bg-slate-100 animate-pulse" />
        ))}
      </div>
    </div>
  );
}
