"use client";

/**
 * Price Index Configuration page — v1.4 RAL-118.
 *
 * A procurement item name + unit price catalogue searched from the WFP
 * procurement line-item entry screen (RAL-125), which snapshots name/unit/price
 * at the moment an item is picked — later edits here never retroactively change
 * a saved WFP line. Built on the same reusable pattern as the Account/Office/
 * Funding-Source config pages with the shared UI components: DataTable · Modal ·
 * MessageDialog · ConfirmDialog · CsvUploadButton · CsvDownloadButton · Toast.
 *
 * Data originates from GSO's own application — currently downloaded as an Excel
 * file — so CSV upload is the PRIMARY seeding/maintenance path here, not a
 * secondary convenience (docs/v1.4/WFP_Rework_Requirements_Draft.md §7.1).
 *
 * (name, unit) is the composite unique key — there is no natural external code
 * for a GSO price item the way Funding Source has `code` or Account has
 * `account_number`.
 *
 * price_updated_at is shown (not edited) — it auto-bumps server-side only when
 * unit_price actually changes, so a stale price is visible rather than silently
 * trusted (§7.1 ★REC).
 *
 * Access guard: only users with canManageConfig may view this page.
 *
 * Endpoints (ConfigPriceIndexFunctions.cs, { data, error, message } envelope):
 *   GET    /api/config/price-index?search=&active=
 *   POST   /api/config/price-index
 *   PUT    /api/config/price-index/{id}
 *   DELETE /api/config/price-index/{id}    (soft delete)
 *   GET    /api/config/price-index/csv     (export)
 *   POST   /api/config/price-index/csv     (upsert)
 */

import { useCallback, useEffect, useState } from "react";
import { useRouter } from "next/navigation";
import { fetchMe } from "@/lib/me-cache";
import {
  configErrorMessage,
  createPriceIndexItem,
  deactivatePriceIndexItem,
  exportPriceIndexCsv,
  importPriceIndexCsv,
  listPriceIndex,
  updatePriceIndexItem,
} from "@/lib/config";
import { formatMoney } from "@/lib/money";
import DataTable, { type Column } from "@/components/ui/DataTable";
import Modal from "@/components/ui/Modal";
import MessageDialog from "@/components/ui/MessageDialog";
import ConfirmDialog, { type ConfirmDialogProps } from "@/components/ui/ConfirmDialog";
import CsvUploadButton from "@/components/ui/CsvUploadButton";
import CsvDownloadButton from "@/components/ui/CsvDownloadButton";
import MoneyInput from "@/components/ui/MoneyInput";
import { useToast } from "@/components/ui/Toast";
import type {
  ActiveFilter,
  CsvImportResult,
  PriceIndexItemResponse,
  UpsertPriceIndexItemRequest,
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

function formatDate(iso: string): string {
  return new Date(iso).toLocaleDateString("en-PH", { year: "numeric", month: "short", day: "numeric" });
}

// ---------------------------------------------------------------------------
// Form state
// ---------------------------------------------------------------------------

interface FormState {
  name: string;
  unit: string;
  unitPrice: number | null;
  category: string;
}

const blankForm = (): FormState => ({
  name: "",
  unit: "",
  unitPrice: null,
  category: "",
});

// ---------------------------------------------------------------------------
// Page
// ---------------------------------------------------------------------------

export default function PriceIndexConfigPage() {
  const router = useRouter();
  const { toast } = useToast();

  // Auth / permission guard
  const [authChecked, setAuthChecked] = useState(false);

  // Data
  const [items, setItems] = useState<PriceIndexItemResponse[]>([]);
  const [loading, setLoading] = useState(true);
  const [fetchError, setFetchError] = useState<string | null>(null);

  // Filters
  const [search, setSearch] = useState("");
  const [debouncedSearch, setDebouncedSearch] = useState("");
  const [statusFilter, setStatusFilter] = useState<StatusFilter>("Active");

  // Add / Edit modal
  const [showForm, setShowForm] = useState(false);
  const [editTarget, setEditTarget] = useState<PriceIndexItemResponse | null>(null);
  const [form, setForm] = useState<FormState>(blankForm());
  const [saving, setSaving] = useState(false);
  const [formError, setFormError] = useState<string | null>(null);

  // Confirm / message dialogs
  const [confirm, setConfirm] = useState<ConfirmDialogProps | null>(null);
  const [pendingCsv, setPendingCsv] = useState<File | null>(null);
  const [importing, setImporting] = useState(false);
  const [importResult, setImportResult] = useState<CsvImportResult | null>(null);

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

  // ── Debounce search ─────────────────────────────────────────────────────────

  useEffect(() => {
    const t = setTimeout(() => setDebouncedSearch(search), 300);
    return () => clearTimeout(t);
  }, [search]);

  // ── Load (server-side filtering) ─────────────────────────────────────────────

  const load = useCallback(async () => {
    setLoading(true);
    setFetchError(null);
    try {
      const data = await listPriceIndex({
        search: debouncedSearch,
        active: STATUS_TO_ACTIVE[statusFilter],
      });
      setItems(data);
    } catch (err) {
      setFetchError(configErrorMessage(err, "Failed to load price index items. Please try again."));
    } finally {
      setLoading(false);
    }
  }, [debouncedSearch, statusFilter]);

  useEffect(() => {
    if (authChecked) load();
  }, [authChecked, load]);

  // ── Add / Edit ────────────────────────────────────────────────────────────────

  function openAdd() {
    setEditTarget(null);
    setForm(blankForm());
    setFormError(null);
    setShowForm(true);
  }

  function openEdit(item: PriceIndexItemResponse) {
    setEditTarget(item);
    setForm({
      name: item.name,
      unit: item.unit,
      unitPrice: item.unitPrice,
      category: item.category ?? "",
    });
    setFormError(null);
    setShowForm(true);
  }

  function closeForm() {
    setShowForm(false);
    setEditTarget(null);
  }

  async function handleSubmit() {
    const name = form.name.trim();
    const unit = form.unit.trim();
    if (!name || !unit) {
      setFormError("Name and unit are required.");
      return;
    }
    if (form.unitPrice == null || form.unitPrice < 0) {
      setFormError("A unit price of zero or more is required.");
      return;
    }

    const body: UpsertPriceIndexItemRequest = {
      name,
      unit,
      unitPrice: form.unitPrice,
      category: form.category.trim() || null,
      // Modal does not edit status; preserve it on update, default active on create.
      isActive: editTarget ? editTarget.isActive : true,
    };

    setSaving(true);
    setFormError(null);
    try {
      if (editTarget) {
        await updatePriceIndexItem(editTarget.id, body);
        toast.success("Price index item updated", `${body.name} saved.`);
      } else {
        await createPriceIndexItem(body);
        toast.success("Price index item created", `${body.name} added.`);
      }
      closeForm();
      await load();
    } catch (err) {
      setFormError(configErrorMessage(err, "Failed to save the price index item. Please try again."));
    } finally {
      setSaving(false);
    }
  }

  // ── Deactivate / Reactivate ───────────────────────────────────────────────────

  function confirmDeactivate(item: PriceIndexItemResponse) {
    setConfirm({
      title: "Deactivate price index item?",
      message: `${item.name} (${item.unit}) will be hidden from the WFP procurement item search. Existing WFP lines that reference it are preserved.`,
      confirmLabel: "Deactivate",
      variant: "danger",
      onConfirm: () => void doDeactivate(item),
      onClose: () => setConfirm(null),
    });
  }

  async function doDeactivate(item: PriceIndexItemResponse) {
    try {
      await deactivatePriceIndexItem(item.id);
      toast.success("Price index item deactivated", `${item.name} is now inactive.`);
      await load();
    } catch (err) {
      toast.error("Deactivate failed", configErrorMessage(err, "Please try again."));
    }
  }

  async function doReactivate(item: PriceIndexItemResponse) {
    try {
      await updatePriceIndexItem(item.id, {
        name: item.name,
        unit: item.unit,
        unitPrice: item.unitPrice,
        category: item.category,
        isActive: true,
      });
      toast.success("Price index item reactivated", `${item.name} is now active.`);
      await load();
    } catch (err) {
      toast.error("Reactivate failed", configErrorMessage(err, "Please try again."));
    }
  }

  // ── CSV import ────────────────────────────────────────────────────────────────

  async function doImport() {
    if (!pendingCsv) return;
    setImporting(true);
    try {
      const text = await pendingCsv.text();
      const result = await importPriceIndexCsv(text);
      setPendingCsv(null);
      setImportResult(result);
      toast.success(
        "Import complete",
        `${result.new} added, ${result.updated} updated, ${result.skipped} skipped.`,
      );
      await load();
    } catch (err) {
      setPendingCsv(null);
      toast.error("Import failed", configErrorMessage(err, "The CSV could not be imported."));
    } finally {
      setImporting(false);
    }
  }

  // ── Columns ───────────────────────────────────────────────────────────────────

  const columns: Column<PriceIndexItemResponse>[] = [
    {
      key: "name",
      header: "Name",
      sortable: true,
      render: (p) => <span className="font-medium text-slate-800">{p.name}</span>,
    },
    {
      key: "unit",
      header: "Unit",
      sortable: true,
      render: (p) => <span className="text-slate-600">{p.unit}</span>,
    },
    {
      key: "category",
      header: "Category",
      sortable: true,
      render: (p) => p.category ?? <span className="text-slate-300">—</span>,
    },
    {
      key: "unitPrice",
      header: "Unit Price",
      align: "right",
      sortable: true,
      render: (p) => <span className="font-mono tabular-nums text-slate-800">₱{formatMoney(p.unitPrice)}</span>,
    },
    {
      key: "priceUpdatedAt",
      header: "Price Updated",
      sortable: true,
      render: (p) => <span className="text-xs text-slate-400">{formatDate(p.priceUpdatedAt)}</span>,
    },
    {
      key: "isActive",
      header: "Status",
      sortable: true,
      sortValue: (p) => (p.isActive ? 1 : 0),
      render: (p) => <StatusBadge active={p.isActive} />,
    },
    {
      key: "actions",
      header: "Actions",
      align: "right",
      render: (p) => (
        <div className="flex items-center justify-end gap-2 text-sm">
          <TextAction onClick={() => openEdit(p)}>Edit</TextAction>
          <span className="text-slate-300">·</span>
          {p.isActive ? (
            <TextAction danger onClick={() => confirmDeactivate(p)}>
              Deactivate
            </TextAction>
          ) : (
            <TextAction onClick={() => void doReactivate(p)}>Reactivate</TextAction>
          )}
        </div>
      ),
    },
  ];

  // ── Auth gate ─────────────────────────────────────────────────────────────────

  if (!authChecked) {
    return (
      <div className="min-h-full flex items-center justify-center bg-slate-100">
        <div className="w-8 h-8 border-4 border-green-600 border-t-transparent rounded-full animate-spin" />
      </div>
    );
  }

  const filtersActive = debouncedSearch !== "" || statusFilter !== "Active";

  // ── Render ────────────────────────────────────────────────────────────────────

  return (
    <div className="min-h-full bg-slate-100 font-sans">
      <div className="max-w-6xl mx-auto px-6 py-6 space-y-4">
        {/* Header */}
        <div className="flex items-start justify-between gap-3">
          <div>
            <h1 className="text-lg font-bold text-slate-800">Price Index</h1>
            <p className="text-sm text-slate-500">
              Procurement item catalogue searched from WFP procurement entries. Data comes from
              GSO&apos;s own price lists — upload a CSV to seed or refresh it.
            </p>
          </div>
          <div className="flex items-center gap-2 shrink-0">
            <CsvDownloadButton
              filename="price-index.csv"
              fetchCsv={exportPriceIndexCsv}
              onError={(msg) => toast.error("Export failed", msg)}
            />
            <CsvUploadButton onSelect={(file) => setPendingCsv(file)} />
            <button
              onClick={openAdd}
              className="flex items-center gap-1.5 bg-green-600 text-white font-semibold text-sm px-4 py-2.5 hover:bg-green-500 transition-colors shrink-0"
            >
              <span className="text-base leading-none">+</span>
              Add Item
            </button>
          </div>
        </div>

        {/* Filter bar */}
        <div className="flex flex-wrap items-center gap-3 bg-white border border-slate-200 px-4 py-3">
          <input
            value={search}
            onChange={(e) => setSearch(e.target.value)}
            placeholder="Search by name or category…"
            className="flex-1 min-w-[220px] px-3 py-2 text-sm border border-slate-200 bg-white focus:outline-none focus:ring-2 focus:ring-green-600"
          />

          {/* Status toggle */}
          <div className="flex items-center border border-slate-200 overflow-hidden">
            {STATUS_OPTIONS.map((s) => (
              <button
                key={s}
                onClick={() => setStatusFilter(s)}
                className={`px-3 py-2 text-sm font-medium transition-colors ${
                  statusFilter === s
                    ? "bg-green-600 text-white"
                    : "bg-white text-slate-500 hover:bg-slate-50"
                }`}
              >
                {s}
              </button>
            ))}
          </div>

          {filtersActive && (
            <button
              onClick={() => {
                setSearch("");
                setStatusFilter("Active");
              }}
              className="text-sm text-slate-400 hover:text-slate-600 transition-colors px-1"
            >
              Reset
            </button>
          )}
        </div>

        {/* Table */}
        <DataTable
          columns={columns}
          rows={items}
          rowKey={(p) => p.id}
          loading={loading}
          error={fetchError}
          onRetry={load}
          emptyMessage={
            filtersActive
              ? "No price index items match your filters."
              : "No price index items yet. Upload a CSV from GSO to get started."
          }
          pageSize={25}
          rowNoun={["item", "items"]}
        />
      </div>

      {/* ── Add / Edit modal ──────────────────────────────────────────────────── */}
      {showForm && (
        <Modal
          title={editTarget ? `Edit Price Index Item — ${editTarget.name}` : "Add Price Index Item"}
          size="md"
          onClose={closeForm}
          footer={
            <>
              <Modal.SecondaryButton onClick={closeForm} disabled={saving}>
                Cancel
              </Modal.SecondaryButton>
              <Modal.PrimaryButton onClick={handleSubmit} loading={saving}>
                {editTarget ? "Save Changes" : "Create Item"}
              </Modal.PrimaryButton>
            </>
          }
        >
          <div className="space-y-4">
            {/* Name */}
            <div>
              <label className="block text-xs font-medium text-slate-600 mb-1">Name *</label>
              <input
                value={form.name}
                onChange={(e) => setForm((f) => ({ ...f, name: e.target.value }))}
                placeholder="Bond Paper"
                className="w-full px-3 py-2 text-sm border border-slate-200 focus:outline-none focus:ring-2 focus:ring-green-600"
              />
            </div>

            {/* Unit */}
            <div>
              <label className="block text-xs font-medium text-slate-600 mb-1">Unit *</label>
              <input
                value={form.unit}
                onChange={(e) => setForm((f) => ({ ...f, unit: e.target.value }))}
                placeholder="ream, box, piece, liter…"
                className="w-full px-3 py-2 text-sm border border-slate-200 focus:outline-none focus:ring-2 focus:ring-green-600"
              />
              <p className="mt-1 text-[11px] text-slate-400">
                Name + unit together must be unique (e.g. &quot;Bond Paper&quot; can exist per ream AND per box).
              </p>
            </div>

            {/* Category */}
            <div>
              <label className="block text-xs font-medium text-slate-600 mb-1">Category</label>
              <input
                value={form.category}
                onChange={(e) => setForm((f) => ({ ...f, category: e.target.value }))}
                placeholder="Office Supplies"
                className="w-full px-3 py-2 text-sm border border-slate-200 focus:outline-none focus:ring-2 focus:ring-green-600"
              />
            </div>

            {/* Unit Price */}
            <div>
              <label className="block text-xs font-medium text-slate-600 mb-1">Unit Price *</label>
              <MoneyInput
                value={form.unitPrice}
                onChange={(v) => setForm((f) => ({ ...f, unitPrice: v }))}
                className="w-40"
              />
              {editTarget && (
                <p className="mt-1 text-[11px] text-slate-400">
                  Changing this updates &quot;Price Updated&quot; to today.
                </p>
              )}
            </div>

            {formError && (
              <div className="bg-danger-100 border border-danger-500/30 px-4 py-3">
                <p className="text-sm text-danger-500">{formError}</p>
              </div>
            )}
          </div>
        </Modal>
      )}

      {/* ── CSV import confirm ─────────────────────────────────────────────────── */}
      {pendingCsv && (
        <Modal
          title="Import price index from CSV"
          size="sm"
          onClose={() => !importing && setPendingCsv(null)}
          footer={
            <>
              <Modal.SecondaryButton onClick={() => setPendingCsv(null)} disabled={importing}>
                Cancel
              </Modal.SecondaryButton>
              <Modal.PrimaryButton onClick={doImport} loading={importing}>
                Import
              </Modal.PrimaryButton>
            </>
          }
        >
          <div className="space-y-3 text-sm text-slate-600">
            <p>
              Import <span className="font-medium text-slate-800">{pendingCsv.name}</span>?
            </p>
            <p>
              Rows are matched by <span className="font-mono text-xs">name + unit</span>: new
              combinations are added and existing ones are updated. Nothing is deleted.
            </p>
            <p className="text-xs text-slate-400">
              Expected columns: name, unit, unit_price, category, is_active.
            </p>
          </div>
        </Modal>
      )}

      {/* ── CSV import summary ─────────────────────────────────────────────────── */}
      {importResult && (
        <MessageDialog
          title="Import complete"
          variant={importResult.errors.length > 0 ? "warning" : "success"}
          size="md"
          onClose={() => setImportResult(null)}
        >
          <div className="space-y-3">
            <div className="flex gap-4">
              <Stat label="Added" value={importResult.new} tone="green" />
              <Stat label="Updated" value={importResult.updated} tone="blue" />
              <Stat label="Skipped" value={importResult.skipped} tone="slate" />
            </div>
            {importResult.errors.length > 0 && (
              <div>
                <p className="text-xs font-semibold text-amber-500 uppercase tracking-wide mb-1">
                  {importResult.errors.length} row{importResult.errors.length === 1 ? "" : "s"} skipped
                </p>
                <ul className="max-h-40 overflow-y-auto text-xs text-slate-500 list-disc pl-4 space-y-0.5">
                  {importResult.errors.map((e, i) => (
                    <li key={i}>{e}</li>
                  ))}
                </ul>
              </div>
            )}
          </div>
        </MessageDialog>
      )}

      {/* ── Deactivate confirm ─────────────────────────────────────────────────── */}
      {confirm && <ConfirmDialog {...confirm} />}
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

function Stat({ label, value, tone }: { label: string; value: number; tone: "green" | "blue" | "slate" }) {
  const cls: Record<typeof tone, string> = {
    green: "text-green-700",
    blue: "text-info-500",
    slate: "text-slate-500",
  };
  return (
    <div className="flex-1 border border-slate-200 px-3 py-2 text-center">
      <div className={`text-2xl font-bold ${cls[tone]}`}>{value}</div>
      <div className="text-[11px] text-slate-400 uppercase tracking-wide">{label}</div>
    </div>
  );
}
