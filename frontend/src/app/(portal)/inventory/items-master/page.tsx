"use client";

/**
 * Items Master page — RAL-52 (inline-editing, uncontrolled-input fix).
 * Server-side search/sort/paging — RAL-192 step 3.
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
 *   - TanStack Table v8 — per-column filter row, global search, sortable columns.
 *     Filtering, sorting, and paging are all server-side (RAL-192) via
 *     GET /api/items/master/search. Free-text fields are debounced 300ms.
 *   - ✏️ click → row switches to inline edit mode (inputs in cells)
 *   - Only one row editable at a time
 *   - isNewItem rendered as a toggle button in edit mode / ★ NEW badge in display mode
 *   - Remarks column always visible; editable inline
 *   - "+ Add Item" appends a blank sentinel row at the top, immediately in edit mode
 *   - Validation errors appear as a sub-row below the editing row
 *   - Save/API success → toast. API errors on save → toast.
 *
 * API endpoints (ItemFunctions.cs):
 *   GET  /api/items/master         → full catalog (unused here — see /search below)
 *   GET  /api/items/master/search  → filtered/sorted/paged catalog (this page)
 *   POST /api/items/master         → create item
 *   PUT  /api/items/master/{id}    → update item
 */

import { Fragment, useCallback, useEffect, useMemo, useRef, useState } from "react";
import { useRouter } from "next/navigation";
import {
  useReactTable,
  getCoreRowModel,
  flexRender,
  type ColumnDef,
  type SortingState,
} from "@tanstack/react-table";
import api from "@/lib/api";
import { fetchMe } from "@/lib/me-cache";
import { useToast } from "@/components/ui/Toast";
import TableSkeleton from "@/components/ui/TableSkeleton";
import ConfigPageHeader from "@/components/ui/ConfigPageHeader";
import type {
  CreateItemMasterRequest,
  ItemMasterResponse,
  ItemMasterSearchResult,
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

/** The Items Master page's per-column filter row — mirrors ItemMasterSearchFilterDto's
 * per-field parameters. Keys match each column's accessorKey / TanStack column id. */
interface ColumnFilters {
  stockNo:     string;
  description: string;
  category:    string;
  unit:        string;
  itemType:    string;
  remarks:     string;
}

const EMPTY_COLUMN_FILTERS: ColumnFilters = {
  stockNo: "", description: "", category: "", unit: "", itemType: "", remarks: "",
};

const FILTERABLE_COLUMNS = ["stockNo", "description", "category", "unit", "itemType", "remarks"] as const;

function isFilterableColumn(id: string): id is keyof ColumnFilters {
  return (FILTERABLE_COLUMNS as readonly string[]).includes(id);
}

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
          : "bg-slate-100 border-slate-300 text-slate-600"
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
  const [authChecked] = useState(true);

  // Master data — holds only the CURRENT page's rows (server-paged).
  const [items, setItems]         = useState<ItemMasterResponse[]>([]);
  const [totalCount, setTotalCount] = useState(0);
  const [newCount, setNewCount]   = useState(0); // total ★ NEW across the whole catalog
  const [hasSentinel, setSentinel] = useState(false);
  const [loading, setLoading]     = useState(true);
  const [fetchError, setFetchError] = useState<string | null>(null);
  const [refreshToken, setRefreshToken] = useState(0);

  // ---------------------------------------------------------------------------
  // Inline edit state — ONLY editingId is in React state.
  // Actual field values live in editRef so keystrokes never trigger re-renders.
  // ---------------------------------------------------------------------------
  const [editingId, setEditingId] = useState<string | null>(null);
  const editRef = useRef<EditValues | null>(null);

  const [rowError, setRowError]   = useState<string | null>(null);
  const [saving, setSaving]       = useState(false);
  const [reviewingId, setReviewingId] = useState<string | null>(null);

  // ── Search / filter / sort / page state (server-driven — RAL-192) ──────────

  const [searchInput, setSearchInput]           = useState("");
  const [debouncedSearch, setDebouncedSearch]   = useState("");
  const [columnFilterInput, setColumnFilterInput] = useState<ColumnFilters>(EMPTY_COLUMN_FILTERS);
  const [debouncedColumnFilters, setDebouncedColumnFilters] = useState<ColumnFilters>(EMPTY_COLUMN_FILTERS);
  const [sorting, setSorting]     = useState<SortingState>([]);
  const [showNewOnly, setShowNewOnly] = useState(false);
  const [page, setPage]           = useState(1);
  const [pageSize, setPageSize]   = useState(25);

  // Free-text fields are debounced off the keystroke hot path.
  useEffect(() => {
    const t = setTimeout(() => {
      setDebouncedSearch(searchInput);
      setDebouncedColumnFilters(columnFilterInput);
    }, 300);
    return () => clearTimeout(t);
  }, [searchInput, columnFilterInput]);

  // Reset to page 1 whenever the effective filter/sort set changes — a stale deep
  // page number would otherwise land past the end of a newly-narrowed result set.
  // eslint-disable-next-line react-hooks/exhaustive-deps
  useEffect(() => { setPage(1); }, [debouncedSearch, debouncedColumnFilters, showNewOnly, sorting]);

  // ── Auth guard ─────────────────────────────────────────────────────────────

  useEffect(() => {
    fetchMe()
      .then((data) => {
        if (!data.canAccessInventory) router.replace(data.officeId != null ? "/budget-planning" : "/dashboard");
      })
      .catch(() => router.replace("/login"));
  }, [router]);

  // ── Load (server-side search + sort + page) ─────────────────────────────────

  const reqSeqRef = useRef(0);

  useEffect(() => {
    let cancelled = false;
    const seq = ++reqSeqRef.current;

    setLoading(true);
    setFetchError(null);

    const sort = sorting[0];
    const params: Record<string, string | number | boolean> = { page, pageSize };
    if (debouncedSearch)                    params.search      = debouncedSearch;
    if (debouncedColumnFilters.stockNo)     params.stockNo     = debouncedColumnFilters.stockNo;
    if (debouncedColumnFilters.description) params.description = debouncedColumnFilters.description;
    if (debouncedColumnFilters.category)    params.category    = debouncedColumnFilters.category;
    if (debouncedColumnFilters.unit)        params.unit        = debouncedColumnFilters.unit;
    if (debouncedColumnFilters.itemType)    params.itemType    = debouncedColumnFilters.itemType;
    if (debouncedColumnFilters.remarks)     params.remarks     = debouncedColumnFilters.remarks;
    if (showNewOnly)                        params.isNewOnly   = true;
    if (sort) {
      params.sortBy  = sort.id;
      params.sortDir = sort.desc ? "desc" : "asc";
    }

    api.get<ItemMasterSearchResult>("/items/master/search", { params })
      .then(({ data }) => {
        if (cancelled || seq !== reqSeqRef.current) return;
        setItems(data.items);
        setTotalCount(data.totalCount);
        setNewCount(data.totalNewItemCount);
      })
      .catch(() => {
        if (cancelled || seq !== reqSeqRef.current) return;
        setFetchError("Failed to load items. Please try again.");
      })
      .finally(() => {
        if (cancelled || seq !== reqSeqRef.current) return;
        setLoading(false);
      });

    return () => { cancelled = true; };
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [page, pageSize, debouncedSearch, debouncedColumnFilters, showNewOnly, sorting, refreshToken]);

  const reload = useCallback(() => setRefreshToken((t) => t + 1), []);

  // ── Table data ─────────────────────────────────────────────────────────────

  const tableData = useMemo<ItemMasterResponse[]>(
    () => (hasSentinel ? [blankSentinel(), ...items] : items),
    [items, hasSentinel]
  );

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
        setPage(1);
      } else {
        await api.put(`/items/master/${editingId}`, body satisfies UpdateItemMasterRequest);
        toast.success("Changes saved", `${body.description} was updated.`);
      }
      editRef.current = null;
      setEditingId(null);
      reload();
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
      reload();
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
            <span className="text-slate-600 text-xs">{row.index + 1}</span>
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
              className="text-slate-600 text-xs truncate block max-w-[230px]"
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
                className="p-1.5 rounded-lg text-xs transition-colors hover:bg-green-50 text-slate-600 hover:text-green-700 disabled:opacity-40"
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

  // ── Table instance (server-driven sorting + pagination — RAL-192) ──────────

  const pageCount = Math.max(1, Math.ceil(totalCount / pageSize));

  const table = useReactTable({
    data: tableData,
    columns,
    state: { sorting },
    onSortingChange: setSorting,
    manualSorting: true,
    manualPagination: true,
    pageCount,
    getCoreRowModel: getCoreRowModel(),
  });

  const hasActiveFilter =
    !!debouncedSearch || Object.values(debouncedColumnFilters).some(Boolean) || showNewOnly;

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
      <div className="max-w-screen-xl mx-auto px-3 py-4 sm:px-6 sm:py-6 space-y-4">

        <ConfigPageHeader
          title="Items Master"
          description="Master catalog of stock items — descriptions, categories, units, and reorder points."
        />

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
              onClick={() => { setSearchInput(""); setDebouncedSearch(""); }}
              className="text-sm text-slate-600 hover:text-slate-600 px-2"
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
            <TableSkeleton columns={["#", "Stock No.", "Description", "Category", "Unit", "Unit Cost", "Type", "Reorder", "Remarks", "New?", ""]} />
          ) : fetchError ? (
            <div className="flex flex-col items-center justify-center py-20 gap-3">
              <p className="text-sm text-red-500">{fetchError}</p>
              <button onClick={reload} className="text-sm text-green-600 hover:underline">Retry</button>
            </div>
          ) : (
            <div className="overflow-x-auto overflow-y-hidden">
              <table className="w-full text-sm border-collapse min-w-[1250px]">

                {/* ── Header ── */}
                <thead>
                  {table.getHeaderGroups().map((hg) => (
                    <tr key={hg.id} className="bg-slate-50 border-b border-slate-200 text-xs text-slate-600 uppercase tracking-wide">
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

                  {/* Filter row — server-side, debounced (RAL-192) */}
                  {table.getHeaderGroups().map((hg) => (
                    <tr key={`filter-${hg.id}`} className="bg-white border-b border-slate-100">
                      {hg.headers.map((h) => (
                        <th key={`f-${h.id}`} className="px-2 py-1.5">
                          {h.column.getCanFilter() && isFilterableColumn(h.column.id) ? (
                            <input
                              value={columnFilterInput[h.column.id]}
                              onChange={(e) => {
                                const columnId = h.column.id;
                                if (!isFilterableColumn(columnId)) return;
                                setColumnFilterInput((prev) => ({ ...prev, [columnId]: e.target.value }));
                              }}
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
                      <td colSpan={columns.length} className="text-center py-16 text-slate-600 text-sm">
                        {hasActiveFilter ? "No items match your filters." : "No items in the catalog yet."}
                      </td>
                    </tr>
                  ) : (
                    table.getRowModel().rows.map((row, i) => {
                      const isEditing = row.original.id === editingId;
                      return (
                        <Fragment key={row.id}>
                          <tr
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
                        </Fragment>
                      );
                    })
                  )}
                </tbody>
              </table>

              {/* ── Status bar + pagination ── */}
              <div className="flex items-center justify-between px-4 py-2 border-t border-slate-100 text-xs text-slate-600 flex-wrap gap-2">
                <span>
                  {totalCount} item{totalCount !== 1 ? "s" : ""}
                  {newCount > 0 && (
                    <span className="ml-2 text-amber-600 font-medium">· {newCount} pending review</span>
                  )}
                  {editingId && (
                    <span className="ml-2 text-green-600 font-medium">· editing…</span>
                  )}
                </span>
                <div className="flex items-center gap-2">
                  <button onClick={() => setPage((p) => Math.max(1, p - 1))} disabled={page <= 1} className="px-2 py-0.5 rounded border border-slate-200 disabled:opacity-40 hover:bg-slate-50 transition-colors">‹</button>
                  <span>Page {page} / {pageCount}</span>
                  <button onClick={() => setPage((p) => Math.min(pageCount, p + 1))} disabled={page >= pageCount} className="px-2 py-0.5 rounded border border-slate-200 disabled:opacity-40 hover:bg-slate-50 transition-colors">›</button>
                  <select
                    value={pageSize}
                    onChange={(e) => { setPageSize(Number(e.target.value)); setPage(1); }}
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
          <p className="text-xs text-slate-600 text-right">
            Press <kbd className="px-1.5 py-0.5 rounded bg-slate-200 text-slate-600 font-mono">✕</kbd> to cancel without saving
          </p>
        )}
      </div>
    </div>
  );
}
