# Budget Planning: PPDO-only scoping + Dashboard optimization — findings

> **Status: findings only — no implementation yet.** Written 2026-07-16 from an Explore-agent
> survey of every office picker in Budget Planning and the Dashboard's data flow. Goal: (1) stop
> loading the office dropdown in most Budget Planning pages since PPDO is effectively the only
> office in real use, (2) scope the Dashboard to PPDO + its divisions (varying by division-scoped
> Staff vs finance/admin), (3) fix the Dashboard's query pattern.

---

## 1. Office dropdown/picker survey

Every page loads the **entire** office list client-side via `listOffices({active:"true"})` →
`GET /api/config/offices` (`backend/PPDO.Functions/Functions/ConfigOfficeFunctions.cs:39`),
unfiltered by caller, even though in practice only the PPDO row is ever relevant. The existing
PPDO-scoping helper is `PPDO_OFFICE_CODE` / `findPpdoOffice()` in `frontend/src/lib/config.ts:113-129`.

| Page | Office list load | Picker locked how | Uses `findPpdoOffice`? |
|---|---|---|---|
| Dashboard (`budget-planning/page.tsx`) | line 361-363, unconditional (even for office users) | Rendered only for PPDO users (`user?.officeId == null`, line 399); office users get a static `<span>` | **No** — defaults to "All Offices" (`selectedOffice = null`), not PPDO |
| WFP Entry (`wfp/entry/page.tsx`) | line 918 | `disabled={isOfficeUser \|\| !canBypassDivision}` (line 1360) — locked for office users AND for PPDO Staff without `canManageAllocation` | Yes (line 938) |
| WFP main (`wfp/page.tsx`) | line 669 | Same `canBypassDivision` gate + `isFinal` lock (line 1174) | Yes (line 693) |
| Allocation (`allocation/page.tsx`) | line 493 | Only `isOfficeUser` is locked (line 461); **any PPDO Staff with `canAccessBudgetPlanning` can freely switch office** — deliberate per code comment (lines 502-504): "Allocation manages ceilings across offices" | Yes, but stays editable (line 511) |
| LDIP Form (`ldip/LdipForm.tsx`) | line 238 | Only `isOfficeUser \|\| isReadOnly` locked (line 538); **no role gate at all** — any PPDO Staff can pick any office on create | **No** — gap, PPDO users must manually pick on every new LDIP |
| WFP Report (`report/page.tsx`) | Different source — `getWfpReportOffices(fiscalYear)`, naturally scoped to offices with a Draft WFP | Raw `<select>`, `disabled={... \|\| !canBypassDivision}` (line 590) | Yes (line 446) |
| AIP pages | No dropdown at all — AIP records are inherently multi-office (one upload spans every implementing office), grouped for display, not picked | n/a | n/a |

**Safety check:** no reference to any non-PPDO office code (e.g. GSO) anywhere in `backend/` or
`frontend/src` — the "office user" mechanism is generic code capability, not evidence of a
currently-provisioned external office actually using these pages. Every page already fully locks
the picker for real office users (`office_id` set) regardless of what we do here — the only
behavior change from removing/defaulting the dropdown is for **PPDO-internal users**, who today
can (inconsistently) pick a non-PPDO office on Allocation and LDIP specifically (no
`canManageAllocation` gate on either).

---

## 2. Budget Planning Dashboard — current state

### 2.1 What it shows

`PlanningDashboardDto` (`backend/PPDO.Application/DTOs/BudgetPlanning/PlanningDashboardDtos.cs:14-22`):
- LDIP summary — **all offices, unconditionally, no FY filter at all**
- AIP summary — all offices, current FY
- WFP summary (`FinalCount`, `ActiveOfficeCount`) + **`WfpByOffice`** — one row per active office,
  rendered as "WFP Status by Office" table
- `AllocationSetupOverviewDto` — office-level ceiling/allocated/remaining counts, all active offices
- Recent activity (audit log, last 10, optional office filter)

Office-scoped variant `OfficeDashboardDto` — allocation summary, LDIP/AIP counts for **one**
office, no division breakdown.

**Nothing here is division-scoped today.** There is no per-division remaining-allocation display,
no "activities with WFP expenditures" count, and no Staff-vs-finance view variation.

### 2.2 Server-side scoping — currently none

All three Dashboard endpoints (`BudgetPlanningDashboardFunctions.cs`: `GetDashboard`,
`GetActivity`, `GetOfficeDashboard`) validate the JWT and check `CanAccessBudgetPlanningAsync`,
then **discard the resolved `caller` User** — only the client-supplied `officeId` query param (if
any) scopes the response. Contrast with `WfpReportFunctions.GetPreview`, which explicitly
re-derives and clamps `divisionId` server-side for non-finance callers (RAL-136 pattern) — the
Dashboard never adopted this.

The reliable source for role/division server-side is the `User` object `_jwt.ValidateAsync(...)`
already returns inside each Function (`Role`, `DivisionId`, `OfficeId` all populated) — it's just
not threaded into `IBudgetPlanningDashboardService` today (no method takes a `User`/role/division
param). Note: `ICurrentUserService`/`CurrentUserService` exists but has **zero consumers** anywhere
in Application services — it's dead code for this purpose, don't build on it.

### 2.3 Query pattern — the CLAUDE.md anti-pattern, repeated

`BudgetPlanningDashboardService.GetDashboardAsync` (lines 42-131) does **four unfiltered
full-table loads**, then filters/counts in memory:
- `_ldipRepo.GetAllAsync()` — entire `ldip_records`, no FY filter anywhere
- `_aipRepo.GetAllAsync()` — entire `aip_records`, filtered to FY in memory
- `_wfpRepo.GetAllAsync()` — entire `wfp_records`, filtered/grouped in memory
- `_officeRepo.GetAllAsync()` — entire `offices`, filtered to `IsActive` in memory

Plus it calls `AllocationService.GetSetupOverviewAsync`, which internally does **4 more**
unfiltered `GetAllAsync()` loads (office/ceiling/division/allocation repos). **One Dashboard page
load = 8 unfiltered full-table scans**, growing linearly with total row counts across every office
— this is exactly the `IRepository<T>.GetAllAsync()`-then-filter pattern CLAUDE.md's Performance
Guidelines forbid.

`GetOfficeDashboardAsync`'s `BuildOfficeAipSummaryAsync` similarly does two full-table scans
(`_officeRepo.GetAllAsync()` + `FirstOrDefault`, `_aipRepo.GetAllAsync()` + `FirstOrDefault`) just
to find one office row and one AIP record — should be single-row `WHERE` queries.

**Good pattern already in the codebase to copy:** `GetRecentActivityAsync` delegates to
`IAuditRepository.GetRecentAsync(10, officeId, ct)`, which pushes `ORDER BY` + `WHERE` + `Take` to
SQL — the audit table is never materialized. `BuildAllocationSummaryAsync` and
`BuildOfficeLdipSummaryAsync` also delegate to properly-scoped existing methods
(`GetListAsync(officeId, ...)`, `AllocationService` methods) rather than re-querying — this is the
model to extend, not `GetDashboardAsync`'s own four loads.

**No N+1 (per-row-in-a-loop) issues found** in the Dashboard service — the inefficiency is
"full table then filter in memory," not per-item round-trips.

### 2.4 Reusable building blocks for the requested new stats

- **"Remaining allocation per division"** — already computed, fund-scoped, at
  `WfpCeilingService.GetStatusAsync` (`WfpCeilingService.cs:51-84`), returns
  `WfpFundCeilingDto(fundId, code, name, allocation, remaining)` per fund + a GF-specific
  `gfRemaining`, backed by the `WfpDivisionAllocationLedger` table (kept current on save, not
  summed live). This is the right primitive to build a per-division Dashboard breakdown on —
  just needs to be called once per division instead of the single-division use it has today.
- **"How many activities have WFP expenditures"** — **no existing method**. `IWfpExpenditureRepository`
  has per-activity/batch lookups and sum-by-activity/by-record, but nothing that counts distinct
  activities with ≥1 expenditure. Would need a new scoped query
  (`COUNT(DISTINCT WfpActivityId) WHERE ...`), cheap to add as a proper SQL aggregate — not
  something to build via `GetAllAsync()`.
- **Office-level allocated totals** — `AllocationService.GetAllocationsAsync(officeId, fiscalYear,
  fundingSourceId, ct)` already returns `DivisionAllocationDto[]` (per division); the Dashboard's
  existing `BuildAllocationSummaryAsync` just re-sums this to one office total today — a
  per-division view can reuse the same call without a new query, just skip the re-summing.
- `AllocationSetupOverviewDto`/`GetSetupOverviewAsync` gives fully-setup/incomplete/not-started
  counts across offices for a FY, but its internals carry the same `GetAllAsync()`-then-filter
  pattern (lines 380-394) — reusable as a data source but would need the same query fix applied.

---

## 3. Open questions before implementing

1. **Office dropdown removal scope** — for pages where a PPDO-internal user can currently pick a
   non-PPDO office (Allocation, LDIP), is that capability actually used/needed, or can it be
   removed/defaulted-to-PPDO like the other pages? Allocation's code comment claims it's
   deliberate ("manages ceilings across offices") — worth confirming with Ralph whether that's
   still true in practice or was written for a hypothetical.
2. **Dashboard office picker** — for PPDO users, should it be removed entirely (hard-lock to
   PPDO, matching every other page's `disabled` treatment) or kept but pre-selected to PPDO by
   default (so a finance officer can still explicitly switch to another office if one is ever
   provisioned)?
3. **New Dashboard stats — exact shape** — "how many activities have WFP expenditures" and
   "remaining allocation per division": scoped to which fiscal year (current only, or a picker)?
   Shown per fund source or General-Fund-only (matching the rest of the app's GF-centric history)?
4. **Staff (division-scoped) view** — should Staff see only their own division's numbers with no
   awareness other divisions exist, or their own division highlighted within an office-wide list
   (closer to how the WFP Report page's Division filter already works for division-scoped
   callers)?
5. **Server-side scoping enforcement** — recommend adding the `WfpReportFunctions.GetPreview`
   pattern (re-derive/clamp scoping server-side from `caller`, ignore client-supplied
   office/division params for non-finance roles) to the Dashboard endpoints, since today a
   division-scoped Staff user's dashboard request isn't actually enforced server-side at all —
   confirm this is in scope for this change, not a separate security fix.

---

## 4. Rework plan — Dashboard, in place (confirmed 2026-07-16)

**Decision:** rework `budget-planning/page.tsx` + `BudgetPlanningDashboardService` in place (same
route), not a new page. Scope for this round: **Dashboard only** — the office-dropdown gaps on
Allocation/LDIP (§3 open question 1) are deferred, not part of this plan.

### 4.1 Facts that shape the plan (confirmed via a second Explore pass)

- **No `IOfficeRepository` exists at all.** Every office lookup today is `IRepository<Office>
  .GetAllAsync()` + in-memory `FirstOrDefault` — `Office`'s PK is `int`, and the generic repo's
  `GetByIdAsync` is `Guid`-keyed (CLAUDE.md's documented gap: "int-keyed entities need a feature-repo
  by-id method"). **New `IOfficeRepository : IRepository<Office>` needed** with `GetByIdAsync(int)`
  and `GetByCodeAsync(string)` — matches the existing `PurchaseRequestRepository`/`CalendarEventRepository`
  convention.
- **`ILdipRepository.GetListAsync(int? officeId, string? status, ct)` already exists and is
  properly SQL-scoped** — `GetDashboardAsync`'s unfiltered `GetAllAsync()` can be replaced with a
  call to this directly once Dashboard is single-office scoped. No new LDIP repo method needed.
- **`IWfpRepository.GetFilteredAsync(int? aipRecordId, int? officeId, int? divisionId, ct)` already
  exists and is properly SQL-scoped** — but `BudgetPlanningDashboardService` currently injects the
  generic `IRepository<WfpRecord>`, not `IWfpRepository`, so it can't use it today. **Swap the
  injected type.** No new WFP repo method needed for record-level scoping.
- **`IAipRepository` has no fiscal-year-scoped single/latest-record lookup** — needs a new
  `GetLatestByFiscalYearAsync(int fiscalYear, ct)` (single `WHERE FiscalYear = @fy ORDER BY ...
  TOP 1` query) to replace the `GetAllAsync()` + in-memory filter used in both `GetDashboardAsync`
  and `BuildOfficeAipSummaryAsync`.
- **No existing query counts distinct WFP-expenditure-bearing activities.** New
  `IWfpExpenditureRepository.CountActivitiesWithExpenditureAsync(int officeId, int? divisionId, int
  fiscalYear, ct)` needed — a real `COUNT(DISTINCT wfp_activity_id)` scoped join, not built from
  existing methods.
- **`AllocationService.GetSetupOverviewAsync` is fleet-wide by design** (its own docstring says it
  was deliberately written as a bulk/all-offices query to avoid a per-office loop) — scoping it to
  one office would mean an entirely different method shape, not a parameter add. **Decision: drop
  this call from the Dashboard entirely** rather than rewrite it — "how many of N offices are fully
  set up" stops being a meaningful stat once the Dashboard is permanently scoped to one office. Its
  replacement is the per-division allocation table (§4.3) built from methods that already exist
  (`AllocationService.GetCeilingAsync`/`GetAllocationsAsync`, already used by
  `BuildAllocationSummaryAsync`).
- **Per-division remaining allocation is already computed**, just single-division:
  `WfpCeilingService.GetStatusAsync(officeId, divisionId, fiscalYear, ct)`. Plan: call it once per
  division (small N — PPDO has ~6 divisions) with **sequential `await`s**, never `Task.WhenAll`
  (CLAUDE.md: `DbContext` is not thread-safe — this exact mistake caused a prior prod 500 in
  `GetStatsAsync`).
- **28 existing tests in `BudgetPlanningDashboardServiceTests.cs` mock the exact current
  signatures** (`IRepository<WfpRecord>`/`IRepository<Office>`/`GetAllAsync`) — swapping to
  `IWfpRepository`/`IOfficeRepository` **will break these mocks**. Expect to rewrite most of this
  test file, not patch it — treat as an accepted cost of the fix, not a regression to avoid by
  keeping the old (broken) query pattern.

### 4.2 New/changed DTO shape

Replace `PlanningDashboardDto`'s multi-office fields with a PPDO-scoped shape (name TBD, e.g.
`PpdoDashboardDto`):

```
FiscalYear, AvailableFiscalYears
LdipSummary        — status counts, PPDO only          (was: all offices)
AipSummary         — program/project/activity counts, PPDO only, FY-scoped
Wfp:
  ByDivision[]     — one row per division: WfpStatus (Draft/Final/NotStarted),
                     ActivitiesWithExpenditures, TotalActivities   (replaces WfpByOffice)
Allocation:
  ByDivision[]     — one row per division: Ceiling, Allocated, Remaining, per-fund breakdown
                     (from WfpCeilingService.GetStatusAsync)        (replaces AllocationSetupOverviewDto)
RecentActivity[]   — unchanged, already correctly scoped
```

**Division-scoped Staff vs finance/admin:** server clamps both `ByDivision[]` arrays to a single
entry (`caller.DivisionId`) for non-`CanManageAllocation` callers, mirroring
`WfpReportFunctions.GetPreview`'s existing division-clamp pattern — not a client-side filter. This
also shrinks the response payload for Staff callers as a side effect.

### 4.3 Backend sequencing

1. **New `IOfficeRepository`** (`GetByIdAsync(int)`, `GetByCodeAsync(string)`) + EF implementation.
2. **New `IAipRepository.GetLatestByFiscalYearAsync(int fiscalYear, ct)`.**
3. **New `IWfpExpenditureRepository.CountActivitiesWithExpenditureAsync(officeId, divisionId?,
   fiscalYear, ct)`.**
4. **Swap `BudgetPlanningDashboardService`'s injected `IRepository<WfpRecord>` → `IWfpRepository`,
   `IRepository<Office>` → `IOfficeRepository`.**
5. Rewrite `GetDashboardAsync`: resolve PPDO's office id once via `GetByCodeAsync("PPDO")`; replace
   the four `GetAllAsync()` calls with `GetListAsync(ppdoId, null)` (LDIP),
   `GetLatestByFiscalYearAsync(fy)` (AIP), `GetFilteredAsync(null, ppdoId, null)` (WFP, all
   divisions) or `GetFilteredAsync(null, ppdoId, caller.DivisionId)` (WFP, Staff); drop the
   `GetSetupOverviewAsync` call.
6. Add the per-division loop: for each active division under PPDO (or just `caller`'s division for
   Staff), sequentially `await WfpCeilingService.GetStatusAsync(...)` +
   `CountActivitiesWithExpenditureAsync(...)`.
7. Thread `caller: User` into `IBudgetPlanningDashboardService`'s three method signatures (today
   none take it) so the service — not just the Function — can enforce the division clamp; Functions
   already resolve `caller` via `_jwt.ValidateAsync` but currently discard it (§2.2).
8. Fix `BuildOfficeAipSummaryAsync`'s two full-table scans using the new
   `IOfficeRepository`/`IAipRepository` methods from steps 1-2 (same fix, reused).
9. Rewrite `BudgetPlanningDashboardServiceTests.cs` against the new mocked interfaces — expect most
   of the 28 tests to need rewriting, plus new tests for the division-clamp behavior and the new
   count query.

### 4.4 Frontend sequencing

1. Remove `listOffices()` call and the "All Offices" `OfficeSelect` entirely from
   `budget-planning/page.tsx` — no dropdown, no office-switching state at all (Dashboard becomes
   permanently PPDO-scoped, consistent with the rest of the plan's premise).
2. Replace the "WFP Status by Office" table with a "WFP & Allocation by Division" table — one row
   per division (or one row, pre-filtered, for Staff), columns: WFP status, activities-with-expenditures
   / total, ceiling, allocated, remaining.
3. Verify the page still renders sensibly for both roles live (SuperAdmin/finance sees full
   division table; a division-scoped Staff test account sees only their own row) — same
   verification method used for the WFP Report page (Browser pane, both account types).

### 4.5 Explicitly out of scope for this round

- Office-dropdown removal on Allocation/LDIP (§3 question 1) — separate follow-up.
- Any change to `AllocationService.GetSetupOverviewAsync` itself — it's left as-is for whatever
  other caller needs the fleet-wide view (if any currently exists — not checked, since this plan
  drops the Dashboard's call to it rather than fixing it).
- Fund-source scope for the per-division breakdown defaults to **all active funds** (matching
  `WfpCeilingService.GetStatusAsync`'s existing per-fund array) unless told otherwise — no
  GF-only cut planned.
