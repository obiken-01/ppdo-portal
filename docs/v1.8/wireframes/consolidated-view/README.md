---
type: wireframe
version: v1.8.0
status: shipped
tickets: [PPDO-73, PPDO-79]
updated: 2026-09-14
---

# Consolidated AIP — wireframes (PPDO-73)

Up: [v1.8 hub](../../README.md)

The design canvas for the consolidated view, agreed before the build per the PPDO-79 lesson
(`../aip-review/README.md`).

Ralph, 2026-09-14: the grid follows the province's AIP Excel — this layout is also the future Excel
output, so the page doubles as its preview. Clicking an activity opens the read-only review modal.

## What is here

| File | Artboard |
|---|---|
| `Main.dc.html` | The consolidated page on the General sector — Annex B columns A–R per `AIP_Form_Spec.md`. |
| `ActivityModal.dc.html` | Copied unchanged from `../aip-review/ActivityModalReviewer.dc.html` — the modal an activity opens. |
| `States.dc.html` | Loading, empty sector, fiscal year not opened. |
| `canvas.json` | Frame positions and the sticky notes carrying the open questions. |

Sample data is FY2028-shaped and is not real.

## Rebuilding the canvas

`consolidated-view.html` is a git-ignored build output (~2.5 MB, it bundles the editor). Edit the
`.dc.html` files here and re-seed through the `design` skill — never hand-edit the output.

## Related

- [v1.8.0 Phase 4 — AIP Review & Consolidation](../../AIP_Review_Spec.md) — spec, shipped
- [v1.8.0 — AIP Review: the same drill-down as AIP Entry, with a Full office view](../../AIP_Review_Layout_Spec.md) — spec, shipped
- [v1.8.0 — Consolidated AIP moves into the Report page](../../AIP_Report_Spec.md) — spec, shipped
- [v1.8.0 — Division heads submit to the department head (PPDO-130)](../../Division_Submit_Spec.md) — spec, current
- [AIP Review — wireframes](../aip-review/README.md) — wireframe, shipped
- [Submission history — wireframes (PPDO-77)](../submission-history/README.md) — wireframe, shipped
