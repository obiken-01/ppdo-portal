"use client";

/**
 * Procurement Presets Configuration page — v1.4 RAL-119.
 *
 * Account-scoped, reusable procurement line-item templates. Shared across all
 * offices/divisions (§7.2) — createdByName is shown for traceability only, never
 * used to scope visibility. Loading a preset into a WFP entry always copies its
 * items (snapshot, editable) — presets are templates, not live links to the price
 * index (ticket #11 / RAL-125 builds that loading UI; this page is schema + API +
 * standalone config CRUD only).
 *
 * Unlike the other config pages (flat DataTable), this is a MASTER-DETAIL layout:
 * pick an account (or leave it on "All Accounts", the default), see the preset
 * list, expand a preset to see its item rows. No CSV import/export — presets are
 * captured from real WFP entries or curated here one at a time, never bulk-imported.
 *
 * Access guard: only users with canManageConfig may view this page.
 *
 * Endpoints (ConfigProcurementPresetFunctions.cs, { data, error, message } envelope):
 *   GET    /api/config/procurement-presets?accountId=&active=   (accountId optional — omit for all accounts)
 *   GET    /api/config/procurement-presets/{id}
 *   POST   /api/config/procurement-presets
 *   PUT    /api/config/procurement-presets/{id}
 *   DELETE /api/config/procurement-presets/{id}    (soft delete)
 */

import { useCallback, useEffect, useState } from "react";
import { useRouter } from "next/navigation";
import { fetchMe } from "@/lib/me-cache";
import {
  configErrorMessage,
  createProcurementPreset,
  deactivateProcurementPreset,
  listAccounts,
  listPriceIndex,
  listProcurementPresets,
  updateProcurementPreset,
} from "@/lib/config";
import { formatMoney } from "@/lib/money";
import Modal from "@/components/ui/Modal";
import ConfirmDialog, { type ConfirmDialogProps } from "@/components/ui/ConfirmDialog";
import MoneyInput from "@/components/ui/MoneyInput";
import Lookup from "@/components/ui/Lookup";
import { useToast } from "@/components/ui/Toast";
import type {
  AccountResponse,
  ActiveFilter,
  PriceIndexItemResponse,
  ProcurementPresetResponse,
  UpsertProcurementPresetItemRequest,
  UpsertProcurementPresetRequest,
} from "@/types";

// ---------------------------------------------------------------------------
// Filter option types
// ---------------------------------------------------------------------------

type StatusFilter = "Active" | "Inactive" | "All";

const STATUS_OPTIONS: StatusFilter[] = ["Active", "Inactive", "All"];

const STATUS_TO_ACTIVE: Record<StatusFilter, ActiveFilter> = {
  Active: "true",
  Inactive: "false",
  All: "all",
};

const accountLabel = (a: AccountResponse) => `${a.accountNumber} — ${a.accountTitle}`;
const accountSearchText = (a: AccountResponse) => `${a.accountNumber} ${a.accountTitle}`;

const priceIndexItemLabel = (p: PriceIndexItemResponse) => `${p.name} (${p.unit}) — ₱${formatMoney(p.unitPrice)}`;
const priceIndexItemSearchText = (p: PriceIndexItemResponse) => `${p.name} ${p.unit}`;

/** Sum of unitPrice × qty across a set of line items — the preset's full total. */
function sumItemTotals(items: { unitPrice: number | null; defaultQty: number | null }[]): number {
  return items.reduce((sum, i) => sum + (i.unitPrice ?? 0) * (i.defaultQty ?? 0), 0);
}

// ---------------------------------------------------------------------------
// Status badge
// ---------------------------------------------------------------------------

function StatusBadge({ active }: { active: boolean }) {
  return (
    <span
      className={`inline-flex items-center px-2 py-0.5 text-xs font-medium ${
        active ? "bg-green-100 text-green-700" : "bg-danger-100 text-danger-500"
      }`}
    >
      {active ? "Active" : "Inactive"}
    </span>
  );
}

// ---------------------------------------------------------------------------
// Item form row state
// ---------------------------------------------------------------------------

interface ItemFormRow {
  key: string;
  priceIndexItemId: number | null;
  name: string;
  unit: string;
  unitPrice: number | null;
  defaultQty: number | null;
}

let rowKeySeq = 0;
const blankItemRow = (): ItemFormRow => ({
  key: `row-${++rowKeySeq}`,
  priceIndexItemId: null,
  name: "",
  unit: "",
  unitPrice: null,
  defaultQty: 1,
});

interface FormState {
  accountId: number | null;
  name: string;
  items: ItemFormRow[];
}

const blankForm = (accountId: number | null): FormState => ({
  accountId,
  name: "",
  items: [blankItemRow()],
});

// ---------------------------------------------------------------------------
// Page
// ---------------------------------------------------------------------------

export default function ProcurementPresetsConfigPage() {
  const router = useRouter();
  const { toast } = useToast();

  const [authChecked, setAuthChecked] = useState(false);

  // Accounts (for the scope filter + item picker)
  const [accounts, setAccounts] = useState<AccountResponse[]>([]);
  // null = "All Accounts" (the default view)
  const [accountId, setAccountId] = useState<number | null>(null);

  // Price index (for the item picker)
  const [priceIndex, setPriceIndex] = useState<PriceIndexItemResponse[]>([]);

  // Data
  const [presets, setPresets] = useState<ProcurementPresetResponse[]>([]);
  const [loading, setLoading] = useState(false);
  const [fetchError, setFetchError] = useState<string | null>(null);
  const [expanded, setExpanded] = useState<Set<number>>(new Set());

  // Filters
  const [statusFilter, setStatusFilter] = useState<StatusFilter>("Active");

  // Add / Edit modal
  const [showForm, setShowForm] = useState(false);
  const [editTarget, setEditTarget] = useState<ProcurementPresetResponse | null>(null);
  const [form, setForm] = useState<FormState>(blankForm(null));
  const [saving, setSaving] = useState(false);
  const [formError, setFormError] = useState<string | null>(null);

  const [confirm, setConfirm] = useState<ConfirmDialogProps | null>(null);

  // ── Auth check ──────────────────────────────────────────────────────────────

  useEffect(() => {
    fetchMe()
      .then((data) => {
        if (!data.canManageConfig) {
          router.replace(data.officeId != null ? "/budget-planning" : "/dashboard");
          return;
        }
        setAuthChecked(true);
      })
      .catch(() => router.replace("/login"));
  }, [router]);

  // ── Load accounts + price index once ─────────────────────────────────────────

  useEffect(() => {
    if (!authChecked) return;
    listAccounts({ active: "true" })
      .then((data) => setAccounts(data))
      .catch(() => toast.error("Failed to load accounts", "Please refresh the page."));
    listPriceIndex({ active: "true" }).catch(() => undefined).then((data) => setPriceIndex(data ?? []));
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [authChecked]);

  // ── Load presets (scoped to accountId, or all accounts when null) ───────────

  const load = useCallback(async () => {
    setLoading(true);
    setFetchError(null);
    try {
      const data = await listProcurementPresets({ accountId, active: STATUS_TO_ACTIVE[statusFilter] });
      setPresets(data);
    } catch (err) {
      setFetchError(configErrorMessage(err, "Failed to load procurement presets. Please try again."));
    } finally {
      setLoading(false);
    }
  }, [accountId, statusFilter]);

  useEffect(() => {
    if (authChecked) void load();
  }, [authChecked, load]);

  // ── Expand / collapse ─────────────────────────────────────────────────────────

  function toggleExpand(id: number) {
    setExpanded((cur) => {
      const next = new Set(cur);
      if (next.has(id)) next.delete(id);
      else next.add(id);
      return next;
    });
  }

  // ── Add / Edit ────────────────────────────────────────────────────────────────

  function openAdd() {
    setEditTarget(null);
    setForm(blankForm(accountId));
    setFormError(null);
    setShowForm(true);
  }

  function openEdit(preset: ProcurementPresetResponse) {
    setEditTarget(preset);
    setForm({
      accountId: preset.accountId,
      name: preset.name,
      items: preset.items.map((i) => ({
        key: `row-${++rowKeySeq}`,
        priceIndexItemId: i.priceIndexItemId,
        name: i.name,
        unit: i.unit,
        unitPrice: i.unitPrice,
        defaultQty: i.defaultQty,
      })),
    });
    setFormError(null);
    setShowForm(true);
  }

  function closeForm() {
    setShowForm(false);
    setEditTarget(null);
  }

  function addItemRow() {
    setForm((f) => ({ ...f, items: [...f.items, blankItemRow()] }));
  }

  function removeItemRow(key: string) {
    setForm((f) => ({ ...f, items: f.items.filter((r) => r.key !== key) }));
  }

  function updateItemRow(key: string, patch: Partial<ItemFormRow>) {
    setForm((f) => ({
      ...f,
      items: f.items.map((r) => (r.key === key ? { ...r, ...patch } : r)),
    }));
  }

  function pickPriceIndexItem(key: string, priceIndexItemId: number | null) {
    if (priceIndexItemId == null) {
      updateItemRow(key, { priceIndexItemId: null });
      return;
    }
    const source = priceIndex.find((p) => p.id === priceIndexItemId);
    updateItemRow(key, {
      priceIndexItemId,
      name: source?.name ?? "",
      unit: source?.unit ?? "",
      unitPrice: source?.unitPrice ?? null,
    });
  }

  async function handleSubmit() {
    const name = form.name.trim();
    if (form.accountId == null) {
      setFormError("An account is required.");
      return;
    }
    if (!name) {
      setFormError("Name is required.");
      return;
    }
    if (form.items.length === 0) {
      setFormError("At least one item is required.");
      return;
    }
    for (const row of form.items) {
      if (row.priceIndexItemId == null) {
        if (!row.name.trim() || !row.unit.trim() || row.unitPrice == null) {
          setFormError("Every free-typed item needs a name, unit, and unit price.");
          return;
        }
      }
      if (row.defaultQty == null || row.defaultQty < 0) {
        setFormError("Every item needs a default quantity of zero or more.");
        return;
      }
    }

    const items: UpsertProcurementPresetItemRequest[] = form.items.map((row) => ({
      priceIndexItemId: row.priceIndexItemId,
      name: row.priceIndexItemId == null ? row.name.trim() : null,
      unit: row.priceIndexItemId == null ? row.unit.trim() : null,
      unitPrice: row.priceIndexItemId == null ? row.unitPrice : null,
      defaultQty: row.defaultQty ?? 0,
    }));

    const body: UpsertProcurementPresetRequest = {
      accountId: form.accountId,
      name,
      isActive: editTarget ? editTarget.isActive : true,
      items,
    };

    setSaving(true);
    setFormError(null);
    try {
      if (editTarget) {
        await updateProcurementPreset(editTarget.id, body);
        toast.success("Preset updated", `${body.name} saved.`);
      } else {
        await createProcurementPreset(body);
        toast.success("Preset created", `${body.name} added.`);
      }
      closeForm();
      await load();
    } catch (err) {
      setFormError(configErrorMessage(err, "Failed to save the preset. Please try again."));
    } finally {
      setSaving(false);
    }
  }

  // ── Deactivate / Reactivate ───────────────────────────────────────────────────

  function confirmDeactivate(preset: ProcurementPresetResponse) {
    setConfirm({
      title: "Deactivate preset?",
      message: `${preset.name} will be hidden from "Load preset" in WFP entry. Existing WFP lines that used it are preserved.`,
      confirmLabel: "Deactivate",
      variant: "danger",
      onConfirm: () => void doDeactivate(preset),
      onClose: () => setConfirm(null),
    });
  }

  async function doDeactivate(preset: ProcurementPresetResponse) {
    try {
      await deactivateProcurementPreset(preset.id);
      toast.success("Preset deactivated", `${preset.name} is now inactive.`);
      await load();
    } catch (err) {
      toast.error("Deactivate failed", configErrorMessage(err, "Please try again."));
    }
  }

  async function doReactivate(preset: ProcurementPresetResponse) {
    try {
      await updateProcurementPreset(preset.id, {
        accountId: preset.accountId,
        name: preset.name,
        isActive: true,
        items: preset.items.map((i) => ({
          priceIndexItemId: i.priceIndexItemId,
          name: i.priceIndexItemId == null ? i.name : null,
          unit: i.priceIndexItemId == null ? i.unit : null,
          unitPrice: i.priceIndexItemId == null ? i.unitPrice : null,
          defaultQty: i.defaultQty,
        })),
      });
      toast.success("Preset reactivated", `${preset.name} is now active.`);
      await load();
    } catch (err) {
      toast.error("Reactivate failed", configErrorMessage(err, "Please try again."));
    }
  }

  // ── Auth gate ─────────────────────────────────────────────────────────────────

  if (!authChecked) {
    return (
      <div className="min-h-full flex items-center justify-center bg-slate-100">
        <div className="w-8 h-8 border-4 border-green-600 border-t-transparent rounded-full animate-spin" />
      </div>
    );
  }

  // ── Render ────────────────────────────────────────────────────────────────────

  return (
    <div className="min-h-full bg-slate-100 font-sans">
      <div className="max-w-6xl mx-auto px-6 py-6 space-y-4">
        {/* Header */}
        <div className="flex items-start justify-between gap-3">
          <div>
            <h1 className="text-lg font-bold text-slate-800">Procurement Presets</h1>
            <p className="text-sm text-slate-600">
              Account-scoped, reusable procurement line-item templates for WFP entry. Shared
              across all offices and divisions.
            </p>
          </div>
          <button
            onClick={openAdd}
            className="flex items-center gap-1.5 bg-green-600 text-white font-semibold text-sm px-4 py-2.5 hover:bg-green-500 transition-colors shrink-0"
          >
            <span className="text-base leading-none">+</span>
            Add Preset
          </button>
        </div>

        {/* Filter bar */}
        <div className="flex flex-wrap items-center gap-3 bg-white border border-slate-200 px-4 py-3">
          <div className="flex-1 min-w-[280px]">
            <Lookup
              items={accounts}
              value={accountId}
              onChange={setAccountId}
              getId={(a) => a.id}
              getLabel={accountLabel}
              getSearchText={accountSearchText}
              allOptionLabel="All Accounts"
              placeholder="Search account by number or title…"
            />
          </div>

          {/* Status toggle */}
          <div className="flex items-center border border-slate-200 overflow-hidden">
            {STATUS_OPTIONS.map((s) => (
              <button
                key={s}
                onClick={() => setStatusFilter(s)}
                className={`px-3 py-2 text-sm font-medium transition-colors ${
                  statusFilter === s ? "bg-green-600 text-white" : "bg-white text-slate-600 hover:bg-slate-50"
                }`}
              >
                {s}
              </button>
            ))}
          </div>
        </div>

        {/* Master-detail preset list */}
        <div className="bg-white border border-slate-200">
          {loading ? (
            <div className="py-16 flex items-center justify-center">
              <div className="w-6 h-6 border-2 border-green-600 border-t-transparent rounded-full animate-spin" />
            </div>
          ) : fetchError ? (
            <div className="py-16 flex flex-col items-center justify-center gap-3">
              <p className="text-sm text-danger-500">{fetchError}</p>
              <button onClick={load} className="text-sm text-green-600 hover:underline">
                Retry
              </button>
            </div>
          ) : presets.length === 0 ? (
            <div className="py-16 flex flex-col items-center justify-center gap-2 text-slate-600">
              <span className="text-3xl">📭</span>
              <p className="text-sm">
                {accountId == null ? "No presets yet." : "No presets for this account yet."}
              </p>
            </div>
          ) : (
            <ul className="divide-y divide-slate-100">
              {presets.map((preset) => {
                const isOpen = expanded.has(preset.id);
                return (
                  <li key={preset.id}>
                    <div className="flex items-center gap-3 px-4 py-3 hover:bg-green-50 transition-colors">
                      <button
                        onClick={() => toggleExpand(preset.id)}
                        className="w-5 h-5 shrink-0 flex items-center justify-center text-slate-600 hover:text-slate-800"
                        aria-label={isOpen ? "Collapse" : "Expand"}
                      >
                        <span className={`inline-block transition-transform ${isOpen ? "rotate-90" : ""}`}>▸</span>
                      </button>

                      <div className="flex-1 min-w-0">
                        <div className="flex items-center gap-2">
                          <span className="font-medium text-slate-800">{preset.name}</span>
                          <StatusBadge active={preset.isActive} />
                        </div>
                        <p className="text-xs text-slate-600 mt-0.5">
                          {preset.accountNumber ?? "—"} — {preset.accountTitle ?? "Unknown account"} ·{" "}
                          {preset.items.length} item{preset.items.length === 1 ? "" : "s"} · created by{" "}
                          {preset.createdByName ?? "—"}
                        </p>
                      </div>

                      <span className="text-sm font-mono tabular-nums font-semibold text-slate-800 shrink-0">
                        ₱{formatMoney(sumItemTotals(preset.items))}
                      </span>

                      <div className="flex items-center gap-2 text-sm shrink-0">
                        <TextAction onClick={() => openEdit(preset)}>Edit</TextAction>
                        <span className="text-slate-300">·</span>
                        {preset.isActive ? (
                          <TextAction danger onClick={() => confirmDeactivate(preset)}>
                            Deactivate
                          </TextAction>
                        ) : (
                          <TextAction onClick={() => void doReactivate(preset)}>Reactivate</TextAction>
                        )}
                      </div>
                    </div>

                    {isOpen && (
                      <div className="bg-slate-50 border-t border-slate-100 px-4 py-3 pl-12">
                        <table className="w-full text-sm">
                          <thead>
                            <tr className="text-xs text-slate-600 uppercase tracking-wide">
                              <th className="text-left font-medium py-1">Item</th>
                              <th className="text-left font-medium py-1">Unit</th>
                              <th className="text-right font-medium py-1">Unit Price</th>
                              <th className="text-right font-medium py-1">Default Qty</th>
                              <th className="text-right font-medium py-1">Total</th>
                            </tr>
                          </thead>
                          <tbody>
                            {preset.items.map((item) => (
                              <tr key={item.id} className="border-t border-slate-200">
                                <td className="py-1.5 text-slate-700">{item.name}</td>
                                <td className="py-1.5 text-slate-600">{item.unit}</td>
                                <td className="py-1.5 text-right font-mono tabular-nums text-slate-700">
                                  ₱{formatMoney(item.unitPrice)}
                                </td>
                                <td className="py-1.5 text-right font-mono tabular-nums text-slate-700">
                                  {item.defaultQty}
                                </td>
                                <td className="py-1.5 text-right font-mono tabular-nums text-slate-800 font-medium">
                                  ₱{formatMoney(item.unitPrice * item.defaultQty)}
                                </td>
                              </tr>
                            ))}
                          </tbody>
                          <tfoot>
                            <tr className="border-t-2 border-slate-300">
                              <td colSpan={4} className="py-1.5 text-right font-medium text-slate-600">
                                Preset Total
                              </td>
                              <td className="py-1.5 text-right font-mono tabular-nums font-semibold text-slate-800">
                                ₱{formatMoney(sumItemTotals(preset.items))}
                              </td>
                            </tr>
                          </tfoot>
                        </table>
                      </div>
                    )}
                  </li>
                );
              })}
            </ul>
          )}
        </div>
      </div>

      {/* ── Add / Edit modal ──────────────────────────────────────────────────── */}
      {showForm && (
        <Modal
          title={editTarget ? `Edit Preset — ${editTarget.name}` : "Add Procurement Preset"}
          size="xl"
          onClose={closeForm}
          footer={
            <>
              <Modal.SecondaryButton onClick={closeForm} disabled={saving}>
                Cancel
              </Modal.SecondaryButton>
              <Modal.PrimaryButton onClick={handleSubmit} loading={saving}>
                {editTarget ? "Save Changes" : "Create Preset"}
              </Modal.PrimaryButton>
            </>
          }
        >
          <div className="space-y-4">
            <div className="grid grid-cols-2 gap-4">
              {/* Account */}
              <div>
                <label className="block text-xs font-medium text-slate-600 mb-1">Account *</label>
                <Lookup
                  items={accounts}
                  value={form.accountId}
                  onChange={(id) => setForm((f) => ({ ...f, accountId: id }))}
                  getId={(a) => a.id}
                  getLabel={accountLabel}
                  getSearchText={accountSearchText}
                  placeholder="Search account by number or title…"
                />
              </div>

              {/* Name */}
              <div>
                <label className="block text-xs font-medium text-slate-600 mb-1">Name *</label>
                <input
                  value={form.name}
                  onChange={(e) => setForm((f) => ({ ...f, name: e.target.value }))}
                  placeholder="Standard Office Supplies Kit"
                  className="w-full px-3 py-1.5 text-sm border border-slate-200 focus:outline-none focus:ring-2 focus:ring-green-600"
                />
              </div>
            </div>

            {/* Items */}
            <div>
              <div className="flex items-center justify-between mb-1">
                <label className="block text-xs font-medium text-slate-600">Items *</label>
                <button onClick={addItemRow} className="text-xs font-medium text-green-600 hover:underline">
                  + Add item
                </button>
              </div>
              <div className="space-y-2">
                {form.items.map((row) => (
                  <ItemRow
                    key={row.key}
                    row={row}
                    priceIndex={priceIndex}
                    onPickPriceIndexItem={(id) => pickPriceIndexItem(row.key, id)}
                    onChange={(patch) => updateItemRow(row.key, patch)}
                    onRemove={form.items.length > 1 ? () => removeItemRow(row.key) : undefined}
                  />
                ))}
              </div>
              <div className="flex items-center justify-end gap-2 mt-2 px-1">
                <span className="text-xs font-medium text-slate-600 uppercase tracking-wide">Preset Total</span>
                <span className="text-base font-mono tabular-nums font-semibold text-slate-800">
                  ₱{formatMoney(sumItemTotals(form.items))}
                </span>
              </div>
            </div>

            {formError && (
              <div className="bg-danger-100 border border-danger-500/30 px-4 py-3">
                <p className="text-sm text-danger-500">{formError}</p>
              </div>
            )}
          </div>
        </Modal>
      )}

      {/* ── Deactivate confirm ─────────────────────────────────────────────────── */}
      {confirm && <ConfirmDialog {...confirm} />}
    </div>
  );
}

// ---------------------------------------------------------------------------
// Item row editor
// ---------------------------------------------------------------------------

function ItemRow({
  row,
  priceIndex,
  onPickPriceIndexItem,
  onChange,
  onRemove,
}: {
  row: ItemFormRow;
  priceIndex: PriceIndexItemResponse[];
  onPickPriceIndexItem: (id: number | null) => void;
  onChange: (patch: Partial<ItemFormRow>) => void;
  onRemove?: () => void;
}) {
  const fromPriceIndex = row.priceIndexItemId != null;
  const total = (row.unitPrice ?? 0) * (row.defaultQty ?? 0);

  return (
    <div className="border border-slate-200 p-3 space-y-2">
      <div className="flex items-center justify-between gap-2">
        <Lookup
          items={priceIndex}
          value={row.priceIndexItemId}
          onChange={onPickPriceIndexItem}
          getId={(p) => p.id}
          getLabel={priceIndexItemLabel}
          getSearchText={priceIndexItemSearchText}
          allOptionLabel="Free-typed item (no price index link)"
          placeholder="Search price index by item name…"
          className="flex-1 min-w-0"
        />
        {onRemove && (
          <button onClick={onRemove} className="text-danger-500 hover:text-red-600 text-sm shrink-0">
            Remove
          </button>
        )}
      </div>

      <div className="flex items-end gap-2">
        <div className="flex-1 min-w-0">
          <label className="block text-[11px] text-slate-600 mb-0.5">Name</label>
          <input
            value={row.name}
            onChange={(e) => onChange({ name: e.target.value })}
            disabled={fromPriceIndex}
            placeholder="Item name"
            className="w-full px-2 py-1.5 text-sm border border-slate-200 focus:outline-none focus:ring-2 focus:ring-green-600 disabled:bg-slate-100 disabled:text-slate-400"
          />
        </div>
        <div className="w-28 shrink-0">
          <label className="block text-[11px] text-slate-600 mb-0.5">Unit</label>
          <input
            value={row.unit}
            onChange={(e) => onChange({ unit: e.target.value })}
            disabled={fromPriceIndex}
            placeholder="ream"
            className="w-full px-2 py-1.5 text-sm border border-slate-200 focus:outline-none focus:ring-2 focus:ring-green-600 disabled:bg-slate-100 disabled:text-slate-400"
          />
        </div>
        <div className="w-32 shrink-0">
          <label className="block text-[11px] text-slate-600 mb-0.5">Unit Price</label>
          <MoneyInput
            value={row.unitPrice}
            onChange={(v) => onChange({ unitPrice: v })}
            disabled={fromPriceIndex}
            className="w-full"
          />
        </div>
        <div className="w-20 shrink-0">
          <label className="block text-[11px] text-slate-600 mb-0.5">Qty</label>
          <input
            type="number"
            min={0}
            step="1"
            value={row.defaultQty ?? ""}
            onChange={(e) => onChange({ defaultQty: e.target.value === "" ? null : Number(e.target.value) })}
            className="w-full px-2 py-1.5 text-sm border border-slate-200 focus:outline-none focus:ring-2 focus:ring-green-600"
          />
        </div>
        <div className="w-32 shrink-0">
          <label className="block text-[11px] text-slate-600 mb-0.5">Total</label>
          <div className="w-full px-2 py-1.5 text-sm text-right font-mono tabular-nums text-slate-700 bg-slate-50 border border-slate-200">
            ₱{formatMoney(total)}
          </div>
        </div>
      </div>
    </div>
  );
}

// ---------------------------------------------------------------------------
// Small helpers
// ---------------------------------------------------------------------------

function TextAction({
  children,
  danger,
  onClick,
}: {
  children: React.ReactNode;
  danger?: boolean;
  onClick: () => void;
}) {
  return (
    <button
      onClick={onClick}
      className={`font-medium transition-colors ${
        danger ? "text-danger-500 hover:text-red-600" : "text-green-600 hover:text-green-700"
      } hover:underline`}
    >
      {children}
    </button>
  );
}
