---
id: PPDO-189
title: 'Perf: stock balance import, set-based instead of 3–4 queries per row'
status: Done
project: PPDO Portal
labels: 
milestone: v1.8.2 — Dashboard Redesign, Caching & Remaining Optimizations
created: 2026-10-04
completed: 2026-10-05
parent: (none)
blocked_by: (none)
related: (none)
url: https://linear.app/ralphoksiprojects/issue/PPDO-189
uuid: 374481b7-7702-46d7-b605-eaa1841f9a41
---

# PPDO-189 — Perf: stock balance import, set-based instead of 3–4 queries per row

From the performance audit, 2026-10-04 (finding **O7**). Optimization ticket 8 of 8. Backend, size M. Inventory, so last in line (feature priority order).

## Current behaviour

`StockBalanceService` commit (lines 359–380) runs, per row and inside one transaction:

* `EnsureItemMasterAsync` (item lookup, maybe insert)
* `FindByStockNoAndEffectiveDateAsync`
* `ComputeSystemOnHandAsync` (an aggregate)

A few-hundred-row file means a thousand-plus round trips while the transaction is held open, on a 5-DTU Basic database.

## Target behaviour

* Before the loop, load for all stock numbers in the file:
  * the item masters;
  * the existing balances for each (stock no, effective date);
  * the on-hand totals, grouped.
* Loop in memory; keep the single transaction and the EF retry strategy.
* **Unchanged:** row-level error messages and the all-or-nothing result.

## Acceptance

- [ ] Existing `StockBalanceServiceTests` pass; a new test covers two rows for the same stock number in one file.
- [ ] Import of a 300-row file locally: time and query count before and after on the PR.
- [ ] Re-uploading the same file still overwrites, not duplicates.
