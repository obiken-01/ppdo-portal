"use client";

import { formatMoney } from "@/lib/money";

/**
 * StackedFundBar — one funding source's ceiling, split by division, as a stacked bar (PPDO-20).
 *
 * Replaces the per-fund doughnut charts. On the live FY2027 page three of the four doughnuts were
 * a single 100% slice and five of the six General Fund legend rows read ₱0.00 — a chart drawn to
 * show a split, showing no split. A stacked bar carries the same information, sorts by size, stays
 * readable at six divisions, stacks on a phone, and takes the Chart.js dependency off this page
 * (decision 10 of `docs/v1.8/Budget_Planning_Dashboard_Requirements.md`).
 *
 * The unallocated remainder is a segment like any other, drawn last in a muted tone. When
 * allocations EXCEED the ceiling there is no remainder to draw and the bar is scaled to the
 * allocated total instead, so the segments still sum to the full width — the over-ceiling fact is
 * carried by the caller's risk pill and by the stated numbers, not by a bar that silently
 * overflows its container.
 */

export interface FundBarSegment {
  key: string | number;
  /** Short label — a division code where there is one, the name otherwise. Never an empty pill. */
  label: string;
  amount: number;
}

/**
 * Division colours. Deliberately the same eight the doughnuts used, so a reader who knew the old
 * chart's colours does not have to relearn them. Cycles past eight divisions; PPDO has six.
 */
const SEGMENT_COLORS = [
  "#7F77DD", "#1D9E75", "#D85A30", "#D4537E",
  "#378ADD", "#BA7517", "#639922", "#993556",
];
const REMAINDER_COLOR = "#C7CFD6"; // slate-300 — decorative, matches the muted-divider token

function segmentColor(index: number): string {
  return SEGMENT_COLORS[index % SEGMENT_COLORS.length];
}

export default function StackedFundBar({
  fundName,
  ceiling,
  segments,
  remaining,
}: {
  fundName: string;
  ceiling: number;
  segments: FundBarSegment[];
  /** ceiling − allocated. Negative means over-allocated. */
  remaining: number;
}) {
  const allocated = segments.reduce((sum, s) => sum + s.amount, 0);
  // Scale to whichever is larger so an over-allocated bar still fills its track exactly once
  // rather than overflowing. Guarded against a zero denominator: a fund with no ceiling and no
  // allocation renders an empty track, not NaN widths.
  const scale = Math.max(ceiling, allocated);
  const sorted = [...segments].sort((a, b) => b.amount - a.amount);

  const pct = (n: number) => (scale > 0 ? (n / scale) * 100 : 0);

  return (
    <div className="bg-white border border-slate-200 p-4">
      <div className="flex items-baseline justify-between gap-3 mb-2">
        <p className="text-sm font-semibold text-slate-800 truncate">{fundName}</p>
        <p className="text-sm font-semibold text-slate-800 tabular-nums whitespace-nowrap">
          ₱{formatMoney(ceiling)}
        </p>
      </div>

      <div className="flex h-3 w-full bg-slate-100 overflow-hidden mb-3">
        {sorted.map((segment, i) => (
          <div
            key={segment.key}
            className="h-full"
            style={{ width: `${pct(segment.amount)}%`, backgroundColor: segmentColor(i) }}
            title={`${segment.label}: ₱${formatMoney(segment.amount)}`}
          />
        ))}
        {remaining > 0 && (
          <div
            className="h-full"
            style={{ width: `${pct(remaining)}%`, backgroundColor: REMAINDER_COLOR }}
            title={`Unallocated: ₱${formatMoney(remaining)}`}
          />
        )}
      </div>

      <ul className="space-y-1">
        {sorted.map((segment, i) => (
          <li key={segment.key} className="flex items-center gap-2 text-xs">
            <span
              className="w-2 h-2 rounded-full shrink-0"
              style={{ backgroundColor: segmentColor(i) }}
            />
            <span className="text-slate-600 truncate">{segment.label}</span>
            <span className="ml-auto text-slate-600 tabular-nums whitespace-nowrap">
              ₱{formatMoney(segment.amount)}
            </span>
          </li>
        ))}
        <li className="flex items-center gap-2 text-xs">
          <span
            className="w-2 h-2 rounded-full shrink-0"
            style={{ backgroundColor: REMAINDER_COLOR }}
          />
          <span className="text-slate-600">{remaining < 0 ? "Over ceiling" : "Unallocated"}</span>
          <span
            className={`ml-auto tabular-nums whitespace-nowrap ${
              remaining < 0 ? "text-danger-500 font-medium" : "text-slate-600"
            }`}
          >
            ₱{formatMoney(Math.abs(remaining))}
          </span>
        </li>
      </ul>
    </div>
  );
}
