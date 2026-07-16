"use client";

/**
 * Budget Planning Dashboard (RAL-80, RAL-60; PPDO-scoped rework — v1.4.5, RAL-161/162).
 *
 * The readiness hub (RAL-60) — Allocation setup / LDIP / AIP / WFP, 2×2 — is the
 * primary view for everyone:
 *   - Office user (e.g. GSO): always locked to their own office (OfficeReadinessPanels).
 *   - PPDO user: the backend's GET /budget-planning/dashboard now always resolves the
 *     PPDO office internally — there is no "All Offices" mode or office picker any more
 *     (Budget Planning is effectively PPDO-only in practice). OfficeReadinessPanels is
 *     driven by dashboard.officeId once the PPDO-scoped dashboard call resolves.
 *
 * PPDO additionally gets two new sections (v1.4.5): "Ceiling and allocation by fund" —
 * one pie chart per active funding source, one slice per division's allocation plus the
 * unallocated remainder of that fund's office-wide ceiling — and "WFP status by division"
 * — one row per division (WFP status, activity coverage, total allocated), expandable to
 * the per-fund breakdown. Both come back already server-side clamped to the caller's own
 * division for division-scoped Staff (never trust a client-side filter for this).
 */

import { useCallback, useEffect, useRef, useState } from "react";
import Link from "next/link";
import Chart from "chart.js/auto";
import { getDashboard, getOfficeDashboard, getRecentActivity } from "@/lib/budget-planning";
import { useMe } from "@/lib/me-cache";
import { formatMoney } from "@/lib/money";
import type {
  DivisionWfpStatus,
  FundCeiling,
  OfficeDashboard,
  PpdoDashboard,
  RecentActivity,
} from "@/types";

// ---------------------------------------------------------------------------
// Spinner
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

/** Allocation setup / LDIP / AIP / WFP for one specific office+FY. */
function OfficeReadinessPanels({
  officeId,
  fiscalYear,
  dashboard,
  loading,
  error,
  wfpStatus,
  wfpAipRecordId,
}: {
  officeId: number;
  fiscalYear: number | null;
  dashboard: OfficeDashboard | null;
  loading: boolean;
  error: string | null;
  wfpStatus: string | null;
  wfpAipRecordId: number | null;
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
    wfpAipRecordId != null
      ? `/budget-planning/wfp/entry?aipId=${wfpAipRecordId}&officeId=${officeId}`
      : `/budget-planning/wfp/entry${qs}`;
  const wfpActionLabel = wfpStatus === "Final" ? "View" : wfpStatus === "Draft" ? "Continue" : "Start";

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
      <ReadinessPanel title="WFP" href={`/budget-planning/wfp/entry${qs}`}>
        {wfpStatus != null && wfpStatus !== "Not started" ? (
          <>
            <p className="font-medium">
              WFP {wfpStatus === "Final" ? "finalized" : "in progress"}{" "}
              <span className="text-slate-600 font-normal">({wfpStatus})</span>
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
// Ceiling and allocation by fund — one pie chart per active funding source (v1.4.5)
// ---------------------------------------------------------------------------

const DIVISION_COLORS = ["#7F77DD", "#1D9E75", "#D85A30", "#D4537E", "#378ADD", "#BA7517", "#639922", "#993556"];
const REMAINING_COLOR = "#B4B2A9";

function divisionColor(index: number): string {
  return DIVISION_COLORS[index % DIVISION_COLORS.length];
}

function pesoShort(n: number): string {
  if (Math.abs(n) >= 1_000_000) return `₱${(n / 1_000_000).toFixed(1)}M`;
  if (Math.abs(n) >= 1_000) return `₱${Math.round(n / 1000)}K`;
  return `₱${Math.round(n)}`;
}

const centerTextPlugin = {
  id: "centerText",
  afterDraw(chart: Chart) {
    const total = (chart.config as unknown as { _ceilingTotal?: number })._ceilingTotal;
    if (total == null) return;
    const { ctx, chartArea } = chart;
    const cx = (chartArea.left + chartArea.right) / 2;
    const cy = (chartArea.top + chartArea.bottom) / 2;
    ctx.save();
    ctx.textAlign = "center";
    ctx.textBaseline = "middle";
    ctx.font = "600 14px sans-serif";
    ctx.fillStyle = "#1e293b";
    ctx.fillText(pesoShort(total), cx, cy - 8);
    ctx.font = "400 11px sans-serif";
    ctx.fillStyle = "#64748b";
    ctx.fillText("ceiling", cx, cy + 10);
    ctx.restore();
  },
};

const percentLabelPlugin = {
  id: "percentLabel",
  afterDatasetsDraw(chart: Chart) {
    const meta = chart.getDatasetMeta(0);
    const dataset = chart.data.datasets[0];
    const total = (dataset.data as number[]).reduce((a, b) => a + b, 0);
    if (total <= 0) return;
    const { ctx } = chart;
    ctx.save();
    ctx.font = "600 11px sans-serif";
    ctx.fillStyle = "#fff";
    ctx.textAlign = "center";
    ctx.textBaseline = "middle";
    meta.data.forEach((element, i) => {
      const value = (dataset.data as number[])[i];
      const pct = Math.round((value / total) * 100);
      if (pct < 5) return;
      const pos = (element as unknown as { tooltipPosition: () => { x: number; y: number } }).tooltipPosition();
      ctx.fillText(`${pct}%`, pos.x, pos.y);
    });
    ctx.restore();
  },
};

function FundCeilingCard({ fund }: { fund: FundCeiling }) {
  const canvasRef = useRef<HTMLCanvasElement>(null);
  const chartRef = useRef<Chart | null>(null);

  useEffect(() => {
    if (!canvasRef.current) return;

    const labels = [...fund.byDivision.map((d) => d.divisionCode ?? d.divisionName), "Remaining"];
    const data = [...fund.byDivision.map((d) => d.amount), Math.max(fund.remaining, 0)];
    const colors = [...fund.byDivision.map((_, i) => divisionColor(i)), REMAINING_COLOR];

    chartRef.current?.destroy();
    const chart = new Chart<"doughnut", number[], string>(canvasRef.current, {
      type: "doughnut",
      data: { labels, datasets: [{ data, backgroundColor: colors, borderWidth: 2, borderColor: "#fff" }] },
      options: {
        cutout: "62%",
        plugins: {
          legend: { display: false },
          tooltip: { callbacks: { label: (c) => `${c.label}: ₱${formatMoney(c.raw as number)}` } },
        },
      },
      plugins: [centerTextPlugin, percentLabelPlugin],
    });
    (chart.config as unknown as { _ceilingTotal: number })._ceilingTotal = fund.ceiling;
    chart.update();
    chartRef.current = chart;

    return () => chart.destroy();
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [fund.fundingSourceId, fund.ceiling, fund.remaining, JSON.stringify(fund.byDivision)]);

  return (
    <div className="bg-white border border-slate-200 p-4">
      <p className="text-sm font-semibold text-slate-700 mb-2">{fund.fundName}</p>
      <div className="relative h-40 mb-2">
        <canvas ref={canvasRef} />
      </div>
      <p className="text-xs mb-1.5">
        <span
          className="inline-block w-2 h-2 mr-1.5 align-middle"
          style={{ backgroundColor: REMAINING_COLOR, borderRadius: "50%" }}
        />
        <span className="text-slate-600">Remaining</span>
        <span className="float-right font-medium text-slate-700">₱{formatMoney(fund.remaining)}</span>
      </p>
      {fund.byDivision.map((d, i) => (
        <p key={d.divisionId} className="text-xs mb-1">
          <span
            className="inline-block w-2 h-2 mr-1.5 align-middle"
            style={{ backgroundColor: divisionColor(i), borderRadius: "50%" }}
          />
          <span className="text-slate-600">{d.divisionCode ?? d.divisionName}</span>
          <span className="float-right text-slate-700">₱{formatMoney(d.amount)}</span>
        </p>
      ))}
    </div>
  );
}

// ---------------------------------------------------------------------------
// WFP status by division — click a row to see its per-fund allocation breakdown
// ---------------------------------------------------------------------------

function DivisionRow({ division }: { division: DivisionWfpStatus }) {
  const [expanded, setExpanded] = useState(false);

  return (
    <>
      <tr
        className="border-t border-slate-100 cursor-pointer hover:bg-slate-50"
        onClick={() => setExpanded((e) => !e)}
      >
        <td className="px-4 py-2.5 text-sm text-slate-700">
          <span
            className={`inline-block mr-1.5 transition-transform text-slate-400 ${expanded ? "rotate-90" : ""}`}
          >
            ›
          </span>
          {division.divisionName}
        </td>
        <td className="px-4 py-2.5">
          <WfpStatusBadge status={division.wfpStatus} />
        </td>
        <td className="px-4 py-2.5 text-sm text-right text-slate-700">
          {division.activitiesWithExpenditures} / {division.totalActivities}
        </td>
        <td className="px-4 py-2.5 text-sm text-right font-medium text-slate-700">
          ₱{formatMoney(division.totalAllocated)}
        </td>
      </tr>
      {expanded && (
        <tr>
          <td colSpan={4} className="p-0">
            <table className="w-full">
              <tbody>
                {division.allocationByFund.length === 0 ? (
                  <tr className="bg-slate-50">
                    <td className="px-4 py-2 pl-10 text-xs text-slate-600" colSpan={4}>
                      No allocation in any fund.
                    </td>
                  </tr>
                ) : (
                  division.allocationByFund.map((f) => (
                    <tr key={f.fundingSourceId} className="bg-slate-50 text-xs">
                      <td className="px-4 py-1.5 pl-10 text-slate-600">{f.fundName}</td>
                      <td className="px-4 py-1.5" />
                      <td className="px-4 py-1.5" />
                      <td className="px-4 py-1.5 text-right text-slate-600">₱{formatMoney(f.amount)}</td>
                    </tr>
                  ))
                )}
              </tbody>
            </table>
          </td>
        </tr>
      )}
    </>
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

  const [dashboard, setDashboard] = useState<PpdoDashboard | null>(null);
  const [activity, setActivity] = useState<RecentActivity[]>([]);

  const [dashboardLoading, setDashboardLoading] = useState(true);
  const [activityLoading, setActivityLoading] = useState(true);
  const [dashboardError, setDashboardError] = useState<string | null>(null);
  const [activityError, setActivityError] = useState<string | null>(null);

  const [officeDashboard, setOfficeDashboard] = useState<OfficeDashboard | null>(null);
  const [officeDashboardLoading, setOfficeDashboardLoading] = useState(false);
  const [officeDashboardError, setOfficeDashboardError] = useState<string | null>(null);

  // ── Dashboard load ─────────────────────────────────────────────────────────
  // For a PPDO user this is now the PPDO-scoped dashboard (no office param — the
  // backend always resolves PPDO internally and clamps by division server-side).

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

  // Office user: locked to their own office. PPDO user: the PPDO-scoped dashboard
  // resolves the office server-side — its id/code/name only become known once loaded.
  const effectiveOfficeId = isPpdo ? dashboard?.officeId ?? null : user?.officeId ?? null;

  const officeLabel = isPpdo
    ? dashboard
      ? `${dashboard.officeCode} — ${dashboard.officeName}`
      : "PPDO"
    : user?.officeCode && user?.officeName
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

  // A PPDO user's own WFP status comes straight from the PPDO-scoped dashboard's
  // wfpByDivision (their own division's row for Staff, or the "most advanced" status
  // across divisions for finance/admin — mirroring the old office-status summary).
  const ownWfpDivision =
    isPpdo && dashboard
      ? dashboard.wfpByDivision.find((d) => d.divisionId === user?.divisionId) ?? dashboard.wfpByDivision[0]
      : null;

  // ── Render ────────────────────────────────────────────────────────────────

  return (
    <div className="min-h-full bg-slate-100 font-sans">
      <div className="max-w-6xl mx-auto px-6 py-6 space-y-5">

        {/* Header */}
        <div>
          <h1 className="text-lg font-bold text-slate-800">Budget Planning Dashboard</h1>
          <p className="text-sm text-slate-600">FY overview · {officeLabel}</p>
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

          <div className="flex items-center gap-2">
            <span className="text-xs font-semibold text-slate-600 uppercase tracking-wide">
              Office
            </span>
            <span className="text-sm text-slate-700 bg-white border border-slate-200 px-3 py-1.5">
              {officeLabel}
            </span>
          </div>
        </div>

        {/* Nav buttons — carry the office into each flow */}
        <div className="flex gap-2">
          {[
            { label: "LDIP", href: navHref("/budget-planning/ldip") },
            { label: "AIP",  href: navHref("/budget-planning/aip")  },
            { label: "WFP",  href: navHref("/budget-planning/wfp/entry")  },
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
            wfpStatus={ownWfpDivision?.wfpStatus ?? null}
            wfpAipRecordId={null}
          />
        ) : (
          <ReadinessSkeleton />
        )}

        {/* ── PPDO-only: Ceiling and allocation by fund ───────────────────── */}
        {isPpdo && dashboard && dashboard.ceilingByFund.length > 0 && (
          <div>
            <h2 className="text-sm font-semibold text-slate-700 mb-2">
              Ceiling and Allocation by Fund — FY {fiscalYear ?? "…"}
            </h2>
            <div className="grid grid-cols-1 sm:grid-cols-2 lg:grid-cols-3 gap-4">
              {dashboard.ceilingByFund.map((fund) => (
                <FundCeilingCard key={fund.fundingSourceId} fund={fund} />
              ))}
            </div>
          </div>
        )}

        {/* ── PPDO-only: WFP status by division ───────────────────────────── */}
        {isPpdo && (
          <div className="bg-white border border-slate-200">
            <div className="px-5 py-4 border-b border-slate-100 flex items-center justify-between">
              <div>
                <h2 className="text-sm font-semibold text-slate-700">
                  WFP Status by Division — FY {fiscalYear ?? "…"}
                </h2>
                <p className="text-xs text-slate-600 mt-0.5">Click a row to see allocation per fund</p>
              </div>
            </div>
            {dashboardLoading ? (
              <div className="px-5 py-6 flex items-center gap-2 text-sm text-slate-600">
                <Spinner />
                <span>Loading…</span>
              </div>
            ) : dashboardError ? (
              <div className="px-5 py-4 text-sm text-red-500">{dashboardError}</div>
            ) : !dashboard || dashboard.wfpByDivision.length === 0 ? (
              <div className="px-5 py-6 text-sm text-slate-600">No active divisions found.</div>
            ) : (
              <table className="w-full">
                <thead>
                  <tr className="bg-slate-50">
                    <th className="px-4 py-2.5 text-left text-xs font-semibold text-slate-600 uppercase tracking-wide">
                      Division
                    </th>
                    <th className="px-4 py-2.5 text-left text-xs font-semibold text-slate-600 uppercase tracking-wide">
                      WFP Status
                    </th>
                    <th className="px-4 py-2.5 text-right text-xs font-semibold text-slate-600 uppercase tracking-wide">
                      Activities Covered
                    </th>
                    <th className="px-4 py-2.5 text-right text-xs font-semibold text-slate-600 uppercase tracking-wide">
                      Total Allocated
                    </th>
                  </tr>
                </thead>
                <tbody>
                  {dashboard.wfpByDivision.map((division) => (
                    <DivisionRow key={division.divisionId} division={division} />
                  ))}
                </tbody>
              </table>
            )}
          </div>
        )}

        {/* ── Recent activity (both views) ──────────────────────────────────── */}
        <div className="bg-white border border-slate-200">
          <div className="px-5 py-4 border-b border-slate-100">
            <h2 className="text-sm font-semibold text-slate-700">Recent Activity</h2>
            <p className="text-xs text-slate-600 mt-0.5">{officeLabel}</p>
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
                    {new Date(entry.changedAt).toLocaleString("en-PH", { timeZone: "Asia/Manila" })}
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
