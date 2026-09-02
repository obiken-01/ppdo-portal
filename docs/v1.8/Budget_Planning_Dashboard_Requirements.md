# PPDO-20 (V18-30) — Budget Planning Dashboard

> **Authoritative spec.** Governed by `docs/SPEC_STANDARD.md`. Written 2026-09-02.
>
> Companion: the interactive wireframe (8 switchable accounts) is the visual reference for every
> layout decision below. Where this document and the wireframe disagree, **this document wins**.
>
> Read first: `docs/v1.8/Permission_Matrix.md` (every gate named here is a row in it),
> `docs/v1.8/Phase_Plan.md` §3.5, `docs/DESIGN_SYSTEM.md`, `docs/PERFORMANCE_GUIDELINES.md`.

---

## 1. Goal

Today the Budget Planning dashboard has two hardcoded shapes: a 2×2 readiness hub for everyone, and
two PPDO-only sections bolted below it. Guest-office users were deliberately parked on the hub
(RAL-230) because "an office dashboard belongs to the redesign". The 2026-08-25 PPDC meeting decided
Phase 1 ships that real dashboard.

Neither shape answers the question a person actually arrives with — *what do I have to do, and who
am I waiting on?* This spec replaces both with **one page that resolves per person**: same six
bands, gated by flags that already exist, so a PPDO encoder, a guest-office reviewer, the PBO
ceiling officer and a system administrator each get a page addressed to them.

It also makes the dashboard a viable landing target (V18-30's other half), which it is not while
office users land on a hub that reports nothing they can act on.

---

## 2. Decisions (settled)

1. **One page, six bands, gated by flag — not per-role page variants.** Role variants multiply: eight
   accounts would become eight components that drift. Bands are independently gated and composable,
   and the gates are flags the Permission Matrix already pins.

2. **The pipeline rail replaces the 2×2 readiness hub.** Ceiling → division allocation → PPA
   assignment → AIP → AIP submission is the real order of work and already exists in the code as a
   set of gates; the 2×2 hides that order and shows four of the five. **Each stage names its owner**
   — roughly half the support traffic on this feature is "why can't I edit this?", and the answer is
   almost always that the stage belongs to somebody else.

3. **WFP does not appear on the dashboard at all — including for PPDO.** WFP is about to become an
   update to what AIP creation already produced, so its present shape (own record, own coverage
   count, own planned total) is the thing being replaced. Reporting on it now would teach a model
   that goes wrong within a release and would be redesigned twice. WFP keeps its sidebar link and
   quick button for PPDO users; the dashboard simply stops reporting on it.

4. **Money comes from the AIP.** "Costed", not "planned in WFP". Follows directly from 3.

5. **Submission means the AIP, for every account.** v1.8.0's subject is the AIP. There is no WFP
   submission, review, or approval in this release.

6. **WFP and the Report page are PPDO-internal.** A guest office has no division split to build a
   WFP from and its users have never been shown the feature. The Report page renders a WFP, so it
   follows. **Already shipped** — `feature/v1.8.0-ppdo-20-hide-wfp-report-from-guest-offices`,
   commit `78ef5d7`, PR #279. Guest offices keep LDIP and AIP.

7. **Guest offices see a three-stage rail** (ceiling → AIP → AIP submission), not a five-stage one
   with two struck through. Division allocation and PPA assignment are host-office-only (settled
   2026-09-02, `Permission_Matrix.md` §4) and, with decision 3, WFP is gone as well. An earlier draft
   showed them struck through; that was reversed — a guest office does not need to be told about
   stages that will never apply to it.

8. **Fiscal year, office and division are all context fields.** They are the three axes that decide
   what the page shows, so all three are stated. A **locked** field (dashed border, no chevron) is an
   axis this account cannot change. **Guest offices get no division field at all** — division does
   not narrow them (`Permission_Matrix.md` §3.1), and rendering an inert control implies it might.
   This replaces an earlier "Seeing: RMED only" chip, which read as jargon.

9. **One status vocabulary, borrowed from Linear: Todo · In progress · Review · Done.** The page had
   grown five overlapping sets (Draft/Final, Met/Not yet, Set/Not set, Submitted, Not started).
   **`Over ceiling`, `Behind` and `Cannot submit` stay outside the four** — they are exceptions, not
   stages, and folding them in would lose the warning.

10. **Stacked bars, not donuts, for ceiling-by-fund.** On the live FY2027 page three of four donuts
    are a single 100% slice and five of six General Fund legend rows read ₱0.00. A stacked bar
    carries the same information, sorts by size, stays readable at six divisions, stacks on a phone,
    and removes the Chart.js dependency from this page.

11. **Ceiling row actions deep-link; they do not open a modal.** A ceiling is per office *per fund
    source*, so a modal grows a row per fund and becomes the Allocation page rebuilt in a dialog.
    `Edit` / `Set ceiling` navigate to `/budget-planning/allocation?officeId=N`, reusing the office
    picker PPDO-17 shipped. **`Bulk set from FY <prior>` does get a modal** — it is a single complete
    action with no page of its own, and it must list the offices and amounts it is about to create
    with the ability to drop rows before confirming.

12. **The office list is a new aggregate read, consumed through the existing cross-office entry
    points.** `OfficeScope.ResolveForReview` for reviewers, `ResolveForCeiling` for PBO — never
    `OfficeScope.Resolve`. Teaching `Resolve` either grant would silently promote a cross-office
    *reader* into a cross-office *editor* with no diff at any write site to notice it
    (`Permission_Matrix.md` §4).

13. **No schema change.** Everything here is a read.

### Open follow-ups (not blocking)

- **Is "unspent"/"remaining" the right word** for allocation minus costed? May be "unprogrammed" or
  "unobligated" in PBO's vocabulary. One question to the finance officers — it is stamped across four
  bands and an export.
- **Does a cross-office reviewer land on the roll-up or on their last-opened office?** Drawn as the
  roll-up with the picker remembering the last office. Reversible.
- **Cut-off date** — the rail and the action band both reference a submission deadline. There is no
  such field in the schema. See §7; it is a non-goal for this spec.
- **Division rename** — an `SPD` division is reportedly being merged into `FPIP`. Does not affect
  this spec (nothing keys on a division code) but will affect sample data and screenshots.

---

## 3. Behaviour

### 3.1 Core

| Case | Given | When | Then |
|---|---|---|---|
| Happy path — host office | PPDO Staff, division RMED, FY2028 has a ceiling and an AIP | Opens `/budget-planning` | Five-stage rail, action band, money tiles scoped to RMED, RMED's fund rows, RMED activity feed |
| Happy path — guest office | GSO Staff, FY2028 ceiling published | Opens `/budget-planning` | Three-stage rail, office-scoped tiles, no division field, no WFP anywhere |
| Edge: no fiscal year selected | User has never picked one | First load | Defaults to the latest FY with any record; if none, the newest FY in `fiscal-years` |
| Edge: no data for the FY | FY2029 exists in the picker, nothing recorded | Selects FY2029 | Every band renders its empty state; the rail shows all stages Todo. **Not** an error |
| Edge: no ceiling yet | Guest office, PBO has not published | Load | Blocking banner; stage 1 `risk`; drafting still permitted (decision in §2.7 of the wireframe notes); submission blocked |
| Edge: over-allocated | Division allocations exceed the office ceiling | Load | `Over ceiling` risk pill on the affected fund bar and the office row; figures still render |
| Edge: office with no reviewer | No user in that office holds `CanReviewBudgetPlanning` | Admin loads the office table | Row shows `Cannot submit` risk pill and "None — assign" in the Reviewer column |
| Edge: single office in scope | Cross-office reviewer, only one office has a ceiling | Load | Office table renders with one row — no special-casing to the single-office view |
| Failure: dashboard fetch rejects | API returns 500 | Load | Page shell and context bar still render; each failed band shows its own error state with a retry, per §6 |
| Failure: office-list fetch rejects | Office aggregate 500s, dashboard succeeds | Load | Other bands render normally; only the office table shows an error. One failing band must not blank the page |

### 3.2 Permission and scope cases

Every row below is a flag combination already pinned in `docs/v1.8/Permission_Matrix.md`.

| Account | Given | When | Then |
|---|---|---|---|
| SuperAdmin | Every flag resolves true | Load | Unrestricted: office picker over all offices, office table, review queue. Division field omitted while office = all |
| Admin, host office | No per-user grants (Admin is **not** auto-granted them) | Load | Host-office bands, **no** office table, **no** review queue, **no** fund-bar edit link |
| Admin, guest office | Deliberately tied to a guest office | Load | Guest-office view. **Office wins over role** — the SuperAdmin/Admin bypass governs feature flags, not data scope |
| Staff, host, division-scoped, no grants | `division_id` = RMED | Load | Money and tables clamped to RMED **server-side**. Office ceiling visible read-only (muted tile) |
| Staff, host, `CanManagePpdoAllocation` | Finance officer | Load | All six divisions, fund bars, division table with an Allocation link |
| Staff, host, `CanReviewAllOffices` | Cross-office reviewer | Load | Office table over every office, **read-only**. No write control anywhere in it |
| Staff, guest, `CanManagePboCeiling` | PBO officer | Load | Ceilings table over every office with `Set ceiling`/`Edit`. **No** division field, **no** rail, **no** review queue |
| Staff, guest, no grants | GSO encoder | Load | Own office only. Three-stage rail |
| Staff, guest, `CanReviewBudgetPlanning` | GSO reviewer | Load | Own office plus submission checklist and submit control |
| **Any user, `office_id` null** | Unassigned record | Load | `OfficeScope` → `NoOffice` (id 0) → **sees nothing**. Empty states, not an error, not everything. ⚠️ A null office is an incomplete record, not a privileged one (DECISION F / RAL-258) |
| **Any user, `division_id` null, host office** | Unassigned division | Load | `DivisionScope` → `Nothing`. Division-scoped bands empty |
| Staff requests another office via query string | Guest office user hits `?officeId=<other>` | Load | Server clamps to their own office. The response is their office's data — **not** a 403 that leaks the other office's existence |
| Caller lacks `CanAccessBudgetPlanning` | Any role resolving false | Load | 403 from the endpoint; the route guard in `(portal)/layout.tsx` has already redirected |

---

## 4. API contract

### 4.1 Reused unchanged

| Endpoint | Envelope | Notes |
|---|---|---|
| `GET /api/budget-planning/dashboard` | raw JSON | **`PpdoDashboardDto`** (v1.4.5/RAL-161 — it *replaced* the older multi-office `PlanningDashboardDto`, which still exists in the codebase; do not extend the wrong one). Already resolves the host office internally and clamps `WfpByDivision` and every `FundCeilingDto.ByDivision` entry to the caller's own division |
| `GET /api/budget-planning/dashboard/office` | `ApiResponse<T>` | `OfficeDashboardDto` — `?officeId&fiscalYear` |
| `GET /api/budget-planning/fiscal-years` | raw JSON | FY picker |
| `GET /api/budget-planning/activity` | raw JSON | Recent activity, `?officeId` |

> ⚠️ The envelope is inconsistent across these four and this spec does **not** fix it — see §7.

### 4.2 New — `GET /api/budget-planning/dashboard/offices`

One row per office in the caller's scope. JWT-protected.

**Gate:** `CanAccessBudgetPlanning` **and** at least one of `CanReviewAllOffices` /
`CanManagePboCeiling` / `SuperAdmin`. A caller with none of those has no cross-office scope and must
receive **403**, not an empty list — an empty list reads as "no offices exist".

**Scope resolution — the load-bearing part of this endpoint:**

```
CanReviewAllOffices   → OfficeScope.ResolveForReview(user, true)
CanManagePboCeiling   → OfficeScope.ResolveForCeiling(user, true)
neither               → 403
```

Never `OfficeScope.Resolve`. A caller holding both resolves through `ResolveForReview`; the
distinction only matters for what the UI renders, since this endpoint is read-only either way.

- **Request:** `?fiscalYear=<int>` (required)
- **200:** `ApiResponse<IReadOnlyList<OfficeSummaryDto>>`

```csharp
public record OfficeSummaryDto(
    int      OfficeId,
    string   OfficeCode,
    string   OfficeName,
    bool     IsHostOffice,
    decimal? CeilingAmount,      // null = not published
    decimal  CostedInAip,        // 0 when no AIP
    int      ActivityCount,
    string   AipStatus,          // "Todo" | "In progress" | "Review" | "Done"
    string   SubmissionStatus,   // same four
    bool     IsOverCeiling,
    string?  ReviewerName        // null = nobody can submit for this office
);
```

Slim by construction — no free-text AIP columns (a fat AIP DTO once produced a 1.2 MB response).
Fourteen offices today; **paginate when the count can exceed one screen**, not before.

| Status | Shape | When |
|---|---|---|
| 400 | `{ error: "fiscalYear is required." }` | Missing/unparseable FY |
| 401 | — | No/expired JWT |
| 403 | `{ error: "You do not have access to Budget Planning." }` | Fails the gate. Same message for both gate failures — do not distinguish, it enumerates grants |
| 500 | `{ error: "Could not load offices." }` | Log with `ILogger<T>` including `UserId` and `FiscalYear`; never the request body |

### 4.3 Changed — the per-division row becomes AIP-based

`PpdoDashboardDto.WfpByDivision` is a list of `DivisionWfpStatusDto`:

```csharp
public record DivisionWfpStatusDto(
    int DivisionId, string? DivisionCode, string DivisionName,
    string WfpStatus,                 // "Draft" | "Final" | "Not started"
    int ActivitiesWithExpenditures,   // WFP coverage
    int TotalActivities,
    decimal TotalAllocated,
    IReadOnlyList<DivisionFundAmountDto> AllocationByFund);
```

⚠️ **This is not merely missing a field — three of its members are WFP concepts**, and decisions 3
and 4 retire all three from this page. `ActivitiesWithExpenditures` counts activities that have a
*WFP expenditure*; the dashboard now needs activities that are *costed in the AIP*. Adding
`CostedInAip` beside `ActivitiesWithExpenditures` would leave two coverage counts on one row meaning
different things — exactly the confusion this redesign is removing.

Replace it. The property on `PpdoDashboardDto` is renamed `ByDivision`:

```csharp
public record DivisionSummaryDto(
    int      DivisionId,
    string?  DivisionCode,            // nullable — some divisions have no short code
    string   DivisionName,
    decimal  Allocated,               // was TotalAllocated
    decimal  CostedInAip,             // new — replaces the WFP expenditure total
    decimal  Remaining,               // new — Allocated - CostedInAip
    int      CostedActivityCount,     // replaces ActivitiesWithExpenditures
    int      TotalActivities,
    string   AipStatus,               // replaces WfpStatus
    string   SubmissionStatus,        // constant "Todo" until Phase 4 (§7)
    IReadOnlyList<DivisionFundAmountDto> AllocationByFund);
```

`DivisionCode` stays **nullable** — `Allocation_Requirements.md` §5 makes the code optional, with the
name as the fallback identifier. The UI must render the name when the code is null, not an empty pill.

Both new figures come from the AIP tables, which already hold them. **Query at the database** — a
`GroupBy`/`Sum` in a repository method, never `GetAllAsync()` then aggregate in memory
(`docs/PERFORMANCE_GUIDELINES.md`). **Await sequentially** — `DbContext` is not thread-safe, and
`Task.WhenAll` over two repo calls on the shared context is what produced the `GetStatsAsync`
production 500.

> **This is a breaking response change.** `WfpByDivision` is consumed by the current dashboard page
> and typed in `frontend/src/types/budget-planning.ts:312`. Ticket C carries both halves.

Note also that **the per-division clamp for division-scoped Staff lives on this payload** — the
service already narrows `WfpByDivision` and each `FundCeilingDto.ByDivision`. That clamp must survive
the rename; it is the mechanism behind the §3.2 row "Money and tables clamped to RMED server-side".

---

## 5. Data model changes

**None.** No new table, no new column, no migration, no backfill.

Indexes needed by §4.2/§4.3 (`aip_activities` by office + fiscal year; `division_allocations` by
office + fiscal year) should be **verified against the existing schema** before adding — the
allocation tables were indexed for exactly these lookups in v1.4.3.

> Because this section is empty, §8 names **no** manual `dotnet ef database update` step. That is a
> deliberate statement, not an omission.

---

## 6. UI states

### 6.1 `/budget-planning` — the dashboard

| State | Content |
|---|---|
| **Loading** | Page shell, title and context bar render immediately. Each band shows a skeleton matching its loaded structure — the rail shows five (or three) grey stage boxes of the real height, tiles show four grey tiles, tables show a header plus 5 grey rows. **Never a centered spinner replaced by a full-height table** (CLS — `PERFORMANCE_GUIDELINES.md` §6) |
| **Empty — no FY data** | Rail renders with all stages `Todo`. Tiles render with `—`. Tables show "No records for FY \<year\> yet." plus the relevant action link. The page is never blank |
| **Empty — no offices in scope** | Office table: "No offices have a FY \<year\> ceiling yet." For PBO, the primary action `Set ceilings` sits in the empty state |
| **Error** | Per band, not per page. `"Could not load <band>."` plus a `Retry` that refetches only that band. A failed office table must leave the rail and tiles intact |
| **Success** | As the wireframe |
| **Read-only / forbidden** | **Hidden, not disabled**, for anything the account can never do — a cross-office reviewer sees no `Edit` on any office row, rather than a greyed one. **Disabled** is reserved for actions blocked by *state* rather than permission: `Submit` is disabled with a reason ("Ceiling not published") when the account may submit but the checklist is unmet |
| **Validation** | No form on this page. The bulk-ceiling modal (§2 decision 11) validates per row, message under the field |

**Components:** reuse `ConfigPageHeader`, `RowActions`, `Modal`, `ConfirmDialog`, `useToast` from
`components/ui/`. **New, and worth extracting because both this page and the AIP detail page want
them:** `PipelineRail`, `StatusPill` (the four statuses plus risk), `StackedFundBar`, `ActionCard`.

Flat design throughout — no `rounded-*` on cards, panels, tables or buttons; `rounded-full` for
pills only. PPDO tokens only; `slate-800` headings, `slate-600` body. **Never `text-slate-700`.**

### 6.2 Shared state

Read the current user from the portal-layout context. **Do not call `/auth/me` per band** — the WFP
page once fired it four times per load.

---

## 7. Non-goals

- **Submission / review workflow.** No submission entity, no queue, no approval, no cut-off date
  exists in the schema — that is Phase 4. This spec renders the submission stage and any review queue
  **from a constant** (`"Todo"`, empty queue) so the layout does not move when Phase 4 fills it in.
  Everything submission-shaped in the wireframe is drawn ahead of its ticket.
- **A cut-off date.** Referenced in wireframe copy; there is no field for it. Ships when Phase 4 adds
  one. Do not invent a config value for it here.
- **WFP anywhere on this page.** Decision 3. Do not helpfully add it back.
- **Normalising the four endpoints' response envelopes.** Two return raw JSON, one uses
  `ApiResponse<T>`. Worth fixing; not here — it would touch every caller and bury this change.
- **Landing-page selection.** V18-16/V18-20 own that. This spec only makes the page worth landing on.
- **Charts.** Decision 10 removes the only chart. Do not reintroduce a charting library.
- **Export.** The office table shows an `Export` affordance in the wireframe; it is **not** in scope.
  Remove it from the implementation or ticket it separately.

---

## 8. Deployment notes

- **No migration.** Nothing to run against Azure SQL.
- **No new dependency.** Chart.js can be dropped from this page's imports; check it is not still
  needed elsewhere before removing the package.
- **No new environment variable or CORS origin.**
- **Ordering:** PR #279 (decision 6) is independent and can merge first or last.

---

## 9. Ticket split

| Ticket | Scope | Blocked by |
|---|---|---|
| **A** | Hide WFP + Report from guest offices — sidebar, route gate, prefetch | — (**done**, PR #279) |
| **B** | `GET /dashboard/offices` + `OfficeSummaryDto` + scope resolution + tests | — |
| **C** | Replace `DivisionWfpStatusDto` with `DivisionSummaryDto` (AIP-based), rename `WfpByDivision` → `ByDivision`, update the frontend type and the current page + tests. **Breaking response change** | — |
| **D** | Shared components: `PipelineRail`, `StatusPill`, `StackedFundBar`, `ActionCard` | — |
| **E** | Dashboard page rebuild — context bar, action band, rail, tiles | D |
| **F** | Table lane — division table (PPDO) and office table (cross-office / PBO / admin) | B, C, D |
| **G** | Ceiling row deep-links + bulk-set modal | F |

B, C and D are independent and parallelisable. **D is a good manual-implementation candidate** —
small blast radius, presentational, and `RowActions` is a near-identical sibling to pattern-match
against. B is **not** a candidate: its scope resolution is exactly the class of thing where a wrong
choice compiles cleanly and leaks data.

---

## 10. Acceptance checklist

```
- [ ] A PPDO Staff user in one division sees only that division's figures in the money tiles,
      the fund rows and the activity feed
- [ ] That same user sees the office ceiling as a muted, read-only tile — not an editable field
- [ ] A guest-office user sees a three-stage rail: Ceiling, AIP, AIP submission
- [ ] A guest-office user sees no Division field in the context bar
- [ ] A guest-office user sees no WFP or Report anywhere — sidebar, quick buttons, or page body
- [ ] Typing /budget-planning/wfp as a guest-office user redirects to their landing page
- [ ] A PPDO user still sees WFP and Report in the sidebar and the WFP quick button
- [ ] A guest office with no published ceiling sees the blocking banner, a red stage 1, and can
      still open and edit its AIP
- [ ] The PBO officer sees one row per office with Set ceiling on every unpublished row
- [ ] Clicking Set ceiling opens /budget-planning/allocation?officeId=<that office>, not a modal
- [ ] Clicking Bulk set from FY 2027 opens a modal listing the offices and amounts, with each row
      removable before confirming
- [ ] A cross-office reviewer sees no Edit, Set ceiling, or any other write control on any office row
- [ ] A user whose office_id is null sees empty states, not every office and not an error
- [ ] Selecting a fiscal year with no records shows every band's empty state and all stages Todo
- [ ] Killing the offices endpoint (500) leaves the rail and tiles rendered, with an error and a
      Retry inside the office table only
- [ ] First load shows skeletons matching the final layout — the page does not visibly jump
- [ ] Every status pill on the page reads Todo, In progress, Review, or Done, except the
      Over ceiling / Behind / Cannot submit risk pills
- [ ] No element on the page uses text-slate-700 (grep the compiled CSS)
- [ ] /auth/me is called once per page load, not once per band (check the network panel)
```

---

## 11. Test focus

| Class | Behaviour |
|---|---|
| `BudgetPlanningDashboardServiceTests` | `GetOfficesAsync` returns every office for `CanReviewAllOffices`; every office for `CanManagePboCeiling`; **403 for a caller with neither**; own office only is *not* a valid result for either grant — that would mean the wrong resolver was used |
| `BudgetPlanningDashboardServiceTests` | `CostedInAip` / `Remaining` per division; a division with an allocation and no AIP returns `Remaining == Allocated`, not null; a division whose `DivisionCode` is null still returns its name |
| `BudgetPlanningDashboardServiceTests` | **The division clamp survives the rename** — a division-scoped Staff caller gets exactly one entry in `ByDivision` and one entry in each `CeilingByFund[].ByDivision`. This test exists today against `WfpByDivision`; it must be carried over, not dropped with the old DTO |
| `PermissionMatrixTests` | No new flag, so no new row. **Confirm the build still passes** `Matrix_CoversEveryFlagOnThePermissionService` |
| `OfficeScopeTests` | Pin that the new endpoint's resolution cannot be satisfied by `Resolve` — mirror the existing `Resolve_IgnoresTheCrossOfficeGrant_SoWritePathsStayScoped` |
| `BudgetPlanningFunctionsTests` | Integration: 400 on a missing `fiscalYear`; 403 for a Staff caller with no cross-office grant; 200 with the expected row count for a reviewer |

TDD is mandatory for the scope resolution in ticket B (`CLAUDE.md` — auth flows and permission
resolution). The presentational work in D and E is "tests after".
