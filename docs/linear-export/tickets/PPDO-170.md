---
id: PPDO-170
title: 'Staff Divisions — show a toast when a division assignment saves'
status: Done
project: PPDO Portal
labels: 
milestone: Demo 2 — change requests & fixes
created: 2026-09-30
completed: 2026-09-30
parent: PPDO-122
blocked_by: (none)
related: PPDO-130, PPDO-135
url: https://linear.app/ralphoksiprojects/issue/PPDO-170
uuid: cb7d91e4-c6de-4a9c-aae2-88e3de509e0b
---

# PPDO-170 — Staff Divisions — show a toast when a division assignment saves

Raised by Ralph on 2026-09-30 during the Demo 2.8 run (step 17). On **Configuration → Staff Divisions** (`/config/office-users`, [PPDO-135](https://linear.app/ralphoksiprojects/issue/PPDO-135/demo-213-let-a-department-head-assign-divisions-to-their-own-offices)), the division select saves as soon as it changes, with no Save button. A failed save already showed an error toast and reverted, but a successful one showed nothing.

## Change

On success, a toast reads "{name} assigned to {division}." or "{name} is now unassigned." Failure handling is unchanged.

## Acceptance

- [ ] Changing a user's division shows the success toast naming the user and the division
- [ ] Setting Unassigned shows "{name} is now unassigned."
- [ ] A failed save still shows the error toast and snaps back
