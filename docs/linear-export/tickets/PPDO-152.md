---
id: PPDO-152
title: 'Demo 2.8 T5 — Department-head divisions panel, re-tag, readiness board chip'
status: Done
project: PPDO Portal
labels: 
milestone: Demo 2 — change requests & fixes
created: 2026-09-28
completed: 2026-09-29
parent: PPDO-130
blocked_by: PPDO-149
related: (none)
url: https://linear.app/ralphoksiprojects/issue/PPDO-152
uuid: 62de8549-0beb-41cf-99d1-d8df171ea729
---

# PPDO-152 — Demo 2.8 T5 — Department-head divisions panel, re-tag, readiness board chip

Spec: `docs/v1.8/Division_Submit_Spec.md` §6.2, §6.3 and §10. Read `docs/DESIGN_SYSTEM.md` first.

## Scope

* **Divisions panel**, for the department head on Entry and Review:
  * each division's status, activity count, and who submitted it and when
  * **Return** on each Submitted division, with a `ConfirmDialog`
* **Untagged activities** callout: shows the count and filters the tree to those activities.
* A **division select** on each activity row for re-tagging. Hidden for anyone who isn't the department head.
* **Submit to PPDO** disabled with "Waiting on: {divisions}" until every division has submitted.
* **Readiness board:** the office card shows "n of m divisions submitted" while the office is in Draft.
* The notification count includes divisions that submitted since the department head's last visit (existing `AipNotificationService` pattern, no email).
* All UI states per §6.

## 🎓 Manual-implementation candidate (part)

The readiness-board "n of m divisions submitted" chip, once T3 exists. The sibling to copy is the existing office card's status chip. Guidance ladder level 1.
