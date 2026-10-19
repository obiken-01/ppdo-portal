---
id: PPDO-146
title: 'Demo 2.8 T0 — Ceiling warns (not blocks) at the department-head submit'
status: Done
project: PPDO Portal
labels: 
milestone: Demo 2 — change requests & fixes
created: 2026-09-28
completed: 2026-09-28
parent: PPDO-130
blocked_by: (none)
related: PPDO-216
url: https://linear.app/ralphoksiprojects/issue/PPDO-146
uuid: e104fc5d-31e6-4dd4-bf71-b8b5d9603e00
---

# PPDO-146 — Demo 2.8 T0 — Ceiling warns (not blocks) at the department-head submit

Spec: `docs/v1.8/Division_Submit_Spec.md` — decision 13, §4 "Changed existing endpoints", §10, §11 (merged in [obiken-01/ppdo-portal#386](https://linear.app/ralphoksiprojects/review/docsv18-spec-division-heads-submitting-to-the-department-head-ppdo-130-504d29c87106)).

Applies to **every office**, including offices without divisions. It is independent of T1–T5 and can ship first.

## Current → target

* Today: being over the ceiling blocks the encoder's `POST …/aip/{aipId}/submit` (`AipReadinessDto.CanSubmit` is false, "there is no submit anyway").
* Target: the submit to the department head **succeeds with a warning**. `SubmitToPpdoAsync` still refuses while the office is over its ceiling (unchanged).

## Scope

* `AipReadinessDto` gains `CeilingWarning` (string or null). The ceiling alone no longer sets `CanSubmit` to false. Completeness issues still do.
* `AipSubmitResultDto` gains `ceilingWarning`.
* Readiness panel: show the ceiling as an amber warning, not a blocker. The submit confirm dialog repeats it, and so does the toast.

## Test focus (`AipSubmitServiceTests`, TDD)

* Over-ceiling submit succeeds with `ceilingWarning` set
* An incomplete activity still blocks
* Submit to PPDO over the ceiling is still refused

## 🎓 Manual-implementation candidate

Small, one service, and fast `dotnet test` feedback. The sibling to copy is how `SubmitToPpdoAsync` re-runs the checklist. Guidance ladder level 1: spec + sibling. Handing it back is fine.
