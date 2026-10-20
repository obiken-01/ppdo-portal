---
id: PPDO-184
title: 'Perf: AIP Entry — load in parallel and fetch only the user''s own office'
status: Done
project: PPDO Portal
labels: 
milestone: v1.8.1 — Optimization & Dashboard Fixes
created: 2026-10-04
completed: 2026-10-04
parent: (none)
blocked_by: (none)
related: (none)
url: https://linear.app/ralphoksiprojects/issue/PPDO-184
uuid: a7e336c7-b2f9-4f15-bee8-952bdc27ce5d
---

# PPDO-184 — Perf: AIP Entry — load in parallel and fetch only the user's own office

From the performance audit, 2026-10-04 (findings **O3 + O4**). Optimization ticket 3 of 8. Backend + frontend, size M.

## Current behaviour

* **A five-step chain:** `aip?fiscalYear` → `aip/{id}` → `aip/{id}/readiness` → `…/offices/{o}/divisions` → `…/comments`. Each waits for the one before (`aip/entry/page.tsx:338-361`). At production's \~0.4 s per request, that is about 2 s before the page has its data.
* **The whole province for PPDO users:** `GET /aip/{id}` gives a host-office caller every office's hierarchy, but Entry edits one office. Today's FY 2028 record is small. Record 13 (FY 2027, 34 offices, 2,375 activities) shows where it goes: 1.5 MB.

## Target behaviour

* **Backend:** an optional `officeId` on `GET /budget-planning/aip/{id}`.
  * It **narrows** the existing `AipReadScope` result to that office. It never widens it: an office user passing another office's id gets nothing extra.
  * Without it, behaviour is unchanged.
* **Frontend:** once the record id is known, fire detail (with `officeId`), readiness, divisions and comments together.

## Acceptance

- [ ] Network tab on AIP Entry: after `aip?fiscalYear`, the four calls start together.
- [ ] `jose.santos` / `ppdo.user` on Entry: the detail response holds PPDO only.
- [ ] `opa.user` passing `officeId` of another office: no other office's rows (test).
- [ ] The detail page (`/aip/detail`) still shows every office for a host user.
- [ ] Submit checklist, division strip and comments behave as before.

## Manual-coding note

**Not a candidate:** it touches AIP read scope, where a mistake is a data leak the compiler does not catch.
