---
id: PPDO-198
title: 'Security: WFP endpoints that take an ID don''t check the caller''s office or division'
status: Done
project: PPDO Portal
labels: 
milestone: v1.8.3 — Concurrent-edit guard: turn it on
created: 2026-10-08
completed: 2026-10-13
parent: (none)
blocked_by: (none)
related: PPDO-20, PPDO-195
url: https://linear.app/ralphoksiprojects/issue/PPDO-198
uuid: a349ecc9-2ed4-46b3-9833-679ee2455fd0
---

# PPDO-198 — Security: WFP endpoints that take an ID don't check the caller's office or division

⚠️ **Security finding. Keep it off public GitHub** (the repo is public): no issue there, and a neutral PR title.

## Finding (security review, 2026-10-08)

Every WFP endpoint that takes a record ID checks only `CanAccessBudgetPlanningAsync`. That flag **defaults to ON for guest-office Staff** (Permission Matrix §2.2). The handlers discard the caller (`User? _`) or never pass it on, so the service can't scope by office or division.

| Endpoint | File | Effect |
| -- | -- | -- |
| `GET budget-planning/wfp/{id}` | `WfpFunctions.cs:85` (`caller` unused) | read any WFP record |
| `POST budget-planning/wfp/{id}/finalize` | `WfpFunctions.cs:140` | finalize any office's or division's WFP |
| `POST budget-planning/wfp/{id}/unlock` | `WfpFunctions.cs:152` | unlock any WFP |
| `GET budget-planning/wfp/expenditures/{id}` | `WfpExpenditureFunctions.cs:55` (`User? _`) | read any expenditure |
| `GET budget-planning/wfp/expenditures?wfpActivityId=` | `WfpExpenditureFunctions.cs:36` | list any activity's expenditures |
| `POST budget-planning/wfp/expenditures` | `WfpExpenditureFunctions.cs:68` | **create or edit** expenditures on any WFP activity |
| `DELETE budget-planning/wfp/expenditures/{id}` | `WfpExpenditureFunctions.cs:90` | **delete** any expenditure |

WFP is PPDO-only ([PPDO-20](https://linear.app/ralphoksiprojects/issue/PPDO-20/budget-planning-dashboard-one-page-that-resolves-per-person)) and division-scoped inside PPDO (the list endpoint, `WfpList`, already clamps the division via `ResolveDivisionFilterAsync`). So:

* **Guest-office Staff** (e.g. a PTO encoder) can read, edit, delete, finalize and unlock **PPDO's** WFP.
* **PPDO division Staff** can do the same to **other divisions'** WFP.

## Proof (local, read-only)

* As `pto.user` (PTO, a guest office), `GET /api/budget-planning/wfp/expenditures/1` returned **200** with PPDO's Salaries and Wages line (office 7, division 1).
* `GET /api/budget-planning/wfp/4` also returned **200**.
* The write paths were **not** executed. They are confirmed from the code: same gate, caller discarded.

## Fix direction

Mirror the list endpoint's rule on every by-ID path:

1. Pass the caller into the service.
2. Load the parent WFP record and require that the caller may access it:
   * **host office only** (guest offices: 404, not 403, so IDs can't be enumerated);
   * for Staff without `CanManagePpdoAllocation` or Admin, **their own division only**.
3. Expenditures resolve their WFP via `wfp_activity_id → wfp_id`. Check it once in the service and reuse it for get, list, save and delete.

Add matrix tests per Permission_Matrix.md (guest Staff, other-division Staff, own-division Staff, finance, Admin), and red-test each guard.

## Severity

**High.** Any guest-office account can change PPDO's financial plan. Production is on v1.7.4, where WFP has the same shape, so **check whether production is affected too** before deciding whether this waits for v1.8.3.
