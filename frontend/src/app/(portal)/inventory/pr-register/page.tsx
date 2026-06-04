"use client";

/**
 * PR Register page — RAL-57.
 * Full PR list with status filter tabs and a View Report action per row.
 * View Report → /inventory/pr-report/[prNo]
 *
 * API: GET /api/purchase-requests → PRSummaryResponse[]
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
import { useToast } from "@/components/ui/Toast";
import type { MeResponse, PRSummaryResponse } from "@/types";

// ---------------------------------------------------------------------------
// Constants
// ---------------------------------------------------------------------------

type PRStatus = "Open" | "PartiallyDelivered" | "FullyDelivered" | "Completed";
type TabKey   = "All" | PRStatus;

const STATUS_LABEL: Record<string, string> = {
  Open:               "Open",
  PartiallyDelivered: "Partial",
  FullyDelivered:     "Fully Delivered",
  Completed:          "Completed",
};

const STATUS_CLASSES: Record<string, string> = {
  Open:               "bg-info-100 text-info-500 border-blue-300",
  PartiallyDelivered: "bg-amber-100 text-amber-700 border-amber-300",
  FullyDelivered:     "bg-green-100 text-green-700 border-green-300",
  Completed:          "bg-slate-100 text-slate-600 border-slate-300",
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

function fmtDate(iso: string) {
  if (!iso) return "—";
  const d = new Date(iso);
  if (isNaN(d.getTime())) return iso;
  return d.toLocaleDateString("en-PH", { year: "numeric", month: "short", day: "numeric" });
}

// ---------------------------------------------------------------------------
// Page
// ---------------------------------------------------------------------------

export default function PRRegisterPage() {
  const router     = useRouter();
  const { toast }  = useToast();
  const [authChecked, setAuthChecked] = useState(false);

  const [prs, setPRs]               = useState<PRSummaryResponse[]>([]);
  const [loading, setLoading]       = useState(true);
  const [fetchError, setFetchError] = useState<string | null>(null);

  // Track which PR is being marked completed (shows spinner on that row)
  const [completingId, setCompletingId] = useState<string | null>(null);

  const [searchInput, setSearchInput]   = useState("");
  const [globalFilter, setGlobalFilter] = useState("");
  const [sorting, setSorting]           = useState<SortingState>([]);
  const [activeTab, setActiveTab]       = useState<TabKey>("All");

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

  const loadPRs = useCallback(async () => {
    setLoading(true);
    setFetchError(null);
    try {
      const { data } = await api.get<PRSummaryResponse[]>("/purchase-requests");
      setPRs(data);
    } catch {
      setFetchError("Failed to load purchase requests. Please try again.");
    } finally {
      setLoading(false);
    }
  }, []);

  useEffect(() => {
    if (authChecked) loadPRs();
  }, [authChecked, loadPRs]);

  // ── Tab counts ─────────────────────────────────────────────────────────────

  const counts = useMemo(() => {
    const c: Record<string, number> = { All: prs.length };
    for (const pr of prs) {
      c[pr.status] = (c[pr.status] ?? 0) + 1;
    }
    return c;
  }, [prs]);

  // ── Table data (tab filter applied before TanStack) ───────────────────────

  const tableData = useMemo(() => {
    if (activeTab === "All") return prs;
    return prs.filter((p) => p.status === activeTab);
  }, [prs, activeTab]);

  // ── Mark Completed ─────────────────────────────────────────────────────────

  async function handleMarkCompleted(pr: PRSummaryResponse) {
    if (!window.confirm(`Mark "${pr.prNo}" as Completed?\n\nThis cannot be undone.`)) return;
    setCompletingId(pr.id);
    try {
      await api.put(`/purchase-requests/${pr.id}/complete`);
      // Optimistically update the row status in place — no full reload needed.
      setPRs((prev) =>
        prev.map((p) => p.id === pr.id ? { ...p, status: "Completed" } : p)
      );
      toast.success("PR Completed", `${pr.prNo} has been marked as Completed.`);
    } catch (e: unknown) {
      const msg =
        (e as { response?: { data?: string } })?.response?.data
        ?? "Could not mark the PR as Completed. Please try again.";
      toast.error("Action failed", msg);
    } finally {
      setCompletingId(null);
    }
  }

  // ── Columns ────────────────────────────────────────────────────────────────

  const columns = useMemo<ColumnDef<PRSummaryResponse>[]>(() => [
    {
      id: "rowNo",
      header: "#",
      size: 44,
      enableSorting: false,
      enableColumnFilter: false,
      cell: ({ row }) => (
        <span className="text-slate-400 text-xs">{row.index + 1}</span>
      ),
    },
    {
      accessorKey: "prNo",
      header: "PR No.",
      size: 220,
      cell: ({ getValue }) => (
        <span className="font-mono text-xs text-slate-800">{getValue<string>()}</span>
      ),
    },
    {
      accessorKey: "prDate",
      header: "PR Date",
      size: 110,
      enableColumnFilter: false,
      cell: ({ getValue }) => (
        <span className="text-slate-600 text-sm">{fmtDate(getValue<string>())}</span>
      ),
    },
    {
      accessorKey: "division",
      header: "Division",
      size: 100,
      cell: ({ getValue }) => (
        <span className="text-slate-600 text-sm">{getValue<string>()}</span>
      ),
    },
    {
      accessorKey: "requestedBy",
      header: "Requested By",
      size: 160,
      cell: ({ getValue }) => (
        <span className="text-slate-700 text-sm">{getValue<string>()}</span>
      ),
    },
    {
      accessorKey: "totalAmount",
      header: "Total Amount",
      size: 120,
      enableColumnFilter: false,
      cell: ({ getValue }) => (
        <span className="text-slate-700 text-sm tabular-nums">₱{fmt(getValue<number>())}</span>
      ),
    },
    {
      accessorKey: "status",
      header: "Status",
      size: 130,
      enableColumnFilter: false,
      enableSorting: false,
      cell: ({ getValue }) => {
        const status = getValue<string>();
        return (
          <span className={`inline-flex items-center px-2.5 py-0.5 rounded-full text-xs font-semibold border ${STATUS_CLASSES[status] ?? "bg-slate-100 text-slate-600 border-slate-300"}`}>
            {STATUS_LABEL[status] ?? status}
          </span>
        );
      },
    },
    {
      id: "actions",
      header: "",
      size: 220,
      enableSorting: false,
      enableColumnFilter: false,
      cell: ({ row }) => {
        const pr           = row.original;
        const isCompleting = completingId === pr.id;
        return (
          <div className="flex items-center justify-end gap-2">
            {/* Mark as Completed — only shown for FullyDelivered PRs */}
            {pr.status === "FullyDelivered" && (
              <button
                onClick={() => handleMarkCompleted(pr)}
                disabled={isCompleting || completingId !== null}
                className="inline-flex items-center gap-1 px-3 py-1.5 text-xs rounded-lg border border-slate-300 text-slate-600 bg-white hover:bg-slate-50 transition-colors font-medium disabled:opacity-50 disabled:cursor-not-allowed whitespace-nowrap"
              >
                {isCompleting
                  ? <span className="w-3 h-3 border-2 border-slate-400 border-t-transparent rounded-full animate-spin" />
                  : "✓"}
                {isCompleting ? "Saving…" : "Mark Completed"}
              </button>
            )}
            <button
              onClick={() => router.push(`/inventory/pr-report?id=${encodeURIComponent(pr.id)}`)}
              className="inline-flex items-center gap-1 px-3 py-1.5 text-xs rounded-lg border border-green-300 text-green-700 bg-green-50 hover:bg-green-100 transition-colors font-medium whitespace-nowrap"
            >
              📋 View Report
            </button>
          </div>
        );
      },
    },
  // eslint-disable-next-line react-hooks/exhaustive-deps
  ], [router, completingId]);

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
    initialState: {
      pagination: { pageSize: 25 },
      sorting: [{ id: "prDate", desc: true }],
    },
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

  const TABS: Array<{ key: TabKey; label: string }> = [
    { key: "All",               label: `All (${counts.All ?? 0})` },
    { key: "Open",              label: `Open (${counts.Open ?? 0})` },
    { key: "PartiallyDelivered", label: `Partially Delivered (${counts.PartiallyDelivered ?? 0})` },
    { key: "FullyDelivered",    label: `Fully Delivered (${counts.FullyDelivered ?? 0})` },
    { key: "Completed",         label: `Completed (${counts.Completed ?? 0})` },
  ];

  return (
    <div className="min-h-screen bg-slate-100 font-sans">
      <div className="max-w-screen-xl mx-auto px-6 py-6 space-y-4">

        {/* ── Toolbar ──────────────────────────────────────────────────────── */}
        <div className="flex flex-wrap items-center gap-3">
          <input
            value={searchInput}
            onChange={(e) => setSearchInput(e.target.value)}
            placeholder="Search PRs…"
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
            onClick={loadPRs}
            className="flex items-center gap-1.5 px-3 py-2.5 rounded-lg text-sm border border-slate-200 bg-white text-slate-600 hover:bg-slate-50 transition-colors shadow-sm shrink-0"
            title="Refresh"
          >
            ↻ Refresh
          </button>
        </div>

        {/* ── Status filter tabs ────────────────────────────────────────────── */}
        <div className="flex gap-1 flex-wrap border-b border-slate-200">
          {TABS.map(({ key, label }) => (
            <button
              key={key}
              onClick={() => setActiveTab(key)}
              className={`px-4 py-2 text-sm font-medium border-b-2 -mb-px transition-colors ${
                activeTab === key
                  ? "border-green-600 text-green-700"
                  : "border-transparent text-slate-500 hover:text-slate-700 hover:border-slate-300"
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
              <button onClick={loadPRs} className="text-sm text-green-600 hover:underline">Retry</button>
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

                  {/* Filter row — text columns only */}
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
                        {prs.length === 0
                          ? "No purchase requests found."
                          : "No PRs match your filters."}
                      </td>
                    </tr>
                  ) : (
                    table.getRowModel().rows.map((row, i) => (
                      <tr
                        key={row.id}
                        className={`transition-colors ${
                          i % 2 === 1 ? "bg-slate-50 hover:bg-green-50" : "bg-white hover:bg-green-50"
                        }`}
                      >
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

              {/* ── Status bar + pagination ── */}
              <div className="flex items-center justify-between px-4 py-2 border-t border-slate-100 text-xs text-slate-400 flex-wrap gap-2">
                <span>
                  {visibleRows === totalFiltered
                    ? `${totalFiltered} PR${totalFiltered !== 1 ? "s" : ""}`
                    : `${visibleRows} of ${totalFiltered} PRs`}
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
