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

import { useCallback, useEffect, useMemo, useState } from "react";
import Link from "next/link";
import { useSearchParams } from "next/navigation";
import { useMe } from "@/lib/me-cache";
import { getAipById, aipErrorMessage } from "@/lib/aip";
import type {
  AipRecordDetail,
  AipOfficeDetail,
  AipActivityDetail,
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

  const sectors = useMemo(() => record ? groupBySector(record.offices) : [], [record]);

  // On record load: activate first sector tab, collapse everything.
  useEffect(() => {
    if (!sectors.length) return;
    const [firstSector, firstOffices] = sectors[0];
    setActiveTab(firstSector);
    const c = allCollapsed(firstOffices);
    setCollapsedOffices(c.offices);
    setCollapsedPrograms(c.programs);
    setCollapsedProjects(c.projects);
  }, [sectors]);

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
              and only for PPDO uploaders. */}
          {record.status === "Draft" && record.entrySource === "Upload" && me?.canUploadAip === true && (
            <Link
              href={`/budget-planning/aip/new?replaceId=${record.id}`}
              className="px-3 py-1.5 text-sm font-medium text-white bg-green-700 hover:bg-green-800 transition-colors whitespace-nowrap"
            >
              Re-upload File
            </Link>
          )}
          <Link href="/budget-planning/aip" className="text-sm text-green-700 hover:underline whitespace-nowrap">
            ← Back to AIP list
          </Link>
        </div>
      </div>

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
                <>
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
                    <td /><td /><td />
                  </tr>

                  {/* ── Programs (only in DOM when office is expanded) ── */}
                  {officeOpen && office.programs.map((prog) => {
                    const progOpen = !collapsedPrograms.has(prog.id);

                    return (
                      <>
                        <tr key={`prog-${prog.id}`} className="bg-white border-t-2 border-slate-200">
                          <td className="px-2 py-1.5 pl-5 font-mono text-xs text-slate-600 border-l-4 border-green-400">
                            <button onClick={() => toggleProgram(prog.id)} className="flex items-center gap-1.5 text-left">
                              <Chevron open={progOpen} className="text-green-500" />
                              {prog.refCode}
                            </button>
                          </td>
                          <td colSpan={14} className="px-2 py-1.5 font-semibold text-xs italic text-slate-700 uppercase tracking-wide">
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
                            <>
                              <tr key={`proj-${proj.id}`} className="bg-slate-50 border-t border-slate-200">
                                <td className="px-2 py-1.5 pl-9 font-mono text-xs text-slate-600 border-l-4 border-slate-300">
                                  <button onClick={() => toggleProject(proj.id)} className="flex items-center gap-1.5 text-left">
                                    <Chevron open={projOpen} className="text-slate-400" />
                                    {proj.refCode}
                                  </button>
                                </td>
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
                                  {!projOpen && (
                                    <span className="ml-2 font-normal text-[10px] text-slate-600">
                                      {proj.activities.length} activities
                                    </span>
                                  )}
                                </td>
                              </tr>

                              {/* ── Activities (only in DOM when project is expanded) ── */}
                              {projOpen && proj.activities.map((act) => (
                                <tr key={`act-${act.id}`} className="bg-white border-t border-slate-100 hover:bg-green-50 transition-colors">
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
                                </tr>
                              ))}
                            </>
                          );
                        })}
                      </>
                    );
                  })}
                </>
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
