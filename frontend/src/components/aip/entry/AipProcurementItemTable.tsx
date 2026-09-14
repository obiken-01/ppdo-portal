"use client";

/**
 * Procurement line items on an AIP expenditure (V18-80 / PPDO-54).
 *
 * Lifted from `components/wfp/WfpProcurementItemTable.tsx` — the ticket's instruction is parity,
 * because encoders already know that table and two procurement UIs behaving differently is its own
 * cost. What came across: the price-index picker (RAL-231), the line arithmetic, presets (RAL-119),
 * the duplicate-item warning (RAL-153) and — ↩️ since 2026-09-14 — the **quarter tabs** with their
 * carry-forward actions and Q1–Q4 strip.
 *
 * ↩️ **Quarters were deliberately left out at first and added 2026-09-14** (Ralph, after a full-cycle
 * test). They are **input only**: the server still sums every item across all four quarters into the
 * line's one column, so the printed form, the ceiling and the consolidated grid see the same single
 * annual figure. Nothing downstream of the line may read a quarter.
 *
 * ⚠️ **What still did NOT come across, and must not be added back.** `frequency`,
 * `annualQuarterChoice` and the reserve fields are the rest of WFP's *schedule* model. An AIP line is
 * always four quarters — never monthly, never a single annual period — and carries no reserve.
 *
 * ⚠️ **`numberOfDays` stays**, as before: PPDO employees asked for it (settled 2026-09-07).
 *
 * ⚠️ **The duplicate rule is scoped to the ACTIVE QUARTER** — this line's rows in it, plus the
 * activity's other lines in the same quarter. The same item recurring in another quarter is normal
 * (a quarterly supplies purchase), which is exactly why RAL-153 scopes WFP's warning to the period.
 *
 * ⚠️ **The amount is derived once a line has items.** The server puts Σ line-total in the one column
 * the account's expense class names and discards whatever was typed. The parent table shows the amount
 * read-only.
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
import type {
  PriceIndexPickerItem,
  ProcurementPresetResponse,
  SaveAipProcurementItemRequest,
} from "@/types";

const QUARTERS = [1, 2, 3, 4] as const;
const quarterLabel = (q: number) => `Q${q}`;

const priceIndexItemLabel = (p: PriceIndexPickerItem) =>
  `${p.name} (${p.unit}) — ₱${formatMoney(p.unitPrice)}`;
const priceIndexItemSearchText = (p: PriceIndexPickerItem) => `${p.name} ${p.unit}`;

const lineTotalOf = (r: SaveAipProcurementItemRequest) => r.qty * r.unitPrice * r.numberOfDays;

/** A price-index item already on one of the activity's OTHER lines, and the quarter it sits in. */
export interface AipSiblingItem {
  priceIndexItemId: number;
  periodNo: number;
}

export interface AipProcurementItemTableProps {
  /** Drives presets (account-scoped) and, server-side, which column the total lands in. */
  accountId: number | null;
  /** Every quarter's items — this table shows one quarter at a time. */
  items: SaveAipProcurementItemRequest[];
  onItemsChange: (items: SaveAipProcurementItemRequest[]) => void;
  priceIndex: PriceIndexPickerItem[];
  /**
   * True while the ~6,400-row catalogue is still loading (RAL-231). It is fetched off the page's
   * critical path, so the form can be opened before it lands — without this the picker would just
   * look like a catalogue with nothing in it.
   */
  priceIndexLoading: boolean;
  /** What the activity's other lines already use, by quarter — see the duplicate rule in the header. */
  siblingItems: AipSiblingItem[];
}

export default function AipProcurementItemTable({
  accountId,
  items,
  onItemsChange,
  priceIndex,
  priceIndexLoading,
  siblingItems,
}: AipProcurementItemTableProps) {
  const { toast } = useToast();

  // Opens on the first quarter that has items, so editing a Q3-only line does not land on an empty Q1.
  const [activeQuarter, setActiveQuarter] = useState<number>(() =>
    items.length > 0 ? Math.min(...items.map((i) => i.periodNo)) : 1
  );

  // Presets (account-scoped, loaded lazily — RAL-119's for-entry endpoint)
  const [presets, setPresets] = useState<ProcurementPresetResponse[]>([]);
  const [presetsLoaded, setPresetsLoaded] = useState<number | null>(null);
  const [loadPresetOpen, setLoadPresetOpen] = useState(false);
  const [savePresetOpen, setSavePresetOpen] = useState(false);
  const [presetName, setPresetName] = useState("");
  const [savingPreset, setSavingPreset] = useState(false);

  const rowsIn = (q: number) => items.filter((i) => i.periodNo === q);
  const activeRows = rowsIn(activeQuarter);

  /** Swaps the active quarter's rows, leaving the other quarters exactly as they were. */
  function replaceActiveRows(rows: SaveAipProcurementItemRequest[]) {
    const others = items.filter((i) => i.periodNo !== activeQuarter);
    // Kept in quarter order so a saved line's items read Q1 → Q4, as the server returns them.
    onItemsChange([...others, ...rows].sort((a, b) => a.periodNo - b.periodNo));
  }

  function addRow() {
    replaceActiveRows([
      ...activeRows,
      { periodNo: activeQuarter, priceIndexItemId: null, name: "", unit: "", unitPrice: 0, qty: 1, numberOfDays: 1 },
    ]);
  }

  function removeRow(index: number) {
    replaceActiveRows(activeRows.filter((_, i) => i !== index));
  }

  function updateRow(index: number, patch: Partial<SaveAipProcurementItemRequest>) {
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
      // Reset to 1 when the newly-picked item doesn't use the Days multiplier, clearing a leftover
      // value from a previously-picked days-enabled item in the same row (RAL-138).
      numberOfDays: source?.daysEnabled ? activeRows[index]?.numberOfDays ?? 1 : 1,
    });
  }

  // Resolved live from the current Price Index rather than snapshotted onto the row (RAL-138) — the
  // flag gates a UI affordance only and never affects the arithmetic. Free-typed rows have no config
  // record to gate off, so they stay editable.
  function daysEnabledFor(row: SaveAipProcurementItemRequest): boolean {
    if (row.priceIndexItemId == null) return true;
    return priceIndex.find((p) => p.id === row.priceIndexItemId)?.daysEnabled ?? false;
  }

  // ── Carry-forward — always an explicit action, never a silent fill (WFP §5.2) ─

  /** Copies the active quarter's rows into every quarter, replacing what the others held. */
  function applyToAllQuarters() {
    onItemsChange(QUARTERS.flatMap((q) => activeRows.map((r) => ({ ...r, periodNo: q }))));
    toast.success("Items applied", `${quarterLabel(activeQuarter)}'s items now fill all four quarters.`);
  }

  function copyPreviousQuarter() {
    if (activeQuarter <= 1) return;
    const previous = rowsIn(activeQuarter - 1);
    if (previous.length === 0) return;
    replaceActiveRows(previous.map((r) => ({ ...r, periodNo: activeQuarter })));
  }

  // ── Presets ─────────────────────────────────────────────────────────────────

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
    // "Load" copies an editable SNAPSHOT into the active quarter, not a live link — later preset
    // edits never reach a saved plan. Presets carry no day count, so a loaded row starts at 1.
    replaceActiveRows(
      preset.items.map((i) => ({
        periodNo: activeQuarter,
        priceIndexItemId: i.priceIndexItemId,
        name: i.name,
        unit: i.unit,
        unitPrice: i.unitPrice,
        qty: i.defaultQty,
        numberOfDays: 1,
      })),
    );
    setLoadPresetOpen(false);
    toast.success("Preset loaded", `${preset.name} copied into ${quarterLabel(activeQuarter)}.`);
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
      setPresetsLoaded(null);
    } catch (err) {
      toast.error("Save failed", configErrorMessage(err, "Could not save this preset."));
    } finally {
      setSavingPreset(false);
    }
  }

  // ── Duplicates, scoped to the ACTIVE QUARTER (see the header) ───────────────
  //
  // A free-typed row has no priceIndexItemId and is never reported: two rows both typed by hand are
  // not known to be the same item, and guessing by name would flag "Bond paper" against "bond paper,
  // long" as a duplicate.
  const usageCounts = activeRows.reduce<Record<number, number>>((counts, r) => {
    if (r.priceIndexItemId != null) counts[r.priceIndexItemId] = (counts[r.priceIndexItemId] ?? 0) + 1;
    return counts;
  }, {});
  const siblingIdsThisQuarter = new Set(
    siblingItems.filter((s) => s.periodNo === activeQuarter).map((s) => s.priceIndexItemId)
  );

  function duplicateNote(row: SaveAipProcurementItemRequest): string | null {
    if (row.priceIndexItemId == null) return null;
    if ((usageCounts[row.priceIndexItemId] ?? 0) > 1)
      return `This item is already on this line in ${quarterLabel(activeQuarter)}.`;
    if (siblingIdsThisQuarter.has(row.priceIndexItemId))
      return `This item is already on another expenditure line of this activity in ${quarterLabel(activeQuarter)}.`;
    return null;
  }

  // ── Totals ──────────────────────────────────────────────────────────────────
  const quarterTotal = (q: number) => rowsIn(q).reduce((sum, r) => sum + lineTotalOf(r), 0);
  const annualTotal = items.reduce((sum, r) => sum + lineTotalOf(r), 0);

  return (
    <div className="space-y-3">
      {/* Quarter tabs — a dot marks a quarter that holds items. */}
      <div className="inline-flex border border-slate-200" role="tablist" aria-label="Quarter">
        {QUARTERS.map((q) => {
          const active = q === activeQuarter;
          return (
            <button
              key={q}
              type="button"
              role="tab"
              aria-selected={active}
              onClick={() => setActiveQuarter(q)}
              className={`flex items-center gap-1 px-3 py-1.5 text-sm font-medium transition-colors ${
                active ? "bg-green-700 text-white" : "bg-white text-slate-600 hover:bg-slate-50"
              }`}
            >
              {quarterLabel(q)}
              {rowsIn(q).length > 0 && (
                <span className={`h-1.5 w-1.5 rounded-full ${active ? "bg-white" : "bg-green-700"}`} />
              )}
            </button>
          );
        })}
      </div>

      {/* Toolbar: carry-forward + presets */}
      <div className="flex flex-wrap items-center gap-3 text-xs">
        {activeRows.length > 0 && (
          <button
            type="button"
            onClick={applyToAllQuarters}
            title="Replaces the items in the other three quarters"
            className="font-medium text-green-700 hover:underline"
          >
            Apply items to all quarters
          </button>
        )}
        {activeQuarter > 1 && rowsIn(activeQuarter - 1).length > 0 && (
          <button type="button" onClick={copyPreviousQuarter} className="font-medium text-green-700 hover:underline">
            Copy previous quarter
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

      <div className="space-y-2">
        {activeRows.length === 0 && (
          <p className="text-xs text-slate-600 border border-dashed border-slate-300 px-3 py-4 text-center">
            {items.length === 0
              ? `No procurement items on this line yet. Add one to cost ${quarterLabel(activeQuarter)} from the Price Index, or leave the line empty and type the amount instead.`
              : `No items in ${quarterLabel(activeQuarter)} yet.`}
          </p>
        )}

        {activeRows.map((row, index) => {
          const lineTotal = lineTotalOf(row);
          const daysEnabled = daysEnabledFor(row);
          const duplicate = duplicateNote(row);

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
                  placeholder={
                    priceIndexLoading
                      ? "Loading price index catalogue…"
                      : "Search price index by item name…"
                  }
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

              {duplicate && <p className="text-[11px] text-amber-600">⚠ {duplicate}</p>}

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

              <div className="flex items-end gap-2">
                <div className="w-36 shrink-0">
                  <label className="block text-[11px] text-slate-600 mb-0.5">Unit Price</label>
                  <MoneyInput
                    value={row.unitPrice}
                    onChange={(v) => updateRow(index, { unitPrice: v ?? 0 })}
                    // Match the Qty / Days / Line Total boxes beside it — MoneyInput's own padding
                    // is the compact grid size, a row shorter than these.
                    className="w-full text-sm [&_input]:py-1.5"
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
                  <div className="w-full px-2 py-1.5 text-sm text-right font-mono tabular-nums text-slate-600 bg-slate-50 border border-slate-200">
                    ₱{formatMoney(lineTotal)}
                  </div>
                </div>
              </div>
            </div>
          );
        })}

        <button type="button" onClick={addRow} className="text-xs font-medium text-green-700 hover:underline">
          + Add item
        </button>
      </div>

      {items.length > 0 && (
        <>
          <div className="flex items-center justify-end gap-2 pt-2 border-t border-slate-200 text-sm">
            <span className="text-slate-600">{quarterLabel(activeQuarter)} total</span>
            <span className="font-mono tabular-nums font-medium text-slate-800">
              ₱{formatMoney(quarterTotal(activeQuarter))}
            </span>
          </div>

          {/* Every quarter at a glance, and the one figure the line actually carries. */}
          <div className="grid grid-cols-4 gap-2 pt-2 border-t border-slate-200 text-center">
            {QUARTERS.map((q) => (
              <div key={q}>
                <p className="text-[11px] text-slate-600">{quarterLabel(q)}</p>
                <p className="font-mono text-sm tabular-nums text-slate-800">₱{formatMoney(quarterTotal(q))}</p>
              </div>
            ))}
          </div>
          <div className="flex flex-wrap items-center justify-between gap-2 text-sm">
            {/* ⚠️ Says out loud that the quarters are not what prints — see the header. */}
            <span className="text-[11px] text-slate-600">
              Quarters are for planning. The line&apos;s amount is the total of all four.
            </span>
            <span>
              <span className="text-slate-600">Items total </span>
              <span className="font-mono tabular-nums font-semibold text-slate-800">₱{formatMoney(annualTotal)}</span>
            </span>
          </div>
        </>
      )}

      {/* ── Load preset modal ───────────────────────────────────────────────── */}
      {loadPresetOpen && (
        <Modal title="Load Preset" size="md" onClose={() => setLoadPresetOpen(false)}>
          {presets.length === 0 ? (
            <p className="text-sm text-slate-600 text-center py-6">
              No presets for this account yet. Build the item list first, then use &quot;Save as
              preset&quot; to create one.
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

      {/* ── Save as preset modal ────────────────────────────────────────────── */}
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
              {quarterLabel(activeQuarter)}.
            </p>
          </div>
        </Modal>
      )}
    </div>
  );
}
