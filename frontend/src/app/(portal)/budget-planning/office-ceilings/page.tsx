"use client";

/**
 * Office Ceilings — PPDO finance sets every office's ceiling for a fiscal year (PPDO-106).
 *
 * ↩️ **Moved off the Allocation page**, which set one office's ceiling at a time behind an office
 * picker. Since 2026-09-15 ceiling authority belongs to PPDO finance for *every* office
 * (`Office_Setup_Spec.md` D3), and setting twenty offices one picker-selection at a time is the
 * wrong shape for that job. Allocation still SHOWS the ceiling, read-only.
 *
 * ⚠️ **Only the General Fund tab is rendered** (D4, Ralph 2026-09-19). The per-fund tab strip is
 * built because `budget_ceilings` has been per fund since v1.4.3 and the province may carry ceilings
 * on other funds later — but the current direction is that only the GF carries one, and showing four
 * empty tabs would invite ceilings nobody asked for. Flip {@link SHOW_ALL_FUND_TABS} to reveal them;
 * everything below already works per fund.
 *
 * ⚠️ **A ceiling cut below what the office has already encoded is allowed** (A5-b, non-destructive).
 * The row turns amber and the office fails its own submit gate — the alternative, refusing the save,
 * would leave finance unable to record a cut that has actually happened.
 *
 * ⚠️ **Two reads per office, issued in parallel.** `ceilings` is the authority on the amount (per
 * fund); `ceiling-usage` is what the office has encoded against the GF, and is the *same* figure the
 * office's own submit gate uses, so the two screens cannot disagree. The office band's `costedInAip`
 * was not used for that column: it is an all-funds, PS-inclusive total and would disagree. Same
 * precedent as `BulkCeilingModal`; if the province grows past a few dozen offices, this wants one
 * batch endpoint rather than a loop (`PERFORMANCE_GUIDELINES.md`).
 */

import { useCallback, useEffect, useMemo, useState } from "react";
import Link from "next/link";
import ConfigPageHeader from "@/components/ui/ConfigPageHeader";
import DataTable, { type Column } from "@/components/ui/DataTable";
import MoneyInput from "@/components/ui/MoneyInput";
import { useToast } from "@/components/ui/Toast";
import BulkCeilingModal from "../BulkCeilingModal";
import { allocationErrorMessage, getCeilingUsage, getCeilings, upsertCeiling } from "@/lib/allocation";
import { getDashboardOffices, getFiscalYears } from "@/lib/budget-planning";
import { budgetPlanningFallback, canOpenOfficeCeilings } from "@/lib/budget-planning-access";
import { findGeneralFund, listFundingSources } from "@/lib/config";
import { useMe } from "@/lib/me-cache";
import { formatMoney } from "@/lib/money";
import type { FundingSourceResponse, OfficeSummary } from "@/types";

/** ⚠️ See the file header (D4). `true` reveals the other funds' tabs; nothing else need change. */
const SHOW_ALL_FUND_TABS = false;

interface Row {
  officeId: number;
  officeCode: string;
  officeName: string;
  /** Null = no ceiling set for this fund. ⚠️ Rendered as "Not set", never ₱0.00. */
  ceiling: number | null;
  /** What the office has encoded against the GF ceiling. Null = no AIP record for the year. */
  encoded: number | null;
  /** Unsaved edit, or undefined when the row is untouched. */
  draft?: number | null;
  saving?: boolean;
  error?: string | null;
  saved?: boolean;
}

export default function OfficeCeilingsPage() {
  const me = useMe(canOpenOfficeCeilings, budgetPlanningFallback);
  const { toast } = useToast();

  const [fiscalYear, setFiscalYear] = useState<number | null>(null);
  const [availableFiscalYears, setAvailableFiscalYears] = useState<number[]>([]);
  const [funds, setFunds] = useState<FundingSourceResponse[]>([]);
  const [fundId, setFundId] = useState<number | null>(null);
  const [rows, setRows] = useState<Row[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [bulkOpen, setBulkOpen] = useState(false);
  const [bulkNotice, setBulkNotice] = useState<string | null>(null);

  // The fund tabs, in config order. Only the GF is offered until D4 is lifted, and the GF is
  // selected outright rather than defaulting to funds[0] — config order is not a promise.
  const tabs = useMemo(
    () =>
      SHOW_ALL_FUND_TABS
        ? funds
        : funds.filter((f) => f.id === findGeneralFund(funds)?.id),
    [funds]
  );

  useEffect(() => {
    if (!me) return;
    getFiscalYears()
      .then((data) => {
        setAvailableFiscalYears(data.availableFiscalYears);
        setFiscalYear((current) => current ?? data.fiscalYear);
      })
      .catch(() => setError("Could not load fiscal years."));
    listFundingSources({ active: "true" })
      .then((all) => {
        setFunds(all);
        // Selected outright rather than defaulting to funds[0]: config order is not a promise, and
        // the General Fund is the one fund this page is about until D4 is lifted.
        setFundId(findGeneralFund(all)?.id ?? null);
      })
      .catch(() => setError("Could not load funding sources."));
  }, [me]);

  const load = useCallback(() => {
    if (fiscalYear == null || fundId == null) return;
    setLoading(true);
    setError(null);
    getDashboardOffices(fiscalYear)
      .then((offices) =>
        Promise.all(
          offices.map(async (office: OfficeSummary) => {
            const [ceilings, usage] = await Promise.all([
              getCeilings(office.officeId, fiscalYear),
              getCeilingUsage(office.officeId, fiscalYear),
            ]);
            const own = ceilings.find((c) => c.fundingSourceId === fundId);
            return {
              officeId: office.officeId,
              officeCode: office.officeCode,
              officeName: office.officeName,
              ceiling: own?.amount ?? null,
              encoded: usage?.encodedBaseRounded ?? null,
            } satisfies Row;
          })
        )
      )
      .then(setRows)
      .catch(() => setError("Could not load this year's ceilings."))
      .finally(() => setLoading(false));
  }, [fiscalYear, fundId]);

  useEffect(load, [load]);

  const patch = (officeId: number, changes: Partial<Row>) =>
    setRows((current) =>
      current.map((r) => (r.officeId === officeId ? { ...r, ...changes } : r))
    );

  const save = async (row: Row) => {
    if (fiscalYear == null || fundId == null || row.draft == null) return;
    patch(row.officeId, { saving: true, error: null, saved: false });
    try {
      const saved = await upsertCeiling({
        officeId: row.officeId,
        fiscalYear,
        fundingSourceId: fundId,
        amount: row.draft,
      });
      patch(row.officeId, {
        ceiling: saved.amount,
        draft: undefined,
        saving: false,
        saved: true,
      });
      // The office is named: this table saves one row at a time and the row that moved is not
      // necessarily the one the reader is looking at by the time the toast lands.
      toast.success("Saved", `${row.officeCode} ceiling set to ₱${formatMoney(saved.amount)}.`);
    } catch (err) {
      // Named per office: a failure on one row must not read as "nothing saved" when the rows
      // above it did save.
      const message = allocationErrorMessage(err, `Could not save ${row.officeCode}'s ceiling.`);
      // Both: the toast carries it to a reader whose eyes are elsewhere, the row keeps it visible
      // after the toast goes.
      patch(row.officeId, { saving: false, error: message });
      toast.error("Save failed", message);
    }
  };

  const columns: Column<Row>[] = [
    {
      key: "office",
      header: "Office",
      sortable: true,
      sortValue: (r) => r.officeCode,
      render: (r) => (
        <span>
          <span className="font-mono text-xs text-slate-600">{r.officeCode}</span>
          <span className="ml-2 text-slate-800">{r.officeName}</span>
        </span>
      ),
    },
    {
      key: "ceiling",
      header: "Ceiling",
      align: "right",
      sortable: true,
      sortValue: (r) => r.ceiling ?? -1,
      render: (r) => (
        <MoneyInput
          value={r.draft !== undefined ? r.draft : r.ceiling}
          onChange={(value) => patch(r.officeId, { draft: value, saved: false, error: null })}
          disabled={r.saving}
          placeholder="Not set"
          className="w-40 text-right"
        />
      ),
    },
    {
      key: "encoded",
      header: "Encoded",
      align: "right",
      sortable: true,
      sortValue: (r) => r.encoded ?? -1,
      // ⚠️ Null is absence, not zero — no AIP record for the year, or the office holds no rows in
      // it. Showing ₱0.00 would claim the office had encoded nothing.
      render: (r) => (
        <span className="tabular-nums text-slate-600">
          {r.encoded == null ? "—" : formatMoney(r.encoded)}
        </span>
      ),
    },
    {
      key: "remaining",
      header: "Remaining",
      align: "right",
      sortable: true,
      sortValue: (r) => (r.ceiling == null || r.encoded == null ? -1 : r.ceiling - r.encoded),
      render: (r) => {
        if (r.ceiling == null || r.encoded == null) return <span className="text-slate-500">—</span>;
        const remaining = r.ceiling - r.encoded;
        return (
          <span className={`tabular-nums ${remaining < 0 ? "text-amber-700 font-medium" : "text-slate-600"}`}>
            {formatMoney(remaining)}
            {remaining < 0 && <span className="ml-1 text-xs">over</span>}
          </span>
        );
      },
    },
    {
      key: "actions",
      header: "",
      align: "right",
      render: (r) => (
        <span className="flex items-center justify-end gap-2">
          {r.error && <span className="text-xs text-red-600">{r.error}</span>}
          {r.saved && !r.error && <span className="text-xs text-green-700">Saved</span>}
          <button
            type="button"
            onClick={() => save(r)}
            disabled={r.draft === undefined || r.draft == null || r.saving}
            className="px-3 py-1.5 bg-green-600 hover:bg-green-500 disabled:bg-slate-200 disabled:text-slate-500 text-white text-xs font-medium transition-colors"
          >
            {r.saving ? "Saving…" : "Save"}
          </button>
        </span>
      ),
    },
  ];

  const officesWithoutCeiling = rows.filter((r) => r.ceiling == null);

  if (!me) return null;

  return (
    <div className="p-6 space-y-4">
      <ConfigPageHeader
        title="Office Ceilings"
        description="The General Fund ceiling each office plans against, per fiscal year."
        actions={
          <div className="flex items-center gap-2">
            {/* ⚠️ `aria-label`, never an `sr-only` label: Tailwind's `sr-only` is `position:
                absolute`, and the portal shell is not `relative`, so one escaped its clipping and
                grew the document by 349px (PPDO-104). Do not reintroduce one inside the shell. */}
            <select
              aria-label="Fiscal year"
              value={fiscalYear ?? ""}
              onChange={(e) => setFiscalYear(Number(e.target.value))}
              className="border border-slate-200 px-3 py-2 text-sm text-slate-800"
            >
              {availableFiscalYears.map((year) => (
                <option key={year} value={year}>
                  FY {year}
                </option>
              ))}
            </select>
            <button
              type="button"
              onClick={() => setBulkOpen(true)}
              disabled={fiscalYear == null || officesWithoutCeiling.length === 0}
              className="px-3 py-2 border border-slate-200 text-sm text-slate-800 hover:bg-slate-50 disabled:text-slate-400 transition-colors"
            >
              Carry forward from FY {fiscalYear != null ? fiscalYear - 1 : "—"}
            </button>
          </div>
        }
      />

      {tabs.length > 1 && (
        <div className="flex gap-1 border-b border-slate-200">
          {tabs.map((fund) => (
            <button
              key={fund.id}
              type="button"
              onClick={() => setFundId(fund.id)}
              className={`px-4 py-2 text-sm ${
                fund.id === fundId
                  ? "border-b-2 border-green-600 text-slate-800 font-medium"
                  : "text-slate-600 hover:text-slate-800"
              }`}
            >
              {fund.name}
            </button>
          ))}
        </div>
      )}

      {bulkNotice && <p className="text-sm text-green-700">{bulkNotice}</p>}

      <DataTable
        columns={columns}
        rows={rows}
        rowKey={(r) => r.officeId}
        loading={loading}
        error={error}
        onRetry={load}
        emptyMessage="No offices to show for this fiscal year."
        rowNoun={["office", "offices"]}
      />

      <p className="text-xs text-slate-500">
        A ceiling below what an office has already encoded is allowed — the office is shown as over
        its ceiling and cannot submit until the two agree. The division split lives on{" "}
        <Link href="/budget-planning/allocation" className="text-green-800 underline">
          Allocation
        </Link>
        .
      </p>

      {bulkOpen && fiscalYear != null && (
        <BulkCeilingModal
          offices={officesWithoutCeiling.map((r) => ({
            officeId: r.officeId,
            officeCode: r.officeCode,
            officeName: r.officeName,
          }))}
          fiscalYear={fiscalYear}
          priorFiscalYear={fiscalYear - 1}
          onClose={() => setBulkOpen(false)}
          onApplied={(created) => {
            setBulkOpen(false);
            setBulkNotice(
              `Published ${created} ceiling${created === 1 ? "" : "s"} for FY ${fiscalYear}.`
            );
            load();
          }}
        />
      )}
    </div>
  );
}
