# Consolidated AIP — wireframes (PPDO-73)

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
