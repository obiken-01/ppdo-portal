---
id: PPDO-167
title: 'AIP Review search — rows a division has submitted show under Office review before the whole office is in'
status: Done
project: PPDO Portal
labels: 
milestone: Demo 2 — change requests & fixes
created: 2026-09-30
completed: 2026-09-30
parent: PPDO-122
blocked_by: (none)
related: PPDO-130, PPDO-149
url: https://linear.app/ralphoksiprojects/issue/PPDO-167
uuid: e3e0ab50-78f7-47fd-8ed6-775aa4731222
---

# PPDO-167 — AIP Review search — rows a division has submitted show under Office review before the whole office is in

Raised by Ralph on 2026-09-30 during the Demo 2.8 ([PPDO-130](https://linear.app/ralphoksiprojects/issue/PPDO-130/demo-28-division-head-submits-to-department-head-who-can-return-that)) run, as `pto.head` on AIP Review search.

The **Status → Office review** chip only fills once the whole office is in Department Review, meaning every division has submitted. Until then, a department head can't see which PPAs have already reached them.

## Cause

`AipRepository.BuildNodeQuery` gave every row the office's `workflow_status` and filtered offices on it. The division submits (`aip_division_submissions`, [PPDO-149](https://linear.app/ralphoksiprojects/issue/PPDO-149/demo-28-t3-division-submit-return-endpoints-office-state-derivation)) never reached the search.

## Decisions (Ralph, 2026-09-30)

1. **An activity follows its own division.** A **project or program** counts only when it has activities and **every** one is in a submitted division. An untagged activity counts as not submitted.
2. These rows count under the **existing Office review chip** (no new chip).
3. **A returned office:** a division that re-submits to the department head moves to Office review, and the others stay under Returned.

Past the division stage (office in Department Review, With PPDO, Done), the office's own state still wins. Division rows stay `Submitted` after the office goes to PPDO and must not pull its rows back.

## Acceptance

- [ ] PTO with Admin submitted and Cash in Draft: the Office review chip counts and lists Admin's activities, plus any project or program whose activities are all Admin's
- [ ] Cash's activities and mixed projects/programs stay under Draft
- [ ] After the department head sends the office to PPDO, every row shows With PPDO
- [ ] An office with no divisions behaves exactly as before
