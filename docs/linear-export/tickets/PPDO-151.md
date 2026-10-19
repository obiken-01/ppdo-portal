---
id: PPDO-151
title: 'Demo 2.8 T4 — AIP Entry UI for division encoders (status strip, lock, submit)'
status: Done
project: PPDO Portal
labels: 
milestone: Demo 2 — change requests & fixes
created: 2026-09-28
completed: 2026-09-29
parent: PPDO-130
blocked_by: PPDO-149
related: PPDO-133
url: https://linear.app/ralphoksiprojects/issue/PPDO-151
uuid: 81d1266c-b7cc-45cb-a716-c626dfe0586e
---

# PPDO-151 — Demo 2.8 T4 — AIP Entry UI for division encoders (status strip, lock, submit)

Spec: `docs/v1.8/Division_Submit_Spec.md` §6.1 and §10. Read `docs/DESIGN_SYSTEM.md` first.

## Scope

* **Division status strip** above the tree:
  * division name, state and activity count
  * **Submit {Division}**, with a `ConfirmDialog` that shows the ceiling warning when the office is over its ceiling
  * the office's ceiling position, shown at every stage
* **Division pill** on each activity row (`rounded-full`), shown only in offices with divisions.
* **Read-only states:** other divisions' rows are disabled (not hidden), with a lock icon and a "Belongs to {Division}" tooltip. Own rows are disabled after submitting, with a banner.
* **Staff with no division:** a banner, and the tree is read-only.
* **All UI states** per §6.1: the loading skeleton (no spinner), empty, error with Retry (read-only until the status loads), a success toast, and validation shown in the readiness panel.
* Driven by `canEdit` on `AipActivityDto`. Don't re-derive the lock rules on the client.
* Offices without divisions: no visual change.

Not a manual-implementation candidate as a whole: the entry page is 769 lines and the lock states are subtle.
