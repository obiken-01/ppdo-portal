---
id: PPDO-87
title: 'Office ceiling authority moves to PPDO finance — exempt ceiling save from ReviewerWriteGuard + rename CanManagePboCeiling'
status: Done
project: PPDO Portal
labels: 
milestone: v1.8.0 — Office Users, AIP Redesign & Reviewer Flow
created: 2026-09-15
completed: 2026-09-15
parent: (none)
blocked_by: (none)
related: PPDO-9, PPDO-96, PPDO-106, PPDO-196
url: https://linear.app/ralphoksiprojects/issue/PPDO-87
uuid: 1fd6a267-e0f9-4ca0-9019-bf68f6de4a84
---

# PPDO-87 — Office ceiling authority moves to PPDO finance — exempt ceiling save from ReviewerWriteGuard + rename CanManagePboCeiling

**In one line:** PPDO finance users hold both `CanReviewAllOffices` and the ceiling grant; saving a ceiling must work for them, and the grant is renamed now that PBO no longer owns it.

**Source:** PDC + finance demo meeting, 2026-09-15. Originally a PBO user set every office's ceiling; that authority moves to designated PPDO finance users (Fiscal Planning and Investment Planning division), who are also the PPDO cross-office reviewer.

## Problem found

`PUT /api/budget-planning/allocation/ceiling` authorizes via `ConfigHttp.AuthorizeWriteAsync`, which runs `ReviewerWriteGuard`. The guard denies every non-SuperAdmin `CanReviewAllOffices` holder. A user holding **both** flags therefore gets **403 on ceiling save**.

## Decided (Ralph, 2026-09-15)

1. **Exempt the ceiling save from** `ReviewerWriteGuard`**.** The guard protects an office's AIP *content* from the comment-only PPDO reviewer; a ceiling is top-down PPDO authority, not office content. Every other budget-planning write keeps the guard.
2. **Rename** `CanManagePboCeiling` **→** `CanManageOfficeCeilings` everywhere (IPermissionService, PermissionService, User entity, DTOs, `/auth/me`, frontend types/usages, tests, Permission_Matrix.md). **No migration:** the legacy column `Users.OverrideCanManagePboCeiling` is kept via `HasColumnName` — same precedent as `CanManagePpdoAllocation` → `OverrideCanManageAllocation` ([PPDO-9](https://linear.app/ralphoksiprojects/issue/PPDO-9/rename-canmanageallocation-manage-ppdo-allocation)). Historical ticket text keeps the old name.
3. Finance users who also encode for their own division use a **separate encoder account** (recommended; Ralph to confirm with finance) — no narrowing of the guard.

## Acceptance

- [ ] A Staff user holding both `CanReviewAllOffices` and `CanManageOfficeCeilings` can save an office ceiling on the Allocation page (200, value persists on reload)
- [ ] The same user still gets 403 on any other budget-planning write (e.g. adding an AIP activity for another office)
- [ ] A user with only `CanReviewAllOffices` still gets 403 on the ceiling save
- [ ] `/auth/me` returns `canManageOfficeCeilings`; the admin Users page toggle still grants it and the Allocation page still honours it
- [ ] `dotnet ef migrations add` produces an empty diff (no schema change)

## Tests

* `ReviewerWriteGuardCoverageTests` — the ceiling PUT is the documented exemption; every other write endpoint stays covered
* `AllocationFunctionsTests` — both-flags user 200 on ceiling save; reviewer-only 403
* `PermissionMatrixTests` / `PermissionServiceTests` / `OfficeScopeTests` — renamed, behaviour unchanged

Not a manual-coding candidate — permission code (a missing check is not a compile error).
