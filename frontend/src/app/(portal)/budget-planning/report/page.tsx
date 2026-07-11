"use client";

/**
 * Budget Planning Report page (v1.4 WFP Rework — RAL-132).
 *
 * Prelude to a full report generator: pick a report type (only "WFP" wired up for now),
 * fiscal year, and office (scoped to offices with at least a Draft WFP for that year — see
 * getWfpReportOffices), then preview a read-only layout modeled one-to-one on the province's
 * "WFP FINAL" reference sheet (WFP-Copy_NEW.xlsx). The sheet repeats its ENTIRE header +
 * hierarchy + totals block once per fund source (a separate block for e.g. "5% GAD Fund"
 * after the "General Fund" block) rather than mixing funds into one table — this page mirrors
 * that: one full ReportTable per `report.fundSourceReports` entry. Within each fund's table:
 *   function band section -> program -> project -> activity -> expense-class subsection
 *   (PERSONAL SERVICES / MAINTENANCE AND OTHER OPERATING EXPENSES / CAPITAL OUTLAY) with a
 *   SUB-TOTAL row -> ACTIVITY GRAND TOTAL -> PROJECT GRAND TOTAL -> PROGRAM GRAND TOTAL. The
 *   fund source's whole table (every CORE/STRATEGIC/SUPPORT/UNASSIGNED section) closes ONCE
 *   with the sheet's TOTAL - PERSONAL SERVICES / MOOE (Excluding Creation) / CAPITAL OUTLAY /
 *   PERSONAL SERVICES CREATION / MOOE - CREATION / GRAND-TOTAL breakdown — not once per section
 *   (Personal Services and MOOE are split by the activity's "…-CREATION" flag — RAL-126; the
 *   flag is documented as "GF, PS, position-creation only", so Capital Outlay has no creation
 *   split, matching the reference sheet). ACTIVITY/PROJECT/PROGRAM GRAND TOTAL labels align
 *   under the "Nature" column header with their ref code in the "Account Code" column; the
 *   TOTAL - * breakdown labels align under "Account Code", matching the reference sheet.
 *
 * The sheet's SECTOR column is included (AipOffice.Sector, mapped to the sheet's exact labels
 * server-side — WfpReportService), but its five narrative columns (Resources Needed,
 * Responsible Person/Unit, Success Indicator, Means of Verification, Outcome Indicator, Target
 * Beneficiaries) are NOT captured anywhere in AipActivity today (flagged when this page
 * shipped) — omitted here rather than rendered blank.
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
  WfpReportBreakdownDto,
  WfpReportDto,
  WfpReportFundSourceDto,
  WfpReportOfficeDto,
  WfpReportRowDto,
} from "@/types";

// ---------------------------------------------------------------------------
// Report-type dropdown — only "WFP" is wired up; the shape allows more later.
// ---------------------------------------------------------------------------

const REPORT_TYPES = [{ value: "WFP", label: "Work and Financial Plan (WFP)" }] as const;

// ---------------------------------------------------------------------------
// Flatten one fund source's nested sections into one row per Excel line (WFP
// FINAL sheet layout) so each fund's block renders as a single continuous
// <table>.
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

function flattenSections(sections: WfpReportFundSourceDto["sections"]): ReportRow[] {
  const rows: ReportRow[] = [];
  for (const section of sections) {
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
  }
  return rows;
}

/** The fund source's closing summary — appears once, after every section, not once per section. */
function flattenBreakdown(b: WfpReportBreakdownDto): ReportRow[] {
  return [
    { type: "breakdownLine", label: "TOTAL - PERSONAL SERVICES", amounts: b.personalServices },
    { type: "breakdownLine", label: "TOTAL - MOOE (Excluding Creation)", amounts: b.mooeExcludingCreation },
    { type: "breakdownLine", label: "TOTAL - CAPITAL OUTLAY", amounts: b.capitalOutlay },
    { type: "breakdownLine", label: "TOTAL - PERSONAL SERVICES CREATION", amounts: b.personalServicesCreation },
    { type: "breakdownLine", label: "TOTAL - MOOE - CREATION", amounts: b.mooeCreation },
    { type: "breakdownLine", label: "GRAND-TOTAL", amounts: b.grandTotal, emphasis: true },
  ];
}

// ---------------------------------------------------------------------------
// Table — 14 columns total. table-layout: fixed + colgroup is load-bearing:
// rows range from a bare ref code to a multi-hundred-character activity name,
// and different row types span different numbers of columns — table-layout:
// auto lets the browser compute different column widths per row in that
// situation, which visibly shifts data out from under its header. Fixed
// layout pins every row to the same 14-column grid regardless of content.
// ---------------------------------------------------------------------------

const COLUMN_HEADERS = [
  "AIP Ref Code", "Programs, Projects and Activities", "Sector", "Nature", "Account Code", "Object of Expenditure",
  "Total Appropriation", "Reserved", "Net Appropriation",
  "Q1", "Q2", "Q3", "Q4", "Amount to be Released",
];

const COLUMN_WIDTHS = ["10%", "15%", "7%", "5%", "7%", "13%", "6%", "5%", "6%", "5%", "5%", "5%", "5%", "6%"];

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

/** Ref-code cell used in program/project/activity rows — wraps instead of overflowing into the next column (some ref codes run 25-30+ characters). */
function RefCodeCell({ refCode, indent }: { refCode: string; indent: number }) {
  return (
    <td
      className="px-2 py-1.5 font-mono text-slate-500 border border-slate-200 break-words align-top"
      style={{ paddingLeft: `${8 + indent * 16}px` }}
    >
      {refCode}
    </td>
  );
}

function ReportTable({ sections, breakdown }: { sections: WfpReportFundSourceDto["sections"]; breakdown: WfpReportBreakdownDto }) {
  const rows = [...flattenSections(sections), ...flattenBreakdown(breakdown)];

  return (
    <div className="border border-slate-300 max-h-[70vh] overflow-auto">
      <table className="w-full text-xs border-collapse min-w-[1600px]" style={{ tableLayout: "fixed" }}>
        <colgroup>
          {COLUMN_WIDTHS.map((w, i) => <col key={i} style={{ width: w }} />)}
        </colgroup>
        <thead className="sticky top-0 z-10">
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
                    <td colSpan={14} className="px-2 py-2 bg-green-700 text-white font-semibold uppercase tracking-wide border border-slate-300">
                      {row.label}
                    </td>
                  </tr>
                );
              case "program":
                return (
                  <tr key={i} className="bg-slate-100">
                    <RefCodeCell refCode={row.refCode} indent={0} />
                    <td colSpan={13} className="px-2 py-1.5 font-semibold text-slate-800 border border-slate-200">
                      {row.name}
                    </td>
                  </tr>
                );
              case "project":
                return (
                  <tr key={i} className="bg-slate-50">
                    <RefCodeCell refCode={row.refCode} indent={1} />
                    <td colSpan={13} className="px-2 py-1.5 pl-4 font-medium text-slate-700 border border-slate-200">
                      {row.name}
                    </td>
                  </tr>
                );
              case "activity":
                return (
                  <tr key={i}>
                    <RefCodeCell refCode={row.refCode} indent={2} />
                    <td colSpan={13} className="px-2 py-1.5 pl-8 text-slate-700 border border-slate-200">
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
                    <td colSpan={4} className="px-2 py-1 pl-10 font-semibold text-slate-600 border border-slate-200">
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
                    <td className="px-2 py-1 text-slate-600 border border-slate-200 break-words">{row.row.sector}</td>
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
                    <td colSpan={5} className="px-2 py-1 pl-10 text-slate-600 border border-slate-200">SUB-TOTAL</td>
                    <AmountsCells amounts={row.amounts} className="border border-slate-200 text-slate-700 font-semibold" />
                  </tr>
                );
              case "activityGrandTotal":
                return (
                  <tr key={i} className="bg-green-50 font-semibold text-green-800">
                    <td colSpan={3} className="border border-slate-200" />
                    <td className="px-2 py-1 border border-slate-200">ACTIVITY GRAND TOTAL</td>
                    <td className="px-2 py-1 font-mono border border-slate-200 break-words">{row.refCode}</td>
                    <td className="border border-slate-200" />
                    <AmountsCells amounts={row.amounts} className="border border-slate-200" />
                  </tr>
                );
              case "projectGrandTotal":
                return (
                  <tr key={i} className="bg-green-100 font-semibold text-green-800">
                    <td colSpan={3} className="border border-slate-200" />
                    <td className="px-2 py-1 border border-slate-200">PROJECT GRAND TOTAL</td>
                    <td className="px-2 py-1 font-mono border border-slate-200 break-words">{row.refCode}</td>
                    <td className="border border-slate-200" />
                    <AmountsCells amounts={row.amounts} className="border border-slate-200" />
                  </tr>
                );
              case "programGrandTotal":
                return (
                  <tr key={i} className="bg-green-200 font-semibold text-green-900">
                    <td colSpan={3} className="border border-slate-200" />
                    <td className="px-2 py-1 border border-slate-200">PROGRAM GRAND TOTAL</td>
                    <td className="px-2 py-1 font-mono border border-slate-200 break-words">{row.refCode}</td>
                    <td className="border border-slate-200" />
                    <AmountsCells amounts={row.amounts} className="border border-slate-200" />
                  </tr>
                );
              case "breakdownLine":
                return (
                  <tr key={i} className={row.emphasis ? "bg-slate-800 text-white font-bold" : "bg-slate-100 font-medium text-slate-700"}>
                    <td colSpan={4} className="border border-slate-200" />
                    <td colSpan={2} className="px-2 py-1.5 border border-slate-200">{row.label}</td>
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

/** One fund source's full report block — its own header (matching the sheet's per-fund "SOURCE OF FUND:" block) plus its own table. */
function FundSourceBlock({ report, fundReport }: { report: WfpReportDto; fundReport: WfpReportFundSourceDto }) {
  return (
    <div className="mb-8 last:mb-0">
      <div className="px-5 py-4 border-b border-slate-200 text-center space-y-0.5 bg-white border-x border-t">
        <p className="text-base font-bold text-slate-800">
          WORK AND FINANCIAL PLAN FY {report.fiscalYear}
        </p>
        <p className="text-sm text-slate-600">
          DEPARTMENT/OFFICE: {report.officeCode} — {report.officeName}
        </p>
        <p className="text-sm text-slate-600">SOURCE OF FUND: {fundReport.fundSourceName}</p>
        <p className="text-xs text-slate-400">
          Equiv. to {(report.reserveRate * 100).toFixed(0)}% of Operational Expenses
        </p>
      </div>
      <ReportTable sections={fundReport.sections} breakdown={fundReport.breakdown} />
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

      {/* Report — one full block per fund source (WFP FINAL sheet repeats the whole header +
          table per fund, e.g. General Fund then 5% GAD Fund) */}
      {report && !reportLoading && (
        report.fundSourceReports.length === 0 ? (
          <div className="bg-white border border-slate-200 p-6">
            <p className="text-slate-400 text-sm text-center">
              No WFP expenditures entered yet for this office under FY {report.fiscalYear}.
            </p>
          </div>
        ) : (
          report.fundSourceReports.map((fundReport) => (
            <FundSourceBlock key={fundReport.fundSourceName} report={report} fundReport={fundReport} />
          ))
        )
      )}
    </div>
  );
}
