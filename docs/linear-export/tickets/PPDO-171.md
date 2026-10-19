---
id: PPDO-171
title: 'A PPDO-returned office without divisions sits with its department head — board column and encoder pill'
status: Done
project: PPDO Portal
labels: 
milestone: Demo 2 — change requests & fixes
created: 2026-09-30
completed: 2026-09-30
parent: PPDO-122
blocked_by: (none)
related: PPDO-130, PPDO-149, PPDO-169
url: https://linear.app/ralphoksiprojects/issue/PPDO-171
uuid: 148e957d-8ad3-4586-be6a-3312ff6ddea2
---

# PPDO-171 — A PPDO-returned office without divisions sits with its department head — board column and encoder pill

Found by Ralph on 2026-09-30 in the Demo 2.8 run, Part 6 (OPA, an office without divisions).

## What [PPDO-169](https://linear.app/ralphoksiprojects/issue/PPDO-169/readiness-board-an-office-ppdo-returned-sits-in-in-progress-not-office) got wrong for these offices

[PPDO-169](https://linear.app/ralphoksiprojects/issue/PPDO-169/readiness-board-an-office-ppdo-returned-sits-in-in-progress-not-office) moved **every** PPDO-returned office to **In Progress**, reasoning that the work is back with its encoders. That holds for an office **with divisions**, where a PPDO return reopens every division ([PPDO-149](https://linear.app/ralphoksiprojects/issue/PPDO-149/demo-28-t3-division-submit-return-endpoints-office-state-derivation)). It does not hold for an office **without** divisions: the return lands with its **department head**, who re-sends it to PPDO. `AipSubmitService` allows SubmitToPpdo from ReturnedByPpdo, and ReturnToEncoder only from DepartmentReview. The encoder has no submit to make.

## Encoder pill contradiction

As `opa.user`, AIP Entry showed the pill **"With you — returned by PPDO"** directly above **"Your department head has this office's AIP… only they can send it on to PPDO."** `describeAipHolder` hard-coded "you" for ReturnedByPpdo.

## Decision (Ralph)

The board column follows who holds it:

* **with divisions → In Progress**
* **without divisions (or before the entered years) → Office Review**

Both keep the Returned badge.

## Change

* `AipReadinessColumn.For(status, activities, inDivisionFlow)`. The dashboard passes whether the office has a division-progress entry (an active division in an entered year), the same rule the PPDO return uses to reopen divisions.
* `describeAipHolder(status, inDivisionFlow)`: ReturnedByPpdo reads "you — returned by PPDO" with divisions, and "your department head — returned by PPDO" without. AIP Entry passes `divisions.hasDivisions`.
* Spec §6.3 updated.

## Acceptance

- [ ] OPA (no divisions) returned by PPDO: board card in **Office Review** with the Returned badge; `opa.user`'s pill reads "With your department head — returned by PPDO"
- [ ] PTO (divisions) returned by PPDO: board card in **In Progress** with the badge; `pto.user`'s pill reads "With you — returned by PPDO"
