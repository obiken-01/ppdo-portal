"use client";

/**
 * LdipForm — shared create/edit form for LDIP documents (RAL-61).
 *
 * Layout follows the RAP-01 Penpot redesign (see
 * docs/v1.3/RAL-61_LDIP_Entry_Form_Design.md — the ticket's original flat field
 * table was superseded):
 *   1. LDIP information — year range + the office the whole document belongs to.
 *   2. Program information — a repeatable "add a program" mini-form: pick a
 *      Sector, see the office-level AIP ref-code preview, set the office/
 *      sub-office group name (a sector may hold MULTIPLE groups — e.g.
 *      "PGO - WARDEN" / "PGO - AKAP-HUB" / "PGO - HOUSING" all under Social,
 *      sharing one ref code; pick an existing name from the suggestions to keep
 *      adding under that group, or type a new name to start another), name the
 *      program, enter its whole-period budget (₱000), and Add.
 *   3. Created programs — the grouped table (green header per group, AIP-detail
 *      style). Program numbering runs continuously across groups that share a
 *      ref code; removals renumber with no gaps.
 *
 * Ref codes shown here are PREVIEWS — the server recomputes all AIP ref codes
 * on every save, so they are authoritative. Budgets are entered and stored in
 * thousands (₱000), like AIP totals.
 */

import { useEffect, useMemo, useState } from "react";
import Link from "next/link";
import { useRouter } from "next/navigation";
import { createLdip, finalizeLdip, ldipErrorMessage, unlockLdip, updateLdip } from "@/lib/ldip";
import { listOffices } from "@/lib/config";
import { useMe } from "@/lib/me-cache";
import { formatMoney } from "@/lib/money";
import MoneyInput from "@/components/ui/MoneyInput";
import OfficeSelect from "@/components/ui/OfficeSelect";
import ConfirmDialog, { type ConfirmDialogProps } from "@/components/ui/ConfirmDialog";
import { useToast } from "@/components/ui/Toast";
import type {
  LdipProgram,
  LdipRecordDetail,
  LdipSector,
  LdipStatus,
  OfficeResponse,
  SaveLdipGroup,
} from "@/types";

// ---------------------------------------------------------------------------
// Constants / helpers
// ---------------------------------------------------------------------------

const SECTORS: LdipSector[] = ["General", "Social", "Economic", "Others"];
const SECTOR_PREFIX: Record<LdipSector, string> = {
  General: "1000",
  Social: "3000",
  Economic: "8000",
  Others: "9000",
};

const CURRENT_YEAR = new Date().getFullYear();

interface DraftProgram {
  key: number;
  name: string;
  budget: number;
  /**
   * Server-persisted ref code, present only when loaded from an existing record.
   * Newly-added (unsaved) programs compute a live preview instead (see render).
   */
  refCode?: string;
  /**
   * Upload-derived detail fields (RAL-113) — present only for programs created
   * via file upload; undefined for programs added through "+ Add Program".
   */
  detail?: LdipProgram;
}

/** True when any upload-only field on the program is populated. */
function hasUploadDetail(p: LdipProgram): boolean {
  return (
    p.implementingOffice != null || p.startDate != null || p.endDate != null ||
    p.expectedOutputs != null || p.fundingSourceSnapshot != null ||
    p.ps != null || p.mooe != null || p.co != null ||
    p.ccAdaptation != null || p.ccMitigation != null || p.ccTypologyCode != null ||
    p.pdpRdp != null || p.sdgs != null || p.sendaiFramework != null ||
    p.ndrrmPlan != null || p.nsp != null || p.pdpdfp != null
  );
}

/**
 * Ordered group list — order matters: the server numbers programs continuously
 * per ref code in submitted order. Identity is the (sector, name) pair.
 */
interface DraftGroup {
  sector: LdipSector;
  name: string;
  programs: DraftProgram[];
  /** Server-persisted ref code, present only when loaded from an existing record. */
  refCode?: string;
}

function sameName(a: string, b: string): boolean {
  return a.trim().toLowerCase() === b.trim().toLowerCase();
}

function StatusBadge({ status }: { status: LdipStatus }) {
  const cls =
    status === "Final"
      ? "bg-green-100 text-green-700"
      : status === "Draft"
      ? "bg-amber-100 text-amber-700"
      : "bg-slate-100 text-slate-600";
  return <span className={`px-2 py-0.5 text-xs font-medium ${cls}`}>{status}</span>;
}

/** Read-only detail row for an upload-derived program (RAL-113) — never editable here. */
function ProgramDetailPanel({ program: p }: { program: LdipProgram }) {
  const field = (label: string, value: string | null): [string, string | null] => [label, value];
  const fields: [string, string | null][] = [
    field("Implementing Office", p.implementingOffice),
    field("Start Date", p.startDate),
    field("Completion Date", p.endDate),
    field("Funding Source", p.fundingSourceSnapshot),
    field("PS (₱000)", p.ps != null ? formatMoney(p.ps) : null),
    field("MOOE (₱000)", p.mooe != null ? formatMoney(p.mooe) : null),
    field("CO (₱000)", p.co != null ? formatMoney(p.co) : null),
    field("CC Adaptation (₱000)", p.ccAdaptation != null ? formatMoney(p.ccAdaptation) : null),
    field("CC Mitigation (₱000)", p.ccMitigation != null ? formatMoney(p.ccMitigation) : null),
    field("CC Typology Code", p.ccTypologyCode),
    field("PDP/RDP", p.pdpRdp),
    field("SDGs", p.sdgs),
    field("Sendai Framework", p.sendaiFramework),
    field("NDRRM Plan", p.ndrrmPlan),
    field("NSP", p.nsp),
    field("PDPDFP", p.pdpdfp),
  ].filter(([, v]) => v != null && v !== "");

  return (
    <div className="px-4 py-3 pl-11 bg-slate-50 border-t border-slate-100">
      {p.expectedOutputs && (
        <div className="mb-3">
          <p className="text-[10px] font-bold text-slate-400 uppercase tracking-wide">Expected Outputs</p>
          <p className="text-xs text-slate-600 whitespace-pre-wrap mt-0.5">{p.expectedOutputs}</p>
        </div>
      )}
      <div className="grid grid-cols-2 sm:grid-cols-4 gap-x-4 gap-y-2">
        {fields.map(([label, value]) => (
          <div key={label}>
            <p className="text-[10px] font-bold text-slate-400 uppercase tracking-wide">{label}</p>
            <p className="text-xs text-slate-600 mt-0.5">{value}</p>
          </div>
        ))}
      </div>
    </div>
  );
}

function SectionHead({ num, title, hint }: { num: number; title: string; hint?: string }) {
  return (
    <div className="flex items-center gap-2 px-4 py-3 border-b border-slate-100 flex-wrap">
      <span className="w-5 h-5 rounded-full bg-green-700 text-white text-[11px] font-bold flex items-center justify-center shrink-0">
        {num}
      </span>
      <span className="text-sm font-semibold text-slate-700">{title}</span>
      {hint && <span className="text-xs text-slate-400">{hint}</span>}
    </div>
  );
}

// ---------------------------------------------------------------------------
// Form
// ---------------------------------------------------------------------------

export default function LdipForm({ record }: { record?: LdipRecordDetail }) {
  const router = useRouter();
  const { toast } = useToast();
  const me = useMe(
    (m) => m.canAccessBudgetPlanning,
    (m) => (m.officeId != null ? "/account" : "/dashboard"),
  );

  const isEdit = record != null;
  // Uploaded multi-office records (RAL-113) have no single office — editing them
  // through this single-office-centric form doesn't make sense, so they're always
  // read-only regardless of Draft/Final status.
  const isMultiOffice = isEdit && record.officeId == null && record.entryMode === "Upload";
  const isReadOnly = isEdit && (record.status !== "Draft" || isMultiOffice);
  const isAdmin = me?.role === "Admin" || me?.role === "SuperAdmin";
  const isOfficeUser = me != null && me.officeId != null;

  // ── Section 1 state ────────────────────────────────────────────────────────

  const [offices, setOffices] = useState<OfficeResponse[]>([]);
  const [officeId, setOfficeId] = useState<number | null>(record?.officeId ?? null);
  const [yearStart, setYearStart] = useState(record?.fiscalYearStart ?? CURRENT_YEAR + 1);
  const [yearEnd, setYearEnd] = useState(record?.fiscalYearEnd ?? CURRENT_YEAR + 3);

  // ── Section 2/3 state ──────────────────────────────────────────────────────

  const [groups, setGroups] = useState<DraftGroup[]>(() =>
    (record?.groups ?? []).map((g, gi) => ({
      sector: g.sector,
      name: g.name,
      refCode: g.refCode,
      programs: g.programs.map((p, pi) => ({
        key: gi * 1000 + pi,
        name: p.name,
        budget: p.budget,
        refCode: p.refCode,
        detail: hasUploadDetail(p) ? p : undefined,
      })),
    }))
  );
  const [nextKey, setNextKey] = useState(1_000_000);
  const [expandedKeys, setExpandedKeys] = useState<Set<number>>(new Set());

  function toggleExpanded(key: number) {
    setExpandedKeys((prev) => {
      const next = new Set(prev);
      if (next.has(key)) next.delete(key); else next.add(key);
      return next;
    });
  }

  const [sector, setSector] = useState<LdipSector>("General");
  const [subOfficeName, setSubOfficeName] = useState("");
  const [programName, setProgramName] = useState("");
  const [programBudget, setProgramBudget] = useState<number | null>(null);
  const [addError, setAddError] = useState<string | null>(null);

  // ── UI state ───────────────────────────────────────────────────────────────

  const [saving, setSaving] = useState(false);
  const [submitError, setSubmitError] = useState<string | null>(null);
  const [confirm, setConfirm] = useState<ConfirmDialogProps | null>(null);

  // ── Load offices; lock office users to their own ──────────────────────────

  useEffect(() => {
    listOffices({ active: "true" }).then(setOffices).catch(() => setOffices([]));
  }, []);

  useEffect(() => {
    if (!isEdit && me?.officeId != null) setOfficeId(me.officeId);
  }, [me, isEdit]);

  // ── Derived ────────────────────────────────────────────────────────────────

  const office = useMemo(
    () => offices.find((o) => o.id === officeId) ?? null,
    [offices, officeId]
  );
  const officeRefSuffix = office?.officeRefCode ?? null;

  const groupRefCode =
    officeRefSuffix != null ? `${SECTOR_PREFIX[sector]}-000-1-${officeRefSuffix}` : null;

  const sectorGroups = useMemo(() => groups.filter((g) => g.sector === sector), [groups, sector]);

  // Program numbering is continuous per ref code (= per sector, since the office
  // is fixed) across all its groups — mirrors the server's computation.
  const sectorProgramCount = sectorGroups.reduce((n, g) => n + g.programs.length, 0);
  const programRefPreview =
    groupRefCode != null
      ? `${groupRefCode}-${String(sectorProgramCount + 1).padStart(3, "0")}`
      : null;

  const totalPrograms = groups.reduce((n, g) => n + g.programs.length, 0);
  const targetsExistingGroup = sectorGroups.some((g) => sameName(g.name, subOfficeName));

  // Reset the group-name suggestion when the sector or office changes; picking
  // an existing name from the datalist re-targets that group. Names are always
  // UPPERCASE, matching the source AIP files (server normalises too).
  useEffect(() => {
    setSubOfficeName((office?.officeName ?? "").toUpperCase());
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [sector, officeId, offices.length]);

  // ── Program add/remove ────────────────────────────────────────────────────

  function handleAddProgram() {
    if (!programName.trim() || !subOfficeName.trim() || !programBudget || programBudget <= 0) {
      setAddError("Office/sub-office name, program name, and a budget above zero are required.");
      return;
    }
    if (groupRefCode == null) {
      setAddError("Pick an office with a configured AIP ref code first.");
      return;
    }
    setAddError(null);
    const entry: DraftProgram = { key: nextKey, name: programName.trim(), budget: programBudget };
    setGroups((prev) => {
      const idx = prev.findIndex((g) => g.sector === sector && sameName(g.name, subOfficeName));
      if (idx >= 0) {
        const next = [...prev];
        next[idx] = { ...next[idx], programs: [...next[idx].programs, entry] };
        return next;
      }
      return [...prev, { sector, name: subOfficeName.trim(), programs: [entry] }];
    });
    setNextKey((k) => k + 1);
    setProgramName("");
    setProgramBudget(null);
  }

  function handleRemoveProgram(groupIndex: number, key: number) {
    setGroups((prev) => {
      const group = prev[groupIndex];
      const programs = group.programs.filter((p) => p.key !== key);
      const next = [...prev];
      if (programs.length === 0) next.splice(groupIndex, 1);
      else next[groupIndex] = { ...group, programs };
      return next;
    });
  }

  // ── Save / finalize ───────────────────────────────────────────────────────

  function buildPayloadGroups(): SaveLdipGroup[] {
    return groups
      .filter((g) => g.programs.length > 0)
      .map((g) => ({
        sector: g.sector,
        name: g.name,
        programs: g.programs.map((p) => ({ name: p.name, budget: p.budget })),
      }));
  }

  async function saveDraft(): Promise<LdipRecordDetail | null> {
    if (officeId == null) {
      setSubmitError("Office is required.");
      return null;
    }
    setSubmitError(null);
    setSaving(true);
    try {
      const body = {
        title: "",
        entryMode: record?.entryMode ?? ("New" as const),
        fiscalYearStart: yearStart,
        fiscalYearEnd: yearEnd,
        officeId,
        groups: buildPayloadGroups(),
      };
      const saved = isEdit ? await updateLdip(record.id, body) : await createLdip(body);
      return saved;
    } catch (err) {
      setSubmitError(ldipErrorMessage(err, "Failed to save LDIP."));
      return null;
    } finally {
      setSaving(false);
    }
  }

  async function handleSaveDraft() {
    const saved = await saveDraft();
    if (!saved) return;
    toast.success("Saved", `${saved.refCode} saved as Draft.`);
    if (!isEdit) router.push(`/budget-planning/ldip/edit?id=${saved.id}`);
  }

  function handleFinalize() {
    if (totalPrograms === 0) {
      setSubmitError("Add at least one program before finalizing.");
      return;
    }
    setConfirm({
      title: "Finalize LDIP",
      message:
        "Finalize this LDIP? The latest changes are saved first; once Final it is locked and can only be unlocked by an admin.",
      confirmLabel: "Finalize",
      cancelLabel: "Cancel",
      variant: "primary",
      onConfirm: async () => {
        setConfirm(null);
        const saved = await saveDraft();
        if (!saved) return;
        try {
          await finalizeLdip(saved.id);
          toast.success("Finalized", `${saved.refCode} is now Final.`);
          router.push("/budget-planning/ldip");
        } catch (err) {
          toast.error("Failed", ldipErrorMessage(err, "Could not finalize LDIP."));
        }
      },
      onClose: () => setConfirm(null),
    });
  }

  function handleUnlock() {
    if (!record) return;
    setConfirm({
      title: "Unlock LDIP",
      message: `Unlock ${record.refCode} to allow further editing?`,
      confirmLabel: "Unlock",
      cancelLabel: "Cancel",
      variant: "warning",
      onConfirm: async () => {
        setConfirm(null);
        try {
          await unlockLdip(record.id);
          toast.success("Unlocked", `${record.refCode} is now editable.`);
          router.refresh();
          window.location.reload();
        } catch (err) {
          toast.error("Failed", ldipErrorMessage(err, "Could not unlock LDIP."));
        }
      },
      onClose: () => setConfirm(null),
    });
  }

  // ── Render ────────────────────────────────────────────────────────────────

  if (!me) return null;

  const inputCls = (user: boolean) =>
    `border border-slate-200 text-sm px-3 py-2 text-slate-700 focus:outline-none focus:ring-1 focus:ring-green-600 disabled:cursor-not-allowed disabled:text-slate-500 ${
      user ? "bg-yellow-50" : "bg-slate-100"
    }`;

  // Continuous display numbering per ref code across groups (mirrors the server).
  const seqByRefCode: Record<string, number> = {};

  return (
    <div className="p-6 max-w-4xl mx-auto">
      {/* Header */}
      <div className="mb-4">
        <div className="flex items-center gap-3">
          <h1 className="text-xl font-bold text-slate-800">
            {isEdit ? record.refCode : "LDIP Entry Form"}
          </h1>
          {isEdit && <StatusBadge status={record.status} />}
        </div>
        <p className="text-sm text-slate-500 mt-0.5">
          {isReadOnly
            ? "This record is read-only."
            : "One office, multiple programs across sectors — grouped like the AIP hierarchy."}
        </p>
      </div>

      {/* Legend + ribbon */}
      <div className="flex items-center gap-4 flex-wrap bg-white border border-slate-200 px-4 py-2.5 mb-4">
        <span className="flex items-center gap-1.5 text-xs text-slate-500">
          <span className="w-2.5 h-2.5 bg-yellow-50 border border-slate-200 inline-block" />
          User input
        </span>
        <span className="flex items-center gap-1.5 text-xs text-slate-500">
          <span className="w-2.5 h-2.5 bg-slate-100 border border-slate-200 inline-block" />
          Auto-filled
        </span>
        <div className="flex-1" />
        {!isReadOnly && (
          <>
            <button
              onClick={handleSaveDraft}
              disabled={saving}
              className="px-4 py-1.5 text-sm font-medium border border-slate-300 text-slate-700 bg-white hover:bg-slate-50 transition-colors disabled:opacity-60 flex items-center gap-2"
            >
              {saving && (
                <span className="w-3.5 h-3.5 border-2 border-slate-400 border-t-transparent rounded-full animate-spin" />
              )}
              Save Draft
            </button>
            <button
              onClick={handleFinalize}
              disabled={saving}
              className="px-4 py-1.5 text-sm font-medium text-white bg-green-700 hover:bg-green-800 transition-colors disabled:opacity-60"
            >
              Finalize
            </button>
          </>
        )}
        {isReadOnly && record.status === "Final" && isAdmin && (
          <button
            onClick={handleUnlock}
            className="px-4 py-1.5 text-sm font-medium border border-amber-300 text-amber-700 bg-amber-50 hover:bg-amber-100 transition-colors"
          >
            Unlock
          </button>
        )}
        <Link
          href="/budget-planning/ldip"
          className="px-4 py-1.5 text-sm font-medium border border-slate-200 text-slate-500 bg-white hover:bg-slate-50 transition-colors"
        >
          {isReadOnly ? "Back" : "Cancel"}
        </Link>
      </div>

      {/* ── Section 1: LDIP information ─────────────────────────────────────── */}
      <div className="bg-white border border-slate-200 mb-4">
        <SectionHead num={1} title="LDIP Information" />
        <div className="p-4 grid grid-cols-1 sm:grid-cols-2 gap-4">
          <div>
            <label className="block text-xs font-semibold text-slate-600 uppercase tracking-wide mb-1">
              Year Start <span className="text-red-500">*</span>
            </label>
            <input
              type="number"
              min={2020}
              max={2055}
              value={yearStart}
              disabled={isReadOnly}
              onChange={(e) => setYearStart(Number(e.target.value) || CURRENT_YEAR + 1)}
              className={`w-28 ${inputCls(true)}`}
            />
          </div>
          <div>
            <label className="block text-xs font-semibold text-slate-600 uppercase tracking-wide mb-1">
              Year End <span className="text-red-500">*</span>
            </label>
            <input
              type="number"
              min={2020}
              max={2060}
              value={yearEnd}
              disabled={isReadOnly}
              onChange={(e) => setYearEnd(Number(e.target.value) || CURRENT_YEAR + 3)}
              className={`w-28 ${inputCls(true)}`}
            />
            {yearStart > yearEnd && (
              <p className="text-xs text-red-600 mt-1">Year start must be on or before year end.</p>
            )}
          </div>
          <div className="sm:col-span-2">
            <label className="block text-xs font-semibold text-slate-600 uppercase tracking-wide mb-1">
              Office <span className="text-red-500">*</span>
            </label>
            {isOfficeUser || isReadOnly ? (
              <span className="inline-block text-sm text-slate-700 bg-slate-100 border border-slate-200 px-3 py-2">
                {isMultiOffice
                  ? "All Offices"
                  : office
                  ? `${office.officeCode} — ${office.officeName}`
                  : record?.officeName ?? "—"}
              </span>
            ) : (
              <OfficeSelect
                className="w-96 max-w-full"
                offices={offices}
                value={officeId}
                onChange={(id) => setOfficeId(id)}
                placeholder="— select office —"
              />
            )}
            {office && !office.officeRefCode && (
              <p className="text-xs text-red-600 mt-1">
                This office has no AIP ref code configured — set office_ref_code in Config → Offices
                before adding programs.
              </p>
            )}
          </div>
        </div>
      </div>

      {/* ── Section 2: Program information (hidden when read-only) ─────────── */}
      {!isReadOnly && (
        <div className="bg-white border border-slate-200 mb-4">
          <SectionHead
            num={2}
            title="Program Information"
            hint="PPA name should match the LGU-approved Development Investment Program nomenclature"
          />
          <div className="p-4 grid grid-cols-1 sm:grid-cols-2 gap-4">
            <div>
              <label className="block text-xs font-semibold text-slate-600 uppercase tracking-wide mb-1">
                Sector <span className="text-red-500">*</span>
              </label>
              <select
                value={sector}
                onChange={(e) => setSector(e.target.value as LdipSector)}
                className={inputCls(true)}
              >
                {SECTORS.map((s) => (
                  <option key={s} value={s}>
                    {s}
                  </option>
                ))}
              </select>
            </div>
            <div>
              <label className="block text-xs font-semibold text-slate-600 uppercase tracking-wide mb-1">
                (Preview) Office AIP Ref Code
              </label>
              <input
                readOnly
                value={groupRefCode ?? "—"}
                className={`w-full font-mono ${inputCls(false)}`}
              />
            </div>
            <div className="sm:col-span-2">
              <label className="block text-xs font-semibold text-slate-600 uppercase tracking-wide mb-1">
                Office / Sub-office Name <span className="text-red-500">*</span>
                <span className="ml-1 font-normal normal-case text-slate-400">
                  {targetsExistingGroup
                    ? "— adds to this existing group"
                    : "— starts a new group (a sector can hold several sub-offices)"}
                </span>
              </label>
              <input
                value={subOfficeName}
                onChange={(e) => setSubOfficeName(e.target.value.toUpperCase())}
                maxLength={500}
                list="ldip-group-names"
                placeholder='e.g. "OFFICE OF THE GOVERNOR - WARDEN"'
                className={`w-full uppercase ${inputCls(true)}`}
              />
              <datalist id="ldip-group-names">
                {sectorGroups.map((g) => (
                  <option key={g.name} value={g.name} />
                ))}
              </datalist>
            </div>
            <div className="sm:col-span-2">
              <label className="block text-xs font-semibold text-slate-600 uppercase tracking-wide mb-1">
                Program Name <span className="text-red-500">*</span>
              </label>
              <input
                value={programName}
                onChange={(e) => setProgramName(e.target.value.toUpperCase())}
                maxLength={500}
                placeholder="e.g. OFFICE FUNCTIONALITY AND OPERATIONS PROGRAM"
                className={`w-full uppercase ${inputCls(true)}`}
              />
            </div>
            <div>
              <label className="block text-xs font-semibold text-slate-600 uppercase tracking-wide mb-1">
                (Preview) Program AIP Ref Code
              </label>
              <input
                readOnly
                value={programRefPreview ?? "—"}
                className={`w-full font-mono ${inputCls(false)}`}
              />
            </div>
            <div>
              <label className="block text-xs font-semibold text-slate-600 uppercase tracking-wide mb-1">
                Budget — {yearStart}–{yearEnd} total{" "}
                <span className="font-normal normal-case text-slate-400">(₱000)</span>{" "}
                <span className="text-red-500">*</span>
              </label>
              <MoneyInput value={programBudget} onChange={setProgramBudget} className="w-48" />
            </div>
            <div className="sm:col-span-2 flex items-center justify-between gap-3 flex-wrap">
              {addError ? (
                <span className="text-xs text-red-600">{addError}</span>
              ) : (
                <span />
              )}
              <button
                onClick={handleAddProgram}
                disabled={officeId == null || !office?.officeRefCode}
                className="px-4 py-2 text-sm font-medium text-white bg-green-700 hover:bg-green-800 transition-colors disabled:opacity-50 disabled:cursor-not-allowed"
              >
                + Add Program
              </button>
            </div>
          </div>
        </div>
      )}

      {/* ── Section 3: Created programs ─────────────────────────────────────── */}
      <div className="bg-white border border-slate-200">
        <SectionHead num={isReadOnly ? 2 : 3} title="Created Programs" />
        <div className="grid grid-cols-[24px_130px_1fr_120px_90px] gap-2 px-4 py-2 bg-slate-100 text-[10px] font-bold text-slate-500 uppercase tracking-wide">
          <span />
          <span>AIP Ref Code</span>
          <span>Program</span>
          <span className="text-right">Budget (₱000)</span>
          <span>{!isReadOnly ? "Actions" : ""}</span>
        </div>
        {totalPrograms === 0 ? (
          <p className="px-4 py-6 text-center text-sm text-slate-400">No programs added yet.</p>
        ) : (
          groups.map((group, groupIndex) => {
            if (group.programs.length === 0) return null;
            // Prefer the server-persisted ref code (always correct, and the only
            // option for multi-office records where each group belongs to a
            // DIFFERENT office than whatever single office this form has loaded).
            // Fall back to a live client-side preview only for a brand-new,
            // not-yet-saved group.
            const ref =
              group.refCode ??
              (officeRefSuffix != null
                ? `${SECTOR_PREFIX[group.sector]}-000-1-${officeRefSuffix}`
                : "—");
            return (
              <div key={`${group.sector}|${group.name}`}>
                <div className="flex items-center gap-3 bg-green-800 text-white px-4 py-2 text-xs font-bold">
                  <span className="font-mono opacity-85">{ref}</span>
                  <span>{group.name.toUpperCase()}</span>
                  <span className="font-normal opacity-70">({group.sector})</span>
                </div>
                {group.programs.map((p) => {
                  const seq = (seqByRefCode[ref] = (seqByRefCode[ref] ?? 0) + 1);
                  const expanded = expandedKeys.has(p.key);
                  const programRef = p.refCode ?? `${ref}-${String(seq).padStart(3, "0")}`;
                  return (
                    <div key={p.key} className="border-b border-slate-50">
                      <div className="grid grid-cols-[24px_130px_1fr_120px_90px] gap-2 px-4 py-2 items-center text-sm">
                        <span>
                          {p.detail && (
                            <button
                              onClick={() => toggleExpanded(p.key)}
                              title={expanded ? "Hide uploaded detail" : "Show uploaded detail"}
                              className="w-4 h-4 flex items-center justify-center text-slate-400 hover:text-slate-700"
                            >
                              <span
                                className={`inline-block text-xs transition-transform duration-150 ${expanded ? "rotate-90" : ""}`}
                              >
                                ›
                              </span>
                            </button>
                          )}
                        </span>
                        <span className="font-mono text-xs text-slate-500">
                          {programRef}
                        </span>
                        <span className="italic font-semibold text-slate-800">{p.name}</span>
                        <span className="text-right tabular-nums">₱{formatMoney(p.budget)}</span>
                        <span>
                          {!isReadOnly && (
                            <button
                              onClick={() => handleRemoveProgram(groupIndex, p.key)}
                              className="text-xs font-bold text-red-500 hover:text-red-600"
                            >
                              Remove
                            </button>
                          )}
                        </span>
                      </div>
                      {expanded && p.detail && <ProgramDetailPanel program={p.detail} />}
                    </div>
                  );
                })}
              </div>
            );
          })
        )}
      </div>

      {/* Submit error */}
      {submitError && (
        <div className="mt-4 border border-red-200 bg-red-50 px-4 py-3 text-sm text-red-700">
          {submitError}
        </div>
      )}

      {confirm && <ConfirmDialog {...confirm} />}
    </div>
  );
}
