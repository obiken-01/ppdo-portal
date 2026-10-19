---
id: PPDO-58
title: 'V18-48 — PBO ceiling management UI (picker half already shipped)'
status: Done
project: PPDO Portal
labels: 
milestone: v1.8.0 — Office Users, AIP Redesign & Reviewer Flow
created: 2026-09-03
completed: 2026-09-08
parent: PPDO-48
blocked_by: (none)
related: PPDO-17, PPDO-73, PPDO-163
url: https://linear.app/ralphoksiprojects/issue/PPDO-58
uuid: 810c90c8-e12c-4696-b6ae-0bd2b4fc7c61
---

# PPDO-58 — V18-48 — PBO ceiling management UI (picker half already shipped)

**In one line:** the PBO's own view for setting and revising office ceilings — the office picker that makes it reachable is already done.

**Plan: **`docs/v1.8/Phase_Plan.md` §5 · **Size:** S

## ✅ Read first — this does NOT start from a clean page

[PPDO-17](https://linear.app/ralphoksiprojects/issue/PPDO-17/allocation-page-office-picker-for-cross-office-ceiling-holders-pull) **shipped the office-picker half** (PR #277, merged), pulling it forward out of this ticket. Already done:

* The office axis is "is this caller cross-office?" — host-office membership **or **`canManagePboCeiling` — as one named flag, not an inlined OR
* Role-based labelling: a PBO-only caller sees **Budget Ceilings** in sidebar, breadcrumb, header and tab, with no division vocabulary and no PPA tab (`frontend/src/lib/budget-planning-labels.ts` is the single source for all four)
* Division split and PPA tab gated on `canSetAllocations && selectedOffice.isHostOffice`
* `effectiveTab` fallback so switching office off PPDO while on the PPA tab does not strand the user
* `InfoTip.tsx`, and the fixed `hidden` + `display:flex` bug on Save Allocations

**What remains is the ceiling *management* experience itself** — reviewing, revising and understanding ceilings across offices, now that reaching them is possible.

## ⚠️ Do not clamp `remaining` at zero

The standing warning, and this is the page where it is most tempting. ✅ **A5-b, answered 2026-08-26: a ceiling cut is non-destructive.** Encoded work **stands** and fails at V18-49's submit gate. PBO needs **no confirmation dialog beyond the ordinary one, and no cascade**.

After a cut, an office's remaining allocation is legitimately **negative**. A `Math.Max(0, …)` in the ledger, the DTO or the UI hides the only signal the office has that it must revise. **Show the negative figure.**

## Watch out

⚠️ [PPDO-17](https://linear.app/ralphoksiprojects/issue/PPDO-17/allocation-page-office-picker-for-cross-office-ceiling-holders-pull) flagged that its work was `tsc`/lint clean but **never browser-verified** (PR #273 carried the gate and labels). A manual pass over the allocation page as a PBO-only caller belongs in this ticket's test plan.

⚠️ The PBO officer is a **non-PPDO user** and cannot be modelled as a PPDO admin — seven flags return `true` for Admin before any override is read, so "admin with restrictions" is not expressible. See [PPDO-17](https://linear.app/ralphoksiprojects/issue/PPDO-17/allocation-page-office-picker-for-cross-office-ceiling-holders-pull)'s reasoning before proposing it again.

## Done when

* A PBO-only caller can review and revise ceilings across offices
* A cut ceiling shows a negative remaining rather than a clamped zero
* No cascade and no extra confirmation on a cut
* The four labelling surfaces still read "Budget Ceilings" with no division vocabulary
* Browser-verified as a PBO-only caller, not only type-checked

## Out of scope

The office picker (done, [PPDO-17](https://linear.app/ralphoksiprojects/issue/PPDO-17/allocation-page-office-picker-for-cross-office-ceiling-holders-pull)) · the ceiling check (V18-46) · the submit gate (V18-49) · division allocation for guest offices (they have none — V18-47).
