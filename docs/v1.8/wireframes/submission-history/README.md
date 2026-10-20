---
type: wireframe
version: v1.8.0
status: shipped
tickets: [PPDO-77, PPDO-79]
updated: 2026-09-14
---

# Submission history — wireframes (PPDO-77)

Up: [v1.8 hub](../../README.md)

The design canvas for "Show History", agreed before the build per the PPDO-79 lesson
(`../aip-review/README.md`).

## What is here

| File | Artboard |
|---|---|
| `Main.dc.html` | AIP Review, one office, as the PPDO reviewer sees it — the History button in the header. |
| `EntryScreen.dc.html` | AIP Entry for the office's own users — History in the submit panel header. `stage` tweak: returned / locked. |
| `HistoryModal.dc.html` | The modal both buttons open. `state` tweak: success / loading / empty / error. |
| `canvas.json` | Frame positions and the sticky notes carrying the open questions. |

Values are lifted from `frontend/tailwind.config.ts`, `Modal`, `AipSubmitChecklist` and the shipped
review page. Sample data is OPA / FY2028-shaped and is not real.

## Rebuilding the canvas

`submission-history.html` is a git-ignored build output (~2.5 MB, it bundles the editor). Edit the
`.dc.html` files here and re-seed through the `design` skill — never hand-edit the output.

## Related

- [v1.8.0 Phase 4 — AIP Review & Consolidation](../../AIP_Review_Spec.md) — spec, shipped
- [v1.8.0 — AIP Review: the same drill-down as AIP Entry, with a Full office view](../../AIP_Review_Layout_Spec.md) — spec, shipped
- [v1.8.0 — Consolidated AIP moves into the Report page](../../AIP_Report_Spec.md) — spec, shipped
- [v1.8.0 — Division heads submit to the department head (PPDO-130)](../../Division_Submit_Spec.md) — spec, current
- [AIP Review — wireframes](../aip-review/README.md) — wireframe, shipped
- [Consolidated AIP — wireframes (PPDO-73)](../consolidated-view/README.md) — wireframe, shipped
