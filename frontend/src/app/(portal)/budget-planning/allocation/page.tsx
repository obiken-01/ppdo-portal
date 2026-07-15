"use client";

/**
 * Allocation page — RAL-101. Per-fund-source ceiling & allocation — v1.4.3, RAL-155.
 *
 * Finance-officer-only page for setting the PBO budget ceiling per office (now per
 * active funding source, RAL-154/155), distributing it among divisions, and
 * assigning AIP programs to divisions.
 *
 * Access: canManageAllocation. Hidden in sidebar for everyone else.
 * Route:  /budget-planning/allocation
 *
 * Amounts are in PESOS — no ×1000 conversion (that lives in WFP only).
 *
 * Tab 1 — Ceiling & Division Allocation:
 *   One ceiling + division-allocation section per ACTIVE funding source (config-driven,
 *   never hardcoded — §2 D1 of docs/v1.4.3/v1.4.3_Requirements.md). General Fund is
 *   always expanded first; other funds render as collapsible cards with a one-line
 *   status summary until expanded.
 *
 * Tab 2 — PPA → Division:
 *   Reuses the WFP Sector→Program hierarchy (collapsed at Program level).
 *   Each division gets a checkbox column. Multi-division toggle per program.
 *   Unassigned filter + bulk-assign + per-division assigned counts in headers.
 *   NOT fund-scoped — unchanged by RAL-155.
 *
 * Endpoints (AllocationFunctions.cs, { data, error, message } envelope):
 *   GET/PUT /api/budget-planning/allocation/ceiling?officeId=&fiscalYear=&fundingSourceId=
 *   GET     /api/budget-planning/allocation/ceilings?officeId=&fiscalYear= (all funds, RAL-154)
 *   GET/PUT /api/budget-planning/allocation/divisions?officeId=&fiscalYear=&fundingSourceId=
 *   GET/PUT /api/budget-planning/allocation/programs?officeId=&fiscalYear=
 *   GET     /api/budget-planning/allocation/status?officeId=&fiscalYear=&divisionId=
 */

import { Fragment, useEffect, useMemo, useState } from "react";
import { useMe } from "@/lib/me-cache";
import { findGeneralFund, findPpdoOffice, listOffices, listDivisions, listFundingSources } from "@/lib/config";
import {
  allocationErrorMessage,
  getAllocations,
  getCeilings,
  getPrograms,
  upsertAllocations,
  upsertCeiling,
  upsertProgram,
} from "@/lib/allocation";
import MoneyInput from "@/components/ui/MoneyInput";
import OfficeSelect from "@/components/ui/OfficeSelect";
import { useToast } from "@/components/ui/Toast";
import { formatMoney } from "@/lib/money";
import type {
  BudgetCeilingDto,
  DivisionAllocationDto,
  DivisionResponse,
  FundingSourceResponse,
  OfficeResponse,
  ProgramAssignmentDto,
} from "@/types";

// ---------------------------------------------------------------------------
// Constants
// ---------------------------------------------------------------------------

const DIVISION_COLORS = [
  "#2563eb", // blue-600
  "#16a34a", // green-600
  "#d97706", // amber-600
  "#7c3aed", // violet-600
  "#0891b2", // cyan-600
  "#ea580c", // orange-600
  "#be185d", // pink-700
  "#0f766e", // teal-700
];

// ---------------------------------------------------------------------------
// Allocation stacked bar
// ---------------------------------------------------------------------------

function AllocationBar({
  ceiling,
  divisions,
  allocationInputs,
}: {
  ceiling: number;
  divisions: DivisionResponse[];
  allocationInputs: Record<number, number | null>;
}) {
  if (ceiling <= 0) return null;

  const total = divisions.reduce((sum, d) => sum + (allocationInputs[d.id] ?? 0), 0);
  const isOver = total > ceiling + 0.001;
  // When over: normalize segments by total so they sum to 100%; no grey.
  // When under: normalize by ceiling; grey fills the rest.
  const denom = isOver ? total : ceiling;

  return (
    <div className="mt-4">
      <div
        className={`flex h-5 overflow-hidden border ${
          isOver ? "border-red-300" : "border-slate-200"
        }`}
      >
        {divisions.map((div, i) => {
          const amount = allocationInputs[div.id] ?? 0;
          if (amount <= 0) return null;
          const pct = (amount / denom) * 100;
          return (
            <div
              key={div.id}
              style={{
                width: `${pct}%`,
                backgroundColor: DIVISION_COLORS[i % DIVISION_COLORS.length],
                minWidth: 0,
              }}
              title={`${div.name}: ₱${formatMoney(amount)} · ${
                ceiling > 0 ? ((amount / ceiling) * 100).toFixed(1) : 0
              }% of ceiling`}
            />
          );
        })}
        {!isOver && <div className="flex-1 bg-slate-100" />}
      </div>

      {/* Legend */}
      <div className="flex flex-wrap gap-x-4 gap-y-1 mt-1.5">
        {divisions.map((div, i) => {
          const amount = allocationInputs[div.id] ?? 0;
          if (amount <= 0) return null;
          const pct = ceiling > 0 ? ((amount / ceiling) * 100).toFixed(1) : "0";
          return (
            <span key={div.id} className="flex items-center gap-1.5 text-xs text-slate-600">
              <span
                className="inline-block w-2.5 h-2.5 shrink-0"
                style={{ backgroundColor: DIVISION_COLORS[i % DIVISION_COLORS.length] }}
              />
              {div.name}: {pct}%
            </span>
          );
        })}
        {!isOver && total > 0 && (
          <span className="flex items-center gap-1.5 text-xs text-slate-600">
            <span className="inline-block w-2.5 h-2.5 shrink-0 bg-slate-200" />
            Unallocated: {(((ceiling - total) / ceiling) * 100).toFixed(1)}%
          </span>
        )}
      </div>
    </div>
  );
}

// ---------------------------------------------------------------------------
// FundSection — one ceiling + division-allocation block per funding source (RAL-155)
// ---------------------------------------------------------------------------

function FundSection({
  fund,
  isGeneralFund,
  expanded,
  onToggleExpand,
  selectedOffice,
  selectedFiscalYear,
  ceiling,
  ceilingInput,
  onCeilingInputChange,
  onSaveCeiling,
  savingCeiling,
  divisions,
  allocationInputs,
  onAllocationInputChange,
  onSaveAllocations,
  savingAllocations,
}: {
  fund: FundingSourceResponse;
  isGeneralFund: boolean;
  expanded: boolean;
  onToggleExpand: () => void;
  selectedOffice: OfficeResponse | null;
  selectedFiscalYear: number;
  ceiling: BudgetCeilingDto | null;
  ceilingInput: number | null;
  onCeilingInputChange: (v: number | null) => void;
  onSaveCeiling: () => void;
  savingCeiling: boolean;
  divisions: DivisionResponse[];
  allocationInputs: Record<number, number | null>;
  onAllocationInputChange: (divisionId: number, v: number | null) => void;
  onSaveAllocations: () => void;
  savingAllocations: boolean;
}) {
  const allocationTotal = divisions.reduce((sum, d) => sum + (allocationInputs[d.id] ?? 0), 0);
  const isOverCeiling = (ceilingInput ?? 0) > 0 && allocationTotal > (ceilingInput ?? 0) + 0.001;
  const remaining = (ceilingInput ?? 0) - allocationTotal;

  const statusLabel = !ceiling ? "Not set" : allocationTotal > 0 ? "Set up" : "Ceiling only";
  const statusClasses =
    statusLabel === "Set up"
      ? "text-green-700 bg-green-100"
      : statusLabel === "Ceiling only"
      ? "text-amber-700 bg-amber-100"
      : "text-slate-600 bg-slate-100 border border-slate-200";

  const body = (
    <>
      {/* Ceiling */}
      <div className="flex items-center gap-3 mb-1">
        <MoneyInput value={ceilingInput} onChange={onCeilingInputChange} className="w-52" />
        <button
          onClick={onSaveCeiling}
          disabled={savingCeiling || !ceilingInput || ceilingInput <= 0}
          className="px-4 py-2 bg-green-700 text-white text-sm font-medium hover:bg-green-600 transition-colors disabled:opacity-50 disabled:cursor-not-allowed flex items-center gap-2"
        >
          {savingCeiling && (
            <span className="w-4 h-4 border-2 border-white border-t-transparent rounded-full animate-spin" />
          )}
          Set Ceiling
        </button>
      </div>
      {ceiling && (
        <p className="mb-4 text-xs text-slate-600">Saved ceiling: ₱{formatMoney(ceiling.amount)}</p>
      )}
      {!ceiling && <div className="mb-4" />}

      {divisions.length === 0 ? (
        <p className="text-sm text-slate-600">
          No divisions configured for this office. Add divisions in Config → Divisions.
        </p>
      ) : (
        <>
          <div
            className={`mb-3 text-sm font-medium tabular-nums ${
              isOverCeiling ? "text-red-600" : "text-slate-700"
            }`}
          >
            Allocated ₱{formatMoney(allocationTotal)} of ₱
            {formatMoney(ceilingInput ?? 0)} · Remaining ₱
            {formatMoney(remaining)}
            {isOverCeiling && (
              <span className="ml-2 text-xs font-normal text-red-500">
                (over by ₱{formatMoney(allocationTotal - (ceilingInput ?? 0))})
              </span>
            )}
          </div>

          {(ceilingInput ?? 0) > 0 && (
            <AllocationBar
              ceiling={ceilingInput ?? 0}
              divisions={divisions}
              allocationInputs={allocationInputs}
            />
          )}

          <table className="w-full mt-4 text-sm border-collapse">
            <thead>
              <tr className="border-b border-slate-200">
                <th className="text-left py-2 text-xs font-medium text-slate-600 uppercase tracking-wide">
                  Division
                </th>
                <th className="text-right py-2 text-xs font-medium text-slate-600 uppercase tracking-wide w-52">
                  Amount (₱)
                </th>
                <th className="text-right py-2 text-xs font-medium text-slate-600 uppercase tracking-wide w-20">
                  % of Ceiling
                </th>
              </tr>
            </thead>
            <tbody className="divide-y divide-slate-100">
              {divisions.map((div, i) => {
                const amount = allocationInputs[div.id] ?? null;
                const pct =
                  (ceilingInput ?? 0) > 0 && (amount ?? 0) > 0
                    ? (((amount ?? 0) / ceilingInput!) * 100).toFixed(1)
                    : "—";
                return (
                  <tr key={div.id} className="hover:bg-slate-50">
                    <td className="py-2 pr-3">
                      <span className="flex items-center gap-2">
                        <span
                          className="w-2.5 h-2.5 shrink-0"
                          style={{
                            backgroundColor: DIVISION_COLORS[i % DIVISION_COLORS.length],
                          }}
                        />
                        {div.name}
                        {div.code && (
                          <span className="text-xs text-slate-600 font-mono">{div.code}</span>
                        )}
                      </span>
                    </td>
                    <td className="py-2 text-right">
                      <div className="flex justify-end">
                        <MoneyInput
                          value={amount}
                          onChange={(v) => onAllocationInputChange(div.id, v)}
                          className="w-48 text-sm"
                        />
                      </div>
                    </td>
                    <td className="py-2 text-right text-slate-600 tabular-nums">
                      {pct}
                      {pct !== "—" ? "%" : ""}
                    </td>
                  </tr>
                );
              })}
            </tbody>
            <tfoot>
              <tr className="border-t border-slate-300">
                <td className="py-2 text-sm font-semibold text-slate-700">Total</td>
                <td
                  className={`py-2 text-right font-semibold tabular-nums ${
                    isOverCeiling ? "text-red-600" : "text-slate-700"
                  }`}
                >
                  ₱{formatMoney(allocationTotal)}
                </td>
                <td className="py-2 text-right text-slate-600 tabular-nums">
                  {(ceilingInput ?? 0) > 0
                    ? `${((allocationTotal / ceilingInput!) * 100).toFixed(1)}%`
                    : "—"}
                </td>
              </tr>
            </tfoot>
          </table>

          <div className="mt-4 flex items-center gap-3">
            <button
              onClick={onSaveAllocations}
              disabled={savingAllocations || isOverCeiling || !ceiling}
              className={`px-4 py-2 text-sm font-medium transition-colors disabled:cursor-not-allowed flex items-center gap-2 ${
                isOverCeiling
                  ? "bg-red-100 text-red-700 border border-red-300"
                  : "bg-green-700 text-white hover:bg-green-600 disabled:opacity-50"
              }`}
            >
              {savingAllocations && (
                <span className="w-4 h-4 border-2 border-white border-t-transparent rounded-full animate-spin" />
              )}
              {isOverCeiling ? "Over Ceiling — Cannot Save" : "Save Allocations"}
            </button>
            {!ceiling && (
              <span className="text-xs text-slate-600">
                Set a ceiling first before saving allocations.
              </span>
            )}
          </div>
        </>
      )}
    </>
  );

  if (isGeneralFund) {
    return (
      <div className="border border-slate-200 p-4">
        <h2 className="text-sm font-semibold text-slate-700 mb-3 flex items-center gap-2">
          {fund.name}
          <span className="text-[10px] font-medium text-green-700 bg-green-100 px-1.5 py-0.5">
            Required
          </span>
          {selectedOffice && (
            <span className="font-normal text-slate-600">
              — {selectedOffice.officeName} · FY{selectedFiscalYear}
            </span>
          )}
        </h2>
        {body}
      </div>
    );
  }

  return (
    <div className="border border-slate-200">
      <button
        onClick={onToggleExpand}
        className="w-full flex flex-wrap items-center justify-between gap-2 px-4 py-3 text-left hover:bg-slate-50 transition-colors"
      >
        <span className="flex items-center gap-2 text-sm font-medium text-slate-700">
          <span className="text-slate-600">{expanded ? "▼" : "▶"}</span>
          {fund.color && (
            <span
              className="w-2.5 h-2.5 shrink-0"
              style={{ backgroundColor: fund.color }}
            />
          )}
          {fund.name}
          <span className={`text-[10px] font-medium px-1.5 py-0.5 ${statusClasses}`}>
            {statusLabel}
          </span>
        </span>
        <span className="text-xs text-slate-600">
          {ceiling
            ? `Ceiling ₱${formatMoney(ceiling.amount)} · Allocated ₱${formatMoney(
                allocationTotal
              )} · Remaining ₱${formatMoney(remaining)}`
            : "No ceiling set"}
        </span>
      </button>
      {expanded && <div className="px-4 pb-4">{body}</div>}
    </div>
  );
}

// ---------------------------------------------------------------------------
// AllocationPageInner
// ---------------------------------------------------------------------------

function AllocationPageInner() {
  const { toast } = useToast();
  const me = useMe((m) => m.canManageAllocation);

  // ── Selectors ─────────────────────────────────────────────────────────────

  const [officeList, setOfficeList] = useState<OfficeResponse[]>([]);
  const [selectedOfficeId, setSelectedOfficeId] = useState<number | null>(null);
  const [selectedFiscalYear, setSelectedFiscalYear] = useState<number>(
    new Date().getFullYear() + 1
  );

  // ── Loaded data ────────────────────────────────────────────────────────────

  const [divisions, setDivisions] = useState<DivisionResponse[]>([]);
  const [fundList, setFundList] = useState<FundingSourceResponse[]>([]);
  const [programs, setPrograms] = useState<ProgramAssignmentDto[]>([]);
  const [loading, setLoading] = useState(false);

  // ── Tab state ─────────────────────────────────────────────────────────────

  const [activeTab, setActiveTab] = useState<"ceiling" | "ppa">("ceiling");

  // ── Tab 1: Ceiling & Division Allocation — per fund source (RAL-155) ───────

  const [ceilings, setCeilings] = useState<Record<number, BudgetCeilingDto | null>>({});
  const [ceilingInputs, setCeilingInputs] = useState<Record<number, number | null>>({});
  const [allocationInputsByFund, setAllocationInputsByFund] =
    useState<Record<number, Record<number, number | null>>>({});
  const [savingCeilingFundId, setSavingCeilingFundId] = useState<number | null>(null);
  const [savingAllocationsFundId, setSavingAllocationsFundId] = useState<number | null>(null);
  const [expandedFundIds, setExpandedFundIds] = useState<Set<number>>(new Set());

  const generalFund = useMemo(() => findGeneralFund(fundList), [fundList]);
  const otherFunds = useMemo(
    () => fundList.filter((f) => f.id !== generalFund?.id),
    [fundList, generalFund]
  );
  const orderedFunds = useMemo(
    () => (generalFund ? [generalFund, ...otherFunds] : otherFunds),
    [generalFund, otherFunds]
  );

  // ── Tab 2: PPA → Division ─────────────────────────────────────────────────

  const [localAssignments, setLocalAssignments] = useState<Record<string, number[]>>({});
  const [multiDivision, setMultiDivision] = useState<Record<string, boolean>>({});
  const [showUnassignedOnly, setShowUnassignedOnly] = useState(false);
  const [savingPpa, setSavingPpa] = useState(false);
  const [checkedPrograms, setCheckedPrograms] = useState<Set<string>>(new Set());
  const [bulkDivisionId, setBulkDivisionId] = useState<number | null>(null);
  const [collapsedSectors, setCollapsedSectors] = useState<Set<string>>(new Set());

  // ── Derived ───────────────────────────────────────────────────────────────

  const isOfficeUser = me != null && me.officeId != null;
  const selectedOffice = officeList.find((o) => o.id === selectedOfficeId) ?? null;

  const unassignedCount = useMemo(
    () =>
      programs.filter(
        (p) =>
          (localAssignments[`${p.officeRefCode}:${p.programRefCode}`]?.length ?? 0) === 0
      ).length,
    [programs, localAssignments]
  );

  const groupedPrograms = useMemo(() => {
    const filtered = showUnassignedOnly
      ? programs.filter(
          (p) =>
            (localAssignments[`${p.officeRefCode}:${p.programRefCode}`]?.length ?? 0) === 0
        )
      : programs;
    const map = new Map<string, ProgramAssignmentDto[]>();
    for (const p of filtered) {
      if (!map.has(p.sector)) map.set(p.sector, []);
      map.get(p.sector)!.push(p);
    }
    return Array.from(map.entries());
  }, [programs, localAssignments, showUnassignedOnly]);

  // ── Load offices + active fund sources on mount ───────────────────────────
  // Fund sources are global config (not office/FY-scoped) — loaded once here,
  // never hardcoded (§2 D1).

  useEffect(() => {
    listOffices({ active: "true" })
      .then(setOfficeList)
      .catch(() => toast.error("Load failed", "Could not load offices."));
    listFundingSources({ active: "true" })
      .then(setFundList)
      .catch(() => toast.error("Load failed", "Could not load funding sources."));
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, []);

  // ── Pre-fill office: non-PPDO users get their own office; PPDO-internal ──
  // finance officers default to the PPDO office itself, but remain free to
  // switch to another office (Allocation manages ceilings across offices).

  useEffect(() => {
    if (!me) return;
    if (me.officeId != null) {
      setSelectedOfficeId(me.officeId);
    } else {
      const ppdo = findPpdoOffice(officeList);
      if (ppdo) setSelectedOfficeId(ppdo.id);
    }
  }, [me, officeList]);

  // ── Load allocation data when office, FY, or the fund list changes ───────
  // Division allocations have no bulk-across-funds endpoint — one call per
  // active fund, run in parallel. Ceilings DO have a bulk endpoint (RAL-154).

  useEffect(() => {
    if (selectedOfficeId == null) {
      setDivisions([]);
      setCeilings({});
      setCeilingInputs({});
      setAllocationInputsByFund({});
      setPrograms([]);
      setLocalAssignments({});
      setMultiDivision({});
      setCheckedPrograms(new Set());
      return;
    }

    let cancelled = false;

    async function load() {
      setLoading(true);
      try {
        const [divs, ceilingList, progs] = await Promise.all([
          listDivisions({ active: "true", officeId: selectedOfficeId! }),
          getCeilings(selectedOfficeId!, selectedFiscalYear),
          getPrograms(selectedOfficeId!, selectedFiscalYear),
        ]);
        if (cancelled) return;

        setDivisions(divs);

        const ceilingsByFund: Record<number, BudgetCeilingDto | null> = {};
        const ceilingInputsByFund: Record<number, number | null> = {};
        for (const fund of fundList) {
          const found = ceilingList.find((c) => c.fundingSourceId === fund.id) ?? null;
          ceilingsByFund[fund.id] = found;
          ceilingInputsByFund[fund.id] = found?.amount ?? null;
        }
        setCeilings(ceilingsByFund);
        setCeilingInputs(ceilingInputsByFund);

        const allocLists = await Promise.all(
          fundList.map((fund) => getAllocations(selectedOfficeId!, selectedFiscalYear, fund.id))
        );
        if (cancelled) return;

        const allocInputsByFund: Record<number, Record<number, number | null>> = {};
        fundList.forEach((fund, i) => {
          const allocs: DivisionAllocationDto[] = allocLists[i];
          const inputs: Record<number, number | null> = {};
          for (const div of divs) {
            const saved = allocs.find((a) => a.divisionId === div.id);
            inputs[div.id] = saved?.amount ?? null;
          }
          allocInputsByFund[fund.id] = inputs;
        });
        setAllocationInputsByFund(allocInputsByFund);

        setPrograms(progs);
        const assignments: Record<string, number[]> = {};
        for (const p of progs) {
          assignments[`${p.officeRefCode}:${p.programRefCode}`] = [...p.divisionIds];
        }
        setLocalAssignments(assignments);
        setMultiDivision({});
        setCheckedPrograms(new Set());
        setBulkDivisionId(null);
        setCollapsedSectors(new Set());
        setExpandedFundIds(new Set());
      } catch {
        if (!cancelled) toast.error("Load failed", "Could not load allocation data.");
      } finally {
        if (!cancelled) setLoading(false);
      }
    }

    load();
    return () => { cancelled = true; };
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [selectedOfficeId, selectedFiscalYear, fundList]);

  // ── Tab 1: Ceiling & Allocation — per fund source ─────────────────────────

  async function handleSaveCeiling(fundId: number) {
    const amount = ceilingInputs[fundId];
    if (selectedOfficeId == null || amount == null || amount <= 0) return;
    setSavingCeilingFundId(fundId);
    try {
      const result = await upsertCeiling({
        officeId: selectedOfficeId,
        fiscalYear: selectedFiscalYear,
        fundingSourceId: fundId,
        amount,
      });
      setCeilings((prev) => ({ ...prev, [fundId]: result }));
      toast.success("Saved", "Budget ceiling updated.");
    } catch (err) {
      toast.error("Save failed", allocationErrorMessage(err, "Could not save ceiling."));
    } finally {
      setSavingCeilingFundId(null);
    }
  }

  async function handleSaveAllocations(fundId: number) {
    const inputs = allocationInputsByFund[fundId] ?? {};
    const total = divisions.reduce((sum, d) => sum + (inputs[d.id] ?? 0), 0);
    const ceilingAmount = ceilingInputs[fundId] ?? 0;
    const isOver = ceilingAmount > 0 && total > ceilingAmount + 0.001;
    if (selectedOfficeId == null || isOver) return;

    setSavingAllocationsFundId(fundId);
    try {
      const allocs = divisions.map((d) => ({
        divisionId: d.id,
        amount: inputs[d.id] ?? 0,
      }));
      await upsertAllocations({
        officeId: selectedOfficeId,
        fiscalYear: selectedFiscalYear,
        fundingSourceId: fundId,
        allocations: allocs,
      });
      toast.success("Saved", "Division allocations saved.");
    } catch (err) {
      toast.error("Save failed", allocationErrorMessage(err, "Could not save allocations."));
    } finally {
      setSavingAllocationsFundId(null);
    }
  }

  function toggleFundExpanded(fundId: number) {
    setExpandedFundIds((prev) => {
      const s = new Set(prev);
      if (s.has(fundId)) s.delete(fundId);
      else s.add(fundId);
      return s;
    });
  }

  // ── Tab 2: PPA → Division ─────────────────────────────────────────────────

  function handleProgramCheck(
    p: ProgramAssignmentDto,
    divId: number,
    checked: boolean
  ) {
    const key = `${p.officeRefCode}:${p.programRefCode}`;
    const isMulti = multiDivision[key] ?? false;
    const current = localAssignments[key] ?? [];
    let newIds: number[];
    if (checked) {
      newIds = isMulti ? [...current, divId] : [divId];
    } else {
      newIds = current.filter((id) => id !== divId);
    }
    setLocalAssignments((prev) => ({ ...prev, [key]: newIds }));
  }

  function handleBulkAssign() {
    if (bulkDivisionId == null || checkedPrograms.size === 0) return;
    const keys = Array.from(checkedPrograms);
    setLocalAssignments((prev) => {
      const next = { ...prev };
      for (const key of keys) {
        const current = next[key] ?? [];
        next[key] = current.includes(bulkDivisionId) ? current : [...current, bulkDivisionId];
      }
      return next;
    });
    setCheckedPrograms(new Set());
    setBulkDivisionId(null);
  }

  async function handleSavePpa() {
    if (savingPpa || programs.length === 0) return;
    setSavingPpa(true);
    let failed = 0;
    try {
      for (const p of programs) {
        const key = `${p.officeRefCode}:${p.programRefCode}`;
        try {
          await upsertProgram({
            officeRefCode: p.officeRefCode,
            programRefCode: p.programRefCode,
            divisionIds: localAssignments[key] ?? [],
          });
        } catch {
          failed++;
        }
      }
      if (failed > 0) {
        toast.error("Partial failure", `${failed} program(s) could not be saved.`);
      } else {
        toast.success("Saved", "Program assignments saved.");
      }
    } finally {
      setSavingPpa(false);
    }
  }

  function assignedCountForDiv(divId: number): number {
    return programs.filter((p) =>
      (localAssignments[`${p.officeRefCode}:${p.programRefCode}`] ?? []).includes(divId)
    ).length;
  }

  function toggleSectorCollapse(sector: string) {
    setCollapsedSectors((prev) => {
      const s = new Set(prev);
      if (s.has(sector)) s.delete(sector);
      else s.add(sector);
      return s;
    });
  }

  // ── Bulk select helpers ───────────────────────────────────────────────────

  const visibleProgramKeys = useMemo(
    () =>
      groupedPrograms.flatMap(([, progs]) =>
        progs.map((p) => `${p.officeRefCode}:${p.programRefCode}`)
      ),
    [groupedPrograms]
  );

  const allChecked =
    visibleProgramKeys.length > 0 &&
    visibleProgramKeys.every((k) => checkedPrograms.has(k));

  function toggleSelectAll() {
    if (allChecked) {
      setCheckedPrograms(new Set());
    } else {
      setCheckedPrograms(new Set(visibleProgramKeys));
    }
  }

  // ── Render ────────────────────────────────────────────────────────────────

  return (
    <div className="flex flex-col min-h-full">
      <div className="p-6 max-w-screen-xl mx-auto w-full flex-1">

        {/* Header */}
        <h1 className="text-xl font-bold text-slate-800 mb-5">Allocation</h1>

        {/* Selectors */}
        <div className="flex flex-wrap items-center gap-4 mb-5">
          {/* Fiscal Year */}
          <div className="flex items-center gap-2">
            <label className="text-sm text-slate-600 font-medium whitespace-nowrap">
              Fiscal Year
            </label>
            <input
              type="number"
              value={selectedFiscalYear}
              min={2020}
              max={2050}
              onChange={(e) => setSelectedFiscalYear(Number(e.target.value))}
              className="border border-slate-300 bg-white text-sm px-2 py-1.5 w-24 text-slate-700 focus:outline-none focus:ring-1 focus:ring-green-600"
            />
          </div>

          {/* Office */}
          <div className="flex items-center gap-2">
            <label className="text-sm text-slate-600 font-medium whitespace-nowrap">
              Office
            </label>
            {isOfficeUser ? (
              <span className="text-sm text-slate-700 font-medium">
                {me?.officeName ?? `Office #${me?.officeId}`}
              </span>
            ) : (
              <OfficeSelect
                className="w-64"
                offices={officeList}
                value={selectedOfficeId}
                onChange={setSelectedOfficeId}
                placeholder="— select office —"
              />
            )}
          </div>
        </div>

        {/* Loading */}
        {loading && (
          <div className="flex items-center gap-2 text-slate-600 text-sm py-6">
            <span className="w-4 h-4 border-2 border-slate-300 border-t-green-600 rounded-full animate-spin" />
            Loading…
          </div>
        )}

        {/* Empty state */}
        {!loading && selectedOfficeId == null && (
          <p className="text-slate-600 text-sm py-6">
            Select an office to configure allocation.
          </p>
        )}

        {/* Main content */}
        {!loading && selectedOfficeId != null && (
          <>
            {/* Tabs */}
            <div className="flex border-b border-slate-200 mb-6">
              {(["ceiling", "ppa"] as const).map((tab) => (
                <button
                  key={tab}
                  onClick={() => setActiveTab(tab)}
                  className={`px-5 py-2.5 text-sm font-medium border-b-2 transition-colors ${
                    activeTab === tab
                      ? "border-green-600 text-green-700"
                      : "border-transparent text-slate-600 hover:text-slate-700"
                  }`}
                >
                  {tab === "ceiling" ? "Ceiling & Division Allocation" : "PPA → Division"}
                </button>
              ))}
            </div>

            {/* ── TAB 1: Ceiling & Division Allocation ───────────────── */}
            {activeTab === "ceiling" && (
              <div className="max-w-2xl space-y-3">
                <p className="text-xs text-slate-600">
                  One ceiling and division split per active fund source. General Fund is
                  required; others are optional.
                </p>

                {fundList.length === 0 ? (
                  <p className="text-sm text-slate-600 py-4">
                    No active funding sources configured. Add them in Config → Funding Sources.
                  </p>
                ) : (
                  orderedFunds.map((fund) => (
                    <FundSection
                      key={fund.id}
                      fund={fund}
                      isGeneralFund={generalFund != null && fund.id === generalFund.id}
                      expanded={
                        (generalFund != null && fund.id === generalFund.id) ||
                        expandedFundIds.has(fund.id)
                      }
                      onToggleExpand={() => toggleFundExpanded(fund.id)}
                      selectedOffice={selectedOffice}
                      selectedFiscalYear={selectedFiscalYear}
                      ceiling={ceilings[fund.id] ?? null}
                      ceilingInput={ceilingInputs[fund.id] ?? null}
                      onCeilingInputChange={(v) =>
                        setCeilingInputs((prev) => ({ ...prev, [fund.id]: v }))
                      }
                      onSaveCeiling={() => handleSaveCeiling(fund.id)}
                      savingCeiling={savingCeilingFundId === fund.id}
                      divisions={divisions}
                      allocationInputs={allocationInputsByFund[fund.id] ?? {}}
                      onAllocationInputChange={(divId, v) =>
                        setAllocationInputsByFund((prev) => ({
                          ...prev,
                          [fund.id]: { ...(prev[fund.id] ?? {}), [divId]: v },
                        }))
                      }
                      onSaveAllocations={() => handleSaveAllocations(fund.id)}
                      savingAllocations={savingAllocationsFundId === fund.id}
                    />
                  ))
                )}
              </div>
            )}

            {/* ── TAB 2: PPA → Division ────────────────────────────────────── */}
            {activeTab === "ppa" && (
              <div>
                {divisions.length === 0 ? (
                  <p className="text-sm text-slate-600 py-4">
                    No divisions configured for this office. Add divisions in Config → Divisions.
                  </p>
                ) : programs.length === 0 ? (
                  <p className="text-sm text-slate-600 py-4">
                    No programs found for FY{selectedFiscalYear} on this office. Upload an AIP first.
                  </p>
                ) : (
                  <>
                    {/* Filter + bulk action bar */}
                    <div className="flex flex-wrap items-center gap-3 mb-4 justify-between">
                      {/* Unassigned filter toggle */}
                      <button
                        onClick={() => setShowUnassignedOnly((p) => !p)}
                        className={`flex items-center gap-1.5 px-3 py-1.5 text-xs font-medium border transition-colors ${
                          showUnassignedOnly
                            ? "border-amber-400 bg-amber-50 text-amber-700"
                            : "border-slate-200 text-slate-600 hover:bg-slate-50"
                        }`}
                      >
                        Unassigned
                        {unassignedCount > 0 && (
                          <span
                            className={`px-1.5 py-0.5 text-xs font-bold ${
                              showUnassignedOnly
                                ? "bg-amber-200 text-amber-800"
                                : "bg-amber-100 text-amber-700"
                            }`}
                          >
                            {unassignedCount}
                          </span>
                        )}
                      </button>

                      {/* Bulk assign + Save — right side */}
                      <div className="flex items-center gap-2">
                      {checkedPrograms.size > 0 && (
                        <div className="flex items-center gap-2 border border-slate-200 px-3 py-1.5 bg-slate-50">
                          <span className="text-xs text-slate-600 font-medium">
                            {checkedPrograms.size} selected →
                          </span>
                          <select
                            value={bulkDivisionId ?? ""}
                            onChange={(e) =>
                              setBulkDivisionId(e.target.value ? Number(e.target.value) : null)
                            }
                            className="text-xs border border-slate-200 px-1.5 py-0.5 bg-white focus:outline-none focus:ring-1 focus:ring-green-500"
                          >
                            <option value="">— assign to division —</option>
                            {divisions.map((d) => (
                              <option key={d.id} value={d.id}>
                                {d.name}
                              </option>
                            ))}
                          </select>
                          <button
                            onClick={handleBulkAssign}
                            disabled={bulkDivisionId == null}
                            className="px-2.5 py-1 text-xs bg-green-700 text-white font-medium hover:bg-green-600 transition-colors disabled:opacity-50 disabled:cursor-not-allowed"
                          >
                            Assign
                          </button>
                          <button
                            onClick={() => setCheckedPrograms(new Set())}
                            className="text-xs text-slate-600 hover:text-slate-600"
                          >
                            Clear
                          </button>
                        </div>
                      )}

                      {/* Save button — top-right */}
                      <button
                        onClick={handleSavePpa}
                        disabled={savingPpa || programs.length === 0}
                        className="px-4 py-1.5 bg-green-700 text-white text-sm font-medium hover:bg-green-600 transition-colors disabled:opacity-50 disabled:cursor-not-allowed flex items-center gap-2"
                      >
                        {savingPpa && (
                          <span className="w-3.5 h-3.5 border-2 border-white border-t-transparent rounded-full animate-spin" />
                        )}
                        Save
                      </button>
                      </div>
                    </div>

                    {/* Programs grid — no overflow here; <main> handles scroll so sticky thead works */}
                    <div className="border border-slate-200">
                      <table className="text-sm border-collapse"
                        style={{ minWidth: `${420 + divisions.length * 100}px` }}>
                        <thead className="sticky top-0 z-10 bg-slate-50">
                          <tr className="text-xs text-slate-600 border-b border-slate-200">
                            <th className="px-3 py-2 w-8">
                              <input
                                type="checkbox"
                                checked={allChecked}
                                onChange={toggleSelectAll}
                                className="cursor-pointer"
                              />
                            </th>
                            <th className="px-3 py-2 text-left whitespace-nowrap w-32">
                              REF CODE
                            </th>
                            <th className="px-3 py-2 text-left">PROGRAM</th>
                            <th className="px-3 py-2 text-center whitespace-nowrap w-24">
                              MULTI?
                            </th>
                            {divisions.map((div, i) => (
                              <th
                                key={div.id}
                                className="px-2 py-2 text-center whitespace-nowrap w-24"
                              >
                                <div className="flex flex-col items-center gap-0.5">
                                  <span
                                    className="w-2 h-2"
                                    style={{
                                      backgroundColor:
                                        DIVISION_COLORS[i % DIVISION_COLORS.length],
                                    }}
                                  />
                                  <span className="text-xs leading-tight">
                                    {div.code ?? div.name.split(" ")[0]}
                                  </span>
                                  <span className="text-[10px] text-slate-600">
                                    {assignedCountForDiv(div.id)}/{programs.length}
                                  </span>
                                </div>
                              </th>
                            ))}
                          </tr>
                        </thead>
                        <tbody className="divide-y divide-slate-100">
                          {groupedPrograms.map(([sector, sectorPrograms]) => {
                            const isCollapsed = collapsedSectors.has(sector);
                            return (
                              <Fragment key={sector}>
                                {/* Sector header row */}
                                <tr className="bg-slate-200">
                                  <td
                                    colSpan={4 + divisions.length}
                                    className="px-3 py-1.5 text-xs font-bold text-slate-700 uppercase tracking-wide"
                                  >
                                    <button
                                      onClick={() => toggleSectorCollapse(sector)}
                                      className="mr-2 text-slate-600 hover:text-slate-700"
                                    >
                                      {isCollapsed ? "▶" : "▼"}
                                    </button>
                                    {sector}
                                    <span className="ml-2 font-normal text-slate-600">
                                      ({sectorPrograms.length})
                                    </span>
                                  </td>
                                </tr>

                                {/* Program rows */}
                                {!isCollapsed &&
                                  sectorPrograms.map((p) => {
                                    const key = `${p.officeRefCode}:${p.programRefCode}`;
                                    const assigned = localAssignments[key] ?? [];
                                    const isMulti = multiDivision[key] ?? false;
                                    const isChecked = checkedPrograms.has(key);
                                    const isUnassigned = assigned.length === 0;

                                    return (
                                      <tr
                                        key={key}
                                        className={`hover:bg-slate-50 ${
                                          isUnassigned ? "bg-amber-50/30" : "bg-white"
                                        }`}
                                      >
                                        {/* Row checkbox */}
                                        <td className="px-3 py-2 text-center">
                                          <input
                                            type="checkbox"
                                            checked={isChecked}
                                            onChange={(e) =>
                                              setCheckedPrograms((prev) => {
                                                const s = new Set(prev);
                                                if (e.target.checked) s.add(key);
                                                else s.delete(key);
                                                return s;
                                              })
                                            }
                                            className="cursor-pointer"
                                          />
                                        </td>

                                        {/* Ref Code */}
                                        <td className="px-3 py-2 font-mono text-xs text-slate-600 whitespace-nowrap align-top">
                                          {p.programRefCode}
                                        </td>

                                        {/* Program Name */}
                                        <td className="px-3 py-2 text-slate-700 align-top">
                                          <div className="flex items-start gap-2">
                                            <span className="flex-1 min-w-0">{p.programName}</span>
                                            {isUnassigned && (
                                              <span className="text-[10px] font-medium text-amber-600 bg-amber-100 px-1 py-0.5 shrink-0">
                                                Unassigned
                                              </span>
                                            )}
                                          </div>
                                        </td>

                                        {/* Multi-division toggle */}
                                        <td className="px-3 py-2 text-center align-top">
                                          <button
                                            onClick={() =>
                                              setMultiDivision((prev) => ({
                                                ...prev,
                                                [key]: !isMulti,
                                              }))
                                            }
                                            className={`px-2 py-0.5 text-xs font-medium border transition-colors ${
                                              isMulti
                                                ? "border-blue-300 bg-blue-50 text-blue-700"
                                                : "border-slate-200 text-slate-600 hover:bg-slate-50"
                                            }`}
                                            title={
                                              isMulti
                                                ? "Multi-division (multiple allowed)"
                                                : "Single-division (radio)"
                                            }
                                          >
                                            {isMulti ? "Multi" : "Single"}
                                          </button>
                                        </td>

                                        {/* Division checkboxes */}
                                        {divisions.map((div) => {
                                          const isAssigned = assigned.includes(div.id);
                                          return (
                                            <td
                                              key={div.id}
                                              className="px-2 py-2 text-center align-top"
                                            >
                                              <input
                                                type={isMulti ? "checkbox" : "radio"}
                                                name={isMulti ? undefined : key}
                                                checked={isAssigned}
                                                onChange={(e) =>
                                                  handleProgramCheck(p, div.id, e.target.checked)
                                                }
                                                className="cursor-pointer"
                                              />
                                            </td>
                                          );
                                        })}
                                      </tr>
                                    );
                                  })}
                              </Fragment>
                            );
                          })}
                        </tbody>
                      </table>
                    </div>

                    {groupedPrograms.length === 0 && (
                      <p className="text-sm text-slate-600 py-4 text-center">
                        {showUnassignedOnly
                          ? "All programs are assigned."
                          : "No programs to display."}
                      </p>
                    )}

                    {/* Save button — bottom-right */}
                    {programs.length > 0 && (
                      <div className="flex justify-end mt-3">
                        <button
                          onClick={handleSavePpa}
                          disabled={savingPpa}
                          className="px-4 py-2 bg-green-700 text-white text-sm font-medium hover:bg-green-600 transition-colors disabled:opacity-50 disabled:cursor-not-allowed flex items-center gap-2"
                        >
                          {savingPpa && (
                            <span className="w-4 h-4 border-2 border-white border-t-transparent rounded-full animate-spin" />
                          )}
                          Save
                        </button>
                      </div>
                    )}
                  </>
                )}
              </div>
            )}
          </>
        )}
      </div>
    </div>
  );
}

// ---------------------------------------------------------------------------
// Page export
// ---------------------------------------------------------------------------

export default function AllocationPage() {
  return <AllocationPageInner />;
}
