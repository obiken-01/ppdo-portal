"use client";

/**
 * Extracted verbatim from `aip/detail/page.tsx` (PPDO-64). No behaviour change.
 */

import AipMoneyInput from "@/components/aip/AipMoneyInput";
import { useState } from "react";
import { aipErrorMessage, deleteAipActivity, updateAipActivity } from "@/lib/aip";
import { fmtPesos } from "@/lib/aip-units";
import { AIP_ESRE_OPTIONS, AIP_MONTHS } from "@/lib/aipConstants";
import { AmtTD, inputCls, selectCls } from "@/components/aip/AipTreeCells";
import type { ConfirmDialogProps } from "@/components/ui/ConfirmDialog";
import type { AipActivityDetail, FundingSourceResponse } from "@/types";

// ── Activity row (RAL-179 — inline edit) ─────────────────────────────────────
// A read-only row that swaps to an edit form in place when the user clicks Edit — no whole-page
// submit, Save/Cancel per row. RefCode/ProjectId/identity are never editable here.

export default function ActivityRow({
  act, aipRecordId, canEdit, fundingSources, onSaved, onDeleted, onRequestConfirm,
}: {
  act: AipActivityDetail;
  aipRecordId: number;
  canEdit: boolean;
  fundingSources: FundingSourceResponse[];
  onSaved: (updated: AipActivityDetail) => void;
  onDeleted: (activityId: number) => void;
  onRequestConfirm: (props: ConfirmDialogProps) => void;
}) {
  const [editing, setEditing] = useState(false);
  const [saving, setSaving]   = useState(false);
  const [error, setError]     = useState<string | null>(null);

  const [name, setName]                             = useState(act.name);
  const [esreCode, setEsreCode]                     = useState(act.esreCode ?? "");
  const [implementingOffice, setImplementingOffice] = useState(act.implementingOffice ?? "");
  const [startDate, setStartDate]                   = useState(act.startDate ?? "");
  const [endDate, setEndDate]                       = useState(act.endDate ?? "");
  const [expectedOutputs, setExpectedOutputs]       = useState(act.expectedOutputs ?? "");
  const [fundingSourceId, setFundingSourceId]       = useState(act.fundingSourceId != null ? String(act.fundingSourceId) : "");
  // ⚠️ Money state is PESOS throughout — what is stored, what the ₱ inputs show, what the live row
  // total sums, and what is posted. Nothing converts on the way in or out.
  // ↩️ It was held in ₱000 and multiplied on save (decision P2-a, reversed 2026-09-07); only the
  // read-only cells divide now, via AmtTD. Each input carries its own `= x ₱000` echo.
  const [ps, setPs]                     = useState<number | null>(act.ps);
  const [mooe, setMooe]                 = useState<number | null>(act.mooe);
  const [co, setCo]                     = useState<number | null>(act.co);
  const [ccAdaptation, setCcAdaptation] = useState<number | null>(act.ccAdaptation);
  const [ccMitigation, setCcMitigation] = useState<number | null>(act.ccMitigation);
  const [ccTypologyCode, setCcTypologyCode] = useState(act.ccTypologyCode ?? "");

  function startEdit() {
    setName(act.name);
    setEsreCode(act.esreCode ?? "");
    setImplementingOffice(act.implementingOffice ?? "");
    setStartDate(act.startDate ?? "");
    setEndDate(act.endDate ?? "");
    setExpectedOutputs(act.expectedOutputs ?? "");
    setFundingSourceId(act.fundingSourceId != null ? String(act.fundingSourceId) : "");
    setPs(act.ps);
    setMooe(act.mooe);
    setCo(act.co);
    setCcAdaptation(act.ccAdaptation);
    setCcMitigation(act.ccMitigation);
    setCcTypologyCode(act.ccTypologyCode ?? "");
    setError(null);
    setEditing(true);
  }

  async function handleSave() {
    if (!name.trim()) { setError("Name is required."); return; }
    setSaving(true);
    setError(null);
    try {
      const updated = await updateAipActivity(aipRecordId, act.id, {
        name: name.trim(),
        esreCode: esreCode || null,
        implementingOffice: implementingOffice.trim() || null,
        startDate: startDate || null,
        endDate: endDate || null,
        expectedOutputs: expectedOutputs.trim() || null,
        fundingSourceId: fundingSourceId ? Number(fundingSourceId) : null,
        ps, mooe, co, ccAdaptation, ccMitigation,
        ccTypologyCode: ccTypologyCode.trim() || null,
      });
      onSaved(updated);
      setEditing(false);
    } catch (err) {
      setError(aipErrorMessage(err, "Could not save changes."));
    } finally {
      setSaving(false);
    }
  }

  function confirmDelete() {
    onRequestConfirm({
      title: "Delete Activity?",
      message: `This removes "${act.name}". This cannot be undone.`,
      confirmLabel: "Delete",
      variant: "danger",
      onConfirm: async () => {
        try {
          await deleteAipActivity(act.id);
          onDeleted(act.id);
        } catch (err) {
          setError(aipErrorMessage(err, "Could not delete activity."));
        }
      },
      onClose: () => {},
    });
  }

  if (!editing) {
    return (
      <tr className="bg-white border-t border-slate-100 hover:bg-green-50 transition-colors">
        <td className="px-2 py-1.5 pl-12 font-mono text-[11px] text-slate-600 align-top border-l-4 border-transparent">
          {act.refCode}
        </td>
        <td className="px-2 py-1.5 pl-12 text-xs text-slate-900 align-top leading-snug">
          {act.name}
          {act.isSynthetic && (
            <span
              className="ml-2 px-1.5 py-0.5 text-[10px] font-normal bg-amber-100 text-amber-700"
              title="This activity does not exist in the source file — it was created to hold a line item recorded directly on the parent program/project row."
            >
              project-level entry
            </span>
          )}
        </td>
        <td className="px-2 py-1.5 text-center text-xs text-slate-600">{act.esreCode ?? "—"}</td>
        <td className="px-2 py-1.5 text-xs text-slate-600 align-top">{act.implementingOffice ?? "—"}</td>
        <td className="px-2 py-1.5 text-center text-xs text-slate-600 whitespace-nowrap">{act.startDate ?? "—"}</td>
        <td className="px-2 py-1.5 text-center text-xs text-slate-600 whitespace-nowrap">{act.endDate ?? "—"}</td>
        <td className="px-2 py-1.5 text-xs text-slate-600 align-top leading-snug">{act.expectedOutputs ?? "—"}</td>
        <td className="px-2 py-1.5 text-center text-xs font-medium text-slate-600">{act.fundingSourceSnapshot ?? "—"}</td>
        <AmtTD value={act.ps} />
        <AmtTD value={act.mooe} />
        <AmtTD value={act.co} />
        <AmtTD value={act.total} />
        <AmtTD value={act.ccAdaptation} />
        <AmtTD value={act.ccMitigation} />
        <td className="px-2 py-1.5 text-center text-xs text-slate-600">{act.ccTypologyCode ?? "—"}</td>
        <td className="px-2 py-1.5 text-center whitespace-nowrap">
          {canEdit && (
            <span className="inline-flex gap-2">
              <button onClick={startEdit} className="text-xs text-green-700 hover:underline">Edit</button>
              <button onClick={confirmDelete} className="text-xs text-danger-500 hover:underline">Delete</button>
            </span>
          )}
        </td>
      </tr>
    );
  }

  return (
    <tr className="bg-amber-50 border-t border-amber-200 align-top">
      <td className="px-2 py-1.5 pl-12 font-mono text-[11px] text-slate-600">{act.refCode}</td>
      <td className="px-2 py-1.5">
        <textarea value={name} onChange={(e) => setName(e.target.value)} rows={2} className={`${inputCls} resize-vertical`} />
      </td>
      <td className="px-2 py-1.5">
        <select value={esreCode} onChange={(e) => setEsreCode(e.target.value)} className={selectCls}>
          <option value="">—</option>
          {AIP_ESRE_OPTIONS.map((o) => <option key={o.value} value={o.value}>{o.value}</option>)}
        </select>
      </td>
      <td className="px-2 py-1.5">
        <input value={implementingOffice} onChange={(e) => setImplementingOffice(e.target.value)} className={inputCls} />
      </td>
      <td className="px-2 py-1.5">
        <select value={startDate} onChange={(e) => setStartDate(e.target.value)} className={selectCls}>
          <option value="">—</option>
          {AIP_MONTHS.map((m) => <option key={m} value={m}>{m}</option>)}
        </select>
      </td>
      <td className="px-2 py-1.5">
        <select value={endDate} onChange={(e) => setEndDate(e.target.value)} className={selectCls}>
          <option value="">—</option>
          {AIP_MONTHS.map((m) => <option key={m} value={m}>{m}</option>)}
        </select>
      </td>
      <td className="px-2 py-1.5">
        <textarea value={expectedOutputs} onChange={(e) => setExpectedOutputs(e.target.value)} rows={2} className={`${inputCls} resize-vertical`} />
      </td>
      <td className="px-2 py-1.5">
        <select value={fundingSourceId} onChange={(e) => setFundingSourceId(e.target.value)} className={selectCls}>
          <option value="">—</option>
          {fundingSources.map((f) => <option key={f.id} value={f.id}>{f.code}</option>)}
        </select>
      </td>
      <td className="px-1 py-1.5"><AipMoneyInput value={ps} onChange={setPs} /></td>
      <td className="px-1 py-1.5"><AipMoneyInput value={mooe} onChange={setMooe} /></td>
      <td className="px-1 py-1.5"><AipMoneyInput value={co} onChange={setCo} /></td>
      <td className="px-2 py-1.5 text-right text-xs tabular-nums font-semibold text-slate-800">
        {fmtPesos((ps ?? 0) + (mooe ?? 0) + (co ?? 0))}
      </td>
      <td className="px-1 py-1.5"><AipMoneyInput value={ccAdaptation} onChange={setCcAdaptation} /></td>
      <td className="px-1 py-1.5"><AipMoneyInput value={ccMitigation} onChange={setCcMitigation} /></td>
      <td className="px-2 py-1.5">
        <input value={ccTypologyCode} onChange={(e) => setCcTypologyCode(e.target.value)} className={inputCls} />
      </td>
      <td className="px-2 py-1.5 text-center whitespace-nowrap">
        <div className="flex flex-col items-center gap-1">
          <div className="flex gap-2">
            <button
              onClick={handleSave}
              disabled={saving}
              className={`text-xs font-medium ${saving ? "text-green-300" : "text-green-700 hover:underline"}`}
            >
              {saving ? "Saving…" : "Save"}
            </button>
            <button
              onClick={() => setEditing(false)}
              disabled={saving}
              className="text-xs text-slate-600 hover:underline disabled:opacity-50"
            >
              Cancel
            </button>
          </div>
          {error && <p className="text-[10px] text-danger-600 max-w-[110px] leading-snug">{error}</p>}
        </div>
      </td>
    </tr>
  );
}
