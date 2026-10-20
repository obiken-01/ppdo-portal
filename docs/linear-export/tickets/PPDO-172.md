---
id: PPDO-172
title: 'CC typology code — searchable lookup instead of a 282-option dropdown'
status: Done
project: PPDO Portal
labels: 
milestone: Demo 2 — change requests & fixes
created: 2026-09-30
completed: 2026-09-30
parent: PPDO-122
blocked_by: (none)
related: PPDO-125, PPDO-130, PPDO-139
url: https://linear.app/ralphoksiprojects/issue/PPDO-172
uuid: 41f4820e-75c0-41c7-82cc-df3143f4da74
---

# PPDO-172 — CC typology code — searchable lookup instead of a 282-option dropdown

Raised by Ralph on 2026-09-30 during the Demo 2 test run. Since the LGU list was seeded ([PPDO-139](https://linear.app/ralphoksiprojects/issue/PPDO-139/finalize-cc-typology-descriptions-then-seed-the-final-list-via)), CC typology has 282 active codes, and the AIP editors show them in a plain `<select>` (`AipCodeSelect`, [PPDO-125](https://linear.app/ralphoksiprojects/issue/PPDO-125/demo-23-cc-typology-should-be-a-lookup-and-the-config-needs)) that can only be scrolled.

## Change

* New `AipCodeLookup`, wrapping the shared `Lookup` combobox. The encoder types a code ("A113") or a word from the name or description ("awareness") and picks from the matches. "—" clears the field.
* It keeps `AipCodeSelect`'s rule: **an already-saved code stays selectable**, shown as "(inactive)" when config no longer lists it, so a deactivated code or a failed config fetch can't silently blank the field on the next save.
* Used for CC typology in all three editors: AIP Entry Edit details (`AipActivityFields`), and the detail page's row edit and add-row (`AipActivityRow`, `AipAddActivityRow`). eSRE (4 codes) stays a select.

## Acceptance

- [ ] Typing "awareness" in CC typology lists the matching codes (A113-01, A713-01, …); picking one fills the field; Save stores the code
- [ ] An activity whose saved code is inactive shows "CODE (inactive)" and keeps it on save
- [ ] "—" clears the code
