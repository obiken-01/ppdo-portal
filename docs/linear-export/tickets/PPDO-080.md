---
id: PPDO-80
title: 'AIP Entry: fund sources, PS/MOOE/CO + CC totals on the row, form order, unit label, office default'
status: Done
project: PPDO Portal
labels: 
milestone: 
created: 2026-09-08
completed: 2026-09-08
parent: PPDO-48
blocked_by: (none)
related: PPDO-81, PPDO-100
url: https://linear.app/ralphoksiprojects/issue/PPDO-80
uuid: 5291c087-e56d-476b-9998-696beb161c8f
---

# PPDO-80 — AIP Entry: fund sources, PS/MOOE/CO + CC totals on the row, form order, unit label, office default

Five AIP Entry refinements asked for after PR #314 merged, before Phase 4 continues. All of them move the entry surface closer to the printed Annex B form the encoder reconciles against.

## 1. Show the fund sources an activity uses

An activity's funds live on its **expenditure lines** (one fund per line, Phase 2 decision 4), and the collapsed tree does not load lines — so the activity header has no way to show them today. Add the distinct fund codes to the activity DTO and render them joined with `/` (e.g. `GF/GAD Fund`), matching column (7) of the form, which prints `5% CF/ NGA`.

* Order is **first-use order** (the line the code first appears on), not alphabetical.
* FY≤2027 uploaded activities have no lines — fall back to the activity's own `fundingSourceSnapshot`.
* The codes must also come back on an **expenditure write** and on a **details save**, or the header goes blank after an edit (the tree is patched in place, never reloaded — see the entry page's `patchActivity` note).

## 2. Reorder the activity details form to the AIP form's column order

Description (2) → eSRE code → Implementing office (3) → Start (4) → End (5) → Expected outputs (6) → CC adaptation (12) → CC mitigation (13) → CC typology code (14). Applies to both the read and edit views, which must stay in the same order as each other.

## 3. PS / MOOE / CO and CC adaptation / mitigation totals on the row

On the **activity** header row and the **office** (sub-office group) header row only. Program and project rows stay blank, which is what the printed form does. Office figures are summed client-side from the tree — every activity in the group is already loaded, so this needs no endpoint.

## 4. `(₱000)` → `(In Thousand Pesos)`

On the AIP Entry surfaces only — the activity details read labels, the expenditure table header, and the submit checklist's ceiling figures. LDIP and the AIP detail page are out of scope.

## 5. Implementing office defaults to the user's own office

Prefilled with the user's **office code** (`PPDO`, `OPV`), matching what the form prints, and freely editable — an activity implemented jointly is entered as `OPV/LFC/HRMO` in the real document. Set on activity creation and prefilled in the edit form when the field is empty.

## Out of scope

* The AIP detail page (`aip/detail`) and LDIP keep their current labels and layout.
* No rollup of figures to program/project rows.
* No change to how funds are stored — one fund per expenditure line stands.
