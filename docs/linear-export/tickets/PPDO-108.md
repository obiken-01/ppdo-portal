---
id: PPDO-108
title: 'Division config for department heads: own office only, permission switches hidden and server-dropped'
status: Done
project: PPDO Portal
labels: 
milestone: v1.8.0 — Office Users, AIP Redesign & Reviewer Flow
created: 2026-09-20
completed: 2026-09-20
parent: (none)
blocked_by: PPDO-107
related: PPDO-123, PPDO-126, PPDO-135
url: https://linear.app/ralphoksiprojects/issue/PPDO-108
uuid: 53d7278f-8be3-491d-96f6-d7be3afc39d3
---

# PPDO-108 — Division config for department heads: own office only, permission switches hidden and server-dropped

Spec: `docs/v1.8/Office_Setup_Spec.md` (C3). From the 2026-09-15 demo item 7 and Ralph's 2026-09-16 decision.

**Blocked by** [PPDO-107](https://linear.app/ralphoksiprojects/issue/PPDO-107/new-grant-canmanageofficesetup-a-department-head-sets-up-their-own) (the `CanManageOfficeSetup` grant).

## Goal

A department head manages **their own office's** divisions from Configuration → Divisions, without ever seeing or setting the seven feature switches. Ralph, 2026-09-16: "we own the site, we can't let them set permissions of our features."

## Scope

* `GET|POST|PUT|DELETE /api/config/divisions[/{id}]` accept `CanManageConfig` **or** `CanManageOfficeSetup`.
* With `CanManageOfficeSetup` only: reads filtered to `caller.OfficeId`; writes must target it, else 403.
* **D8:** the seven `Can*` fields are **dropped** from such a caller's body — not a 400, so an older client cannot break. Hiding them in the UI is not enforcement; both halves are required.
* **D9:** a division created by a department head has all seven switches off. An admin turns them on.
* Delete stays the existing soft delete (`isActive = false`), and is blocked when users or allocations still reference the division, naming what uses it.
* UI: no office column or picker for such a caller, plus a scope note "You are managing <Office> only." Admin view is unchanged.

## Tests

* A department head's switch values are dropped, and the response carries the stored values.
* A write targeting a foreign office → 403.
* Admin behaviour unchanged (every office, switches visible).

## Out of scope

User management, office config, and every other config page.
