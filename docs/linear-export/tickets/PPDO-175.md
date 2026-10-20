---
id: PPDO-175
title: 'Dashboard: the submission stage and the department head''s Submit still say "opens in a later release"'
status: Done
project: PPDO Portal
labels: 
milestone: v1.8.1 — Optimization & Dashboard Fixes
created: 2026-10-04
completed: 2026-10-04
parent: (none)
blocked_by: (none)
related: PPDO-176, PPDO-177
url: https://linear.app/ralphoksiprojects/issue/PPDO-175
uuid: 28b0269d-1743-46c4-aa9f-fe1e52b0d1d7
---

# PPDO-175 — Dashboard: the submission stage and the department head's Submit still say "opens in a later release"

From the dashboard design pass, 2026-10-04 (`docs/v1.8/Dashboard_Design_Pass.md`, findings **F1 + F2**, backend field **B1**). Ticket 1 of 7. Branch off and PR into `release/1.8.1` (the working branch since 2026-10-04; `release/1.8.0` is frozen). Required before the production deploy.

## Current behaviour (wrong)

* **Everyone:** on the Investment Planning dashboard (`/budget-planning`), the pipeline rail's last stage reads **"AIP submission · Todo · Opens in a later release"**. It is a constant in `budget-planning/page.tsx` (`submissionStage`, comment "Constant until Phase 4").
* **Department head:** the action card shows a disabled **Submit AIP** button, "Submission opens in a later release".
* Submission has worked in AIP Entry since Phase 4. So the dashboard tells the one person who submits that they can't, and it contradicts the Offices board, which shows PPDO as *Returned*.

## Target behaviour

* **The stage reflects the office's real workflow state** (`AipWorkflowStatus`): Draft → DepartmentReview → SubmittedToPpdo → Returned → Consolidated. It names who has it, using the same rollup the board uses: `AipReadinessColumn.OfficeStatus`, the least-advanced group wins.
* **The department head's card:**
  * In `DepartmentReview`, an enabled link "Review and send to PPDO" opens AIP Entry for the year. The dashboard never submits by itself; the checklist and its gates stay in AIP Entry.
  * In other states, it says where the AIP is (for example "With PPDO since Oct 3", "PPDO returned it with comments").
* **The encoder's card:** after submitting, it says the AIP is with their department head, instead of "keep costing".
* **FY2027 and earlier:** no submission stage, or "Review and submission start with FY 2028".

## Steps

1. **Backend:**
   * Add the office's workflow state and the date it entered that state to `OfficeAipSummaryDto` (`GET /budget-planning/dashboard/office`).
   * If it is cheap, also add the divisions submitted n of m (already computed for `OfficeSummary`).
   * Reuse `AipReadinessColumn.OfficeStatus`; do not write a second rollup.
   * No new endpoint and no extra query per office.
2. **Frontend:** derive `submissionStage` and the action card from that field. Remove the "opens in a later release" text everywhere on the page.
3. **Tests:**
   * Service tests for each state, including mixed group states and a guest office with no groups.
   * Red-test the rollup reuse.

## Acceptance (local, per role)

- [ ] `opa.head.user`, OPA in DepartmentReview: the card links to AIP Entry and nothing is disabled.
- [ ] `opa.user` after submitting: "With your department head".
- [ ] `admin` (PPDO, Returned): the PPDO stage reads Returned, matching the board.
- [ ] FY 2027 selected: no submission stage.
- [ ] No new request in the network tab; page CLS still under 0.1.

## Out of scope

The full status-band redesign (ticket 4), the reviewer's card (ticket 2).

Wireframes: `docs/v1.8/wireframes/dashboard/` (board 5 lists the sentence for every state).
