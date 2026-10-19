---
id: PPDO-148
title: 'Demo 2.8 T2 — Activity division tagging + division lock in the write guard'
status: Done
project: PPDO Portal
labels: 
milestone: Demo 2 — change requests & fixes
created: 2026-09-28
completed: 2026-09-28
parent: PPDO-130
blocked_by: PPDO-147
related: PPDO-31, PPDO-134, PPDO-195, PPDO-218, PPDO-224
url: https://linear.app/ralphoksiprojects/issue/PPDO-148
uuid: f34c2d82-29a4-4750-a8c8-bb2d55ad5e1b
---

# PPDO-148 — Demo 2.8 T2 — Activity division tagging + division lock in the write guard

Spec: `docs/v1.8/Division_Submit_Spec.md` decisions 5–6, "Further rules", §3.1, §3.3, §3.5 and §4 (the re-tag endpoint and the `AipActivityDto` fields).

## Scope

* **Tag on create.** An encoder's activity is always tagged with `caller.DivisionId`, and any client value is ignored. The department head, Admin and SuperAdmin must supply a division in an office with divisions. An encoder with no division in such an office is refused with a 400.
* `PUT /api/budget-planning/aip/activities/{activityId}/division` — for the department head and Admin/SuperAdmin only. It refuses a division from another office or an inactive division.
* **Bulk creation** (import, re-upload, LDIP seed, carry-forward) tags from the program's single `ProgramDivision` division, and leaves the activity untagged otherwise.
* **Division lock** in `AipWriteGuard`, used by `AipService` and the 3 `AipExpenditureService` call sites:
  * an encoder can't edit their own division once it is Submitted
  * an encoder can't edit another division's activity
  * the department head can edit locked work
  * the division rule **never** allows a write that the office state refuses
* **Container delete rule:** an encoder can't delete a program or project that holds another division's activity or one of their own submitted activities.
* `AipActivityDto` gains `divisionId`, `divisionName` and `canEdit`.
* Offices without divisions behave exactly as today.
* Add the new rows to `Permission_Matrix.md` and `PermissionMatrixTests`.

## Tests

§11 "AipWriteGuard / AipServiceTests / AipExpenditureServiceTests" and "Tagging" (TDD).

Not a manual-implementation candidate: write guard and permission paths.
