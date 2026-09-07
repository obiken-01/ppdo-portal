"use client";

/**
 * AIP Detail page — read-only hierarchy view with sector tabs.
 * Route: /budget-planning/aip/detail?id=<aipRecordId>
 *
 * Performance strategy:
 *  1. Sector tabs — only one sector's rows are in the DOM at a time.
 *  2. Start everything collapsed — initial render per tab is just office header
 *     rows (~10 rows), not 1219 activities.
 *  3. Incremental expand — user drills down one level at a time; each expand
 *     mounts only that node's immediate children (avg ~37 activities per office).
 *
 * Access: canAccessBudgetPlanning.
 */

import { Fragment, useCallback, useEffect, useMemo, useState } from "react";
import Link from "next/link";
import { useSearchParams } from "next/navigation";
import { useMe } from "@/lib/me-cache";
import {
  getAipById, aipErrorMessage,
  addAipOffice, addAipProgram, addAipProject, addAipActivity,
  updateAipOffice, updateAipProgram, updateAipProject, updateAipActivity,
  deleteAipOffice, deleteAipProgram, deleteAipProject, deleteAipActivity,
  seedAipProgramsFromLdip,
} from "@/lib/aip";
import { listLdip, getLdipById } from "@/lib/ldip";
import { listOffices, listFundingSources } from "@/lib/config";
import { aipProgramsAreLdipOnly, aipUploadRefusal } from "@/lib/aip-fiscal-years";
import AddProgramRow from "@/components/aip/AipAddProgramRow";
import ProgramRow from "@/components/aip/AipProgramRow";
import type { TreeActions } from "@/components/aip/AipTreeActions";
import {
  Chevron, StatusBadge, TH, AmtTD, selectCls, inputCls,
} from "@/components/aip/AipTreeCells";
import {
  sumActivities,
  replaceActivity, removeActivityFromTree, addActivityToTree,
  replaceProject, removeProjectFromTree, addProjectToTree,
  replaceProgram, removeProgramFromTree, addProgramToTree,
  replaceOffice, removeOfficeFromTree, addOfficeToTree,
  groupBySector, toggleSet, allCollapsed,
} from "@/lib/aip-tree";
import { fmt, toDisplayUnits, toStorageUnits } from "@/lib/aip-units";
import {
  AIP_MONTHS, AIP_ESRE_OPTIONS, AIP_SECTOR_OPTIONS, AIP_SECTOR_PREFIX, AIP_FUNCTION_BANDS,
} from "@/lib/aipConstants";
import MoneyInput from "@/components/ui/MoneyInput";
import ConfirmDialog, { type ConfirmDialogProps } from "@/components/ui/ConfirmDialog";
import type {
  AipRecordDetail,
  AipOfficeDetail,
  AipProgramDetail,
  AipProjectDetail,
  AipActivityDetail,
  FundingSourceResponse,
  OfficeResponse,
  LdipOfficeGroup,
} from "@/types";

// ── Office row — Edit/Delete for itself, renders its programs + Add Program ─────

function OfficeRow({
  office, open, onToggle, collapsedPrograms, onToggleProgram, collapsedProjects, onToggleProject, actions,
}: {
  office: AipOfficeDetail;
  open: boolean;
  onToggle: () => void;
  collapsedPrograms: Set<number>;
  onToggleProgram: (id: number) => void;
  collapsedProjects: Set<number>;
  onToggleProject: (id: number) => void;
  actions: TreeActions;
}) {
  const [editing, setEditing] = useState(false);
  const [name, setName]       = useState(office.name);
  const [saving, setSaving]   = useState(false);
  const [error, setError]     = useState<string | null>(null);

  const officePs    = sumActivities(office, "ps");
  const officeMooe  = sumActivities(office, "mooe");
  const officeCo    = sumActivities(office, "co");
  const officeTotal = sumActivities(office, "total");
  const actCount    = office.programs.flatMap((p) => p.projects).flatMap((p) => p.activities).length;

  function startEdit() { setName(office.name); setError(null); setEditing(true); }

  async function handleSave() {
    if (!name.trim()) { setError("Name is required."); return; }
    setSaving(true);
    setError(null);
    try {
      const updated = await updateAipOffice(office.id, { name: name.trim() });
      // Server response omits Programs by convention — merge onto the existing node.
      actions.onOfficeUpdated({ ...office, name: updated.name });
      setEditing(false);
    } catch (err) {
      setError(aipErrorMessage(err, "Could not save changes."));
    } finally {
      setSaving(false);
    }
  }

  function confirmDelete() {
    actions.onRequestConfirm({
      title: "Delete Office?",
      message: `This removes "${office.name}" and everything under it (${office.programs.length} programs, ${actCount} activities). This cannot be undone.`,
      confirmLabel: "Delete",
      variant: "danger",
      onConfirm: async () => {
        try {
          await deleteAipOffice(office.id);
          actions.onOfficeDeleted(office.id);
        } catch (err) {
          setError(aipErrorMessage(err, "Could not delete office."));
        }
      },
      onClose: () => {},
    });
  }

  return (
    <>
      <tr className="bg-green-700 border-t-2 border-green-800 align-top">
        <td className="px-2 py-2 font-mono text-xs text-slate-600 align-top">
          <button onClick={onToggle} className="flex items-center gap-1.5 text-left">
            <Chevron open={open} className="text-green-200" />
            <span className="text-green-100">{office.refCode}</span>
          </button>
        </td>
        {editing ? (
          <td colSpan={5} className="px-2 py-1.5">
            <div className="flex items-center gap-2 flex-wrap">
              <input value={name} onChange={(e) => setName(e.target.value)} className="border border-slate-300 bg-white text-xs px-1.5 py-1 text-slate-800 w-full max-w-xs focus:outline-none focus:ring-1 focus:ring-green-600" />
              <button onClick={handleSave} disabled={saving} className={`text-xs font-medium ${saving ? "text-green-200" : "text-white hover:underline"}`}>
                {saving ? "Saving…" : "Save"}
              </button>
              <button onClick={() => setEditing(false)} disabled={saving} className="text-xs text-green-100 hover:underline disabled:opacity-50">
                Cancel
              </button>
              {error && <span className="text-[10px] text-amber-200">{error}</span>}
            </div>
          </td>
        ) : (
          <td className="px-2 py-2 align-top">
            <span className="font-bold text-sm uppercase text-white">{office.name}</span>
            {!open && (
              <span className="ml-2 text-[10px] text-green-200">
                {office.programs.length} programs · {actCount} activities
              </span>
            )}
          </td>
        )}
        {!editing && (
          <>
            <td /><td /><td /><td />
            <AmtTD value={officePs}    white />
            <AmtTD value={officeMooe}  white />
            <AmtTD value={officeCo}    white />
            <AmtTD value={officeTotal} white />
            <td /><td /><td />
          </>
        )}
        <td className="px-2 py-2 text-center whitespace-nowrap">
          {actions.canEdit && !editing && (
            <span className="inline-flex gap-2">
              <button onClick={startEdit} className="text-xs text-green-100 hover:underline">Edit</button>
              <button onClick={confirmDelete} className="text-xs text-amber-200 hover:underline">Delete</button>
            </span>
          )}
        </td>
      </tr>

      {open && office.programs.map((prog) => (
        <ProgramRow
          key={`prog-${prog.id}`}
          prog={prog}
          open={!collapsedPrograms.has(prog.id)}
          onToggle={() => onToggleProgram(prog.id)}
          collapsedProjects={collapsedProjects}
          onToggleProject={onToggleProject}
          actions={actions}
        />
      ))}

      {open && actions.canEdit && (
        <AddProgramRow
          officeId={office.id}
          onAdded={(newProg) => actions.onProgramAdded(office.id, newProg)}
          ldipOnly={actions.programsLdipOnly}
        />
      )}
    </>
  );
}

// ── Add Office (panel near the page header, only while Draft) ───────────────────

function AddOfficePanel({
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

// ── Seed Programs from LDIP (RAL-181) ───────────────────────────────────────────
//
// Seeds bare-shell AipProgram rows (Name+RefCode only, FunctionBand=CORE) from a matching
// LdipOffice's programs — no Project/Activity rows, no LDIP amounts/detail fields copied
// (LdipProgram.Budget is a multi-year total across FiscalYearStart-FiscalYearEnd, not a valid
// single-fiscal-year figure — see the ticket's "why LDIP amounts don't carry over" reasoning).
// The backend resolves which LdipRecord to read from on its own (newest non-Archived record for
// this office with a sector group match); this panel mirrors that same resolution client-side
// just to show the user real program checkboxes to pick from before submitting.
function SeedFromLdipPanel({
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

// ── Main page ─────────────────────────────────────────────────────────────────

export default function AipDetailPage() {
  const searchParams = useSearchParams();
  const id           = parseInt(searchParams.get("id") ?? "", 10);

  const me = useMe((m) => m.canAccessBudgetPlanning);
  const [record,  setRecord]  = useState<AipRecordDetail | null>(null);
  const [loading, setLoading] = useState(true);
  const [error,   setError]   = useState<string | null>(null);
  const [fundingSources, setFundingSources] = useState<FundingSourceResponse[]>([]);
  const [officeConfigs,  setOfficeConfigs]  = useState<OfficeResponse[]>([]);
  const [confirmDialog,  setConfirmDialog]  = useState<ConfirmDialogProps | null>(null);

  const requestConfirm = useCallback((props: ConfirmDialogProps) => {
    setConfirmDialog({ ...props, onClose: () => { props.onClose(); setConfirmDialog(null); } });
  }, []);

  // Detail-page CRUD follow-up to RAL-179 — every mutation updates local tree state immutably
  // (see the tree-update helpers above replaceActivity) so a full page refetch isn't needed.
  const handleOfficeUpdated  = useCallback((updated: AipOfficeDetail)  => setRecord((prev) => prev && replaceOffice(prev, updated)), []);
  const handleOfficeDeleted  = useCallback((officeId: number)         => setRecord((prev) => prev && removeOfficeFromTree(prev, officeId)), []);
  const handleOfficeAdded    = useCallback((newOffice: AipOfficeDetail) => setRecord((prev) => prev && addOfficeToTree(prev, newOffice)), []);
  // RAL-181 — the seed response may be a brand-new office OR an existing one with
  // programs merged in; replace if it's already in the tree, otherwise append.
  /** Applies an office returned by seeding — added, or updated in place if it already existed. */
  const handleOfficeSeeded = useCallback((office: AipOfficeDetail) => {
    setRecord((prev) => {
      if (!prev) return prev;
      return prev.offices.some((o) => o.id === office.id)
        ? replaceOffice(prev, office)
        : addOfficeToTree(prev, office);
    });
  }, []);
  const handleProgramAdded   = useCallback((officeId: number, p: AipProgramDetail) => setRecord((prev) => prev && addProgramToTree(prev, officeId, p)), []);
  const handleProgramUpdated = useCallback((updated: AipProgramDetail) => setRecord((prev) => prev && replaceProgram(prev, updated)), []);
  const handleProgramDeleted = useCallback((officeId: number, programId: number) => setRecord((prev) => prev && removeProgramFromTree(prev, officeId, programId)), []);
  const handleProjectAdded   = useCallback((programId: number, j: AipProjectDetail) => setRecord((prev) => prev && addProjectToTree(prev, programId, j)), []);
  const handleProjectUpdated = useCallback((updated: AipProjectDetail) => setRecord((prev) => prev && replaceProject(prev, updated)), []);
  const handleProjectDeleted = useCallback((programId: number, projectId: number) => setRecord((prev) => prev && removeProjectFromTree(prev, programId, projectId)), []);
  const handleActivityAdded  = useCallback((projectId: number, a: AipActivityDetail) => setRecord((prev) => prev && addActivityToTree(prev, projectId, a)), []);
  const handleActivitySaved  = useCallback((updated: AipActivityDetail) => setRecord((prev) => (prev ? replaceActivity(prev, updated) : prev)), []);
  const handleActivityDeleted = useCallback((projectId: number, activityId: number) => setRecord((prev) => prev && removeActivityFromTree(prev, projectId, activityId)), []);

  const [activeTab,         setActiveTab]         = useState<string>("");
  const [collapsedOffices,  setCollapsedOffices]  = useState<Set<number>>(new Set());
  const [collapsedPrograms, setCollapsedPrograms] = useState<Set<number>>(new Set());
  const [collapsedProjects, setCollapsedProjects] = useState<Set<number>>(new Set());

  const toggleOffice  = useCallback((k: number) => setCollapsedOffices( (p) => toggleSet(p, k)), []);
  const toggleProgram = useCallback((k: number) => setCollapsedPrograms((p) => toggleSet(p, k)), []);
  const toggleProject = useCallback((k: number) => setCollapsedProjects((p) => toggleSet(p, k)), []);

  useEffect(() => {
    if (!me || isNaN(id)) return;
    setLoading(true);
    setError(null);
    getAipById(id)
      .then(setRecord)
      .catch((err) => setError(aipErrorMessage(err, "Failed to load AIP record.")))
      .finally(() => setLoading(false));
  }, [me, id]);

  useEffect(() => {
    if (!me) return;
    listFundingSources({ active: "true" }).then(setFundingSources).catch(() => {});
    listOffices({ active: "true" }).then(setOfficeConfigs).catch(() => {});
  }, [me]);

  const sectors = useMemo(() => record ? groupBySector(record.offices) : [], [record]);
  // Stable key (not `sectors` itself, a new array reference on every edit) — only changes when
  // the actual set of sector tabs changes, e.g. the first office ever added to a blank Draft
  // record built from scratch on this page, or the last office of a sector being deleted.
  const sectorKeys = useMemo(() => sectors.map(([s]) => s).join(","), [sectors]);

  // Activate the first sector tab whenever the current tab no longer exists — on initial load,
  // and whenever a sector appears/disappears (e.g. adding the first office to an empty record).
  // Deliberately NOT keyed off every `sectors` change — otherwise saving one activity's edit
  // would reset the whole tab/expand state back to the first sector, fully collapsed, every time.
  useEffect(() => {
    if (!sectors.length) { setActiveTab(""); return; }
    if (sectors.some(([s]) => s === activeTab)) return;
    const [firstSector, firstOffices] = sectors[0];
    setActiveTab(firstSector);
    const c = allCollapsed(firstOffices);
    setCollapsedOffices(c.offices);
    setCollapsedPrograms(c.programs);
    setCollapsedProjects(c.projects);
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [sectorKeys]);

  // On tab switch: collapse all nodes in the new sector so only office rows render.
  const handleTabChange = useCallback((sector: string, offices: AipOfficeDetail[]) => {
    setActiveTab(sector);
    const c = allCollapsed(offices);
    setCollapsedOffices(c.offices);
    setCollapsedPrograms(c.programs);
    setCollapsedProjects(c.projects);
  }, []);

  // Memoised summary counts.
  const programCount  = useMemo(() => record?.offices.flatMap((o) => o.programs).length ?? 0, [record]);
  const projectCount  = useMemo(() => record?.offices.flatMap((o) => o.programs).flatMap((p) => p.projects).length ?? 0, [record]);
  const activityCount = useMemo(() => record?.offices.flatMap((o) => o.programs).flatMap((p) => p.projects).flatMap((p) => p.activities).length ?? 0, [record]);

  // V18-38 — re-upload runs the same .xlsm importer as a first upload, so it is frozen to the
  // same fiscal years. Records this can fire on should not exist (only an upload sets
  // entrySource "Upload", and uploads can no longer reach these years) — but rows imported
  // before the freeze can, so the button is disabled with its reason rather than sending the
  // user to a page that refuses them on arrival.
  const reuploadFrozen = record ? aipUploadRefusal(record.fiscalYear) : null;

  const activeOffices = useMemo(
    () => sectors.find(([s]) => s === activeTab)?.[1] ?? [],
    [sectors, activeTab]
  );

  const actions: TreeActions | null = record ? {
    aipRecordId: record.id,
    canEdit: record.status === "Draft",
    programsLdipOnly: aipProgramsAreLdipOnly(record.fiscalYear),
    fundingSources,
    onRequestConfirm: requestConfirm,
    onOfficeUpdated: handleOfficeUpdated,
    onOfficeDeleted: handleOfficeDeleted,
    onProgramAdded: handleProgramAdded,
    onProgramUpdated: handleProgramUpdated,
    onProgramDeleted: handleProgramDeleted,
    onProjectAdded: handleProjectAdded,
    onProjectUpdated: handleProjectUpdated,
    onProjectDeleted: handleProjectDeleted,
    onActivityAdded: handleActivityAdded,
    onActivitySaved: handleActivitySaved,
    onActivityDeleted: handleActivityDeleted,
  } : null;

  // ── Guards ───────────────────────────────────────────────────────────────────

  if (!me) return null;

  if (isNaN(id))
    return (
      <div className="p-8">
        <p className="text-red-600 text-sm mb-3">Invalid AIP record ID.</p>
        <Link href="/budget-planning/aip" className="text-sm text-green-700 hover:underline">← Back to AIP list</Link>
      </div>
    );

  if (loading)
    return <div className="p-8 text-slate-600 text-sm">Loading AIP record…</div>;

  if (error || !record)
    return (
      <div className="p-8">
        <p className="text-red-600 text-sm mb-3">{error ?? "Record not found."}</p>
        <Link href="/budget-planning/aip" className="text-sm text-green-700 hover:underline">← Back to AIP list</Link>
      </div>
    );

  // ── Render ───────────────────────────────────────────────────────────────────

  return (
    <div className="p-6 max-w-screen-2xl mx-auto">

      {/* ── Header ─────────────────────────────────────────────────── */}
      <div className="flex items-start justify-between mb-4 gap-4">
        <div>
          <div className="flex items-center gap-3 mb-1">
            <h1 className="text-xl font-bold text-slate-800">
              Annual Investment Program — FY {record.fiscalYear}
            </h1>
            <StatusBadge status={record.status} />
          </div>
          <p className="text-sm text-slate-600">
            {record.offices.length} office records &middot; {programCount} programs &middot;{" "}
            {projectCount} projects &middot; {activityCount} activities
          </p>
          {record.originalFilename && (
            <p className="text-xs text-slate-600 mt-0.5">Source: {record.originalFilename}</p>
          )}
        </div>
        <div className="flex items-center gap-4 shrink-0">
          {/* Re-upload (RAL-178): correct an uploaded AIP by importing a fixed file into THIS
              same record. Draft + Upload-entry-source only (Final needs admin Unlock first),
              only for PPDO uploaders, and blocked once a WFP has been built from this AIP —
              replacing the hierarchy would delete AipActivity rows the WFP's activities still
              reference (FK Restrict), so the backend rejects it; hide the button instead of
              letting the user hit that error. */}
          {record.status === "Draft" && record.entrySource === "Upload" && me?.canUploadAip === true && (
            reuploadFrozen ? (
              <span
                className="px-3 py-1.5 text-sm font-medium text-slate-400 bg-slate-100 border border-slate-200 whitespace-nowrap cursor-not-allowed"
                title={reuploadFrozen}
              >
                Re-upload File
              </span>
            ) : record.hasWfpUsage ? (
              <span
                className="px-3 py-1.5 text-sm font-medium text-slate-400 bg-slate-100 border border-slate-200 whitespace-nowrap cursor-not-allowed"
                title="A Work Financial Plan has already been built from this AIP. Archive this record and upload the corrected file as a new AIP instead."
              >
                Re-upload File
              </span>
            ) : (
              <Link
                href={`/budget-planning/aip/new?replaceId=${record.id}`}
                className="px-3 py-1.5 text-sm font-medium text-white bg-green-700 hover:bg-green-800 transition-colors whitespace-nowrap"
              >
                Re-upload File
              </Link>
            )
          )}
          <Link href="/budget-planning/aip" className="text-sm text-green-700 hover:underline whitespace-nowrap">
            ← Back to AIP list
          </Link>
        </div>
      </div>
      {record.status === "Draft" && record.entrySource === "Upload" && me?.canUploadAip === true &&
        reuploadFrozen && (
        <div className="mb-4 -mt-2 border border-amber-200 bg-amber-50 px-4 py-2.5 text-xs text-amber-800">
          Re-upload is disabled &mdash; {reuploadFrozen}
        </div>
      )}
      {record.status === "Draft" && record.entrySource === "Upload" && me?.canUploadAip === true &&
        !reuploadFrozen && record.hasWfpUsage && (
        <div className="mb-4 -mt-2 border border-amber-200 bg-amber-50 px-4 py-2.5 text-xs text-amber-800">
          Re-upload is disabled — a Work Financial Plan has already been built from this AIP. Archive
          this record and upload the corrected file as a new AIP instead.
        </div>
      )}

      {/* Add Office (detail-page CRUD follow-up to RAL-179) + Seed from LDIP (RAL-181) —
          Draft-only, same gate as everything below. Carry Forward Office was removed by PPDO-63:
          the PDC wants offices building their AIP from scratch. */}
      {record.status === "Draft" && (
        <div className="flex flex-wrap items-start gap-3 mb-4">
          <AddOfficePanel aipRecordId={record.id} officeConfigs={officeConfigs} onAdded={handleOfficeAdded} />
          <SeedFromLdipPanel
            targetFiscalYear={record.fiscalYear}
            officeConfigs={officeConfigs}
            onSeeded={handleOfficeSeeded}
            ldipOnly={aipProgramsAreLdipOnly(record.fiscalYear)}
          />
        </div>
      )}

      {/* ── Sector tabs ────────────────────────────────────────────── */}
      <div className="flex border-b border-slate-200 mb-0">
        {sectors.map(([sector, offices]) => {
          const sActCount = offices
            .flatMap((o) => o.programs)
            .flatMap((p) => p.projects)
            .flatMap((p) => p.activities).length;
          const isActive = activeTab === sector;
          return (
            <button
              key={sector}
              onClick={() => handleTabChange(sector, offices)}
              className={`px-5 py-2.5 text-xs font-semibold uppercase tracking-wider border-b-2 transition-colors whitespace-nowrap ${
                isActive
                  ? "border-green-700 text-green-700 bg-green-50"
                  : "border-transparent text-slate-600 hover:text-slate-800 hover:bg-slate-50"
              }`}
            >
              {sector}
              <span className="ml-1.5 text-[10px] font-normal opacity-60">
                {offices.length}o · {sActCount}a
              </span>
            </button>
          );
        })}
      </div>

      {/* ── Table — only active sector's rows are in the DOM ───────── */}
      <div className="border border-t-0 border-slate-200 shadow-sm">
        <table className="min-w-[1800px] w-full border-collapse text-sm">
          <colgroup>
            <col style={{ width: "140px" }} />
            <col style={{ width: "300px" }} />
            <col style={{ width: "56px"  }} />
            <col style={{ width: "110px" }} />
            <col style={{ width: "72px"  }} />
            <col style={{ width: "72px"  }} />
            <col style={{ width: "200px" }} />
            <col style={{ width: "90px"  }} />
            <col style={{ width: "90px"  }} />
            <col style={{ width: "90px"  }} />
            <col style={{ width: "90px"  }} />
            <col style={{ width: "100px" }} />
            <col style={{ width: "80px"  }} />
            <col style={{ width: "80px"  }} />
            <col style={{ width: "70px"  }} />
            <col style={{ width: "80px"  }} />
          </colgroup>

          <thead className="sticky top-0 z-10">
            <tr>
              <TH rowSpan={2}>AIP Ref Code</TH>
              <TH rowSpan={2}>Program / Project / Activity Description</TH>
              <TH rowSpan={2} align="center">eSRE Code</TH>
              <TH rowSpan={2}>Implementing Office</TH>
              <TH colSpan={2} align="center">Schedule of Implementation</TH>
              <TH rowSpan={2}>Expected Outputs</TH>
              <TH rowSpan={2} align="center">Funding Source</TH>
              <TH colSpan={4} align="center">Amount (in ₱000)</TH>
              <TH colSpan={3} align="center">CC Expenditure (₱000)</TH>
              <TH rowSpan={2} align="center">Actions</TH>
            </tr>
            <tr>
              <TH align="center">Start</TH>
              <TH align="center">End</TH>
              <TH align="right">PS</TH>
              <TH align="right">MOOE</TH>
              <TH align="right">CO</TH>
              <TH align="right">Total</TH>
              <TH align="right">Adaptation</TH>
              <TH align="right">Mitigation</TH>
              <TH align="center">CC Code</TH>
            </tr>
          </thead>

          <tbody>
            {actions && activeOffices.map((office) => (
              <OfficeRow
                key={`office-${office.id}`}
                office={office}
                open={!collapsedOffices.has(office.id)}
                onToggle={() => toggleOffice(office.id)}
                collapsedPrograms={collapsedPrograms}
                onToggleProgram={toggleProgram}
                collapsedProjects={collapsedProjects}
                onToggleProject={toggleProject}
                actions={actions}
              />
            ))}
          </tbody>
        </table>
      </div>

      {confirmDialog && <ConfirmDialog {...confirmDialog} />}

      <p className="text-xs text-slate-600 mt-2 text-right">
        {activityCount} activities across {record.offices.length} office records
      </p>
    </div>
  );
}
