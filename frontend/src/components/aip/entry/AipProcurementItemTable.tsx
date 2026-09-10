"use client";

/**
 * Procurement line items on an AIP expenditure (V18-80 / PPDO-54).
 *
 * Lifted from `components/wfp/WfpProcurementItemTable.tsx` — the ticket's instruction is parity,
 * because encoders already know that table and two procurement UIs behaving differently is its own
 * cost. What came across: the price-index picker (RAL-231), the line arithmetic, presets (RAL-119)
 * and the duplicate-item warning (RAL-153).
 *
 * ⚠️ **What deliberately did NOT come across, and must not be added back.** `periodNo`, `frequency`,
 * `annualQuarterChoice`, the reserve fields, the period tabs, the carry-forward actions
 * ("Apply items to all periods" / "Copy previous period") and the Q1–Q4 roll-up strip are *schedule*
 * concepts. An AIP activity carries **one annual figure**. Copying them would import a scheduling
 * model the AIP does not have, and it would reach the printed form.
 *
 * ⚠️ **`numberOfDays` is the one exception, and it is not an oversight.** While everything else
 * schedule-shaped was stripped, days stayed: PPDO employees asked for it, so it is a requirement in
 * its own right rather than a copied artefact (settled 2026-09-07).
 *
 * ⚠️ **The duplicate rule is re-derived, not transliterated.** RAL-153 scopes the WFP warning to the
 * *active period*, precisely because the same item recurring across periods is normal — a monthly
 * office-supplies purchase. With no periods that scoping is meaningless, so the AIP's scope is **the
 * activity's own expenditure lines**: `siblingPriceIndexItemIds` carries what the activity's *other*
 * lines already use, and repeats within this line are counted too.
 *
 * ⚠️ **The amount is derived once a line has items.** The server puts Σ line-total in the one column
 * the account's expense class names and discards whatever was typed — it does not add the two the
 * way WFP's `mergeWfpPeriodAndItemAmounts` does. The parent table shows the amount read-only.
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

const priceIndexItemLabel = (p: PriceIndexPickerItem) =>
  `${p.name} (${p.unit}) — ₱${formatMoney(p.unitPrice)}`;
const priceIndexItemSearchText = (p: PriceIndexPickerItem) => `${p.name} ${p.unit}`;

export interface AipProcurementItemTableProps {
  /** Drives presets (account-scoped) and, server-side, which column the total lands in. */
  accountId: number | null;
  items: SaveAipProcurementItemRequest[];
  onItemsChange: (items: SaveAipProcurementItemRequest[]) => void;
  priceIndex: PriceIndexPickerItem[];
  /**
   * True while the ~6,400-row catalogue is still loading (RAL-231). It is fetched off the page's
   * critical path, so the form can be opened before it lands — without this the picker would just
   * look like a catalogue with nothing in it.
   */
  priceIndexLoading: boolean;
  /**
   * Price-index item ids already used by the activity's OTHER expenditure lines. This is what makes
   * the duplicate warning activity-scoped rather than line-scoped — see this file's header.
   */
  siblingPriceIndexItemIds: number[];
}

export default function AipProcurementItemTable({
  accountId,
  items,
  onItemsChange,
  priceIndex,
  priceIndexLoading,
  siblingPriceIndexItemIds,
}: AipProcurementItemTableProps) {
  const { toast } = useToast();

  // Presets (account-scoped, loaded lazily — RAL-119's for-entry endpoint)
  const [presets, setPresets] = useState<ProcurementPresetResponse[]>([]);
  const [presetsLoaded, setPresetsLoaded] = useState<number | null>(null);
  const [loadPresetOpen, setLoadPresetOpen] = useState(false);
  const [savePresetOpen, setSavePresetOpen] = useState(false);
  const [presetName, setPresetName] = useState("");
  const [savingPreset, setSavingPreset] = useState(false);

  function addRow() {
    onItemsChange([
      ...items,
      { priceIndexItemId: null, name: "", unit: "", unitPrice: 0, qty: 1, numberOfDays: 1 },
    ]);
  }

  function removeRow(index: number) {
    onItemsChange(items.filter((_, i) => i !== index));
  }

  function updateRow(index: number, patch: Partial<SaveAipProcurementItemRequest>) {
    onItemsChange(items.map((r, i) => (i === index ? { ...r, ...patch } : r)));
  }

  function pickPriceIndexItem(index: number, priceIndexItemId: number | null) {
    if (priceIndexItemId == null) {
      updateRow(index, { priceIndexItemId: null });
      return;
    }
    const source = priceIndex.find((p) => p.id === priceIndexItemId);
    updateRow(index, {
      priceIndexItemId,
      name: source?.name ?? items[index]?.name ?? "",
      unit: source?.unit ?? items[index]?.unit ?? "",
      unitPrice: source?.unitPrice ?? items[index]?.unitPrice ?? 0,
      // Reset to 1 when the newly-picked item doesn't use the Days multiplier, clearing a leftover
      // value from a previously-picked days-enabled item in the same row (RAL-138).
      numberOfDays: source?.daysEnabled ? items[index]?.numberOfDays ?? 1 : 1,
    });
  }

  // Resolved live from the current Price Index rather than snapshotted onto the row (RAL-138) — the
  // flag gates a UI affordance only and never affects the arithmetic. Free-typed rows have no config
  // record to gate off, so they stay editable.
  function daysEnabledFor(row: SaveAipProcurementItemRequest): boolean {
    if (row.priceIndexItemId == null) return true;
    return priceIndex.find((p) => p.id === row.priceIndexItemId)?.daysEnabled ?? false;
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
    // "Load" copies an editable SNAPSHOT, not a live link — later preset edits never reach a saved
    // plan. Presets carry no day count (RAL-119 templates are name/unit/price/qty), and days are
    // event-specific, so a loaded row starts at 1.
    onItemsChange(
      preset.items.map((i) => ({
        priceIndexItemId: i.priceIndexItemId,
        name: i.name,
        unit: i.unit,
        unitPrice: i.unitPrice,
        qty: i.defaultQty,
        numberOfDays: 1,
      })),
    );
    setLoadPresetOpen(false);
    toast.success("Preset loaded", `${preset.name} copied into this line.`);
  }

  async function saveAsPreset() {
    if (accountId == null || items.length === 0) return;
    const name = presetName.trim();
    if (!name) return;

    setSavingPreset(true);
    try {
      await quickSaveProcurementPreset({
        accountId,
        name,
        isActive: true,
        items: items.map((r) => ({
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

  // ── Duplicates, scoped to the ACTIVITY (see the header) ─────────────────────
  //
  // A free-typed row has no priceIndexItemId and is never reported: two rows both typed by hand are
  // not known to be the same item, and guessing by name would flag "Bond paper" against "bond paper,
  // long" as a duplicate.
  const usageCounts = items.reduce<Record<number, number>>((counts, r) => {
    if (r.priceIndexItemId != null) counts[r.priceIndexItemId] = (counts[r.priceIndexItemId] ?? 0) + 1;
    return counts;
  }, {});
  const siblingIds = new Set(siblingPriceIndexItemIds);

  function duplicateNote(row: SaveAipProcurementItemRequest): string | null {
    if (row.priceIndexItemId == null) return null;
    if ((usageCounts[row.priceIndexItemId] ?? 0) > 1) return "This item is already on this line.";
    if (siblingIds.has(row.priceIndexItemId))
      return "This item is already on another expenditure line of this activity.";
    return null;
  }

  const total = items.reduce((sum, r) => sum + r.qty * r.unitPrice * r.numberOfDays, 0);

  return (
    <div className="space-y-3">
      {/* Toolbar — presets only. No carry-forward: there are no periods to carry between. */}
      <div className="flex flex-wrap items-center gap-3 text-xs">
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
          disabled={accountId == null || items.length === 0}
          className="font-medium text-slate-600 hover:underline disabled:opacity-40 disabled:cursor-not-allowed"
          title={accountId == null ? "Pick an account first" : undefined}
        >
          Save as preset
        </button>
      </div>

      <div className="space-y-2">
        {items.length === 0 && (
          <p className="text-xs text-slate-600 border border-dashed border-slate-300 px-3 py-4 text-center">
            No procurement items on this line yet. Add one to cost it from the Price Index, or leave
            it empty and type the amount instead.
          </p>
        )}

        {items.map((row, index) => {
          const lineTotal = row.qty * row.unitPrice * row.numberOfDays;
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
                  <div className="w-full px-2 py-1.5 text-sm text-right font-mono tabular-nums text-slate-600 bg-slate-50 border border-slate-200">
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

      {items.length > 0 && (
        <div className="flex items-center justify-end gap-2 pt-2 border-t border-slate-200 text-sm">
          <span className="text-slate-600">Items total</span>
          <span className="font-mono tabular-nums font-medium text-slate-800">₱{formatMoney(total)}</span>
        </div>
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
              Saves the {items.length} item{items.length === 1 ? "" : "s"} currently on this line.
            </p>
          </div>
        </Modal>
      )}
    </div>
  );
}
