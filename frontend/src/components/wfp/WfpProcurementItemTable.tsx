"use client";

/**
 * Procurement line-item table (v1.4 WFP Rework — RAL-125).
 *
 * Per-period item table for the "nature = Procurement" branch of the expenditure wizard
 * shipped by RAL-123 (`budget-planning/wfp/entry/page.tsx`) — Item name / Unit / Qty /
 * Unit price / Line total (computed) columns, one table per period, with the same period
 * grain (12/4/2/1) as RAL-124's frequency grid (§2).
 *
 * Item name + price come from the Price Index (RAL-118) via a typeahead search; picking a
 * result SNAPSHOTS name/unit/price into the row — the fields stay editable afterward, and a
 * later price-index edit never retroactively changes this row (the backend stores exactly
 * what the client sends; see WfpExpenditureService.SaveExpenditureAsync).
 *
 * Presets (RAL-119): "Load preset" copies a preset's items into the active period (still
 * editable, a snapshot not a live link); "Save as preset" posts the active period's current
 * rows via the quick-save endpoint (CanAccessBudgetPlanning, not CanManageConfig).
 *
 * Carry-forward is always an EXPLICIT action (§5.2, same rule as RAL-124's frequency grid) —
 * "Apply items to all periods" and "Copy previous period", never silent auto-fill.
 *
 * The live totals strip mirrors WfpExpenditureCalculator.Compute the same way RAL-124's grid
 * does, via computeWfpRollUpPreview + mergeWfpPeriodAndItemAmounts (frontend/src/lib/wfp.ts) —
 * see that file's docstring for why this client-side preview exists (no preview endpoint; the
 * server remains the sole source of truth for what's actually saved).
 */

import { useState } from "react";
import Lookup from "@/components/ui/Lookup";
import MoneyInput from "@/components/ui/MoneyInput";
import Modal from "@/components/ui/Modal";
import { useToast } from "@/components/ui/Toast";
import { formatMoney } from "@/lib/money";
import {
  configErrorMessage,
  listProcurementPresetsForEntry,
  quickSaveProcurementPreset,
} from "@/lib/config";
import { computeWfpRollUpPreview, mergeWfpPeriodAndItemAmounts, wfpPeriodCount } from "@/lib/wfp";
import type {
  PriceIndexItemResponse,
  ProcurementPresetResponse,
  SaveWfpProcurementItemRequest,
  WfpExpenditureFrequency,
} from "@/types";

// ---------------------------------------------------------------------------
// Period labels per frequency — duplicated (not imported) from WfpFrequencyGrid.tsx: that
// component is RAL-124's and out of scope for this ticket to touch.
// ---------------------------------------------------------------------------

const MONTH_LABELS = ["Jan", "Feb", "Mar", "Apr", "May", "Jun", "Jul", "Aug", "Sep", "Oct", "Nov", "Dec"];

function periodLabels(frequency: WfpExpenditureFrequency): string[] {
  switch (frequency) {
    case "M": return MONTH_LABELS;
    case "Q": return ["Q1", "Q2", "Q3", "Q4"];
    case "B": return ["1st Half", "2nd Half"];
    case "A": return ["Annual"];
  }
}

const priceIndexItemLabel = (p: PriceIndexItemResponse) => `${p.name} (${p.unit}) — ₱${formatMoney(p.unitPrice)}`;
const priceIndexItemSearchText = (p: PriceIndexItemResponse) => `${p.name} ${p.unit}`;

// ---------------------------------------------------------------------------
// Props
// ---------------------------------------------------------------------------

export interface WfpProcurementItemTableProps {
  frequency: WfpExpenditureFrequency;
  accountId: number | null;
  procurementItems: SaveWfpProcurementItemRequest[];
  onProcurementItemsChange: (items: SaveWfpProcurementItemRequest[]) => void;
  priceIndex: PriceIndexItemResponse[];
  annualQuarterChoice: number;
  onAnnualQuarterChoiceChange: (choice: number) => void;
  applyReserve: boolean;
  reserveAmount: number | null;
  reserveRate: number;
  /** Price-index item ids already used in one of this activity's OTHER expenditures (RAL-152) —
   *  a picked row whose id is in this set shows a non-blocking heads-up, never disables Save. */
  duplicatePriceIndexItemIds: Set<number>;
}

// ---------------------------------------------------------------------------
// Component
// ---------------------------------------------------------------------------

export default function WfpProcurementItemTable({
  frequency,
  accountId,
  procurementItems,
  onProcurementItemsChange,
  priceIndex,
  annualQuarterChoice,
  onAnnualQuarterChoiceChange,
  applyReserve,
  reserveAmount,
  reserveRate,
  duplicatePriceIndexItemIds,
}: WfpProcurementItemTableProps) {
  const { toast } = useToast();
  const count = wfpPeriodCount(frequency);
  const labels = periodLabels(frequency);

  const [activePeriod, setActivePeriod] = useState(1);

  // Presets (account-scoped, loaded lazily — RAL-119's for-entry endpoint)
  const [presets, setPresets] = useState<ProcurementPresetResponse[]>([]);
  const [presetsLoaded, setPresetsLoaded] = useState<number | null>(null); // accountId presets were loaded for
  const [loadPresetOpen, setLoadPresetOpen] = useState(false);
  const [savePresetOpen, setSavePresetOpen] = useState(false);
  const [presetName, setPresetName] = useState("");
  const [savingPreset, setSavingPreset] = useState(false);

  const rowsInPeriod = (periodNo: number) => procurementItems.filter((i) => i.periodNo === periodNo);
  const activeRows = rowsInPeriod(activePeriod);

  function replaceActiveRows(rows: SaveWfpProcurementItemRequest[]) {
    const others = procurementItems.filter((i) => i.periodNo !== activePeriod);
    onProcurementItemsChange([...others, ...rows]);
  }

  function addRow() {
    replaceActiveRows([
      ...activeRows,
      { periodNo: activePeriod, priceIndexItemId: null, name: "", unit: "", unitPrice: 0, qty: 1, numberOfDays: 1 },
    ]);
  }

  function removeRow(index: number) {
    replaceActiveRows(activeRows.filter((_, i) => i !== index));
  }

  function updateRow(index: number, patch: Partial<SaveWfpProcurementItemRequest>) {
    replaceActiveRows(activeRows.map((r, i) => (i === index ? { ...r, ...patch } : r)));
  }

  function pickPriceIndexItem(index: number, priceIndexItemId: number | null) {
    if (priceIndexItemId == null) {
      updateRow(index, { priceIndexItemId: null });
      return;
    }
    const source = priceIndex.find((p) => p.id === priceIndexItemId);
    updateRow(index, {
      priceIndexItemId,
      name: source?.name ?? activeRows[index]?.name ?? "",
      unit: source?.unit ?? activeRows[index]?.unit ?? "",
      unitPrice: source?.unitPrice ?? activeRows[index]?.unitPrice ?? 0,
      // Reset to 1 when the newly-picked item doesn't use the Days multiplier — clears any
      // leftover value from a previously-picked days-enabled item in the same row (RAL-138).
      numberOfDays: source?.daysEnabled ? activeRows[index]?.numberOfDays ?? 1 : 1,
    });
  }

  // Resolved live from the current Price Index list rather than snapshotted onto the row
  // (RAL-138) — the flag only gates a UI affordance, never affects LineTotal math, so there's
  // no correctness reason to persist it. Free-typed rows (no price-index link) have no config
  // record to gate off, so they keep the pre-RAL-138 always-editable behavior.
  function daysEnabledFor(row: SaveWfpProcurementItemRequest): boolean {
    if (row.priceIndexItemId == null) return true;
    return priceIndex.find((p) => p.id === row.priceIndexItemId)?.daysEnabled ?? false;
  }

  // ── Explicit carry-forward (§5.2, same rule as RAL-124's frequency grid) ────────────────

  function applyToAllPeriods() {
    if (activeRows.length === 0) return;
    const cloned: SaveWfpProcurementItemRequest[] = [];
    for (let periodNo = 1; periodNo <= count; periodNo++) {
      for (const row of activeRows) cloned.push({ ...row, periodNo });
    }
    onProcurementItemsChange(cloned);
  }

  function copyPreviousPeriod() {
    if (activePeriod <= 1) return;
    const previous = rowsInPeriod(activePeriod - 1);
    if (previous.length === 0) return;
    replaceActiveRows(previous.map((r) => ({ ...r, periodNo: activePeriod })));
  }

  // ── Presets ───────────────────────────────────────────────────────────────────

  async function openLoadPreset() {
    if (accountId == null) return;
    setLoadPresetOpen(true);
    if (presetsLoaded !== accountId) {
      try {
        const data = await listProcurementPresetsForEntry(accountId, "true");
        setPresets(data);
        setPresetsLoaded(accountId);
      } catch (err) {
        toast.error("Failed to load presets", configErrorMessage(err, "Please try again."));
      }
    }
  }

  function loadPreset(preset: ProcurementPresetResponse) {
    // Presets don't carry a day count (RAL-119 templates are name/unit/price/qty only) —
    // days is event-specific, entered per use, so a loaded row starts at 1 day (RAL-127).
    replaceActiveRows(
      preset.items.map((i) => ({
        periodNo: activePeriod,
        priceIndexItemId: i.priceIndexItemId,
        name: i.name,
        unit: i.unit,
        unitPrice: i.unitPrice,
        qty: i.defaultQty,
        numberOfDays: 1,
      })),
    );
    setLoadPresetOpen(false);
    toast.success("Preset loaded", `${preset.name} copied into ${labels[activePeriod - 1]}.`);
  }

  async function saveAsPreset() {
    if (accountId == null || activeRows.length === 0) return;
    const name = presetName.trim();
    if (!name) return;

    setSavingPreset(true);
    try {
      await quickSaveProcurementPreset({
        accountId,
        name,
        isActive: true,
        items: activeRows.map((r) => ({
          priceIndexItemId: r.priceIndexItemId,
          name: r.priceIndexItemId == null ? r.name : null,
          unit: r.priceIndexItemId == null ? r.unit : null,
          unitPrice: r.priceIndexItemId == null ? r.unitPrice : null,
          defaultQty: r.qty,
        })),
      });
      toast.success("Preset saved", `${name} is now available under "Load preset".`);
      setSavePresetOpen(false);
      setPresetName("");
      setPresetsLoaded(null); // force a refresh next time "Load preset" opens
    } catch (err) {
      toast.error("Save failed", configErrorMessage(err, "Could not save this preset."));
    } finally {
      setSavingPreset(false);
    }
  }

  // ── Totals ────────────────────────────────────────────────────────────────────

  const activeTotal = activeRows.reduce((sum, r) => sum + r.qty * r.unitPrice * r.numberOfDays, 0);
  const merged = mergeWfpPeriodAndItemAmounts([], procurementItems);
  const preview = computeWfpRollUpPreview(frequency, merged, 0, annualQuarterChoice);
  const resolvedReserve = applyReserve ? reserveAmount ?? preview.net * reserveRate : 0;
  const total = preview.net + resolvedReserve;

  return (
    <div className="space-y-3">
      {/* Annual "charge to" selector — same as the frequency grid, frequency is nature-independent */}
      {frequency === "A" && (
        <div>
          <label className="block text-xs font-medium text-slate-600 uppercase tracking-wide mb-1">
            Charge to
          </label>
          <div className="flex items-center border border-slate-200 overflow-hidden w-fit">
            {[1, 2, 3, 4].map((q) => (
              <button
                key={q}
                type="button"
                onClick={() => onAnnualQuarterChoiceChange(q)}
                className={`px-3 py-1.5 text-sm font-medium transition-colors ${
                  annualQuarterChoice === q ? "bg-green-600 text-white" : "bg-white text-slate-600 hover:bg-slate-50"
                }`}
              >
                Q{q}
              </button>
            ))}
          </div>
        </div>
      )}

      {/* Period tabs */}
      {count > 1 && (
        <div className="flex flex-wrap items-center border border-slate-200 overflow-hidden w-fit">
          {labels.map((label, i) => {
            const periodNo = i + 1;
            const hasItems = rowsInPeriod(periodNo).length > 0;
            return (
              <button
                key={periodNo}
                type="button"
                onClick={() => setActivePeriod(periodNo)}
                className={`px-3 py-1.5 text-sm font-medium transition-colors flex items-center gap-1 ${
                  activePeriod === periodNo ? "bg-green-600 text-white" : "bg-white text-slate-600 hover:bg-slate-50"
                }`}
              >
                {label}
                {hasItems && (
                  <span className={`w-1.5 h-1.5 rounded-full ${activePeriod === periodNo ? "bg-white" : "bg-green-600"}`} />
                )}
              </button>
            );
          })}
        </div>
      )}

      {/* Toolbar: carry-forward + presets */}
      <div className="flex flex-wrap items-center gap-3 text-xs">
        {activeRows.length > 0 && count > 1 && (
          <button type="button" onClick={applyToAllPeriods} className="font-medium text-green-700 hover:underline">
            Apply items to all periods
          </button>
        )}
        {activePeriod > 1 && rowsInPeriod(activePeriod - 1).length > 0 && (
          <button type="button" onClick={copyPreviousPeriod} className="font-medium text-green-700 hover:underline">
            Copy previous period
          </button>
        )}
        <span className="flex-1" />
        <button
          type="button"
          onClick={openLoadPreset}
          disabled={accountId == null}
          className="font-medium text-slate-600 hover:underline disabled:opacity-40 disabled:cursor-not-allowed"
          title={accountId == null ? "Pick an account first" : undefined}
        >
          Load preset
        </button>
        <button
          type="button"
          onClick={() => setSavePresetOpen(true)}
          disabled={accountId == null || activeRows.length === 0}
          className="font-medium text-slate-600 hover:underline disabled:opacity-40 disabled:cursor-not-allowed"
          title={accountId == null ? "Pick an account first" : undefined}
        >
          Save as preset
        </button>
      </div>

      {/* Item rows for the active period */}
      <div className="space-y-2">
        {activeRows.length === 0 && (
          <p className="text-xs text-slate-600 border border-dashed border-slate-300 px-3 py-4 text-center">
            No items for {labels[activePeriod - 1]} yet.
          </p>
        )}
        {activeRows.map((row, index) => {
          const lineTotal = row.qty * row.unitPrice * row.numberOfDays;
          const daysEnabled = daysEnabledFor(row);
          return (
            <div key={index} className="border border-slate-200 p-3 space-y-2">
              <div className="flex items-center justify-between gap-2">
                <Lookup
                  items={priceIndex}
                  value={row.priceIndexItemId}
                  onChange={(id) => pickPriceIndexItem(index, id)}
                  getId={(p) => p.id}
                  getLabel={priceIndexItemLabel}
                  getSearchText={priceIndexItemSearchText}
                  allOptionLabel="Free-typed item (no price index link)"
                  placeholder="Search price index by item name…"
                  className="flex-1 min-w-0"
                />
                <button
                  type="button"
                  onClick={() => removeRow(index)}
                  className="text-danger-500 hover:text-red-600 text-sm shrink-0"
                >
                  Remove
                </button>
              </div>

              {row.priceIndexItemId != null && duplicatePriceIndexItemIds.has(row.priceIndexItemId) && (
                <p className="text-[11px] text-amber-600">
                  ⚠ Already used in another expenditure for this activity.
                </p>
              )}

              {/* Name (full width) + Unit */}
              <div className="flex items-end gap-2">
                <div className="flex-1 min-w-0">
                  <label className="block text-[11px] text-slate-600 mb-0.5">Name</label>
                  <input
                    value={row.name}
                    onChange={(e) => updateRow(index, { name: e.target.value })}
                    placeholder="Item name"
                    className="w-full px-2 py-1.5 text-sm border border-slate-200 focus:outline-none focus:ring-2 focus:ring-green-600"
                  />
                </div>
                <div className="w-32 shrink-0">
                  <label className="block text-[11px] text-slate-600 mb-0.5">Unit</label>
                  <input
                    value={row.unit}
                    onChange={(e) => updateRow(index, { unit: e.target.value })}
                    placeholder="ream"
                    className="w-full px-2 py-1.5 text-sm border border-slate-200 focus:outline-none focus:ring-2 focus:ring-green-600"
                  />
                </div>
              </div>

              {/* Unit Price · Qty · Days · Line Total */}
              <div className="flex items-end gap-2">
                <div className="w-36 shrink-0">
                  <label className="block text-[11px] text-slate-600 mb-0.5">Unit Price</label>
                  <MoneyInput
                    value={row.unitPrice}
                    onChange={(v) => updateRow(index, { unitPrice: v ?? 0 })}
                    className="w-full"
                  />
                </div>
                <div className="w-20 shrink-0">
                  <label className="block text-[11px] text-slate-600 mb-0.5">Qty</label>
                  <input
                    type="number"
                    min={0}
                    step="1"
                    value={row.qty}
                    onChange={(e) => updateRow(index, { qty: Number(e.target.value) || 0 })}
                    className="w-full px-2 py-1.5 text-sm border border-slate-200 focus:outline-none focus:ring-2 focus:ring-green-600"
                  />
                </div>
                <div className="w-20 shrink-0">
                  <label className="block text-[11px] text-slate-600 mb-0.5">Days</label>
                  <input
                    type="number"
                    min={1}
                    step="1"
                    value={daysEnabled ? row.numberOfDays : 1}
                    disabled={!daysEnabled}
                    title={daysEnabled ? undefined : "Enable “Days” for this item in Price Index config"}
                    onChange={(e) => updateRow(index, { numberOfDays: Number(e.target.value) || 1 })}
                    className="w-full px-2 py-1.5 text-sm border border-slate-200 focus:outline-none focus:ring-2 focus:ring-green-600 disabled:bg-slate-50 disabled:text-slate-400"
                  />
                </div>
                <div className="flex-1 min-w-0">
                  <label className="block text-[11px] text-slate-600 mb-0.5">Line Total</label>
                  <div className="w-full px-2 py-1.5 text-sm text-right font-mono tabular-nums text-slate-700 bg-slate-50 border border-slate-200">
                    ₱{formatMoney(lineTotal)}
                  </div>
                </div>
              </div>
            </div>
          );
        })}
        <button type="button" onClick={addRow} className="text-xs font-medium text-green-600 hover:underline">
          + Add item
        </button>
      </div>

      {/* Active period total */}
      <div className="flex items-center justify-end gap-2 pt-2 border-t border-slate-200 text-sm">
        <span className="text-slate-600">{labels[activePeriod - 1]} total</span>
        <span className="font-mono tabular-nums font-medium text-slate-800">₱{formatMoney(activeTotal)}</span>
      </div>

      {/* Live totals strip — across ALL periods, mirrors the frequency grid's strip */}
      <div className="grid grid-cols-4 gap-2 pt-2 border-t border-slate-200 text-center">
        {(["q1", "q2", "q3", "q4"] as const).map((q, i) => (
          <div key={q}>
            <div className="text-[10px] text-slate-600 uppercase tracking-wide">Q{i + 1}</div>
            <div className="text-sm font-mono tabular-nums text-slate-700">₱{formatMoney(preview[q])}</div>
          </div>
        ))}
      </div>
      <div className="flex items-center justify-end gap-4 text-sm">
        <span className="text-slate-600">
          Net <span className="font-mono tabular-nums text-slate-700">₱{formatMoney(preview.net)}</span>
        </span>
        {applyReserve && (
          <span className="text-slate-600">
            Reserved <span className="font-mono tabular-nums text-slate-700">₱{formatMoney(resolvedReserve)}</span>
          </span>
        )}
        <span className="font-semibold text-slate-800">
          Total <span className="font-mono tabular-nums">₱{formatMoney(total)}</span>
        </span>
      </div>

      {/* ── Load preset modal ─────────────────────────────────────────────────── */}
      {loadPresetOpen && (
        <Modal title="Load Preset" size="md" onClose={() => setLoadPresetOpen(false)}>
          {presets.length === 0 ? (
            <p className="text-sm text-slate-600 text-center py-6">
              No presets for this account yet. Build the item list below, then use
              &quot;Save as preset&quot; to create one.
            </p>
          ) : (
            <ul className="divide-y divide-slate-100 -mx-1">
              {presets.map((p) => (
                <li key={p.id}>
                  <button
                    type="button"
                    onClick={() => loadPreset(p)}
                    className="w-full text-left px-1 py-2.5 hover:bg-green-50 transition-colors"
                  >
                    <span className="font-medium text-slate-800">{p.name}</span>
                    <span className="text-xs text-slate-600 ml-2">
                      {p.items.length} item{p.items.length === 1 ? "" : "s"}
                    </span>
                  </button>
                </li>
              ))}
            </ul>
          )}
        </Modal>
      )}

      {/* ── Save as preset modal ──────────────────────────────────────────────── */}
      {savePresetOpen && (
        <Modal
          title="Save as Preset"
          size="sm"
          onClose={() => !savingPreset && setSavePresetOpen(false)}
          footer={
            <>
              <Modal.SecondaryButton onClick={() => setSavePresetOpen(false)} disabled={savingPreset}>
                Cancel
              </Modal.SecondaryButton>
              <Modal.PrimaryButton onClick={saveAsPreset} disabled={!presetName.trim()} loading={savingPreset}>
                Save Preset
              </Modal.PrimaryButton>
            </>
          }
        >
          <div className="space-y-2">
            <label className="block text-xs font-medium text-slate-600">Preset Name</label>
            <input
              autoFocus
              value={presetName}
              onChange={(e) => setPresetName(e.target.value)}
              placeholder="Standard Office Supplies Kit"
              className="w-full px-3 py-1.5 text-sm border border-slate-200 focus:outline-none focus:ring-2 focus:ring-green-600"
            />
            <p className="text-[11px] text-slate-600">
              Saves the {activeRows.length} item{activeRows.length === 1 ? "" : "s"} currently in{" "}
              {labels[activePeriod - 1]}.
            </p>
          </div>
        </Modal>
      )}
    </div>
  );
}
