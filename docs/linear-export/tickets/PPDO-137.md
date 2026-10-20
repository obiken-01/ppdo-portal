---
id: PPDO-137
title: 'CC typology CSV import fails with 500 — official names exceed the 200-char name column'
status: Done
project: PPDO Portal
labels: 
milestone: Demo 2 — change requests & fixes
created: 2026-09-24
completed: 2026-09-24
parent: PPDO-122
blocked_by: (none)
related: PPDO-139
url: https://linear.app/ralphoksiprojects/issue/PPDO-137
uuid: e8d3bd3e-e010-4b37-8969-fffa93235170
---

# PPDO-137 — CC typology CSV import fails with 500 — official names exceed the 200-char name column

Found 2026-09-24 while loading the final CC typology list (`cc_typologies_final.csv`, 452 rows).

## Symptom

Import toast: "An unexpected error occurred" (500). Server log: `String or binary data would be truncated in table 'climate_change_typologies', column 'name'`. Nothing was imported — the whole save rolls back.

## Cause

1. `climate_change_typologies.name` is `nvarchar(200)`. Seven official CCET titles are longer — A611-02 is 356 characters (also M515-01 307, A207-03 225, A440-02 211, M613-12 202, M715-01 202, M205-02 201).
2. `ClimateChangeTypologyService.Validate` checks required fields, category and the multi-code guard, but **no lengths** — so an over-long value reaches SQL and surfaces as a bare 500 instead of a named row error. Same gap on the Add/Edit form.

## Fix (decided: widen, keep the official wording)

* ⚠️ MIGRATION `WidenClimateChangeTypologyName`: `name` 200 → 500. Up only widens; Down fails if any name > 200 exists.
* `CodeMaxLength` / `NameMaxLength` constants on the entity, used by both the EF config and the validator.
* Validator checks code and name length → a row error on import, 400 on the form.

## Acceptance

- [ ] `cc_typologies_final.csv` imports all 452 rows
- [ ] A name over 500 chars is reported as "Row N: Name must be at most 500 characters", other rows still import
- [ ] Migration added to `docs/v1.8/Pre_Deployment_Checklist.md`
