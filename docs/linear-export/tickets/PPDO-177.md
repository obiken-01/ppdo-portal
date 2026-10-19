---
id: PPDO-177
title: 'Dashboard: load the bands in parallel (default the fiscal year server-side)'
status: Done
project: PPDO Portal
labels: 
milestone: v1.8.1 — Optimization & Dashboard Fixes
created: 2026-10-04
completed: 2026-10-04
parent: (none)
blocked_by: (none)
related: PPDO-136, PPDO-175, PPDO-176
url: https://linear.app/ralphoksiprojects/issue/PPDO-177
uuid: b98f21a8-40cf-48b5-aa5b-e89d1dc72b7d
---

# PPDO-177 — Dashboard: load the bands in parallel (default the fiscal year server-side)

From the dashboard design pass, 2026-10-04 (`docs/v1.8/Dashboard_Design_Pass.md` §3 and **B5**). Ticket 3 of 7. Backend + frontend, size S.

## Current behaviour

* **Requests run three deep:** `/auth/me` → `/budget-planning/dashboard` → `/dashboard/office` + `/dashboard/offices`.
* **Why they wait:** the last two wait for `/dashboard` only to learn the fiscal year and the office id.
* **Cost:** at production's \~0.4 s per request, the lower half of the page appears \~0.4–0.6 s later than it needs to. The endpoints themselves are fast (0.03–0.22 s, ≤7 KB).

## Target behaviour

* **Backend (B5):** `/dashboard/office` and `/dashboard/offices` default to the configured fiscal year when no `fiscalYear` is sent (the same default [PPDO-136](https://linear.app/ralphoksiprojects/issue/PPDO-136/demo-214-admin-configurable-default-fiscal-year-for-investment) set up). An explicit `fiscalYear` still wins.
* **Frontend:** after `/auth/me`, fire `/dashboard`, `/dashboard/office`, `/dashboard/offices` and `/activity` together. The office id comes from the current user context.
* **FY switch:** picking another year still refetches with the explicit year.
* **No new requests**, and payloads unchanged.

## Acceptance (local)

- [ ] Network tab: after `/auth/me`, the four calls start together (one waterfall step, not two).
- [ ] `admin`, `ppdo.finance`, `opa.user`, `opa.head.user`: every band shows the same figures as before.
- [ ] Switching the FY still loads that year's figures in every band.
- [ ] Office endpoint with no `fiscalYear` returns the configured year; with one, returns that year (tests).
- [ ] CLS still under 0.1.

## Out of scope

New fields on the responses (tickets 4, 6, 7).
