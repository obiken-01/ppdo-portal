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
 * do, and who am I waiting on?* ↩️ PPDO-178: one status band answers both — a sentence saying where
 * the AIP is and who has it, the one next step, and a thin step track. It replaced the action card
 * and the 3–5 pipeline stage cards, which on a phone filled the whole first screen.
 *
 * Three things that are deliberate and easy to "fix" wrongly:
 *
 *   1. **WFP appears nowhere on this page, including for PPDO.** WFP is about to become an update
 *      to what AIP creation already produced, so its present shape is the thing being replaced;
 *      reporting on it now would teach a model that goes wrong within a release. It keeps its
 *      sidebar link and quick button for PPDO users — the dashboard simply stops reporting on it.
 *   2. **Money comes from the AIP** — "costed", not "planned in WFP". Follows from 1.
 *   3. **A guest office's step track has no Division allocation or Program assignment step.** Both
 *      are host-office-only. An earlier draft struck them through; that was reversed — a guest
 *      office does not need to be told about stages that never apply to it.
 *
 * **The Offices band has two views** (PPDO-78, `AIP_Review_Spec.md` §6.3) — a readiness board and
 * the table, behind a Board / Table switch, for a cross-office reviewer or SuperAdmin. The budget
 * officer gets the table alone: it is where they publish ceilings, and where an office sits in
 * review is not theirs to act on.
 */

import { useCallback, useEffect, useMemo, useState } from "react";
import Link from "next/link";
import ConfigPageHeader from "@/components/ui/ConfigPageHeader";
import StackedFundBar from "@/components/ui/StackedFundBar";
import {
  getDashboard,
  getDashboardOffices,
  getFiscalYears,
  getOfficeDashboard,
  getRecentActivity,
} from "@/lib/budget-planning";
import { useMe } from "@/lib/me-cache";
import { FIRST_ENTERED_FISCAL_YEAR } from "@/lib/aip-fiscal-years";
import { statusBand, type BandCount } from "@/lib/dashboard-status-band";
import { useAipNotifications } from "@/lib/aip-notifications";
import type {
  OfficeDashboard,
  OfficeSummary,
  PpdoDashboard,
  RecentActivity,
} from "@/types";
import Band, { BandEmpty, TableBandSkeleton } from "./Band";
import BulkCeilingModal from "./BulkCeilingModal";
import ContextBar from "./ContextBar";
import DivisionTable from "./DivisionTable";
import MoneyTiles, { MoneyTilesSkeleton, type MoneyTile } from "./MoneyTiles";
import OfficeBoard, { BOARD_COLUMNS, OfficeBoardSkeleton } from "./OfficeBoard";
import OfficeTable from "./OfficeTable";
import RecentActivityList from "./RecentActivityList";
import StatusBand, { StatusBandSkeleton } from "./StatusBand";

type OfficesView = "board" | "table";

/**
 * The Offices band's remembered view (PPDO-78). ⚠️ Per person, per device — keyed by user id so two
 * people sharing a PC keep their own choice. A convenience, never a gate: anything missing or
 * unreadable (private window, blocked storage) falls back to the caller's default.
 */
const officesViewKey = (userId: string) => `ppdo.budgetPlanning.officesView.${userId}`;

function readOfficesView(userId: string | undefined): OfficesView | null {
  if (!userId) return null;
  try {
    const stored = localStorage.getItem(officesViewKey(userId));
    return stored === "board" || stored === "table" ? stored : null;
  } catch {
    return null;
  }
}

// ---------------------------------------------------------------------------
// Recent activity
// ---------------------------------------------------------------------------

/** The Offices band's Board / Table switch — a two-segment control in the band header. */
function ViewSwitch({ value, onChange }: { value: OfficesView; onChange: (view: OfficesView) => void }) {
  const segment = (view: OfficesView, label: string) => (
    <button
      type="button"
      onClick={() => onChange(view)}
      aria-pressed={value === view}
      className={`px-3 py-1.5 text-xs font-medium transition-colors ${
        value === view ? "bg-green-700 text-white" : "bg-white text-slate-600 hover:bg-slate-50"
      }`}
    >
      {label}
    </button>
  );
  return (
    <div role="group" aria-label="Offices view" className="inline-flex divide-x divide-slate-200 border border-slate-200">
      {segment("board", "Board")}
      {segment("table", "Table")}
    </div>
  );
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
  // PPDO-177 — the year the reader PICKED, null until they do. The office bands load with this
  // rather than `fiscalYear`: null asks the server for its default (the same year /dashboard
  // resolves), so those bands no longer wait for /dashboard just to learn the year. Keying them
  // on `fiscalYear` instead would fetch twice on every load — once before the year is known and
  // again when /dashboard sets it.
  const [requestedFiscalYear, setRequestedFiscalYear] = useState<number | null>(null);
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
  const canManageOfficeCeilings = user?.canManageOfficeCeilings === true;
  const canReviewAllOffices = user?.canReviewAllOffices === true;
  const canReview = user?.canReviewBudgetPlanning === true;
  // Mirrors the sidebar's own gate for the Audit Log link (Sidebar.tsx `showAuditLog`).
  const canSeeAuditLog = user?.isHostOffice === true && user?.role === "SuperAdmin";

  // The office table's own gate — it must match the endpoint's, or the page requests a band it is
  // about to be 403'd for. SuperAdmin resolves true on both flags server-side; naming it here
  // keeps the client's gate honest rather than relying on that coincidence.
  const hasCrossOfficeScope = canReviewAllOffices || canManageOfficeCeilings || isSuperAdmin;

  // ── Offices band view (PPDO-78) ─────────────────────────────────────────
  // The board is for whoever reviews across offices; a budget officer who holds only the ceiling
  // grant gets the table and no switch. Holding both grants gets the switch. A stored "board" is
  // ignored for a caller who cannot see it.
  const canSeeBoard = canReviewAllOffices || isSuperAdmin;

  // PPDO-176 — a cross-office reviewer's real work is the other offices, not PPDO's own encoding.
  // The review grant, or SuperAdmin (which resolves it true server-side). Cross-office review is a
  // grant, not a role (Ralph, 2026-10-04): a PPDO finance user given it gets this top too, with the
  // finance bands following below.
  const isCrossOfficeReviewer = canReviewAllOffices || isSuperAdmin;
  // The same response the sidebar badge reads: one shared fetch for the whole portal, so the
  // reviewer card adds no request.
  const notifications = useAipNotifications(isCrossOfficeReviewer ? user ?? null : null);
  const [chosenView, setChosenView] = useState<OfficesView | null>(null);
  // Read during render, not in an effect: the band is not drawn until /auth/me has landed, which
  // only happens in the browser, so there is no server render for this to mismatch — and no flash
  // of the board for someone who last chose the table.
  const storedView = useMemo(() => readOfficesView(user?.userId), [user?.userId]);
  const officesView: OfficesView = canSeeBoard ? chosenView ?? storedView ?? "board" : "table";

  function chooseOfficesView(view: OfficesView) {
    setChosenView(view);
    if (!user) return;
    try {
      localStorage.setItem(officesViewKey(user.userId), view);
    } catch {
      // Storage blocked — the choice still holds for this visit.
    }
  }

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

  // PPDO-177 — every caller's office id comes from /auth/me. A host-office user's own office IS
  // the host office (`isHost` is read off it), so there is no need to wait for /dashboard to name
  // it. The dashboard's id stays as a fallback for a host caller with no office on the token.
  const officeId = user?.officeId ?? (isHost ? dashboard?.officeId ?? null : null);

  const loadOfficeDashboard = useCallback(() => {
    if (officeId == null) return;
    setOfficeLoading(true);
    setOfficeError(null);
    getOfficeDashboard(officeId, requestedFiscalYear)
      .then(setOfficeDashboard)
      .catch(() => setOfficeError("Could not load this office's readiness."))
      .finally(() => setOfficeLoading(false));
  }, [officeId, requestedFiscalYear]);

  useEffect(loadOfficeDashboard, [loadOfficeDashboard]);

  const loadOffices = useCallback(() => {
    if (!hasCrossOfficeScope) return;
    setOfficesLoading(true);
    setOfficesError(null);
    getDashboardOffices(requestedFiscalYear)
      .then(setOffices)
      .catch(() => setOfficesError("Could not load offices."))
      .finally(() => setOfficesLoading(false));
  }, [hasCrossOfficeScope, requestedFiscalYear]);

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

  // PPDO-179 (F8) — the header's one context line. A guest office has no division axis, so it names
  // the office alone; a host-office user also says which divisions they are looking at.
  const contextLine = isHost
    ? `${officeLabel} · ${canManageAllocation ? "All divisions" : user?.division ?? "Unassigned"}`
    : officeLabel;

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

  /**
   * The Divisions band's own data source (PPDO-126, PPDO-127) — `dashboard.byDivision` for PPDO,
   * `officeDashboard.byDivision` for a guest office. Deliberately NOT folded into `divisions`
   * above: that one only ever comes from `dashboard` (host-only) and feeds the host-only money-tile
   * math below, which must keep reading empty for a guest office exactly as it always has.
   */
  const divisionsForBand = isHost ? divisions : officeDashboard?.byDivision ?? [];

  /** A department head manages their own office's split the same way PPDO finance manages PPDO's. */
  const canManageAllocationForBand = isHost ? canManageAllocation : user?.canManageOfficeSetup === true;

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

  const hasAip = isHost
    ? divisions.some((d) => d.totalActivities > 0)
    : officeDashboard?.aip.exists === true;

  // ── Links the status band hands off to ─────────────────────────────────────

  // ⚠️ **AIP Entry, not the AIP record list** (PPDO-81). The list creates, finalizes and archives
  // the base record and is now Admin-only; entry is where the reader of this hub actually works, and
  // it is the page every office has. It carries no `officeId` because it scopes to the caller's own
  // office server-side — passing one would imply a choice that does not exist.
  //
  // The fiscal year IS carried, so the hub and the page it hands off to agree on which year is being
  // planned. Only from the break year, below which there is no entry process to hand off to.
  const aipEntryHref =
    fiscalYear != null && fiscalYear >= FIRST_ENTERED_FISCAL_YEAR
      ? `/budget-planning/aip/entry?fiscalYear=${fiscalYear}`
      : "/budget-planning/aip/entry";
  const allocationHref =
    officeId != null
      ? `/budget-planning/allocation?officeId=${officeId}${fiscalYear != null ? `&fiscalYear=${fiscalYear}` : ""}`
      : "/budget-planning/allocation";

  // ── Money tiles ─────────────────────────────────────────────────────────

  const tiles = useMemo<MoneyTile[]>(() => {
    /**
     * The ceiling tile is the same in every view, and its read-only-ness keys on
     * `CanManageOfficeCeilings` — **not** on `CanManagePpdoAllocation**, which governs the division
     * split one level down. Two live findings drove that:
     *
     *   - A PPDO finance officer (allocation grant, no ceiling grant) saw the ceiling rendered as
     *     editable. It is not: PPDO-18 gives them a read-only ceiling on the Allocation page, and
     *     a tile that disagrees with the page it links to is how "why can't I edit this?" starts.
     *   - The PBO officer was told "Set by PBO — read only" about their own office's ceiling,
     *     which they publish from the office table immediately below it.
     *
     * It is always SHOWN, never hidden, even to an encoder who can do nothing with it — it is the
     * figure their own allocation has to fit inside, and hiding it just moves the question to
     * whoever they ask next.
     */
    const ceilingTile: MoneyTile = {
      key: "ceiling",
      label: "Office ceiling",
      value: officeCeiling,
      muted: !canManageOfficeCeilings,
      hint: canManageOfficeCeilings ? "You publish this" : "Set by PBO — read only",
    };

    if (isHost) {
      return [
        ceilingTile,
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
    // ⚠️ Against the CEILING, so the ceiling's figure — General Fund, the submit gate's rule. The
    // all-shared-funds `costedInAip` would let GAD money shrink a GF remaining (2026-09-24).
    const guestCosted = officeDashboard?.aip.costedAgainstCeiling ?? null;
    return [
      ceilingTile,
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
  }, [
    isHost, officeCeiling, canManageAllocation, canManageOfficeCeilings,
    allocatedToDivisions, costedInAip, remaining, officeDashboard,
  ]);

  // ── Status band (PPDO-178) ─────────────────────────────────────────────
  // One sentence for this reader and this state; every word comes from lib/dashboard-status-band.
  // It reads only what the page already loads: /dashboard/office (B1), the host dashboard, the
  // offices list and the sidebar's notification count. No request of its own.

  const officeColumns = useMemo<BandCount[] | null>(
    () =>
      canSeeBoard && offices
        ? BOARD_COLUMNS.map((c) => ({
            key: c.key,
            label: c.title,
            count: offices.filter((o) => o.readinessColumn === c.key).length,
          }))
        : null,
    [canSeeBoard, offices]
  );

  const band = useMemo(() => {
    const aip = officeDashboard?.aip;
    // A division-clamped PPDO encoder counts their own division's rows, as the tiles do; everyone
    // else gets the office's own figure.
    const uncosted =
      isHost && !seesEveryDivision
        ? divisions.reduce((n, d) => n + (d.totalActivities - d.costedActivityCount), 0)
        : aip?.uncostedActivityCount ?? 0;
    return statusBand({
      fiscalYear,
      firstEnteredYear: FIRST_ENTERED_FISCAL_YEAR,
      officeCode: (isHost ? dashboard?.officeCode : user?.officeCode) ?? "your office",
      isCrossOfficeReviewer,
      canManageOfficeCeilings,
      isDepartmentHead: canReview,
      isHost,
      canManageAllocation,
      loaded:
        officeDashboard != null && !officeLoading && (!isHost || (dashboard != null && !dashboardLoading)),
      ceiling: officeDashboard?.allocation.ceilingAmount ?? null,
      costedAgainstCeiling: aip?.costedAgainstCeiling ?? 0,
      hasAip,
      activityCount: isHost ? activityTotal : aip?.activityCount ?? 0,
      uncostedActivityCount: uncosted,
      workflowStatus: aip?.workflowStatus,
      workflowStatusSince: aip?.workflowStatusSince,
      lastHandOff: aip?.lastHandOff,
      divisionsSubmitted: aip?.divisionsSubmitted,
      divisionsRequired: aip?.divisionsRequired,
      divisionsWaiting: aip?.divisionsWaiting,
      unresolvedComments: aip?.unresolvedComments,
      officeCeilingAllFunds: officeCeiling,
      allocatedToDivisions,
      assignedProgramCount: officeDashboard?.allocation.assignedProgramCount ?? 0,
      unassignedProgramCount: officeDashboard?.allocation.unassignedProgramCount ?? 0,
      pendingForPpdo: notifications?.pendingForPpdo ?? null,
      pendingFiscalYear: notifications?.ppdoFiscalYear ?? null,
      officeColumns,
      officesWithoutCeiling: offices ? offices.filter((o) => o.ceilingAmount == null).length : null,
      aipEntryHref,
      allocationHref,
      reportHref: "/budget-planning/report",
    });
  }, [
    officeDashboard, isHost, seesEveryDivision, divisions, fiscalYear, dashboard, user,
    isCrossOfficeReviewer, canManageOfficeCeilings, canReview, canManageAllocation, officeLoading,
    dashboardLoading, hasAip, activityTotal, officeCeiling, allocatedToDivisions, notifications,
    officeColumns, offices, aipEntryHref, allocationHref,
  ]);

  // ── Fund bars ───────────────────────────────────────────────────────────
  // Funds with neither a ceiling nor an allocation are hidden — an all-zero bar is noise.

  const setUpFunds = (dashboard?.ceilingByFund ?? []).filter(
    (fund) => fund.ceiling > 0 || fund.byDivision.some((d) => d.amount > 0)
  );

  const officesWithoutCeiling = (offices ?? []).filter((o) => o.ceilingAmount == null);
  const priorFiscalYear = fiscalYear != null ? fiscalYear - 1 : null;

  // ── Render ──────────────────────────────────────────────────────────────

  // ── Offices band ────────────────────────────────────────────────────────
  // Cross-office scope only. PPDO-176: a cross-office reviewer sees it right under the action card,
  // above PPDO's own rail, tiles and tables; everyone else (a ceiling-only budget officer) keeps it
  // where it was, after the division table. The band carries its own skeleton, so moving it keeps
  // the loading layout in the loaded order.
  const officesBand = hasCrossOfficeScope ? (
    <Band
      title={`Offices — FY ${fiscalYear ?? "…"}`}
      description={
        officesView === "board"
          ? "Where every office stands · click an office to open it in AIP Review"
          : canManageOfficeCeilings
          ? "Ceilings you publish for every office"
          : "Read-only across every office"
      }
      actions={
        (canManageOfficeCeilings && officesWithoutCeiling.length > 0 && priorFiscalYear != null) ||
        canSeeBoard ? (
          <>
            {canManageOfficeCeilings && officesWithoutCeiling.length > 0 && priorFiscalYear != null && (
              <button
                type="button"
                onClick={() => setBulkOpen(true)}
                className="px-3 py-2 bg-white border border-slate-200 hover:bg-slate-50 text-slate-800 text-sm font-medium transition-colors"
              >
                Bulk set from FY {priorFiscalYear}
              </button>
            )}
            {canSeeBoard && (
              <ViewSwitch value={officesView} onChange={chooseOfficesView} />
            )}
          </>
        ) : undefined
      }
      loading={officesLoading}
      error={officesError}
      onRetry={loadOffices}
      skeleton={
        officesView === "board" ? <OfficeBoardSkeleton /> : <TableBandSkeleton columns={7} />
      }
    >
      {bulkNotice && <p className="px-5 pt-3 text-sm text-green-700">{bulkNotice}</p>}
      {offices == null || offices.length === 0 ? (
        <BandEmpty
          message={`No offices have a FY ${fiscalYear ?? "—"} ceiling yet.`}
          action={
            canManageOfficeCeilings ? (
              <Link
                href={allocationHref}
                className="px-3 py-2 bg-green-600 hover:bg-green-500 text-white text-sm font-medium transition-colors"
              >
                Set ceilings
              </Link>
            ) : undefined
          }
        />
      ) : officesView === "board" ? (
        <OfficeBoard
          offices={offices}
          fiscalYear={fiscalYear}
          canAssignReviewers={user?.canManageUsers === true}
        />
      ) : (
        <OfficeTable
          offices={offices}
          fiscalYear={fiscalYear}
          canSetCeiling={canManageOfficeCeilings}
        />
      )}
    </Band>
  ) : null;

  return (
    <div className="min-h-full bg-slate-100 font-sans">
      <div className="max-w-6xl mx-auto px-3 py-4 sm:px-6 sm:py-6 space-y-4">
        {/* PPDO-179 (F8) — the office and division are stated once, in the line under the title; the
            fiscal-year picker, the only axis a reader can change, sits in the header's action slot.
            The locked Office/Division fields and the "FY … · office" repeat line are gone. */}
        <ConfigPageHeader
          title="Investment Planning"
          description={contextLine}
          actions={
            <ContextBar
              fiscalYear={fiscalYear}
              availableFiscalYears={availableFiscalYears}
              fiscalYearDisabled={dashboardLoading || officeLoading}
              onFiscalYearChange={(fy) => {
                setFiscalYear(fy);
                // The office-readiness and offices effects re-run off requestedFiscalYear on their own.
                // A guest office has no host dashboard to reload.
                setRequestedFiscalYear(fy);
                if (isHost) loadDashboard(fy);
              }}
            />
          }
        />

        {/* PPDO-178 — the status band. Its own error state: it is fed by the office-readiness fetch,
            so a failure there must not blank the tiles or the tables below — errors are per band,
            not per page. The reviewer's band reads the queue count, not that fetch. */}
        {band ? (
          <StatusBand band={band} />
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
          <StatusBandSkeleton />
        )}

        {/* PPDO-176 — the reviewer's main work first; PPDO's own office follows below. */}
        {isCrossOfficeReviewer && officesBand}

        {dashboardLoading || officeLoading ? <MoneyTilesSkeleton /> : <MoneyTiles tiles={tiles} />}

        {/* ── Ceiling and allocation by fund — host office only ───────────── */}
        {isHost && setUpFunds.length > 0 && (
          <div>
            <h2 className="text-sm font-semibold text-slate-800 mb-2">
              Ceiling and allocation by fund — FY {fiscalYear ?? "…"}
            </h2>
            {/* PPDO-179 (F9) — one fund takes the whole row, two share it, three or more keep the
                three-up grid. A lone card in a three-column grid left two thirds of the row empty. */}
            <div
              className={`grid grid-cols-1 gap-3 ${
                setUpFunds.length === 1
                  ? ""
                  : setUpFunds.length === 2
                  ? "sm:grid-cols-2"
                  : "sm:grid-cols-2 lg:grid-cols-3"
              }`}
            >
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

        {/* ── Division table — every office (PPDO-127). PPDO-179 (F6): a guest office with no
               divisions gets no band at all — an empty "No divisions configured…" card with a
               "Click a row" description helped nobody. While its data is loading a guest office
               also draws nothing: most have no divisions, and a skeleton that then vanished would
               shift everything below it. The host office keeps its band and its empty state. ───── */}
        {(isHost || (!officeLoading && divisionsForBand.length > 0)) && (
          <Band
            title={`Divisions — FY ${fiscalYear ?? "…"}`}
            description="Click a row to see allocation per fund"
            loading={isHost ? dashboardLoading : officeLoading}
            error={isHost ? dashboardError : officeError}
            onRetry={isHost ? () => loadDashboard(fiscalYear ?? undefined) : loadOfficeDashboard}
            skeleton={<TableBandSkeleton columns={6} />}
          >
            {divisionsForBand.length === 0 ? (
              <BandEmpty message={`No records for FY ${fiscalYear ?? "—"} yet.`} />
            ) : (
              <DivisionTable
                divisions={divisionsForBand}
                noDivision={isHost ? dashboard?.noDivision : officeDashboard?.noDivision}
                canManageAllocation={canManageAllocationForBand}
                officeId={officeId}
                fiscalYear={fiscalYear}
              />
            )}
          </Band>
        )}

        {!isCrossOfficeReviewer && officesBand}

        {/* ── Recent activity ────────────────────────────────────────────── */}
        <Band
          title="Recent activity"
          description={officeLabel}
          // PPDO-181 — the audit log is SuperAdmin-only (the sidebar gates it the same way), so the
          // link is only offered to someone it will open for.
          actions={
            canSeeAuditLog ? (
              <Link href="/config/audit-log" className="text-xs font-medium text-green-600 hover:text-green-700">
                Full history →
              </Link>
            ) : undefined
          }
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
            <RecentActivityList entries={activity} />
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
