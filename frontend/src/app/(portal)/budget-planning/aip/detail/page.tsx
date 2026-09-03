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
  getAipById, aipErrorMessage, listAip,
  addAipOffice, addAipProgram, addAipProject, addAipActivity,
  updateAipOffice, updateAipProgram, updateAipProject, updateAipActivity,
  deleteAipOffice, deleteAipProgram, deleteAipProject, deleteAipActivity,
  copyAipOfficeFromPriorYear, seedAipProgramsFromLdip,
} from "@/lib/aip";
import { listLdip, getLdipById } from "@/lib/ldip";
import { listOffices, listFundingSources } from "@/lib/config";
import { aipUploadRefusal } from "@/lib/aip-shape";
import {
  AIP_MONTHS, AIP_ESRE_OPTIONS, AIP_SECTOR_OPTIONS, AIP_SECTOR_PREFIX, AIP_FUNCTION_BANDS,
} from "@/lib/aipConstants";
import MoneyInput from "@/components/ui/MoneyInput";
import ConfirmDialog, { type ConfirmDialogProps } from "@/components/ui/ConfirmDialog";
import type {
  AipRecordDetail,
  AipRecordResponse,
  AipOfficeDetail,
  AipProgramDetail,
  AipProjectDetail,
  AipActivityDetail,
  FundingSourceResponse,
  OfficeResponse,
  LdipOfficeGroup,
} from "@/types";

// ── Chevron ────────────────────────────────────────────────────────────────────

function Chevron({ open, className = "" }: { open: boolean; className?: string }) {
  return (
    <svg viewBox="0 0 12 12" width="10" height="10"
      className={`inline-block shrink-0 transition-transform duration-100 ${open ? "rotate-90" : ""} ${className}`}
      fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round"
    >
      <polyline points="4,2 8,6 4,10" />
    </svg>
  );
}

// ── Number helpers ─────────────────────────────────────────────────────────────

function fmt(n: number | null | undefined): string {
  if (n == null || n === 0) return "—";
  return n.toLocaleString("en-PH", { minimumFractionDigits: 2, maximumFractionDigits: 2 });
}

// ── Units: pesos in storage, ₱000 on this page (V18-35 / PPDO-34, decision P2-a) ──────────────
//
// AIP amounts are stored in PESOS for every fiscal year. The province's AIP form is denominated
// in thousands and the encoders read and type it that way, so this page — and only this page —
// converts at its edge: divide when drawing a figure, multiply when saving one. The "(in ₱000)"
// column headers stay true, and nothing a user sees or types changed when storage did.
//
// ⚠️ Both directions or neither. Converting only the display leaves every subsequent edit
// dividing the record by a thousand, because the input would post back what it was shown.
const PESOS_PER_DISPLAY_UNIT = 1000;

/** Pesos as stored → the ₱000 figure this page shows and accepts. */
function toDisplayUnits(pesos: number | null | undefined): number | null {
  return pesos == null ? null : pesos / PESOS_PER_DISPLAY_UNIT;
}

/** A ₱000 figure typed on this page → pesos for storage. */
function toStorageUnits(displayed: number | null | undefined): number | null {
  return displayed == null ? null : displayed * PESOS_PER_DISPLAY_UNIT;
}

function sumActivities(
  office: AipOfficeDetail,
  field: keyof Pick<AipActivityDetail, "ps" | "mooe" | "co" | "total">
): number {
  return office.programs
    .flatMap((p) => p.projects)
    .flatMap((p) => p.activities)
    .reduce((s, a) => s + (a[field] ?? 0), 0);
}

// RAL-179 — immutably replaces one activity in the nested tree after a successful inline edit.
function replaceActivity(record: AipRecordDetail, updated: AipActivityDetail): AipRecordDetail {
  return {
    ...record,
    offices: record.offices.map((o) => ({
      ...o,
      programs: o.programs.map((p) => ({
        ...p,
        projects: p.projects.map((j) =>
          j.id !== updated.projectId ? j : {
            ...j,
            activities: j.activities.map((a) => (a.id === updated.id ? updated : a)),
          }
        ),
      })),
    })),
  };
}

// ── Detail-page CRUD follow-up to RAL-179 — generic immutable tree updates for the other three
// levels. Every DTO already carries its immediate parent's id (officeId/programId/projectId), so
// these locate the right spot the same way replaceActivity does above — no need to thread parent
// ids through component props separately.

function removeActivityFromTree(record: AipRecordDetail, projectId: number, activityId: number): AipRecordDetail {
  return {
    ...record,
    offices: record.offices.map((o) => ({
      ...o,
      programs: o.programs.map((p) => ({
        ...p,
        projects: p.projects.map((j) =>
          j.id !== projectId ? j : { ...j, activities: j.activities.filter((a) => a.id !== activityId) }
        ),
      })),
    })),
  };
}

function addActivityToTree(record: AipRecordDetail, projectId: number, newActivity: AipActivityDetail): AipRecordDetail {
  return {
    ...record,
    offices: record.offices.map((o) => ({
      ...o,
      programs: o.programs.map((p) => ({
        ...p,
        projects: p.projects.map((j) =>
          j.id !== projectId ? j : { ...j, activities: [...j.activities, newActivity] }
        ),
      })),
    })),
  };
}

function replaceProject(record: AipRecordDetail, updated: AipProjectDetail): AipRecordDetail {
  return {
    ...record,
    offices: record.offices.map((o) => ({
      ...o,
      programs: o.programs.map((p) =>
        p.id !== updated.programId ? p : { ...p, projects: p.projects.map((j) => (j.id === updated.id ? updated : j)) }
      ),
    })),
  };
}

function removeProjectFromTree(record: AipRecordDetail, programId: number, projectId: number): AipRecordDetail {
  return {
    ...record,
    offices: record.offices.map((o) => ({
      ...o,
      programs: o.programs.map((p) =>
        p.id !== programId ? p : { ...p, projects: p.projects.filter((j) => j.id !== projectId) }
      ),
    })),
  };
}

function addProjectToTree(record: AipRecordDetail, programId: number, newProject: AipProjectDetail): AipRecordDetail {
  return {
    ...record,
    offices: record.offices.map((o) => ({
      ...o,
      programs: o.programs.map((p) =>
        p.id !== programId ? p : { ...p, projects: [...p.projects, newProject] }
      ),
    })),
  };
}

function replaceProgram(record: AipRecordDetail, updated: AipProgramDetail): AipRecordDetail {
  return {
    ...record,
    offices: record.offices.map((o) =>
      o.id !== updated.officeId ? o : { ...o, programs: o.programs.map((p) => (p.id === updated.id ? updated : p)) }
    ),
  };
}

function removeProgramFromTree(record: AipRecordDetail, officeId: number, programId: number): AipRecordDetail {
  return {
    ...record,
    offices: record.offices.map((o) =>
      o.id !== officeId ? o : { ...o, programs: o.programs.filter((p) => p.id !== programId) }
    ),
  };
}

function addProgramToTree(record: AipRecordDetail, officeId: number, newProgram: AipProgramDetail): AipRecordDetail {
  return {
    ...record,
    offices: record.offices.map((o) =>
      o.id !== officeId ? o : { ...o, programs: [...o.programs, newProgram] }
    ),
  };
}

function replaceOffice(record: AipRecordDetail, updated: AipOfficeDetail): AipRecordDetail {
  return { ...record, offices: record.offices.map((o) => (o.id === updated.id ? updated : o)) };
}

function removeOfficeFromTree(record: AipRecordDetail, officeId: number): AipRecordDetail {
  return { ...record, offices: record.offices.filter((o) => o.id !== officeId) };
}

function addOfficeToTree(record: AipRecordDetail, newOffice: AipOfficeDetail): AipRecordDetail {
  return { ...record, offices: [...record.offices, newOffice] };
}

// ── Status badge ──────────────────────────────────────────────────────────────

function StatusBadge({ status }: { status: string }) {
  const cls =
    status === "Final"  ? "bg-green-100 text-green-700" :
    status === "Draft"  ? "bg-amber-100 text-amber-700" :
                          "bg-slate-100 text-slate-600";
  return <span className={`px-2 py-0.5 text-xs font-medium ${cls}`}>{status}</span>;
}

// ── Table header cell ──────────────────────────────────────────────────────────

function TH({ children, align = "left", rowSpan, colSpan }: {
  children: React.ReactNode;
  align?: "left" | "right" | "center";
  rowSpan?: number;
  colSpan?: number;
}) {
  const a = align === "right" ? "text-right" : align === "center" ? "text-center" : "text-left";
  return (
    <th rowSpan={rowSpan} colSpan={colSpan}
      className={`px-2 py-2 text-[10px] font-bold uppercase tracking-wide text-slate-600 whitespace-nowrap border-b border-slate-300 bg-slate-100 ${a}`}
    >
      {children}
    </th>
  );
}

// ── Amount cell ────────────────────────────────────────────────────────────────

function AmtTD({ value, bold = false, white = false }: { value: number | null | undefined; bold?: boolean; white?: boolean }) {
  return (
    <td className={`px-2 py-1.5 text-right text-xs tabular-nums whitespace-nowrap ${
      white ? "text-white font-semibold" : bold ? "font-semibold text-slate-800" : "text-slate-600"
    }`}>
      {fmt(toDisplayUnits(value))}
    </td>
  );
}

// ── Activity row (RAL-179 — inline edit) ─────────────────────────────────────
// A read-only row that swaps to an edit form in place when the user clicks Edit — no whole-page
// submit, Save/Cancel per row. RefCode/ProjectId/identity are never editable here.

const selectCls = "border border-slate-300 bg-white text-xs px-1.5 py-1 text-slate-600 w-full focus:outline-none focus:ring-1 focus:ring-green-600";
const inputCls  = "border border-slate-300 bg-white text-xs px-1.5 py-1 text-slate-600 w-full focus:outline-none focus:ring-1 focus:ring-green-600";

function ActivityRow({
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
  // Money state is held in DISPLAY units (₱000) for as long as it is on screen — the inputs show
  // it, the live row total sums it, and handleSave converts it back to pesos on the way out.
  const [ps, setPs]                     = useState<number | null>(toDisplayUnits(act.ps));
  const [mooe, setMooe]                 = useState<number | null>(toDisplayUnits(act.mooe));
  const [co, setCo]                     = useState<number | null>(toDisplayUnits(act.co));
  const [ccAdaptation, setCcAdaptation] = useState<number | null>(toDisplayUnits(act.ccAdaptation));
  const [ccMitigation, setCcMitigation] = useState<number | null>(toDisplayUnits(act.ccMitigation));
  const [ccTypologyCode, setCcTypologyCode] = useState(act.ccTypologyCode ?? "");

  function startEdit() {
    setName(act.name);
    setEsreCode(act.esreCode ?? "");
    setImplementingOffice(act.implementingOffice ?? "");
    setStartDate(act.startDate ?? "");
    setEndDate(act.endDate ?? "");
    setExpectedOutputs(act.expectedOutputs ?? "");
    setFundingSourceId(act.fundingSourceId != null ? String(act.fundingSourceId) : "");
    setPs(toDisplayUnits(act.ps));
    setMooe(toDisplayUnits(act.mooe));
    setCo(toDisplayUnits(act.co));
    setCcAdaptation(toDisplayUnits(act.ccAdaptation));
    setCcMitigation(toDisplayUnits(act.ccMitigation));
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
        ps:           toStorageUnits(ps),
        mooe:         toStorageUnits(mooe),
        co:           toStorageUnits(co),
        ccAdaptation: toStorageUnits(ccAdaptation),
        ccMitigation: toStorageUnits(ccMitigation),
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
      <td className="px-1 py-1.5"><MoneyInput value={ps} onChange={setPs} className="w-full" /></td>
      <td className="px-1 py-1.5"><MoneyInput value={mooe} onChange={setMooe} className="w-full" /></td>
      <td className="px-1 py-1.5"><MoneyInput value={co} onChange={setCo} className="w-full" /></td>
      <td className="px-2 py-1.5 text-right text-xs tabular-nums font-semibold text-slate-800">
        {fmt((ps ?? 0) + (mooe ?? 0) + (co ?? 0))}
      </td>
      <td className="px-1 py-1.5"><MoneyInput value={ccAdaptation} onChange={setCcAdaptation} className="w-full" /></td>
      <td className="px-1 py-1.5"><MoneyInput value={ccMitigation} onChange={setCcMitigation} className="w-full" /></td>
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

// ── Detail-page CRUD follow-up to RAL-179 — Office/Program/Project add+edit+delete ──
// Bundles every tree-mutation callback into one object so ProjectRow/ProgramRow/OfficeRow don't
// each need a dozen individual props; ActivityRow (above) keeps its own explicit prop list
// unchanged since it predates this and already works.

interface TreeActions {
  aipRecordId: number;
  canEdit: boolean;
  fundingSources: FundingSourceResponse[];
  onRequestConfirm: (props: ConfirmDialogProps) => void;
  onOfficeUpdated: (updated: AipOfficeDetail) => void;
  onOfficeDeleted: (officeId: number) => void;
  onProgramAdded: (officeId: number, newProgram: AipProgramDetail) => void;
  onProgramUpdated: (updated: AipProgramDetail) => void;
  onProgramDeleted: (officeId: number, programId: number) => void;
  onProjectAdded: (programId: number, newProject: AipProjectDetail) => void;
  onProjectUpdated: (updated: AipProjectDetail) => void;
  onProjectDeleted: (programId: number, projectId: number) => void;
  onActivityAdded: (projectId: number, newActivity: AipActivityDetail) => void;
  onActivitySaved: (updated: AipActivityDetail) => void;
  onActivityDeleted: (projectId: number, activityId: number) => void;
}

// ── Add Activity (inline form row, appended under an expanded project) ──────────

function AddActivityRow({
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
        ps:           toStorageUnits(ps),
        mooe:         toStorageUnits(mooe),
        co:           toStorageUnits(co),
        ccAdaptation: toStorageUnits(ccAdaptation),
        ccMitigation: toStorageUnits(ccMitigation),
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
      <td className="px-1 py-1.5"><MoneyInput value={ps} onChange={setPs} className="w-full" /></td>
      <td className="px-1 py-1.5"><MoneyInput value={mooe} onChange={setMooe} className="w-full" /></td>
      <td className="px-1 py-1.5"><MoneyInput value={co} onChange={setCo} className="w-full" /></td>
      <td className="px-2 py-1.5 text-right text-xs tabular-nums font-semibold text-slate-800">
        {fmt((ps ?? 0) + (mooe ?? 0) + (co ?? 0))}
      </td>
      <td className="px-1 py-1.5"><MoneyInput value={ccAdaptation} onChange={setCcAdaptation} className="w-full" /></td>
      <td className="px-1 py-1.5"><MoneyInput value={ccMitigation} onChange={setCcMitigation} className="w-full" /></td>
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

// ── Project row — Edit/Delete for itself, renders its activities + Add Activity ──

function ProjectRow({
  proj, open, onToggle, actions,
}: {
  proj: AipProjectDetail;
  open: boolean;
  onToggle: () => void;
  actions: TreeActions;
}) {
  const [editing, setEditing] = useState(false);
  const [name, setName]       = useState(proj.name);
  const [saving, setSaving]   = useState(false);
  const [error, setError]     = useState<string | null>(null);

  function startEdit() { setName(proj.name); setError(null); setEditing(true); }

  async function handleSave() {
    if (!name.trim()) { setError("Name is required."); return; }
    setSaving(true);
    setError(null);
    try {
      const updated = await updateAipProject(proj.id, { name: name.trim() });
      // Server response omits Activities by convention — merge onto the existing node.
      actions.onProjectUpdated({ ...proj, name: updated.name });
      setEditing(false);
    } catch (err) {
      setError(aipErrorMessage(err, "Could not save changes."));
    } finally {
      setSaving(false);
    }
  }

  function confirmDelete() {
    actions.onRequestConfirm({
      title: "Delete Project?",
      message: `This removes "${proj.name}" and its ${proj.activities.length} activit${proj.activities.length !== 1 ? "ies" : "y"}. This cannot be undone.`,
      confirmLabel: "Delete",
      variant: "danger",
      onConfirm: async () => {
        try {
          await deleteAipProject(proj.id);
          actions.onProjectDeleted(proj.programId, proj.id);
        } catch (err) {
          setError(aipErrorMessage(err, "Could not delete project."));
        }
      },
      onClose: () => {},
    });
  }

  return (
    <>
      <tr className="bg-slate-50 border-t border-slate-200 align-top">
        <td className="px-2 py-1.5 pl-9 font-mono text-xs text-slate-600 border-l-4 border-slate-300">
          <button onClick={onToggle} className="flex items-center gap-1.5 text-left">
            <Chevron open={open} className="text-slate-400" />
            {proj.refCode}
          </button>
        </td>
        {editing ? (
          <td colSpan={14} className="px-2 py-1">
            <div className="flex items-center gap-2 flex-wrap">
              <input value={name} onChange={(e) => setName(e.target.value)} className={`${inputCls} max-w-md`} />
              <button onClick={handleSave} disabled={saving} className={`text-xs font-medium ${saving ? "text-green-300" : "text-green-700 hover:underline"}`}>
                {saving ? "Saving…" : "Save"}
              </button>
              <button onClick={() => setEditing(false)} disabled={saving} className="text-xs text-slate-600 hover:underline disabled:opacity-50">
                Cancel
              </button>
              {error && <span className="text-[10px] text-danger-600">{error}</span>}
            </div>
          </td>
        ) : (
          <td colSpan={14} className="px-2 py-1.5 text-xs font-medium text-slate-600">
            {proj.name}
            {proj.isSynthetic && (
              <span
                className="ml-2 px-1.5 py-0.5 text-[10px] font-normal bg-amber-100 text-amber-700"
                title="This project does not exist in the source file — it was created to hold a line item recorded directly on the parent program row."
              >
                program-level entry
              </span>
            )}
            {!open && (
              <span className="ml-2 font-normal text-[10px] text-slate-600">
                {proj.activities.length} activities
              </span>
            )}
          </td>
        )}
        <td className="px-2 py-1.5 text-center whitespace-nowrap">
          {actions.canEdit && !editing && (
            <span className="inline-flex gap-2">
              <button onClick={startEdit} className="text-xs text-green-700 hover:underline">Edit</button>
              <button onClick={confirmDelete} className="text-xs text-danger-500 hover:underline">Delete</button>
            </span>
          )}
        </td>
      </tr>

      {open && proj.activities.map((act) => (
        <ActivityRow
          key={`act-${act.id}`}
          act={act}
          aipRecordId={actions.aipRecordId}
          canEdit={actions.canEdit}
          fundingSources={actions.fundingSources}
          onSaved={actions.onActivitySaved}
          onDeleted={(activityId) => actions.onActivityDeleted(proj.id, activityId)}
          onRequestConfirm={actions.onRequestConfirm}
        />
      ))}

      {open && actions.canEdit && (
        <AddActivityRow
          projectId={proj.id}
          fundingSources={actions.fundingSources}
          onAdded={(newAct) => actions.onActivityAdded(proj.id, newAct)}
        />
      )}
    </>
  );
}

// ── Add Project (inline form row, appended under an expanded program) ───────────

function AddProjectRow({ programId, onAdded }: { programId: number; onAdded: (newProject: AipProjectDetail) => void }) {
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

// ── Program row — Edit/Delete for itself, renders its projects + Add Project ────

function ProgramRow({
  prog, open, onToggle, collapsedProjects, onToggleProject, actions,
}: {
  prog: AipProgramDetail;
  open: boolean;
  onToggle: () => void;
  collapsedProjects: Set<number>;
  onToggleProject: (id: number) => void;
  actions: TreeActions;
}) {
  const [editing, setEditing]           = useState(false);
  const [name, setName]                 = useState(prog.name);
  const [functionBand, setFunctionBand] = useState(prog.functionBand ?? "CORE");
  const [saving, setSaving]             = useState(false);
  const [error, setError]               = useState<string | null>(null);

  function startEdit() {
    setName(prog.name);
    setFunctionBand(prog.functionBand ?? "CORE");
    setError(null);
    setEditing(true);
  }

  async function handleSave() {
    if (!name.trim()) { setError("Name is required."); return; }
    setSaving(true);
    setError(null);
    try {
      const updated = await updateAipProgram(prog.id, { name: name.trim(), functionBand });
      // Server response omits Projects by convention — merge onto the existing node.
      actions.onProgramUpdated({ ...prog, name: updated.name, functionBand: updated.functionBand });
      setEditing(false);
    } catch (err) {
      setError(aipErrorMessage(err, "Could not save changes."));
    } finally {
      setSaving(false);
    }
  }

  function confirmDelete() {
    const activityCount = prog.projects.reduce((n, j) => n + j.activities.length, 0);
    actions.onRequestConfirm({
      title: "Delete Program?",
      message: `This removes "${prog.name}" and everything under it (${prog.projects.length} project${prog.projects.length !== 1 ? "s" : ""}, ${activityCount} activit${activityCount !== 1 ? "ies" : "y"}). This cannot be undone.`,
      confirmLabel: "Delete",
      variant: "danger",
      onConfirm: async () => {
        try {
          await deleteAipProgram(prog.id);
          actions.onProgramDeleted(prog.officeId, prog.id);
        } catch (err) {
          setError(aipErrorMessage(err, "Could not delete program."));
        }
      },
      onClose: () => {},
    });
  }

  return (
    <>
      <tr className="bg-white border-t-2 border-slate-200 align-top">
        <td className="px-2 py-1.5 pl-5 font-mono text-xs text-slate-600 border-l-4 border-green-400">
          <button onClick={onToggle} className="flex items-center gap-1.5 text-left">
            <Chevron open={open} className="text-green-500" />
            {prog.refCode}
          </button>
        </td>
        {editing ? (
          <td colSpan={14} className="px-2 py-1">
            <div className="flex items-center gap-2 flex-wrap">
              <input value={name} onChange={(e) => setName(e.target.value)} className={`${inputCls} max-w-md`} />
              <select value={functionBand} onChange={(e) => setFunctionBand(e.target.value)} className={`${selectCls} w-32`}>
                {AIP_FUNCTION_BANDS.map((b) => <option key={b} value={b}>{b}</option>)}
              </select>
              <button onClick={handleSave} disabled={saving} className={`text-xs font-medium ${saving ? "text-green-300" : "text-green-700 hover:underline"}`}>
                {saving ? "Saving…" : "Save"}
              </button>
              <button onClick={() => setEditing(false)} disabled={saving} className="text-xs text-slate-600 hover:underline disabled:opacity-50">
                Cancel
              </button>
              {error && <span className="text-[10px] text-danger-600">{error}</span>}
            </div>
          </td>
        ) : (
          <td colSpan={14} className="px-2 py-1.5 font-semibold text-xs italic text-slate-600 uppercase tracking-wide">
            {prog.name}
            {!open && (
              <span className="ml-2 font-normal not-italic text-[10px] text-slate-600">
                {prog.projects.length} projects ·{" "}
                {prog.projects.flatMap((p) => p.activities).length} activities
              </span>
            )}
          </td>
        )}
        <td className="px-2 py-1.5 text-center whitespace-nowrap">
          {actions.canEdit && !editing && (
            <span className="inline-flex gap-2">
              <button onClick={startEdit} className="text-xs text-green-700 hover:underline">Edit</button>
              <button onClick={confirmDelete} className="text-xs text-danger-500 hover:underline">Delete</button>
            </span>
          )}
        </td>
      </tr>

      {open && prog.projects.map((proj) => (
        <ProjectRow
          key={`proj-${proj.id}`}
          proj={proj}
          open={!collapsedProjects.has(proj.id)}
          onToggle={() => onToggleProject(proj.id)}
          actions={actions}
        />
      ))}

      {open && actions.canEdit && (
        <AddProjectRow programId={prog.id} onAdded={(newProj) => actions.onProjectAdded(prog.id, newProj)} />
      )}
    </>
  );
}

// ── Add Program (inline form row, appended under an expanded office) ────────────

function AddProgramRow({ officeId, onAdded }: { officeId: number; onAdded: (newProgram: AipProgramDetail) => void }) {
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
        <AddProgramRow officeId={office.id} onAdded={(newProg) => actions.onProgramAdded(office.id, newProg)} />
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

// ── Carry Forward Office (RAL-180) ──────────────────────────────────────────────
//
// Copies selected programs (full project/activity subtrees) from a prior fiscal year's
// office into THIS record's fiscal year. Placement decision: lives on the detail page
// (an existing target record), not as a third tab on aip/new/page.tsx — covers the main
// use case ("this year's record already exists, seed one more office into it") including
// "an office that doesn't exist yet in the current year." The backend also supports
// auto-creating the target record itself when none exists yet, but that path isn't reached
// from this UI — a user hits Manual Entry first (two seconds) to get a Draft record, then
// uses this panel, same as adding an office by hand.
function CarryForwardOfficePanel({
  targetFiscalYear, onCopied,
}: {
  targetFiscalYear: number;
  onCopied: (office: AipOfficeDetail) => void;
}) {
  const [open, setOpen]                     = useState(false);
  const [sourceFiscalYear, setSourceFiscalYear] = useState(String(targetFiscalYear - 1));
  const [sourceRecords, setSourceRecords]   = useState<AipRecordResponse[]>([]);
  const [sourceRecordId, setSourceRecordId] = useState("");
  const [sourceRecord, setSourceRecord]     = useState<AipRecordDetail | null>(null);
  const [sourceOfficeId, setSourceOfficeId] = useState("");
  const [checkedProgramIds, setCheckedProgramIds] = useState<Set<number>>(new Set());
  const [loadingYear, setLoadingYear]       = useState(false);
  const [loadingOffices, setLoadingOffices] = useState(false);
  const [saving, setSaving]                 = useState(false);
  const [error, setError]                   = useState<string | null>(null);

  const selectedSourceOffice = sourceRecord?.offices.find((o) => String(o.id) === sourceOfficeId) ?? null;

  async function handleLoadYear() {
    const fy = Number(sourceFiscalYear);
    if (!fy) { setError("Enter a fiscal year."); return; }
    setLoadingYear(true);
    setError(null);
    setSourceRecords([]);
    setSourceRecordId("");
    setSourceRecord(null);
    setSourceOfficeId("");
    setCheckedProgramIds(new Set());
    try {
      const records = await listAip({ fiscalYear: fy });
      if (records.length === 0) setError(`No AIP found for FY ${fy}.`);
      setSourceRecords(records);
    } catch (err) {
      setError(aipErrorMessage(err, "Could not load that fiscal year."));
    } finally {
      setLoadingYear(false);
    }
  }

  async function handlePickRecord(id: string) {
    setSourceRecordId(id);
    setSourceRecord(null);
    setSourceOfficeId("");
    setCheckedProgramIds(new Set());
    if (!id) return;
    setLoadingOffices(true);
    setError(null);
    try {
      setSourceRecord(await getAipById(Number(id)));
    } catch (err) {
      setError(aipErrorMessage(err, "Could not load that AIP record."));
    } finally {
      setLoadingOffices(false);
    }
  }

  function handlePickOffice(id: string) {
    setSourceOfficeId(id);
    const office = sourceRecord?.offices.find((o) => String(o.id) === id);
    // Select All checked by default (matches the ticket's UX default).
    setCheckedProgramIds(new Set(office?.programs.map((p) => p.id) ?? []));
  }

  function toggleProgram(id: number) {
    setCheckedProgramIds((prev) => {
      const next = new Set(prev);
      if (next.has(id)) next.delete(id); else next.add(id);
      return next;
    });
  }

  function toggleSelectAll() {
    if (!selectedSourceOffice) return;
    setCheckedProgramIds((prev) =>
      prev.size === selectedSourceOffice.programs.length
        ? new Set()
        : new Set(selectedSourceOffice.programs.map((p) => p.id))
    );
  }

  function resetAndClose() {
    setOpen(false);
    setSourceRecords([]);
    setSourceRecordId("");
    setSourceRecord(null);
    setSourceOfficeId("");
    setCheckedProgramIds(new Set());
    setError(null);
  }

  async function handleConfirm() {
    if (!selectedSourceOffice || checkedProgramIds.size === 0) {
      setError("Pick a source office and at least one program.");
      return;
    }
    setSaving(true);
    setError(null);
    try {
      const copied = await copyAipOfficeFromPriorYear({
        sourceOfficeId: selectedSourceOffice.id,
        targetFiscalYear,
        programIds: Array.from(checkedProgramIds),
      });
      onCopied(copied);
      resetAndClose();
    } catch (err) {
      setError(aipErrorMessage(err, "Could not carry forward that office."));
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
        + Carry Forward Office
      </button>
    );
  }

  return (
    <div className="border border-slate-200 bg-slate-50 p-4 mb-4 space-y-3">
      <div className="grid grid-cols-3 gap-3 items-end">
        <div>
          <label className="block text-xs font-semibold text-slate-600 uppercase tracking-wide mb-1">
            Source Fiscal Year
          </label>
          <div className="flex gap-2">
            <input
              type="number"
              value={sourceFiscalYear}
              onChange={(e) => setSourceFiscalYear(e.target.value)}
              className="border border-slate-300 bg-white text-sm px-3 py-2 text-slate-600 w-full focus:outline-none focus:ring-1 focus:ring-green-600"
            />
            <button
              onClick={handleLoadYear}
              disabled={loadingYear}
              className="px-3 py-2 text-sm font-medium border border-slate-300 text-slate-800 hover:bg-slate-100 transition-colors disabled:opacity-50 whitespace-nowrap"
            >
              {loadingYear ? "Loading…" : "Load"}
            </button>
          </div>
        </div>
        <div>
          <label className="block text-xs font-semibold text-slate-600 uppercase tracking-wide mb-1">
            Source AIP Record
          </label>
          <select
            value={sourceRecordId}
            onChange={(e) => handlePickRecord(e.target.value)}
            disabled={sourceRecords.length === 0}
            className="border border-slate-300 bg-white text-sm px-3 py-2 text-slate-600 w-full focus:outline-none focus:ring-1 focus:ring-green-600 disabled:bg-slate-100"
          >
            <option value="">Select…</option>
            {sourceRecords.map((r) => (
              <option key={r.id} value={r.id}>FY {r.fiscalYear} — {r.status} ({r.entrySource})</option>
            ))}
          </select>
        </div>
        <div>
          <label className="block text-xs font-semibold text-slate-600 uppercase tracking-wide mb-1">
            Source Office
          </label>
          <select
            value={sourceOfficeId}
            onChange={(e) => handlePickOffice(e.target.value)}
            disabled={!sourceRecord || loadingOffices}
            className="border border-slate-300 bg-white text-sm px-3 py-2 text-slate-600 w-full focus:outline-none focus:ring-1 focus:ring-green-600 disabled:bg-slate-100"
          >
            <option value="">{loadingOffices ? "Loading…" : "Select…"}</option>
            {sourceRecord?.offices.map((o) => (
              <option key={o.id} value={o.id}>{o.name} ({o.sector})</option>
            ))}
          </select>
        </div>
      </div>

      {selectedSourceOffice && (
        <div className="border border-slate-200 bg-white">
          <div className="flex items-center justify-between px-3 py-2 border-b border-slate-200">
            <label className="flex items-center gap-2 text-xs font-semibold text-slate-600 uppercase tracking-wide cursor-pointer">
              <input
                type="checkbox"
                checked={checkedProgramIds.size === selectedSourceOffice.programs.length && selectedSourceOffice.programs.length > 0}
                onChange={toggleSelectAll}
                className="accent-green-600"
              />
              Select All ({selectedSourceOffice.programs.length} programs)
            </label>
          </div>
          <div className="max-h-56 overflow-y-auto divide-y divide-slate-100">
            {selectedSourceOffice.programs.map((p) => {
              const activityCount = p.projects.flatMap((j) => j.activities).length;
              return (
                <label key={p.id} className="flex items-center gap-2 px-3 py-2 text-sm text-slate-600 cursor-pointer hover:bg-slate-50">
                  <input
                    type="checkbox"
                    checked={checkedProgramIds.has(p.id)}
                    onChange={() => toggleProgram(p.id)}
                    className="accent-green-600 shrink-0"
                  />
                  <span className="flex-1 truncate">{p.name}</span>
                  <span className="text-xs text-slate-600 whitespace-nowrap">
                    {p.projects.length} project{p.projects.length === 1 ? "" : "s"}, {activityCount} activit{activityCount === 1 ? "y" : "ies"}
                  </span>
                </label>
              );
            })}
            {selectedSourceOffice.programs.length === 0 && (
              <p className="px-3 py-2 text-xs text-slate-600">This office has no programs to copy.</p>
            )}
          </div>
        </div>
      )}

      {error && <p className="text-xs text-danger-600">{error}</p>}
      <div className="flex items-center gap-3">
        <button
          onClick={handleConfirm}
          disabled={saving || !selectedSourceOffice || checkedProgramIds.size === 0}
          className={`px-4 py-1.5 text-sm font-medium text-white transition-colors ${
            saving || !selectedSourceOffice || checkedProgramIds.size === 0
              ? "bg-green-300 cursor-not-allowed" : "bg-green-700 hover:bg-green-800"
          }`}
        >
          {saving ? "Copying…" : `Copy ${checkedProgramIds.size || ""} Program${checkedProgramIds.size === 1 ? "" : "s"}`}
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
  targetFiscalYear, officeConfigs, onSeeded,
}: {
  targetFiscalYear: number;
  officeConfigs: OfficeResponse[];
  onSeeded: (office: AipOfficeDetail) => void;
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
        setError(`This office has no LDIP for the ${sector} sector.`);
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

// ── Sector grouping ────────────────────────────────────────────────────────────

const SECTOR_ORDER = ["GENERAL", "SOCIAL", "ECONOMIC", "OTHERS"];

function groupBySector(offices: AipOfficeDetail[]): [string, AipOfficeDetail[]][] {
  const map = new Map<string, AipOfficeDetail[]>();
  for (const o of offices) {
    const s = (o.sector ?? "OTHERS").toUpperCase();
    if (!map.has(s)) map.set(s, []);
    map.get(s)!.push(o);
  }
  const result: [string, AipOfficeDetail[]][] = [];
  for (const s of SECTOR_ORDER)           if (map.has(s)) result.push([s, map.get(s)!]);
  for (const [s, list] of Array.from(map.entries()))  if (!SECTOR_ORDER.includes(s)) result.push([s, list]);
  return result;
}

function toggleSet<T>(prev: Set<T>, key: T): Set<T> {
  const next = new Set(prev);
  if (next.has(key)) next.delete(key); else next.add(key);
  return next;
}

function allCollapsed(offices: AipOfficeDetail[]) {
  return {
    offices:  new Set(offices.map((o) => o.id)),
    programs: new Set(offices.flatMap((o) => o.programs).map((p) => p.id)),
    projects: new Set(offices.flatMap((o) => o.programs).flatMap((p) => p.projects).map((p) => p.id)),
  };
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
  // RAL-180 — the copy-office response may be a brand-new office OR an existing one with
  // programs merged in; replace if it's already in the tree, otherwise append.
  const handleOfficeCarriedForward = useCallback((office: AipOfficeDetail) => {
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

      {/* Add Office (detail-page CRUD follow-up to RAL-179) + Carry Forward Office (RAL-180)
          + Seed from LDIP (RAL-181) — Draft-only, same gate as everything below. */}
      {record.status === "Draft" && (
        <div className="flex flex-wrap items-start gap-3 mb-4">
          <AddOfficePanel aipRecordId={record.id} officeConfigs={officeConfigs} onAdded={handleOfficeAdded} />
          <CarryForwardOfficePanel targetFiscalYear={record.fiscalYear} onCopied={handleOfficeCarriedForward} />
          <SeedFromLdipPanel targetFiscalYear={record.fiscalYear} officeConfigs={officeConfigs} onSeeded={handleOfficeCarriedForward} />
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
