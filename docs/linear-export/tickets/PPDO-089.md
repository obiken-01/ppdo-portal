---
id: PPDO-89
title: 'AIP Entry: WFP-style Program → Project → Activity drill-down layout'
status: Done
project: PPDO Portal
labels: 
milestone: v1.8.0 — Office Users, AIP Redesign & Reviewer Flow
created: 2026-09-15
completed: 2026-09-16
parent: (none)
blocked_by: (none)
related: PPDO-95, PPDO-99
url: https://linear.app/ralphoksiprojects/issue/PPDO-89
uuid: 5952e211-78b0-413c-956a-60a66d674115
---

# PPDO-89 — AIP Entry: WFP-style Program → Project → Activity drill-down layout

**In one line:** replace AIP Entry's whole-tree page with the WFP entry page's picker, so the encoder only ever sees the program, project or activity they are working on.

**Spec:** `docs/v1.8/AIP_Entry_Layout_Spec.md` §2 decisions 1–7, §3 non-delete rows, §6 · **Size:** L · **Blocks:** the delete-controls ticket

## Why

PDC feedback (demo 2026-09-15): entering one activity while every other PPA is on screen is overwhelming. The WFP entry page never had this problem because it picks Program → Project → Activity first.

## Scope

* Three `Lookup`s (Program / Project / Activity), each disabled until its parent is picked and clearing its children on change. Program options show the sub-office group when the office has several.
* Panel shows the deepest selected level only: Program panel (header + compact project list + Add project) → Project panel (header + reserved "Project details" section + activity list + Add activity) → Activity panel (existing `AipActivityFields` + `AipExpenditureTable` unchanged, footer Change activity · Change project · Done).
* Checklist, comment filter bar and a sticky office figure header stay at the top.
* Selection in the URL (`?fiscalYear=&programId=&projectId=&activityId=`); stale id falls back to the deepest valid ancestor with a notice.
* Checklist issues and unresolved comments **select** their node; lookup options carry unresolved-comment badges.
* Add selects what it created, spliced in without reload.
* Split into `components/aip/entry/AipEntryPicker|AipProgramPanel|AipProjectPanel|AipActivityPanel.tsx` — the 784-line page must shrink.

## Not in this ticket

Delete controls (own ticket), the new Project fields (pending field list), AIP Review screen (Ralph asking PDC).

## Manual-coding candidate?

No — large stateful page with a CLS-sensitive loading shape.
