"use client";

/**
 * Extracted verbatim from `aip/detail/page.tsx` (PPDO-64). No behaviour change.
 */

import AipMoneyInput from "@/components/aip/AipMoneyInput";
import { useState } from "react";
import { addAipActivity, aipErrorMessage } from "@/lib/aip";
import { fmtPesos } from "@/lib/aip-units";
import { AIP_ESRE_OPTIONS, AIP_MONTHS } from "@/lib/aipConstants";
import { inputCls, selectCls } from "@/components/aip/AipTreeCells";
import type { AipActivityDetail, FundingSourceResponse } from "@/types";

// ── Add Activity (inline form row, appended under an expanded project) ──────────

export default function AddActivityRow({
  projectId, fundingSources, onAdded,
}: {
  projectId: number;
  fundingSources: FundingSourceResponse[];
  onAdded: (newActivity: AipActivityDetail) => void;
}) {
  const [open, setOpen]     = useState(false);
  const [saving, setSaving] = useState(false);
  const [error, setError]   = useState<string | null>(null);

  const [name, setName]                             = useState("");
  const [esreCode, setEsreCode]                     = useState("");
  const [implementingOffice, setImplementingOffice] = useState("");
  const [startDate, setStartDate]                   = useState("");
  const [endDate, setEndDate]                       = useState("");
  const [expectedOutputs, setExpectedOutputs]       = useState("");
  const [fundingSourceRaw, setFundingSourceRaw]     = useState("");
  const [ps, setPs]                     = useState<number | null>(null);
  const [mooe, setMooe]                 = useState<number | null>(null);
  const [co, setCo]                     = useState<number | null>(null);
  const [ccAdaptation, setCcAdaptation] = useState<number | null>(null);
  const [ccMitigation, setCcMitigation] = useState<number | null>(null);
  const [ccTypologyCode, setCcTypologyCode] = useState("");

  function reset() {
    setName(""); setEsreCode(""); setImplementingOffice(""); setStartDate(""); setEndDate("");
    setExpectedOutputs(""); setFundingSourceRaw(""); setPs(null); setMooe(null); setCo(null);
    setCcAdaptation(null); setCcMitigation(null); setCcTypologyCode(""); setError(null);
  }

  async function handleAdd() {
    if (!name.trim()) { setError("Name is required."); return; }
    setSaving(true);
    setError(null);
    try {
      const created = await addAipActivity(projectId, {
        name: name.trim(),
        esreCode: esreCode || null,
        implementingOffice: implementingOffice.trim() || null,
        startDate: startDate || null,
        endDate: endDate || null,
        expectedOutputs: expectedOutputs.trim() || null,
        fundingSourceRaw: fundingSourceRaw || null,
        // Typed in ₱000 like every other figure on this page — converted to pesos on the way out.
        ps:           ps,
        mooe:         mooe,
        co:           co,
        ccAdaptation: ccAdaptation,
        ccMitigation: ccMitigation,
        ccTypologyCode: ccTypologyCode.trim() || null,
      });
      onAdded(created);
      reset(); // keep the form open so multiple activities can be added in a row
    } catch (err) {
      setError(aipErrorMessage(err, "Could not add activity."));
    } finally {
      setSaving(false);
    }
  }

  if (!open) {
    return (
      <tr className="bg-white border-t border-slate-100">
        <td colSpan={16} className="px-2 py-1.5 pl-12">
          <button onClick={() => setOpen(true)} className="text-xs text-green-700 hover:underline">
            + Add Activity
          </button>
        </td>
      </tr>
    );
  }

  return (
    <tr className="bg-green-50 border-t border-green-200 align-top">
      <td className="px-2 py-1.5 pl-12 font-mono text-[11px] text-slate-400">auto</td>
      <td className="px-2 py-1.5">
        <textarea value={name} onChange={(e) => setName(e.target.value)} rows={2}
          placeholder="Activity description" className={`${inputCls} resize-vertical`} />
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
        <select value={fundingSourceRaw} onChange={(e) => setFundingSourceRaw(e.target.value)} className={selectCls}>
          <option value="">—</option>
          {fundingSources.map((f) => <option key={f.id} value={f.code}>{f.code}</option>)}
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
              onClick={handleAdd}
              disabled={saving}
              className={`text-xs font-medium ${saving ? "text-green-300" : "text-green-700 hover:underline"}`}
            >
              {saving ? "Adding…" : "Add"}
            </button>
            <button onClick={() => setOpen(false)} disabled={saving} className="text-xs text-slate-600 hover:underline disabled:opacity-50">
              Close
            </button>
          </div>
          {error && <p className="text-[10px] text-danger-600 max-w-[110px] leading-snug">{error}</p>}
        </div>
      </td>
    </tr>
  );
}
