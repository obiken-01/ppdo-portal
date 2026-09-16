"use client";

/**
 * The consolidated AIP (v1.8.0 Phase 4 — V18-55 / PPDO-73, `AIP_Review_Spec.md` §6.3a).
 *
 * ⚠️ **The grid is the province's Annex B form (`AIP_Form_Spec.md`), and it is the preview of Phase
 * 5's Excel export.** Same columns A–R, same rows, same figures. Every row and every amount arrives
 * built by the server (`AipFormRowBuilder`) — this page lays them out and nothing more. Rounding or
 * uplifting anything here would make the preview and the file disagree.
 *
 * ⚠️ **The amounts are PRINTED figures** — rounded up per figure, MOOE and CO uplifted 30% — so they
 * will not match AIP Entry or the review screen, which show exact amounts. The note under the grid
 * says so; keep it next to the grid.
 *
 * ⚠️ **Partial by design** (decision 14). Only offices with PPDO or already accepted are on the sheet;
 * the rest are left out rather than shown as ₱0, and the header and tab counts keep that legible.
 *
 * ⚠️ **Cross-office reviewers only** — `canOpenAipOfficeReview`, the same rule as the one-office review
 * screen. PPDO's own division users are host-office users and must not see this (tracker B4).
 */

import { useCallback, useEffect, useState } from "react";
import Link from "next/link";
import { useRouter, useSearchParams } from "next/navigation";
import { useMe } from "@/lib/me-cache";
import { downloadAipConsolidatedExcel, getAipConsolidated } from "@/lib/aip-review";
import { aipErrorMessage } from "@/lib/aip";
import { canOpenAipOfficeReview, budgetPlanningFallback } from "@/lib/budget-planning-access";
import { FIRST_ENTERED_FISCAL_YEAR } from "@/lib/aip-fiscal-years";
import { AIP_SECTOR_OPTIONS, AIP_SECTOR_PREFIX } from "@/lib/aipConstants";
import { AIP_WORKFLOW } from "@/lib/aip-workflow";
import { fmtThousands } from "@/lib/aip-units";
import { aipHeaderRow } from "@/components/aip/entry/AipHierarchy";
import AipActivityReviewModal from "@/components/aip/review/AipActivityReviewModal";
import type { AipConsolidatedRow, AipConsolidatedSheet, AipPrintedAmounts } from "@/types";

const YEARS = [0, 1, 2].map((n) => FIRST_ENTERED_FISCAL_YEAR + n);

const SECTOR_LABEL: Record<string, string> = {
  GENERAL: "General", SOCIAL: "Social", ECONOMIC: "Economic", OTHERS: "Others",
};

const SECTOR_TITLE: Record<string, string> = {
  GENERAL: "General Public Services",
  SOCIAL: "Social Services",
  ECONOMIC: "Economic Services",
  OTHERS: "Other Services",
};

/**
 * Column widths in screen pixels, in the proportions of the province's workbook: A, then B–E as one
 * description cell (the level shows as indent, as the four narrow columns do in the file), then F–R.
 */
const COL_WIDTHS = [150, 360, 64, 110, 78, 86, 200, 88, 84, 92, 84, 96, 84, 84, 72];
const GRID_WIDTH = COL_WIDTHS.reduce((a, b) => a + b, 0);

export default function AipConsolidatedPage() {
  const me = useMe(canOpenAipOfficeReview, budgetPlanningFallback);
  const router = useRouter();
  const searchParams = useSearchParams();

  const requestedYear = Number(searchParams.get("fiscalYear"));
  const requestedSector = (searchParams.get("sector") ?? "").toUpperCase();

  const [fiscalYear, setFiscalYear] = useState(
    YEARS.includes(requestedYear) ? requestedYear : FIRST_ENTERED_FISCAL_YEAR
  );
  const [sector, setSector] = useState<string>(
    (AIP_SECTOR_OPTIONS as readonly string[]).includes(requestedSector) ? requestedSector : "GENERAL"
  );

  const [sheet, setSheet] = useState<AipConsolidatedSheet | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [openActivityId, setOpenActivityId] = useState<number | null>(null);

  // `quiet` keeps the grid on screen — used after the modal closes, where a skeleton would throw
  // away the reader's scroll position in a sheet that can run to hundreds of rows.
  const load = useCallback(async (quiet = false) => {
    if (!quiet) setLoading(true);
    setError(null);
    try {
      setSheet(await getAipConsolidated(fiscalYear, sector));
    } catch (e) {
      setError(aipErrorMessage(e, "The consolidated AIP could not be loaded."));
    } finally {
      setLoading(false);
    }
  }, [fiscalYear, sector]);

  useEffect(() => { if (me) void load(); }, [me, load]);

  // The year and sector live in the URL, so a sheet can be linked to and survives a reload.
  useEffect(() => {
    router.replace(`/budget-planning/aip/consolidated?fiscalYear=${fiscalYear}&sector=${sector}`, { scroll: false });
  }, [router, fiscalYear, sector]);

  const sectorCount = sheet?.sectors.find((s) => s.sector === sector) ?? null;

  // PPDO-84 — the Annex B workbook, all four sectors whichever tab is open (Form Spec §14).
  const [exporting, setExporting] = useState(false);
  const [exportError, setExportError] = useState<string | null>(null);

  const exportBlockedReason = loading || !sheet
    ? "Loading the consolidated AIP…"
    : !sheet.opened
      ? `FY ${fiscalYear} has not been opened.`
      : sheet.submittedOffices === 0
        ? "No office has reached PPDO yet."
        : null;

  async function handleExport() {
    setExporting(true);
    setExportError(null);
    try {
      await downloadAipConsolidatedExcel(fiscalYear);
    } catch (e) {
      setExportError(aipErrorMessage(e, "The Excel file could not be prepared."));
    } finally {
      setExporting(false);
    }
  }

  return (
    <div className="p-4 sm:p-6">
      <div className="mb-4 flex flex-wrap items-end justify-between gap-3">
        <div>
          <Link
            href={`/budget-planning/aip/review/search?fiscalYear=${fiscalYear}`}
            className="text-xs text-slate-600 underline hover:text-slate-800"
          >
            ← AIP Review
          </Link>
          <h1 className="mt-1 text-xl font-semibold text-slate-800">Consolidated AIP</h1>
          <p className="mt-0.5 text-sm text-slate-600">
            {sheet?.opened ? (
              <>
                FY {fiscalYear} ·{" "}
                <strong className="text-slate-800">
                  {sheet.submittedOffices} of {sheet.totalOffices} offices
                </strong>{" "}
                submitted to PPDO · totals cover submitted offices only
              </>
            ) : (
              `FY ${fiscalYear} · every office's AIP as it reaches PPDO, in the province's Annex B form.`
            )}
          </p>
        </div>
        <div className="flex flex-wrap items-end gap-2">
          <div>
            <label htmlFor="consolidated-fiscal-year" className="mb-1 block text-xs font-semibold uppercase tracking-wide text-slate-600">
              Fiscal year
            </label>
            <select
              id="consolidated-fiscal-year"
              value={fiscalYear}
              onChange={(e) => setFiscalYear(Number(e.target.value))}
              className="border border-slate-300 bg-white px-3 py-1.5 text-sm text-slate-800 focus:outline-none focus:ring-1 focus:ring-green-600"
            >
              {YEARS.map((y) => <option key={y} value={y}>FY {y}</option>)}
            </select>
          </div>
          {/* Rendered disabled while the sheet loads, so the header never shifts (Form Spec §14). */}
          <button
            type="button"
            onClick={() => void handleExport()}
            disabled={exporting || exportBlockedReason != null}
            title={exportBlockedReason ?? "Downloads all four sector sheets — General, Social, Economic and Others — as the Annex B workbook."}
            className="inline-flex items-center gap-2 border border-slate-300 bg-white px-3 py-1.5 text-sm font-medium text-slate-800 transition-colors hover:bg-slate-50 focus:outline-none focus:ring-1 focus:ring-green-600 disabled:cursor-not-allowed disabled:text-slate-400 disabled:hover:bg-white"
          >
            {exporting ? (
              <span className="h-4 w-4 animate-spin rounded-full border-2 border-slate-400 border-t-transparent" aria-hidden />
            ) : (
              <svg viewBox="0 0 20 20" fill="currentColor" className="h-4 w-4" aria-hidden>
                <path d="M10 2.75a.75.75 0 0 1 .75.75v7.19l2.22-2.22a.75.75 0 1 1 1.06 1.06l-3.5 3.5a.75.75 0 0 1-1.06 0l-3.5-3.5a.75.75 0 1 1 1.06-1.06l2.22 2.22V3.5a.75.75 0 0 1 .75-.75ZM3.5 13.25a.75.75 0 0 1 .75.75v1.5c0 .14.11.25.25.25h11a.25.25 0 0 0 .25-.25V14a.75.75 0 0 1 1.5 0v1.5A1.75 1.75 0 0 1 15.5 17.25h-11A1.75 1.75 0 0 1 2.75 15.5V14a.75.75 0 0 1 .75-.75Z" />
              </svg>
            )}
            {exporting ? "Preparing…" : "Download Excel"}
          </button>
        </div>
      </div>

      {exportError && (
        <div className="mb-4 flex flex-wrap items-center justify-between gap-3 border border-red-200 bg-red-50 px-4 py-3 text-sm text-red-700">
          <span>{exportError}</span>
          <button
            type="button"
            onClick={() => setExportError(null)}
            className="border border-slate-300 bg-white px-3 py-1 text-sm font-medium text-slate-800 hover:bg-slate-50"
          >
            Dismiss
          </button>
        </div>
      )}

      {/* The four sheets of the workbook. */}
      <div role="tablist" className="mb-4 flex flex-wrap gap-1 border-b border-slate-200">
        {AIP_SECTOR_OPTIONS.map((s) => {
          const count = sheet?.sectors.find((c) => c.sector === s);
          const active = s === sector;
          return (
            <button
              key={s}
              type="button"
              role="tab"
              aria-selected={active}
              onClick={() => setSector(s)}
              className={`-mb-px inline-flex items-center gap-2 border-b-2 px-3.5 py-2 text-sm transition-colors ${
                active
                  ? "border-green-700 font-semibold text-green-800"
                  : "border-transparent font-medium text-slate-600 hover:text-slate-800"
              }`}
            >
              {SECTOR_LABEL[s]}
              {count && (
                <span className="rounded-full bg-slate-100 px-2 text-xs font-normal text-slate-600">
                  {count.submittedOffices} of {count.totalOffices}
                </span>
              )}
            </button>
          );
        })}
      </div>

      {error && (
        <div className="mb-4 flex flex-wrap items-center justify-between gap-3 border border-red-200 bg-red-50 px-4 py-3 text-sm text-red-700">
          <span>{error}</span>
          <button
            type="button"
            onClick={() => void load()}
            className="border border-slate-300 bg-white px-3 py-1 text-sm font-medium text-slate-800 hover:bg-slate-50"
          >
            Try again
          </button>
        </div>
      )}

      {loading ? (
        <GridSkeleton />
      ) : !sheet ? null : !sheet.opened ? (
        <EmptyPanel
          title={`FY ${fiscalYear} has not been opened yet`}
          body="An administrator opens the fiscal year. There is nothing to consolidate until offices start submitting."
        />
      ) : sheet.rows.length === 0 ? (
        <EmptyPanel
          title={`No ${SECTOR_LABEL[sector]} office has submitted to PPDO yet`}
          body={
            sectorCount && sectorCount.totalOffices > 0
              ? `${sectorCount.totalOffices} ${sectorCount.totalOffices === 1 ? "office in this sector is" : "offices in this sector are"} still with their own people. Their rows appear here as each one is sent to PPDO.`
              : `No office has an AIP in this sector for FY ${fiscalYear}.`
          }
        />
      ) : (
        <FormSheet sheet={sheet} onOpenActivity={setOpenActivityId} />
      )}

      {openActivityId != null && (
        <AipActivityReviewModal
          activityId={openActivityId}
          crossOffice={me?.canReviewAllOffices === true}
          readerOfficeId={me?.officeId ?? null}
          onClose={() => setOpenActivityId(null)}
          onChanged={() => void load(true)}
        />
      )}
    </div>
  );
}

// ── The sheet ───────────────────────────────────────────────────────────────

function FormSheet({
  sheet,
  onOpenActivity,
}: {
  sheet: AipConsolidatedSheet;
  onOpenActivity: (activityId: number) => void;
}) {
  const count = sheet.sectors.find((s) => s.sector === sheet.sector);
  // The form's "As of" line — the month the sheet is being read, in Manila.
  const asOf = new Date()
    .toLocaleString("en-PH", { month: "long", year: "numeric", timeZone: "Asia/Manila" })
    .toUpperCase();

  return (
    <div className="border border-slate-200 bg-white">
      {/* Preamble — rows 1–7 of the sheet (AIP_Form_Spec §2). */}
      <div className="px-4 pb-3 pt-4 text-center">
        <p className="text-xs text-slate-600">Annex B</p>
        <p className="mt-0.5 text-[15px] font-bold text-slate-800">
          ANNUAL INVESTMENT PROGRAM (AIP) FY {sheet.fiscalYear}
        </p>
        <p className="text-[13px] font-semibold text-slate-800">By Program/Project/Activity by Sector</p>
        <p className="text-[13px] font-semibold text-slate-800">As of {asOf}</p>
        <div className="mt-3 flex flex-wrap justify-between gap-2 text-left text-xs">
          <span className="font-semibold text-slate-800">
            Province/City/Municipality/Barangay: OCCIDENTAL MINDORO
          </span>
          <span className="text-slate-600">
            {sheet.sector}_FY{sheet.fiscalYear} · {AIP_SECTOR_PREFIX[sheet.sector]} {SECTOR_TITLE[sheet.sector]}
          </span>
        </div>
      </div>

      {/* The grid scrolls sideways inside its panel; the page never does. */}
      <div className="overflow-x-auto border-t border-slate-300">
        <table className="table-fixed border-collapse text-xs" style={{ width: GRID_WIDTH }}>
          <colgroup>
            {COL_WIDTHS.map((w, i) => <col key={i} style={{ width: w }} />)}
          </colgroup>
          <thead>
            <tr>
              <Th rowSpan={2} n="(1)">AIP Reference Code</Th>
              <Th rowSpan={2} n="(2)">Program/Project/Activity Description</Th>
              {/* ⚠️ No DBM number: eSRE is a provincial insertion, not an Annex B column (Form Spec §3). */}
              <Th rowSpan={2}>eSRE Code</Th>
              <Th rowSpan={2} n="(3)">Implementing Office/ Department</Th>
              <Th colSpan={2}>Schedule of Implementation</Th>
              <Th rowSpan={2} n="(6)">Expected Outputs</Th>
              <Th rowSpan={2} n="(7)">Funding Source</Th>
              {/* ⚠️ Units stated on the AMOUNT block — the FY2027 form did not (Form Spec §6 #5). */}
              <Th colSpan={4}>Amount (in thousand pesos)</Th>
              <Th colSpan={3}>Climate Change Expenditure (in thousand pesos)</Th>
            </tr>
            <tr>
              <Th n="(4)">Start Date</Th>
              <Th n="(5)">Completion Date</Th>
              <Th n="(8)">PS</Th>
              <Th n="(9)">MOOE</Th>
              <Th n="(10)">CO</Th>
              <Th n="(11) 8+9+10">Total</Th>
              <Th n="(12)">CC Adaptation</Th>
              <Th n="(13)">CC Mitigation</Th>
              <Th n="(14)">CC Typology Code</Th>
            </tr>
          </thead>
          <tbody>
            {sheet.rows.map((row, i) => (
              <FormRow key={`${row.kind}-${row.refCode}-${i}`} row={row} onOpenActivity={onOpenActivity} />
            ))}
            <tr className="bg-green-700 text-white">
              <td colSpan={8} className="border border-green-800 px-1.5 py-1.5 font-bold tracking-wide">
                TOTAL
                {count && (
                  <span className="ml-2 font-normal tracking-normal">
                    · {count.submittedOffices} of {count.totalOffices} {SECTOR_LABEL[sheet.sector]} offices submitted
                  </span>
                )}
              </td>
              <MoneyCells amounts={sheet.total} tone="total" />
              <td className="border border-green-800" />
            </tr>
          </tbody>
        </table>
      </div>

      {/* ⚠️ AIP_Form_Spec §6.2 — the printed MOOE and CO read above a ceiling by design, and the reader
          has no way to know that unless the document says so. Wording still to agree with PPDC. */}
      <p className="border-t border-slate-200 px-4 py-2.5 text-xs text-slate-600">
        Amounts in thousand pesos. Each figure is rounded up to the thousand before it is added. MOOE
        and CO include the +30% required from FY {FIRST_ENTERED_FISCAL_YEAR}, so they can read above an
        office&rsquo;s ceiling. These figures differ from AIP Entry, which shows amounts exactly as encoded.
      </p>
    </div>
  );
}

const CELL = "border border-slate-200 px-1.5 py-1 align-top text-slate-800";
const EMPTY = "border border-slate-200";

function FormRow({
  row,
  onOpenActivity,
}: {
  row: AipConsolidatedRow;
  onOpenActivity: (activityId: number) => void;
}) {
  switch (row.kind) {
    case "Office":
      return (
        <tr className={aipHeaderRow("office")}>
          <td className={`${CELL} font-mono text-[11px] font-semibold`}>{row.refCode}</td>
          <td className={`${CELL} font-bold`}>
            <span className="inline-flex flex-wrap items-center gap-1.5">
              {row.name}
              {/* Screen-only — the Excel does not carry a status. */}
              <StatusPill status={row.workflowStatus} />
            </span>
          </td>
          <EmptyCells count={6} />
          <MoneyCells amounts={row.amounts} tone="subtotal" />
          <td className={EMPTY} />
        </tr>
      );

    // ↩️ Program and project rows carried no figures until PPDO-98 — the province's form leaves those
    // cells blank. The 2026-09-15 demo asked for the subtotals WFP's report already shows. A heading
    // with nothing costed under it still comes through null, and `MoneyCells` renders that blank.
    case "Program":
      return (
        <tr className={aipHeaderRow("program")}>
          <td className={`${CELL} font-mono text-[11px]`}>{row.refCode}</td>
          <td className={`${CELL} pl-5 font-bold`}>{row.name}</td>
          <EmptyCells count={6} />
          <MoneyCells amounts={row.amounts} tone="subtotal" />
          <td className={EMPTY} />
        </tr>
      );

    case "Project":
      return (
        <tr className={aipHeaderRow("project")}>
          <td className={`${CELL} font-mono text-[11px]`}>{row.refCode}</td>
          <td className={`${CELL} pl-9 font-semibold`}>{row.name}</td>
          <EmptyCells count={6} />
          <MoneyCells amounts={row.amounts} tone="subtotal" />
          <td className={EMPTY} />
        </tr>
      );

    case "Activity":
      return (
        <tr className="bg-white">
          <td className={`${CELL} font-mono text-[11px] text-slate-600`}>{row.refCode}</td>
          <td className={`${CELL} pl-12 whitespace-pre-line`}>
            {row.activityId != null ? (
              <button
                type="button"
                onClick={() => onOpenActivity(row.activityId!)}
                className="text-left text-green-800 underline decoration-green-200 underline-offset-2 hover:decoration-green-700"
              >
                {row.name}
              </button>
            ) : (
              row.name
            )}
          </td>
          <td className={`${CELL} text-center`}>{row.esreCode}</td>
          <td className={CELL}>{row.implementingOffice}</td>
          <td className={CELL}>{row.startDate}</td>
          <td className={CELL}>{row.endDate}</td>
          <td className={CELL}>{row.expectedOutputs}</td>
          <td className={CELL}>{row.fundingSource}</td>
          <MoneyCells amounts={row.amounts} tone="line" />
          <td className={`${CELL} text-center`}>{row.ccTypologyCode}</td>
        </tr>
      );
  }
}

/**
 * Columns L–Q: PS, MOOE, CO, Total, CC adaptation, CC mitigation.
 *
 * ⚠️ `fmtThousands` and nothing else — the server already rounded and uplifted these. It renders the
 * two decimals the province's file shows, and a blank dash for zero, as the form leaves such cells.
 */
function MoneyCells({
  amounts,
  tone,
}: {
  amounts: AipPrintedAmounts | null;
  tone: "line" | "subtotal" | "total";
}) {
  const base =
    tone === "total"
      ? "border border-green-800 font-bold text-white"
      : tone === "subtotal"
        ? "border border-slate-200 font-bold text-slate-800"
        : "border border-slate-200 text-slate-800";

  const values = amounts
    ? [amounts.ps, amounts.mooe, amounts.co, amounts.total, amounts.ccAdaptation, amounts.ccMitigation]
    : [null, null, null, null, null, null];

  return (
    <>
      {values.map((v, i) => (
        <td
          key={i}
          className={`${base} whitespace-nowrap px-1.5 py-1 text-right align-top tabular-nums ${
            // Column (11) reads heavier on activity rows — the figure the office row adds up.
            tone === "line" && i === 3 ? "font-semibold" : ""
          }`}
        >
          {fmtThousands(v)}
        </td>
      ))}
    </>
  );
}

function EmptyCells({ count }: { count: number }) {
  return (
    <>
      {Array.from({ length: count }, (_, i) => <td key={i} className={EMPTY} />)}
    </>
  );
}

function StatusPill({ status }: { status: string | null }) {
  if (status === AIP_WORKFLOW.consolidated)
    return (
      <span className="rounded-full bg-green-700 px-2 py-0.5 text-[10px] font-semibold normal-case tracking-normal text-white">
        Accepted
      </span>
    );
  if (status === AIP_WORKFLOW.submittedToPpdo)
    return (
      <span className="rounded-full bg-amber-100 px-2 py-0.5 text-[10px] font-semibold normal-case tracking-normal text-amber-900">
        With PPDO
      </span>
    );
  return null;
}

function Th({
  children,
  n,
  rowSpan,
  colSpan,
}: {
  children: React.ReactNode;
  /** The DBM column number printed on the form itself. */
  n?: string;
  rowSpan?: number;
  colSpan?: number;
}) {
  return (
    <th
      rowSpan={rowSpan}
      colSpan={colSpan}
      className="border border-slate-300 bg-slate-100 px-1 py-1.5 text-center align-middle text-[10px] font-bold uppercase leading-tight tracking-wide text-slate-600"
    >
      {children}
      {n && <span className="mt-0.5 block font-normal normal-case">{n}</span>}
    </th>
  );
}

/** Shaped like the sheet — preamble, header band, rows — so nothing jumps when it lands (CLS). */
function GridSkeleton() {
  return (
    <div className="border border-slate-200 bg-white" aria-hidden>
      <div className="flex flex-col items-center gap-1.5 px-4 py-4">
        <div className="h-3 w-16 animate-pulse bg-slate-200" />
        <div className="h-4 w-80 animate-pulse bg-slate-200" />
        <div className="h-3 w-56 animate-pulse bg-slate-200" />
      </div>
      <div className="h-11 border-y border-slate-300 bg-slate-100" />
      {Array.from({ length: 10 }, (_, i) => (
        <div key={i} className="grid grid-cols-[150px_minmax(0,1fr)_96px_96px_96px] gap-4 border-b border-slate-200 px-3 py-2.5">
          <div className="h-2.5 animate-pulse bg-slate-200" />
          <div className="h-2.5 w-2/3 animate-pulse bg-slate-200" />
          <div className="h-2.5 animate-pulse bg-slate-200" />
          <div className="h-2.5 animate-pulse bg-slate-200" />
          <div className="h-2.5 animate-pulse bg-slate-200" />
        </div>
      ))}
    </div>
  );
}

function EmptyPanel({ title, body }: { title: string; body: string }) {
  return (
    <div className="border border-slate-200 bg-white px-6 py-10 text-center">
      <h2 className="text-sm font-semibold text-slate-800">{title}</h2>
      <p className="mx-auto mt-2 max-w-xl text-sm text-slate-600">{body}</p>
    </div>
  );
}
