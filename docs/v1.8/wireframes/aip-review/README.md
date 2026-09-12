# AIP Review — wireframes

The design canvas behind the PPDO-79 review feedback:
**https://claude.ai/code/artifact/e78ec10f-f86d-4550-b2c8-c9348c6b340b**

> ⚠️ **These exist because a screen got built before its layout was agreed.** Ralph's manual review
> of the AIP Review search page (2026-09-09) raised four findings whose root cause was one process
> gap, not four bugs — no wireframe. `docs/SPEC_STANDARD.md` §2 already lists UI states as the
> project's largest fix category. Agree the layout here, then build.

## What is here

| File | Artboard |
|---|---|
| `Main.dc.html` | Search results with the filter panel collapsed. Carries a `panel` tweak (collapsed / expanded) — flip it on the canvas to see the expanded panel with `OfficeSelect` in place of the hand-rolled `<select multiple>`. |
| `ActivityModalReviewer.dc.html` | The activity modal as the PPDO consolidated reviewer sees it — read-only, comment enabled. |
| `ActivityModalOfficeHead.dc.html` | The same modal as the department head sees it — editable, on an AIP returned by PPDO. |
| `canvas.json` | Frame positions and the sticky notes carrying the open questions. |

Every value is lifted from the real thing rather than approximated — the tokens in
`frontend/tailwind.config.ts`, and the anatomy of `Modal`, `AipHierarchy`, `AipRowFigures`,
`AipComments` and `AipTreeCells`. Sample data is OPA / FY2028-shaped and is not real.

## Rebuilding the canvas

The published `aip-review-screens.html` is a **build output** — ~2.5 MB, because it bundles the
Claude Design editor — and is git-ignored. Never hand-edit it. Edit the `.dc.html` files here, then
re-seed and republish through the `design` skill, which owns `seed-canvas.mjs` and the payload
template. From a Claude Code session in this repo, `/design` is the entry point.

## Open questions

Six of them, each a sticky note beside its artboard on the canvas and listed in full on
**PPDO-79**. They are deliberately unanswered — do not build past them.
