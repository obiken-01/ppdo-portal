---
id: PPDO-79
title: 'V18-83 — Split the AIP sidebar item into Entry and Review'
status: Done
project: PPDO Portal
labels: 
milestone: v1.8.0 — Office Users, AIP Redesign & Reviewer Flow
created: 2026-09-08
completed: 2026-09-13
parent: PPDO-67
blocked_by: PPDO-68, PPDO-76
related: PPDO-71, PPDO-74, PPDO-78, PPDO-82
url: https://linear.app/ralphoksiprojects/issue/PPDO-79
uuid: 28ace3e1-26c6-49b4-bac0-dea6ee439267
---

# PPDO-79 — V18-83 — Split the AIP sidebar item into Entry and Review

**In one line:** AIP Review becomes its own sidebar destination beside AIP Entry, gated separately.

**Plan:** `Phase_Plan.md` §12.5a, §12.7 · **Size:** S · **Blocked by:** [PPDO-68](https://linear.app/ralphoksiprojects/issue/PPDO-68/phase-4-spec-aip-review-specmd) (spec), [PPDO-76](https://linear.app/ralphoksiprojects/issue/PPDO-76/v18-75-query-first-review-page)

## Why

§12.5 called entry and review "tabs" in one place and "two pages" in another; **§12.5a settles it as two pages.** With the kanban living on Review, Review is the PPDO reviewer's home and deserves to be a destination rather than a tab inside an encoder's page — and it lets the two be permission-gated separately (Entry → encoders, Review → reviewers).

## ⚠️ Two siblings, not a third nesting level

`Sidebar.tsx` is hand-written JSX with exactly **one** collapsible level (Budget Planning → flat children) and no nesting primitive. **WFP already sets the precedent** — it renders as one flat item while linking to `/budget-planning/wfp/entry`.

**Current state:** Budget Planning already has `AIP` (→ `/budget-planning/aip`) and `AIP Entry` (→ `/budget-planning/aip/entry`) as two flat children. So this ticket adds a **third sibling**, `AIP Review`, rather than restructuring anything — smaller than §12.5a's wording implies.

⚠️ Check whether the existing `AIP` item should keep its name once `AIP Review` sits beside it. Three items reading AIP / AIP Entry / AIP Review is one more than most readers will parse; the list item may want to become "AIP Records" or similar. Decide in [PPDO-68](https://linear.app/ralphoksiprojects/issue/PPDO-68/phase-4-spec-aip-review-specmd) rather than at implementation time.

## Scope

* The new sidebar entry + its route, gated on `CanReviewAllOffices` (or the department-head flag — the spec settles which reviewers land here).
* ⚠️ **Hidden, not disabled**, for users without the grant — matching how the sidebar already treats ungranted features.
