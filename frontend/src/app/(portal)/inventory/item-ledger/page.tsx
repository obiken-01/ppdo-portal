"use client";

/**
 * Item Ledger page — RAL-57.
 * Shows remaining stock per item with color-coded status badges.
 *   green  = in stock  (onHand > reorderQty)
 *   amber  = low stock (onHand > 0, onHand <= reorderQty)
 *   red    = out of stock (onHand <= 0)
 *
 * API: GET /api/items/ledger → ItemLedgerRowResponse[]
 */

import { useCallback, useEffect, useMemo, useState } from "react";
import { useRouter } from "next/navigation";
import {
  useReactTable,
  getCoreRowModel,
  getFilteredRowModel,
  getSortedRowModel,
  getPaginationRowModel,
  flexRender,
  type ColumnDef,
  type SortingState,
} from "@tanstack/react-table";
import api from "@/lib/api";
import type { ItemLedgerRowResponse, MeResponse } from "@/types";

// ---------------------------------------------------------------------------
// Helpers
// ---------------------------------------------------------------------------

function fmt(n: number) {
  return new Intl.NumberFormat("en-PH", {
    minimumFractionDigits: 0,
    maximumFractionDigits: 2,
  }).format(n);
}

type StockStatus = "in-stock" | "low" | "out-of-stock";

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

const STATUS_CLASSES: Record<StockStatus, string> = {
  "in-stock":     "bg-green-100 text-green-700 border-green-300",
  "low":          "bg-amber-100 text-amber-700 border-amber-300",
  "out-of-stock": "bg-danger-100 text-danger-500 border-red-300",
};

// ---------------------------------------------------------------------------
// Page
// ---------------------------------------------------------------------------

export default function ItemLedgerPage() {
  const router = useRouter();
  const [authChecked, setAuthChecked] = useState(false);

  const [rows, setRows]         = useState<ItemLedgerRowResponse[]>([]);
  const [loading, setLoading]   = useState(true);
  const [fetchError, setFetchError] = useState<string | null>(null);

  const [searchInput, setSearchInput] = useState("");
  const [globalFilter, setGlobalFilter] = useState("");
  const [sorting, setSorting]           = useState<SortingState>([]);

  // Status filter
  const [statusFilter, setStatusFilter] = useState<StockStatus | "all">("all");

  useEffect(() => {
    const id = setTimeout(() => setGlobalFilter(searchInput), 150);
    return () => clearTimeout(id);
  }, [searchInput]);

  // ── Auth guard ─────────────────────────────────────────────────────────────

  useEffect(() => {
    api.get<MeResponse>("/auth/me")
      .then(({ data }) => {
        if (!data.canAccessInventory) router.replace("/dashboard");
        else setAuthChecked(true);
      })
      .catch(() => router.replace("/login"));
  }, [router]);

  // ── Load ───────────────────────────────────────────────────────────────────

  const loadLedger = useCallback(async () => {
    setLoading(true);
    setFetchError(null);
    try {
      const { data } = await api.get<ItemLedgerRowResponse[]>("/inventory/ledger");
      setRows(data);
    } catch {
      setFetchError("Failed to load the item ledger. Please try again.");
    } finally {
      setLoading(false);
    }
  }, []);

  useEffect(() => {
    if (authChecked) loadLedger();
  }, [authChecked, loadLedger]);

  // ── Table data (status filter applied before TanStack) ────────────────────

  const tableData = useMemo(() => {
    if (statusFilter === "all") return rows;
    return rows.filter((r) => getStatus(r) === statusFilter);
  }, [rows, statusFilter]);

  // ── Counts ─────────────────────────────────────────────────────────────────

  const counts = useMemo(() => ({
    all:           rows.length,
    "in-stock":    rows.filter((r) => getStatus(r) === "in-stock").length,
    low:           rows.filter((r) => getStatus(r) === "low").length,
    "out-of-stock": rows.filter((r) => getStatus(r) === "out-of-stock").length,
  }), [rows]);

  // ── Columns ────────────────────────────────────────────────────────────────

  const columns = useMemo<ColumnDef<ItemLedgerRowResponse>[]>(() => [
    {
      id: "rowNo",
      header: "#",
      size: 40,
      enableSorting: false,
      enableColumnFilter: false,
      cell: ({ row }) => (
        <span className="text-slate-400 text-xs">{row.index + 1}</span>
      ),
    },
    {
      accessorKey: "stockNo",
      header: "Stock No.",
      size: 110,
      cell: ({ getValue }) => (
        <span className="font-mono text-xs text-slate-700">{getValue<string>()}</span>
      ),
    },
    {
      accessorKey: "description",
      header: "Description",
      size: 240,
      cell: ({ getValue }) => (
        <span className="text-slate-800 text-sm">{getValue<string>()}</span>
      ),
    },
    {
      accessorKey: "unit",
      header: "Unit",
      size: 72,
      cell: ({ getValue }) => (
        <span className="text-slate-600 text-sm">{getValue<string>()}</span>
      ),
    },
    {
      accessorKey: "totalOrdered",
      header: "Ordered",
      size: 88,
      enableColumnFilter: false,
      cell: ({ getValue }) => (
        <span className="text-slate-700 text-sm tabular-nums">{fmt(getValue<number>())}</span>
      ),
    },
    {
      accessorKey: "totalDelivered",
      header: "Delivered",
      size: 88,
      enableColumnFilter: false,
      cell: ({ getValue }) => (
        <span className="text-slate-700 text-sm tabular-nums">{fmt(getValue<number>())}</span>
      ),
    },
    {
      accessorKey: "totalDistributed",
      header: "Distributed",
      size: 96,
      enableColumnFilter: false,
      cell: ({ getValue }) => (
        <span className="text-slate-700 text-sm tabular-nums">{fmt(getValue<number>())}</span>
      ),
    },
    {
      accessorKey: "onHand",
      header: "On Hand",
      size: 88,
      enableColumnFilter: false,
      cell: ({ getValue }) => (
        <span className="font-semibold text-sm tabular-nums text-slate-800">
          {fmt(getValue<number>())}
        </span>
      ),
    },
    {
      accessorKey: "reorderQty",
      header: "Reorder Qty",
      size: 96,
      enableColumnFilter: false,
      cell: ({ getValue }) => (
        <span className="text-slate-500 text-sm tabular-nums">{getValue<number>()}</span>
      ),
    },
    {
      id: "status",
      header: "Status",
      size: 120,
      enableSorting: false,
      enableColumnFilter: false,
      cell: ({ row }) => {
        const status = getStatus(row.original);
        return (
          <span className={`inline-flex items-center px-2.5 py-0.5 rounded-full text-xs font-semibold border ${STATUS_CLASSES[status]}`}>
            {STATUS_LABEL[status]}
          </span>
        );
      },
    },
  ], []);

  // ── Table instance ─────────────────────────────────────────────────────────

  const table = useReactTable({
    data: tableData,
    columns,
    state: { globalFilter, sorting },
    onGlobalFilterChange: setGlobalFilter,
    onSortingChange: setSorting,
    getCoreRowModel: getCoreRowModel(),
    getFilteredRowModel: getFilteredRowModel(),
    getSortedRowModel: getSortedRowModel(),
    getPaginationRowModel: getPaginationRowModel(),
    initialState: { pagination: { pageSize: 25 } },
    globalFilterFn: "includesString",
  });

  const totalFiltered = table.getFilteredRowModel().rows.length;
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

  const FILTER_TABS: Array<{ key: StockStatus | "all"; label: string }> = [
    { key: "all",          label: `All (${counts.all})` },
    { key: "in-stock",     label: `In Stock (${counts["in-stock"]})` },
    { key: "low",          label: `Low Stock (${counts.low})` },
    { key: "out-of-stock", label: `Out of Stock (${counts["out-of-stock"]})` },
  ];

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
          <button
            onClick={loadLedger}
            className="flex items-center gap-1.5 px-3 py-2.5 rounded-lg text-sm border border-slate-200 bg-white text-slate-600 hover:bg-slate-50 transition-colors shadow-sm shrink-0"
            title="Refresh"
          >
            ↻ Refresh
          </button>
        </div>

        {/* ── Status filter tabs ────────────────────────────────────────────── */}
        <div className="flex gap-1 flex-wrap">
          {FILTER_TABS.map(({ key, label }) => (
            <button
              key={key}
              onClick={() => setStatusFilter(key)}
              className={`px-4 py-2 rounded-lg text-sm font-medium transition-colors border ${
                statusFilter === key
                  ? key === "in-stock"
                    ? "bg-green-600 text-white border-green-600"
                    : key === "low"
                    ? "bg-amber-500 text-white border-amber-500"
                    : key === "out-of-stock"
                    ? "bg-danger-500 text-white border-danger-500"
                    : "bg-green-600 text-white border-green-600"
                  : "bg-white text-slate-600 border-slate-200 hover:bg-slate-50"
              }`}
            >
              {label}
            </button>
          ))}
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
              <button onClick={loadLedger} className="text-sm text-green-600 hover:underline">Retry</button>
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
                </thead>

                {/* ── Body ── */}
                <tbody className="divide-y divide-slate-100">
                  {table.getRowModel().rows.length === 0 ? (
                    <tr>
                      <td colSpan={columns.length} className="text-center py-16 text-slate-400 text-sm">
                        {rows.length === 0 ? "No items in the ledger yet." : "No items match your filters."}
                      </td>
                    </tr>
                  ) : (
                    table.getRowModel().rows.map((row, i) => {
                      const status = getStatus(row.original);
                      return (
                        <tr
                          key={row.id}
                          className={`transition-colors ${
                            status === "out-of-stock"
                              ? "bg-danger-100 hover:bg-red-100"
                              : status === "low"
                              ? "bg-amber-50 hover:bg-amber-100"
                              : i % 2 === 1
                              ? "bg-slate-50 hover:bg-green-50"
                              : "bg-white hover:bg-green-50"
                          }`}
                        >
                          {row.getVisibleCells().map((cell) => (
                            <td key={cell.id} className="px-3 py-2.5 align-middle">
                              {flexRender(cell.column.columnDef.cell, cell.getContext())}
                            </td>
                          ))}
                        </tr>
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
                  {counts["out-of-stock"] > 0 && (
                    <span className="ml-2 text-danger-500 font-medium">
                      · {counts["out-of-stock"]} out of stock
                    </span>
                  )}
                  {counts.low > 0 && (
                    <span className="ml-2 text-amber-600 font-medium">
                      · {counts.low} low stock
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
                  <span>Page {table.getState().pagination.pageIndex + 1} / {table.getPageCount() || 1}</span>
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
