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

import { useCallback, useEffect, useState } from "react";
import { useRouter } from "next/navigation";
import { fetchMe } from "@/lib/me-cache";
import { configErrorMessage, listAuditLog, listAuditLogTableNames } from "@/lib/config";
import DataTable, { type Column } from "@/components/ui/DataTable";
import type { AuditLogEntry } from "@/types";

const PAGE_SIZE = 50;
const ACTIONS = ["CREATE", "UPDATE", "DELETE"] as const;

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
          router.replace(data.officeId != null ? "/budget-planning" : "/dashboard");
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

  const load = useCallback(async () => {
    setLoading(true);
    setFetchError(null);
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
      setEntries(result.items);
      setTotalCount(result.totalCount);
    } catch (err) {
      setFetchError(configErrorMessage(err, "Failed to load the audit log. Please try again."));
    } finally {
      setLoading(false);
    }
  }, [page, tableFilter, actionFilter, debouncedActorSearch, fromDate, toDate]);

  useEffect(() => {
    if (authChecked) load();
  }, [authChecked, load]);

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
      render: (e) => <span className="font-mono text-xs text-slate-700">{e.tableName}</span>,
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
        <span className="text-xs text-slate-700 whitespace-pre-line">{e.description}</span>
      ),
    },
    {
      key: "actorName",
      header: "Username",
      className: "whitespace-nowrap align-top",
      render: (e) => <span className="text-slate-700">{e.actorName}</span>,
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
      <div className="max-w-7xl mx-auto px-6 py-6 space-y-4">
        {/* Header */}
        <div>
          <h1 className="text-lg font-bold text-slate-800">Audit Log</h1>
          <p className="text-sm text-slate-600">
            Every recorded create, update, and deactivation across Configuration, Budget
            Planning, and User Management.
          </p>
        </div>

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
        </div>

        {/* Table */}
        <DataTable
          columns={columns}
          rows={entries}
          rowKey={(e) => e.id}
          loading={loading}
          error={fetchError}
          onRetry={load}
          emptyMessage={filtersActive ? "No audit entries match your filters." : "No audit entries yet."}
          rowNoun={["entry", "entries"]}
          serverPagination={{ page, pageSize: PAGE_SIZE, totalCount, onPageChange: setPage }}
        />
      </div>
    </div>
  );
}
