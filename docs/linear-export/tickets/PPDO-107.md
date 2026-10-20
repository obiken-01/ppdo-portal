---
id: PPDO-107
title: 'New grant CanManageOfficeSetup: a department head sets up their own office'
status: Done
project: PPDO Portal
labels: 
milestone: v1.8.0 — Office Users, AIP Redesign & Reviewer Flow
created: 2026-09-20
completed: 2026-09-20
parent: (none)
blocked_by: (none)
related: PPDO-135
url: https://linear.app/ralphoksiprojects/issue/PPDO-107
uuid: 9655bba5-1031-4286-977f-4ee7aa9f41b6
---

# PPDO-107 — New grant CanManageOfficeSetup: a department head sets up their own office

Spec: `docs/v1.8/Office_Setup_Spec.md` (C2). From the 2026-09-15 PDC/finance demo, items 7 and 8.

## Goal

One new per-user grant lets a department head set up **their own office**: the division split, the programme → division assignment, division config (PPDO-C3) and fund-source config (PPDO-C4).

## Decisions

* **D1:** one flag, `CanManageOfficeSetup`, covers all four surfaces — not four flags.
* **D2:** it is **not** implied by any reviewer flag; granted per user, off by default. SuperAdmin bypasses; Admin is not auto-granted (same shape as `CanManagePpdoAllocation`).
* ⚠️ `CanManagePpdoAllocation` **does not widen.** It stays host-office-only — that is what closed the `pto.user` leak (Permission_Matrix §4a, 2026-09-02). The new grant must not become a back door into it.

## Scope

* `PermissionService.CanManageOfficeSetupAsync` + `Override` column, `/auth/me`, user DTOs, the admin user-form toggle.
* `PUT /budget-planning/allocation/divisions` and `PUT /budget-planning/allocation/programs`: accept **either** `CanManagePpdoAllocation` (host office, any office — unchanged) **or** `CanManageOfficeSetup` where `body.OfficeId == caller.OfficeId`, else 403.
* **D10:** office-setup writes are allowed only while the office's AIP is office-editable (Draft, DepartmentReview, ReturnedByPpdo); otherwise 409 naming the state.
* Allocation page for such a caller: own office only, no office picker.
* `Permission_Matrix.md` — new flag row in §4 and a scope row in §4a. A flag without a row fails the build.
* ⚠️ Confirm `ReviewerWriteGuard` (`ConfigHttp.AuthorizeWriteAsync`) lets an office reviewer through on these two writes; pin it with a test either way.

## Tests

* Department head targeting a **foreign** office → 403, on both writes.
* `UpsertDivisions_AsOfficeUser_TargetingOwnOffice_IsAlsoForbidden` must keep passing for a caller holding only `CanManagePpdoAllocation`.
* `PermissionMatrixTests` row for the new flag.
* A both-flags user, and a grant-less department head (read-only page).

## Out of scope

Division and fund-source config changes (PPDO-C3, PPDO-C4). Any change to reviewing.

Not a hand-coding candidate: permission code, where a missing check is not caught by the compiler.
