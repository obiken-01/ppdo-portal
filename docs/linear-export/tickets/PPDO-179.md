---
id: PPDO-179
title: 'Dashboard cleanups: header, empty Divisions band, zero rows, one-fund width, Not started column'
status: Done
project: PPDO Portal
labels: 
milestone: v1.8.2 — Dashboard Redesign, Caching & Remaining Optimizations
created: 2026-10-04
completed: 2026-10-05
parent: (none)
blocked_by: (none)
related: (none)
url: https://linear.app/ralphoksiprojects/issue/PPDO-179
uuid: a45f18ba-5083-4302-aca0-656db671819f
---

# PPDO-179 — Dashboard cleanups: header, empty Divisions band, zero rows, one-fund width, Not started column

From the dashboard design pass, 2026-10-04 (`docs/v1.8/Dashboard_Design_Pass.md`, findings **F6–F10**). Ticket 5 of 7. Frontend only, size S. Wireframe boards 1–3 and 5.

## Current behaviour → target

* **F6 — empty Divisions band:** guest offices without divisions get "No divisions configured…", with a description saying "Click a row". → Leave the band out when the office has no divisions.
* **F7 — zero rows:** empty division rows repeat `Todo · 0/0 · ₱0.00 · ₱0.00 · ₱0.00`, and "Allocation →" wraps onto two lines. → Empty rows fold into one line, zero amounts print as a dash, and the link stays on one line.
* **F8 — header:** "FY 2028 · PPDO — …" repeats the context bar right under it; the locked Office and Division fields take a full row. → Drop the repeat line and the locked fields (the office is already in the header line).
* **F9 — one fund:** with one fund, the fund card fills a third of the row. → Full width when there is one fund.
* **F10 — Not started column:** 15 chips, with the red-dot legend ("No reviewer — cannot submit") at the bottom. → One wrapped list with "13 have no reviewer · Assign reviewers" at the top. Tint the PPDO review column.

## Acceptance (local)

- [ ] `opa.user`: no Divisions band.
- [ ] `ppdo.finance`: empty division rows fold into one line; dashes, not ₱0.00; Allocation link on one line at 1440 and 1024 px.
- [ ] Header shows the context once; no locked Office/Division row.
- [ ] One fund: the fund card spans the row.
- [ ] `admin` board: the no-reviewer line sits at the top of Not started.
- [ ] Skeletons still match the loaded layout (CLS under 0.1).

## Manual-coding note

**Good hand-coding candidate:** small, frontend only, reversible. Siblings: `DivisionTable.tsx` and the header in `budget-planning/page.tsx`. Easy to check in the browser per role.
