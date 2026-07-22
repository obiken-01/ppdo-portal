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
import { getAipById, updateAipActivity, aipErrorMessage } from "@/lib/aip";
import { listFundingSources } from "@/lib/config";
import { AIP_MONTHS, AIP_ESRE_OPTIONS } from "@/lib/aipConstants";
import MoneyInput from "@/components/ui/MoneyInput";
import type {
  AipRecordDetail,
  AipOfficeDetail,
  AipActivityDetail,
  FundingSourceResponse,
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
      white ? "text-white font-semibold" : bold ? "font-semibold text-slate-800" : "text-slate-700"
    }`}>
      {fmt(value)}
    </td>
  );
}

// ── Activity row (RAL-179 — inline edit) ─────────────────────────────────────
// A read-only row that swaps to an edit form in place when the user clicks Edit — no whole-page
// submit, Save/Cancel per row. RefCode/ProjectId/identity are never editable here.

const selectCls = "border border-slate-300 bg-white text-xs px-1.5 py-1 text-slate-700 w-full focus:outline-none focus:ring-1 focus:ring-green-600";
const inputCls  = "border border-slate-300 bg-white text-xs px-1.5 py-1 text-slate-700 w-full focus:outline-none focus:ring-1 focus:ring-green-600";

function ActivityRow({
  act, aipRecordId, canEdit, fundingSources, onSaved,
}: {
  act: AipActivityDetail;
  aipRecordId: number;
  canEdit: boolean;
  fundingSources: FundingSourceResponse[];
  onSaved: (updated: AipActivityDetail) => void;
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
        <td className="px-2 py-1.5 text-center text-xs font-medium text-slate-700">{act.fundingSourceSnapshot ?? "—"}</td>
        <AmtTD value={act.ps} />
        <AmtTD value={act.mooe} />
        <AmtTD value={act.co} />
        <AmtTD value={act.total} />
        <AmtTD value={act.ccAdaptation} />
        <AmtTD value={act.ccMitigation} />
        <td className="px-2 py-1.5 text-center text-xs text-slate-600">{act.ccTypologyCode ?? "—"}</td>
        <td className="px-2 py-1.5 text-center">
          {canEdit && (
            <button onClick={startEdit} className="text-xs text-green-700 hover:underline whitespace-nowrap">
              Edit
            </button>
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

  const handleActivitySaved = useCallback((updated: AipActivityDetail) => {
    setRecord((prev) => (prev ? replaceActivity(prev, updated) : prev));
  }, []);

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
  }, [me]);

  const sectors = useMemo(() => record ? groupBySector(record.offices) : [], [record]);

  // On record load: activate first sector tab, collapse everything. Keyed off record?.id (not
  // `sectors`, which is a new array reference on every render since RAL-179 started calling
  // setRecord for in-place activity edits) — otherwise saving one activity's edit would reset
  // the whole tab/expand state back to the first sector, fully collapsed, every time.
  useEffect(() => {
    if (!sectors.length) return;
    const [firstSector, firstOffices] = sectors[0];
    setActiveTab(firstSector);
    const c = allCollapsed(firstOffices);
    setCollapsedOffices(c.offices);
    setCollapsedPrograms(c.programs);
    setCollapsedProjects(c.projects);
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [record?.id]);

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

  const activeOffices = useMemo(
    () => sectors.find(([s]) => s === activeTab)?.[1] ?? [],
    [sectors, activeTab]
  );

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
            record.hasWfpUsage ? (
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
        record.hasWfpUsage && (
        <div className="mb-4 -mt-2 border border-amber-200 bg-amber-50 px-4 py-2.5 text-xs text-amber-800">
          Re-upload is disabled — a Work Financial Plan has already been built from this AIP. Archive
          this record and upload the corrected file as a new AIP instead.
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
                  : "border-transparent text-slate-600 hover:text-slate-700 hover:bg-slate-50"
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
            {activeOffices.map((office) => {
              const officeOpen  = !collapsedOffices.has(office.id);
              const officePs    = sumActivities(office, "ps");
              const officeMooe  = sumActivities(office, "mooe");
              const officeCo    = sumActivities(office, "co");
              const officeTotal = sumActivities(office, "total");
              const actCount    = office.programs.flatMap((p) => p.projects).flatMap((p) => p.activities).length;

              return (
                <Fragment key={`office-frag-${office.id}`}>
                  {/* ── Office row ──────────────────────────────── */}
                  <tr key={`office-${office.id}`} className="bg-green-700 border-t-2 border-green-800">
                    <td className="px-2 py-2 font-mono text-xs text-slate-600 align-top">
                      <button onClick={() => toggleOffice(office.id)} className="flex items-center gap-1.5 text-left">
                        <Chevron open={officeOpen} className="text-green-200" />
                        <span className="text-green-100">{office.refCode}</span>
                      </button>
                    </td>
                    <td className="px-2 py-2 align-top">
                      <span className="font-bold text-sm uppercase text-white">{office.name}</span>
                      {!officeOpen && (
                        <span className="ml-2 text-[10px] text-green-200">
                          {office.programs.length} programs · {actCount} activities
                        </span>
                      )}
                    </td>
                    <td /><td /><td /><td /><td /><td />
                    <AmtTD value={officePs}    white />
                    <AmtTD value={officeMooe}  white />
                    <AmtTD value={officeCo}    white />
                    <AmtTD value={officeTotal} white />
                    <td /><td /><td /><td />
                  </tr>

                  {/* ── Programs (only in DOM when office is expanded) ── */}
                  {officeOpen && office.programs.map((prog) => {
                    const progOpen = !collapsedPrograms.has(prog.id);

                    return (
                      <Fragment key={`prog-frag-${prog.id}`}>
                        <tr key={`prog-${prog.id}`} className="bg-white border-t-2 border-slate-200">
                          <td className="px-2 py-1.5 pl-5 font-mono text-xs text-slate-600 border-l-4 border-green-400">
                            <button onClick={() => toggleProgram(prog.id)} className="flex items-center gap-1.5 text-left">
                              <Chevron open={progOpen} className="text-green-500" />
                              {prog.refCode}
                            </button>
                          </td>
                          <td colSpan={15} className="px-2 py-1.5 font-semibold text-xs italic text-slate-700 uppercase tracking-wide">
                            {prog.name}
                            {!progOpen && (
                              <span className="ml-2 font-normal not-italic text-[10px] text-slate-600">
                                {prog.projects.length} projects ·{" "}
                                {prog.projects.flatMap((p) => p.activities).length} activities
                              </span>
                            )}
                          </td>
                        </tr>

                        {/* ── Projects (only in DOM when program is expanded) ── */}
                        {progOpen && prog.projects.map((proj) => {
                          const projOpen = !collapsedProjects.has(proj.id);

                          return (
                            <Fragment key={`proj-frag-${proj.id}`}>
                              <tr key={`proj-${proj.id}`} className="bg-slate-50 border-t border-slate-200">
                                <td className="px-2 py-1.5 pl-9 font-mono text-xs text-slate-600 border-l-4 border-slate-300">
                                  <button onClick={() => toggleProject(proj.id)} className="flex items-center gap-1.5 text-left">
                                    <Chevron open={projOpen} className="text-slate-400" />
                                    {proj.refCode}
                                  </button>
                                </td>
                                <td colSpan={15} className="px-2 py-1.5 text-xs font-medium text-slate-600">
                                  {proj.name}
                                  {proj.isSynthetic && (
                                    <span
                                      className="ml-2 px-1.5 py-0.5 text-[10px] font-normal bg-amber-100 text-amber-700"
                                      title="This project does not exist in the source file — it was created to hold a line item recorded directly on the parent program row."
                                    >
                                      program-level entry
                                    </span>
                                  )}
                                  {!projOpen && (
                                    <span className="ml-2 font-normal text-[10px] text-slate-600">
                                      {proj.activities.length} activities
                                    </span>
                                  )}
                                </td>
                              </tr>

                              {/* ── Activities (only in DOM when project is expanded) ── */}
                              {projOpen && proj.activities.map((act) => (
                                <ActivityRow
                                  key={`act-${act.id}`}
                                  act={act}
                                  aipRecordId={record.id}
                                  canEdit={record.status === "Draft"}
                                  fundingSources={fundingSources}
                                  onSaved={handleActivitySaved}
                                />
                              ))}
                            </Fragment>
                          );
                        })}
                      </Fragment>
                    );
                  })}
                </Fragment>
              );
            })}
          </tbody>
        </table>
      </div>

      <p className="text-xs text-slate-600 mt-2 text-right">
        {activityCount} activities across {record.offices.length} office records
      </p>
    </div>
  );
}
