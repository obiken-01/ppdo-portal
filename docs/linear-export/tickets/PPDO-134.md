---
id: PPDO-134
title: 'Demo 2.12 — AIP Entry shows a guest-office encoder every program, ignoring their division'
status: Done
project: PPDO Portal
labels: 
milestone: Demo 2 — change requests & fixes
created: 2026-09-23
completed: 2026-09-23
parent: PPDO-122
blocked_by: (none)
related: PPDO-123, PPDO-126, PPDO-127, PPDO-130, PPDO-135, PPDO-148
url: https://linear.app/ralphoksiprojects/issue/PPDO-134
uuid: b5c6a9a8-8551-468a-baf7-e300251cbac8
---

# PPDO-134 — Demo 2.12 — AIP Entry shows a guest-office encoder every program, ignoring their division

Found live while testing SPO from scratch (2026-09-23).

`spo.user` is assigned to SPO's **ADMIN** division. In PPA → Division, ADMIN holds **3 of 4** programs — CAPACITY DEVELOPMENT PROGRAM belongs to RECORDS. But AIP Entry's program picker offers `spo.user` **all four**, so an ADMIN encoder can encode against another division's programme.

## Cause — a rule keyed to the wrong thing

`AipReadScope`:

> ⚠️ **The division filter applies only to the host office's own AIP offices.** A division belongs to an office, so it can only narrow that office's work. A PPDO caller scoped to the Planning Division still sees every guest office's AIP in full — PPDO reviews all of them, and its internal division of labour says nothing about GSO's programs.

The frontend mirrors it in `aip/entry/page.tsx`:

```ts
const divisionFiltered = me?.isHostOffice === true && me.divisionId != null && !!me.division;
```

⚠️ **The reasoning is correct and must survive the fix.** What is wrong is the implementation: *"host office only"* and *"the caller's own office"* were the same thing while only PPDO users had divisions. [PPDO-123](https://linear.app/ralphoksiprojects/issue/PPDO-123/demo-21-office-users-cannot-be-assigned-a-division) **ended that equivalence** and nothing downstream was revisited — same stale assumption as [PPDO-126](https://linear.app/ralphoksiprojects/issue/PPDO-126/demo-24-division-allocations-are-not-saved) and [PPDO-127](https://linear.app/ralphoksiprojects/issue/PPDO-127/demo-25-per-division-allocation-breakdown-for-non-ppdo-offices).

## Fix

Re-key the division axis from **the host office's** AIP offices to **the caller's own office's** AIP offices. Identical behaviour for every PPDO caller; correct for a guest office. The cross-office rule is preserved exactly — a division still narrows only its own office's work.

Both layers: `AipReadScope` and the `divisionFiltered` flag plus its notice ("Showing the programs assigned to …").

## Acceptance

- [ ] A guest-office encoder with a division sees only their division's programs
- [ ] A guest-office encoder with **no** division still sees all of their office's programs — a filter on nothing must not hide everything
- [ ] A PPDO caller's behaviour is **unchanged**, including still seeing every guest office's AIP in full
- [ ] The "showing programs assigned to X" notice appears for guest offices too — a silent filter reads as missing data
- [ ] Server-side, not just the picker: the read scope is the guard

⚠️ Check the **submit gate and the readiness counts** in the same pass. If entry is division-scoped but "0 activities in this office" still counts the whole office, the two will disagree on screen.

Prerequisite for [PPDO-130](https://linear.app/ralphoksiprojects/issue/PPDO-130/demo-28-division-head-submits-to-department-head-who-can-return-that) (division-level submit), which needs a settled answer to "which activities are this division's work".
