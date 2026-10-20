---
id: PPDO-106
title: 'Office Ceilings page for PPDO finance; Allocation''s ceiling becomes read-only'
status: Done
project: PPDO Portal
labels: 
milestone: v1.8.0 — Office Users, AIP Redesign & Reviewer Flow
created: 2026-09-20
completed: 2026-09-20
parent: (none)
blocked_by: (none)
related: PPDO-87
url: https://linear.app/ralphoksiprojects/issue/PPDO-106
uuid: 8a8a16e8-865c-455d-bde2-a260a334889a
---

# PPDO-106 — Office Ceilings page for PPDO finance; Allocation's ceiling becomes read-only

Spec: `docs/v1.8/Office_Setup_Spec.md` (C1). From the 2026-09-15 PDC/finance demo, item 8.

## Goal

PPDO finance sets every office's General Fund ceiling from a page of its own, instead of the PPDO-only Allocation page. The Allocation page keeps showing the ceiling, read-only.

## Scope

* New page `/budget-planning/office-ceilings`, gated on `CanManageOfficeCeilings` (already exists — [PPDO-87](https://linear.app/ralphoksiprojects/issue/PPDO-87/office-ceiling-authority-moves-to-ppdo-finance-exempt-ceiling-save)).
* One row per active office for the chosen fiscal year: ceiling, encoded so far, remainder. Reuse `BulkCeilingModal` and the existing `ceiling-usage` strip.
* Saves go through the existing `PUT /budget-planning/allocation/ceiling` — no API change. It is deliberately not office-clamped and is exempt from `ReviewerWriteGuard` ([PPDO-87](https://linear.app/ralphoksiprojects/issue/PPDO-87/office-ceiling-authority-moves-to-ppdo-finance-exempt-ceiling-save)); do not change either.
* Allocation page: the ceiling field becomes read-only, with a link to the new page for holders of the grant.
* **D4:** build the page with one tab per funding source but render **only the General Fund tab**. Other funds' tabs stay behind a constant, commented with this decision — current direction is that only the GF carries a ceiling.
* Route access in **three** places: sidebar, the page's `useMe` gate, and `(portal)/layout.tsx`'s `ppdoOnlyBudgetPlanning`.

## Behaviour

* A ceiling cut below what the office already encoded still **succeeds** (non-destructive, A5-b); the row shows the over-ceiling state.
* A fiscal year with no ceilings shows "Not set", never ₱0.00.
* Loading shows a skeleton row per office, not a centred spinner (CLS rule).
* Bulk save: a failure on one office names that office and leaves the rest saved.

## Out of scope

* Any permission change (none needed).
* Ceiling history.

## Acceptance

1. A finance user sets three offices' ceilings; Allocation shows them read-only.
2. Only the General Fund tab is visible.
3. A user without the grant is redirected and sees no sidebar link.

Candidate for hand-coding: close siblings exist (the Allocation ceiling tab, `BulkCeilingModal`) and it touches no permission code.
