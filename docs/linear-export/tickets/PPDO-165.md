---
id: PPDO-165
title: 'AIP Entry — Division allocations panel doesn''t update after an edit until the page is reloaded'
status: Done
project: PPDO Portal
labels: 
milestone: Demo 2 — change requests & fixes
created: 2026-09-30
completed: 2026-09-30
parent: PPDO-122
blocked_by: (none)
related: PPDO-130
url: https://linear.app/ralphoksiprojects/issue/PPDO-165
uuid: 2bec7f86-db54-476b-ab86-c7defca6628e
---

# PPDO-165 — AIP Entry — Division allocations panel doesn't update after an edit until the page is reloaded

Found by Ralph on 2026-09-30 during the [PPDO-130](https://linear.app/ralphoksiprojects/issue/PPDO-130/demo-28-division-head-submits-to-department-head-who-can-return-that) (Demo 2.8) acceptance run, as `pto.user` (PTO Cash Division).

He added an activity with ₱3,600 of General Fund MOOE. The submit card's "Encoded" updated at once (699 → 703), but the **Division allocations** panel above it still showed Cash "Costed in AIP ₱0.00" until he reloaded the page. After the reload it showed ₱4,000 (the figure rounded up to the thousand), which is correct.

## Cause

`aip/entry/page.tsx` loads the panel's rows (`getDashboard` / `getOfficeDashboard` → `byDivision`) once, in an effect keyed on office and year. `refreshReadiness()` runs after every add, delete, expenditure or details save and refreshes the checklist and the division status, but not these rows.

## Fix

After each edit, mark the rows stale. Refetch them only while the panel is open, or when it is next opened. The panel is collapsed by default, and the dashboard payload is heavier than the readiness call, so refetching on every expenditure keystroke-save would be waste (PERFORMANCE_GUIDELINES). A background refresh must not unmount or collapse the open panel.

## Acceptance

- [ ] With the panel open, saving an expenditure line updates the division's Costed / Remaining without a reload
- [ ] With the panel closed, no dashboard request fires on save; opening it afterwards shows the new figures
- [ ] The panel stays open through a refresh
