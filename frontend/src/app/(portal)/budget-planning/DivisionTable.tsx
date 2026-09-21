"use client";

import { useState } from "react";
import Link from "next/link";
import StatusPill from "@/components/ui/StatusPill";
import { formatMoney } from "@/lib/money";
import type { DivisionSummary } from "@/types";

/**
 * DivisionTable — one row per PPDO division: allocation, AIP progress, and what is left
 * (PPDO-20, ticket F). Host office only; a guest office has no division split.
 *
 * ⚠️ **The column is not additive down the page when a PPA is shared.** A program assigned to two
 * divisions counts in full against both, because the row answers "what is this division
 * responsible for" — not "how does the office total split". Splitting it evenly would invent a
 * number nobody entered. Do not add a total row implying otherwise.
 *
 * Rows expand to the per-fund breakdown, which keeps the WFP ledger's Used/Remaining figures. That
 * is the Allocation page's own ledger view; the AIP-based Remaining is the one on the row itself.
 */

function divisionLabel(division: DivisionSummary): string {
  // divisionCode is optional by design (Allocation_Requirements.md §5) — fall back to the name
  // rather than rendering an empty pill.
  return division.divisionCode ?? division.divisionName;
}

function DivisionRow({
  division,
  canManageAllocation,
  officeId,
  fiscalYear,
}: {
  division: DivisionSummary;
  canManageAllocation: boolean;
  officeId: number | null;
  fiscalYear: number | null;
}) {
  const [expanded, setExpanded] = useState(false);
  const isOver = division.remaining < 0;

  return (
    <>
      <tr
        className="border-t border-slate-100 cursor-pointer hover:bg-slate-50"
        onClick={() => setExpanded((e) => !e)}
      >
        <td className="px-4 py-2.5 text-sm text-slate-600">
          <span
            className={`inline-block mr-1.5 text-slate-300 transition-transform ${
              expanded ? "rotate-90" : ""
            }`}
            aria-hidden
          >
            ›
          </span>
          <span className="font-medium text-slate-800">{divisionLabel(division)}</span>
          {division.divisionCode && (
            <span className="ml-2 text-xs text-slate-500">{division.divisionName}</span>
          )}
        </td>
        <td className="px-4 py-2.5">
          <div className="flex flex-wrap items-center gap-1">
            <StatusPill stage={division.aipStatus} />
            {isOver && <StatusPill risk="Over ceiling" />}
          </div>
        </td>
        <td className="px-4 py-2.5 text-sm text-right text-slate-600 tabular-nums">
          {division.costedActivityCount} / {division.totalActivities}
        </td>
        <td className="px-4 py-2.5 text-sm text-right text-slate-600 tabular-nums">
          ₱{formatMoney(division.allocated)}
        </td>
        <td className="px-4 py-2.5 text-sm text-right text-slate-600 tabular-nums">
          ₱{formatMoney(division.costedInAip)}
        </td>
        <td
          className={`px-4 py-2.5 text-sm text-right font-medium tabular-nums ${
            isOver ? "text-danger-500" : "text-slate-600"
          }`}
        >
          ₱{formatMoney(division.remaining)}
        </td>
        <td className="px-4 py-2.5 text-right">
          {/* Hidden, not disabled: a division-scoped encoder can never edit an allocation, and a
              greyed control just invites clicking. Disabled is reserved for state, not permission. */}
          {canManageAllocation && officeId != null && (
            <Link
              href={`/budget-planning/allocation?officeId=${officeId}${
                fiscalYear != null ? `&fiscalYear=${fiscalYear}` : ""
              }`}
              className="text-xs font-medium text-green-600 hover:text-green-700"
              onClick={(e) => e.stopPropagation()}
            >
              Allocation →
            </Link>
          )}
        </td>
      </tr>

      {expanded && (
        <tr>
          <td colSpan={7} className="p-0">
            {division.allocationByFund.length === 0 ? (
              <div className="bg-slate-50 px-4 py-2 pl-10 text-xs text-slate-600">
                No allocation in any fund.
              </div>
            ) : (
              <table className="w-full">
                <tbody>
                  {division.allocationByFund.map((fund) => (
                    <tr key={fund.fundingSourceId} className="bg-slate-50 text-xs">
                      <td className="px-4 py-1.5 pl-10 text-slate-600">{fund.fundName}</td>
                      <td className="px-4 py-1.5 text-slate-500">WFP used</td>
                      <td className="px-4 py-1.5" />
                      <td className="px-4 py-1.5 text-right text-slate-600 tabular-nums">
                        ₱{formatMoney(fund.amount)}
                      </td>
                      <td className="px-4 py-1.5 text-right text-slate-600 tabular-nums">
                        ₱{formatMoney(fund.used)}
                      </td>
                      <td className="px-4 py-1.5 text-right text-slate-600 tabular-nums">
                        ₱{formatMoney(fund.remaining)}
                      </td>
                      <td className="px-4 py-1.5" />
                    </tr>
                  ))}
                </tbody>
              </table>
            )}
          </td>
        </tr>
      )}
    </>
  );
}

const TH =
  "px-4 py-2.5 text-xs font-semibold text-slate-600 uppercase tracking-wide whitespace-nowrap";

export default function DivisionTable({
  divisions,
  canManageAllocation,
  officeId,
  fiscalYear,
}: {
  divisions: DivisionSummary[];
  canManageAllocation: boolean;
  officeId: number | null;
  fiscalYear: number | null;
}) {
  return (
    <div className="overflow-x-auto">
      <table className="w-full min-w-[860px]">
        <thead>
          <tr className="bg-slate-50">
            <th className={`${TH} text-left`}>Division</th>
            <th className={`${TH} text-left`}>AIP</th>
            <th className={`${TH} text-right`}>Costed / Total</th>
            <th className={`${TH} text-right`}>Allocated</th>
            <th className={`${TH} text-right`}>Costed in AIP</th>
            <th className={`${TH} text-right`}>Remaining</th>
            <th className={`${TH} text-right`} />
          </tr>
        </thead>
        <tbody>
          {divisions.map((division) => (
            <DivisionRow
              key={division.divisionId}
              division={division}
              canManageAllocation={canManageAllocation}
              officeId={officeId}
              fiscalYear={fiscalYear}
            />
          ))}
        </tbody>
      </table>
    </div>
  );
}
