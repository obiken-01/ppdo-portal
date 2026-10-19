---
id: PPDO-31
title: 'Enforce 1 program : 1 division on PPA assignment'
status: Canceled
project: PPDO Portal
labels: 
milestone: techdebt
created: 2026-09-02
completed: 2026-10-07
parent: (none)
blocked_by: (none)
related: PPDO-1, PPDO-20, PPDO-32, PPDO-39, PPDO-148
url: https://linear.app/ralphoksiprojects/issue/PPDO-31
uuid: c5b5eec5-1e5e-4d8e-8863-c73a1d8f51dc
---

# PPDO-31 — Enforce 1 program : 1 division on PPA assignment

**Do this as part of the WFP rework, not v1.8.0.** Rationale for deferring is at the bottom.

## The problem

`program_divisions` lets one program be assigned to N divisions. The WFP entry wizard then filters programs by **membership**, not exclusivity:

```ts
// wfp/entry/page.tsx:1044  (also wfp/page.tsx:890)
.filter((a) => a.divisionIds.includes(selectedDivisionId))
.map((a) => a.programRefCode)
```

So a program assigned to two divisions appears in **both** divisions' WFP wizards, and each can cost its activities independently against its own allocation. Nothing anywhere detects it. That is a double-planning path for real money, not a display quirk.

The same sharing also makes the [PPDO-20](https://linear.app/ralphoksiprojects/issue/PPDO-20/budget-planning-dashboard-one-page-that-resolves-per-person) dashboard's per-division column non-additive: each row carries a shared program in full, because the row answers "what is this division responsible for". Found live on 2026-09-02 — the dashboard rail read **140 activities** against the office table's **139** for PPDO, from a single shared program. The dashboard was made immune by reading office-level AIP totals instead of summing division rows; the underlying data condition was never fixed.

## Current state

| Layer | State |
| -- | -- |
| **Schema** | Unique index `IX_program_divisions_ref_div` on *(office_ref_code, program_ref_code, division_id)*. Blocks duplicate rows, **permits N divisions per program** |
| **API** | `UpsertProgramAssignmentDto.DivisionIds` is `IReadOnlyList<int>`; `AllocationService.UpsertProgramAssignmentAsync` writes one row per id |
| **UI** | Allocation page → PPA tab has a deliberate **Single / Multi toggle** per program, with its own bug-fix history (multi kept reverting to Single on reload) |
| **Data (dev, 2026-09-02)** | 19 rows / 19 distinct programs — **zero violations**. The one instance, `1000-000-1-01-010-003` shared by RMES + OG-CSO, was reassigned to ADMIN by Ralph |

⚠️ **Multi-assignment is a built feature, not an oversight.** Someone implemented the toggle and then fixed a bug in it. This ticket deletes working functionality. Ralph's call, 2026-09-02: the rule is 1 program : 1 division.

## Scope

1. **Data check first. **`SELECT office_ref_code, program_ref_code, COUNT(*) FROM program_divisions GROUP BY 1,2 HAVING COUNT(*) > 1` must return zero rows in the target environment, or the migration fails. Dev is clean; **production has not been checked** — do that before writing the migration.
2. **Migration** — replace `IX_program_divisions_ref_div` with a unique index on *(office_ref_code, program_ref_code)*. ⚠️ **MIGRATION**: CI does not run migrations; needs a manual `dotnet ef database update` against Azure SQL.
3. **API** — `UpsertProgramAssignmentDto.DivisionIds` → single nullable `DivisionId`; validator rejects more than one. `ProgramAssignmentDto.DivisionIds` collapses to `DivisionId?` (breaking response change — the Allocation page and the two WFP pages consume it).
4. **UI** — remove the Single/Multi toggle from the PPA tab; radio only. Drop the derived `multiDivision` state and the re-derivation block it needs.
5. **WFP** — with a single division, the two `.includes()` filters become an equality check. Verify no other consumer assumes a list.
6. **Dashboard** — simplify the attribution loop in `BudgetPlanningDashboardService.BuildByDivisionAsync`, and close the open follow-up in `docs/v1.8/Budget_Planning_Dashboard_Requirements.md` §2. **Keep reading office totals for the tiles** — correct regardless, and cheap insurance.

## Out of scope

* Backfilling or auto-resolving existing multi-assignments. A shared program needs a human to say which division owns it.
* The WFP rework itself. This is a precondition to tidy up while that work is open, not part of its feature scope.
* `ProgramDivision`'s ref-code keying. That is deliberate and documented on the entity ([RAL-249](https://linear.app/ralphoksiprojects/issue/PPDO-1/programdivision-replace-ref-code-string-keys-with-real-fks)) — do not "finish the job" with an FK to `aip_programs.Id`.

## Why not v1.8.0

* Nothing in v1.8.0 depends on it — the [PPDO-20](https://linear.app/ralphoksiprojects/issue/PPDO-20/budget-planning-dashboard-one-page-that-resolves-per-person) dashboard is correct either way.
* Exposure is currently **zero rows**; the acute instance is fixed.
* It would be a **third** manual migration on a release that already carries `AddClimateChangeTypologies` and `AddEsreCodes`, with no v1.8.0 benefit.
* The bug lives in WFP, which is reworked immediately after v1.8.0. That is its natural home.
* `docs/v1.8/RETROSPECTIVE.md`: releases this size are what produced the v1.4 and v1.7 patch trains.

**Residual risk while deferred:** with no enforcement, the Multi toggle can recreate the condition at any time. If that becomes a concern before the WFP rework, step 4 alone (hide the toggle — ~20 lines, no migration, no API change) closes the door without release risk.

Found while implementing [PPDO-20](https://linear.app/ralphoksiprojects/issue/PPDO-20/budget-planning-dashboard-one-page-that-resolves-per-person) (PR #281).
