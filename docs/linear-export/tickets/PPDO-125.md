---
id: PPDO-125
title: 'Demo 2.3 — CC typology should be a lookup, and the config needs descriptions'
status: Done
project: PPDO Portal
labels: 
milestone: Demo 2 — change requests & fixes
created: 2026-09-23
completed: 2026-09-23
parent: PPDO-122
blocked_by: (none)
related: PPDO-139, PPDO-172
url: https://linear.app/ralphoksiprojects/issue/PPDO-125
uuid: 9716a29e-4de9-4a0a-b0e8-e277a543eda6
---

# PPDO-125 — Demo 2.3 — CC typology should be a lookup, and the config needs descriptions

*"Add proper description to the CC config and use this in a lookup field for the CC typology field."*

Two halves, one of which is data rather than code.

## The field is free text today

Same three editors as Demo 2.2, same pattern — a bare `<input>`:

```tsx
<input value={ccTypologyCode} onChange={(e) => setCcTypologyCode(e.target.value)} />
```

`listCcTypologies()` and a `/config/cc-typologies` CRUD page already exist. Nothing connects them, so the code an encoder types is unvalidated and the config table is decorative.

## The description column already exists

`ClimateChangeTypologyResponse` already carries `code`, `name`, `category` and `description`. **No schema change** — "add proper description" means populating the rows, which is a data task for whoever owns the CC typology list, not a migration.

Worth confirming before building the lookup: are the current rows' descriptions empty, or just unhelpful? The answer changes whether this is a seed script or a content pass.

## Lookup shape

Show `code — name` in the list with the description as helper text on the selected value; a bare code means nothing to an encoder choosing between typologies. If the list is long, a searchable picker rather than a plain `<select>`.

## Acceptance

- [ ] CC typology is chosen from config, not typed
- [ ] The selected typology's description is visible at the point of choosing
- [ ] Existing free-text values still render, including ones matching no config row
- [ ] Deactivated typologies are not offered

Shares its three files with Demo 2.2 — do both in one PR.
