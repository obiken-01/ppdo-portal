---
id: PPDO-169
title: 'Readiness board — an office PPDO returned sits in In Progress, not Office Review'
status: Done
project: PPDO Portal
labels: 
milestone: Demo 2 — change requests & fixes
created: 2026-09-30
completed: 2026-09-30
parent: PPDO-122
blocked_by: (none)
related: PPDO-78, PPDO-130, PPDO-149, PPDO-171
url: https://linear.app/ralphoksiprojects/issue/PPDO-169
uuid: 3429ea42-b919-4d1f-817f-190a5bb01f86
---

# PPDO-169 — Readiness board — an office PPDO returned sits in In Progress, not Office Review

Raised by Ralph on 2026-09-30 during the Demo 2.8 run, step 15 (PPDO returns PTO).

The readiness board put a returned office (`ReturnedByPpdo`) in **Office Review**, with a Returned badge. The reasoning in `AipReadinessColumn` and `AIP_Review_Spec.md` §6.3 was that "returned work is editable by the office again, exactly as in department review". Since [PPDO-149](https://linear.app/ralphoksiprojects/issue/PPDO-149/demo-28-t3-division-submit-return-endpoints-office-state-derivation), though, a PPDO return reopens every division to Draft, so the work is back with the encoders. That is **In Progress**.

## Change

* `AipReadinessColumn.For`: `ReturnedByPpdo` → `InProgress`, whatever the activity count; a returned office has submitted once. The badge (`IsReturned`) is unchanged, and it is still not a sixth column.
* The office moves to Office Review by itself once it re-submits: the office state becomes DepartmentReview.
* The table's Submission pill already read "In progress" for Returned (`PlanningStage.ForSubmission`), so board and table now agree.
* Spec §6.3 column table and the acceptance line updated.

## Acceptance

- [ ] After PPDO returns an office, its card sits in **In Progress** with the orange Returned badge
- [ ] Once it re-submits to the department head, it moves to **Office Review**; after sending to PPDO, to **PPDO Review**
