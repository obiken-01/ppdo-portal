---
id: PPDO-135
title: 'Demo 2.13 — Let a department head assign divisions to their own office''s users'
status: Done
project: PPDO Portal
labels: 
milestone: Demo 2 — change requests & fixes
created: 2026-09-23
completed: 2026-09-23
parent: PPDO-122
blocked_by: (none)
related: PPDO-18, PPDO-30, PPDO-107, PPDO-108, PPDO-123, PPDO-130, PPDO-134, PPDO-170, PPDO-195
url: https://linear.app/ralphoksiprojects/issue/PPDO-135
uuid: de354379-7801-4f25-8fe5-8fdcc47ce3a7
---

# PPDO-135 — Demo 2.13 — Let a department head assign divisions to their own office's users

Ralph, 2026-09-23, after testing SPO from scratch: *"I think we should let the dept head user assign division to their users. Maybe a new simple screen or page for them?"*

[PPDO-123](https://linear.app/ralphoksiprojects/issue/PPDO-123/demo-21-office-users-cannot-be-assigned-a-division) made the assignment **possible**; today only a PPDO administrator can perform it. A department head already creates their office's divisions ([PPDO-108](https://linear.app/ralphoksiprojects/issue/PPDO-108/division-config-for-department-heads-own-office-only-permission)) and sets its allocations ([PPDO-107](https://linear.app/ralphoksiprojects/issue/PPDO-107/new-grant-canmanageofficesetup-a-department-head-sets-up-their-own)), so needing to ask PPDO to put their own staff into their own divisions is the odd step out.

## ⚠️ A new screen, NOT the `CanManageUsers` flag

Granting the existing flag would be a privilege escalation, not a shortcut. `GET /api/users` has no office axis:

```csharp
if (!await _permissions.CanManageUsersAsync(caller, ct)) return Forbidden;
IReadOnlyList<UserResponseDto> result = await _users.GetAllAsync(ct);   // every user, every office
```

and the write guard checks **role only**:

```csharp
private static bool CanRequesterManageTarget(User requester, User target)
    => CanRequesterManageRole(requester, target.Role);   // no office comparison
```

So a department head holding `CanManageUsers` could list and edit Staff in **every** office province-wide, including PPDO's. Ralph's instinct for a separate screen is the right call for that reason, not just for simplicity.

## Shape

Follow the [PPDO-108](https://linear.app/ralphoksiprojects/issue/PPDO-108/division-config-for-department-heads-own-office-only-permission) **divisions-config precedent**, which solved the same problem: same page family, own office only, reduced controls, and the server stamps the office rather than trusting the body.

Scope it to what the need actually is — **assigning an existing user to one of the office's divisions**. Not creating users, not resetting passwords, not editing roles or permission flags. A narrow screen is far easier to reason about than a filtered clone of User Management, and every capability left out is one that cannot leak.

Suggested gate: `CanManageOfficeSetup`, the grant a department head already holds for divisions, ceilings and allocations. Adding a second grant for one more office-setup action splits a coherent role across two flags.

## Server work

The office axis has to exist on the server, not only in the UI:

* a scoped list — the caller's own office's users only
* an office comparison in the update path, alongside the existing role check
* refuse rather than clamp, as `AllocationFunctions` does: *"silently rewriting which office a peso amount lands on is a worse failure than a 403"*

Add rows to `docs/v1.8/Permission_Matrix.md`; `PermissionMatrixTests` fails the build for a flag with no row.

## Acceptance

- [ ] A department head can set/clear the division of a user **in their own office**
- [ ] They cannot see or touch a user in any other office — verified against the API, not just the screen
- [ ] They cannot change roles, permission flags or passwords
- [ ] They cannot assign a division belonging to another office (already refused by `ValidateDivisionAsync` — pin it)
- [ ] A PPDO administrator's existing User Management is unchanged
- [ ] Permission matrix updated and pinned

⚠️ **Worth a red-test on the office axis specifically.** A cross-office leak is the one failure here that is silent, and this project has already shipped three of them ([RAL-229](https://linear.app/ralphoksiprojects/issue/RAL-229/clamp-get-budget-planningdashboardoffice-to-the-callers-own-office), [PPDO-18](https://linear.app/ralphoksiprojects/issue/PPDO-18/office-clamp-on-allocationfunctions-cross-office-read-leak-with-a-pbo), [PPDO-30](https://linear.app/ralphoksiprojects/issue/PPDO-30/budget-planning-allocation-setup-status-divisionid-is-not-scoped-to)).
