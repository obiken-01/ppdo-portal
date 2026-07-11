"use client";

/**
 * Budget Planning Dashboard (RAL-80, RAL-60).
 *
 * The readiness hub (RAL-60) — Allocation setup / LDIP / AIP / WFP, 2×2 — is the
 * primary view for everyone and replaces the old global 3-stat-card row:
 *   - Office user: always locked to their own office (OfficeReadinessPanels).
 *   - PPDO with a specific office picked: same per-office panels.
 *   - PPDO with "All Offices" (the default): GlobalReadinessPanels reuses the
 *     already-loaded global LDIP/AIP/WFP summary — Allocation setup has no
 *     global equivalent (it's inherently per-office) and prompts to pick one.
 *
 * PPDO additionally gets the WFP-status-by-office DataTable (all offices at a
 * glance). Recent activity is shown for both views.
 */

import { useCallback, useEffect, useState } from "react";
import Link from "next/link";
import { getDashboard, getOfficeDashboard, getRecentActivity } from "@/lib/budget-planning";
import { listOffices } from "@/lib/config";
import { useMe } from "@/lib/me-cache";
import { formatMoney } from "@/lib/money";
import DataTable, { Column } from "@/components/ui/DataTable";
import OfficeSelect from "@/components/ui/OfficeSelect";
import type {
  OfficeDashboard,
  OfficeResponse,
  PlanningDashboard,
  RecentActivity,
  WfpOfficeStatus,
} from "@/types";

// ---------------------------------------------------------------------------
// Spinner / error helpers
// ---------------------------------------------------------------------------

function Spinner() {
  return (
    <span className="inline-block w-5 h-5 border-2 border-slate-300 border-t-transparent rounded-full animate-spin" />
  );
}

// ---------------------------------------------------------------------------
// Status badge
// ---------------------------------------------------------------------------

function WfpStatusBadge({ status }: { status: string }) {
  const cls =
    status === "Final"
      ? "bg-green-100 text-green-700"
      : status === "Draft"
      ? "bg-amber-100 text-amber-700"
      : "bg-slate-100 text-slate-600";
  return <span className={`px-2 py-0.5 text-xs font-medium ${cls}`}>{status}</span>;
}

// ---------------------------------------------------------------------------
// WFP DataTable columns
// ---------------------------------------------------------------------------

const WFP_COLUMNS: Column<WfpOfficeStatus>[] = [
  {
    key: "officeName",
    header: "OFFICE",
    sortable: true,
  },
  {
    key: "wfpStatus",
    header: "STATUS",
    sortable: true,
    render: (r) => <WfpStatusBadge status={r.wfpStatus} />,
  },
  {
    key: "action",
    header: "ACTION",
    align: "right",
    render: (r) => {
      if (r.aipRecordId == null || r.wfpStatus === "Not started") return <span className="text-slate-600">—</span>;
      const href = `/budget-planning/wfp?aipId=${r.aipRecordId}&officeId=${r.officeId}`;
      const label = r.wfpStatus === "Final" ? "View" : "Open";
      return (
        <Link href={href} className="text-sm font-medium text-green-600 hover:text-green-700">
          {label}
        </Link>
      );
    },
  },
];

// ---------------------------------------------------------------------------
// Readiness hub — Allocation setup / LDIP / AIP / WFP panels for one office (RAL-60)
// ---------------------------------------------------------------------------

function ReadinessPanel({
  title,
  href,
  children,
}: {
  title: string;
  href: string;
  children: React.ReactNode;
}) {
  return (
    <div className="bg-white border border-slate-200 p-4 flex flex-col">
      <div className="flex items-center justify-between mb-2">
        <h3 className="text-xs font-semibold text-slate-600 uppercase tracking-wide">{title}</h3>
        <Link href={href} className="text-xs font-medium text-green-600 hover:text-green-700">
          Open →
        </Link>
      </div>
      <div className="flex-1 text-sm text-slate-700 space-y-1.5">{children}</div>
    </div>
  );
}

function ReadinessSkeleton() {
  return (
    <div className="grid grid-cols-1 sm:grid-cols-2 gap-4">
      {["Allocation Setup", "LDIP", "AIP", "WFP"].map((t) => (
        <div key={t} className="bg-white border border-slate-200 p-4">
          <p className="text-xs font-semibold text-slate-600 uppercase tracking-wide mb-2">{t}</p>
          <Spinner />
        </div>
      ))}
    </div>
  );
}

/**
 * "All Offices" variant — no single office is selected, so Allocation setup
 * (inherently per-office) can't be shown; LDIP/AIP/WFP reuse the global
 * summary already loaded for the page (no extra request).
 */
function GlobalReadinessPanels({
  fiscalYear,
  dashboard,
}: {
  fiscalYear: number | null;
  dashboard: PlanningDashboard;
}) {
  const { ldip, aip, wfp, wfpByOffice, allocation } = dashboard;

  const aipDraftCount = aip.breakdown.find((b) => b.status === "Draft")?.count ?? 0;
  const aipFinalCount = aip.breakdown.find((b) => b.status === "Final")?.count ?? 0;
  const aipArchivedCount = aip.breakdown.find((b) => b.status === "Archived")?.count ?? 0;

  const wfpFinalCount = wfpByOffice.filter((r) => r.wfpStatus === "Final").length;
  const wfpDraftCount = wfpByOffice.filter((r) => r.wfpStatus === "Draft").length;
  const wfpNotStartedCount = wfpByOffice.filter((r) => r.wfpStatus === "Not started").length;

  return (
    <div className="grid grid-cols-1 sm:grid-cols-2 gap-4">
      <ReadinessPanel title="Allocation Setup" href="/budget-planning/allocation">
        <p className="font-medium">
          {allocation.fullySetupCount} of {allocation.totalOffices} office(s) fully set up
        </p>
        <p className="text-xs text-slate-600">
          {allocation.incompleteCount} incomplete · {allocation.notStartedCount} not started
        </p>
        <p className="text-xs text-slate-600">
          Pick a specific office above for its ceiling/allocation/PPA-assignment detail.
        </p>
      </ReadinessPanel>

      <ReadinessPanel title="LDIP" href="/budget-planning/ldip">
        <p className="font-medium">{ldip.total} program(s) · all offices</p>
        <p className="text-xs text-slate-600">
          {ldip.breakdown.map((b) => `${b.count} ${b.status}`).join(" · ") || "No records yet"}
        </p>
      </ReadinessPanel>

      <ReadinessPanel title="AIP" href="/budget-planning/aip">
        <p className="font-medium">{aip.total} record(s) · all offices</p>
        <p className="text-xs text-slate-600">
          {aipDraftCount} Draft · {aipFinalCount} Final
          {aipArchivedCount > 0 && ` · ${aipArchivedCount} Archived`}
        </p>
      </ReadinessPanel>

      <ReadinessPanel title="WFP" href="/budget-planning/wfp">
        <p className="font-medium">
          {wfpFinalCount} Final · {wfpDraftCount} Draft
        </p>
        <p className="text-xs text-slate-600">
          {wfpNotStartedCount} not started · {wfp.activeOfficeCount} active offices — FY {fiscalYear ?? "…"}
        </p>
      </ReadinessPanel>
    </div>
  );
}

/** Per-office variant — Allocation setup / LDIP / AIP / WFP for one specific office+FY. */
function OfficeReadinessPanels({
  officeId,
  fiscalYear,
  dashboard,
  loading,
  error,
  wfpRow,
}: {
  officeId: number;
  fiscalYear: number | null;
  dashboard: OfficeDashboard | null;
  loading: boolean;
  error: string | null;
  wfpRow: WfpOfficeStatus | null;
}) {
  const qs = `?officeId=${officeId}`;

  if (loading) return <ReadinessSkeleton />;

  if (error) {
    return <div className="bg-white border border-slate-200 p-4 text-sm text-red-500">{error}</div>;
  }

  if (!dashboard) return null;

  const { allocation, ldip, aip } = dashboard;

  // Office-wide approximation of the setup-complete gate (Allocation_Requirements.md §4)
  // — the real gate is per-division; this flags whether the office has even started.
  const missingSetup: string[] = [];
  if (allocation.ceilingAmount == null) missingSetup.push("ceiling");
  if (allocation.allocated <= 0) missingSetup.push("division allocation");
  if (allocation.assignedProgramCount === 0) missingSetup.push("PPA assignment");
  const isSetupComplete = missingSetup.length === 0;

  const wfpHref =
    wfpRow?.aipRecordId != null
      ? `/budget-planning/wfp?aipId=${wfpRow.aipRecordId}&officeId=${officeId}`
      : `/budget-planning/wfp${qs}`;
  const wfpActionLabel =
    wfpRow?.wfpStatus === "Final" ? "View" : wfpRow?.wfpStatus === "Draft" ? "Continue" : "Start";

  return (
    <div className="grid grid-cols-1 sm:grid-cols-2 gap-4">
      {/* Allocation setup */}
      <ReadinessPanel title="Allocation Setup" href={`/budget-planning/allocation${qs}`}>
        {isSetupComplete ? (
          <span className="inline-flex items-center gap-1 text-xs font-semibold text-green-700 bg-green-100 px-1.5 py-0.5">
            ✓ Setup complete
          </span>
        ) : (
          <span className="inline-flex items-center gap-1 text-xs font-semibold text-amber-700 bg-amber-100 px-1.5 py-0.5">
            ⚠ Setup incomplete — missing {missingSetup.join(", ")}
          </span>
        )}
        <p>
          Ceiling:{" "}
          <span className="font-medium">
            {allocation.ceilingAmount != null ? `₱${formatMoney(allocation.ceilingAmount)}` : "Not set"}
          </span>
        </p>
        {allocation.ceilingAmount != null && (
          <p className={allocation.isOverAllocated ? "text-red-600 font-medium" : "text-slate-600"}>
            Allocated ₱{formatMoney(allocation.allocated)} of ₱{formatMoney(allocation.ceilingAmount)} ·
            Remaining ₱{formatMoney(allocation.remaining ?? 0)}
            {allocation.isOverAllocated && " (over)"}
          </p>
        )}
        <p className="text-xs text-slate-600">
          PPAs: {allocation.assignedProgramCount} assigned
          {allocation.unassignedProgramCount > 0 && (
            <span className="ml-1 text-amber-600 font-medium">
              · {allocation.unassignedProgramCount} unassigned
            </span>
          )}
        </p>
      </ReadinessPanel>

      {/* LDIP */}
      <ReadinessPanel title="LDIP" href={`/budget-planning/ldip${qs}`}>
        {ldip.scopingSupported ? (
          <>
            <p className="font-medium">{ldip.total} record(s) · this office</p>
            <p className="text-xs text-slate-600">
              {ldip.breakdown.map((b) => `${b.count} ${b.status}`).join(" · ") || "No records yet"}
            </p>
          </>
        ) : (
          <p className="text-xs text-slate-600">Office scoping pending (RAL-61).</p>
        )}
      </ReadinessPanel>

      {/* AIP */}
      <ReadinessPanel title="AIP" href={`/budget-planning/aip${qs}`}>
        {aip.exists ? (
          <>
            <p className="font-medium">
              AIP created {aip.status && <span className="text-slate-600 font-normal">({aip.status})</span>}
            </p>
            <p className="text-xs text-slate-600">
              {aip.programCount} program(s) · {aip.projectCount} project(s) · {aip.activityCount} activity(ies)
            </p>
          </>
        ) : (
          <p className="text-slate-600 text-xs">
            No AIP for this office yet {fiscalYear ? `(FY ${fiscalYear})` : ""}.
          </p>
        )}
      </ReadinessPanel>

      {/* WFP */}
      <ReadinessPanel title="WFP" href={`/budget-planning/wfp${qs}`}>
        {wfpRow && wfpRow.wfpStatus !== "Not started" ? (
          <>
            <p className="font-medium">
              WFP {wfpRow.wfpStatus === "Final" ? "finalized" : "in progress"}{" "}
              <span className="text-slate-600 font-normal">({wfpRow.wfpStatus})</span>
            </p>
            <p className="text-xs text-slate-600">
              <Link href={wfpHref} className="font-medium text-green-600 hover:text-green-700">
                {wfpActionLabel} →
              </Link>
            </p>
          </>
        ) : (
          <p className="text-slate-600 text-xs">
            No WFP started for this office yet {fiscalYear ? `(FY ${fiscalYear})` : ""}.
          </p>
        )}
      </ReadinessPanel>
    </div>
  );
}

// ---------------------------------------------------------------------------
// Page
// ---------------------------------------------------------------------------

export default function BudgetPlanningPage() {
  // On permission failure, an office user must NOT be sent to /dashboard — the
  // office-user gate in the portal layout would bounce them straight back here,
  // creating an infinite redirect loop. Send office users to /account (a terminal
  // page they can always reach); PPDO users still fall back to /dashboard.
  const user = useMe(
    (me) => me.canAccessBudgetPlanning,
    (me) => (me.officeId != null ? "/account" : "/dashboard"),
  );

  const [fiscalYear, setFiscalYear] = useState<number | null>(null);
  const [selectedOffice, setSelectedOffice] = useState<number | null>(null);

  const [dashboard, setDashboard] = useState<PlanningDashboard | null>(null);
  const [activity, setActivity] = useState<RecentActivity[]>([]);

  const [dashboardLoading, setDashboardLoading] = useState(true);
  const [activityLoading, setActivityLoading] = useState(true);
  const [dashboardError, setDashboardError] = useState<string | null>(null);
  const [activityError, setActivityError] = useState<string | null>(null);

  const [officeDashboard, setOfficeDashboard] = useState<OfficeDashboard | null>(null);
  const [officeDashboardLoading, setOfficeDashboardLoading] = useState(false);
  const [officeDashboardError, setOfficeDashboardError] = useState<string | null>(null);

  const [officeList, setOfficeList] = useState<OfficeResponse[]>([]);

  // ── Office list load (for the dropdown — OfficeSelect sorts by ref code) ────

  useEffect(() => {
    listOffices({ active: "true" }).catch(() => []).then(setOfficeList);
  }, []);

  // ── Dashboard load ─────────────────────────────────────────────────────────

  const loadDashboard = useCallback(
    (fy?: number) => {
      setDashboardLoading(true);
      setDashboardError(null);
      getDashboard(fy)
        .then((data) => {
          setDashboard(data);
          if (fy == null) setFiscalYear(data.fiscalYear);
        })
        .catch(() => setDashboardError("Failed to load dashboard data."))
        .finally(() => setDashboardLoading(false));
    },
    []
  );

  // ── Initial data load ──────────────────────────────────────────────────────

  useEffect(() => {
    loadDashboard();
  }, [loadDashboard]);

  useEffect(() => {
    if (!user) return;
    setActivityLoading(true);
    getRecentActivity(user.officeId ?? undefined)
      .then(setActivity)
      .catch(() => setActivityError("Failed to load recent activity."))
      .finally(() => setActivityLoading(false));
  }, [user]);

  // ── Derived ────────────────────────────────────────────────────────────────

  const isPpdo = user?.officeId == null;

  // The office this readiness hub is scoped to: PPDO picks any via the selector;
  // an office user is always locked to their own.
  const effectiveOfficeId = isPpdo ? selectedOffice : user?.officeId ?? null;

  const filteredWfpRows =
    selectedOffice == null
      ? (dashboard?.wfpByOffice ?? [])
      : (dashboard?.wfpByOffice ?? []).filter((r) => r.officeId === selectedOffice);

  const effectiveOfficeWfpRow =
    effectiveOfficeId == null
      ? null
      : (dashboard?.wfpByOffice ?? []).find((r) => r.officeId === effectiveOfficeId) ?? null;

  const ownOfficeLabel =
    user?.officeCode && user?.officeName
      ? `${user.officeCode} — ${user.officeName}`
      : user?.officeCode ?? user?.officeName ?? "Your Office";

  const navHref = (path: string) =>
    effectiveOfficeId != null ? `${path}?officeId=${effectiveOfficeId}` : path;

  // ── Office readiness hub load ────────────────────────────────────────────

  useEffect(() => {
    if (effectiveOfficeId == null || fiscalYear == null) {
      setOfficeDashboard(null);
      return;
    }
    let cancelled = false;
    setOfficeDashboardLoading(true);
    setOfficeDashboardError(null);
    getOfficeDashboard(effectiveOfficeId, fiscalYear)
      .then((data) => { if (!cancelled) setOfficeDashboard(data); })
      .catch(() => { if (!cancelled) setOfficeDashboardError("Failed to load office readiness data."); })
      .finally(() => { if (!cancelled) setOfficeDashboardLoading(false); });
    return () => { cancelled = true; };
  }, [effectiveOfficeId, fiscalYear]);

  // ── Render ────────────────────────────────────────────────────────────────

  return (
    <div className="min-h-full bg-slate-100 font-sans">
      <div className="max-w-6xl mx-auto px-6 py-6 space-y-5">

        {/* Header */}
        <div>
          <h1 className="text-lg font-bold text-slate-800">Budget Planning Dashboard</h1>
          <p className="text-sm text-slate-600">
            {isPpdo
              ? "FY overview · PPDO view — all offices"
              : `FY overview · Office view — ${ownOfficeLabel}`}
          </p>
        </div>

        {/* Selectors row */}
        <div className="flex flex-wrap items-center gap-3">
          {/* FY selector */}
          <div className="flex items-center gap-2">
            <label className="text-xs font-semibold text-slate-600 uppercase tracking-wide">
              Fiscal Year
            </label>
            <select
              className="border border-slate-200 bg-white text-sm text-slate-700 px-3 py-1.5 focus:outline-none focus:ring-1 focus:ring-green-500"
              value={fiscalYear ?? ""}
              onChange={(e) => {
                const fy = Number(e.target.value);
                setFiscalYear(fy);
                setSelectedOffice(null);
                loadDashboard(fy);
              }}
              disabled={dashboardLoading}
            >
              {(dashboard?.availableFiscalYears ?? (fiscalYear ? [fiscalYear] : [])).map((fy) => (
                <option key={fy} value={fy}>
                  FY {fy}
                </option>
              ))}
            </select>
          </div>

          {/* PPDO: office filter selector / Office user: locked display */}
          {isPpdo ? (
            <div className="flex items-center gap-2">
              <label className="text-xs font-semibold text-slate-600 uppercase tracking-wide">
                Office
              </label>
              <OfficeSelect
                className="w-64"
                offices={officeList}
                value={selectedOffice}
                onChange={setSelectedOffice}
                allOptionLabel="All Offices"
                disabled={dashboardLoading || officeList.length === 0}
              />
            </div>
          ) : (
            <div className="flex items-center gap-2">
              <span className="text-xs font-semibold text-slate-600 uppercase tracking-wide">
                Office
              </span>
              <span className="text-sm text-slate-700 bg-white border border-slate-200 px-3 py-1.5">
                {ownOfficeLabel}
              </span>
            </div>
          )}
        </div>

        {/* Nav buttons — carry the selected office into each flow */}
        <div className="flex gap-2">
          {[
            { label: "LDIP", href: navHref("/budget-planning/ldip") },
            { label: "AIP",  href: navHref("/budget-planning/aip")  },
            { label: "WFP",  href: navHref("/budget-planning/wfp")  },
          ].map(({ label, href }) => (
            <Link
              key={label}
              href={href}
              className="px-5 py-2 bg-white border border-slate-200 text-sm font-semibold text-slate-700 hover:bg-green-50 hover:border-green-300 hover:text-green-700 transition-colors"
            >
              {label}
            </Link>
          ))}
        </div>

        {/* ── Readiness hub: Allocation setup / LDIP / AIP / WFP ────────────── */}
        {effectiveOfficeId != null ? (
          <OfficeReadinessPanels
            officeId={effectiveOfficeId}
            fiscalYear={fiscalYear}
            dashboard={officeDashboard}
            loading={officeDashboardLoading}
            error={officeDashboardError}
            wfpRow={effectiveOfficeWfpRow}
          />
        ) : dashboardLoading ? (
          <ReadinessSkeleton />
        ) : dashboard ? (
          <GlobalReadinessPanels fiscalYear={fiscalYear} dashboard={dashboard} />
        ) : null}

        {/* ── PPDO-only: WFP status by office ─────────────────────────────── */}
        {isPpdo && (
          <div className="bg-white border border-slate-200">
            <div className="px-5 py-4 border-b border-slate-100">
              <h2 className="text-sm font-semibold text-slate-700">
                WFP Status by Office — FY {fiscalYear ?? "…"}
              </h2>
              <p className="text-xs text-slate-600 mt-0.5">
                Sorted: Not started → Draft → Final · No appropriation amounts shown
              </p>
            </div>
            <DataTable
              columns={WFP_COLUMNS}
              rows={filteredWfpRows}
              rowKey={(r) => r.officeId}
              loading={dashboardLoading}
              error={dashboardError}
              emptyMessage="No active offices found."
              pageSize={20}
            />
          </div>
        )}

        {/* ── Recent activity (both views) ──────────────────────────────────── */}
        <div className="bg-white border border-slate-200">
          <div className="px-5 py-4 border-b border-slate-100">
            <h2 className="text-sm font-semibold text-slate-700">Recent Activity</h2>
            <p className="text-xs text-slate-600 mt-0.5">
              {isPpdo ? "All offices (PPDO view)" : `${ownOfficeLabel} only`}
            </p>
          </div>
          <div className="divide-y divide-slate-50">
            {activityLoading ? (
              <div className="px-5 py-6 flex items-center gap-2 text-sm text-slate-600">
                <Spinner />
                <span>Loading activity…</span>
              </div>
            ) : activityError ? (
              <div className="px-5 py-4 text-sm text-red-500">{activityError}</div>
            ) : activity.length === 0 ? (
              <div className="px-5 py-6 text-sm text-slate-600">No recent activity yet.</div>
            ) : (
              activity.map((entry) => (
                <div key={entry.id} className="px-5 py-3 flex items-start justify-between gap-4">
                  <div className="text-sm text-slate-700">
                    <span className="font-medium">{entry.actorName}</span>
                    {" — "}
                    <span className="text-slate-600">
                      {entry.action.toLowerCase()} on {entry.tableName} #{entry.recordId}
                    </span>
                  </div>
                  <span className="text-xs text-slate-600 whitespace-nowrap flex-shrink-0">
                    {new Date(entry.changedAt).toLocaleString()}
                  </span>
                </div>
              ))
            )}
          </div>
        </div>

      </div>
    </div>
  );
}
