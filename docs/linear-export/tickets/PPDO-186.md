---
id: PPDO-186
title: 'Perf: price index single-item reads and saves stop loading all 6,400 rows'
status: Done
project: PPDO Portal
labels: 
milestone: v1.8.1 — Optimization & Dashboard Fixes
created: 2026-10-04
completed: 2026-10-04
parent: (none)
blocked_by: (none)
related: (none)
url: https://linear.app/ralphoksiprojects/issue/PPDO-186
uuid: abc4759d-7b8a-4531-8b7b-cb664f41fb7d
---

# PPDO-186 — Perf: price index single-item reads and saves stop loading all 6,400 rows

From the performance audit, 2026-10-04 (finding **O6**). Optimization ticket 5 of 8. Backend, size S.

## Current behaviour

`PriceIndexService` calls `GetAllAsync` (all \~6,400 rows, change-tracked) for one item:

* `GetByIdAsync` (line 92)
* `CreateAsync` duplicate check (line 109)
* `UpdateAsync` find + duplicate check (line 146)
* `DeleteAsync` (line 184)

## Target behaviour

* **By id:** a repository method, `WHERE id = @id`.
* **Duplicates:** an `AnyAsync` on name + unit (excluding the item's own id on update), matching `SameKey`'s comparison.
* **Unchanged:** CSV export and import load the whole list; that is their job.

## Acceptance

- [ ] `PriceIndexServiceTests` pass, with new tests for the duplicate check on create and update.
- [ ] SQL profiler: editing one item runs single-row queries, not a full scan.
- [ ] A duplicate name + unit still returns 409.

## Manual-coding note

**Good hand-coding candidate.** The sibling to copy is `ItemMasterRepository.GetByStockNoAsync`. Feedback comes from `dotnet test`, and the change is small and reversible.
