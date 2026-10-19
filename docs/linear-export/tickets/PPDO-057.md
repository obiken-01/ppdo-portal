---
id: PPDO-57
title: 'V18-47 — Office-level AIP ceiling checks for offices without divisions'
status: Done
project: PPDO Portal
labels: 
milestone: v1.8.0 — Office Users, AIP Redesign & Reviewer Flow
created: 2026-09-03
completed: 2026-09-08
parent: PPDO-48
blocked_by: PPDO-56
related: PPDO-4, PPDO-30, PPDO-38, PPDO-66
url: https://linear.app/ralphoksiprojects/issue/PPDO-57
uuid: efa58714-c970-400e-8dc6-803785645fc5
---

# PPDO-57 — V18-47 — Office-level AIP ceiling checks for offices without divisions

**In one line:** guest offices have ceilings but no divisions, so the check has to work one level up.

**Plan: **`docs/v1.8/Phase_Plan.md` §5 · **Size:** S · **Blocked by:** V18-46

## Why this is a separate ticket

Every allocation primitive in Budget Planning was built when PPDO was the only office, so the ceiling → **division allocation** → draw-down chain assumes a division exists. **Non-PPDO offices have ceilings and no divisions.** V18-46's check, written against the division-level chain, has nothing to resolve for them.

## The rule underneath

**Division is a host-office-only axis** (`Permission_Matrix.md` §3.1, [PPDO-4](https://linear.app/ralphoksiprojects/issue/PPDO-4/scope-rule-division-is-not-a-scoping-axis-for-non-ppdo-office-users), [RAL-250](https://linear.app/ralphoksiprojects/issue/PPDO-4/scope-rule-division-is-not-a-scoping-axis-for-non-ppdo-office-users)). It is not that guest offices happen to have no divisions configured yet — it is that division is not a scoping axis for them at all. [PPDO-30](https://linear.app/ralphoksiprojects/issue/PPDO-30/budget-planning-allocation-setup-status-divisionid-is-not-scoped-to) fixed a live leak of exactly this shape on the allocation setup gate, and [PPDO-38](https://linear.app/ralphoksiprojects/issue/PPDO-38/v18-39-aip-scope-resolver-officescope-divisionscope)'s `AipReadScope` applies the division filter **only to the host office's own AIP offices**.

So this is not "handle a null division". It is a second, legitimate shape.

## ⚠️ Do not resolve it by inventing a default division

A synthetic "office-wide" division row would make every division-shaped query work and would put a row in `divisions` that does not correspond to anything, that appears in pickers, and that PPDO's own division logic would eventually trip over. The two shapes should stay two shapes.

## ⚠️ Blank means zero

Same trap as V18-46: `GetDivisionAllocationAsync` resolves a missing allocation to `0m`. An office-level check that falls through to the division path for a guest office will read *zero ceiling*, not *no ceiling* — and silently forbid everything.

## Done when

* A guest office with a ceiling and no divisions can be checked at submit
* The check reads the office-level ceiling directly, with no synthetic division row anywhere
* A guest office's missing configuration is distinguishable from a zero ceiling
* PPDO's division-level path is unchanged
* Tests cover both shapes, and a guest office is not represented by a null division

## Out of scope

Creating divisions for guest offices · the PBO ceiling UI (V18-48) · the submit checklist (V18-49).
