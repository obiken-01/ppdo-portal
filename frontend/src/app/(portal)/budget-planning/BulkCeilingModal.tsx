"use client";

import { useCallback, useEffect, useState } from "react";
import Modal from "@/components/ui/Modal";
import MoneyInput from "@/components/ui/MoneyInput";
import { getCeilings, upsertCeiling } from "@/lib/allocation";
import { formatMoney } from "@/lib/money";
import type { OfficeSummary } from "@/types";

/**
 * BulkCeilingModal — copy last fiscal year's ceilings forward (PPDO-20, ticket G).
 *
 * **The one ceiling action that gets a modal**, and the reason is the shape of the work. Setting
 * or editing ONE office's ceiling deep-links to the Allocation page instead, because a ceiling is
 * per office *per fund source*: a modal for it grows a row per fund and becomes the Allocation
 * page rebuilt in a dialog. Bulk-copying is different — it is a single complete action with no
 * page of its own.
 *
 * It must show its work before doing it. The modal lists every office it is about to create a
 * ceiling for and the amount it would carry over, each row removable and each amount editable,
 * because "copy last year" is exactly the operation where one stale figure goes through unnoticed.
 *
 * **Per fund, not per office total.** The office table's `ceilingAmount` is a sum across funds and
 * is not writable as such — `PUT /allocation/ceiling` takes one fund. So the prior year's rows are
 * fetched per office at fund granularity and each is written back individually. That is one GET
 * per office on open (issued in parallel; they are independent HTTP requests, not one DbContext)
 * and one PUT per row on confirm. Acceptable for a deliberate admin action behind a click; if the
 * province ever grows past a couple of dozen offices this wants a batch endpoint.
 */

interface CarryRow {
  officeId: number;
  officeCode: string;
  officeName: string;
  fundingSourceId: number;
  fundingSourceName: string;
  amount: number;
}

export default function BulkCeilingModal({
  offices,
  fiscalYear,
  priorFiscalYear,
  onClose,
  onApplied,
}: {
  /** Only offices WITHOUT a ceiling this year are worth carrying forward. */
  offices: OfficeSummary[];
  fiscalYear: number;
  priorFiscalYear: number;
  onClose: () => void;
  /** Called after a successful apply so the caller can refetch its band. */
  onApplied: (created: number) => void;
}) {
  const [rows, setRows] = useState<CarryRow[] | null>(null);
  const [loadError, setLoadError] = useState<string | null>(null);
  const [saving, setSaving] = useState(false);
  const [saveError, setSaveError] = useState<string | null>(null);

  const load = useCallback(() => {
    setRows(null);
    setLoadError(null);
    Promise.all(
      offices.map(async (office) => {
        const ceilings = await getCeilings(office.officeId, priorFiscalYear);
        return ceilings
          .filter((c) => c.amount > 0)
          .map<CarryRow>((c) => ({
            officeId: office.officeId,
            officeCode: office.officeCode,
            officeName: office.officeName,
            fundingSourceId: c.fundingSourceId,
            fundingSourceName: c.fundingSourceName,
            amount: c.amount,
          }));
      })
    )
      .then((perOffice) => setRows(perOffice.flat()))
      .catch(() => setLoadError(`Could not read FY ${priorFiscalYear} ceilings.`));
  }, [offices, priorFiscalYear]);

  useEffect(load, [load]);

  const apply = async () => {
    if (rows == null || rows.length === 0) return;
    setSaving(true);
    setSaveError(null);
    try {
      // Sequential, not Promise.all: each PUT is a write, and a half-applied batch is easier to
      // reason about in order than scattered. The row count here is small by construction.
      for (const row of rows) {
        await upsertCeiling({
          officeId: row.officeId,
          fiscalYear,
          fundingSourceId: row.fundingSourceId,
          amount: row.amount,
        });
      }
      onApplied(rows.length);
    } catch {
      setSaveError("Could not publish every ceiling. Some may already have been created — reopen to check.");
    } finally {
      setSaving(false);
    }
  };

  const removeRow = (index: number) =>
    setRows((current) => (current == null ? current : current.filter((_, i) => i !== index)));

  const setAmount = (index: number, amount: number) =>
    setRows((current) =>
      current == null ? current : current.map((row, i) => (i === index ? { ...row, amount } : row))
    );

  const total = (rows ?? []).reduce((sum, r) => sum + r.amount, 0);

  return (
    <Modal
      title={`Set FY ${fiscalYear} ceilings from FY ${priorFiscalYear}`}
      size="xl"
      onClose={onClose}
      footer={
        <>
          <Modal.SecondaryButton onClick={onClose}>Cancel</Modal.SecondaryButton>
          <Modal.PrimaryButton
            onClick={apply}
            loading={saving}
            disabled={rows == null || rows.length === 0}
          >
            {rows == null ? "Publish" : `Publish ${rows.length} ceiling${rows.length === 1 ? "" : "s"}`}
          </Modal.PrimaryButton>
        </>
      }
    >
      <p className="text-sm text-slate-600 mb-3">
        These offices have no FY {fiscalYear} ceiling yet. Each row below copies that office&apos;s
        FY {priorFiscalYear} figure for one fund source. Remove any row you do not want, or edit the
        amount before publishing.
      </p>

      {loadError ? (
        <div className="flex items-center gap-3">
          <p className="text-sm text-danger-500">{loadError}</p>
          <button
            type="button"
            onClick={load}
            className="px-3 py-1.5 bg-white border border-slate-200 hover:bg-slate-50 text-slate-800 text-xs font-medium transition-colors"
          >
            Retry
          </button>
        </div>
      ) : rows == null ? (
        <div className="space-y-2">
          {[0, 1, 2].map((i) => (
            <div key={i} className="h-10 bg-slate-100 animate-pulse" />
          ))}
        </div>
      ) : rows.length === 0 ? (
        <p className="text-sm text-slate-600">
          No FY {priorFiscalYear} ceilings exist for these offices, so there is nothing to carry
          forward. Set them individually from the office table instead.
        </p>
      ) : (
        <>
          <div className="overflow-x-auto border border-slate-200">
            <table className="w-full min-w-[560px]">
              <thead>
                <tr className="bg-slate-50">
                  <th className="px-3 py-2 text-left text-xs font-semibold text-slate-600 uppercase tracking-wide">
                    Office
                  </th>
                  <th className="px-3 py-2 text-left text-xs font-semibold text-slate-600 uppercase tracking-wide">
                    Fund
                  </th>
                  <th className="px-3 py-2 text-right text-xs font-semibold text-slate-600 uppercase tracking-wide">
                    Amount
                  </th>
                  <th className="px-3 py-2" />
                </tr>
              </thead>
              <tbody>
                {rows.map((row, i) => (
                  <tr key={`${row.officeId}-${row.fundingSourceId}`} className="border-t border-slate-100">
                    <td className="px-3 py-2 text-sm text-slate-600">
                      <span className="font-medium text-slate-800">{row.officeCode}</span>
                      <span className="ml-2 text-xs text-slate-500">{row.officeName}</span>
                    </td>
                    <td className="px-3 py-2 text-sm text-slate-600">{row.fundingSourceName}</td>
                    <td className="px-3 py-2">
                      <MoneyInput
                        value={row.amount}
                        onChange={(value) => setAmount(i, value ?? 0)}
                        className="w-40 text-right"
                      />
                    </td>
                    <td className="px-3 py-2 text-right">
                      <button
                        type="button"
                        onClick={() => removeRow(i)}
                        className="text-xs font-medium text-danger-500 hover:opacity-80"
                      >
                        Remove
                      </button>
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>

          <p className="text-sm text-slate-600 mt-3 text-right tabular-nums">
            Total to publish: <span className="font-semibold text-slate-800">₱{formatMoney(total)}</span>
          </p>
        </>
      )}

      {saveError && <p className="text-sm text-danger-500 mt-3">{saveError}</p>}
    </Modal>
  );
}
