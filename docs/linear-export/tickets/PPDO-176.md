---
id: PPDO-176
title: 'Dashboard: role-aware top for the PPDO reviewer (action card and band order)'
status: Done
project: PPDO Portal
labels: 
milestone: v1.8.1 — Optimization & Dashboard Fixes
created: 2026-10-04
completed: 2026-10-04
parent: (none)
blocked_by: (none)
related: PPDO-75, PPDO-175, PPDO-177, PPDO-180
url: https://linear.app/ralphoksiprojects/issue/PPDO-176
uuid: 7e16e1bd-a418-4237-be7d-1c4182ce4640
---

# PPDO-176 — Dashboard: role-aware top for the PPDO reviewer (action card and band order)

From the dashboard design pass, 2026-10-04 (`docs/v1.8/Dashboard_Design_Pass.md`, findings **F3 + F4**). Ticket 2 of 7. Frontend only. Branch off and PR into `release/1.8.1` (the working branch since 2026-10-04; `release/1.8.0` is frozen). Required before the production deploy. Related to [PPDO-175](https://linear.app/ralphoksiprojects/issue/PPDO-175/dashboard-the-submission-stage-and-the-department-heads-submit-still).

## Current behaviour

* **Wrong next step:** a cross-office reviewer (`CanReviewAllOffices`, or SuperAdmin) gets the action card "Keep costing your AIP activities". That is PPDO's own encoding, not their job.
* **Main work buried:** their real work, the **Offices** board, is the third band on the third screen. It sits below PPDO's money tiles, fund bar and division table.

## Target behaviour

* **Action card for a reviewer:**
  * "N offices are waiting for PPDO review", with **Open the review queue** (AIP Review search, `?mine=true` or the waiting filter [PPDO-75](https://linear.app/ralphoksiprojects/issue/PPDO-75/v18-58-in-app-notifications-pending-count-review-queue) uses).
  * Nothing waiting: "No office is waiting for review", with a quiet link to the board.
* **Count source:** the count comes from the **AIP review notifications** response the shell already loads for the sidebar badge (`lib/aip-notifications.ts`). **No new request.**
* **Band order for a reviewer:** status / action → **Offices** → their own office (PPDO) → recent activity.
  * PPDO's tiles, fund bar and division table stay, but below the Offices band, for a reviewer who is not also the allocation manager.
  * **Cross-office review is a grant, not a role** (Ralph, 2026-10-04). Anyone holding `CanReviewAllOffices` gets this top, a PPDO finance user given the grant included. Holding both grants (SuperAdmin, `admin`, or finance with the review grant): the reviewer order wins, and the finance bands follow below. Finance-only callers keep today's order.
* **Unchanged:** the Board/Table switch and its remembered choice.

## Acceptance (local)

- [ ] `admin`: the card says how many offices wait and links to the review queue; Offices is the first band below it.
- [ ] `ppdo.finance` (no review grant): unchanged.
- [ ] `ppdo.finance` given `CanReviewAllOffices` for the test: the reviewer top first, then the allocation, fund and division bands. Remove the grant afterwards.
- [ ] `opa.user` / `opa.head.user`: unchanged.
- [ ] No new request in the network tab; CLS still under 0.1 (the skeleton order matches the new band order).

## Out of scope

The full status band (ticket 4), the compact Not started column (ticket 5), and the all-offices **Investment proposals** card for reviewers (ticket 6, which needs proposal counts from the backend; see wireframe board 2).

## Manual-coding note

This is a reasonable hand-coding candidate: frontend only, one page, the gates already exist in `page.tsx`, and it is easy to check in the browser per role.
