"use client";

/**
 * Budget Planning Report page (v1.4 WFP Rework — RAL-132).
 *
 * Prelude to a full report generator: pick a report type (only "WFP" wired up for now),
 * fiscal year, and office (scoped to offices with at least a Draft WFP for that year — see
 * getWfpReportOffices), then preview a read-only layout modeled on the province's "WFP FINAL"
 * reference sheet (function band -> program -> project -> activity -> expense-class
 * subsections with sub-totals -> activity grand total).
 *
 * Preview only — no Excel export yet (that's follow-up scope once this ships). The classic
 * WFP page's per-record Excel export (`downloadWfpReport`) is untouched and unrelated: it
 * exports the OLD WfpExpenditureLine model for a single (office, division) record, while this
 * page reads the NEW v1.4 WfpExpenditure model merged across every division of an office.
 *
 * API endpoints (RAL-132, { data, error, message } envelope):
 *   GET /api/budget-planning/wfp/report/offices?fiscalYear=
 *   GET /api/budget-planning/wfp/report/preview?officeId=&fiscalYear=
 *
 * Access: canAccessBudgetPlanning, same as the rest of Budget Planning.
 */

import { useEffect, useState } from "react";
import { useMe } from "@/lib/me-cache";
import { getDashboard } from "@/lib/budget-planning";
import { getWfpReportOffices, getWfpReportPreview, wfpErrorMessage } from "@/lib/wfp";
import { useToast } from "@/components/ui/Toast";
import { formatMoney } from "@/lib/money";
import type {
  WfpReportActivityDto,
  WfpReportAmountsDto,
  WfpReportDto,
  WfpReportExpenseClassGroupDto,
  WfpReportOfficeDto,
} from "@/types";

// ---------------------------------------------------------------------------
// Report-type dropdown — only "WFP" is wired up; the shape allows more later.
// ---------------------------------------------------------------------------

const REPORT_TYPES = [{ value: "WFP", label: "Work and Financial Plan (WFP)" }] as const;

// ---------------------------------------------------------------------------
// Amounts row — shared by expenditure rows, sub-totals, and grand totals
// ---------------------------------------------------------------------------

function AmountsCells({ amounts, emphasis }: { amounts: WfpReportAmountsDto; emphasis?: boolean }) {
  const cls = emphasis ? "font-semibold text-slate-800" : "text-slate-700";
  return (
    <>
      <td className={`px-2 py-1.5 text-right tabular-nums ${cls}`}>{formatMoney(amounts.totalAppropriation)}</td>
      <td className={`px-2 py-1.5 text-right tabular-nums ${cls}`}>{formatMoney(amounts.reserved)}</td>
      <td className={`px-2 py-1.5 text-right tabular-nums ${cls}`}>{formatMoney(amounts.netAppropriation)}</td>
      <td className={`px-2 py-1.5 text-right tabular-nums ${cls}`}>{formatMoney(amounts.q1)}</td>
      <td className={`px-2 py-1.5 text-right tabular-nums ${cls}`}>{formatMoney(amounts.q2)}</td>
      <td className={`px-2 py-1.5 text-right tabular-nums ${cls}`}>{formatMoney(amounts.q3)}</td>
      <td className={`px-2 py-1.5 text-right tabular-nums ${cls}`}>{formatMoney(amounts.q4)}</td>
      <td className={`px-2 py-1.5 text-right tabular-nums ${cls}`}>{formatMoney(amounts.amountToBeReleased)}</td>
    </>
  );
}

const COLUMN_HEADERS = [
  "Nature", "Account Code", "Object of Expenditure",
  "Total Appropriation", "Reserved", "Net Appropriation",
  "Q1", "Q2", "Q3", "Q4", "Amount to be Released",
];

function ExpenseClassTable({ group }: { group: WfpReportExpenseClassGroupDto }) {
  return (
    <div className="mb-3">
      <p className="text-xs font-semibold text-slate-600 uppercase tracking-wide mb-1">
        {group.expenseClassLabel}
      </p>
      <table className="w-full text-xs border border-slate-200">
        <thead>
          <tr className="bg-slate-50 text-slate-500 text-left border-b border-slate-200">
            {COLUMN_HEADERS.map((h) => (
              <th key={h} className={`px-2 py-1.5 font-medium ${h === "Nature" || h === "Account Code" || h === "Object of Expenditure" ? "" : "text-right"}`}>
                {h}
              </th>
            ))}
          </tr>
        </thead>
        <tbody className="divide-y divide-slate-100">
          {group.rows.map((row, i) => (
            <tr key={i}>
              <td className="px-2 py-1.5 text-slate-600">{row.nature}</td>
              <td className="px-2 py-1.5 font-mono text-slate-500">{row.accountNumber ?? "—"}</td>
              <td className="px-2 py-1.5 text-slate-700">{row.accountTitle ?? "—"}</td>
              <AmountsCells amounts={row.amounts} />
            </tr>
          ))}
          <tr className="bg-slate-50 font-medium">
            <td className="px-2 py-1.5 text-slate-600" colSpan={3}>SUB-TOTAL</td>
            <AmountsCells amounts={group.subTotal} emphasis />
          </tr>
        </tbody>
      </table>
    </div>
  );
}

function ActivityBlock({ activity }: { activity: WfpReportActivityDto }) {
  return (
    <div className="mb-4 pl-4 border-l-2 border-green-200">
      <div className="flex items-center gap-2 mb-1.5">
        <span className="font-mono text-xs text-slate-400">{activity.refCode}</span>
        <span className="text-sm font-medium text-slate-700">{activity.name}</span>
        {activity.isCreation && (
          <span className="px-1.5 py-0.5 text-[10px] font-medium bg-amber-100 text-amber-700">
            …-CREATION
          </span>
        )}
      </div>

      {activity.expenseClasses.length === 0 ? (
        <p className="text-xs text-slate-400 italic mb-3">No WFP expenditures entered yet for this activity.</p>
      ) : (
        <>
          {activity.expenseClasses.map((group) => (
            <ExpenseClassTable key={group.expenseClass} group={group} />
          ))}
          <table className="w-full text-xs border border-slate-300 bg-green-50">
            <tbody>
              <tr className="font-semibold text-green-800">
                <td className="px-2 py-1.5" colSpan={3}>ACTIVITY GRAND TOTAL</td>
                <AmountsCells amounts={activity.grandTotal} emphasis />
              </tr>
            </tbody>
          </table>
        </>
      )}
    </div>
  );
}

// ---------------------------------------------------------------------------
// Page
// ---------------------------------------------------------------------------

export default function WfpReportPage() {
  useMe((m) => m.canAccessBudgetPlanning);
  const { toast } = useToast();

  const [reportType, setReportType] = useState<string>("WFP");
  const [fiscalYear, setFiscalYear] = useState<number | null>(null);
  const [availableFiscalYears, setAvailableFiscalYears] = useState<number[]>([]);
  const [officeId, setOfficeId] = useState<number | null>(null);
  const [offices, setOffices] = useState<WfpReportOfficeDto[]>([]);
  const [officesLoading, setOfficesLoading] = useState(false);

  const [report, setReport] = useState<WfpReportDto | null>(null);
  const [reportLoading, setReportLoading] = useState(false);

  // ── Fiscal years — reuse the Dashboard's list, no separate endpoint ────────

  useEffect(() => {
    getDashboard()
      .then((d) => {
        setAvailableFiscalYears(d.availableFiscalYears);
        setFiscalYear(d.fiscalYear);
      })
      .catch(() => toast.error("Load failed", "Could not load fiscal years."));
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, []);

  // ── Offices scoped to the selected fiscal year ──────────────────────────────

  useEffect(() => {
    if (fiscalYear == null) return;
    setOfficeId(null);
    setReport(null);
    setOfficesLoading(true);
    getWfpReportOffices(fiscalYear)
      .then(setOffices)
      .catch(() => toast.error("Load failed", "Could not load offices for this fiscal year."))
      .finally(() => setOfficesLoading(false));
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [fiscalYear]);

  function handleGeneratePreview() {
    if (officeId == null || fiscalYear == null) return;
    setReportLoading(true);
    setReport(null);
    getWfpReportPreview(officeId, fiscalYear)
      .then(setReport)
      .catch((err) => toast.error("Could not generate preview", wfpErrorMessage(err, "Please try again.")))
      .finally(() => setReportLoading(false));
  }

  return (
    <div className="p-6 max-w-screen-xl mx-auto w-full">
      {/* Header */}
      <div className="mb-5">
        <h1 className="text-xl font-bold text-slate-800">Report</h1>
        <p className="text-sm text-slate-500 mt-0.5">
          Select a report type, fiscal year, and office to preview.
        </p>
      </div>

      {/* Selector row */}
      <div className="flex flex-wrap items-end gap-3 mb-6 px-4 py-3 bg-white border border-slate-200">
        <div>
          <label className="block text-xs font-medium text-slate-500 uppercase tracking-wide mb-1">
            Report Type
          </label>
          <select
            value={reportType}
            onChange={(e) => setReportType(e.target.value)}
            className="border border-slate-300 bg-white text-sm px-2 py-1.5 text-slate-700 focus:outline-none focus:ring-1 focus:ring-green-600"
          >
            {REPORT_TYPES.map((t) => (
              <option key={t.value} value={t.value}>{t.label}</option>
            ))}
          </select>
        </div>

        <div>
          <label className="block text-xs font-medium text-slate-500 uppercase tracking-wide mb-1">
            Fiscal Year
          </label>
          <select
            value={fiscalYear ?? ""}
            onChange={(e) => setFiscalYear(e.target.value ? Number(e.target.value) : null)}
            className="border border-slate-300 bg-white text-sm px-2 py-1.5 text-slate-700 focus:outline-none focus:ring-1 focus:ring-green-600"
          >
            {availableFiscalYears.length === 0 && fiscalYear != null && (
              <option value={fiscalYear}>FY {fiscalYear}</option>
            )}
            {availableFiscalYears.map((fy) => (
              <option key={fy} value={fy}>FY {fy}</option>
            ))}
          </select>
        </div>

        <div>
          <label className="block text-xs font-medium text-slate-500 uppercase tracking-wide mb-1">
            Office
          </label>
          <select
            value={officeId ?? ""}
            onChange={(e) => setOfficeId(e.target.value ? Number(e.target.value) : null)}
            disabled={officesLoading || offices.length === 0}
            className="w-64 border border-slate-300 bg-white text-sm px-2 py-1.5 text-slate-700 focus:outline-none focus:ring-1 focus:ring-green-600 disabled:opacity-50"
          >
            <option value="">
              {officesLoading ? "Loading offices…" : offices.length === 0 ? "No offices with a WFP yet" : "— select office —"}
            </option>
            {offices.map((o) => (
              <option key={o.officeId} value={o.officeId}>
                {o.officeCode} — {o.officeName} ({o.wfpStatus})
              </option>
            ))}
          </select>
        </div>

        <button
          onClick={handleGeneratePreview}
          disabled={officeId == null || reportLoading}
          className="px-4 py-1.5 text-sm font-medium bg-green-600 text-white hover:bg-green-500 disabled:opacity-50 disabled:cursor-not-allowed"
        >
          {reportLoading ? "Generating…" : "Generate Preview"}
        </button>
      </div>

      {/* Empty / loading state */}
      {!report && !reportLoading && (
        <p className="text-slate-400 text-sm py-10 text-center">
          Select an office and click &quot;Generate Preview&quot; to view its WFP report.
        </p>
      )}
      {reportLoading && (
        <div className="flex items-center justify-center gap-2 text-slate-500 text-sm py-10">
          <span className="w-4 h-4 border-2 border-slate-300 border-t-green-600 rounded-full animate-spin" />
          Generating preview…
        </div>
      )}

      {/* Report */}
      {report && !reportLoading && (
        <div className="bg-white border border-slate-200">
          {/* Report header block — mirrors the WFP FINAL sheet's header */}
          <div className="px-5 py-4 border-b border-slate-200 text-center space-y-0.5">
            <p className="text-base font-bold text-slate-800">
              WORK AND FINANCIAL PLAN FY {report.fiscalYear}
            </p>
            <p className="text-sm text-slate-600">
              DEPARTMENT/OFFICE: {report.officeCode} — {report.officeName}
            </p>
            <p className="text-sm text-slate-600">SOURCE OF FUND: GENERAL FUND</p>
            <p className="text-xs text-slate-400">
              Equiv. to {(report.reserveRate * 100).toFixed(0)}% of Operational Expenses
            </p>
          </div>

          <div className="p-5">
            {report.sections.length === 0 ? (
              <p className="text-slate-400 text-sm text-center py-6">
                No AIP programs found for this office under FY {report.fiscalYear}.
              </p>
            ) : (
              report.sections.map((section) => (
                <div key={section.functionBand} className="mb-6">
                  <div className="px-3 py-2 bg-green-700 text-white text-sm font-semibold tracking-wide uppercase mb-3">
                    {section.functionBandLabel}
                  </div>
                  {section.programs.map((program) => (
                    <div key={program.refCode} className="mb-4">
                      <div className="flex items-center gap-2 mb-2">
                        <span className="font-mono text-xs text-slate-400">{program.refCode}</span>
                        <span className="text-sm font-semibold text-slate-800">{program.name}</span>
                      </div>
                      {program.projects.map((project) => (
                        <div key={project.refCode} className="mb-3 pl-3 border-l-2 border-slate-200">
                          <div className="flex items-center gap-2 mb-2">
                            <span className="font-mono text-xs text-slate-400">{project.refCode}</span>
                            <span className="text-sm font-medium text-slate-700">{project.name}</span>
                          </div>
                          {project.activities.map((activity) => (
                            <ActivityBlock key={activity.refCode} activity={activity} />
                          ))}
                        </div>
                      ))}
                    </div>
                  ))}
                </div>
              ))
            )}
          </div>
        </div>
      )}
    </div>
  );
}
