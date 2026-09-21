# Submission history — wireframes (PPDO-77)

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
