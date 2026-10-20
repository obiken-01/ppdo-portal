---
id: PPDO-178
title: 'Dashboard: one status band replaces the action card and the pipeline rail (incl. phone)'
status: Done
project: PPDO Portal
labels: 
milestone: v1.8.2 — Dashboard Redesign, Caching & Remaining Optimizations
created: 2026-10-04
completed: 2026-10-05
parent: (none)
blocked_by: PPDO-175, PPDO-176
related: (none)
url: https://linear.app/ralphoksiprojects/issue/PPDO-178
uuid: 5fd58168-60d0-45ad-a1da-6a0407284524
---

# PPDO-178 — Dashboard: one status band replaces the action card and the pipeline rail (incl. phone)

From the dashboard design pass, 2026-10-04 (`docs/v1.8/Dashboard_Design_Pass.md` §4, finding **F12**). Ticket 4 of 7. Frontend, size M. Wireframe boards 1, 4 and 5.

Blocked by [PPDO-175](https://linear.app/ralphoksiprojects/issue/PPDO-175/dashboard-the-submission-stage-and-the-department-heads-submit-still) (it adds the office workflow state, B1) and [PPDO-176](https://linear.app/ralphoksiprojects/issue/PPDO-176/dashboard-role-aware-top-for-the-ppdo-reviewer-action-card-and-band) (role-aware band order).

## Current behaviour

* An action card plus 3–5 pipeline stage cards fill the top of the page.
* On a phone, they fill the whole first screen; the money is below the fold.

## Target behaviour

* **One status band:**
  * A plain sentence: where the AIP is and who has it ("Your encoders are done. The AIP is ready for you to send to PPDO.").
  * One line saying why, then **one** next-step button, or none when there is nothing to do.
  * A thin step track underneath: Ceiling · Encoding · Department review · PPDO review · Accepted. The host office adds Division allocation and Program assignment.
* **Every state and reader has its own sentence:** board 5 lists them (encoder, department head, reviewer, finance × Draft / DepartmentReview / SubmittedToPpdo / Returned / Consolidated).
* **Holder named by role, not person** (open question 1 default).
* **Header:** the fiscal year moves into the page header.
* **Phone:** the step track becomes a short list; status, button and progress fit the first screen (board 4).
* **Skeleton** of the band's loaded size.
* **No new requests:** reads B1 from `/dashboard/office` and the waiting count the shell already loads.

## Acceptance (local)

- [ ] Each role × state on board 5 shows its sentence and button (walk the AIP through submit → return → re-submit → accept).
- [ ] 375 px: status, button and step list on the first screen; no horizontal scroll.
- [ ] No new request; CLS under 0.1.

## Out of scope

Header/field cleanups (ticket 5), proposals band (ticket 6), activity wording (ticket 7).
