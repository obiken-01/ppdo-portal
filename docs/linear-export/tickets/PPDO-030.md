---
id: PPDO-30
title: '[Budget Planning] Allocation setup status: divisionId is not scoped to the office — cross-office leak'
status: Done
project: PPDO Portal
labels: 
milestone: v1.8.0 — Office Users, AIP Redesign & Reviewer Flow
created: 2026-09-02
completed: 2026-09-02
parent: (none)
blocked_by: (none)
related: PPDO-18, PPDO-38, PPDO-57, PPDO-66, PPDO-135
url: https://linear.app/ralphoksiprojects/issue/PPDO-30
uuid: e9e36e33-1246-4a6b-bb2f-a70a4c86de30
---

# PPDO-30 — [Budget Planning] Allocation setup status: divisionId is not scoped to the office — cross-office leak

**Severity:** 🟠 High — returns wrong results on the WFP setup gate, and leaks a boolean across the office boundary.
**Found:** 2026-09-02, while live-verifying [PPDO-18](https://linear.app/ralphoksiprojects/issue/PPDO-18/office-clamp-on-allocationfunctions-cross-office-read-leak-with-a-pbo) on the local instance.
**Component: **`GET /api/budget-planning/allocation/status` · `AllocationService.GetSetupStatusAsync`

## Problem

[PPDO-18](https://linear.app/ralphoksiprojects/issue/PPDO-18/office-clamp-on-allocationfunctions-cross-office-read-leak-with-a-pbo) clamped `officeId` on this endpoint. `divisionId` **is neither clamped nor checked against that office**, and `hasAllocation` is computed from `divisionId` alone:

```csharp
// AllocationService.GetSetupStatusAsync
hasAllocation = await _allocationRepo.HasPositiveAllocationAsync(
    divisionId, fiscalYear, generalFundId, ct);   // no officeId anywhere
```

`IDivisionAllocationRepository.HasPositiveAllocationAsync` takes no office parameter, and `division_allocations` has no `office_id` column — ownership lives on `divisions.office_id` — so nothing ties the two query-string parameters together.

## Steps to reproduce

As `pto.user` (Provincial Treasurer's Office, id 3 — an office that owns **no divisions at all**):

```
GET /api/budget-planning/allocation/status?officeId=3&fiscalYear=2027&divisionId=<n>
```

| `divisionId` | Owner | `hasAllocation` |
| -- | -- | -- |
| 1 | **PPDO** / ADMIN — GF allocation ₱6,854,499.76 | **true** |
| 2 | **PPDO** / PLANNING — GF allocation ₱3,693,517.11 | **true** |
| 8 | PGO / PGTST — no GF allocation | false |
| 99 | does not exist | false |

**Expected: **`hasAllocation` false for every one of these — none of those divisions belong to PTO.
**Actual:** true whenever the *arbitrary* division id happens to carry a positive General Fund allocation.

## Two distinct problems

1. **Cross-office information leak.** Division ids are small sequential integers. Any authenticated user holding `CanAccessBudgetPlanning` — **on by default for office users** — can enumerate them and learn which of PPDO's divisions carry a positive General Fund allocation. Same defect class as [PPDO-18](https://linear.app/ralphoksiprojects/issue/PPDO-18/office-clamp-on-allocationfunctions-cross-office-read-leak-with-a-pbo), on the axis [PPDO-18](https://linear.app/ralphoksiprojects/issue/PPDO-18/office-clamp-on-allocationfunctions-cross-office-read-leak-with-a-pbo) did not cover.
2. **The gate answers the wrong question. **`hasAllocation` is meant to mean *"does this office's division have a budget"*; it means *"does any division with this id have one"*. For an office with no divisions the WFP setup gate can report allocation-complete on the strength of another office's numbers.

The second is arguably worse than the first: the leak is one boolean, but the gate is what unlocks WFP expenditure entry.

## Not affected

`hasCeiling` is office-scoped (`_ceilingRepo.FindAsync(officeId, …)`). `hasProgramAssignment` is too — `GetProgramDivisionsByOfficeIdAsync(officeId)` then filters by division, so a foreign division simply finds no match. `hasAllocation` **is the only unscoped one.**

## Suggested fix

Add `officeId` to `HasPositiveAllocationAsync` and join `divisions` so the ownership check runs **in SQL**, per `docs/PERFORMANCE_GUIDELINES.md` — rather than resolving the office's divisions in memory in the service, which would add a full-table division fetch to a method that currently does not need one.

`AllocationService.GetSetupStatusAsync` is the only caller. Two tests touch the signature (`AllocationServiceTests` lines ~150 and ~1020).

`BudgetPlanningScope` already exists to pair the office and division axes and is the right long-term home for this rule, but it has no consumer yet — wiring it in here is a larger change than this fix needs.

## Test focus

* A division belonging to another office returns `hasAllocation: false`, even when that division has a positive allocation. ← the bug
* A division belonging to the requested office is unaffected.
* A non-existent division id returns false.
* `hasCeiling` / `hasProgramAssignment` behaviour is unchanged.
