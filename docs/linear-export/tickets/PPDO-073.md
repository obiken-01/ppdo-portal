---
id: PPDO-73
title: 'V18-55 — The consolidated view: partial by design'
status: Done
project: PPDO Portal
labels: 
milestone: v1.8.0 — Office Users, AIP Redesign & Reviewer Flow
created: 2026-09-08
completed: 2026-09-14
parent: PPDO-67
blocked_by: PPDO-68
related: PPDO-58, PPDO-61, PPDO-83, PPDO-84
url: https://linear.app/ralphoksiprojects/issue/PPDO-73
uuid: 5d38b550-955f-42fa-b793-e1105318f8df
---

# PPDO-73 — V18-55 — The consolidated view: partial by design

**In one line:** the province-wide AIP as PPDO reviewers see it — which is the record that already exists, filling up office by office, not a document assembled at the end.

**Plan:** `Phase_Plan.md` §6, §12.6 · **Size:** M · **Blocked by:** [PPDO-68](https://linear.app/ralphoksiprojects/issue/PPDO-68/phase-4-spec-aip-review-specmd) (spec)

## ✅ Much smaller than V18-55 originally read (tracker B4-a)

**"Consolidated" is not a new record.** It is the **existing multi-office record, filled in office by office as they submit.** Nothing is assembled, copied or snapshotted. [PPDO-61](https://linear.app/ralphoksiprojects/issue/PPDO-61/reverse-the-office-owned-aip-record-shape-one-base-record-per-fiscal) settled the shape from the other direction — one base AIP record per fiscal year holding every office — so the container this needs already exists and already holds every office's subtree.

What remains is therefore a **view and its access rule**, not a data-building exercise.

## The access rule

| Who | Sees the consolidated view |
| -- | -- |
| PPDO **reviewers** (`CanReviewAllOffices`) | ✅ |
| PPDO **division users** | ❌ — explicitly not (tracker B4) |
| Any other office's users | ❌ |

⚠️ **PPDO division users being excluded is the non-obvious half.** They are host-office users and most cross-office reasoning in the codebase keys on `IsHostOffice`; that is the wrong axis here. The gate is the reviewer flag.

## ⚠️ Partial consolidation is the normal state, not an edge case

PPDO reviewers review *"the consolidated work so far"* (tracker B4-b) — **before every office has submitted**. So there is no "wait until complete" gate, and the view must read correctly when most offices are still in `Draft`. Show what is submitted and make the gaps legible; do not render an unsubmitted office as zero.

That last point matters for the same reason [PPDO-58](https://linear.app/ralphoksiprojects/issue/PPDO-58/v18-48-pbo-ceiling-management-ui-picker-half-already-shipped)'s ceiling-usage strip returns null rather than ₱0: an office that has not submitted has not encoded nothing.

## Scope

* A consolidated read across offices for the fiscal year, respecting the reviewer gate.
* Totals over submitted offices only, with the submitted / not-submitted split visible.
* ⚠️ Slim DTO and server-side aggregation — a fat AIP DTO already produced a 1.2 MB response once (`PERFORMANCE_GUIDELINES.md`), and this is 19 offices at once.
