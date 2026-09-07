"use client";

/**
 * Extracted verbatim from `aip/detail/page.tsx` (PPDO-64). No behaviour change.
 */

import { useState } from "react";
import { aipErrorMessage, seedAipProgramsFromLdip } from "@/lib/aip";
import { getLdipById, listLdip } from "@/lib/ldip";
import { toggleSet } from "@/lib/aip-tree";
import { AIP_SECTOR_OPTIONS, AIP_SECTOR_PREFIX } from "@/lib/aipConstants";
import type { AipOfficeDetail, LdipOfficeGroup, OfficeResponse } from "@/types";

// ── Seed Programs from LDIP (RAL-181) ───────────────────────────────────────────
//
// Seeds bare-shell AipProgram rows (Name+RefCode only, FunctionBand=CORE) from a matching
// LdipOffice's programs — no Project/Activity rows, no LDIP amounts/detail fields copied
// (LdipProgram.Budget is a multi-year total across FiscalYearStart-FiscalYearEnd, not a valid
// single-fiscal-year figure — see the ticket's "why LDIP amounts don't carry over" reasoning).
// The backend resolves which LdipRecord to read from on its own (newest non-Archived record for
// this office with a sector group match); this panel mirrors that same resolution client-side
// just to show the user real program checkboxes to pick from before submitting.
export default function SeedFromLdipPanel({
  targetFiscalYear, officeConfigs, onSeeded, ldipOnly,
}: {
  targetFiscalYear: number;
  officeConfigs: OfficeResponse[];
  onSeeded: (office: AipOfficeDetail) => void;
  /**
   * V18-41 — non-null when the LDIP is this year's ONLY program source. Changes nothing about
   * how seeding works; it changes how hard the "no LDIP" case lands. For FY≤2027 an office
   * without an LDIP can still hand-enter its programs, so that message is informational. From
   * the break year on the same message means the office cannot build an AIP at all yet.
   */
  ldipOnly: string | null;
}) {
  const [open, setOpen]                     = useState(false);
  const [officeConfigId, setOfficeConfigId] = useState("");
  const [sector, setSector]                 = useState<string>(AIP_SECTOR_OPTIONS[0]);
  const [sourceGroup, setSourceGroup]       = useState<LdipOfficeGroup | null>(null);
  const [checkedProgramIds, setCheckedProgramIds] = useState<Set<number>>(new Set());
  const [loading, setLoading]               = useState(false);
  const [saving, setSaving]                 = useState(false);
  const [error, setError]                   = useState<string | null>(null);

  async function handleLoad() {
    if (!officeConfigId) { setError("Pick an office."); return; }
    setLoading(true);
    setError(null);
    setSourceGroup(null);
    setCheckedProgramIds(new Set());
    try {
      // Tier 1 — this office's own LDIP records (New/Amendment/Supplemental). Sector text match
      // is safe here since these records' groups already all belong to this one office.
      const ownRecords = await listLdip({ officeId: Number(officeConfigId) });
      let found: LdipOfficeGroup | null = null;
      for (const rec of ownRecords.filter((r) => r.status !== "Archived")) {
        const detail = await getLdipById(rec.id);
        const group = detail.groups.find((g) => g.sector.toUpperCase() === sector.toUpperCase());
        if (group) { found = group; break; }
      }

      // Tier 2 — multi-office Upload records (officeId null, one document spans every office).
      // An office with no dedicated record of its own (e.g. archived after a bulk import) would
      // otherwise show "no LDIP" even though the bulk document has its data. Sector text alone
      // can't disambiguate within one multi-office document (many offices share "General"), so
      // match by this office's own computed AIP ref code instead — mirrors the backend's own
      // resolution in AipService.SeedProgramsFromLdipAsync.
      if (!found) {
        const office = officeConfigs.find((o) => String(o.id) === officeConfigId);
        if (office?.officeRefCode) {
          const expectedRefCode = `${AIP_SECTOR_PREFIX[sector]}-000-1-${office.officeRefCode}`;
          const allRecords = await listLdip({});
          const multiOfficeRecords = allRecords.filter((r) => r.officeId == null && r.status !== "Archived");
          for (const rec of multiOfficeRecords) {
            const detail = await getLdipById(rec.id);
            const group = detail.groups.find(
              (g) => g.refCode.toUpperCase() === expectedRefCode.toUpperCase()
            );
            if (group) { found = group; break; }
          }
        }
      }

      if (!found) {
        setError(
          ldipOnly
            ? `This office has no LDIP for the ${sector} sector, and FY${targetFiscalYear} AIP ` +
              `programs can only come from the LDIP. Create the office's ${sector} LDIP first — ` +
              `there is no other way to add programs for this year.`
            : `This office has no LDIP for the ${sector} sector.`
        );
        return;
      }
      setSourceGroup(found);
      // Select All checked by default (matches RAL-180's UX default).
      setCheckedProgramIds(new Set(found.programs.map((p) => p.id)));
    } catch (err) {
      setError(aipErrorMessage(err, "Could not load that office's LDIP."));
    } finally {
      setLoading(false);
    }
  }

  function toggleProgram(id: number) {
    setCheckedProgramIds((prev) => toggleSet(prev, id));
  }

  function toggleSelectAll() {
    if (!sourceGroup) return;
    setCheckedProgramIds((prev) =>
      prev.size === sourceGroup.programs.length ? new Set() : new Set(sourceGroup.programs.map((p) => p.id))
    );
  }

  function resetAndClose() {
    setOpen(false);
    setOfficeConfigId("");
    setSector(AIP_SECTOR_OPTIONS[0]);
    setSourceGroup(null);
    setCheckedProgramIds(new Set());
    setError(null);
  }

  async function handleConfirm() {
    if (!sourceGroup || checkedProgramIds.size === 0) {
      setError("Load an office's LDIP and pick at least one program.");
      return;
    }
    setSaving(true);
    setError(null);
    try {
      const seeded = await seedAipProgramsFromLdip({
        targetFiscalYear,
        officeConfigId: Number(officeConfigId),
        sector,
        ldipProgramIds: Array.from(checkedProgramIds),
      });
      onSeeded(seeded);
      resetAndClose();
    } catch (err) {
      setError(aipErrorMessage(err, "Could not seed programs from that LDIP."));
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
        + Seed from LDIP
      </button>
    );
  }

  return (
    <div className="border border-slate-200 bg-slate-50 p-4 mb-4 space-y-3">
      <div className="grid grid-cols-3 gap-3 items-end">
        <div>
          <label className="block text-xs font-semibold text-slate-600 uppercase tracking-wide mb-1">Office</label>
          <select
            value={officeConfigId}
            onChange={(e) => { setOfficeConfigId(e.target.value); setSourceGroup(null); setCheckedProgramIds(new Set()); }}
            className="border border-slate-300 bg-white text-sm px-3 py-2 text-slate-600 w-full focus:outline-none focus:ring-1 focus:ring-green-600"
          >
            <option value="">Select an office…</option>
            {officeConfigs.map((o) => (
              <option key={o.id} value={o.id}>{o.officeName}</option>
            ))}
          </select>
        </div>
        <div>
          <label className="block text-xs font-semibold text-slate-600 uppercase tracking-wide mb-1">Sector</label>
          <select
            value={sector}
            onChange={(e) => { setSector(e.target.value); setSourceGroup(null); setCheckedProgramIds(new Set()); }}
            className="border border-slate-300 bg-white text-sm px-3 py-2 text-slate-600 w-full focus:outline-none focus:ring-1 focus:ring-green-600"
          >
            {AIP_SECTOR_OPTIONS.map((s) => <option key={s} value={s}>{s}</option>)}
          </select>
        </div>
        <button
          onClick={handleLoad}
          disabled={loading || !officeConfigId}
          className="px-3 py-2 text-sm font-medium border border-slate-300 text-slate-800 hover:bg-slate-100 transition-colors disabled:opacity-50 whitespace-nowrap"
        >
          {loading ? "Loading…" : "Load LDIP"}
        </button>
      </div>

      {sourceGroup && (
        <div className="border border-slate-200 bg-white">
          <div className="flex items-center justify-between px-3 py-2 border-b border-slate-200">
            <label className="flex items-center gap-2 text-xs font-semibold text-slate-600 uppercase tracking-wide cursor-pointer">
              <input
                type="checkbox"
                checked={checkedProgramIds.size === sourceGroup.programs.length && sourceGroup.programs.length > 0}
                onChange={toggleSelectAll}
                className="accent-green-600"
              />
              Select All ({sourceGroup.programs.length} programs)
            </label>
          </div>
          <div className="max-h-56 overflow-y-auto divide-y divide-slate-100">
            {sourceGroup.programs.map((p) => (
              <label key={p.id} className="flex items-center gap-2 px-3 py-2 text-sm text-slate-600 cursor-pointer hover:bg-slate-50">
                <input
                  type="checkbox"
                  checked={checkedProgramIds.has(p.id)}
                  onChange={() => toggleProgram(p.id)}
                  className="accent-green-600 shrink-0"
                />
                <span className="flex-1 truncate">{p.name}</span>
                <span className="text-xs text-slate-600 font-mono whitespace-nowrap">{p.refCode}</span>
              </label>
            ))}
            {sourceGroup.programs.length === 0 && (
              <p className="px-3 py-2 text-xs text-slate-600">This LDIP has no programs to seed.</p>
            )}
          </div>
        </div>
      )}

      {error && <p className="text-xs text-danger-600">{error}</p>}
      <div className="flex items-center gap-3">
        <button
          onClick={handleConfirm}
          disabled={saving || !sourceGroup || checkedProgramIds.size === 0}
          className={`px-4 py-1.5 text-sm font-medium text-white transition-colors ${
            saving || !sourceGroup || checkedProgramIds.size === 0
              ? "bg-green-300 cursor-not-allowed" : "bg-green-700 hover:bg-green-800"
          }`}
        >
          {saving ? "Seeding…" : `Seed ${checkedProgramIds.size || ""} Program${checkedProgramIds.size === 1 ? "" : "s"}`}
        </button>
        <button
          onClick={resetAndClose}
          disabled={saving}
          className="px-4 py-1.5 text-sm font-medium border border-slate-300 text-slate-600 hover:bg-slate-50 transition-colors disabled:opacity-50"
        >
          Cancel
        </button>
      </div>
    </div>
  );
}
