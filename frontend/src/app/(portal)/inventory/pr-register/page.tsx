"use client";

/**
 * PR List page — RAL-57 (formerly PR Register).
 * Full PR list with a collapsible filter panel covering:
 *   PR Date (single / range / quarter / quick presets)
 *   Status (multi-select chips + presets)
 *   Division, Requested By, Fund, AIP Code, Account No.
 *   Account Title, Program, Project, Activity (partial match)
 *
 * All filtering is client-side on the full API response.
 * API: GET /api/purchase-requests → PRSummaryResponse[]
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
import { fetchMe } from "@/lib/me-cache";
import { useToast } from "@/components/ui/Toast";
import ConfirmDialog, { type ConfirmDialogProps } from "@/components/ui/ConfirmDialog";
import type { MeResponse, PRSummaryResponse } from "@/types";

// ---------------------------------------------------------------------------
// Types
// ---------------------------------------------------------------------------

type PRStatus  = "Open" | "PartiallyDelivered" | "FullyDelivered" | "Completed";
type DateMode  = "any" | "single" | "range" | "quarter";

interface Filters {
  search:       string;
  dateMode:     DateMode;
  dateSingle:   string;   // YYYY-MM-DD
  dateFrom:     string;   // YYYY-MM-DD
  dateTo:       string;   // YYYY-MM-DD
  quarter:      string;   // "Q1-2026" | ""
  statuses:     PRStatus[];
  division:     string;
  requestedBy:  string;
  fund:         string;
  aipCode:      string;
  accountNo:    string;
  accountTitle: string;
  program:      string;
  project:      string;
  activity:     string;
}

const EMPTY_FILTERS: Filters = {
  search: "", dateMode: "any", dateSingle: "", dateFrom: "", dateTo: "",
  quarter: "", statuses: [], division: "", requestedBy: "", fund: "",
  aipCode: "", accountNo: "", accountTitle: "", program: "", project: "",
  activity: "",
};

// ---------------------------------------------------------------------------
// Constants
// ---------------------------------------------------------------------------

const DIVISIONS = ["Admin", "Planning", "RM", "MIS", "SPD"];

const STATUS_LABEL: Record<string, string> = {
  Open:               "Open",
  PartiallyDelivered: "Partially Delivered",
  FullyDelivered:     "Fully Delivered",
  Completed:          "Completed",
};

const STATUS_CHIP: Record<string, string> = {
  Open:               "bg-info-100 text-info-500 border-blue-300",
  PartiallyDelivered: "bg-amber-100 text-amber-700 border-amber-300",
  FullyDelivered:     "bg-green-100 text-green-700 border-green-300",
  Completed:          "bg-slate-100 text-slate-600 border-slate-300",
};

const STATUS_CHIP_ACTIVE: Record<string, string> = {
  Open:               "bg-blue-500 text-white border-blue-500",
  PartiallyDelivered: "bg-amber-500 text-white border-amber-500",
  FullyDelivered:     "bg-green-600 text-white border-green-600",
  Completed:          "bg-slate-600 text-white border-slate-600",
};

const STATUS_BADGE: Record<string, string> = {
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
    minimumFractionDigits: 2, maximumFractionDigits: 2,
  }).format(n);
}

function fmtDate(iso: string) {
  if (!iso) return "—";
  const d = new Date(iso);
  if (isNaN(d.getTime())) return iso;
  return d.toLocaleDateString("en-PH", { year: "numeric", month: "short", day: "numeric" });
}

/** Returns "Q2-2026" for a YYYY-MM-DD date string. */
function toQuarter(dateStr: string): string {
  const d = new Date(dateStr);
  if (isNaN(d.getTime())) return "";
  return `Q${Math.ceil((d.getMonth() + 1) / 3)}-${d.getFullYear()}`;
}

/** Returns "Q2-2026" for the current local date. */
function currentQuarter(): string {
  return toQuarter(new Date().toISOString().slice(0, 10));
}

/** Returns the quarter immediately before the given "QN-YYYY" string. */
function prevQuarter(q: string): string {
  const m = q.match(/^Q(\d)-(\d{4})$/);
  if (!m) return "";
  const qn = parseInt(m[1]);
  const yr = parseInt(m[2]);
  return qn === 1 ? `Q4-${yr - 1}` : `Q${qn - 1}-${yr}`;
}

/** Returns the first and last YYYY-MM-DD of a "QN-YYYY" quarter. */
function quarterBounds(q: string): { from: string; to: string } | null {
  const m = q.match(/^Q(\d)-(\d{4})$/);
  if (!m) return null;
  const qn   = parseInt(m[1]);
  const yr   = parseInt(m[2]);
  const from = new Date(yr, (qn - 1) * 3, 1);
  const to   = new Date(yr, qn * 3, 0);        // last day of quarter
  return {
    from: from.toISOString().slice(0, 10),
    to:   to.toISOString().slice(0, 10),
  };
}

/** Generates quarter options from the earliest PR date up to current quarter. */
function buildQuarterOptions(prs: PRSummaryResponse[]): string[] {
  if (prs.length === 0) return [];
  const quarters = new Set<string>();
  quarters.add(currentQuarter());
  for (const pr of prs) quarters.add(toQuarter(pr.prDate));
  // Sort descending: Q4-2026, Q3-2026 ...
  return Array.from(quarters).sort((a, b) => {
    const [aqn, ayr] = [parseInt(a[1]), parseInt(a.slice(3))];
    const [bqn, byr] = [parseInt(b[1]), parseInt(b.slice(3))];
    return byr !== ayr ? byr - ayr : bqn - aqn;
  });
}

/** True if prDate falls within "QN-YYYY". */
function inQuarter(prDate: string, q: string): boolean {
  const bounds = quarterBounds(q);
  if (!bounds) return false;
  return prDate >= bounds.from && prDate <= bounds.to;
}


function contains(haystack: string | null | undefined, needle: string): boolean {
  if (!needle) return true;
  return (haystack ?? "").toLowerCase().includes(needle.toLowerCase());
}

// ---------------------------------------------------------------------------
// Filter logic
// ---------------------------------------------------------------------------

function applyFilters(prs: PRSummaryResponse[], f: Filters): PRSummaryResponse[] {
  return prs.filter((pr) => {

    // Global search — prNo, division, requestedBy
    if (f.search) {
      const q = f.search.toLowerCase();
      const hit = pr.prNo.toLowerCase().includes(q)
        || pr.division.toLowerCase().includes(q)
        || pr.requestedBy.toLowerCase().includes(q)
        || pr.fund.toLowerCase().includes(q);
      if (!hit) return false;
    }

    // PR Date
    if (f.dateMode === "single" && f.dateSingle) {
      if (pr.prDate !== f.dateSingle) return false;
    } else if (f.dateMode === "range") {
      if (f.dateFrom && pr.prDate < f.dateFrom) return false;
      if (f.dateTo   && pr.prDate > f.dateTo)   return false;
    } else if (f.dateMode === "quarter" && f.quarter) {
      if (!inQuarter(pr.prDate, f.quarter)) return false;
    }

    // Status
    if (f.statuses.length > 0 && !f.statuses.includes(pr.status as PRStatus)) return false;

    // Division
    if (f.division && pr.division !== f.division) return false;

    // Text fields
    if (!contains(pr.requestedBy,  f.requestedBy))  return false;
    if (!contains(pr.fund,         f.fund))          return false;
    if (!contains(pr.aipCode,      f.aipCode))       return false;
    if (!contains(pr.accountNo,    f.accountNo))     return false;
    if (!contains(pr.accountTitle, f.accountTitle))  return false;
    if (!contains(pr.program,      f.program))       return false;
    if (!contains(pr.project,      f.project))       return false;
    if (!contains(pr.activity,     f.activity))      return false;

    return true;
  });
}

/** Count how many filter groups have non-default values (for the badge). */
function activeFilterCount(f: Filters): number {
  let n = 0;
  if (f.search)                  n++;
  if (f.dateMode !== "any")      n++;
  if (f.statuses.length > 0)     n++;
  if (f.division)                n++;
  if (f.requestedBy)             n++;
  if (f.fund)                    n++;
  if (f.aipCode)                 n++;
  if (f.accountNo)               n++;
  if (f.accountTitle)            n++;
  if (f.program)                 n++;
  if (f.project)                 n++;
  if (f.activity)                n++;
  return n;
}

// ---------------------------------------------------------------------------
// Small sub-components
// ---------------------------------------------------------------------------

function FilterInput({
  label, value, onChange, placeholder,
}: {
  label: string;
  value: string;
  onChange: (v: string) => void;
  placeholder?: string;
}) {
  return (
    <div className="space-y-1">
      <label className="block text-xs font-medium text-slate-600">{label}</label>
      <input
        value={value}
        onChange={(e) => onChange(e.target.value)}
        placeholder={placeholder ?? `Filter by ${label.toLowerCase()}…`}
        className="w-full px-2.5 py-1.5 text-xs rounded border border-slate-200 bg-white focus:outline-none focus:ring-1 focus:ring-green-500 focus:bg-white transition-colors"
      />
    </div>
  );
}

// ---------------------------------------------------------------------------
// Page
// ---------------------------------------------------------------------------

export default function PRListPage() {
  const router    = useRouter();
  const { toast } = useToast();

  const [authChecked] = useState(true);
  const [me, setMe]                   = useState<MeResponse | null>(null);

  const [prs, setPRs]               = useState<PRSummaryResponse[]>([]);
  const [loading, setLoading]       = useState(true);
  const [fetchError, setFetchError] = useState<string | null>(null);

  const [completingId, setCompletingId]     = useState<string | null>(null);
  const [uncompletingId, setUncompletingId] = useState<string | null>(null);
  const [dialog, setDialog]                 = useState<ConfirmDialogProps | null>(null);

  const [filters, setFilters]       = useState<Filters>(EMPTY_FILTERS);
  const [filtersOpen, setFiltersOpen] = useState(false);
  const [sorting, setSorting]       = useState<SortingState>([{ id: "prDate", desc: true }]);

  function setF<K extends keyof Filters>(key: K, value: Filters[K]) {
    setFilters((prev) => ({ ...prev, [key]: value }));
  }

  function toggleStatus(status: PRStatus) {
    setFilters((prev) => ({
      ...prev,
      statuses: prev.statuses.includes(status)
        ? prev.statuses.filter((s) => s !== status)
        : [...prev.statuses, status],
    }));
  }

  // ── Auth guard ─────────────────────────────────────────────────────────────

  useEffect(() => {
    fetchMe()
      .then((data) => {
        if (!data.canAccessInventory) { router.replace(data.officeId != null ? "/budget-planning" : "/dashboard"); return; }
        setMe(data);
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

  useEffect(() => { if (authChecked) loadPRs(); }, [authChecked, loadPRs]);

  // ── Derived ────────────────────────────────────────────────────────────────

  const quarterOptions = useMemo(() => buildQuarterOptions(prs), [prs]);

  const filteredPRs = useMemo(() => applyFilters(prs, filters), [prs, filters]);

  const filterCount = useMemo(() => activeFilterCount(filters), [filters]);

  const statusCounts = useMemo(() => {
    const c: Record<string, number> = {};
    for (const pr of filteredPRs) c[pr.status] = (c[pr.status] ?? 0) + 1;
    return c;
  }, [filteredPRs]);

  // ── Presets ────────────────────────────────────────────────────────────────

  function applyPreset(preset: "pending-this-q" | "last-q-pending" | "ready-to-close" | "overdue-open") {
    const cq = currentQuarter();
    const lq = prevQuarter(cq);
    const lqBounds = quarterBounds(lq);
    setFiltersOpen(true);

    switch (preset) {
      case "pending-this-q":
        setFilters({ ...EMPTY_FILTERS,
          statuses: ["Open", "PartiallyDelivered"],
          dateMode: "quarter", quarter: cq });
        break;
      case "last-q-pending":
        setFilters({ ...EMPTY_FILTERS,
          statuses: ["Open", "PartiallyDelivered"],
          dateMode: "quarter", quarter: lq });
        break;
      case "ready-to-close":
        setFilters({ ...EMPTY_FILTERS, statuses: ["FullyDelivered"] });
        break;
      case "overdue-open":
        setFilters({ ...EMPTY_FILTERS,
          statuses: ["Open"],
          dateMode: "range",
          dateFrom: "",
          dateTo: lqBounds?.to ?? "" });
        break;
    }
  }

  // ── Mark / Unmark completed ────────────────────────────────────────────────

  function handleMarkCompleted(pr: PRSummaryResponse) {
    setDialog({
      title: "Mark as Completed?",
      message: `${pr.prNo} will be marked as Completed. You can undo this at any time.`,
      confirmLabel: "Mark Completed",
      variant: "primary",
      onConfirm: () => doMarkCompleted(pr),
      onClose: () => setDialog(null),
    });
  }

  async function doMarkCompleted(pr: PRSummaryResponse) {
    setCompletingId(pr.id);
    try {
      await api.put(`/purchase-requests/${pr.id}/complete`);
      setPRs((prev) => prev.map((p) => p.id === pr.id ? { ...p, status: "Completed" } : p));
      toast.success("PR Completed", `${pr.prNo} has been marked as Completed.`);
    } catch (e: unknown) {
      toast.error("Action failed",
        (e as { response?: { data?: string } })?.response?.data
        ?? "Could not mark the PR as Completed. Please try again.");
    } finally { setCompletingId(null); }
  }

  function handleUnmarkCompleted(pr: PRSummaryResponse) {
    setDialog({
      title: "Revert to Fully Delivered?",
      message: `${pr.prNo} will be set back to Fully Delivered.`,
      confirmLabel: "Yes, Revert",
      variant: "warning",
      onConfirm: () => doUnmarkCompleted(pr),
      onClose: () => setDialog(null),
    });
  }

  async function doUnmarkCompleted(pr: PRSummaryResponse) {
    setUncompletingId(pr.id);
    try {
      await api.put(`/purchase-requests/${pr.id}/uncomplete`);
      setPRs((prev) => prev.map((p) => p.id === pr.id ? { ...p, status: "FullyDelivered" } : p));
      toast.success("PR reverted", `${pr.prNo} is back to Fully Delivered.`);
    } catch (e: unknown) {
      toast.error("Action failed",
        (e as { response?: { data?: string } })?.response?.data
        ?? "Could not revert the PR. Please try again.");
    } finally { setUncompletingId(null); }
  }

  // ── Columns ────────────────────────────────────────────────────────────────

  const columns = useMemo<ColumnDef<PRSummaryResponse>[]>(() => [
    {
      id: "rowNo", header: "#", size: 44,
      enableSorting: false,
      cell: ({ row }) => <span className="text-slate-600 text-xs">{row.index + 1}</span>,
    },
    {
      accessorKey: "prNo", header: "PR No.", size: 220,
      cell: ({ getValue }) => (
        <span className="font-mono text-xs text-slate-800">{getValue<string>()}</span>
      ),
    },
    {
      accessorKey: "prDate", header: "PR Date", size: 110,
      cell: ({ getValue }) => (
        <span className="text-slate-600 text-sm">{fmtDate(getValue<string>())}</span>
      ),
    },
    {
      accessorKey: "division", header: "Division", size: 90,
      cell: ({ getValue }) => (
        <span className="text-slate-600 text-sm">{getValue<string>()}</span>
      ),
    },
    {
      accessorKey: "requestedBy", header: "Requested By", size: 150,
      cell: ({ getValue }) => (
        <span className="text-slate-700 text-sm">{getValue<string>()}</span>
      ),
    },
    {
      accessorKey: "fund", header: "Fund", size: 110,
      cell: ({ getValue }) => (
        <span className="text-slate-600 text-sm">{getValue<string>() || "—"}</span>
      ),
    },
    {
      accessorKey: "totalAmount", header: "Total Amount", size: 120,
      cell: ({ getValue }) => (
        <span className="text-slate-700 text-sm tabular-nums">₱{fmt(getValue<number>())}</span>
      ),
    },
    {
      accessorKey: "status", header: "Status", size: 130,
      enableSorting: false,
      cell: ({ getValue }) => {
        const s = getValue<string>();
        return (
          <span className={`inline-flex items-center px-2.5 py-0.5 rounded-full text-xs font-semibold border ${STATUS_BADGE[s] ?? "bg-slate-100 text-slate-600 border-slate-300"}`}>
            {STATUS_LABEL[s] ?? s}
          </span>
        );
      },
    },
    {
      id: "actions", header: "", size: 220,
      enableSorting: false,
      cell: ({ row }) => {
        const pr             = row.original;
        const isCompleting   = completingId === pr.id;
        const isUncompleting = uncompletingId === pr.id;
        const anyBusy        = completingId !== null || uncompletingId !== null;
        return (
          <div className="flex items-center justify-end gap-2">
            {pr.status === "FullyDelivered" && (
              <button
                onClick={() => handleMarkCompleted(pr)}
                disabled={isCompleting || anyBusy}
                className="inline-flex items-center gap-1 px-3 py-1.5 text-xs border border-slate-300 text-slate-600 bg-white hover:bg-slate-50 transition-colors font-medium disabled:opacity-50 disabled:cursor-not-allowed whitespace-nowrap"
              >
                {isCompleting ? <span className="w-3 h-3 border-2 border-slate-400 border-t-transparent rounded-full animate-spin" /> : "✓"}
                {isCompleting ? "Saving…" : "Mark Completed"}
              </button>
            )}
            {pr.status === "Completed" && (
              <button
                onClick={() => handleUnmarkCompleted(pr)}
                disabled={isUncompleting || anyBusy}
                className="inline-flex items-center gap-1 px-3 py-1.5 text-xs border border-amber-300 text-amber-700 bg-amber-50 hover:bg-amber-100 transition-colors font-medium disabled:opacity-50 disabled:cursor-not-allowed whitespace-nowrap"
              >
                {isUncompleting ? <span className="w-3 h-3 border-2 border-amber-400 border-t-transparent rounded-full animate-spin" /> : "↩"}
                {isUncompleting ? "Reverting…" : "Unmark"}
              </button>
            )}
            <button
              onClick={() => router.push(`/inventory/pr-report?id=${encodeURIComponent(pr.id)}`)}
              className="inline-flex items-center gap-1 px-3 py-1.5 text-xs border border-green-300 text-green-700 bg-green-50 hover:bg-green-100 transition-colors font-medium whitespace-nowrap"
            >
              📋 View Report
            </button>
          </div>
        );
      },
    },
  // eslint-disable-next-line react-hooks/exhaustive-deps
  ], [router, completingId, uncompletingId]);

  // ── Table ──────────────────────────────────────────────────────────────────

  const table = useReactTable({
    data: filteredPRs,
    columns,
    state: { sorting },
    onSortingChange: setSorting,
    getCoreRowModel: getCoreRowModel(),
    getSortedRowModel: getSortedRowModel(),
    getPaginationRowModel: getPaginationRowModel(),
    initialState: { pagination: { pageSize: 25 } },
  });

  const totalFiltered = filteredPRs.length;
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

  const isAdmin = me?.role === "SuperAdmin" || me?.role === "Admin";

  return (
    <div className="min-h-screen bg-slate-100 font-sans">
      <div className="max-w-screen-xl mx-auto px-6 py-6 space-y-3">

        {/* ── Top bar ────────────────────────────────────────────────────────── */}
        <div className="flex flex-wrap items-center gap-3">
          <input
            value={filters.search}
            onChange={(e) => setF("search", e.target.value)}
            placeholder="Search PR no., division, requested by, fund…"
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
            onClick={loadPRs}
            className="flex items-center gap-1.5 px-3 py-2.5 text-sm border border-slate-200 bg-white text-slate-600 hover:bg-slate-50 transition-colors shadow-sm shrink-0"
          >
            ↻ Refresh
          </button>
        </div>

        {/* ── Filter panel ──────────────────────────────────────────────────── */}
        {filtersOpen && (
          <div className="bg-white border border-slate-200 shadow-sm p-5 space-y-5">

            {/* Quick presets */}
            <div>
              <p className="text-xs font-semibold text-slate-600 uppercase tracking-wide mb-2">Quick Presets</p>
              <div className="flex flex-wrap gap-2">
                {([
                  ["pending-this-q",  "Pending This Quarter",   "bg-amber-50 text-amber-700 border-amber-300 hover:bg-amber-100"],
                  ["last-q-pending",  "Last Quarter Pending",   "bg-amber-50 text-amber-700 border-amber-300 hover:bg-amber-100"],
                  ["ready-to-close",  "Ready to Close",         "bg-green-50 text-green-700 border-green-300 hover:bg-green-100"],
                  ["overdue-open",    "Overdue Open PRs",       "bg-danger-100 text-danger-500 border-red-300 hover:bg-red-100"],
                ] as const).map(([key, label, cls]) => (
                  <button
                    key={key}
                    onClick={() => applyPreset(key)}
                    className={`px-3 py-1.5 text-xs font-medium border transition-colors ${cls}`}
                  >
                    {label}
                  </button>
                ))}
                {filterCount > 0 && (
                  <button
                    onClick={() => setFilters(EMPTY_FILTERS)}
                    className="px-3 py-1.5 text-xs font-medium border border-slate-200 text-slate-600 hover:bg-slate-50 transition-colors"
                  >
                    ✕ Clear all filters
                  </button>
                )}
              </div>
            </div>

            <div className="border-t border-slate-100" />

            {/* Row 1 — Date + Status */}
            <div className="grid grid-cols-1 md:grid-cols-2 gap-6">

              {/* PR Date */}
              <div className="space-y-2">
                <p className="text-xs font-semibold text-slate-600 uppercase tracking-wide">PR Date</p>
                <div className="flex gap-3 flex-wrap">
                  {(["any", "single", "range", "quarter"] as DateMode[]).map((m) => (
                    <label key={m} className="flex items-center gap-1.5 text-xs text-slate-600 cursor-pointer">
                      <input
                        type="radio"
                        checked={filters.dateMode === m}
                        onChange={() => setF("dateMode", m)}
                        className="accent-green-600"
                      />
                      {m === "any" ? "Any" : m === "single" ? "Single" : m === "range" ? "Range" : "Quarter"}
                    </label>
                  ))}
                </div>

                {filters.dateMode === "single" && (
                  <input type="date" value={filters.dateSingle}
                    onChange={(e) => setF("dateSingle", e.target.value)}
                    className="w-full px-2.5 py-1.5 text-xs border border-slate-200 focus:outline-none focus:ring-1 focus:ring-green-500"
                  />
                )}

                {filters.dateMode === "range" && (
                  <div className="flex items-center gap-2">
                    <input type="date" value={filters.dateFrom}
                      onChange={(e) => setF("dateFrom", e.target.value)}
                      className="flex-1 px-2.5 py-1.5 text-xs border border-slate-200 focus:outline-none focus:ring-1 focus:ring-green-500"
                    />
                    <span className="text-slate-600 text-xs">to</span>
                    <input type="date" value={filters.dateTo}
                      onChange={(e) => setF("dateTo", e.target.value)}
                      className="flex-1 px-2.5 py-1.5 text-xs border border-slate-200 focus:outline-none focus:ring-1 focus:ring-green-500"
                    />
                  </div>
                )}

                {filters.dateMode === "quarter" && (
                  <select
                    value={filters.quarter}
                    onChange={(e) => setF("quarter", e.target.value)}
                    className="w-full px-2.5 py-1.5 text-xs border border-slate-200 bg-white focus:outline-none focus:ring-1 focus:ring-green-500"
                  >
                    <option value="">— Select quarter —</option>
                    {quarterOptions.map((q) => (
                      <option key={q} value={q}>{q}</option>
                    ))}
                  </select>
                )}
              </div>

              {/* Status */}
              <div className="space-y-2">
                <p className="text-xs font-semibold text-slate-600 uppercase tracking-wide">Status</p>
                <div className="flex flex-wrap gap-2">
                  {(["Open", "PartiallyDelivered", "FullyDelivered", "Completed"] as PRStatus[]).map((s) => {
                    const active = filters.statuses.includes(s);
                    const count  = statusCounts[s] ?? 0;
                    return (
                      <button
                        key={s}
                        onClick={() => toggleStatus(s)}
                        className={`inline-flex items-center gap-1.5 px-3 py-1.5 text-xs font-medium border transition-colors ${
                          active ? STATUS_CHIP_ACTIVE[s] : STATUS_CHIP[s] + " hover:opacity-80"
                        }`}
                      >
                        {STATUS_LABEL[s]}
                        <span className={`text-xs px-1 rounded-full ${active ? "bg-white/30" : "bg-slate-200/60 text-slate-600"}`}>
                          {count}
                        </span>
                      </button>
                    );
                  })}
                </div>
                <p className="text-xs text-slate-600">Select multiple to combine (e.g. Open + Partially Delivered = all pending)</p>
              </div>
            </div>

            <div className="border-t border-slate-100" />

            {/* Row 2 — Division + PR Details */}
            <div className="grid grid-cols-2 md:grid-cols-4 gap-4">
              {isAdmin && (
                <div className="space-y-1">
                  <label className="block text-xs font-medium text-slate-600">Division</label>
                  <select
                    value={filters.division}
                    onChange={(e) => setF("division", e.target.value)}
                    className="w-full px-2.5 py-1.5 text-xs border border-slate-200 bg-white focus:outline-none focus:ring-1 focus:ring-green-500"
                  >
                    <option value="">All divisions</option>
                    {DIVISIONS.map((d) => <option key={d} value={d}>{d}</option>)}
                  </select>
                </div>
              )}
              <FilterInput label="Requested By" value={filters.requestedBy} onChange={(v) => setF("requestedBy", v)} />
              <FilterInput label="Fund" value={filters.fund} onChange={(v) => setF("fund", v)} placeholder="e.g. General Fund" />
              <FilterInput label="AIP Code" value={filters.aipCode} onChange={(v) => setF("aipCode", v)} />
              <FilterInput label="Account No." value={filters.accountNo} onChange={(v) => setF("accountNo", v)} />
              <FilterInput label="Account Title" value={filters.accountTitle} onChange={(v) => setF("accountTitle", v)} placeholder="Partial match…" />
            </div>

            <div className="border-t border-slate-100" />

            {/* Row 3 — Long text fields */}
            <div>
              <p className="text-xs font-semibold text-slate-600 uppercase tracking-wide mb-3">Program / Project / Activity <span className="font-normal normal-case">(partial match)</span></p>
              <div className="grid grid-cols-1 md:grid-cols-3 gap-4">
                <FilterInput label="Program" value={filters.program} onChange={(v) => setF("program", v)} placeholder="Partial match…" />
                <FilterInput label="Project" value={filters.project} onChange={(v) => setF("project", v)} placeholder="Partial match…" />
                <FilterInput label="Activity" value={filters.activity} onChange={(v) => setF("activity", v)} placeholder="Partial match…" />
              </div>
            </div>

          </div>
        )}

        {/* ── Result count + active filter summary ─────────────────────────── */}
        <div className="flex items-center justify-between text-xs text-slate-600">
          <span>
            Showing <span className="font-semibold text-slate-700">{totalFiltered}</span> of{" "}
            <span className="font-semibold text-slate-700">{prs.length}</span> PRs
            {filterCount > 0 && (
              <button
                onClick={() => setFilters(EMPTY_FILTERS)}
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
              <button onClick={loadPRs} className="text-sm text-green-600 hover:underline">Retry</button>
            </div>
          ) : (
            <div className="overflow-x-auto overflow-y-hidden">
              <table className="w-full text-sm border-collapse">
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
                </thead>
                <tbody className="divide-y divide-slate-100">
                  {table.getRowModel().rows.length === 0 ? (
                    <tr>
                      <td colSpan={columns.length} className="text-center py-16 text-slate-600 text-sm">
                        {prs.length === 0 ? "No purchase requests found." : "No PRs match your filters."}
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
              <div className="flex items-center justify-between px-4 py-2 border-t border-slate-100 text-xs text-slate-600 flex-wrap gap-2">
                <span>
                  {visibleRows === totalFiltered
                    ? `${totalFiltered} PR${totalFiltered !== 1 ? "s" : ""}`
                    : `${visibleRows} of ${totalFiltered} PRs`}
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

      {dialog && <ConfirmDialog {...dialog} />}
    </div>
  );
}
