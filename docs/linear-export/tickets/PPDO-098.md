---
id: PPDO-98
title: 'Program and project subtotals on the AIP form — grid, Annex B Excel and JSON'
status: Done
project: PPDO Portal
labels: 
milestone: v1.8.0 — Office Users, AIP Redesign & Reviewer Flow
created: 2026-09-16
completed: 2026-09-16
parent: (none)
blocked_by: (none)
related: PPDO-102
url: https://linear.app/ralphoksiprojects/issue/PPDO-98
uuid: 4d3a31f1-31c0-459b-9804-6ffb02e23b49
---

# PPDO-98 — Program and project subtotals on the AIP form — grid, Annex B Excel and JSON

**In one line:** the program and project heading rows carry the sum of everything beneath them, the way WFP's report totals its groups.

**Source:** 2026-09-15 PDC/finance demo notes · **Size:** M · **Demo:** required on or before 2026-09-23

## Scope

`AipFormRowBuilder.Build` already emits `Office` / `Program` / `Project` / `Activity` rows in print order, and only the office row and the sheet TOTAL carry figures — `Heading(ProgramRow, …)` and `Heading(ProjectRow, …)` are amount-less. Accumulate each project's activities and each program's projects and attach those sums.

Because one builder feeds both the consolidated grid and the Annex B Excel export (deliberately — see the class comment), this lands in both from one change. Confirm the Excel writer renders a heading row's figures rather than skipping the amount columns for non-activity rows.

**Every money column**, not just PS/MOOE/CO: the printed-amounts DTO in full, so a program row totals the same set of columns an activity row prints.

## Open — decide before building

* The **synthetic project** (`IsSynthetic`, [RAL-108](https://linear.app/ralphoksiprojects/issue/RAL-108/aip-import-capture-line-item-detail-at-programproject-level-currently)) prints no row but its activities do. Its amounts must still reach the program subtotal, or a program will total less than the rows under it.
* **External API:** `/external/v1` schema 1.0.0 is a published contract. Check whether it serves these rows; if it does, added figures on program/project rows are a schema change and need a version note in `External_AIP_API_Spec.md`.

## Acceptance

- [ ] A program row's PS/MOOE/CO and total equal the sum of its projects' activities, including any synthetic project's
- [ ] A project row's figures equal the sum of its activities
- [ ] The exported Annex B shows the same figures on the same rows as the grid
- [ ] The office row and sheet TOTAL are unchanged from today
