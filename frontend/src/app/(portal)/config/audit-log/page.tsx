"use client";

/**
 * Audit Log — read-only trail of every CREATE/UPDATE/DELETE audited via
 * IAuditService (backend/PPDO.Application/Services/AuditService.cs), across
 * Config (Accounts, Divisions, Offices, Funding Sources, Price Index,
 * Procurement Presets), Budget Planning (AIP, LDIP, WFP, Allocation), and
 * User Management.
 *
 * Access guard: SuperAdmin only (PermissionService.CanViewAuditLogAsync),
 * itself gated behind FeatureFlags.AuditLogPageEnabled on the backend — flip
 * that flag/expand the role check there if this ever needs to open up to
 * other roles; no frontend change would be needed beyond this page's own
 * `me.role === "SuperAdmin"` guard below.
 *
 * Server-side filtered + paginated (audit_log has no natural size cap) via
 * DataTable's serverPagination mode.
 *
 * Endpoints (AuditLogFunctions.cs, { data, error, message } envelope):
 *   GET /api/config/audit-log?page=&pageSize=&tableName=&action=&actor=&from=&to=
 *   GET /api/config/audit-log/tables
 */

import { useCallback, useEffect, useRef, useState } from "react";
import { useRouter } from "next/navigation";
import { fetchMe } from "@/lib/me-cache";
import { configErrorMessage, listAuditLog, listAuditLogTableNames } from "@/lib/config";
import DataTable, { type Column } from "@/components/ui/DataTable";
import ConfigPageHeader from "@/components/ui/ConfigPageHeader";
import type { AuditLogEntry } from "@/types";

const PAGE_SIZE = 50;
const ACTIONS = ["CREATE", "UPDATE", "DELETE"] as const;

// Auto-refresh (RAL-182): keep the grid fresh without a manual reload.
const POLL_MS = 15 * 60 * 1000;          // steady 15-minute heartbeat
const RETURN_THROTTLE_MS = 60 * 1000;    // min gap between focus/visibility-driven refreshes (alt-tab spam guard)
const BG_REFRESH_KEY = "ppdo_audit_bg_refresh"; // localStorage: poll even while the tab is hidden

// ---------------------------------------------------------------------------
// Formatting helpers
// ---------------------------------------------------------------------------

function formatTimestamp(iso: string): string {
  return new Date(iso).toLocaleString("en-PH", {
    year: "numeric", month: "short", day: "numeric",
    hour: "numeric", minute: "2-digit", second: "2-digit",
    timeZone: "Asia/Manila",
  });
}

function formatClock(d: Date): string {
  return d.toLocaleTimeString("en-PH", {
    hour: "numeric", minute: "2-digit", second: "2-digit", timeZone: "Asia/Manila",
  });
}

function recordLabel(entry: AuditLogEntry): string {
  if (entry.recordId != null) return `#${entry.recordId}`;
  if (entry.recordGuid != null) return `#${entry.recordGuid.split("-")[0]}`;
  return "—";
}

const ACTION_BADGE_CLASS: Record<string, string> = {
  CREATE: "bg-green-100 text-green-700",
  UPDATE: "bg-info-100 text-info-500",
  DELETE: "bg-danger-100 text-danger-500",
};

function ActionBadge({ action }: { action: string }) {
  return (
    <span
      className={`inline-flex items-center px-2 py-0.5 text-xs font-medium ${
        ACTION_BADGE_CLASS[action] ?? "bg-slate-100 text-slate-600"
      }`}
    >
      {action}
    </span>
  );
}

// ---------------------------------------------------------------------------
// Page
// ---------------------------------------------------------------------------

export default function AuditLogPage() {
  const router = useRouter();

  // Auth guard
  const [authChecked, setAuthChecked] = useState(false);

  // Data
  const [entries, setEntries] = useState<AuditLogEntry[]>([]);
  const [totalCount, setTotalCount] = useState(0);
  const [loading, setLoading] = useState(true);
  const [fetchError, setFetchError] = useState<string | null>(null);

  // Auto-refresh (RAL-182)
  const [lastUpdated, setLastUpdated] = useState<Date | null>(null);
  const [bgRefresh, setBgRefresh] = useState(false); // hydrated from localStorage after mount (SSR-safe)
  const inFlightRef = useRef(false);                 // don't stack silent refreshes
  const reqSeqRef = useRef(0);                        // supersede a stale in-flight result on filter/page change
  const lastReturnRefreshRef = useRef(0);            // throttle focus/visibility refreshes
  const loadRef = useRef<(opts?: { silent?: boolean }) => void>(() => {}); // latest load, for stable listeners
  const bgRefreshRef = useRef(false);                // latest toggle, read inside the interval tick

  // Filters
  const [tableNames, setTableNames] = useState<string[]>([]);
  const [tableFilter, setTableFilter] = useState("");
  const [actionFilter, setActionFilter] = useState("");
  const [actorSearch, setActorSearch] = useState("");
  const [debouncedActorSearch, setDebouncedActorSearch] = useState("");
  const [fromDate, setFromDate] = useState("");
  const [toDate, setToDate] = useState("");
  const [page, setPage] = useState(1);

  // ── Auth check ──────────────────────────────────────────────────────────────

  useEffect(() => {
    fetchMe()
      .then((data) => {
        if (data.role !== "SuperAdmin") {
          router.replace(!data.isHostOffice ? "/budget-planning" : "/dashboard");
          return;
        }
        setAuthChecked(true);
      })
      .catch(() => router.replace("/login"));
  }, [router]);

  // ── Table name filter options (loaded once) ──────────────────────────────────

  useEffect(() => {
    if (!authChecked) return;
    listAuditLogTableNames()
      .then(setTableNames)
      .catch(() => setTableNames([])); // non-fatal — dropdown just stays empty
  }, [authChecked]);

  // ── Debounce actor search ─────────────────────────────────────────────────────

  useEffect(() => {
    const t = setTimeout(() => setDebouncedActorSearch(actorSearch), 300);
    return () => clearTimeout(t);
  }, [actorSearch]);

  // Reset to page 1 whenever a filter changes (a stale deep page number would
  // otherwise land past the end of a newly-narrowed result set).
  useEffect(() => {
    setPage(1);
  }, [tableFilter, actionFilter, debouncedActorSearch, fromDate, toDate]);

  // ── Load (server-side filter + page) ──────────────────────────────────────────

  // A `silent` load refetches the current page + filters WITHOUT flashing the loading
  // skeleton, clearing the grid, or surfacing a transient error — used by the auto-refresh
  // triggers so the visible rows never blink. A non-silent load (initial mount, filter/page
  // change, manual retry) behaves as before. A monotonic request sequence drops a stale
  // in-flight result when a newer load supersedes it (e.g. a filter change lands mid-poll).
  const load = useCallback(async (opts?: { silent?: boolean }) => {
    const silent = opts?.silent ?? false;
    if (silent && inFlightRef.current) return; // a refresh is already running — skip this tick
    const seq = ++reqSeqRef.current;
    inFlightRef.current = true;
    if (!silent) {
      setLoading(true);
      setFetchError(null);
    }
    try {
      const result = await listAuditLog({
        page,
        pageSize: PAGE_SIZE,
        tableName: tableFilter || undefined,
        action: actionFilter || undefined,
        actor: debouncedActorSearch || undefined,
        from: fromDate ? new Date(fromDate).toISOString() : undefined,
        // Inclusive end-of-day for the "to" date, since a bare date parses to midnight.
        to: toDate ? new Date(`${toDate}T23:59:59.999`).toISOString() : undefined,
      });
      if (seq !== reqSeqRef.current) return; // superseded by a newer load — drop this result
      setEntries(result.items);
      setTotalCount(result.totalCount);
      setLastUpdated(new Date());
      setFetchError(null);
    } catch (err) {
      if (seq !== reqSeqRef.current) return;
      // A failed silent refresh leaves the current grid untouched (the next tick may succeed);
      // only a user-initiated load surfaces the error banner.
      if (!silent) setFetchError(configErrorMessage(err, "Failed to load the audit log. Please try again."));
    } finally {
      if (seq === reqSeqRef.current) {
        if (!silent) setLoading(false);
        inFlightRef.current = false;
      }
    }
  }, [page, tableFilter, actionFilter, debouncedActorSearch, fromDate, toDate]);

  // Keep refs pointed at the latest values so the interval/visibility listeners can stay
  // registered once (deps: [authChecked]) instead of re-subscribing on every filter change.
  useEffect(() => { loadRef.current = load; }, [load]);
  useEffect(() => { bgRefreshRef.current = bgRefresh; }, [bgRefresh]);

  useEffect(() => {
    if (authChecked) load();
  }, [authChecked, load]);

  // Hydrate the background-refresh preference from localStorage (client-only, post-mount).
  useEffect(() => {
    setBgRefresh(localStorage.getItem(BG_REFRESH_KEY) === "true");
  }, []);

  function toggleBgRefresh() {
    setBgRefresh((v) => {
      const next = !v;
      try { localStorage.setItem(BG_REFRESH_KEY, String(next)); } catch { /* private mode — in-memory only */ }
      return next;
    });
  }

  // ── Auto-refresh: 15-min heartbeat + refetch-on-return (RAL-182) ──────────────
  //
  // The interval always ticks, but only refetches when the tab is visible OR the
  // background-refresh toggle is on — so an idle background tab costs nothing by
  // default, while an opted-in tab keeps Functions + SQL warm. Returning to the tab
  // (visibilitychange → visible, or window focus) refetches immediately, throttled so
  // rapid alt-tabbing doesn't spam the server.

  useEffect(() => {
    if (!authChecked) return;

    const interval = setInterval(() => {
      if (document.visibilityState === "visible" || bgRefreshRef.current) {
        loadRef.current({ silent: true });
      }
    }, POLL_MS);

    const onReturn = () => {
      if (document.visibilityState !== "visible") return;
      const now = Date.now();
      if (now - lastReturnRefreshRef.current < RETURN_THROTTLE_MS) return;
      lastReturnRefreshRef.current = now;
      loadRef.current({ silent: true });
    };
    document.addEventListener("visibilitychange", onReturn);
    window.addEventListener("focus", onReturn);

    return () => {
      clearInterval(interval);
      document.removeEventListener("visibilitychange", onReturn);
      window.removeEventListener("focus", onReturn);
    };
  }, [authChecked]);

  // ── Columns ───────────────────────────────────────────────────────────────────

  const columns: Column<AuditLogEntry>[] = [
    {
      key: "changedAt",
      header: "Timestamp",
      className: "whitespace-nowrap align-top",
      render: (e) => <span className="text-xs text-slate-600">{formatTimestamp(e.changedAt)}</span>,
    },
    {
      key: "tableName",
      header: "Feature / Table",
      className: "whitespace-nowrap align-top",
      render: (e) => <span className="font-mono text-xs text-slate-600">{e.tableName}</span>,
    },
    {
      key: "recordId",
      header: "Record",
      className: "whitespace-nowrap align-top",
      render: (e) => <span className="font-mono text-xs text-slate-600">{recordLabel(e)}</span>,
    },
    {
      key: "action",
      header: "Action",
      className: "whitespace-nowrap align-top",
      render: (e) => <ActionBadge action={e.action} />,
    },
    {
      key: "description",
      header: "Description",
      render: (e) => (
        <span className="text-xs text-slate-600 whitespace-pre-line">{e.description}</span>
      ),
    },
    {
      key: "actorName",
      header: "Username",
      className: "whitespace-nowrap align-top",
      render: (e) => <span className="text-slate-600">{e.actorName}</span>,
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

  const filtersActive =
    tableFilter !== "" || actionFilter !== "" || debouncedActorSearch !== "" || fromDate !== "" || toDate !== "";

  function resetFilters() {
    setTableFilter("");
    setActionFilter("");
    setActorSearch("");
    setFromDate("");
    setToDate("");
  }

  // ── Render ────────────────────────────────────────────────────────────────────

  return (
    <div className="min-h-full bg-slate-100 font-sans">
      <div className="max-w-7xl mx-auto px-3 py-4 sm:px-6 sm:py-6 space-y-4">
        {/* Header */}
        <ConfigPageHeader
          title="Audit Log"
          description="Every recorded create, update, and deactivation across Configuration, Budget Planning, and User Management."
        />

        {/* Filter bar */}
        <div className="flex flex-wrap items-end gap-3 bg-white border border-slate-200 px-4 py-3">
          <div>
            <label className="block text-[11px] font-medium text-slate-600 mb-1">Feature / Table</label>
            <select
              value={tableFilter}
              onChange={(e) => setTableFilter(e.target.value)}
              className="px-3 py-2 text-sm border border-slate-200 bg-white focus:outline-none focus:ring-2 focus:ring-green-600 min-w-[180px]"
            >
              <option value="">All tables</option>
              {tableNames.map((t) => (
                <option key={t} value={t}>{t}</option>
              ))}
            </select>
          </div>

          <div>
            <label className="block text-[11px] font-medium text-slate-600 mb-1">Action</label>
            <select
              value={actionFilter}
              onChange={(e) => setActionFilter(e.target.value)}
              className="px-3 py-2 text-sm border border-slate-200 bg-white focus:outline-none focus:ring-2 focus:ring-green-600"
            >
              <option value="">All actions</option>
              {ACTIONS.map((a) => (
                <option key={a} value={a}>{a}</option>
              ))}
            </select>
          </div>

          <div>
            <label className="block text-[11px] font-medium text-slate-600 mb-1">Username</label>
            <input
              value={actorSearch}
              onChange={(e) => setActorSearch(e.target.value)}
              placeholder="Search by name or username…"
              className="px-3 py-2 text-sm border border-slate-200 bg-white focus:outline-none focus:ring-2 focus:ring-green-600 min-w-[200px]"
            />
          </div>

          <div>
            <label className="block text-[11px] font-medium text-slate-600 mb-1">From</label>
            <input
              type="date"
              value={fromDate}
              onChange={(e) => setFromDate(e.target.value)}
              className="px-3 py-2 text-sm border border-slate-200 bg-white focus:outline-none focus:ring-2 focus:ring-green-600"
            />
          </div>

          <div>
            <label className="block text-[11px] font-medium text-slate-600 mb-1">To</label>
            <input
              type="date"
              value={toDate}
              onChange={(e) => setToDate(e.target.value)}
              className="px-3 py-2 text-sm border border-slate-200 bg-white focus:outline-none focus:ring-2 focus:ring-green-600"
            />
          </div>

          {filtersActive && (
            <button
              onClick={resetFilters}
              className="text-sm text-slate-600 hover:text-slate-600 transition-colors px-1 pb-2"
            >
              Reset
            </button>
          )}

          {/* Auto-refresh controls (RAL-182) — right-aligned; wraps below the filters on narrow screens */}
          <div className="ml-auto flex items-end gap-3 pb-1">
            {lastUpdated && (
              <span className="text-[11px] text-slate-500 pb-1" title="Grid refreshes automatically every 15 minutes and whenever you return to this tab.">
                Updated {formatClock(lastUpdated)}
              </span>
            )}
            <label className="flex items-center gap-2 pb-1 text-xs text-slate-600 cursor-pointer select-none whitespace-nowrap">
              <input
                type="checkbox"
                checked={bgRefresh}
                onChange={toggleBgRefresh}
                className="accent-green-600"
              />
              Keep refreshing when tab is hidden
            </label>
            {bgRefresh && (
              <span
                className="flex items-center gap-1 pb-1 text-[11px] text-amber-600 whitespace-nowrap"
                title="This tab keeps polling while hidden, so the server stays awake (and won't scale to sleep) until you turn this off or close the tab."
              >
                <span className="w-1.5 h-1.5 bg-amber-500 rounded-full" aria-hidden />
                keeps the server awake
              </span>
            )}
          </div>
        </div>

        {/* Table */}
        <DataTable
          columns={columns}
          rows={entries}
          rowKey={(e) => e.id}
          loading={loading}
          error={fetchError}
          onRetry={() => load()}
          emptyMessage={filtersActive ? "No audit entries match your filters." : "No audit entries yet."}
          rowNoun={["entry", "entries"]}
          serverPagination={{ page, pageSize: PAGE_SIZE, totalCount, onPageChange: setPage }}
          minWidth={950}
        />
      </div>
    </div>
  );
}
