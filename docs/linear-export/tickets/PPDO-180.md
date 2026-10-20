---
id: PPDO-180
title: 'Dashboard: Investment proposals band, plus the all-offices card for cross-office reviewers'
status: Done
project: PPDO Portal
labels: 
milestone: v1.8.2 — Dashboard Redesign, Caching & Remaining Optimizations
created: 2026-10-04
completed: 2026-10-05
parent: (none)
blocked_by: (none)
related: PPDO-176
url: https://linear.app/ralphoksiprojects/issue/PPDO-180
uuid: b4e8f5db-24e6-4db0-a09c-5e5dd91db010
---

# PPDO-180 — Dashboard: Investment proposals band, plus the all-offices card for cross-office reviewers

From the dashboard design pass, 2026-10-04 (`docs/v1.8/Dashboard_Design_Pass.md`, finding **F11**, backend field **B3**). Ticket 6 of 7. Backend + frontend, size M. Wireframe boards 1 and 2.

## Current behaviour

* Investment proposals (new in v1.8.0) are not on the dashboard at all, though AIP submit now warns about projects with no Final proposal.

## Target behaviour

* **Backend (B3):** proposal counts for the fiscal year: projects with a **final**, a **draft**, or **no** proposal.
  * Own office: on `/dashboard/office`.
  * Per office: on `/dashboard/offices`, for cross-office reviewers only.
  * **One grouped** `COUNT` **per fiscal year**, never a loop per office or project (`docs/PERFORMANCE_GUIDELINES.md`). FY 2028+ only (`FIRST_ENTERED_FISCAL_YEAR`); older years return nothing and the band hides.
* **Own-office band** (encoder, department head, finance): a bar of final / draft / none, and the few projects that need attention, each with **Create** or **Open**. Sits beside Recent activity.
* **All-offices card** (anyone with `CanReviewAllOffices`, a PPDO finance user with the grant included):
  * Bar of final / draft / none across every office.
  * "Offices in PPDO review, fewest final first", each with **Open**.
  * Labelled read and export only, which is how reviewers can use proposals today.
* **No new requests;** the page stays under 20 KB of JSON. A skeleton of the loaded size.

## Acceptance (local)

- [ ] `opa.user` FY 2028: the bar matches the Investment Proposals list for that office; Create/Open go to the right project.
- [ ] `admin`: the all-offices card's totals equal the sum of the offices; the list shows only offices in PPDO review.
- [ ] `ppdo.finance` with no review grant: own-office band only. Given `CanReviewAllOffices` for the test: the all-offices card appears. Remove the grant afterwards.
- [ ] An office user never sees another office's counts (scope test on the endpoint).
- [ ] FY 2027: no band.
- [ ] SQL profiler: one query for the counts, not one per office.

## Out of scope

Editing proposals from the dashboard; reviewer write access to proposals.
