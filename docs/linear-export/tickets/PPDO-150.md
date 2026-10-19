---
id: PPDO-150
title: 'Follow-up: attribute the AIP ceiling ledger by activity division, not ProgramDivision'
status: Done
project: PPDO Portal
labels: 
milestone: Demo 2 — change requests & fixes
created: 2026-09-28
completed: 2026-09-28
parent: PPDO-130
blocked_by: PPDO-147
related: PPDO-93, PPDO-195
url: https://linear.app/ralphoksiprojects/issue/PPDO-150
uuid: 12573bcd-478c-43b5-88cf-b23f35a5b6f1
---

# PPDO-150 — Follow-up: attribute the AIP ceiling ledger by activity division, not ProgramDivision

Spec: `docs/v1.8/Division_Submit_Spec.md` decision 7 and §2 Open follow-ups. This was deliberately kept **out** of [PPDO-130](https://linear.app/ralphoksiprojects/issue/PPDO-130/demo-28-division-head-submits-to-department-head-who-can-return-that).

## Current

`AipCeilingService.ResolveDivisionIdAsync` attributes a host-office program's reservation to the **lowest division id** among its `ProgramDivision` assignments. The code comment calls splitting across divisions "a modelling question nobody has answered".

## Target

For FY ≥ 2028, attribute each activity's reservation to `aip_activities.division_id`, the tag [PPDO-130](https://linear.app/ralphoksiprojects/issue/PPDO-130/demo-28-division-head-submits-to-department-head-who-can-return-that) T1 adds.

## Needs deciding first

* What happens to untagged activities: are they unattributed, or blocked?
* Whether guest offices start writing ledger rows. Today they write none.
* Whether `ProgramDivision` is retired for FY2028+.
* It changes the allocation figures, so a before/after comparison on UAT is required.

Needs a short spec addendum before implementation.
