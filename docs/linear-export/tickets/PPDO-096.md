---
id: PPDO-96
title: 'Rename "Budget Planning" → "Investment Planning" (labels only)'
status: Done
project: PPDO Portal
labels: 
milestone: v1.8.0 — Office Users, AIP Redesign & Reviewer Flow
created: 2026-09-16
completed: 2026-09-16
parent: (none)
blocked_by: (none)
related: PPDO-87
url: https://linear.app/ralphoksiprojects/issue/PPDO-96
uuid: 68de9b59-6fe2-452f-894b-dd8456075e6a
---

# PPDO-96 — Rename "Budget Planning" → "Investment Planning" (labels only)

**In one line:** every user-visible "Budget Planning" string becomes "Investment Planning". Routes, API paths, permission-flag names and type names are untouched.

**Source:** 2026-09-15 PDC/finance demo notes · **Size:** S · **Demo:** required on or before 2026-09-23

## Scope

Twelve user-visible strings in the frontend and two backend error messages:

* `components/layout/Sidebar.tsx` nav item, `components/layout/Topbar.tsx` `rootLabel`, `(portal)/layout.tsx` breadcrumb map
* `(portal)/budget-planning/page.tsx` page title
* `admin/users/page.tsx` — "Access Budget Planning", "Review Budget Planning (reviewer)"
* `config/divisions/page.tsx` flag label, `config/audit-log/page.tsx` page description
* `ui/LandingPageSelect.tsx` — "Budget Planning Dashboard"
* `aip/review/page.tsx` — "Back to Budget Planning"; `wfp/page.tsx` + `wfp/entry/page.tsx` — "Go to Budget Planning → Allocation"
* `BudgetPlanningDashboardService.cs` / `BudgetPlanningDashboardFunctions.cs` — "You do not have access to Budget Planning."

## Deliberately NOT renamed

`/budget-planning/*` routes, `/api/budget-planning/*` paths, `CanAccessBudgetPlanning` / `CanReviewBudgetPlanning` flags, `budget-planning-access.ts`, `types/budget-planning.ts`, file and folder names. A URL rename means redirects for every bookmarked page; a flag rename is a second `HasColumnName` exercise like [PPDO-87](https://linear.app/ralphoksiprojects/issue/PPDO-87/office-ceiling-authority-moves-to-ppdo-finance-exempt-ceiling-save)'s for no user-visible gain.

## Acceptance

- [ ] No user-visible "Budget Planning" text remains anywhere in the portal
- [ ] `/budget-planning` URLs still work and are unchanged
- [ ] The dashboard, breadcrumb and sidebar all read "Investment Planning"
