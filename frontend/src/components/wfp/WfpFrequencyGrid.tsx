"use client";

/**
 * Non-procurement frequency grid (v1.4 WFP Rework — RAL-124).
 *
 * One MoneyInput per period (12 monthly / 4 quarterly / 2 half-year / 1 annual — §2), plugged
 * into the "nature = Non-Procurement" (and the periods half of "Combined") branch of the
 * expenditure wizard shipped by RAL-123 (`budget-planning/wfp/entry/page.tsx`).
 *
 * Carry-forward is always an EXPLICIT user action (§5.1 ★REC) — never silent auto-fill as the
 * user tabs/types. "Apply to all remaining periods" copies period 1's value into every period
 * after it; a per-period "copy previous" button copies the immediately preceding period's
 * value into just that one cell.
 *
 * The live totals strip mirrors WfpExpenditureCalculator.Compute (backend:
 * PPDO.Application/Common/WfpExpenditureCalculator.cs) via computeWfpRollUpPreview — see that
 * function's docstring for why this is a client-side preview only, never the value actually
 * saved (the server always recomputes Q1-4/Net/Total on save).
 */

import { useRef } from "react";
import MoneyInput from "@/components/ui/MoneyInput";
import { formatMoney } from "@/lib/money";
import { computeWfpRollUpPreview, wfpPeriodCount } from "@/lib/wfp";
import type { SaveWfpExpenditurePeriodRequest, WfpExpenditureFrequency } from "@/types";

// ---------------------------------------------------------------------------
// Period labels per frequency
// ---------------------------------------------------------------------------

const MONTH_LABELS = ["Jan", "Feb", "Mar", "Apr", "May", "Jun", "Jul", "Aug", "Sep", "Oct", "Nov", "Dec"];

function periodLabels(frequency: WfpExpenditureFrequency): string[] {
  switch (frequency) {
    case "M": return MONTH_LABELS;
    case "Q": return ["Q1", "Q2", "Q3", "Q4"];
    case "B": return ["1st Half", "2nd Half"];
    case "A": return ["Annual Amount"];
  }
}

const GRID_COLS: Record<WfpExpenditureFrequency, string> = {
  M: "grid-cols-3 sm:grid-cols-4 md:grid-cols-6",
  Q: "grid-cols-2 sm:grid-cols-4",
  B: "grid-cols-2",
  A: "grid-cols-1",
};

// ---------------------------------------------------------------------------
// Props
// ---------------------------------------------------------------------------

export interface WfpFrequencyGridProps {
  frequency: WfpExpenditureFrequency;
  periods: SaveWfpExpenditurePeriodRequest[];
  onPeriodsChange: (periods: SaveWfpExpenditurePeriodRequest[]) => void;
  annualQuarterChoice: number;
  onAnnualQuarterChoiceChange: (choice: number) => void;
  applyReserve: boolean;
  /** null = "not specified" — the default (reserveRate x Net) is used for the preview. */
  reserveAmount: number | null;
  reserveRate: number;
}

// ---------------------------------------------------------------------------
// Component
// ---------------------------------------------------------------------------

export default function WfpFrequencyGrid({
  frequency,
  periods,
  onPeriodsChange,
  annualQuarterChoice,
  onAnnualQuarterChoiceChange,
  applyReserve,
  reserveAmount,
  reserveRate,
}: WfpFrequencyGridProps) {
  const gridRef = useRef<HTMLDivElement>(null);
  const count = wfpPeriodCount(frequency);
  const labels = periodLabels(frequency);

  function amountAt(periodNo: number): number | null {
    return periods.find((p) => p.periodNo === periodNo)?.amount ?? null;
  }

  function setAmountAt(periodNo: number, amount: number | null) {
    const rest = periods.filter((p) => p.periodNo !== periodNo);
    onPeriodsChange(amount == null ? rest : [...rest, { periodNo, amount }]);
  }

  function applyToAllRemaining() {
    const first = amountAt(1);
    if (first == null) return;
    const rest: SaveWfpExpenditurePeriodRequest[] = [{ periodNo: 1, amount: first }];
    for (let n = 2; n <= count; n++) rest.push({ periodNo: n, amount: first });
    onPeriodsChange(rest);
  }

  function copyPrevious(periodNo: number) {
    const prev = amountAt(periodNo - 1);
    if (prev == null) return;
    setAmountAt(periodNo, prev);
  }

  // Enter advances to the next period cell (Tab already does this natively via DOM order) —
  // explicit keyboard nav per the ticket's AC, never auto-fills a value.
  function handleGridKeyDown(e: React.KeyboardEvent<HTMLDivElement>) {
    if (e.key !== "Enter") return;
    e.preventDefault();
    const inputs = Array.from(gridRef.current?.querySelectorAll("input") ?? []);
    const active = document.activeElement;
    const idx = inputs.indexOf(active as HTMLInputElement);
    if (idx >= 0 && idx < inputs.length - 1) inputs[idx + 1].focus();
  }

  const canApplyToAll = count > 1 && amountAt(1) != null;

  // Net doesn't depend on reserve, so compute it first (reserveAmount arg = 0), then resolve
  // the reserve display value (explicit amount, or the rate x Net default) against that Net.
  const preview = computeWfpRollUpPreview(frequency, periods, 0, annualQuarterChoice);
  const resolvedReserve = applyReserve ? reserveAmount ?? preview.net * reserveRate : 0;
  const total = preview.net + resolvedReserve;

  return (
    <div className="space-y-3">
      {/* Annual "charge to" selector (§2 ★REC) */}
      {frequency === "A" && (
        <div>
          <label className="block text-xs font-medium text-slate-500 uppercase tracking-wide mb-1">
            Charge to
          </label>
          <div className="flex items-center border border-slate-200 overflow-hidden w-fit">
            {[1, 2, 3, 4].map((q) => (
              <button
                key={q}
                type="button"
                onClick={() => onAnnualQuarterChoiceChange(q)}
                className={`px-3 py-1.5 text-sm font-medium transition-colors ${
                  annualQuarterChoice === q
                    ? "bg-green-600 text-white"
                    : "bg-white text-slate-500 hover:bg-slate-50"
                }`}
              >
                Q{q}
              </button>
            ))}
          </div>
        </div>
      )}

      {/* Explicit carry-forward action */}
      {canApplyToAll && (
        <button
          type="button"
          onClick={applyToAllRemaining}
          className="text-xs font-medium text-green-700 hover:underline"
        >
          Apply period 1&apos;s amount to all remaining periods
        </button>
      )}

      {/* Period grid */}
      <div ref={gridRef} onKeyDown={handleGridKeyDown} className={`grid ${GRID_COLS[frequency]} gap-2`}>
        {labels.map((label, i) => {
          const periodNo = i + 1;
          return (
            <div key={periodNo}>
              <div className="flex items-center justify-between mb-0.5">
                <label className="text-[11px] text-slate-400">{label}</label>
                {periodNo > 1 && (
                  <button
                    type="button"
                    onClick={() => copyPrevious(periodNo)}
                    disabled={amountAt(periodNo - 1) == null}
                    title="Copy previous period's amount"
                    className="text-[10px] text-slate-400 hover:text-green-700 disabled:opacity-30 disabled:cursor-not-allowed"
                  >
                    ↵ copy prev
                  </button>
                )}
              </div>
              <MoneyInput
                value={amountAt(periodNo)}
                onChange={(v) => setAmountAt(periodNo, v)}
                className="w-full"
              />
            </div>
          );
        })}
      </div>

      {/* Live totals strip */}
      <div className="grid grid-cols-4 gap-2 pt-2 border-t border-slate-200 text-center">
        {(["q1", "q2", "q3", "q4"] as const).map((q, i) => (
          <div key={q}>
            <div className="text-[10px] text-slate-400 uppercase tracking-wide">Q{i + 1}</div>
            <div className="text-sm font-mono tabular-nums text-slate-700">₱{formatMoney(preview[q])}</div>
          </div>
        ))}
      </div>
      <div className="flex items-center justify-end gap-4 text-sm">
        <span className="text-slate-500">
          Net <span className="font-mono tabular-nums text-slate-700">₱{formatMoney(preview.net)}</span>
        </span>
        {applyReserve && (
          <span className="text-slate-500">
            Reserved <span className="font-mono tabular-nums text-slate-700">₱{formatMoney(resolvedReserve)}</span>
          </span>
        )}
        <span className="font-semibold text-slate-800">
          Total <span className="font-mono tabular-nums">₱{formatMoney(total)}</span>
        </span>
      </div>
    </div>
  );
}
