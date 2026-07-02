"use client";

/**
 * LdipForm — shared create/edit form for LDIP documents (RAL-61).
 *
 * Layout follows the RAP-01 Penpot redesign (see
 * docs/v1.3/RAL-61_LDIP_Entry_Form_Penpot_Findings.md — the ticket's original
 * flat field table was superseded):
 *   1. LDIP information — year range + the office the whole document belongs to.
 *   2. Program information — a repeatable "add a program" mini-form: pick a
 *      Sector, see the office-level AIP ref-code preview, optionally rename the
 *      office/sub-office for that sector (locked once the group exists — one ref
 *      code maps to exactly one display name), name the program, enter its
 *      whole-period budget (₱000), and Add.
 *   3. Created programs — the grouped table (green header per sector group,
 *      AIP-detail style). Removing a program renumbers the rest of its group.
 *
 * Ref codes shown here are PREVIEWS — the server recomputes all AIP ref codes
 * on every save (contiguous, 001-based per group), so they are authoritative.
 * Budgets are entered and stored in thousands (₱000), like AIP totals.
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
}

/** Groups keyed by sector — the record's office is fixed, so sector ≡ group. */
type DraftGroups = Partial<Record<LdipSector, { name: string; programs: DraftProgram[] }>>;

function StatusBadge({ status }: { status: LdipStatus }) {
  const cls =
    status === "Final"
      ? "bg-green-100 text-green-700"
      : status === "Draft"
      ? "bg-amber-100 text-amber-700"
      : "bg-slate-100 text-slate-600";
  return <span className={`px-2 py-0.5 text-xs font-medium ${cls}`}>{status}</span>;
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
  const isReadOnly = isEdit && record.status !== "Draft";
  const isAdmin = me?.role === "Admin" || me?.role === "SuperAdmin";
  const isOfficeUser = me != null && me.officeId != null;

  // ── Section 1 state ────────────────────────────────────────────────────────

  const [offices, setOffices] = useState<OfficeResponse[]>([]);
  const [officeId, setOfficeId] = useState<number | null>(record?.officeId ?? null);
  const [yearStart, setYearStart] = useState(record?.fiscalYearStart ?? CURRENT_YEAR + 1);
  const [yearEnd, setYearEnd] = useState(record?.fiscalYearEnd ?? CURRENT_YEAR + 3);

  // ── Section 2/3 state ──────────────────────────────────────────────────────

  const [groups, setGroups] = useState<DraftGroups>(() => {
    const initial: DraftGroups = {};
    for (const g of record?.groups ?? []) {
      initial[g.sector] = {
        name: g.name,
        programs: g.programs.map((p, i) => ({ key: i + 1, name: p.name, budget: p.budget })),
      };
    }
    return initial;
  });
  const [nextKey, setNextKey] = useState(1000);

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

  const existingGroup = groups[sector];
  const nameLocked = existingGroup != null && existingGroup.programs.length > 0;

  const programRefPreview =
    groupRefCode != null
      ? `${groupRefCode}-${String((existingGroup?.programs.length ?? 0) + 1).padStart(3, "0")}`
      : null;

  const totalPrograms = SECTORS.reduce((n, s) => n + (groups[s]?.programs.length ?? 0), 0);

  // Sub-office name follows the group when it exists; otherwise defaults to the
  // office name for a fresh group (still editable until a first program is added).
  useEffect(() => {
    if (nameLocked) setSubOfficeName(existingGroup!.name);
    else if (existingGroup) setSubOfficeName(existingGroup.name);
    else setSubOfficeName(office?.officeName ?? "");
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [sector, officeId, offices, nameLocked]);

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
    setGroups((prev) => {
      const group = prev[sector] ?? { name: subOfficeName.trim(), programs: [] };
      return {
        ...prev,
        [sector]: {
          name: group.programs.length > 0 ? group.name : subOfficeName.trim(),
          programs: [...group.programs, { key: nextKey, name: programName.trim(), budget: programBudget }],
        },
      };
    });
    setNextKey((k) => k + 1);
    setProgramName("");
    setProgramBudget(null);
  }

  function handleRemoveProgram(s: LdipSector, key: number) {
    setGroups((prev) => {
      const group = prev[s];
      if (!group) return prev;
      const programs = group.programs.filter((p) => p.key !== key);
      const next = { ...prev };
      if (programs.length === 0) delete next[s];
      else next[s] = { ...group, programs };
      return next;
    });
  }

  // ── Save / finalize ───────────────────────────────────────────────────────

  function buildPayloadGroups(): SaveLdipGroup[] {
    return SECTORS.filter((s) => (groups[s]?.programs.length ?? 0) > 0).map((s) => ({
      sector: s,
      name: groups[s]!.name,
      programs: groups[s]!.programs.map((p) => ({ name: p.name, budget: p.budget })),
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
    if (!isEdit) router.push(`/budget-planning/ldip/${saved.id}/edit`);
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
          Auto-filled / locked
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
                {office ? `${office.officeCode} — ${office.officeName}` : record?.officeName ?? "—"}
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
                  {nameLocked ? "— existing group, name locked" : "— new group, name it"}
                </span>
              </label>
              <input
                value={subOfficeName}
                readOnly={nameLocked}
                onChange={(e) => setSubOfficeName(e.target.value)}
                maxLength={500}
                className={`w-full ${inputCls(!nameLocked)}`}
              />
            </div>
            <div className="sm:col-span-2">
              <label className="block text-xs font-semibold text-slate-600 uppercase tracking-wide mb-1">
                Program Name <span className="text-red-500">*</span>
              </label>
              <input
                value={programName}
                onChange={(e) => setProgramName(e.target.value)}
                maxLength={500}
                placeholder="e.g. Office Functionality and Operations Program"
                className={`w-full ${inputCls(true)}`}
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
        <div className="grid grid-cols-[130px_1fr_120px_90px] gap-2 px-4 py-2 bg-slate-100 text-[10px] font-bold text-slate-500 uppercase tracking-wide">
          <span>AIP Ref Code</span>
          <span>Program</span>
          <span className="text-right">Budget (₱000)</span>
          <span>{!isReadOnly ? "Actions" : ""}</span>
        </div>
        {totalPrograms === 0 ? (
          <p className="px-4 py-6 text-center text-sm text-slate-400">No programs added yet.</p>
        ) : (
          SECTORS.filter((s) => (groups[s]?.programs.length ?? 0) > 0).map((s) => {
            const group = groups[s]!;
            const ref =
              officeRefSuffix != null ? `${SECTOR_PREFIX[s]}-000-1-${officeRefSuffix}` : "—";
            return (
              <div key={s}>
                <div className="flex items-center gap-3 bg-green-800 text-white px-4 py-2 text-xs font-bold">
                  <span className="font-mono opacity-85">{ref}</span>
                  <span>{group.name.toUpperCase()}</span>
                  <span className="font-normal opacity-70">({s})</span>
                </div>
                {group.programs.map((p, idx) => (
                  <div
                    key={p.key}
                    className="grid grid-cols-[130px_1fr_120px_90px] gap-2 px-4 py-2 border-b border-slate-50 items-center text-sm"
                  >
                    <span className="font-mono text-xs text-slate-500">
                      {ref}-{String(idx + 1).padStart(3, "0")}
                    </span>
                    <span className="italic font-semibold text-slate-800">{p.name}</span>
                    <span className="text-right tabular-nums">₱{formatMoney(p.budget)}</span>
                    <span>
                      {!isReadOnly && (
                        <button
                          onClick={() => handleRemoveProgram(s, p.key)}
                          className="text-xs font-bold text-red-500 hover:text-red-600"
                        >
                          Remove
                        </button>
                      )}
                    </span>
                  </div>
                ))}
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
