---
id: PPDO-123
title: 'Demo 2.1 — Office users cannot be assigned a division'
status: Done
project: PPDO Portal
labels: 
milestone: Demo 2 — change requests & fixes
created: 2026-09-23
completed: 2026-09-27
parent: PPDO-122
blocked_by: (none)
related: PPDO-108, PPDO-126, PPDO-134, PPDO-135
url: https://linear.app/ralphoksiprojects/issue/PPDO-123
uuid: 28fa128d-810e-4856-b89c-181c869f145f
---

# PPDO-123 — Demo 2.1 — Office users cannot be assigned a division

Reported twice: *"Can't assign division to non-PPDO users — department heads should also be able to assign division to their office users"* and *"Department head can't assign division to the division head users (encoder users)"*.

## Diagnosis — frontend only

The backend has always allowed it. `UserService.CreateAsync`: *"Office users (non-PPDO Staff with officeId) → division is optional; office_id scopes them"*, and `ValidateDivisionAsync` already refuses a division belonging to another office. `divisionOptions` in the form was already filtering by the selected office. The picker was built for this and then switched off:

* `isPpdoDivisionUser = form.role === "Staff" && !isOfficeUser` drove `disabled`
* `openEdit` nulled `divisionId` for every guest-office user on load

Department heads have been able to **create** divisions for their own office since [PPDO-108](https://linear.app/ralphoksiprojects/issue/PPDO-108/division-config-for-department-heads-own-office-only-permission) — they just couldn't put anyone in one.

## Fix

PR [obiken-01/ppdo-portal#371](https://linear.app/ralphoksiprojects/review/fixusers-let-office-users-be-assigned-a-division-demo-2-112-4d43fd956f55), on `release/1.8.0`. Splits the one flag that was answering two questions: `canHaveDivision` (role is Staff) drives `disabled`; `isDivisionRequired` (Staff **and** host office) drives the ` *` marker. `openEdit` now clears only on a positive office mismatch — the divisions fetch falls back to `[]` on failure, so treating "not in the list" as "wrong office" would have stripped the division off every user opened for edit during a config outage.

The empty picker now points at Config → Divisions, since 15 of 19 offices have no divisions yet and a blank dropdown reads as broken.

## Acceptance

- [ ] A guest-office Staff user can be given one of **their own office's** divisions
- [ ] Division stays optional for them — no ` *`, saving without one works
- [ ] It survives a close/reopen of Edit User
- [ ] SuperAdmin / Admin still cannot have one
- [ ] Host-office Staff still **require** one
- [ ] An office with no divisions explains itself instead of showing an empty list

⚠️ **Not visually confirmed** — no guest office yet has both divisions and users to exercise it. Verify alongside Demo 2.4.
