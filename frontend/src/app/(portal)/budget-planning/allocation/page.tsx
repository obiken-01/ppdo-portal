"use client";

/**
 * Allocation page — RAL-101.
 *
 * Finance-officer-only page for setting the PBO budget ceiling per office,
 * distributing it among divisions, and assigning AIP programs to divisions.
 *
 * Access: canManageAllocation. Hidden in sidebar for everyone else.
 * Route:  /budget-planning/allocation
 *
 * Amounts are in PESOS — no ×1000 conversion (that lives in WFP only).
 *
 * Tab 1 — Ceiling & Division Allocation:
 *   Set the PBO ceiling → distribute it among divisions → live stacked bar.
 *
 * Tab 2 — PPA → Division:
 *   Reuses the WFP Sector→Program hierarchy (collapsed at Program level).
 *   Each division gets a checkbox column. Multi-division toggle per program.
 *   Unassigned filter + bulk-assign + per-division assigned counts in headers.
 *
 * Endpoints (AllocationFunctions.cs, { data, error, message } envelope):
 *   GET/PUT /api/budget-planning/allocation/ceiling?officeId=&fiscalYear=
 *   GET/PUT /api/budget-planning/allocation/divisions?officeId=&fiscalYear=
 *   GET/PUT /api/budget-planning/allocation/programs?officeId=&fiscalYear=
 *   GET     /api/budget-planning/allocation/status?officeId=&fiscalYear=&divisionId=
 */

import { Fragment, useEffect, useMemo, useState } from "react";
import { useMe } from "@/lib/me-cache";
import { listOffices, listDivisions } from "@/lib/config";
import {
  allocationErrorMessage,
  getAllocations,
  getCeiling,
  getPrograms,
  upsertAllocations,
  upsertCeiling,
  upsertProgram,
} from "@/lib/allocation";
import MoneyInput from "@/components/ui/MoneyInput";
import { useToast } from "@/components/ui/Toast";
import { formatMoney } from "@/lib/money";
import type {
  BudgetCeilingDto,
  DivisionResponse,
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
          <span className="flex items-center gap-1.5 text-xs text-slate-400">
            <span className="inline-block w-2.5 h-2.5 shrink-0 bg-slate-200" />
            Unallocated: {(((ceiling - total) / ceiling) * 100).toFixed(1)}%
          </span>
        )}
      </div>
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
  const [ceiling, setCeiling] = useState<BudgetCeilingDto | null>(null);
  const [programs, setPrograms] = useState<ProgramAssignmentDto[]>([]);
  const [loading, setLoading] = useState(false);

  // ── Tab state ─────────────────────────────────────────────────────────────

  const [activeTab, setActiveTab] = useState<"ceiling" | "ppa">("ceiling");

  // ── Tab 1: Ceiling & Division Allocation ──────────────────────────────────

  const [ceilingInput, setCeilingInput] = useState<number | null>(null);
  const [allocationInputs, setAllocationInputs] = useState<Record<number, number | null>>({});
  const [savingCeiling, setSavingCeiling] = useState(false);
  const [savingAllocations, setSavingAllocations] = useState(false);

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

  const allocationTotal = useMemo(
    () => divisions.reduce((sum, d) => sum + (allocationInputs[d.id] ?? 0), 0),
    [allocationInputs, divisions]
  );
  const isOverCeiling =
    ceilingInput != null && ceilingInput > 0 && allocationTotal > ceilingInput + 0.001;
  const remaining = (ceilingInput ?? 0) - allocationTotal;

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

  // ── Load offices on mount ─────────────────────────────────────────────────

  useEffect(() => {
    listOffices({ active: "true" })
      .then(setOfficeList)
      .catch(() => toast.error("Load failed", "Could not load offices."));
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, []);

  // ── Pre-fill office for non-PPDO (office-locked) users ───────────────────

  useEffect(() => {
    if (!me || me.officeId == null) return;
    setSelectedOfficeId(me.officeId);
  }, [me]);

  // ── Load allocation data when office or FY changes ────────────────────────

  useEffect(() => {
    if (selectedOfficeId == null) {
      setDivisions([]);
      setCeiling(null);
      setCeilingInput(null);
      setAllocationInputs({});
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
        const [divs, ceil, allocs, progs] = await Promise.all([
          listDivisions({ active: "true", officeId: selectedOfficeId! }),
          getCeiling(selectedOfficeId!, selectedFiscalYear),
          getAllocations(selectedOfficeId!, selectedFiscalYear),
          getPrograms(selectedOfficeId!, selectedFiscalYear),
        ]);
        if (cancelled) return;

        setDivisions(divs);
        setCeiling(ceil);
        setCeilingInput(ceil?.amount ?? null);

        const inputs: Record<number, number | null> = {};
        for (const div of divs) {
          const saved = allocs.find((a) => a.divisionId === div.id);
          inputs[div.id] = saved?.amount ?? null;
        }
        setAllocationInputs(inputs);

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
      } catch {
        if (!cancelled) toast.error("Load failed", "Could not load allocation data.");
      } finally {
        if (!cancelled) setLoading(false);
      }
    }

    load();
    return () => { cancelled = true; };
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [selectedOfficeId, selectedFiscalYear]);

  // ── Tab 1: Ceiling ────────────────────────────────────────────────────────

  async function handleSaveCeiling() {
    if (selectedOfficeId == null || ceilingInput == null || ceilingInput <= 0) return;
    setSavingCeiling(true);
    try {
      const result = await upsertCeiling({
        officeId: selectedOfficeId,
        fiscalYear: selectedFiscalYear,
        amount: ceilingInput,
      });
      setCeiling(result);
      toast.success("Saved", "Budget ceiling updated.");
    } catch (err) {
      toast.error("Save failed", allocationErrorMessage(err, "Could not save ceiling."));
    } finally {
      setSavingCeiling(false);
    }
  }

  async function handleSaveAllocations() {
    if (selectedOfficeId == null || isOverCeiling) return;
    setSavingAllocations(true);
    try {
      const allocs = divisions.map((d) => ({
        divisionId: d.id,
        amount: allocationInputs[d.id] ?? 0,
      }));
      await upsertAllocations({
        officeId: selectedOfficeId,
        fiscalYear: selectedFiscalYear,
        allocations: allocs,
      });
      toast.success("Saved", "Division allocations saved.");
    } catch (err) {
      toast.error("Save failed", allocationErrorMessage(err, "Could not save allocations."));
    } finally {
      setSavingAllocations(false);
    }
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
              <select
                value={selectedOfficeId ?? ""}
                onChange={(e) =>
                  setSelectedOfficeId(e.target.value ? Number(e.target.value) : null)
                }
                className="border border-slate-300 bg-white text-sm px-2 py-1.5 text-slate-700 focus:outline-none focus:ring-1 focus:ring-green-600"
              >
                <option value="">— select office —</option>
                {officeList.map((o) => (
                  <option key={o.id} value={o.id}>
                    {o.officeCode} — {o.officeName}
                  </option>
                ))}
              </select>
            )}
          </div>
        </div>

        {/* Loading */}
        {loading && (
          <div className="flex items-center gap-2 text-slate-500 text-sm py-6">
            <span className="w-4 h-4 border-2 border-slate-300 border-t-green-600 rounded-full animate-spin" />
            Loading…
          </div>
        )}

        {/* Empty state */}
        {!loading && selectedOfficeId == null && (
          <p className="text-slate-400 text-sm py-6">
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
                      : "border-transparent text-slate-500 hover:text-slate-700"
                  }`}
                >
                  {tab === "ceiling" ? "Ceiling & Division Allocation" : "PPA → Division"}
                </button>
              ))}
            </div>

            {/* ── TAB 1: Ceiling & Division Allocation ─────────────────────── */}
            {activeTab === "ceiling" && (
              <div className="max-w-2xl space-y-6">

                {/* PBO Budget Ceiling */}
                <div className="border border-slate-200 p-4">
                  <h2 className="text-sm font-semibold text-slate-700 mb-3">
                    PBO Budget Ceiling
                    {selectedOffice && (
                      <span className="ml-2 font-normal text-slate-400">
                        — {selectedOffice.officeName} · FY{selectedFiscalYear}
                      </span>
                    )}
                  </h2>
                  <div className="flex items-center gap-3">
                    <MoneyInput
                      value={ceilingInput}
                      onChange={setCeilingInput}
                      className="w-52"
                    />
                    <button
                      onClick={handleSaveCeiling}
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
                    <p className="mt-2 text-xs text-slate-400">
                      Saved ceiling: ₱{formatMoney(ceiling.amount)}
                    </p>
                  )}
                </div>

                {/* Division Allocation */}
                <div className="border border-slate-200 p-4">
                  <h2 className="text-sm font-semibold text-slate-700 mb-3">
                    Division Allocation
                  </h2>

                  {divisions.length === 0 ? (
                    <p className="text-sm text-slate-400">
                      No divisions configured for this office. Add divisions in Config → Divisions.
                    </p>
                  ) : (
                    <>
                      {/* Live totals */}
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

                      {/* Stacked bar */}
                      {(ceilingInput ?? 0) > 0 && (
                        <AllocationBar
                          ceiling={ceilingInput ?? 0}
                          divisions={divisions}
                          allocationInputs={allocationInputs}
                        />
                      )}

                      {/* Division rows */}
                      <table className="w-full mt-4 text-sm border-collapse">
                        <thead>
                          <tr className="border-b border-slate-200">
                            <th className="text-left py-2 text-xs font-medium text-slate-500 uppercase tracking-wide">
                              Division
                            </th>
                            <th className="text-right py-2 text-xs font-medium text-slate-500 uppercase tracking-wide w-52">
                              Amount (₱)
                            </th>
                            <th className="text-right py-2 text-xs font-medium text-slate-500 uppercase tracking-wide w-20">
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
                                        backgroundColor:
                                          DIVISION_COLORS[i % DIVISION_COLORS.length],
                                      }}
                                    />
                                    {div.name}
                                    {div.code && (
                                      <span className="text-xs text-slate-400 font-mono">
                                        {div.code}
                                      </span>
                                    )}
                                  </span>
                                </td>
                                <td className="py-2 text-right">
                                  <div className="flex justify-end">
                                    <MoneyInput
                                      value={amount}
                                      onChange={(v) =>
                                        setAllocationInputs((prev) => ({
                                          ...prev,
                                          [div.id]: v,
                                        }))
                                      }
                                      className="w-48 text-sm"
                                    />
                                  </div>
                                </td>
                                <td className="py-2 text-right text-slate-500 tabular-nums">
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
                            <td className="py-2 text-right text-slate-500 tabular-nums">
                              {(ceilingInput ?? 0) > 0
                                ? `${((allocationTotal / ceilingInput!) * 100).toFixed(1)}%`
                                : "—"}
                            </td>
                          </tr>
                        </tfoot>
                      </table>

                      {/* Save Allocations */}
                      <div className="mt-4 flex items-center gap-3">
                        <button
                          onClick={handleSaveAllocations}
                          disabled={
                            savingAllocations || isOverCeiling || !ceiling
                          }
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
                          <span className="text-xs text-slate-400">
                            Set a ceiling first before saving allocations.
                          </span>
                        )}
                      </div>
                    </>
                  )}
                </div>
              </div>
            )}

            {/* ── TAB 2: PPA → Division ────────────────────────────────────── */}
            {activeTab === "ppa" && (
              <div>
                {divisions.length === 0 ? (
                  <p className="text-sm text-slate-400 py-4">
                    No divisions configured for this office. Add divisions in Config → Divisions.
                  </p>
                ) : programs.length === 0 ? (
                  <p className="text-sm text-slate-400 py-4">
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
                            className="text-xs text-slate-400 hover:text-slate-600"
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
                                  <span className="text-[10px] text-slate-400">
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
                                      className="mr-2 text-slate-500 hover:text-slate-700"
                                    >
                                      {isCollapsed ? "▶" : "▼"}
                                    </button>
                                    {sector}
                                    <span className="ml-2 font-normal text-slate-500">
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
                                        <td className="px-3 py-2 font-mono text-xs text-slate-500 whitespace-nowrap align-top">
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
                                                : "border-slate-200 text-slate-500 hover:bg-slate-50"
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
                      <p className="text-sm text-slate-400 py-4 text-center">
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
