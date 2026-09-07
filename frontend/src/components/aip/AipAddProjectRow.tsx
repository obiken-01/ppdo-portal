"use client";

/**
 * Extracted verbatim from `aip/detail/page.tsx` (PPDO-64). No behaviour change.
 */

import { useState } from "react";
import { addAipProject, aipErrorMessage } from "@/lib/aip";
import { inputCls } from "@/components/aip/AipTreeCells";
import type { AipProjectDetail } from "@/types";

// ── Add Project (inline form row, appended under an expanded program) ───────────

export default function AddProjectRow({ programId, onAdded }: { programId: number; onAdded: (newProject: AipProjectDetail) => void }) {
  const [open, setOpen]     = useState(false);
  const [name, setName]     = useState("");
  const [saving, setSaving] = useState(false);
  const [error, setError]   = useState<string | null>(null);

  async function handleAdd() {
    if (!name.trim()) { setError("Name is required."); return; }
    setSaving(true);
    setError(null);
    try {
      const created = await addAipProject(programId, { name: name.trim() });
      onAdded(created);
      setName("");
    } catch (err) {
      setError(aipErrorMessage(err, "Could not add project."));
    } finally {
      setSaving(false);
    }
  }

  if (!open) {
    return (
      <tr className="bg-white border-t border-slate-100">
        <td colSpan={16} className="px-2 py-1.5 pl-9">
          <button onClick={() => setOpen(true)} className="text-xs text-green-700 hover:underline">+ Add Project</button>
        </td>
      </tr>
    );
  }

  return (
    <tr className="bg-green-50 border-t border-green-200">
      <td className="px-2 py-1.5 pl-9 font-mono text-[11px] text-slate-400">auto</td>
      <td colSpan={15} className="px-2 py-1.5">
        <div className="flex items-center gap-2 flex-wrap">
          <input value={name} onChange={(e) => setName(e.target.value)} placeholder="Project name"
            className={`${inputCls} max-w-md`} />
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
