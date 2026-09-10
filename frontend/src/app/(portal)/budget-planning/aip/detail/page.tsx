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
import { canOpenAipRecords, budgetPlanningFallback } from "@/lib/budget-planning-access";
import { getAipById, aipErrorMessage } from "@/lib/aip";
import { listOffices, listFundingSources } from "@/lib/config";
import { aipProgramsAreLdipOnly, aipUploadRefusal } from "@/lib/aip-fiscal-years";
import SeedFromLdipPanel from "@/components/aip/AipSeedFromLdipPanel";
import AddOfficePanel from "@/components/aip/AipAddOfficePanel";
import OfficeRow from "@/components/aip/AipOfficeRow";
import type { TreeActions } from "@/components/aip/AipTreeActions";
import { StatusBadge, TH } from "@/components/aip/AipTreeCells";
import {
  replaceActivity,
  removeActivityFromTree,
  addActivityToTree,
  replaceProject,
  removeProjectFromTree,
  addProjectToTree,
  replaceProgram,
  removeProgramFromTree,
  addProgramToTree,
  replaceOffice,
  removeOfficeFromTree,
  addOfficeToTree,
  groupBySector,
  toggleSet,
  allCollapsed,
} from "@/lib/aip-tree";
import ConfirmDialog, { type ConfirmDialogProps } from "@/components/ui/ConfirmDialog";
import type {
  AipRecordDetail,
  AipOfficeDetail,
  AipProgramDetail,
  AipProjectDetail,
  AipActivityDetail,
  FundingSourceResponse,
  OfficeResponse,
} from "@/types";

// ── Main page ─────────────────────────────────────────────────────────────────

export default function AipDetailPage() {
  const searchParams = useSearchParams();
  const id           = parseInt(searchParams.get("id") ?? "", 10);

  // PPDO-81 — Admin/SuperAdmin, matching the list this is reached from. ⚠️ This is also the read
  // view for FY≤2027 uploaded AIPs, so a guest office no longer has a route to last year's approved
  // document; that is an accepted consequence, not an oversight — see canOpenAipRecords.
  const me = useMe(canOpenAipRecords, budgetPlanningFallback);
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
