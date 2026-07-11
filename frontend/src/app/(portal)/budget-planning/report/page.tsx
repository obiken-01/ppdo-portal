"use client";

/**
 * Budget Planning Report page (v1.4 WFP Rework — RAL-132).
 *
 * Prelude to a full report generator: pick a report type (only "WFP" wired up for now),
 * fiscal year, and office (scoped to offices with at least a Draft WFP for that year — see
 * getWfpReportOffices), then preview a read-only layout modeled one-to-one on the province's
 * "WFP FINAL" reference sheet (WFP-Copy_NEW.xlsx) — a single continuous table:
 *   function band section -> program -> project -> activity -> expense-class subsection
 *   (PERSONAL SERVICES / MAINTENANCE AND OTHER OPERATING EXPENSES / CAPITAL OUTLAY) with a
 *   SUB-TOTAL row -> ACTIVITY GRAND TOTAL -> PROJECT GRAND TOTAL -> PROGRAM GRAND TOTAL, and
 *   each section closes with the sheet's TOTAL - PERSONAL SERVICES / MOOE (Excluding Creation)
 *   / CAPITAL OUTLAY / PERSONAL SERVICES CREATION / MOOE - CREATION / GRAND-TOTAL breakdown
 *   (Personal Services and MOOE are split by the activity's "…-CREATION" flag — RAL-126; the
 *   flag is documented as "GF, PS, position-creation only", so Capital Outlay has no creation
 *   split, matching the reference sheet).
 *
 * The sheet's six narrative columns (Resources Needed, Responsible Person/Unit, Success
 * Indicator, Means of Verification, Outcome Indicator, Target Beneficiaries) are NOT captured
 * anywhere in AipActivity today (flagged when this page shipped) — omitted here rather than
 * rendered blank; see backend/PPDO.Application/Services/WfpReportService.cs.
 *
 * Preview only — no Excel export yet (follow-up scope). The classic WFP page's per-record
 * Excel export (`downloadWfpReport`) is untouched and unrelated: it exports the OLD
 * WfpExpenditureLine model for a single (office, division) record, while this page reads the
 * NEW v1.4 WfpExpenditure model merged across every division of an office.
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
  WfpReportAmountsDto,
  WfpReportDto,
  WfpReportOfficeDto,
  WfpReportRowDto,
} from "@/types";

// ---------------------------------------------------------------------------
// Report-type dropdown — only "WFP" is wired up; the shape allows more later.
// ---------------------------------------------------------------------------

const REPORT_TYPES = [{ value: "WFP", label: "Work and Financial Plan (WFP)" }] as const;

// ---------------------------------------------------------------------------
// Flatten the nested report DTO into one row per Excel line (WFP FINAL sheet
// layout) so the whole report renders as a single continuous <table>.
// ---------------------------------------------------------------------------

type ReportRow =
  | { type: "sectionHeader"; label: string }
  | { type: "program"; refCode: string; name: string }
  | { type: "project"; refCode: string; name: string }
  | { type: "activity"; refCode: string; name: string; isCreation: boolean }
  | { type: "expenseClassLabel"; label: string }
  | { type: "expenditure"; row: WfpReportRowDto }
  | { type: "subTotal"; amounts: WfpReportAmountsDto }
  | { type: "activityGrandTotal"; refCode: string; amounts: WfpReportAmountsDto }
  | { type: "projectGrandTotal"; refCode: string; amounts: WfpReportAmountsDto }
  | { type: "programGrandTotal"; refCode: string; amounts: WfpReportAmountsDto }
  | { type: "breakdownLine"; label: string; amounts: WfpReportAmountsDto; emphasis?: boolean };

function flattenReport(report: WfpReportDto): ReportRow[] {
  const rows: ReportRow[] = [];
  for (const section of report.sections) {
    rows.push({ type: "sectionHeader", label: section.functionBandLabel });
    for (const program of section.programs) {
      rows.push({ type: "program", refCode: program.refCode, name: program.name });
      for (const project of program.projects) {
        rows.push({ type: "project", refCode: project.refCode, name: project.name });
        for (const activity of project.activities) {
          rows.push({
            type: "activity", refCode: activity.refCode, name: activity.name, isCreation: activity.isCreation,
          });
          for (const group of activity.expenseClasses) {
            rows.push({ type: "expenseClassLabel", label: group.expenseClassLabel });
            for (const row of group.rows) rows.push({ type: "expenditure", row });
            rows.push({ type: "subTotal", amounts: group.subTotal });
          }
          rows.push({ type: "activityGrandTotal", refCode: activity.refCode, amounts: activity.grandTotal });
        }
        rows.push({ type: "projectGrandTotal", refCode: project.refCode, amounts: project.grandTotal });
      }
      rows.push({ type: "programGrandTotal", refCode: program.refCode, amounts: program.grandTotal });
    }
    const b = section.breakdown;
    rows.push({ type: "breakdownLine", label: "TOTAL - PERSONAL SERVICES", amounts: b.personalServices });
    rows.push({ type: "breakdownLine", label: "TOTAL - MOOE (Excluding Creation)", amounts: b.mooeExcludingCreation });
    rows.push({ type: "breakdownLine", label: "TOTAL - CAPITAL OUTLAY", amounts: b.capitalOutlay });
    rows.push({ type: "breakdownLine", label: "TOTAL - PERSONAL SERVICES CREATION", amounts: b.personalServicesCreation });
    rows.push({ type: "breakdownLine", label: "TOTAL - MOOE - CREATION", amounts: b.mooeCreation });
    rows.push({ type: "breakdownLine", label: "GRAND-TOTAL", amounts: b.grandTotal, emphasis: true });
  }
  return rows;
}

// ---------------------------------------------------------------------------
// Table
// ---------------------------------------------------------------------------

const COLUMN_HEADERS = [
  "AIP Ref Code", "Programs, Projects and Activities", "Nature", "Account Code", "Object of Expenditure",
  "Total Appropriation", "Reserved", "Net Appropriation",
  "Q1", "Q2", "Q3", "Q4", "Amount to be Released",
];

function money(n: number) {
  return formatMoney(n);
}

/** Renders the 8 money columns (Total/Reserved/Net/Q1-4/AmountReleased) for any row type. */
function AmountsCells({ amounts, className = "" }: { amounts: WfpReportAmountsDto; className?: string }) {
  const cls = `px-2 py-1 text-right tabular-nums whitespace-nowrap ${className}`;
  return (
    <>
      <td className={cls}>{money(amounts.totalAppropriation)}</td>
      <td className={cls}>{money(amounts.reserved)}</td>
      <td className={cls}>{money(amounts.netAppropriation)}</td>
      <td className={cls}>{money(amounts.q1)}</td>
      <td className={cls}>{money(amounts.q2)}</td>
      <td className={cls}>{money(amounts.q3)}</td>
      <td className={cls}>{money(amounts.q4)}</td>
      <td className={cls}>{money(amounts.amountToBeReleased)}</td>
    </>
  );
}

function ReportTable({ report }: { report: WfpReportDto }) {
  const rows = flattenReport(report);

  return (
    <div className="overflow-x-auto border border-slate-300">
      {/* table-layout: fixed + an explicit colgroup — with 13 columns whose content length
          varies wildly by row (a bare ref code vs. a multi-hundred-character activity name),
          table-layout: auto lets different rows disagree on where column boundaries fall,
          especially once cells in different rows span different numbers of columns. Fixed
          layout forces every row onto the exact same 13-column grid regardless of content. */}
      <table className="w-full text-xs border-collapse min-w-[1500px]" style={{ tableLayout: "fixed" }}>
        <colgroup>
          <col style={{ width: "9%" }} />
          <col style={{ width: "17%" }} />
          <col style={{ width: "6%" }} />
          <col style={{ width: "8%" }} />
          <col style={{ width: "13%" }} />
          <col style={{ width: "6.5%" }} />
          <col style={{ width: "5.5%" }} />
          <col style={{ width: "6.5%" }} />
          <col style={{ width: "5.5%" }} />
          <col style={{ width: "5.5%" }} />
          <col style={{ width: "5.5%" }} />
          <col style={{ width: "5.5%" }} />
          <col style={{ width: "6.5%" }} />
        </colgroup>
        <thead>
          <tr className="bg-green-800 text-white">
            {COLUMN_HEADERS.map((h) => (
              <th key={h} className="px-2 py-2 text-left font-medium whitespace-nowrap border border-green-700">
                {h}
              </th>
            ))}
          </tr>
        </thead>
        <tbody>
          {rows.map((row, i) => {
            switch (row.type) {
              case "sectionHeader":
                return (
                  <tr key={i}>
                    <td colSpan={13} className="px-2 py-2 bg-green-700 text-white font-semibold uppercase tracking-wide border border-slate-300">
                      {row.label}
                    </td>
                  </tr>
                );
              case "program":
                return (
                  <tr key={i} className="bg-slate-100">
                    <td className="px-2 py-1.5 font-mono text-slate-500 border border-slate-200 whitespace-nowrap">{row.refCode}</td>
                    <td colSpan={12} className="px-2 py-1.5 font-semibold text-slate-800 border border-slate-200">
                      {row.name}
                    </td>
                  </tr>
                );
              case "project":
                return (
                  <tr key={i} className="bg-slate-50">
                    <td className="px-2 py-1.5 pl-4 font-mono text-slate-500 border border-slate-200 whitespace-nowrap">{row.refCode}</td>
                    <td colSpan={12} className="px-2 py-1.5 pl-4 font-medium text-slate-700 border border-slate-200">
                      {row.name}
                    </td>
                  </tr>
                );
              case "activity":
                return (
                  <tr key={i}>
                    <td className="px-2 py-1.5 pl-8 font-mono text-slate-400 border border-slate-200 whitespace-nowrap">{row.refCode}</td>
                    <td colSpan={12} className="px-2 py-1.5 pl-8 text-slate-700 border border-slate-200">
                      {row.name}
                      {row.isCreation && (
                        <span className="ml-2 px-1.5 py-0.5 text-[10px] font-medium bg-amber-100 text-amber-700 align-middle">
                          …-CREATION
                        </span>
                      )}
                    </td>
                  </tr>
                );
              case "expenseClassLabel":
                return (
                  <tr key={i}>
                    <td className="border border-slate-200" />
                    <td colSpan={3} className="px-2 py-1 pl-10 font-semibold text-slate-600 border border-slate-200">
                      {row.label}
                    </td>
                    <td colSpan={9} className="border border-slate-200" />
                  </tr>
                );
              case "expenditure":
                return (
                  <tr key={i}>
                    <td className="border border-slate-200" />
                    <td className="border border-slate-200" />
                    <td className="px-2 py-1 text-slate-600 border border-slate-200 whitespace-nowrap">{row.row.nature}</td>
                    <td className="px-2 py-1 font-mono text-slate-500 border border-slate-200 whitespace-nowrap">
                      {row.row.accountNumber ?? "—"}
                    </td>
                    <td className="px-2 py-1 text-slate-700 border border-slate-200">{row.row.accountTitle ?? "—"}</td>
                    <AmountsCells amounts={row.row.amounts} className="border border-slate-200 text-slate-700" />
                  </tr>
                );
              case "subTotal":
                return (
                  <tr key={i} className="bg-slate-50 font-medium">
                    <td className="border border-slate-200" />
                    <td colSpan={4} className="px-2 py-1 pl-10 text-slate-600 border border-slate-200">SUB-TOTAL</td>
                    <AmountsCells amounts={row.amounts} className="border border-slate-200 text-slate-700 font-semibold" />
                  </tr>
                );
              case "activityGrandTotal":
                return (
                  <tr key={i} className="bg-green-50 font-semibold text-green-800">
                    <td colSpan={5} className="px-2 py-1 pl-8 border border-slate-200">
                      ACTIVITY GRAND TOTAL — {row.refCode}
                    </td>
                    <AmountsCells amounts={row.amounts} className="border border-slate-200" />
                  </tr>
                );
              case "projectGrandTotal":
                return (
                  <tr key={i} className="bg-green-100 font-semibold text-green-800">
                    <td colSpan={5} className="px-2 py-1 pl-4 border border-slate-200">
                      PROJECT GRAND TOTAL — {row.refCode}
                    </td>
                    <AmountsCells amounts={row.amounts} className="border border-slate-200" />
                  </tr>
                );
              case "programGrandTotal":
                return (
                  <tr key={i} className="bg-green-200 font-semibold text-green-900">
                    <td colSpan={5} className="px-2 py-1 border border-slate-200">
                      PROGRAM GRAND TOTAL — {row.refCode}
                    </td>
                    <AmountsCells amounts={row.amounts} className="border border-slate-200" />
                  </tr>
                );
              case "breakdownLine":
                return (
                  <tr key={i} className={row.emphasis ? "bg-slate-800 text-white font-bold" : "bg-slate-100 font-medium text-slate-700"}>
                    <td colSpan={5} className="px-2 py-1.5 border border-slate-200">{row.label}</td>
                    <AmountsCells amounts={row.amounts} className={`border border-slate-200 ${row.emphasis ? "" : "text-slate-700"}`} />
                  </tr>
                );
            }
          })}
        </tbody>
      </table>
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
    <div className="p-6 max-w-screen-2xl mx-auto w-full">
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
                No WFP expenditures entered yet for this office under FY {report.fiscalYear}.
              </p>
            ) : (
              <ReportTable report={report} />
            )}
          </div>
        </div>
      )}
    </div>
  );
}
