"use client";

/**
 * Items Master page — RAL-52 (inline-editing, uncontrolled-input fix).
 * Matches Penpot frame "06 Items Master".
 *
 * Access guard: canAccessInventory permission required.
 *
 * Inline editing — focus-loss fix:
 *   Edit values are stored in a useRef (editRef), NOT in useState.
 *   Cell inputs use defaultValue (uncontrolled) and write to editRef on change.
 *   This means the columns useMemo NEVER lists edit values as a dependency,
 *   so the column array is stable across keystrokes → no remount → no focus loss.
 *   The only state that columns depend on: editingId, saving, reviewingId.
 *
 * Table behaviour:
 *   - TanStack Table v8 — per-column filter row, global search, sortable columns
 *   - ✏️ click → row switches to inline edit mode (inputs in cells)
 *   - Only one row editable at a time
 *   - isNewItem rendered as a toggle button in edit mode / ★ NEW badge in display mode
 *   - Remarks column always visible; editable inline
 *   - "+ Add Item" appends a blank sentinel row at the top, immediately in edit mode
 *   - Validation errors appear as a sub-row below the editing row
 *   - Save/API success → toast. API errors on save → toast.
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
import { useToast } from "@/components/ui/Toast";
import type {
  CreateItemMasterRequest,
  ItemMasterResponse,
  MeResponse,
  UpdateItemMasterRequest,
} from "@/types";

// ---------------------------------------------------------------------------
// Constants
// ---------------------------------------------------------------------------

const NEW_ROW_ID = "__new__";

// ---------------------------------------------------------------------------
// Types
// ---------------------------------------------------------------------------

type EditValues = {
  stockNo:     string;
  description: string;
  category:    string;
  unit:        string;
  unitCost:    number;
  itemType:    string;
  reorderQty:  number;
  remarks:     string;
  isNewItem:   boolean;
};

// ---------------------------------------------------------------------------
// Helpers
// ---------------------------------------------------------------------------

function fmt(n: number) {
  return new Intl.NumberFormat("en-PH", {
    minimumFractionDigits: 2,
    maximumFractionDigits: 2,
  }).format(n);
}

function toEditValues(item: ItemMasterResponse): EditValues {
  return {
    stockNo:     item.stockNo,
    description: item.description,
    category:    item.category   ?? "",
    unit:        item.unit,
    unitCost:    item.unitCost,
    itemType:    item.itemType   ?? "",
    reorderQty:  item.reorderQty,
    remarks:     item.remarks    ?? "",
    isNewItem:   item.isNewItem,
  };
}

function blankSentinel(): ItemMasterResponse {
  return {
    id: NEW_ROW_ID, stockNo: "", description: "", category: null,
    unit: "", unitCost: 0, itemType: null, reorderQty: 0,
    remarks: null, isNewItem: false, createdAt: "", updatedAt: "",
  };
}

// ---------------------------------------------------------------------------
// Uncontrolled text input — writes to a ref field on change.
// defaultValue is set when the row enters edit mode (keyed by editingId).
// ---------------------------------------------------------------------------

function EditCell({
  defaultValue,
  onWrite,
  placeholder,
  type = "text",
}: {
  defaultValue: string | number;
  onWrite: (v: string) => void;
  placeholder?: string;
  type?: "text" | "number";
}) {
  return (
    <input
      type={type}
      defaultValue={defaultValue}
      onChange={(e) => onWrite(e.target.value)}
      placeholder={placeholder}
      className="w-full px-2 py-1 text-xs rounded border border-slate-300 bg-cell-fill focus:outline-none focus:ring-1 focus:ring-green-500 focus:bg-white transition-colors"
    />
  );
}

// ---------------------------------------------------------------------------
// isNewItem toggle — controlled locally; syncs to ref on toggle.
// ---------------------------------------------------------------------------

function NewToggleCell({
  initial,
  onWrite,
}: {
  initial: boolean;
  onWrite: (v: boolean) => void;
}) {
  const [value, setValue] = useState(initial);

  function toggle() {
    const next = !value;
    setValue(next);
    onWrite(next);
  }

  return (
    <button
      type="button"
      onClick={toggle}
      className={`inline-flex items-center gap-1 px-2 py-0.5 rounded-full text-xs font-semibold border transition-colors whitespace-nowrap ${
        value
          ? "bg-amber-100 border-amber-300 text-amber-700"
          : "bg-slate-100 border-slate-300 text-slate-500"
      }`}
    >
      ★ {value ? "NEW" : "—"}
    </button>
  );
}

// ---------------------------------------------------------------------------
// Page
// ---------------------------------------------------------------------------

export default function ItemsMasterPage() {
  const router    = useRouter();
  const { toast } = useToast();

  // Auth guard
  const [authChecked, setAuthChecked] = useState(false);

  // Master data
  const [items, setItems]         = useState<ItemMasterResponse[]>([]);
  const [hasSentinel, setSentinel] = useState(false);
  const [loading, setLoading]     = useState(true);
  const [fetchError, setFetchError] = useState<string | null>(null);

  // ---------------------------------------------------------------------------
  // Inline edit state — ONLY editingId is in React state.
  // Actual field values live in editRef so keystrokes never trigger re-renders.
  // ---------------------------------------------------------------------------
  const [editingId, setEditingId] = useState<string | null>(null);
  const editRef = useRef<EditValues | null>(null);

  const [rowError, setRowError]   = useState<string | null>(null);
  const [saving, setSaving]       = useState(false);
  const [reviewingId, setReviewingId] = useState<string | null>(null);

  // Table filter/sort state
  // searchInput is what the user sees; globalFilter is what the table uses.
  // The 150 ms debounce keeps filtering off the keystroke hot path so the
  // page stays responsive even when the catalog grows beyond 1 000 items.
  const [searchInput, setSearchInput]     = useState("");
  const [globalFilter, setGlobalFilter]   = useState("");
  const [columnFilters, setColumnFilters] = useState<ColumnFiltersState>([]);
  const [sorting, setSorting]             = useState<SortingState>([]);
  const [showNewOnly, setShowNewOnly]     = useState(false);

  useEffect(() => {
    const id = setTimeout(() => setGlobalFilter(searchInput), 150);
    return () => clearTimeout(id);
  }, [searchInput]);

  // ── Auth guard ─────────────────────────────────────────────────────────────

  useEffect(() => {
    api.get<MeResponse>("/auth/me")
      .then(({ data }) => {
        if (!data.canAccessInventory) router.replace(data.officeId != null ? "/budget-planning" : "/dashboard");
        else setAuthChecked(true);
      })
      .catch(() => router.replace("/login"));
  }, [router]);

  // ── Load ───────────────────────────────────────────────────────────────────

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

  // ── Table data ─────────────────────────────────────────────────────────────

  const tableData = useMemo<ItemMasterResponse[]>(() => {
    const base = showNewOnly ? items.filter((i) => i.isNewItem) : items;
    return hasSentinel ? [blankSentinel(), ...base] : base;
  }, [items, hasSentinel, showNewOnly]);

  // ── Edit helpers ───────────────────────────────────────────────────────────

  function startEdit(item: ItemMasterResponse) {
    if (hasSentinel && item.id !== NEW_ROW_ID) discardSentinel();
    editRef.current = toEditValues(item);
    setEditingId(item.id);
    setRowError(null);
  }

  function cancelEdit() {
    if (editingId === NEW_ROW_ID) discardSentinel();
    editRef.current = null;
    setEditingId(null);
    setRowError(null);
  }

  function discardSentinel() {
    setSentinel(false);
    editRef.current = null;
    setEditingId(null);
    setRowError(null);
  }

  // ── Validate (reads from ref) ──────────────────────────────────────────────

  function validate(): string | null {
    const v = editRef.current;
    if (!v) return "No edit values.";
    if (!v.stockNo.trim())     return "Stock No. is required.";
    if (!v.description.trim()) return "Description is required.";
    if (!v.unit.trim())        return "Unit is required.";
    return null;
  }

  // ── Save ───────────────────────────────────────────────────────────────────

  async function handleSave() {
    const err = validate();
    if (err) { setRowError(err); return; }

    const v = editRef.current!;
    setSaving(true);
    setRowError(null);

    const body = {
      stockNo:     v.stockNo.trim(),
      description: v.description.trim(),
      unit:        v.unit.trim(),
      unitCost:    v.unitCost,
      category:    v.category.trim()  || null,
      itemType:    v.itemType.trim()  || null,
      reorderQty:  v.reorderQty,
      remarks:     v.remarks.trim()   || null,
      isNewItem:   v.isNewItem,
    };

    try {
      if (editingId === NEW_ROW_ID) {
        await api.post("/items/master", body satisfies CreateItemMasterRequest);
        setSentinel(false);
        toast.success("Item added", `${body.description} was added to the catalog.`);
      } else {
        await api.put(`/items/master/${editingId}`, body satisfies UpdateItemMasterRequest);
        toast.success("Changes saved", `${body.description} was updated.`);
      }
      editRef.current = null;
      setEditingId(null);
      await loadItems();
    } catch (e: unknown) {
      const msg =
        (e as { response?: { data?: { message?: string } } })?.response?.data?.message
        ?? "Failed to save. Please try again.";
      toast.error("Save failed", msg);
    } finally {
      setSaving(false);
    }
  }

  // ── Mark Reviewed ──────────────────────────────────────────────────────────

  async function handleMarkReviewed(item: ItemMasterResponse) {
    setReviewingId(item.id);
    try {
      await api.put(`/items/master/${item.id}`, {
        stockNo: item.stockNo, description: item.description,
        unit: item.unit, unitCost: item.unitCost, category: item.category,
        itemType: item.itemType, reorderQty: item.reorderQty,
        remarks: item.remarks, isNewItem: false,
      } satisfies UpdateItemMasterRequest);
      toast.success("Marked as reviewed", `${item.description} is no longer flagged as NEW.`);
      await loadItems();
    } catch {
      toast.error("Failed to update", "Could not clear the ★ NEW flag. Please try again.");
    } finally {
      setReviewingId(null);
    }
  }

  // ── Add new row ────────────────────────────────────────────────────────────

  function handleAddRow() {
    if (editingId) return;
    editRef.current = toEditValues(blankSentinel());
    setSentinel(true);
    setEditingId(NEW_ROW_ID);
    setRowError(null);
    window.scrollTo({ top: 0, behavior: "smooth" });
  }

  // ── Columns — STABLE: no editValues dependency ────────────────────────────
  //
  // Inputs use defaultValue (uncontrolled). Their key is `${editingId}-<field>`
  // so React resets them only when we switch which row is being edited,
  // not on every keystroke.

  const columns = useMemo<ColumnDef<ItemMasterResponse>[]>(
    () => [
      // # row number
      {
        id: "rowNo",
        header: "#",
        size: 40,
        enableColumnFilter: false,
        enableSorting: false,
        cell: ({ row }) =>
          row.original.id === editingId ? null : (
            <span className="text-slate-400 text-xs">{row.index + 1}</span>
          ),
      },
      // Stock No.
      {
        accessorKey: "stockNo",
        header: "Stock No.",
        size: 110,
        cell: ({ row, getValue }) =>
          row.original.id === editingId ? (
            <EditCell
              key={`${editingId}-stockNo`}
              defaultValue={editRef.current?.stockNo ?? ""}
              onWrite={(v) => { if (editRef.current) editRef.current.stockNo = v; }}
              placeholder="SUP-001"
            />
          ) : (
            <span className="font-mono text-xs text-slate-700">{getValue<string>()}</span>
          ),
      },
      // Description
      {
        accessorKey: "description",
        header: "Description",
        size: 210,
        cell: ({ row, getValue }) =>
          row.original.id === editingId ? (
            <EditCell
              key={`${editingId}-description`}
              defaultValue={editRef.current?.description ?? ""}
              onWrite={(v) => { if (editRef.current) editRef.current.description = v; }}
              placeholder="Full description"
            />
          ) : (
            <div className="flex items-center gap-2">
              <span className="text-slate-800 text-sm">{getValue<string>()}</span>
              {row.original.isNewItem && (
                <span className="inline-flex items-center gap-1 px-2 py-0.5 rounded-full text-xs font-semibold bg-amber-100 text-amber-700 whitespace-nowrap">
                  ★ NEW
                </span>
              )}
            </div>
          ),
      },
      // Category
      {
        accessorKey: "category",
        header: "Category",
        size: 120,
        cell: ({ row, getValue }) =>
          row.original.id === editingId ? (
            <EditCell
              key={`${editingId}-category`}
              defaultValue={editRef.current?.category ?? ""}
              onWrite={(v) => { if (editRef.current) editRef.current.category = v; }}
              placeholder="Office Supplies"
            />
          ) : (
            <span className="text-slate-600 text-sm">{getValue<string | null>() ?? "—"}</span>
          ),
      },
      // Unit
      {
        accessorKey: "unit",
        header: "Unit",
        size: 72,
        cell: ({ row, getValue }) =>
          row.original.id === editingId ? (
            <EditCell
              key={`${editingId}-unit`}
              defaultValue={editRef.current?.unit ?? ""}
              onWrite={(v) => { if (editRef.current) editRef.current.unit = v; }}
              placeholder="ream"
            />
          ) : (
            <span className="text-slate-600 text-sm">{getValue<string>()}</span>
          ),
      },
      // Unit Cost
      {
        accessorKey: "unitCost",
        header: "Unit Cost",
        size: 96,
        enableColumnFilter: false,
        cell: ({ row, getValue }) =>
          row.original.id === editingId ? (
            <EditCell
              key={`${editingId}-unitCost`}
              type="number"
              defaultValue={editRef.current?.unitCost ?? 0}
              onWrite={(v) => { if (editRef.current) editRef.current.unitCost = parseFloat(v) || 0; }}
            />
          ) : (
            <span className="text-slate-700 text-sm tabular-nums">₱{fmt(getValue<number>())}</span>
          ),
      },
      // Item Type
      {
        accessorKey: "itemType",
        header: "Type",
        size: 100,
        cell: ({ row, getValue }) =>
          row.original.id === editingId ? (
            <EditCell
              key={`${editingId}-itemType`}
              defaultValue={editRef.current?.itemType ?? ""}
              onWrite={(v) => { if (editRef.current) editRef.current.itemType = v; }}
              placeholder="Consumable"
            />
          ) : (
            <span className="text-slate-600 text-sm">{getValue<string | null>() ?? "—"}</span>
          ),
      },
      // Reorder Qty
      {
        accessorKey: "reorderQty",
        header: "Reorder",
        size: 72,
        enableColumnFilter: false,
        cell: ({ row, getValue }) =>
          row.original.id === editingId ? (
            <EditCell
              key={`${editingId}-reorderQty`}
              type="number"
              defaultValue={editRef.current?.reorderQty ?? 0}
              onWrite={(v) => { if (editRef.current) editRef.current.reorderQty = parseInt(v) || 0; }}
            />
          ) : (
            <span className="text-slate-600 text-sm tabular-nums">{getValue<number>()}</span>
          ),
      },
      // Remarks — wider column
      {
        accessorKey: "remarks",
        header: "Remarks",
        size: 240,
        cell: ({ row, getValue }) =>
          row.original.id === editingId ? (
            <EditCell
              key={`${editingId}-remarks`}
              defaultValue={editRef.current?.remarks ?? ""}
              onWrite={(v) => { if (editRef.current) editRef.current.remarks = v; }}
              placeholder="Optional notes"
            />
          ) : (
            <span
              className="text-slate-500 text-xs truncate block max-w-[230px]"
              title={getValue<string | null>() ?? ""}
            >
              {getValue<string | null>() ?? "—"}
            </span>
          ),
      },
      // Status / isNewItem toggle — narrower column
      {
        accessorKey: "isNewItem",
        header: "New?",
        size: 66,
        enableColumnFilter: false,
        enableSorting: false,
        cell: ({ row }) =>
          row.original.id === editingId ? (
            <NewToggleCell
              key={`${editingId}-isNewItem`}
              initial={editRef.current?.isNewItem ?? false}
              onWrite={(v) => { if (editRef.current) editRef.current.isNewItem = v; }}
            />
          ) : null, // badge shown inside Description in display mode
      },
      // Actions
      {
        id: "actions",
        header: "",
        size: 110,
        enableColumnFilter: false,
        enableSorting: false,
        cell: ({ row }) => {
          const item = row.original;
          const isEditing = item.id === editingId;

          if (isEditing) {
            return (
              <div className="flex items-center gap-1 justify-end">
                <button
                  onClick={handleSave}
                  disabled={saving}
                  className="flex items-center gap-1 px-2.5 py-1 text-xs rounded-lg bg-green-600 text-white font-medium hover:bg-green-500 disabled:opacity-60 transition-colors"
                >
                  {saving
                    ? <span className="w-3 h-3 border-2 border-white border-t-transparent rounded-full animate-spin" />
                    : "✓"}
                  Save
                </button>
                <button
                  onClick={cancelEdit}
                  disabled={saving}
                  className="px-2.5 py-1 text-xs rounded-lg border border-slate-200 text-slate-600 hover:bg-slate-50 disabled:opacity-60 transition-colors"
                >
                  ✕
                </button>
              </div>
            );
          }

          return (
            <div className="flex items-center justify-end gap-1">
              {item.isNewItem && (
                <button
                  title="Mark as reviewed — clears ★ NEW flag"
                  disabled={reviewingId === item.id || !!editingId}
                  onClick={() => handleMarkReviewed(item)}
                  className="p-1.5 rounded-lg text-xs transition-colors hover:bg-amber-50 text-amber-500 hover:text-amber-700 disabled:opacity-40"
                >
                  {reviewingId === item.id
                    ? <span className="w-3.5 h-3.5 border-2 border-amber-400 border-t-transparent rounded-full animate-spin inline-block" />
                    : "✓"}
                </button>
              )}
              <button
                title="Edit row"
                disabled={!!editingId}
                onClick={() => startEdit(item)}
                className="p-1.5 rounded-lg text-xs transition-colors hover:bg-green-50 text-slate-400 hover:text-green-700 disabled:opacity-40"
              >
                ✏️
              </button>
            </div>
          );
        },
      },
    ],
    // ⚠️ Do NOT add editValues / editRef.current here — they must stay out of
    // deps to keep columns stable across keystrokes.
    // eslint-disable-next-line react-hooks/exhaustive-deps
    [editingId, saving, reviewingId]
  );

  // ── Table instance ─────────────────────────────────────────────────────────

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

  // ── Derived ────────────────────────────────────────────────────────────────

  const newCount      = items.filter((i) => i.isNewItem).length;
  const visibleRows   = table.getRowModel().rows.length;
  const totalFiltered = table.getFilteredRowModel().rows.length;

  // ── Loading / auth ─────────────────────────────────────────────────────────

  if (!authChecked) {
    return (
      <div className="min-h-screen flex items-center justify-center bg-slate-100">
        <div className="w-8 h-8 border-4 border-green-600 border-t-transparent rounded-full animate-spin" />
      </div>
    );
  }

  // ── Render ─────────────────────────────────────────────────────────────────

  return (
    <div className="min-h-screen bg-slate-100 font-sans">
      <div className="max-w-screen-xl mx-auto px-6 py-6 space-y-4">

        {/* ── Toolbar ──────────────────────────────────────────────────────── */}
        <div className="flex flex-wrap items-center gap-3">
          <input
            value={searchInput}
            onChange={(e) => setSearchInput(e.target.value)}
            placeholder="Search items…"
            className="flex-1 min-w-48 px-4 py-2.5 rounded-lg text-sm border border-slate-200 bg-white shadow-sm focus:outline-none focus:ring-2 focus:ring-green-600"
          />
          {searchInput && (
            <button
              onClick={() => { setSearchInput(""); setGlobalFilter(""); }}
              className="text-sm text-slate-400 hover:text-slate-600 px-2"
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
              <span className={`inline-flex items-center justify-center w-5 h-5 rounded-full text-xs font-bold ${
                showNewOnly ? "bg-amber-400 text-white" : "bg-amber-100 text-amber-700"
              }`}>
                {newCount}
              </span>
            )}
          </button>

          {/* Add item */}
          <button
            onClick={handleAddRow}
            disabled={!!editingId}
            className="flex items-center gap-1.5 bg-green-600 text-white font-semibold text-sm px-4 py-2.5 rounded-lg hover:bg-green-500 transition-colors shadow-sm shrink-0 disabled:opacity-60 disabled:cursor-not-allowed"
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
              <button onClick={loadItems} className="text-sm text-green-600 hover:underline">Retry</button>
            </div>
          ) : (
            <div className="overflow-x-auto">
              <table className="w-full text-sm border-collapse">

                {/* ── Header ── */}
                <thead>
                  {table.getHeaderGroups().map((hg) => (
                    <tr key={hg.id} className="bg-slate-50 border-b border-slate-200 text-xs text-slate-500 uppercase tracking-wide">
                      {hg.headers.map((h) => (
                        <th key={h.id} style={{ width: h.getSize() }} className="text-left px-3 py-2.5 font-medium select-none">
                          {h.isPlaceholder ? null : (
                            <div
                              className={h.column.getCanSort() ? "cursor-pointer flex items-center gap-1 hover:text-slate-700" : ""}
                              onClick={h.column.getToggleSortingHandler()}
                            >
                              {flexRender(h.column.columnDef.header, h.getContext())}
                              {h.column.getCanSort() && (
                                <span className="text-slate-300">
                                  {h.column.getIsSorted() === "asc" ? " ▲" : h.column.getIsSorted() === "desc" ? " ▼" : " ⇅"}
                                </span>
                              )}
                            </div>
                          )}
                        </th>
                      ))}
                    </tr>
                  ))}

                  {/* Filter row */}
                  {table.getHeaderGroups().map((hg) => (
                    <tr key={`filter-${hg.id}`} className="bg-white border-b border-slate-100">
                      {hg.headers.map((h) => (
                        <th key={`f-${h.id}`} className="px-2 py-1.5">
                          {h.column.getCanFilter() ? (
                            <input
                              value={(h.column.getFilterValue() as string) ?? ""}
                              onChange={(e) => h.column.setFilterValue(e.target.value)}
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
                      <td colSpan={columns.length} className="text-center py-16 text-slate-400 text-sm">
                        {globalFilter || columnFilters.length > 0 || showNewOnly
                          ? "No items match your filters."
                          : "No items in the catalog yet."}
                      </td>
                    </tr>
                  ) : (
                    table.getRowModel().rows.map((row, i) => {
                      const isEditing = row.original.id === editingId;
                      return (
                        <>
                          <tr
                            key={row.id}
                            className={`transition-colors ${
                              isEditing
                                ? "bg-cell-fill ring-1 ring-inset ring-green-300"
                                : row.original.isNewItem
                                ? "bg-amber-50 hover:bg-amber-100"
                                : i % 2 === 1
                                ? "bg-slate-50 hover:bg-green-50"
                                : "bg-white hover:bg-green-50"
                            }`}
                          >
                            {row.getVisibleCells().map((cell) => (
                              <td key={cell.id} className="px-3 py-2 align-middle">
                                {flexRender(cell.column.columnDef.cell, cell.getContext())}
                              </td>
                            ))}
                          </tr>
                          {/* Inline validation error sub-row */}
                          {isEditing && rowError && (
                            <tr key={`${row.id}-err`}>
                              <td colSpan={columns.length} className="px-3 py-1 bg-red-50 border-t border-red-100">
                                <p className="text-xs text-red-600">{rowError}</p>
                              </td>
                            </tr>
                          )}
                        </>
                      );
                    })
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
                    <span className="ml-2 text-amber-600 font-medium">· {newCount} pending review</span>
                  )}
                  {editingId && (
                    <span className="ml-2 text-green-600 font-medium">· editing…</span>
                  )}
                </span>
                <div className="flex items-center gap-2">
                  <button onClick={() => table.previousPage()} disabled={!table.getCanPreviousPage()} className="px-2 py-0.5 rounded border border-slate-200 disabled:opacity-40 hover:bg-slate-50 transition-colors">‹</button>
                  <span>Page {table.getState().pagination.pageIndex + 1} / {table.getPageCount() || 1}</span>
                  <button onClick={() => table.nextPage()} disabled={!table.getCanNextPage()} className="px-2 py-0.5 rounded border border-slate-200 disabled:opacity-40 hover:bg-slate-50 transition-colors">›</button>
                  <select
                    value={table.getState().pagination.pageSize}
                    onChange={(e) => table.setPageSize(Number(e.target.value))}
                    className="px-2 py-0.5 rounded border border-slate-200 bg-white focus:outline-none text-xs"
                  >
                    {[25, 50, 100].map((n) => <option key={n} value={n}>{n} / page</option>)}
                  </select>
                </div>
              </div>
            </div>
          )}
        </div>

        {editingId && (
          <p className="text-xs text-slate-400 text-right">
            Press <kbd className="px-1.5 py-0.5 rounded bg-slate-200 text-slate-600 font-mono">✕</kbd> to cancel without saving
          </p>
        )}
      </div>
    </div>
  );
}
