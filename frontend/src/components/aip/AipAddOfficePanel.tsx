"use client";

/**
 * Extracted verbatim from `aip/detail/page.tsx` (PPDO-64). No behaviour change.
 */

import { useState } from "react";
import { addAipOffice, aipErrorMessage } from "@/lib/aip";
import { AIP_SECTOR_OPTIONS, AIP_SECTOR_PREFIX } from "@/lib/aipConstants";
import type { AipOfficeDetail, OfficeResponse } from "@/types";

// ── Add Office (panel near the page header, only while Draft) ───────────────────

export default function AddOfficePanel({
  aipRecordId, officeConfigs, onAdded,
}: {
  aipRecordId: number;
  officeConfigs: OfficeResponse[];
  onAdded: (newOffice: AipOfficeDetail) => void;
}) {
  const [open, setOpen]               = useState(false);
  const [officeConfigId, setOfficeConfigId] = useState("");
  const [officeName, setOfficeName]   = useState("");
  const [sector, setSector]           = useState<string>(AIP_SECTOR_OPTIONS[0]);
  const [saving, setSaving]           = useState(false);
  const [error, setError]             = useState<string | null>(null);

  async function handleAdd() {
    if (!officeConfigId) { setError("Pick an office."); return; }
    setSaving(true);
    setError(null);
    try {
      const created = await addAipOffice(aipRecordId, {
        officeConfigId: Number(officeConfigId), sector, name: officeName.trim() || null,
      });
      onAdded(created);
      setOfficeConfigId("");
      setOfficeName("");
      setOpen(false);
    } catch (err) {
      setError(aipErrorMessage(err, "Could not add office."));
    } finally {
      setSaving(false);
    }
  }

  if (!open) {
    return (
      <button
        onClick={() => setOpen(true)}
        className="px-3 py-1.5 text-sm font-medium text-white bg-green-700 hover:bg-green-800 transition-colors whitespace-nowrap"
      >
        + Add Office
      </button>
    );
  }

  return (
    <div className="border border-slate-200 bg-slate-50 p-4 mb-4 space-y-3">
      <div className="grid grid-cols-3 gap-3">
        <div>
          <label className="block text-xs font-semibold text-slate-600 uppercase tracking-wide mb-1">Office</label>
          <select
            value={officeConfigId}
            onChange={(e) => {
              setOfficeConfigId(e.target.value);
              const picked = officeConfigs.find((o) => String(o.id) === e.target.value);
              if (picked) setOfficeName(picked.officeName);
            }}
            className="border border-slate-300 bg-white text-sm px-3 py-2 text-slate-600 w-full focus:outline-none focus:ring-1 focus:ring-green-600"
          >
            <option value="">Select an office…</option>
            {officeConfigs.map((o) => (
              <option key={o.id} value={o.id} disabled={!o.officeRefCode}>
                {o.officeName}{!o.officeRefCode ? " (no AIP ref code configured)" : ""}
              </option>
            ))}
          </select>
        </div>
        <div>
          <label className="block text-xs font-semibold text-slate-600 uppercase tracking-wide mb-1">Sector</label>
          <select
            value={sector}
            onChange={(e) => setSector(e.target.value)}
            className="border border-slate-300 bg-white text-sm px-3 py-2 text-slate-600 w-full focus:outline-none focus:ring-1 focus:ring-green-600"
          >
            {AIP_SECTOR_OPTIONS.map((s) => <option key={s} value={s}>{s}</option>)}
          </select>
        </div>
        <div>
          <label className="block text-xs font-semibold text-slate-600 uppercase tracking-wide mb-1">
            Office Name <span className="text-slate-600 font-normal normal-case">(editable)</span>
          </label>
          <input
            value={officeName}
            onChange={(e) => setOfficeName(e.target.value)}
            className="border border-slate-300 bg-white text-sm px-3 py-2 text-slate-600 w-full focus:outline-none focus:ring-1 focus:ring-green-600"
          />
        </div>
      </div>
      {officeConfigId && (
        <p className="text-xs text-slate-600">
          Ref code preview:{" "}
          <span className="font-mono text-slate-600">
            {AIP_SECTOR_PREFIX[sector]}-000-1-{officeConfigs.find((o) => String(o.id) === officeConfigId)?.officeRefCode ?? "…"}
          </span>
        </p>
      )}
      {error && <p className="text-xs text-danger-600">{error}</p>}
      <div className="flex items-center gap-3">
        <button
          onClick={handleAdd}
          disabled={saving || !officeConfigId}
          className={`px-4 py-1.5 text-sm font-medium text-white transition-colors ${
            saving || !officeConfigId ? "bg-green-300 cursor-not-allowed" : "bg-green-700 hover:bg-green-800"
          }`}
        >
          {saving ? "Adding…" : "Add Office"}
        </button>
        <button
          onClick={() => setOpen(false)}
          disabled={saving}
          className="px-4 py-1.5 text-sm font-medium border border-slate-300 text-slate-600 hover:bg-slate-50 transition-colors disabled:opacity-50"
        >
          Cancel
        </button>
      </div>
    </div>
  );
}
