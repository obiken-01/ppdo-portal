"use client";

/**
 * PPMP report preview rendering (v1.5 — RAL-183). Item-grained, one table per fund source —
 * follows the province's own `ppmp admin2027.xlsx` layout (docs/v1.5/PPMP_Report_Findings.md §8):
 * Program → Project → Activity → Account render as interleaved section rows above their
 * procurement item rows; each account row carries its section total (= sum of its items, Q11);
 * a fund grand-total closes each block. Mode of Procurement column is deliberately absent (Q12).
 *
 * Preview only — no Excel export here (that's RAL-184). Kept in its own module so the shared
 * report page (selectors, scoping, WFP rendering) stays readable.
 */

import { formatMoney } from "@/lib/money";
import type { PpmpReportDto, PpmpReportFundSourceDto, PpmpReportItemDto } from "@/types";

// 18 columns — matches the reference form minus Mode of Procurement. table-layout:fixed + a
// colgroup keeps every row on the same grid regardless of which columns a given row type fills.
const COLUMN_HEADERS = [
  "Item No.", "AIP Reference Code", "Description", "Stock Card No.", "Short Category",
  "Unit", "Unit Price", "Qty", "Est. Budget",
  "1st Qty", "1st Amount", "2nd Qty", "2nd Amount", "3rd Qty", "3rd Amount", "4th Qty", "4th Amount",
  "Total",
];
const COLUMN_WIDTHS = [
  "4%", "13%", "18%", "8%", "8%", "4%", "6%", "4%", "7%",
  "3%", "5%", "3%", "5%", "3%", "5%", "3%", "5%", "6%",
];

// Flatten one fund source's nested hierarchy into one row per rendered line, so each fund's block
// is a single continuous <table> (same approach the WFP report uses).
type PpmpRow =
  | { type: "program"; refCode: string; name: string }
  | { type: "project"; refCode: string; name: string }
  | { type: "activity"; refCode: string; name: string }
  | { type: "account"; accountNumber: string | null; accountTitle: string | null; total: number }
  | { type: "item"; item: PpmpReportItemDto }
  | { type: "fundTotal"; label: string; total: number };

function flattenFund(fund: PpmpReportFundSourceDto): PpmpRow[] {
  const rows: PpmpRow[] = [];
  let itemNo = 0;
  for (const program of fund.programs) {
    rows.push({ type: "program", refCode: program.refCode, name: program.name });
    for (const project of program.projects) {
      rows.push({ type: "project", refCode: project.refCode, name: project.name });
      for (const activity of project.activities) {
        rows.push({ type: "activity", refCode: activity.refCode, name: activity.name });
        for (const account of activity.accounts) {
          rows.push({
            type: "account",
            accountNumber: account.accountNumber,
            accountTitle: account.accountTitle,
            total: account.total,
          });
          for (const item of account.items) rows.push({ type: "item", item });
          itemNo += account.items.length;
        }
      }
    }
  }
  rows.push({ type: "fundTotal", label: `TOTAL — ${fund.fundSourceName}`, total: fund.total });
  void itemNo; // Item No. is emitted per row inside the render pass, not precomputed here
  return rows;
}

function money(n: number) {
  return formatMoney(n);
}

/** Renders the qty (plain) and amount (money) cells for one quarter pair. */
function num(n: number) {
  return n === 0 ? "—" : n.toLocaleString("en-PH", { maximumFractionDigits: 2 });
}

function FundBlock({
  report, fund, printPageBreakAfter,
}: {
  report: PpmpReportDto; fund: PpmpReportFundSourceDto; printPageBreakAfter: boolean;
}) {
  const rows = flattenFund(fund);
  let itemNo = 0;

  const endUser = report.divisionName
    ? `${report.officeName} — ${report.divisionName}`
    : report.officeName;

  return (
    <div className={`mb-8 last:mb-0 ${printPageBreakAfter ? "print:break-after-page" : ""}`}>
      <div className="h-1.5 bg-green-800 border-x border-t border-slate-200" />
      <div className="px-5 py-4 border-b border-slate-200 text-center space-y-0.5 bg-white border-x">
        <p className="text-base font-bold text-green-800">
          PROJECT PROCUREMENT MANAGEMENT PLAN (PPMP) — FY {report.fiscalYear}
        </p>
        <p className="text-sm text-slate-600">END-USER/UNIT: {endUser}</p>
        <p className="text-sm text-slate-600">Charged to: {fund.fundSourceName}</p>
      </div>

      <div className="border border-slate-300 print:border-0 overflow-x-auto">
        <table
          className="w-full text-xs border-collapse min-w-[1700px] print:min-w-0 print:text-[7px]"
          style={{ tableLayout: "fixed" }}
        >
          <colgroup>
            {COLUMN_WIDTHS.map((w, i) => <col key={i} style={{ width: w }} />)}
          </colgroup>
          <thead className="sticky top-0 z-10 print:static">
            <tr className="bg-green-800 text-white">
              {COLUMN_HEADERS.map((h) => (
                <th key={h} className="px-2 py-2 text-left font-medium break-words border border-green-700 align-bottom">
                  {h}
                </th>
              ))}
            </tr>
          </thead>
          <tbody>
            {rows.map((row, i) => {
              switch (row.type) {
                case "program":
                  return (
                    <tr key={i} className="bg-slate-100">
                      <td className="px-2 py-1.5 font-mono text-slate-600 border border-slate-200 break-words align-top">
                        {row.refCode}
                      </td>
                      <td colSpan={17} className="px-2 py-1.5 font-semibold text-slate-800 border border-slate-200">
                        {row.name}
                      </td>
                    </tr>
                  );
                case "project":
                  return (
                    <tr key={i} className="bg-slate-50">
                      <td className="px-2 py-1.5 font-mono text-slate-600 border border-slate-200 break-words align-top">
                        {row.refCode}
                      </td>
                      <td colSpan={17} className="px-2 py-1.5 pl-4 font-medium text-slate-700 border border-slate-200">
                        {row.name}
                      </td>
                    </tr>
                  );
                case "activity":
                  return (
                    <tr key={i}>
                      <td className="px-2 py-1.5 font-mono text-slate-600 border border-slate-200 break-words align-top">
                        {row.refCode}
                      </td>
                      <td colSpan={17} className="px-2 py-1.5 pl-8 text-slate-700 border border-slate-200">
                        {row.name}
                      </td>
                    </tr>
                  );
                case "account":
                  return (
                    <tr key={i} className="bg-green-50 font-medium text-green-900">
                      <td className="border border-slate-200" />
                      <td colSpan={7} className="px-2 py-1 pl-10 border border-slate-200">
                        {row.accountNumber ? `${row.accountNumber} — ` : ""}{row.accountTitle ?? "—"}
                      </td>
                      <td className="px-2 py-1 text-right tabular-nums whitespace-nowrap border border-slate-200">
                        {money(row.total)}
                      </td>
                      <td colSpan={9} className="border border-slate-200" />
                    </tr>
                  );
                case "item": {
                  const it = row.item;
                  itemNo += 1;
                  const cell = "px-2 py-1 border border-slate-200 text-slate-700";
                  const rightCell = `${cell} text-right tabular-nums whitespace-nowrap`;
                  return (
                    <tr key={i}>
                      <td className={`${cell} text-center`}>{itemNo}</td>
                      <td className="border border-slate-200" />
                      <td className={`${cell} pl-12`}>{it.description}</td>
                      <td className={`${cell} font-mono text-xs`}>{it.stockCardNo ?? "—"}</td>
                      <td className={cell}>{it.category ?? "—"}</td>
                      <td className={cell}>{it.unit}</td>
                      <td className={rightCell}>{money(it.unitPrice)}</td>
                      <td className={rightCell}>{num(it.qty)}</td>
                      <td className={rightCell}>{money(it.estBudget)}</td>
                      <td className={rightCell}>{num(it.q1Qty)}</td>
                      <td className={rightCell}>{money(it.q1Amount)}</td>
                      <td className={rightCell}>{num(it.q2Qty)}</td>
                      <td className={rightCell}>{money(it.q2Amount)}</td>
                      <td className={rightCell}>{num(it.q3Qty)}</td>
                      <td className={rightCell}>{money(it.q3Amount)}</td>
                      <td className={rightCell}>{num(it.q4Qty)}</td>
                      <td className={rightCell}>{money(it.q4Amount)}</td>
                      <td className={rightCell}>{money(it.estBudget)}</td>
                    </tr>
                  );
                }
                case "fundTotal":
                  return (
                    <tr key={i} className="bg-slate-800 text-white font-bold print:break-inside-avoid">
                      <td colSpan={8} className="px-2 py-1.5 border border-slate-200">{row.label}</td>
                      <td className="px-2 py-1.5 text-right tabular-nums whitespace-nowrap border border-slate-200">
                        {money(row.total)}
                      </td>
                      <td colSpan={8} className="border border-slate-200" />
                      <td className="px-2 py-1.5 text-right tabular-nums whitespace-nowrap border border-slate-200">
                        {money(row.total)}
                      </td>
                    </tr>
                  );
              }
            })}
          </tbody>
        </table>
      </div>
    </div>
  );
}

export default function PpmpReportView({ report }: { report: PpmpReportDto }) {
  if (report.fundSourceReports.length === 0) {
    return (
      <div className="bg-white border border-slate-200 p-6">
        <p className="text-slate-600 text-sm text-center">
          No procurement items entered yet for this office under FY {report.fiscalYear}.
        </p>
      </div>
    );
  }
  return (
    <>
      {report.fundSourceReports.map((fund, i) => (
        <FundBlock
          key={fund.fundSourceName}
          report={report}
          fund={fund}
          printPageBreakAfter={i < report.fundSourceReports.length - 1}
        />
      ))}
    </>
  );
}
