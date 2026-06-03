"use client";

/**
 * Items Master page — RAL-52.
 * Matches Penpot frame "06 Items Master".
 *
 * Access guard: canAccessInventory permission required.
 * Redirects to /dashboard if the permission is not present.
 *
 * Features:
 *   - TanStack Table v8 data grid with per-column filter row + global search
 *   - ★ NEW badge for items where isNewItem = true
 *   - Add Item modal — creates a new catalog entry
 *   - Edit Item modal — update any field, including clearing the isNewItem flag
 *   - "Mark Reviewed" quick action — clears isNewItem in one click
 *
 * API endpoints (ItemFunctions.cs):
 *   GET  /api/items/master        → list all items
 *   POST /api/items/master        → create item
 *   PUT  /api/items/master/{id}   → update item
 */

import { useCallback, useEffect, useMemo, useRef, useState } from "react";
import { useRouter } from "next/navigation";
import {
  useReactTable,
  getCoreRowModel,
  getFilteredRowModel,
  getSortedRowModel,
  getPaginationRowModel,
  flexRender,
  type ColumnDef,
  type ColumnFiltersState,
  type SortingState,
} from "@tanstack/react-table";
import api from "@/lib/api";
import type {
  CreateItemMasterRequest,
  ItemMasterResponse,
  MeResponse,
  UpdateItemMasterRequest,
} from "@/types";

// ---------------------------------------------------------------------------
// Helpers
// ---------------------------------------------------------------------------

function fmt(n: number) {
  return new Intl.NumberFormat("en-PH", {
    minimumFractionDigits: 2,
    maximumFractionDigits: 2,
  }).format(n);
}

function NewBadge() {
  return (
    <span className="inline-flex items-center gap-1 px-2 py-0.5 rounded-full text-xs font-semibold bg-amber-100 text-amber-700 whitespace-nowrap">
      ★ NEW
    </span>
  );
}

// ---------------------------------------------------------------------------
// Blank forms
// ---------------------------------------------------------------------------

function blankCreate(): CreateItemMasterRequest {
  return {
    stockNo: "",
    description: "",
    unit: "",
    unitCost: 0,
    category: null,
    itemType: null,
    reorderQty: 0,
    remarks: null,
    isNewItem: false,
  };
}

// ---------------------------------------------------------------------------
// Modal
// ---------------------------------------------------------------------------

function Modal({
  title,
  onClose,
  children,
}: {
  title: string;
  onClose: () => void;
  children: React.ReactNode;
}) {
  const backdropRef = useRef<HTMLDivElement>(null);

  function handleBackdrop(e: React.MouseEvent) {
    if (e.target === backdropRef.current) onClose();
  }

  return (
    <div
      ref={backdropRef}
      onClick={handleBackdrop}
      className="fixed inset-0 z-50 flex items-center justify-center bg-black/40 p-4"
    >
      <div className="w-full max-w-2xl bg-white rounded-xl shadow-2xl flex flex-col max-h-[90vh]">
        <div className="flex items-center justify-between px-6 py-4 border-b border-slate-200 shrink-0">
          <h2 className="text-base font-semibold text-slate-800">{title}</h2>
          <button
            onClick={onClose}
            className="text-slate-400 hover:text-slate-600 transition-colors text-xl leading-none"
            aria-label="Close"
          >
            ×
          </button>
        </div>
        <div className="overflow-y-auto flex-1 px-6 py-5">{children}</div>
      </div>
    </div>
  );
}

// ---------------------------------------------------------------------------
// Item form (shared between Add and Edit)
// ---------------------------------------------------------------------------

type ItemFormData = CreateItemMasterRequest | UpdateItemMasterRequest;

function ItemForm({
  form,
  saving,
  error,
  isEdit,
  onChange,
  onSubmit,
  onCancel,
}: {
  form: ItemFormData;
  saving: boolean;
  error: string | null;
  isEdit: boolean;
  onChange: (patch: Partial<ItemFormData>) => void;
  onSubmit: () => void;
  onCancel: () => void;
}) {
  const f = form as unknown as Record<string, unknown>;

  function str(key: string): string {
    const v = f[key];
    return v == null ? "" : String(v);
  }

  return (
    <div className="space-y-4">
      <div className="grid grid-cols-2 gap-3">
        {/* Stock No */}
        <div>
          <label className="block text-xs font-medium text-slate-600 mb-1">
            Stock No. *
          </label>
          <input
            value={str("stockNo")}
            onChange={(e) => onChange({ stockNo: e.target.value })}
            placeholder="e.g. SUP-001"
            className="w-full px-3 py-2 rounded-lg text-sm border border-slate-200 focus:outline-none focus:ring-2 focus:ring-green-600"
          />
        </div>

        {/* Unit */}
        <div>
          <label className="block text-xs font-medium text-slate-600 mb-1">
            Unit *
          </label>
          <input
            value={str("unit")}
            onChange={(e) => onChange({ unit: e.target.value })}
            placeholder="e.g. ream, box, piece"
            className="w-full px-3 py-2 rounded-lg text-sm border border-slate-200 focus:outline-none focus:ring-2 focus:ring-green-600"
          />
        </div>

        {/* Description */}
        <div className="col-span-2">
          <label className="block text-xs font-medium text-slate-600 mb-1">
            Description *
          </label>
          <input
            value={str("description")}
            onChange={(e) => onChange({ description: e.target.value })}
            placeholder="Full item name / description"
            className="w-full px-3 py-2 rounded-lg text-sm border border-slate-200 focus:outline-none focus:ring-2 focus:ring-green-600"
          />
        </div>

        {/* Category */}
        <div>
          <label className="block text-xs font-medium text-slate-600 mb-1">
            Category
          </label>
          <input
            value={str("category")}
            onChange={(e) =>
              onChange({ category: e.target.value || null })
            }
            placeholder="e.g. Office Supplies"
            className="w-full px-3 py-2 rounded-lg text-sm border border-slate-200 focus:outline-none focus:ring-2 focus:ring-green-600"
          />
        </div>

        {/* Item Type */}
        <div>
          <label className="block text-xs font-medium text-slate-600 mb-1">
            Item Type
          </label>
          <input
            value={str("itemType")}
            onChange={(e) =>
              onChange({ itemType: e.target.value || null })
            }
            placeholder="e.g. Consumable"
            className="w-full px-3 py-2 rounded-lg text-sm border border-slate-200 focus:outline-none focus:ring-2 focus:ring-green-600"
          />
        </div>

        {/* Unit Cost */}
        <div>
          <label className="block text-xs font-medium text-slate-600 mb-1">
            Unit Cost (₱)
          </label>
          <input
            type="number"
            min={0}
            step={0.01}
            value={f["unitCost"] == null ? "" : String(f["unitCost"])}
            onChange={(e) =>
              onChange({ unitCost: parseFloat(e.target.value) || 0 })
            }
            className="w-full px-3 py-2 rounded-lg text-sm border border-slate-200 focus:outline-none focus:ring-2 focus:ring-green-600"
          />
        </div>

        {/* Reorder Qty */}
        <div>
          <label className="block text-xs font-medium text-slate-600 mb-1">
            Reorder Qty
          </label>
          <input
            type="number"
            min={0}
            step={1}
            value={f["reorderQty"] == null ? "" : String(f["reorderQty"])}
            onChange={(e) =>
              onChange({ reorderQty: parseInt(e.target.value) || 0 })
            }
            className="w-full px-3 py-2 rounded-lg text-sm border border-slate-200 focus:outline-none focus:ring-2 focus:ring-green-600"
          />
        </div>

        {/* Remarks */}
        <div className="col-span-2">
          <label className="block text-xs font-medium text-slate-600 mb-1">
            Remarks
          </label>
          <input
            value={str("remarks")}
            onChange={(e) =>
              onChange({ remarks: e.target.value || null })
            }
            placeholder="Optional notes"
            className="w-full px-3 py-2 rounded-lg text-sm border border-slate-200 focus:outline-none focus:ring-2 focus:ring-green-600"
          />
        </div>

        {/* isNewItem toggle — edit only */}
        {isEdit && (
          <div className="col-span-2 flex items-center gap-3 py-1">
            <span className="text-xs font-medium text-slate-600">
              ★ NEW Flag
            </span>
            <button
              type="button"
              onClick={() => onChange({ isNewItem: !f["isNewItem"] })}
              className={`relative inline-flex h-6 w-11 items-center rounded-full transition-colors focus:outline-none focus:ring-2 focus:ring-amber-400 ${
                f["isNewItem"] ? "bg-amber-400" : "bg-slate-300"
              }`}
            >
              <span
                className={`inline-block h-4 w-4 rounded-full bg-white shadow transform transition-transform ${
                  f["isNewItem"] ? "translate-x-6" : "translate-x-1"
                }`}
              />
            </button>
            <span className="text-xs text-slate-500">
              {f["isNewItem"]
                ? "Flagged as NEW — pending admin review"
                : "Reviewed / active item"}
            </span>
          </div>
        )}
      </div>

      {error && (
        <div className="rounded-lg bg-red-50 border border-red-200 px-4 py-3">
          <p className="text-sm text-red-600">{error}</p>
        </div>
      )}

      <div className="flex justify-end gap-3 pt-2">
        <button
          type="button"
          onClick={onCancel}
          className="px-4 py-2 text-sm rounded-lg border border-slate-200 text-slate-600 hover:bg-slate-50 transition-colors"
        >
          Cancel
        </button>
        <button
          type="button"
          onClick={onSubmit}
          disabled={saving}
          className="px-5 py-2 text-sm rounded-lg bg-green-600 text-white font-medium hover:bg-green-500 transition-colors disabled:opacity-60 disabled:cursor-not-allowed flex items-center gap-2"
        >
          {saving && (
            <span className="w-4 h-4 border-2 border-white border-t-transparent rounded-full animate-spin" />
          )}
          {saving ? "Saving…" : isEdit ? "Save Changes" : "Add Item"}
        </button>
      </div>
    </div>
  );
}

// ---------------------------------------------------------------------------
// Page
// ---------------------------------------------------------------------------

export default function ItemsMasterPage() {
  const router = useRouter();

  // Auth guard
  const [authChecked, setAuthChecked] = useState(false);

  // Data
  const [items, setItems] = useState<ItemMasterResponse[]>([]);
  const [loading, setLoading] = useState(true);
  const [fetchError, setFetchError] = useState<string | null>(null);

  // TanStack Table state
  const [globalFilter, setGlobalFilter] = useState("");
  const [columnFilters, setColumnFilters] = useState<ColumnFiltersState>([]);
  const [sorting, setSorting] = useState<SortingState>([]);
  const [showNewOnly, setShowNewOnly] = useState(false);

  // Modals
  const [showAdd, setShowAdd] = useState(false);
  const [editTarget, setEditTarget] = useState<ItemMasterResponse | null>(null);

  // Form state
  const [addForm, setAddForm] = useState<CreateItemMasterRequest>(blankCreate());
  const [editForm, setEditForm] = useState<UpdateItemMasterRequest | null>(null);
  const [saving, setSaving] = useState(false);
  const [formError, setFormError] = useState<string | null>(null);

  // Quick-action loading
  const [reviewingId, setReviewingId] = useState<string | null>(null);

  // ── Auth guard ─────────────────────────────────────────────────────────────

  useEffect(() => {
    api
      .get<MeResponse>("/auth/me")
      .then(({ data }) => {
        if (!data.canAccessInventory) {
          router.replace("/dashboard");
        } else {
          setAuthChecked(true);
        }
      })
      .catch(() => {
        router.replace("/login");
      });
  }, [router]);

  // ── Load items ─────────────────────────────────────────────────────────────

  const loadItems = useCallback(async () => {
    setLoading(true);
    setFetchError(null);
    try {
      const { data } = await api.get<ItemMasterResponse[]>("/items/master");
      setItems(data);
    } catch {
      setFetchError("Failed to load items. Please try again.");
    } finally {
      setLoading(false);
    }
  }, []);

  useEffect(() => {
    if (authChecked) loadItems();
  }, [authChecked, loadItems]);

  // ── Table columns ──────────────────────────────────────────────────────────

  const columns = useMemo<ColumnDef<ItemMasterResponse>[]>(
    () => [
      {
        id: "rowNo",
        header: "#",
        size: 48,
        enableColumnFilter: false,
        enableSorting: false,
        cell: ({ row }) => (
          <span className="text-slate-400 text-xs">{row.index + 1}</span>
        ),
      },
      {
        accessorKey: "stockNo",
        header: "Stock No.",
        size: 120,
        cell: ({ getValue }) => (
          <span className="font-mono text-xs text-slate-700">
            {getValue<string>()}
          </span>
        ),
      },
      {
        accessorKey: "description",
        header: "Description",
        size: 260,
        cell: ({ row, getValue }) => (
          <div className="flex items-center gap-2">
            <span className="text-slate-800 text-sm">{getValue<string>()}</span>
            {row.original.isNewItem && <NewBadge />}
          </div>
        ),
      },
      {
        accessorKey: "category",
        header: "Category",
        size: 140,
        cell: ({ getValue }) => (
          <span className="text-slate-600 text-sm">
            {getValue<string | null>() ?? "—"}
          </span>
        ),
      },
      {
        accessorKey: "unit",
        header: "Unit",
        size: 80,
        cell: ({ getValue }) => (
          <span className="text-slate-600 text-sm">{getValue<string>()}</span>
        ),
      },
      {
        accessorKey: "unitCost",
        header: "Unit Cost",
        size: 100,
        enableColumnFilter: false,
        cell: ({ getValue }) => (
          <span className="text-slate-700 text-sm tabular-nums">
            ₱{fmt(getValue<number>())}
          </span>
        ),
      },
      {
        accessorKey: "itemType",
        header: "Type",
        size: 110,
        cell: ({ getValue }) => (
          <span className="text-slate-600 text-sm">
            {getValue<string | null>() ?? "—"}
          </span>
        ),
      },
      {
        accessorKey: "reorderQty",
        header: "Reorder",
        size: 80,
        enableColumnFilter: false,
        cell: ({ getValue }) => (
          <span className="text-slate-600 text-sm tabular-nums">
            {getValue<number>()}
          </span>
        ),
      },
      {
        id: "actions",
        header: "",
        size: 100,
        enableColumnFilter: false,
        enableSorting: false,
        cell: ({ row }) => {
          const item = row.original;
          return (
            <div className="flex items-center justify-end gap-1">
              {item.isNewItem && (
                <button
                  title="Mark as reviewed — clears ★ NEW flag"
                  disabled={reviewingId === item.id}
                  onClick={() => handleMarkReviewed(item)}
                  className="p-1.5 rounded-lg text-xs transition-colors hover:bg-amber-50 text-amber-500 hover:text-amber-700 disabled:opacity-40"
                >
                  {reviewingId === item.id ? (
                    <span className="w-3.5 h-3.5 border-2 border-amber-400 border-t-transparent rounded-full animate-spin inline-block" />
                  ) : (
                    "✓"
                  )}
                </button>
              )}
              <button
                title="Edit item"
                onClick={() => openEdit(item)}
                className="p-1.5 rounded-lg text-xs transition-colors hover:bg-green-50 text-slate-400 hover:text-green-700"
              >
                ✏️
              </button>
            </div>
          );
        },
      },
    ],
    [reviewingId] // eslint-disable-line react-hooks/exhaustive-deps
  );

  // Apply showNewOnly filter before passing to table
  const tableData = useMemo(
    () => (showNewOnly ? items.filter((i) => i.isNewItem) : items),
    [items, showNewOnly]
  );

  const table = useReactTable({
    data: tableData,
    columns,
    state: { globalFilter, columnFilters, sorting },
    onGlobalFilterChange: setGlobalFilter,
    onColumnFiltersChange: setColumnFilters,
    onSortingChange: setSorting,
    getCoreRowModel: getCoreRowModel(),
    getFilteredRowModel: getFilteredRowModel(),
    getSortedRowModel: getSortedRowModel(),
    getPaginationRowModel: getPaginationRowModel(),
    initialState: { pagination: { pageSize: 25 } },
    globalFilterFn: "includesString",
  });

  // ── Handlers — Add ─────────────────────────────────────────────────────────

  function openAdd() {
    setAddForm(blankCreate());
    setFormError(null);
    setShowAdd(true);
  }

  async function handleAdd() {
    if (!addForm.stockNo.trim() || !addForm.description.trim() || !addForm.unit.trim()) {
      setFormError("Stock No., Description, and Unit are required.");
      return;
    }
    setSaving(true);
    setFormError(null);
    try {
      await api.post("/items/master", addForm);
      setShowAdd(false);
      await loadItems();
    } catch (e: unknown) {
      const msg = (e as { response?: { data?: { message?: string } } })?.response?.data?.message;
      setFormError(msg ?? "Failed to add item. Please try again.");
    } finally {
      setSaving(false);
    }
  }

  // ── Handlers — Edit ────────────────────────────────────────────────────────

  function openEdit(item: ItemMasterResponse) {
    setEditTarget(item);
    setEditForm({
      stockNo: item.stockNo,
      description: item.description,
      unit: item.unit,
      unitCost: item.unitCost,
      category: item.category,
      itemType: item.itemType,
      reorderQty: item.reorderQty,
      remarks: item.remarks,
      isNewItem: item.isNewItem,
    });
    setFormError(null);
  }

  async function handleEdit() {
    if (!editTarget || !editForm) return;
    const f = editForm as unknown as Record<string, unknown>;
    const sn = String(f["stockNo"] ?? "").trim();
    const desc = String(f["description"] ?? "").trim();
    const unit = String(f["unit"] ?? "").trim();
    if (!sn || !desc || !unit) {
      setFormError("Stock No., Description, and Unit are required.");
      return;
    }
    setSaving(true);
    setFormError(null);
    try {
      await api.put(`/items/master/${editTarget.id}`, editForm);
      setEditTarget(null);
      setEditForm(null);
      await loadItems();
    } catch (e: unknown) {
      const msg = (e as { response?: { data?: { message?: string } } })?.response?.data?.message;
      setFormError(msg ?? "Failed to update item. Please try again.");
    } finally {
      setSaving(false);
    }
  }

  // ── Handlers — Mark Reviewed ───────────────────────────────────────────────

  async function handleMarkReviewed(item: ItemMasterResponse) {
    setReviewingId(item.id);
    try {
      await api.put(`/items/master/${item.id}`, {
        stockNo: item.stockNo,
        description: item.description,
        unit: item.unit,
        unitCost: item.unitCost,
        category: item.category,
        itemType: item.itemType,
        reorderQty: item.reorderQty,
        remarks: item.remarks,
        isNewItem: false,
      } satisfies UpdateItemMasterRequest);
      await loadItems();
    } catch {
      // silently fail — user can retry via Edit modal
    } finally {
      setReviewingId(null);
    }
  }

  // ── Loading / auth guard ───────────────────────────────────────────────────

  if (!authChecked) {
    return (
      <div className="min-h-screen flex items-center justify-center bg-slate-100">
        <div className="w-8 h-8 border-4 border-green-600 border-t-transparent rounded-full animate-spin" />
      </div>
    );
  }

  // ── Derived counts ─────────────────────────────────────────────────────────

  const newCount = items.filter((i) => i.isNewItem).length;
  const visibleRows = table.getRowModel().rows.length;
  const totalFiltered = table.getFilteredRowModel().rows.length;

  // ── Render ─────────────────────────────────────────────────────────────────

  return (
    <div className="min-h-screen bg-slate-100 font-sans">
      <div className="max-w-screen-xl mx-auto px-6 py-6 space-y-4">

        {/* ── Toolbar ──────────────────────────────────────────────────────── */}
        <div className="flex flex-wrap items-center gap-3">
          {/* Global search */}
          <input
            value={globalFilter}
            onChange={(e) => setGlobalFilter(e.target.value)}
            placeholder="Search items…"
            className="flex-1 min-w-48 px-4 py-2.5 rounded-lg text-sm border border-slate-200 bg-white shadow-sm focus:outline-none focus:ring-2 focus:ring-green-600"
          />
          {globalFilter && (
            <button
              onClick={() => setGlobalFilter("")}
              className="text-sm text-slate-400 hover:text-slate-600 transition-colors px-2"
            >
              Clear
            </button>
          )}

          {/* ★ NEW filter toggle */}
          <button
            onClick={() => setShowNewOnly((v) => !v)}
            className={`flex items-center gap-1.5 px-3 py-2.5 rounded-lg text-sm border transition-colors shrink-0 ${
              showNewOnly
                ? "bg-amber-100 border-amber-300 text-amber-700 font-semibold"
                : "bg-white border-slate-200 text-slate-600 hover:bg-slate-50"
            }`}
          >
            ★ NEW
            {newCount > 0 && (
              <span
                className={`inline-flex items-center justify-center w-5 h-5 rounded-full text-xs font-bold ${
                  showNewOnly
                    ? "bg-amber-400 text-white"
                    : "bg-amber-100 text-amber-700"
                }`}
              >
                {newCount}
              </span>
            )}
          </button>

          {/* Add item */}
          <button
            onClick={openAdd}
            className="flex items-center gap-1.5 bg-green-600 text-white font-semibold text-sm px-4 py-2.5 rounded-lg hover:bg-green-500 transition-colors shadow-sm shrink-0"
          >
            <span className="text-base leading-none">+</span>
            Add Item
          </button>
        </div>

        {/* ── Table card ───────────────────────────────────────────────────── */}
        <div className="bg-white rounded-xl shadow-sm border border-slate-200 overflow-hidden">
          {loading ? (
            <div className="flex items-center justify-center py-20">
              <div className="w-8 h-8 border-4 border-green-600 border-t-transparent rounded-full animate-spin" />
            </div>
          ) : fetchError ? (
            <div className="flex flex-col items-center justify-center py-20 gap-3">
              <p className="text-sm text-red-500">{fetchError}</p>
              <button
                onClick={loadItems}
                className="text-sm text-green-600 hover:underline"
              >
                Retry
              </button>
            </div>
          ) : (
            <div className="overflow-x-auto">
              <table className="w-full text-sm border-collapse">
                {/* ── Headers ── */}
                <thead>
                  {table.getHeaderGroups().map((hg) => (
                    <tr
                      key={hg.id}
                      className="bg-slate-50 border-b border-slate-200 text-xs text-slate-500 uppercase tracking-wide"
                    >
                      {hg.headers.map((header) => (
                        <th
                          key={header.id}
                          style={{ width: header.getSize() }}
                          className="text-left px-3 py-2.5 font-medium select-none"
                        >
                          {header.isPlaceholder ? null : (
                            <div
                              className={
                                header.column.getCanSort()
                                  ? "cursor-pointer flex items-center gap-1 hover:text-slate-700"
                                  : ""
                              }
                              onClick={header.column.getToggleSortingHandler()}
                            >
                              {flexRender(
                                header.column.columnDef.header,
                                header.getContext()
                              )}
                              {header.column.getCanSort() && (
                                <span className="text-slate-300">
                                  {header.column.getIsSorted() === "asc"
                                    ? " ▲"
                                    : header.column.getIsSorted() === "desc"
                                    ? " ▼"
                                    : " ⇅"}
                                </span>
                              )}
                            </div>
                          )}
                        </th>
                      ))}
                    </tr>
                  ))}

                  {/* ── Filter row ── */}
                  {table.getHeaderGroups().map((hg) => (
                    <tr
                      key={`filter-${hg.id}`}
                      className="bg-white border-b border-slate-100"
                    >
                      {hg.headers.map((header) => (
                        <th key={`f-${header.id}`} className="px-2 py-1.5">
                          {header.column.getCanFilter() ? (
                            <input
                              value={
                                (header.column.getFilterValue() as string) ?? ""
                              }
                              onChange={(e) =>
                                header.column.setFilterValue(e.target.value)
                              }
                              placeholder="Filter…"
                              className="w-full px-2 py-1 text-xs rounded border border-slate-200 bg-slate-50 focus:outline-none focus:ring-1 focus:ring-green-500 focus:bg-white transition-colors"
                            />
                          ) : null}
                        </th>
                      ))}
                    </tr>
                  ))}
                </thead>

                {/* ── Body ── */}
                <tbody className="divide-y divide-slate-100">
                  {table.getRowModel().rows.length === 0 ? (
                    <tr>
                      <td
                        colSpan={columns.length}
                        className="text-center py-16 text-slate-400 text-sm"
                      >
                        {globalFilter || columnFilters.length > 0 || showNewOnly
                          ? "No items match your filters."
                          : "No items in the catalog yet."}
                      </td>
                    </tr>
                  ) : (
                    table.getRowModel().rows.map((row, i) => (
                      <tr
                        key={row.id}
                        className={`transition-colors hover:bg-green-50 ${
                          row.original.isNewItem
                            ? "bg-amber-50"
                            : i % 2 === 1
                            ? "bg-slate-50"
                            : "bg-white"
                        }`}
                      >
                        {row.getVisibleCells().map((cell) => (
                          <td
                            key={cell.id}
                            className="px-3 py-2.5 align-middle"
                          >
                            {flexRender(
                              cell.column.columnDef.cell,
                              cell.getContext()
                            )}
                          </td>
                        ))}
                      </tr>
                    ))
                  )}
                </tbody>
              </table>

              {/* ── Status bar + pagination ── */}
              <div className="flex items-center justify-between px-4 py-2 border-t border-slate-100 text-xs text-slate-400 flex-wrap gap-2">
                <span>
                  {visibleRows === totalFiltered
                    ? `${totalFiltered} item${totalFiltered !== 1 ? "s" : ""}`
                    : `${visibleRows} of ${totalFiltered} items`}
                  {newCount > 0 && (
                    <span className="ml-2 text-amber-600 font-medium">
                      · {newCount} pending review
                    </span>
                  )}
                </span>

                <div className="flex items-center gap-2">
                  <button
                    onClick={() => table.previousPage()}
                    disabled={!table.getCanPreviousPage()}
                    className="px-2 py-0.5 rounded border border-slate-200 disabled:opacity-40 hover:bg-slate-50 transition-colors"
                  >
                    ‹
                  </button>
                  <span>
                    Page {table.getState().pagination.pageIndex + 1} /{" "}
                    {table.getPageCount() || 1}
                  </span>
                  <button
                    onClick={() => table.nextPage()}
                    disabled={!table.getCanNextPage()}
                    className="px-2 py-0.5 rounded border border-slate-200 disabled:opacity-40 hover:bg-slate-50 transition-colors"
                  >
                    ›
                  </button>
                  <select
                    value={table.getState().pagination.pageSize}
                    onChange={(e) => table.setPageSize(Number(e.target.value))}
                    className="px-2 py-0.5 rounded border border-slate-200 bg-white focus:outline-none text-xs"
                  >
                    {[25, 50, 100].map((n) => (
                      <option key={n} value={n}>
                        {n} / page
                      </option>
                    ))}
                  </select>
                </div>
              </div>
            </div>
          )}
        </div>
      </div>

      {/* ── Add Item modal ──────────────────────────────────────────────────── */}
      {showAdd && (
        <Modal title="Add New Item" onClose={() => setShowAdd(false)}>
          <ItemForm
            form={addForm}
            saving={saving}
            error={formError}
            isEdit={false}
            onChange={(patch) =>
              setAddForm((f) => ({ ...f, ...patch } as CreateItemMasterRequest))
            }
            onSubmit={handleAdd}
            onCancel={() => setShowAdd(false)}
          />
        </Modal>
      )}

      {/* ── Edit Item modal ─────────────────────────────────────────────────── */}
      {editTarget && editForm && (
        <Modal
          title={`Edit Item — ${editTarget.stockNo}`}
          onClose={() => {
            setEditTarget(null);
            setEditForm(null);
          }}
        >
          <ItemForm
            form={editForm}
            saving={saving}
            error={formError}
            isEdit
            onChange={(patch) =>
              setEditForm((f) =>
                f ? ({ ...f, ...patch } as UpdateItemMasterRequest) : f
              )
            }
            onSubmit={handleEdit}
            onCancel={() => {
              setEditTarget(null);
              setEditForm(null);
            }}
          />
        </Modal>
      )}
    </div>
  );
}
