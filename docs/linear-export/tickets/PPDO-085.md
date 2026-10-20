---
id: PPDO-85
title: 'Long AIP activity names — keep line breaks on screen, grow the entry box, warn past Excel''s printable height'
status: Done
project: PPDO Portal
labels: 
milestone: v1.8.0 — Office Users, AIP Redesign & Reviewer Flow
created: 2026-09-14
completed: 2026-09-15
parent: PPDO-83
blocked_by: (none)
related: PPDO-84, PPDO-105, PPDO-113, PPDO-124
url: https://linear.app/ralphoksiprojects/issue/PPDO-85
uuid: b4ec0260-06e3-4941-b1bc-6699c5362d9a
---

# PPDO-85 — Long AIP activity names — keep line breaks on screen, grow the entry box, warn past Excel's printable height

## Why

Found while checking the Annex B export ([PPDO-84](https://linear.app/ralphoksiprojects/issue/PPDO-84/v18-60-aip-annex-b-excel-export-from-the-consolidated-aip-page)) in real Excel, 2026-09-14. Activity names in the province's data run long and carry their own line breaks — e.g. *"Creation of Plantilla Positions:"* followed by a list of offices and positions, one per line.

## What already works

* `aip_activities.name` is `nvarchar(max)` — nothing is truncated.
* AIP Entry's name field (`AipActivityFields.tsx`) is already a `textarea` (`rows={2}`), so typed line breaks are saved.
* The Excel export wraps column E and keeps the line breaks.

## Gaps

1. **Screens collapse the line breaks.** The Consolidated AIP grid, the review tree and activity modal, and AIP Records render the name without `whitespace-pre-line`, so a multi-line name reads as one run-on line.
2. **The entry box stays 2 rows** and scrolls inside itself for a long name.
3. **Excel caps a row at 409 pt** — about 30 lines of Arial Narrow 10 at column E's width (38). A longer name is clipped on the printed form even though the cell holds it all.

## Proposed scope

1. **Honour line breaks wherever the activity name is shown** — `whitespace-pre-line` (or equivalent) on the Consolidated AIP grid, review tree/modal, AIP Records.
2. **Auto-growing name textarea** on AIP Entry (with a sensible max height, then scroll).
3. **Warn, don't cap** — a character counter, and a soft warning once a name would pass Excel's printable row height (≈1,000 characters; confirm against column E). No hard limit: the province's own FY2027 file has names this long, so a cap would reject real data.
4. **No separate "details" field for now.** Annex B has one description column, so a split field would be joined back together on print — a migration for no change in output. Revisit only if PPDC wants the positions list structured.

## Out of scope

* Changing column widths in the export (they copy the province's file).
* Program/project names — check whether they need the same treatment while in there; file separately if so.

## Acceptance

- [ ] A name typed with line breaks in AIP Entry shows those breaks on the Consolidated AIP grid, the review screens and AIP Records
- [ ] The name box grows as the name gets longer
- [ ] A name past the printable threshold shows a warning but still saves
- [ ] The same name in the downloaded Excel shows its line breaks

## Manual-coding candidate

Items 1–2 are a good hand-coding candidate for Ralph: small, frontend-only, reversible, with a sibling to copy (`AipComments.tsx` already uses a textarea). Not item 3's threshold maths without a test.
