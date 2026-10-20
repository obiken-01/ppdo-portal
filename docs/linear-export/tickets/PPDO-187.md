---
id: PPDO-187
title: 'Perf: AsNoTracking on read-only repository queries'
status: Done
project: PPDO Portal
labels: 
milestone: v1.8.2 — Dashboard Redesign, Caching & Remaining Optimizations
created: 2026-10-04
completed: 2026-10-05
parent: (none)
blocked_by: (none)
related: (none)
url: https://linear.app/ralphoksiprojects/issue/PPDO-187
uuid: d0ea4062-2640-42e2-8c3b-8a689ba74e67
---

# PPDO-187 — Perf: AsNoTracking on read-only repository queries

From the performance audit, 2026-10-04 (finding **O8**). Optimization ticket 6 of 8. Backend, size S.

## Current behaviour

* **Almost everything is tracked:** only 11 queries in 5 repositories use `AsNoTracking`. Every list and detail read builds change-tracking state it never uses. For example, the AIP hierarchy reads track 2,375 activities on record 13.

## Target behaviour

* `AsNoTracking()` on read-only methods, starting with the AIP hierarchy reads (`GetOfficesByAipIdAsync`, `GetProgramsByOfficeIdsAsync`, `GetProjectsByProgramIdsAsync`, `GetActivitiesByProjectIdsAsync`) **when called from read endpoints**.
* ⚠️ **Never on a read whose entities a service then edits and saves.** An untracked entity's changes are silently dropped on `SaveChanges`. Where one method serves both, add a separate read-only method rather than changing the shared one.
* `Repository.GetAllAsync` stays tracked unless every caller is checked (several services find-then-update through it).

## Acceptance

- [ ] Every changed method is listed on the PR, with "no caller saves these entities" checked for each.
- [ ] All tests pass. For each read endpoint touched, the matching edit flow is exercised by hand: AIP inline edit, expenditure save, re-tag.
- [ ] AIP record 13 detail time measured before and after (local).

## Manual-coding note

**Not a candidate:** a wrong call silently loses saves, and the compiler does not catch it.
