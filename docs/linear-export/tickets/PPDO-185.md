---
id: PPDO-185
title: 'Perf: leave empty fields out of the AIP detail and summary JSON'
status: Done
project: PPDO Portal
labels: 
milestone: v1.8.1 — Optimization & Dashboard Fixes
created: 2026-10-04
completed: 2026-10-04
parent: (none)
blocked_by: (none)
related: (none)
url: https://linear.app/ralphoksiprojects/issue/PPDO-185
uuid: a32af640-2009-4f7c-9109-d1bd2f3b9019
---

# PPDO-185 — Perf: leave empty fields out of the AIP detail and summary JSON

From the performance audit, 2026-10-04 (finding **O5**). Optimization ticket 4 of 8. Backend + frontend types, size S.

## Current behaviour

* **A fifth is empty:** on record 13 (2,375 activities), seven activity fields are `null`/`false` on almost every row: `isSynthetic`, `divisionName`, `divisionId`, `isCreation`, `ccMitigation`, `ccAdaptation`, `ccTypologyCode`. That is about 320 KB of the 1.5 MB detail response.

## Target behaviour

* **Backend:** `GET /aip/{id}` and `/aip/{id}/summary` write with `DefaultIgnoreCondition = WhenWritingNull`. Pass the options through `ConfigHttp.FromResultAsync`'s `options` parameter, not globally.
* **Frontend:** check every reader of these fields. A missing field must behave like `null` (`=== null` checks become `== null`, or the types become optional).

## Acceptance

- [ ] Record 13 detail drops by \~20% (uncompressed).
- [ ] AIP detail, AIP Entry, WFP Entry and the review modal render the same for a record with CC typology, divisions and synthetic rows.
- [ ] `npm run build` type-checks clean.

## Note

Worth less once compression is on (ticket 1); still cheaper to parse.
