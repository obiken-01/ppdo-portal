---
id: PPDO-149
title: 'Demo 2.8 T3 — Division submit / return endpoints + office-state derivation'
status: Done
project: PPDO Portal
labels: 
milestone: Demo 2 — change requests & fixes
created: 2026-09-28
completed: 2026-09-28
parent: PPDO-130
blocked_by: PPDO-148
related: PPDO-46, PPDO-167, PPDO-169, PPDO-171
url: https://linear.app/ralphoksiprojects/issue/PPDO-149
uuid: efa26490-5746-44f9-bf80-fc27d014da8e
---

# PPDO-149 — Demo 2.8 T3 — Division submit / return endpoints + office-state derivation

Spec: `docs/v1.8/Division_Submit_Spec.md` decisions 3, 9–11 and 13, §3.2, §3.4 and §4.

## Scope

**New endpoints:**

* `GET …/aip/{aipId}/offices/{officeId}/divisions`
* `POST …/aip/{aipId}/divisions/{divisionId}/submit`
* `POST …/aip/{aipId}/divisions/{divisionId}/return`

**Office state:**

* The office stays `Draft` until the last division with activities submits, then moves automatically to `DepartmentReview`.
* A per-division return moves the office back to Draft.

**Division submit:**

* Refused when any activity in the office is untagged.
* Completeness is checked on the division's own activities.
* The office ceiling is checked too. Being over it succeeds with `ceilingWarning` set (decision 13).

**Changed existing endpoints:**

* The office-level `submit` returns 400 in an office with divisions.
* `return-to-encoder` returns every Submitted division.
* `submit-to-ppdo` requires every division with activities to be Submitted, and names the ones still waiting.
* A PPDO return resets every division to Draft, and the office stays `ReturnedByPpdo` until the last division resubmits.

Audit rows and structured `LogInformation` for each transition. Office-ownership failures return 404 ([PPDO-46](https://linear.app/ralphoksiprojects/issue/PPDO-46/aip-write-paths-have-no-office-ownership-check-cross-office-write-gap)).

## Tests

§11 `AipDivisionSubmitServiceTests` (new) and `AipSubmitServiceTests` (TDD).

Not a manual-implementation candidate: workflow state and permission paths.
