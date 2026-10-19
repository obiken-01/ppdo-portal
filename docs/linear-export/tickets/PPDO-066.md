---
id: PPDO-66
title: 'Remove the dead fleet-wide allocation-setup overview (GetSetupOverviewAsync)'
status: Done
project: PPDO Portal
labels: 
milestone: v1.8.3 — Concurrent-edit guard: turn it on
created: 2026-09-08
completed: 2026-10-07
parent: (none)
blocked_by: (none)
related: PPDO-4, PPDO-30, PPDO-57
url: https://linear.app/ralphoksiprojects/issue/PPDO-66
uuid: 7833368d-9be3-4645-a70c-773bf9ca5d17
---

# PPDO-66 — Remove the dead fleet-wide allocation-setup overview (GetSetupOverviewAsync)

**In one line:** `AllocationService.GetSetupOverviewAsync` and the DTO chain above it are unreachable — nothing calls them — and the classification logic left behind is wrong for guest offices, so it must not be revived as-is.

**Size:** S · **Found:** 2026-09-08, while implementing [PPDO-57](https://linear.app/ralphoksiprojects/issue/PPDO-57/v18-47-office-level-aip-ceiling-checks-for-offices-without-divisions) (office-level ceiling checks)

## ⚠️ Read this first — it is dead code, not a live bug

It was first reported to Ralph as a live guest-office bug on the Budget Planning dashboard. **That was wrong**, and the correction is the reason this ticket is a deletion rather than a fix. Verified by grep over `.cs` sources excluding `bin/`:

* **No Functions endpoint.** `grep -rn "SetupOverview" --include=*.cs backend/PPDO.Functions/` → empty.
* **No frontend caller.** No `setupOverview` / `setup-overview` / `AllocationSetupOverviewDto` anywhere in `frontend/src`.
* **The DTO above it is dead too.** `AllocationSetupOverviewDto` is reached only through `PlanningDashboardDto`, and nothing constructs `PlanningDashboardDto` either — its only other mention is a doc comment in `PpdoDashboardDtos.cs` explaining what replaced it.

`BudgetPlanningDashboardService`'s own class doc already says so, in the v1.4.5 / [RAL-161](https://linear.app/ralphoksiprojects/issue/RAL-161/budget-planning-dashboard-rework-ppdo-scoped-queries-per-divisionfund) PPDO-scoping rework:

> *the old fleet-wide "N offices set up" view (AllocationSetupOverviewDto) and its 8 unfiltered full-table scans are gone*

The **view** went. The service method, the DTOs and 10 tests did not.

## Why it should not simply be revived

If someone ever restores a fleet-wide setup view, this implementation is the wrong starting point. It classifies an office "fully set up" only with a ceiling **and** a positive **division** allocation **and** a **program→division** assignment, over *every active office*:

```csharp
bool hasAllocation = allocatedByOfficeId.GetValueOrDefault(office.Id, 0m) > 0;
bool hasAssignment = officesWithProgramAssignment.Contains(office.Id);
if (hasCeiling && hasAllocation && hasAssignment) fullySetup++;
```

`allocatedByOfficeId` is built by mapping division allocations back to offices via `Division.OfficeId`, and `officesWithProgramAssignment` requires `ProgramDivision` rows. **Division is a host-office-only axis** (`Permission_Matrix.md` §3.1, [PPDO-4](https://linear.app/ralphoksiprojects/issue/PPDO-4/scope-rule-division-is-not-a-scoping-axis-for-non-ppdo-office-users), [PPDO-30](https://linear.app/ralphoksiprojects/issue/PPDO-30/budget-planning-allocation-setup-status-divisionid-is-not-scoped-to)) — not "guest offices have none configured yet", but *not a scoping axis for them at all*, which [PPDO-57](https://linear.app/ralphoksiprojects/issue/PPDO-57/v18-47-office-level-aip-ceiling-checks-for-offices-without-divisions) has now made explicit in `AipCeilingService`. So a guest office can satisfy neither condition, and would have been counted **permanently "incomplete"** no matter what PBO configured. Silent, and unfixable from the PBO screen.

## Scope

Delete, and let the type system find the rest:

1. `IAllocationService.GetSetupOverviewAsync` + its `AllocationService` implementation (`AllocationService.cs:445`)
2. `GetOfficesWithProgramAssignmentAsync` — the private bulk helper that exists only to serve it (`AllocationService.cs:~495`)
3. `AllocationSetupOverviewDto` (`AllocationDtos.cs:51`) and `PlanningDashboardDto` (`PlanningDashboardDtos.cs:14`), once confirmed nothing else builds them
4. The **10** `GetSetupOverview_*` tests in `AllocationServiceTests.cs`

⚠️ `GetSetupStatusAsync` **stays.** It is the *per-office* WFP setup gate, it is live (`WfpService.cs:120`), and it takes a `divisionId` deliberately — [PPDO-30](https://linear.app/ralphoksiprojects/issue/PPDO-30/budget-planning-allocation-setup-status-divisionid-is-not-scoped-to) fixed a cross-office leak there. Only the *fleet-wide overview* is dead. Do not delete both because the names look alike.

## Done when

* `GetSetupOverviewAsync`, its helper, its DTOs and its 10 tests are gone
* `GetSetupStatusAsync` and the WFP setup gate are untouched, and their tests still pass
* `dotnet test` passes; no compiler warnings about unused private members left behind
* If a fleet-wide view is ever wanted again, the guest-office flaw above is written down where whoever rebuilds it will see it — this ticket is that record

## Out of scope

Rebuilding a fleet-wide setup view · changing `GetSetupStatusAsync` · giving guest offices divisions · any change to the office-level ceiling path [PPDO-57](https://linear.app/ralphoksiprojects/issue/PPDO-57/v18-47-office-level-aip-ceiling-checks-for-offices-without-divisions) just shipped.

## Sequencing

Per the standing rule, techdebt is **not** started while v1.8.0 is in flight, and moves into the active version milestone when it is picked up.
