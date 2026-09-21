"use client";

/**
 * Extracted verbatim from `aip/detail/page.tsx` (PPDO-64). No behaviour change.
 */

import { useState } from "react";
import { addAipProgram, aipErrorMessage } from "@/lib/aip";
import { AIP_FUNCTION_BANDS } from "@/lib/aipConstants";
import { inputCls, selectCls } from "@/components/aip/AipTreeCells";
import type { AipProgramDetail } from "@/types";

// ── Add Program (inline form row, appended under an expanded office) ────────────

export default function AddProgramRow({ officeId, onAdded, ldipOnly }: {
  officeId: number;
  onAdded: (newProgram: AipProgramDetail) => void;
  /** V18-41 — non-null means this year takes its programs from the LDIP only. */
  ldipOnly: string | null;
}) {
  const [open, setOpen]                 = useState(false);
  const [name, setName]                 = useState("");
  const [functionBand, setFunctionBand] = useState("CORE");
  const [saving, setSaving]             = useState(false);
  const [error, setError]               = useState<string | null>(null);

  async function handleAdd() {
    if (!name.trim()) { setError("Name is required."); return; }
    setSaving(true);
    setError(null);
    try {
      const created = await addAipProgram(officeId, { name: name.trim(), functionBand });
      onAdded(created);
      setName("");
    } catch (err) {
      setError(aipErrorMessage(err, "Could not add program."));
    } finally {
      setSaving(false);
    }
  }

  // V18-41 — disabled with the reason, not hidden. The user has the permission; the fiscal
  // year is what forbids it (Budget_Planning_Dashboard_Requirements.md §6.1). Hiding it would
  // leave someone hunting for a control that used to be there, with nothing to explain why.
  if (ldipOnly) {
    return (
      <tr className="bg-white border-t border-slate-100">
        <td colSpan={16} className="px-2 py-1.5 pl-5">
          <span className="text-xs text-slate-400 cursor-not-allowed" title={ldipOnly}>
            + Add Program
          </span>
          <span className="text-[10px] text-slate-600 ml-2">
            Programs for this fiscal year come from the LDIP — use “Seed from LDIP” above.
          </span>
        </td>
      </tr>
    );
  }

  if (!open) {
    return (
      <tr className="bg-white border-t border-slate-100">
        <td colSpan={16} className="px-2 py-1.5 pl-5">
          <button onClick={() => setOpen(true)} className="text-xs text-green-700 hover:underline">+ Add Program</button>
        </td>
      </tr>
    );
  }

  return (
    <tr className="bg-green-50 border-t border-green-200">
      <td className="px-2 py-1.5 pl-5 font-mono text-[11px] text-slate-400">auto</td>
      <td colSpan={15} className="px-2 py-1.5">
        <div className="flex items-center gap-2 flex-wrap">
          <input value={name} onChange={(e) => setName(e.target.value)} placeholder="Program name"
            className={`${inputCls} max-w-md`} />
          <select value={functionBand} onChange={(e) => setFunctionBand(e.target.value)} className={`${selectCls} w-32`}>
            {AIP_FUNCTION_BANDS.map((b) => <option key={b} value={b}>{b}</option>)}
          </select>
          <button onClick={handleAdd} disabled={saving} className={`text-xs font-medium ${saving ? "text-green-300" : "text-green-700 hover:underline"}`}>
            {saving ? "Adding…" : "Add"}
          </button>
          <button onClick={() => setOpen(false)} disabled={saving} className="text-xs text-slate-600 hover:underline disabled:opacity-50">
            Close
          </button>
          {error && <span className="text-[10px] text-danger-600">{error}</span>}
        </div>
      </td>
    </tr>
  );
}
