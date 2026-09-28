"use client";

import { useState } from "react";
import Link from "next/link";
import StatusPill from "@/components/ui/StatusPill";
import { formatMoney } from "@/lib/money";
import { FIRST_ENTERED_FISCAL_YEAR } from "@/lib/aip-fiscal-years";
import type { DivisionSummary } from "@/types";

/**
 * DivisionTable — one row per division: allocation, AIP progress, and what is left
 * (PPDO-20, ticket F). Shared by PPDO's own dashboard and a guest office's (PPDO-127) — both
 * pass a server-scoped `divisions` list, already narrowed to what the caller may see.
 *
 * ⚠️ **FY2027 and earlier: the column is not additive down the page when a PPA is shared.** A
 * program assigned to two divisions counts in full against both there. ↩️ **FY2028+ is additive**
 * (PPDO-150): each activity counts once, against its own division tag, and untagged work sits on
 * the separate "No division" row — so the rows plus that row equal the office. Still no total row:
 * a division-scoped viewer sees only their own row, and a total would read as the office's.
 *
 * Rows expand to the per-fund breakdown. ↩️ **Where "used" comes from depends on the year**
 * (2026-09-24): FY2027 and earlier read the WFP ledger, as the Allocation page does; FY2028+ has no
 * WFP, so the server reads the AIP by the ceiling's rule (MOOE + CO, PS exempt, rounded up per
 * activity) and the row becomes the sum of its fund rows. The label says which, because the two are
 * different numbers and a "WFP used" label on AIP money would mislead.
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
  unassigned = false,
}: {
  division: DivisionSummary;
  canManageAllocation: boolean;
  officeId: number | null;
  fiscalYear: number | null;
  /**
   * PPDO-150 — the "No division" row: FY2028+ work nobody has tagged yet. It has no allocation, so
   * Allocated and Remaining read as a dash, and it never shows "Over ceiling" — it is work waiting
   * to be tagged, not a division over its share.
   */
  unassigned?: boolean;
}) {
  const [expanded, setExpanded] = useState(false);
  const isOver = !unassigned && division.remaining < 0;
  const usedLabel = fiscalYear != null && fiscalYear >= FIRST_ENTERED_FISCAL_YEAR ? "AIP costed" : "WFP used";

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
          {unassigned ? (
            <>
              <span className="font-medium italic text-slate-800">No division</span>
              <span className="ml-2 text-xs text-slate-500">Activities to tag in AIP Entry</span>
            </>
          ) : (
            <>
              <span className="font-medium text-slate-800">{divisionLabel(division)}</span>
              {division.divisionCode && (
                <span className="ml-2 text-xs text-slate-500">{division.divisionName}</span>
              )}
            </>
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
          {unassigned ? "—" : `₱${formatMoney(division.allocated)}`}
        </td>
        <td className="px-4 py-2.5 text-sm text-right text-slate-600 tabular-nums">
          ₱{formatMoney(division.costedInAip)}
        </td>
        <td
          className={`px-4 py-2.5 text-sm text-right font-medium tabular-nums ${
            isOver ? "text-danger-500" : "text-slate-600"
          }`}
        >
          {unassigned ? "—" : `₱${formatMoney(division.remaining)}`}
        </td>
        <td className="px-4 py-2.5 text-right">
          {/* Hidden, not disabled: a division-scoped encoder can never edit an allocation, and a
              greyed control just invites clicking. Disabled is reserved for state, not permission. */}
          {canManageAllocation && officeId != null && !unassigned && (
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
                      <td className="px-4 py-1.5 text-slate-500">{usedLabel}</td>
                      <td className="px-4 py-1.5" />
                      <td className="px-4 py-1.5 text-right text-slate-600 tabular-nums">
                        {unassigned ? "—" : `₱${formatMoney(fund.amount)}`}
                      </td>
                      <td className="px-4 py-1.5 text-right text-slate-600 tabular-nums">
                        ₱{formatMoney(fund.used)}
                      </td>
                      <td className="px-4 py-1.5 text-right text-slate-600 tabular-nums">
                        {unassigned ? "—" : `₱${formatMoney(fund.remaining)}`}
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
  noDivision = null,
  canManageAllocation,
  officeId,
  fiscalYear,
}: {
  divisions: DivisionSummary[];
  /** PPDO-150 — the server's untagged row, rendered last. Null when there is nothing untagged. */
  noDivision?: DivisionSummary | null;
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
          {noDivision && (
            <DivisionRow
              key="no-division"
              division={noDivision}
              canManageAllocation={false}
              officeId={officeId}
              fiscalYear={fiscalYear}
              unassigned
            />
          )}
        </tbody>
      </table>
    </div>
  );
}
