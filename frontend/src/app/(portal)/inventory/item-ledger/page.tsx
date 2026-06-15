"use client";

/**
 * Stock Overview page — RAL-57 (formerly Item Ledger).
 * Shows running stock per item with color-coded status badges and a
 * collapsible filter panel (mirrors the PR List filter style).
 *
 * Filters (all client-side):
 *   Status tabs — All / In Stock / Low Stock / Out of Stock
 *   Quick presets — Needs Reorder, Nothing Delivered Yet, Unreleased Stock
 *   Category  — multi-select chips (built from data)
 *   Item Type — multi-select chips (built from data)
 *   Unit      — dropdown (built from data)
 *   On Hand   — min / max range
 *
 * API: GET /api/inventory/ledger → ItemLedgerRowResponse[]
 */

import { useCallback, useEffect, useMemo, useState } from "react";
import { useRouter } from "next/navigation";
import {
  useReactTable,
  getCoreRowModel,
  getSortedRowModel,
  getPaginationRowModel,
  flexRender,
  type ColumnDef,
  type SortingState,
} from "@tanstack/react-table";
import api from "@/lib/api";
import type { ItemLedgerRowResponse, MeResponse } from "@/types";

// ---------------------------------------------------------------------------
// Types
// ---------------------------------------------------------------------------

type StockStatus = "in-stock" | "low" | "out-of-stock";

interface Filters {
  search:      string;
  status:      StockStatus | "all";
  categories:  string[];   // [] = all
  itemTypes:   string[];   // [] = all
  unit:        string;     // "" = all
  onHandMin:   string;     // "" = no min
  onHandMax:   string;     // "" = no max
}

const EMPTY_FILTERS: Filters = {
  search: "", status: "all", categories: [], itemTypes: [],
  unit: "", onHandMin: "", onHandMax: "",
};

// ---------------------------------------------------------------------------
// Helpers
// ---------------------------------------------------------------------------

function fmt(n: number) {
  return new Intl.NumberFormat("en-PH", {
    minimumFractionDigits: 0,
    maximumFractionDigits: 2,
  }).format(n);
}

function getStatus(row: ItemLedgerRowResponse): StockStatus {
  if (row.isOutOfStock) return "out-of-stock";
  if (row.isLowStock)   return "low";
  return "in-stock";
}

const STATUS_LABEL: Record<StockStatus, string> = {
  "in-stock":     "In Stock",
  "low":          "Low Stock",
  "out-of-stock": "Out of Stock",
};

const STATUS_BADGE: Record<StockStatus, string> = {
  "in-stock":     "bg-green-100 text-green-700 border-green-300",
  "low":          "bg-amber-100 text-amber-700 border-amber-300",
  "out-of-stock": "bg-danger-100 text-danger-500 border-red-300",
};

const STATUS_TAB_ACTIVE: Record<StockStatus | "all", string> = {
  "all":          "bg-green-600 text-white border-green-600",
  "in-stock":     "bg-green-600 text-white border-green-600",
  "low":          "bg-amber-500 text-white border-amber-500",
  "out-of-stock": "bg-danger-500 text-white border-danger-500",
};


/** Returns [YYYY-MM-DD, YYYY-MM-DD] bounds for a "QN-YYYY" quarter string. */
function quarterBounds(q: string): { from: string; to: string } | null {
  const m = q.match(/^Q(\d)-(\d{4})$/);
  if (!m) return null;
  const qn   = parseInt(m[1]);
  const yr   = parseInt(m[2]);
  const from = new Date(yr, (qn - 1) * 3, 1);
  const to   = new Date(yr, qn * 3, 0);
  return {
    from: from.toISOString().slice(0, 10),
    to:   to.toISOString().slice(0, 10),
  };
}

/** Generates the last N quarters ending at the current quarter. */
function recentQuarters(n = 8): string[] {
  const result: string[] = [];
  const d = new Date();
  for (let i = 0; i < n; i++) {
    const q = Math.ceil((d.getMonth() + 1) / 3);
    result.push(`Q${q}-${d.getFullYear()}`);
    d.setMonth(d.getMonth() - 3);
  }
  return result;
}

// ---------------------------------------------------------------------------
// Filter logic
// ---------------------------------------------------------------------------

function applyFilters(rows: ItemLedgerRowResponse[], f: Filters): ItemLedgerRowResponse[] {
  return rows.filter((r) => {
    // Search — stockNo + description + category + itemType
    if (f.search) {
      const q = f.search.toLowerCase();
      if (
        !r.stockNo.toLowerCase().includes(q) &&
        !r.description.toLowerCase().includes(q) &&
        !(r.category ?? "").toLowerCase().includes(q) &&
        !(r.itemType ?? "").toLowerCase().includes(q)
      ) return false;
    }

    // Stock status tab
    if (f.status !== "all" && getStatus(r) !== f.status) return false;

    // Category multi-select
    if (f.categories.length > 0) {
      const cat = r.category ?? "Uncategorised";
      if (!f.categories.includes(cat)) return false;
    }

    // Item type multi-select
    if (f.itemTypes.length > 0) {
      const type = r.itemType ?? "Unspecified";
      if (!f.itemTypes.includes(type)) return false;
    }

    // Unit
    if (f.unit && r.unit !== f.unit) return false;

    // On Hand range
    if (f.onHandMin !== "" && r.onHand < parseFloat(f.onHandMin)) return false;
    if (f.onHandMax !== "" && r.onHand > parseFloat(f.onHandMax)) return false;

    return true;
  });
}

function activeFilterCount(f: Filters): number {
  let n = 0;
  if (f.search)              n++;
  if (f.status !== "all")    n++;
  if (f.categories.length)   n++;
  if (f.itemTypes.length)    n++;
  if (f.unit)                n++;
  if (f.onHandMin !== "")    n++;
  if (f.onHandMax !== "")    n++;
  return n;
}

// ---------------------------------------------------------------------------
// Sub-components
// ---------------------------------------------------------------------------

function MultiChip({
  label, active, onClick,
}: {
  label: string; active: boolean; onClick: () => void;
}) {
  return (
    <button
      onClick={onClick}
      className={`px-3 py-1.5 text-xs font-medium border transition-colors ${
        active
          ? "bg-green-600 text-white border-green-600"
          : "bg-white text-slate-600 border-slate-200 hover:bg-slate-50"
      }`}
    >
      {label}
    </button>
  );
}

// ---------------------------------------------------------------------------
// Page
// ---------------------------------------------------------------------------

export default function StockOverviewPage() {
  const router = useRouter();
  const [authChecked, setAuthChecked] = useState(false);

  const [rows, setRows]             = useState<ItemLedgerRowResponse[]>([]);
  const [loading, setLoading]       = useState(true);
  const [fetchError, setFetchError] = useState<string | null>(null);

  const [filters, setFilters]         = useState<Filters>(EMPTY_FILTERS);
  const [filtersOpen, setFiltersOpen] = useState(false);
  const [sorting, setSorting]         = useState<SortingState>([]);

  // "Received in Quarter" — triggers API re-fetch with delivery date params
  const [receivedInQuarter, setReceivedInQuarter] = useState("");

  function setF<K extends keyof Filters>(key: K, value: Filters[K]) {
    setFilters((prev) => ({ ...prev, [key]: value }));
  }

  function toggleChip(key: "categories" | "itemTypes", value: string) {
    setFilters((prev) => ({
      ...prev,
      [key]: prev[key].includes(value)
        ? prev[key].filter((v) => v !== value)
        : [...prev[key], value],
    }));
  }

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

  const loadLedger = useCallback(async (quarter?: string) => {
    setLoading(true);
    setFetchError(null);
    try {
      const bounds = quarter ? quarterBounds(quarter) : null;
      const params = bounds
        ? `?deliveryDateFrom=${bounds.from}&deliveryDateTo=${bounds.to}`
        : "";
      const { data } = await api.get<ItemLedgerRowResponse[]>(`/inventory/ledger${params}`);
      setRows(data);
    } catch {
      setFetchError("Failed to load stock overview. Please try again.");
    } finally {
      setLoading(false);
    }
  }, []);

  useEffect(() => { if (authChecked) loadLedger(receivedInQuarter || undefined); },
    [authChecked, loadLedger, receivedInQuarter]);

  // ── Derived options (built from actual data) ───────────────────────────────

  const categoryOptions = useMemo(() =>
    Array.from(new Set(rows.map((r) => r.category ?? "Uncategorised"))).sort(),
  [rows]);

  const itemTypeOptions = useMemo(() =>
    Array.from(new Set(rows.map((r) => r.itemType ?? "Unspecified"))).sort(),
  [rows]);

  const unitOptions = useMemo(() =>
    Array.from(new Set(rows.map((r) => r.unit))).sort(),
  [rows]);

  // ── Filtered data ──────────────────────────────────────────────────────────

  const filteredRows = useMemo(() => applyFilters(rows, filters), [rows, filters]);

  const filterCount  = useMemo(
    () => activeFilterCount(filters) + (receivedInQuarter ? 1 : 0),
    [filters, receivedInQuarter]);

  const quarterOptions = useMemo(() => recentQuarters(8), []);

  // ── Counts (based on all rows, not filtered, for the status tabs) ──────────

  const counts = useMemo(() => ({
    all:           rows.length,
    "in-stock":    rows.filter((r) => getStatus(r) === "in-stock").length,
    low:           rows.filter((r) => getStatus(r) === "low").length,
    "out-of-stock": rows.filter((r) => getStatus(r) === "out-of-stock").length,
  }), [rows]);

  // ── Columns ────────────────────────────────────────────────────────────────

  const columns = useMemo<ColumnDef<ItemLedgerRowResponse>[]>(() => [
    {
      id: "rowNo", header: "#", size: 40, enableSorting: false,
      cell: ({ row }) => <span className="text-slate-400 text-xs">{row.index + 1}</span>,
    },
    {
      accessorKey: "stockNo", header: "Stock No.", size: 120,
      cell: ({ getValue }) => (
        <span className="font-mono text-xs text-slate-700">{getValue<string>()}</span>
      ),
    },
    {
      accessorKey: "description", header: "Description", size: 220,
      cell: ({ getValue }) => (
        <span className="text-slate-800 text-sm">{getValue<string>()}</span>
      ),
    },
    {
      accessorKey: "category", header: "Category", size: 120,
      cell: ({ getValue }) => (
        <span className="text-slate-500 text-xs">{getValue<string | null>() ?? "—"}</span>
      ),
    },
    {
      accessorKey: "itemType", header: "Type", size: 90,
      cell: ({ getValue }) => (
        <span className="text-slate-500 text-xs">{getValue<string | null>() ?? "—"}</span>
      ),
    },
    {
      accessorKey: "unit", header: "Unit", size: 68,
      cell: ({ getValue }) => (
        <span className="text-slate-600 text-sm">{getValue<string>()}</span>
      ),
    },
    {
      accessorKey: "qtyOrdered", header: "Ordered", size: 80,
      enableSorting: true,
      cell: ({ getValue }) => (
        <span className="text-slate-700 text-sm tabular-nums">{fmt(getValue<number>())}</span>
      ),
    },
    {
      accessorKey: "qtyDelivered", header: "Delivered", size: 80,
      cell: ({ getValue }) => (
        <span className="text-slate-700 text-sm tabular-nums">{fmt(getValue<number>())}</span>
      ),
    },
    {
      accessorKey: "qtyDistributed", header: "Distributed", size: 90,
      cell: ({ getValue }) => (
        <span className="text-slate-700 text-sm tabular-nums">{fmt(getValue<number>())}</span>
      ),
    },
    {
      accessorKey: "onHand", header: "On Hand", size: 80,
      cell: ({ getValue }) => (
        <span className="font-semibold text-sm tabular-nums text-slate-800">
          {fmt(getValue<number>())}
        </span>
      ),
    },
    {
      accessorKey: "reorderQty", header: "Reorder", size: 72,
      cell: ({ getValue }) => (
        <span className="text-slate-500 text-sm tabular-nums">{getValue<number>()}</span>
      ),
    },
    {
      id: "status", header: "Status", size: 120, enableSorting: false,
      cell: ({ row }) => {
        const s = getStatus(row.original);
        return (
          <span className={`inline-flex items-center px-2.5 py-0.5 rounded-full text-xs font-semibold border ${STATUS_BADGE[s]}`}>
            {STATUS_LABEL[s]}
          </span>
        );
      },
    },
  ], []);

  // ── Table ──────────────────────────────────────────────────────────────────

  const table = useReactTable({
    data: filteredRows,
    columns,
    state: { sorting },
    onSortingChange: setSorting,
    getCoreRowModel: getCoreRowModel(),
    getSortedRowModel: getSortedRowModel(),
    getPaginationRowModel: getPaginationRowModel(),
    initialState: { pagination: { pageSize: 25 } },
  });

  const totalFiltered = filteredRows.length;
  const visibleRows   = table.getRowModel().rows.length;

  // ── Auth loading ───────────────────────────────────────────────────────────

  if (!authChecked) {
    return (
      <div className="min-h-screen flex items-center justify-center bg-slate-100">
        <div className="w-8 h-8 border-4 border-green-600 border-t-transparent rounded-full animate-spin" />
      </div>
    );
  }

  // ── Render ─────────────────────────────────────────────────────────────────

  const STATUS_TABS: Array<{ key: StockStatus | "all"; label: string }> = [
    { key: "all",          label: `All (${counts.all})` },
    { key: "in-stock",     label: `In Stock (${counts["in-stock"]})` },
    { key: "low",          label: `Low Stock (${counts.low})` },
    { key: "out-of-stock", label: `Out of Stock (${counts["out-of-stock"]})` },
  ];

  return (
    <div className="min-h-screen bg-slate-100 font-sans">
      <div className="max-w-screen-xl mx-auto px-6 py-6 space-y-3">

        {/* ── Top bar ────────────────────────────────────────────────────────── */}
        <div className="flex flex-wrap items-center gap-3">
          <input
            value={filters.search}
            onChange={(e) => setF("search", e.target.value)}
            placeholder="Search stock no., description, category, type…"
            className="flex-1 min-w-64 px-4 py-2.5 text-sm border border-slate-200 bg-white shadow-sm focus:outline-none focus:ring-2 focus:ring-green-600"
          />
          <button
            onClick={() => setFiltersOpen((o) => !o)}
            className={`flex items-center gap-2 px-4 py-2.5 text-sm border shadow-sm transition-colors shrink-0 ${
              filtersOpen || filterCount > 0
                ? "bg-green-600 text-white border-green-600"
                : "bg-white text-slate-600 border-slate-200 hover:bg-slate-50"
            }`}
          >
            <span>⚙ Filters</span>
            {filterCount > 0 && (
              <span className="inline-flex items-center justify-center w-5 h-5 rounded-full text-xs font-bold bg-white text-green-700">
                {filterCount}
              </span>
            )}
          </button>
          <button
            onClick={() => void loadLedger(receivedInQuarter || undefined)}
            className="flex items-center gap-1.5 px-3 py-2.5 text-sm border border-slate-200 bg-white text-slate-600 hover:bg-slate-50 transition-colors shadow-sm shrink-0"
          >
            ↻ Refresh
          </button>
        </div>

        {/* ── Status tabs ──────────────────────────────────────────────────── */}
        <div className="flex gap-1 flex-wrap">
          {STATUS_TABS.map(({ key, label }) => (
            <button
              key={key}
              onClick={() => setF("status", key)}
              className={`px-4 py-2 text-sm font-medium border transition-colors ${
                filters.status === key
                  ? STATUS_TAB_ACTIVE[key]
                  : "bg-white text-slate-600 border-slate-200 hover:bg-slate-50"
              }`}
            >
              {label}
            </button>
          ))}
        </div>

        {/* ── Filter panel ──────────────────────────────────────────────────── */}
        {filtersOpen && (
          <div className="bg-white border border-slate-200 shadow-sm p-5 space-y-5">

            {/* Quick presets */}
            <div>
              <p className="text-xs font-semibold text-slate-500 uppercase tracking-wide mb-2">Quick Presets</p>
              <div className="flex flex-wrap gap-2">
                <button
                  onClick={() => {
                    setFilters({ ...EMPTY_FILTERS, status: "out-of-stock" });
                    setFiltersOpen(true);
                  }}
                  className="px-3 py-1.5 text-xs font-medium border border-red-300 bg-danger-100 text-danger-500 hover:bg-red-100 transition-colors"
                >
                  Out of Stock
                </button>
                <button
                  onClick={() => {
                    setFilters({ ...EMPTY_FILTERS, status: "low" });
                    setFiltersOpen(true);
                  }}
                  className="px-3 py-1.5 text-xs font-medium border border-amber-300 bg-amber-50 text-amber-700 hover:bg-amber-100 transition-colors"
                >
                  Low Stock (Needs Reorder)
                </button>
                <button
                  onClick={() => {
                    // Nothing delivered yet: qtyDelivered = 0 but qtyOrdered > 0
                    // Approximated with onHandMin = 0, onHandMax = 0 and status = out-of-stock
                    // Actually filter by qtyDelivered = 0 is not in Filters state.
                    // Use onHandMax = 0 to show items with nothing on hand.
                    setFilters({ ...EMPTY_FILTERS, onHandMax: "0" });
                    setFiltersOpen(true);
                  }}
                  className="px-3 py-1.5 text-xs font-medium border border-slate-300 bg-slate-50 text-slate-600 hover:bg-slate-100 transition-colors"
                >
                  Nothing Delivered Yet
                </button>
                <button
                  onClick={() => {
                    // Unreleased stock: delivered > distributed → onHand > 0
                    setFilters({ ...EMPTY_FILTERS, onHandMin: "1" });
                    setFiltersOpen(true);
                  }}
                  className="px-3 py-1.5 text-xs font-medium border border-green-300 bg-green-50 text-green-700 hover:bg-green-100 transition-colors"
                >
                  Unreleased Stock (On Hand &gt; 0)
                </button>
                {filterCount > 0 && (
                  <button
                    onClick={() => { setFilters(EMPTY_FILTERS); setReceivedInQuarter(""); }}
                    className="px-3 py-1.5 text-xs font-medium border border-slate-200 text-slate-500 hover:bg-slate-50 transition-colors"
                  >
                    ✕ Clear all filters
                  </button>
                )}
              </div>
            </div>

            <div className="border-t border-slate-100" />

            {/* Received in Quarter */}
            <div className="space-y-2">
              <p className="text-xs font-semibold text-slate-500 uppercase tracking-wide">
                Received in Quarter
                <span className="ml-1 font-normal normal-case text-slate-400">— re-fetches from server</span>
              </p>
              <div className="flex items-center gap-3">
                <select
                  value={receivedInQuarter}
                  onChange={(e) => setReceivedInQuarter(e.target.value)}
                  className="w-48 px-2.5 py-1.5 text-xs border border-slate-200 bg-white focus:outline-none focus:ring-1 focus:ring-green-500"
                >
                  <option value="">All time (no filter)</option>
                  {quarterOptions.map((q) => (
                    <option key={q} value={q}>{q}</option>
                  ))}
                </select>
                {receivedInQuarter && (
                  <button
                    onClick={() => setReceivedInQuarter("")}
                    className="text-xs text-slate-400 hover:text-slate-600"
                  >
                    ✕ Clear
                  </button>
                )}
                {receivedInQuarter && (
                  <span className="text-xs text-green-700 font-medium">
                    Showing items received in {receivedInQuarter}
                  </span>
                )}
              </div>
            </div>

            <div className="border-t border-slate-100" />

            {/* Category */}
            {categoryOptions.length > 0 && (
              <div className="space-y-2">
                <p className="text-xs font-semibold text-slate-500 uppercase tracking-wide">Category</p>
                <div className="flex flex-wrap gap-2">
                  {categoryOptions.map((cat) => (
                    <MultiChip
                      key={cat}
                      label={cat}
                      active={filters.categories.includes(cat)}
                      onClick={() => toggleChip("categories", cat)}
                    />
                  ))}
                </div>
                {filters.categories.length > 0 && (
                  <p className="text-xs text-slate-400">
                    {filters.categories.length} selected — click to deselect
                  </p>
                )}
              </div>
            )}

            <div className="border-t border-slate-100" />

            {/* Item Type + Unit + On Hand range */}
            <div className="grid grid-cols-1 md:grid-cols-3 gap-6">

              {/* Item Type */}
              {itemTypeOptions.length > 0 && (
                <div className="space-y-2">
                  <p className="text-xs font-semibold text-slate-500 uppercase tracking-wide">Item Type</p>
                  <div className="flex flex-wrap gap-2">
                    {itemTypeOptions.map((t) => (
                      <MultiChip
                        key={t}
                        label={t}
                        active={filters.itemTypes.includes(t)}
                        onClick={() => toggleChip("itemTypes", t)}
                      />
                    ))}
                  </div>
                </div>
              )}

              {/* Unit */}
              <div className="space-y-2">
                <p className="text-xs font-semibold text-slate-500 uppercase tracking-wide">Unit</p>
                <select
                  value={filters.unit}
                  onChange={(e) => setF("unit", e.target.value)}
                  className="w-full px-2.5 py-1.5 text-xs border border-slate-200 bg-white focus:outline-none focus:ring-1 focus:ring-green-500"
                >
                  <option value="">All units</option>
                  {unitOptions.map((u) => <option key={u} value={u}>{u}</option>)}
                </select>
              </div>

              {/* On Hand range */}
              <div className="space-y-2">
                <p className="text-xs font-semibold text-slate-500 uppercase tracking-wide">On Hand</p>
                <div className="flex items-center gap-2">
                  <input
                    type="number"
                    min="0"
                    value={filters.onHandMin}
                    onChange={(e) => setF("onHandMin", e.target.value)}
                    placeholder="Min"
                    className="flex-1 px-2.5 py-1.5 text-xs border border-slate-200 focus:outline-none focus:ring-1 focus:ring-green-500"
                  />
                  <span className="text-slate-400 text-xs">to</span>
                  <input
                    type="number"
                    min="0"
                    value={filters.onHandMax}
                    onChange={(e) => setF("onHandMax", e.target.value)}
                    placeholder="Max"
                    className="flex-1 px-2.5 py-1.5 text-xs border border-slate-200 focus:outline-none focus:ring-1 focus:ring-green-500"
                  />
                </div>
              </div>
            </div>

          </div>
        )}

        {/* ── Result count ─────────────────────────────────────────────────── */}
        <div className="flex items-center justify-between text-xs text-slate-500">
          <span>
            Showing <span className="font-semibold text-slate-700">{totalFiltered}</span> of{" "}
            <span className="font-semibold text-slate-700">{rows.length}</span> items
            {counts["out-of-stock"] > 0 && (
              <span className="ml-3 text-danger-500 font-medium">· {counts["out-of-stock"]} out of stock</span>
            )}
            {counts.low > 0 && (
              <span className="ml-2 text-amber-600 font-medium">· {counts.low} low stock</span>
            )}
            {filterCount > 0 && (
              <button
                onClick={() => { setFilters(EMPTY_FILTERS); setReceivedInQuarter(""); }}
                className="ml-3 text-green-600 hover:underline"
              >
                Clear all filters
              </button>
            )}
          </span>
          {filterCount > 0 && (
            <span className="text-green-700 font-medium">{filterCount} filter{filterCount !== 1 ? "s" : ""} active</span>
          )}
        </div>

        {/* ── Table card ───────────────────────────────────────────────────── */}
        <div className="bg-white border border-slate-200 shadow-sm overflow-hidden">
          {loading ? (
            <div className="flex items-center justify-center py-20">
              <div className="w-8 h-8 border-4 border-green-600 border-t-transparent rounded-full animate-spin" />
            </div>
          ) : fetchError ? (
            <div className="flex flex-col items-center justify-center py-20 gap-3">
              <p className="text-sm text-red-500">{fetchError}</p>
              <button onClick={() => void loadLedger(receivedInQuarter || undefined)} className="text-sm text-green-600 hover:underline">Retry</button>
            </div>
          ) : (
            <div className="overflow-x-auto">
              <table className="w-full text-sm border-collapse">
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
                </thead>
                <tbody className="divide-y divide-slate-100">
                  {table.getRowModel().rows.length === 0 ? (
                    <tr>
                      <td colSpan={columns.length} className="text-center py-16 text-slate-400 text-sm">
                        {rows.length === 0 ? "No items in the ledger yet." : "No items match your filters."}
                      </td>
                    </tr>
                  ) : (
                    table.getRowModel().rows.map((row, i) => (
                      <tr key={row.id} className={`transition-colors ${i % 2 === 1 ? "bg-slate-50 hover:bg-green-50" : "bg-white hover:bg-green-50"}`}>
                        {row.getVisibleCells().map((cell) => (
                          <td key={cell.id} className="px-3 py-2.5 align-middle">
                            {flexRender(cell.column.columnDef.cell, cell.getContext())}
                          </td>
                        ))}
                      </tr>
                    ))
                  )}
                </tbody>
              </table>

              {/* Pagination */}
              <div className="flex items-center justify-between px-4 py-2 border-t border-slate-100 text-xs text-slate-400 flex-wrap gap-2">
                <span>
                  {visibleRows === totalFiltered
                    ? `${totalFiltered} item${totalFiltered !== 1 ? "s" : ""}`
                    : `${visibleRows} of ${totalFiltered} items`}
                </span>
                <div className="flex items-center gap-2">
                  <button onClick={() => table.previousPage()} disabled={!table.getCanPreviousPage()} className="px-2 py-0.5 rounded border border-slate-200 disabled:opacity-40 hover:bg-slate-50">‹</button>
                  <span>Page {table.getState().pagination.pageIndex + 1} / {table.getPageCount() || 1}</span>
                  <button onClick={() => table.nextPage()} disabled={!table.getCanNextPage()} className="px-2 py-0.5 rounded border border-slate-200 disabled:opacity-40 hover:bg-slate-50">›</button>
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
      </div>
    </div>
  );
}
