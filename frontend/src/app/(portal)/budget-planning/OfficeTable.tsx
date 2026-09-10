"use client";

import Link from "next/link";
import StatusPill from "@/components/ui/StatusPill";
import { formatMoney } from "@/lib/money";
import type { OfficeSummary } from "@/types";

/**
 * OfficeTable — one row per office in the caller's cross-office scope (PPDO-20, tickets F and G).
 *
 * Two audiences, one table, and the difference is a single prop:
 *
 *   `canSetCeiling` false — a cross-office REVIEWER. Read-only. **No write control appears
 *      anywhere in the table**, not a greyed one: their grant is read scope, and a disabled Edit
 *      button on every row of every office says the opposite.
 *   `canSetCeiling` true — the PBO ceiling officer, whose grant is authority over any office's
 *      ceiling (RAL-243).
 *
 * **Ceiling actions deep-link; they do not open a modal.** A ceiling is per office *per fund
 * source*, so a modal grows a row per fund and becomes the Allocation page rebuilt in a dialog.
 * `Set ceiling` / `Edit` navigate to the Allocation page with the office preselected, reusing the
 * office picker PPDO-17 shipped. The one exception is bulk carry-forward — see `BulkCeilingModal`.
 *
 * `Export` is deliberately absent. The wireframe drew one; the spec's §7 puts it out of scope.
 */

const TH =
  "px-4 py-2.5 text-xs font-semibold text-slate-600 uppercase tracking-wide whitespace-nowrap";

export default function OfficeTable({
  offices,
  fiscalYear,
  canSetCeiling,
}: {
  offices: OfficeSummary[];
  fiscalYear: number | null;
  canSetCeiling: boolean;
}) {
  const allocationHref = (officeId: number) =>
    `/budget-planning/allocation?officeId=${officeId}${
      fiscalYear != null ? `&fiscalYear=${fiscalYear}` : ""
    }`;

  return (
    <div className="overflow-x-auto">
      <table className="w-full min-w-[900px]">
        <thead>
          <tr className="bg-slate-50">
            <th className={`${TH} text-left`}>Office</th>
            <th className={`${TH} text-left`}>AIP</th>
            <th className={`${TH} text-left`}>Submission</th>
            <th className={`${TH} text-right`}>Ceiling</th>
            <th className={`${TH} text-right`}>Costed in AIP</th>
            <th className={`${TH} text-right`}>Activities</th>
            <th className={`${TH} text-left`}>Reviewer</th>
            {canSetCeiling && <th className={`${TH} text-right`}>Ceiling</th>}
          </tr>
        </thead>
        <tbody>
          {offices.map((office) => (
            <tr key={office.officeId} className="border-t border-slate-100 hover:bg-slate-50">
              <td className="px-4 py-2.5 text-sm">
                <span className="font-medium text-slate-800">{office.officeCode}</span>
                {office.isHostOffice && (
                  <span className="ml-2 px-1.5 py-0.5 rounded-full bg-green-100 text-green-700 text-[10px] font-semibold uppercase tracking-wide">
                    Host
                  </span>
                )}
                <span className="block text-xs text-slate-500 truncate max-w-[220px]">
                  {office.officeName}
                </span>
              </td>

              <td className="px-4 py-2.5">
                <div className="flex flex-wrap items-center gap-1">
                  <StatusPill stage={office.aipStatus} />
                  {office.isOverCeiling && <StatusPill risk="Over ceiling" />}
                </div>
              </td>

              <td className="px-4 py-2.5">
                <div className="flex flex-wrap items-center gap-1">
                  <StatusPill stage={office.submissionStatus} />
                  {/* No reviewer means nobody in that office can submit at all — a blocking fact
                      about the office's setup, not a blank cell in the Reviewer column. */}
                  {office.reviewerName == null && <StatusPill risk="Cannot submit" />}
                </div>
              </td>

              <td className="px-4 py-2.5 text-sm text-right tabular-nums">
                {/* Null is "not published", which is not the same as ₱0.00 and must not read as it. */}
                {office.ceilingAmount == null ? (
                  <span className="text-slate-500">Not published</span>
                ) : (
                  <span className="text-slate-600">₱{formatMoney(office.ceilingAmount)}</span>
                )}
              </td>

              <td
                className={`px-4 py-2.5 text-sm text-right tabular-nums ${
                  office.isOverCeiling ? "text-danger-500 font-medium" : "text-slate-600"
                }`}
              >
                ₱{formatMoney(office.costedInAip)}
              </td>

              <td className="px-4 py-2.5 text-sm text-right text-slate-600 tabular-nums">
                {office.activityCount.toLocaleString("en-PH")}
              </td>

              <td className="px-4 py-2.5 text-sm text-slate-600">
                {office.reviewerName ?? <span className="text-slate-500">None — assign</span>}
              </td>

              {canSetCeiling && (
                <td className="px-4 py-2.5 text-right">
                  <Link
                    href={allocationHref(office.officeId)}
                    className="text-xs font-medium text-green-600 hover:text-green-700 whitespace-nowrap"
                  >
                    {office.ceilingAmount == null ? "Set ceiling →" : "Edit →"}
                  </Link>
                </td>
              )}
            </tr>
          ))}
        </tbody>
      </table>
    </div>
  );
}
