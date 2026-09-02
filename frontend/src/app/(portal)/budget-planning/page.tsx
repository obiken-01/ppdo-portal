"use client";

/**
 * Budget Planning Dashboard (PPDO-20 — docs/v1.8/Budget_Planning_Dashboard_Requirements.md).
 *
 * **One page, six bands, gated by flag.** Not per-role page variants: eight accounts would become
 * eight components that drift apart. Every band below is independently gated on a permission the
 * Permission Matrix already pins, and independently fetched, so one failing band leaves the rest
 * of the page readable.
 *
 * What it replaced: a 2×2 readiness hub shown to everyone, plus two PPDO-only sections bolted
 * underneath. Neither answered the question a person actually arrives with — *what do I have to
 * do, and who am I waiting on?* The rail answers the first by putting the stages in their real
 * order, the action card answers the second by naming the owner.
 *
 * Three things that are deliberate and easy to "fix" wrongly:
 *
 *   1. **WFP appears nowhere on this page, including for PPDO.** WFP is about to become an update
 *      to what AIP creation already produced, so its present shape is the thing being replaced;
 *      reporting on it now would teach a model that goes wrong within a release. It keeps its
 *      sidebar link and quick button for PPDO users — the dashboard simply stops reporting on it.
 *   2. **Money comes from the AIP** — "costed", not "planned in WFP". Follows from 1.
 *   3. **A guest office gets three stages, not five with two struck through.** Division allocation
 *      and PPA assignment are host-office-only. An earlier draft struck them through; that was
 *      reversed — a guest office does not need to be told about stages that never apply to it.
 *
 * Everything submission-shaped is drawn from a constant. There is no submission entity in the
 * schema until Phase 4; rendering the stage now means the layout does not move when it becomes
 * real (spec §7).
 */

import { useCallback, useEffect, useMemo, useState } from "react";
import Link from "next/link";
import ConfigPageHeader from "@/components/ui/ConfigPageHeader";
import ActionCard from "@/components/ui/ActionCard";
import PipelineRail, { PipelineRailSkeleton, type PipelineStage } from "@/components/ui/PipelineRail";
import StackedFundBar from "@/components/ui/StackedFundBar";
import {
  getDashboard,
  getDashboardOffices,
  getFiscalYears,
  getOfficeDashboard,
  getRecentActivity,
} from "@/lib/budget-planning";
import { useMe } from "@/lib/me-cache";
import { formatMoney } from "@/lib/money";
import type {
  OfficeDashboard,
  OfficeSummary,
  PpdoDashboard,
  RecentActivity,
} from "@/types";
import Band, { BandEmpty, TableBandSkeleton } from "./Band";
import BulkCeilingModal from "./BulkCeilingModal";
import ContextBar, { LockedField } from "./ContextBar";
import DivisionTable from "./DivisionTable";
import MoneyTiles, { MoneyTilesSkeleton, type MoneyTile } from "./MoneyTiles";
import OfficeTable from "./OfficeTable";

// ---------------------------------------------------------------------------
// Recent activity
// ---------------------------------------------------------------------------

// Exactly one of recordId/recordGuid is set on an entry, depending on whether the affected table
// has an int or Guid PK. Guids are shortened to their first segment to keep the row compact.
function recordLabel(entry: RecentActivity): string {
  if (entry.recordId != null) return `#${entry.recordId}`;
  if (entry.recordGuid != null) return `#${entry.recordGuid.split("-")[0]}`;
  return "";
}

// ---------------------------------------------------------------------------
// Page
// ---------------------------------------------------------------------------

export default function BudgetPlanningPage() {
  // On permission failure an office user must NOT be sent to /dashboard — the office-user gate in
  // the portal layout would bounce them straight back here, an infinite redirect. Office users go
  // to /account, a terminal page they can always reach.
  const user = useMe(
    (me) => me.canAccessBudgetPlanning,
    (me) => (me.isHostOffice ? "/dashboard" : "/account")
  );

  const [fiscalYear, setFiscalYear] = useState<number | null>(null);
  const [availableFiscalYears, setAvailableFiscalYears] = useState<number[]>([]);

  const [dashboard, setDashboard] = useState<PpdoDashboard | null>(null);
  const [dashboardLoading, setDashboardLoading] = useState(true);
  const [dashboardError, setDashboardError] = useState<string | null>(null);

  const [officeDashboard, setOfficeDashboard] = useState<OfficeDashboard | null>(null);
  const [officeLoading, setOfficeLoading] = useState(true);
  const [officeError, setOfficeError] = useState<string | null>(null);

  const [offices, setOffices] = useState<OfficeSummary[] | null>(null);
  const [officesLoading, setOfficesLoading] = useState(false);
  const [officesError, setOfficesError] = useState<string | null>(null);

  const [activity, setActivity] = useState<RecentActivity[]>([]);
  const [activityLoading, setActivityLoading] = useState(true);
  const [activityError, setActivityError] = useState<string | null>(null);

  const [bulkOpen, setBulkOpen] = useState(false);
  const [bulkNotice, setBulkNotice] = useState<string | null>(null);

  // ── Derived permission shape ────────────────────────────────────────────
  // Read once from the shared /auth/me context. Never per band — the WFP page once fired
  // /auth/me four times per load (docs/PERFORMANCE_GUIDELINES.md).

  const isHost = user?.isHostOffice === true;
  const isSuperAdmin = user?.role === "SuperAdmin";
  const canManageAllocation = user?.canManagePpdoAllocation === true;
  const canManagePboCeiling = user?.canManagePboCeiling === true;
  const canReviewAllOffices = user?.canReviewAllOffices === true;
  const canReview = user?.canReviewBudgetPlanning === true;

  // The office table's own gate — it must match the endpoint's, or the page requests a band it is
  // about to be 403'd for. SuperAdmin resolves true on both flags server-side; naming it here
  // keeps the client's gate honest rather than relying on that coincidence.
  const hasCrossOfficeScope = canReviewAllOffices || canManagePboCeiling || isSuperAdmin;

  // ── Loaders ─────────────────────────────────────────────────────────────

  const loadDashboard = useCallback((fy?: number) => {
    setDashboardLoading(true);
    setDashboardError(null);
    getDashboard(fy)
      .then((data) => {
        setDashboard(data);
        setAvailableFiscalYears(data.availableFiscalYears);
        if (fy == null) setFiscalYear(data.fiscalYear);
      })
      .catch(() => setDashboardError("Could not load the division breakdown."))
      .finally(() => setDashboardLoading(false));
  }, []);

  useEffect(() => {
    if (!user) return;

    if (isHost) {
      loadDashboard();
      return;
    }

    // A guest office has no PPDO dashboard to fetch, so the fiscal-year list has to come from
    // somewhere legitimately theirs. /budget-planning/fiscal-years is exactly that — distinct AIP
    // fiscal years, no office-scoped data in the payload.
    //
    // Ordering matters: the office-readiness effect is gated on fiscalYear, so it stays parked
    // until this resolves. Sourcing the year from the office dashboard itself would deadlock —
    // each would be waiting on the other.
    setDashboardLoading(false);
    getFiscalYears()
      .then((data) => {
        setAvailableFiscalYears(data.availableFiscalYears);
        setFiscalYear(data.fiscalYear);
      })
      .catch(() => setDashboardError("Could not load fiscal years."));
  }, [user, isHost, loadDashboard]);

  // A host caller's office id is resolved server-side and only known once the dashboard lands; a
  // guest caller's is their own.
  const officeId = isHost ? dashboard?.officeId ?? null : user?.officeId ?? null;

  const loadOfficeDashboard = useCallback(() => {
    if (officeId == null || fiscalYear == null) return;
    setOfficeLoading(true);
    setOfficeError(null);
    getOfficeDashboard(officeId, fiscalYear)
      .then(setOfficeDashboard)
      .catch(() => setOfficeError("Could not load this office's readiness."))
      .finally(() => setOfficeLoading(false));
  }, [officeId, fiscalYear]);

  useEffect(loadOfficeDashboard, [loadOfficeDashboard]);

  const loadOffices = useCallback(() => {
    if (!hasCrossOfficeScope || fiscalYear == null) return;
    setOfficesLoading(true);
    setOfficesError(null);
    getDashboardOffices(fiscalYear)
      .then(setOffices)
      .catch(() => setOfficesError("Could not load offices."))
      .finally(() => setOfficesLoading(false));
  }, [hasCrossOfficeScope, fiscalYear]);

  useEffect(loadOffices, [loadOffices]);

  useEffect(() => {
    if (!user) return;
    setActivityLoading(true);
    setActivityError(null);
    getRecentActivity(user.officeId ?? undefined)
      .then(setActivity)
      .catch(() => setActivityError("Could not load recent activity."))
      .finally(() => setActivityLoading(false));
  }, [user]);

  // ── Derived figures ─────────────────────────────────────────────────────

  const officeLabel = isHost
    ? dashboard
      ? `${dashboard.officeCode} — ${dashboard.officeName}`
      : "Host office"
    : user?.officeCode && user?.officeName
    ? `${user.officeCode} — ${user.officeName}`
    : user?.officeCode ?? user?.officeName ?? "Your office";

  /** Office-wide ceiling across every fund. Null when nothing is published at all. */
  const officeCeiling = useMemo<number | null>(() => {
    if (isHost) {
      if (!dashboard) return null;
      const published = dashboard.ceilingByFund.filter((f) => f.ceiling > 0);
      return published.length > 0 ? published.reduce((sum, f) => sum + f.ceiling, 0) : null;
    }
    return officeDashboard?.allocation.ceilingAmount ?? null;
  }, [isHost, dashboard, officeDashboard]);

  /**
   * The division rows in scope. Already clamped server-side for a division-scoped Staff caller —
   * never re-filter here, and never trust a client-side filter for this.
   */
  const divisions = useMemo(() => dashboard?.byDivision ?? [], [dashboard]);

  const allocatedToDivisions = divisions.reduce((sum, d) => sum + d.allocated, 0);

  /**
   * ⚠️ **Costed and activity totals do not come from summing the division rows when the viewer
   * sees every division.** A PPA assigned to two divisions counts in full against both — the row
   * answers "what is this division responsible for" — so the sum overstates the office by its
   * shared programs. Live on FY2027 that showed as 140 activities in the rail against 139 in the
   * office table for the same office, which reads as a bug however it is documented.
   *
   * So: an all-divisions viewer gets the office's own figures, which agree with the office table.
   * A division-clamped viewer gets their single row, which is both correct and the only thing they
   * are entitled to see (the spec's "money tiles scoped to RMED").
   */
  const seesEveryDivision = canManageAllocation;
  const costedInAip = seesEveryDivision
    ? dashboard?.aip.costedInAip ?? 0
    : divisions.reduce((sum, d) => sum + d.costedInAip, 0);
  const activityTotal = seesEveryDivision
    ? dashboard?.aip.activityCount ?? 0
    : divisions.reduce((n, d) => n + d.totalActivities, 0);
  const remaining = allocatedToDivisions - costedInAip;

  const hasCeiling = officeCeiling != null;
  const hasAip = isHost
    ? divisions.some((d) => d.totalActivities > 0)
    : officeDashboard?.aip.exists === true;

  // ── Pipeline rail ───────────────────────────────────────────────────────
  // The host office's five stages, a guest office's three. Every stage names its owner: roughly
  // half the support traffic on this feature is "why can't I edit this?", and the answer is almost
  // always that the stage belongs to somebody else.

  const aipHref = officeId != null ? `/budget-planning/aip?officeId=${officeId}` : "/budget-planning/aip";
  const allocationHref =
    officeId != null
      ? `/budget-planning/allocation?officeId=${officeId}${fiscalYear != null ? `&fiscalYear=${fiscalYear}` : ""}`
      : "/budget-planning/allocation";

  const stages = useMemo<PipelineStage[]>(() => {
    const ceilingStage: PipelineStage = {
      key: "ceiling",
      label: "Ceiling",
      owner: "Provincial Budget Office",
      stage: hasCeiling ? "Done" : "Todo",
      detail: hasCeiling ? `₱${formatMoney(officeCeiling!)} published` : "Not published yet",
      href: canManagePboCeiling || canManageAllocation ? allocationHref : undefined,
    };

    const aipStage: PipelineStage = {
      key: "aip",
      label: "AIP",
      // "Your division" is only true for a division-clamped encoder. A finance caller seeing every
      // division owns none of them in particular, and a guest office has no division at all.
      owner: !isHost ? "Your office" : canManageAllocation ? "PPDO divisions" : "Your division",
      stage: hasAip ? "In progress" : "Todo",
      detail: `${(isHost ? activityTotal : officeDashboard?.aip.activityCount ?? 0).toLocaleString("en-PH")} activities`,
      href: aipHref,
    };

    const submissionStage: PipelineStage = {
      key: "submission",
      label: "AIP submission",
      owner: canReview ? "You" : "Your office's reviewer",
      // Constant until Phase 4 — spec §7. Do not derive this from anything; there is no
      // submission entity to derive it from.
      stage: "Todo",
      detail: "Opens in a later release",
    };

    if (!isHost) return [ceilingStage, aipStage, submissionStage];

    return [
      ceilingStage,
      {
        key: "allocation",
        label: "Division allocation",
        owner: "PPDO finance",
        stage: allocatedToDivisions > 0 ? "Done" : "Todo",
        detail:
          allocatedToDivisions > 0 ? `₱${formatMoney(allocatedToDivisions)} allocated` : "Not started",
        risk: hasCeiling && allocatedToDivisions > officeCeiling! ? "Over ceiling" : undefined,
        href: canManageAllocation ? allocationHref : undefined,
      },
      {
        key: "ppa",
        label: "PPA assignment",
        owner: "PPDO finance",
        stage:
          (officeDashboard?.allocation.assignedProgramCount ?? 0) > 0
            ? (officeDashboard?.allocation.unassignedProgramCount ?? 0) > 0
              ? "In progress"
              : "Done"
            : "Todo",
        detail:
          officeDashboard == null
            ? undefined
            : `${officeDashboard.allocation.assignedProgramCount} assigned · ${officeDashboard.allocation.unassignedProgramCount} unassigned`,
        href: canManageAllocation ? allocationHref : undefined,
      },
      aipStage,
      submissionStage,
    ];
  }, [
    isHost, hasCeiling, officeCeiling, hasAip, activityTotal, officeDashboard, allocatedToDivisions,
    canManageAllocation, canManagePboCeiling, canReview, aipHref, allocationHref,
  ]);

  // ── Money tiles ─────────────────────────────────────────────────────────

  const tiles = useMemo<MoneyTile[]>(() => {
    if (isHost) {
      return [
        {
          key: "ceiling",
          label: "Office ceiling",
          value: officeCeiling,
          // Read-only for anyone who is not finance — shown, not hidden, because it is the number
          // their own allocation has to fit inside.
          muted: !canManageAllocation,
          hint: canManageAllocation ? "All funds" : "Set by PBO — read only",
        },
        {
          key: "allocated",
          label: canManageAllocation ? "Allocated to divisions" : "Allocated to you",
          value: allocatedToDivisions,
        },
        { key: "costed", label: "Costed in AIP", value: costedInAip },
        {
          key: "remaining",
          label: "Remaining",
          value: remaining,
          alert: remaining < 0,
          hint: remaining < 0 ? "Costed past the allocation" : undefined,
        },
      ];
    }

    // A guest office has no division split, so its tiles are the office's own figures throughout.
    // `costedInAip` on the office endpoint is what makes this possible — the cross-office endpoint
    // computes the same number for every office, but correctly 403s a plain office user.
    const guestCeiling = officeCeiling;
    const guestCosted = officeDashboard?.aip.costedInAip ?? null;
    return [
      {
        key: "ceiling",
        label: "Office ceiling",
        value: guestCeiling,
        muted: true,
        hint: "Set by PBO — read only",
      },
      { key: "costed", label: "Costed in AIP", value: guestCosted },
      {
        key: "remaining",
        label: "Remaining",
        value: guestCeiling != null && guestCosted != null ? guestCeiling - guestCosted : null,
        alert: guestCeiling != null && guestCosted != null && guestCosted > guestCeiling,
        hint: guestCeiling == null ? "No ceiling published yet" : undefined,
      },
      {
        key: "activities",
        label: "AIP activities",
        value: officeDashboard?.aip.activityCount ?? null,
        count: true,
      },
    ];
  }, [isHost, officeCeiling, canManageAllocation, allocatedToDivisions, costedInAip, remaining, officeDashboard]);

  // ── Action card ─────────────────────────────────────────────────────────
  // The single next thing this person can do. Ordered by what actually blocks what.

  const actionCard = useMemo(() => {
    if (!hasCeiling) {
      if (canManagePboCeiling) {
        return (
          <ActionCard
            tone="blocked"
            title="Publish this year's ceilings"
            description={`No FY ${fiscalYear ?? "—"} ceiling is published for ${officeLabel}. Offices cannot submit until one is.`}
            actionLabel="Set ceilings"
            href={allocationHref}
          />
        );
      }
      return (
        <ActionCard
          tone="waiting"
          title="Waiting on the Provincial Budget Office"
          description={`No FY ${fiscalYear ?? "—"} ceiling has been published for ${officeLabel} yet. You can still draft your AIP — submission opens once the ceiling is set.`}
          actionLabel="Open AIP"
          href={aipHref}
        />
      );
    }

    if (!hasAip) {
      return (
        <ActionCard
          title="Start this year's AIP"
          description={`The ceiling is published. Enter FY ${fiscalYear ?? "—"} activities for ${officeLabel}.`}
          actionLabel="Open AIP"
          href={aipHref}
        />
      );
    }

    if (canReview) {
      return (
        <ActionCard
          tone="waiting"
          title="Submit when the AIP is complete"
          description="You are this office's reviewer. Submission opens in a later release."
          actionLabel="Submit AIP"
          disabledReason="Submission opens in a later release"
        />
      );
    }

    return (
      <ActionCard
        tone="waiting"
        title="Keep costing your AIP activities"
        description="Your office's reviewer submits once every activity carries a cost."
        actionLabel="Open AIP"
        href={aipHref}
      />
    );
  }, [hasCeiling, hasAip, canManagePboCeiling, canReview, fiscalYear, officeLabel, allocationHref, aipHref]);

  // ── Fund bars ───────────────────────────────────────────────────────────
  // Funds with neither a ceiling nor an allocation are hidden — an all-zero bar is noise.

  const setUpFunds = (dashboard?.ceilingByFund ?? []).filter(
    (fund) => fund.ceiling > 0 || fund.byDivision.some((d) => d.amount > 0)
  );

  const officesWithoutCeiling = (offices ?? []).filter((o) => o.ceilingAmount == null);
  const priorFiscalYear = fiscalYear != null ? fiscalYear - 1 : null;

  // ── Render ──────────────────────────────────────────────────────────────

  return (
    <div className="min-h-full bg-slate-100 font-sans">
      <div className="max-w-6xl mx-auto px-3 py-4 sm:px-6 sm:py-6 space-y-4">
        <ConfigPageHeader
          title="Budget Planning"
          description={`FY ${fiscalYear ?? "…"} · ${officeLabel}`}
        />

        <ContextBar
          fiscalYear={fiscalYear}
          availableFiscalYears={availableFiscalYears}
          fiscalYearDisabled={dashboardLoading || officeLoading}
          onFiscalYearChange={(fy) => {
            setFiscalYear(fy);
            // A guest office has no host dashboard to reload — the office-readiness and offices
            // effects re-run off fiscalYear on their own.
            if (isHost) loadDashboard(fy);
          }}
          officeField={<LockedField label="Office" value={officeLabel} />}
          // A guest office gets NO division field — division does not narrow them, and an inert
          // control would imply it might.
          divisionField={
            isHost ? (
              <LockedField
                label="Division"
                value={
                  canManageAllocation
                    ? "All divisions"
                    : user?.division ?? "Unassigned"
                }
              />
            ) : undefined
          }
        />

        {actionCard}

        {/* The rail's own error state. It is fed by the office-readiness fetch, so a failure there
            must not blank the tiles or the tables below — errors are per band, not per page. */}
        {dashboardLoading || officeLoading ? (
          <PipelineRailSkeleton stages={isHost ? 5 : 3} />
        ) : officeError ? (
          <div className="bg-white border border-slate-200 p-4 flex flex-wrap items-center gap-3">
            <p className="text-sm text-danger-500">{officeError}</p>
            <button
              type="button"
              onClick={loadOfficeDashboard}
              className="px-3 py-1.5 bg-white border border-slate-200 hover:bg-slate-50 text-slate-800 text-xs font-medium transition-colors"
            >
              Retry
            </button>
          </div>
        ) : (
          <PipelineRail stages={stages} />
        )}

        {dashboardLoading || officeLoading ? <MoneyTilesSkeleton /> : <MoneyTiles tiles={tiles} />}

        {/* ── Ceiling and allocation by fund — host office only ───────────── */}
        {isHost && setUpFunds.length > 0 && (
          <div>
            <h2 className="text-sm font-semibold text-slate-800 mb-2">
              Ceiling and allocation by fund — FY {fiscalYear ?? "…"}
            </h2>
            <div className="grid grid-cols-1 sm:grid-cols-2 lg:grid-cols-3 gap-3">
              {setUpFunds.map((fund) => (
                <StackedFundBar
                  key={fund.fundingSourceId}
                  fundName={fund.fundName}
                  ceiling={fund.ceiling}
                  remaining={fund.remaining}
                  segments={fund.byDivision.map((d) => ({
                    key: d.divisionId,
                    label: d.divisionCode ?? d.divisionName,
                    amount: d.amount,
                  }))}
                />
              ))}
            </div>
          </div>
        )}

        {/* ── Division table — host office only ───────────────────────────── */}
        {isHost && (
          <Band
            title={`Divisions — FY ${fiscalYear ?? "…"}`}
            description="Click a row to see allocation per fund"
            loading={dashboardLoading}
            error={dashboardError}
            onRetry={() => loadDashboard(fiscalYear ?? undefined)}
            skeleton={<TableBandSkeleton columns={6} />}
          >
            {divisions.length === 0 ? (
              <BandEmpty message={`No records for FY ${fiscalYear ?? "—"} yet.`} />
            ) : (
              <DivisionTable
                divisions={divisions}
                canManageAllocation={canManageAllocation}
                officeId={officeId}
                fiscalYear={fiscalYear}
              />
            )}
          </Band>
        )}

        {/* ── Office table — cross-office scope only ──────────────────────── */}
        {hasCrossOfficeScope && (
          <Band
            title={`Offices — FY ${fiscalYear ?? "…"}`}
            description={
              canManagePboCeiling
                ? "Ceilings you publish for every office"
                : "Read-only across every office"
            }
            actions={
              canManagePboCeiling && officesWithoutCeiling.length > 0 && priorFiscalYear != null ? (
                <button
                  type="button"
                  onClick={() => setBulkOpen(true)}
                  className="px-3 py-2 bg-white border border-slate-200 hover:bg-slate-50 text-slate-800 text-sm font-medium transition-colors"
                >
                  Bulk set from FY {priorFiscalYear}
                </button>
              ) : undefined
            }
            loading={officesLoading}
            error={officesError}
            onRetry={loadOffices}
            skeleton={<TableBandSkeleton columns={7} />}
          >
            {bulkNotice && <p className="px-5 pt-3 text-sm text-green-700">{bulkNotice}</p>}
            {offices == null || offices.length === 0 ? (
              <BandEmpty
                message={`No offices have a FY ${fiscalYear ?? "—"} ceiling yet.`}
                action={
                  canManagePboCeiling ? (
                    <Link
                      href={allocationHref}
                      className="px-3 py-2 bg-green-600 hover:bg-green-500 text-white text-sm font-medium transition-colors"
                    >
                      Set ceilings
                    </Link>
                  ) : undefined
                }
              />
            ) : (
              <OfficeTable
                offices={offices}
                fiscalYear={fiscalYear}
                canSetCeiling={canManagePboCeiling}
              />
            )}
          </Band>
        )}

        {/* ── Recent activity ────────────────────────────────────────────── */}
        <Band
          title="Recent activity"
          description={officeLabel}
          loading={activityLoading}
          error={activityError}
          onRetry={() => {
            setActivityLoading(true);
            setActivityError(null);
            getRecentActivity(user?.officeId ?? undefined)
              .then(setActivity)
              .catch(() => setActivityError("Could not load recent activity."))
              .finally(() => setActivityLoading(false));
          }}
          skeleton={<TableBandSkeleton rows={4} columns={2} />}
        >
          {activity.length === 0 ? (
            <BandEmpty message="No recent activity yet." />
          ) : (
            <div className="divide-y divide-slate-50">
              {activity.map((entry) => (
                <div key={entry.id} className="px-5 py-3 flex items-start justify-between gap-4">
                  <p className="text-sm text-slate-600">
                    <span className="font-medium text-slate-800">{entry.actorName}</span>
                    {" — "}
                    {entry.action.toLowerCase()} on {entry.tableName} {recordLabel(entry)}
                  </p>
                  <span className="text-xs text-slate-500 whitespace-nowrap shrink-0">
                    {new Date(entry.changedAt).toLocaleString("en-PH", { timeZone: "Asia/Manila" })}
                  </span>
                </div>
              ))}
            </div>
          )}
        </Band>
      </div>

      {bulkOpen && fiscalYear != null && priorFiscalYear != null && (
        <BulkCeilingModal
          offices={officesWithoutCeiling}
          fiscalYear={fiscalYear}
          priorFiscalYear={priorFiscalYear}
          onClose={() => setBulkOpen(false)}
          onApplied={(created) => {
            setBulkOpen(false);
            setBulkNotice(`Published ${created} ceiling${created === 1 ? "" : "s"} for FY ${fiscalYear}.`);
            loadOffices();
          }}
        />
      )}
    </div>
  );
}
