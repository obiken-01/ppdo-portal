---
id: PPDO-129
title: 'Demo 2.7 — One fund source per activity (keep the multi-fund code dormant)'
status: Done
project: PPDO Portal
labels: 
milestone: Demo 2 — change requests & fixes
created: 2026-09-23
completed: 2026-09-24
parent: PPDO-122
blocked_by: (none)
related: (none)
url: https://linear.app/ralphoksiprojects/issue/PPDO-129
uuid: 05c1f3e2-b333-44bb-b3b3-ce2c5017b338
---

# PPDO-129 — Demo 2.7 — One fund source per activity (keep the multi-fund code dormant)

*"1 fund source per activity. Lets not remove the code for the multi fund source. In case they may want it back."*

Constrain the AIP activity to a single fund source, **without deleting the multi-fund handling** — it goes dormant, not away.

## What exists

An activity carries `fundingSourceRaw`, free text with no controlled vocabulary. Multi-fund values are slash-separated (`"GF/20% DF"`), and `FundingSource.Aliases` explicitly refuses to resolve them:

> *Multi-fund AIP values (slash-separated, e.g. "GF/20% DF") are never resolved via aliases.*

Expenditures already carry a single `fundingSourceId`, so the constraint matches where the money actually resolves.

## The interesting part is the existing data

⚠️ FY2027 real data contains multi-fund activities — the province's own AIP has combined fund sources. So this cannot be a blanket validation:

* New and edited FY2028+ activities: one fund
* Existing multi-fund rows: must still **load, render and export** without being corrupted or silently rewritten

Decide explicitly what happens when an encoder opens an existing multi-fund activity. Suggested: show it read-only with a note, and require a single choice only if they edit the field. Forcing a pick on open would rewrite historical records as a side effect of viewing them.

## "Keep the code"

Leave the parsing and rendering paths in place behind the single-fund entry constraint, with a comment saying **why** they look unreachable — dormant code with no explanation gets deleted by the next person as dead weight, which is exactly what Ralph asked to avoid.

## Acceptance

- [ ] New/edited activities accept exactly one fund source
- [ ] Existing multi-fund rows load, render and export unchanged
- [ ] Viewing a multi-fund activity never rewrites it
- [ ] Multi-fund paths remain, commented with the reason
- [ ] Annex B export verified against a record containing both shapes
