---
id: PPDO-139
title: 'Finalize CC typology descriptions, then seed the final list via migration'
status: Done
project: PPDO Portal
labels: 
milestone: Demo 2 — change requests & fixes
created: 2026-09-24
completed: 2026-09-29
parent: PPDO-122
blocked_by: (none)
related: PPDO-125, PPDO-137, PPDO-172
url: https://linear.app/ralphoksiprojects/issue/PPDO-139
uuid: 0c8233db-3273-4e93-b95c-58f381b21680
---

# PPDO-139 — Finalize CC typology descriptions, then seed the final list via migration

Planned for the week of 2026-09-28 (Ralph).

## Part 1 — Ralph: finish the list

`cc_typologies_final.csv` (imported locally 2026-09-24): **452 rows, 237 descriptions blank** (166 Mitigation, 71 Adaptation — starting A611-01).

## Part 2 — seed migration from the finished CSV

* **Merge by code, not insert.** `AddClimateChangeTypologies` (20260901065639) already seeds every code found in the FY2027 AIP, with Name = the code. The new migration UPDATEs existing codes (name, category, description, is_active) and INSERTs the rest — one `MERGE … ON code`.
* **Generated from the CSV by a script**, never hand-typed; SQL-escape quotes. Commit the script beside the CSV so the seed can be regenerated.
* Names can now be up to 500 chars ([PPDO-137](https://linear.app/ralphoksiprojects/issue/PPDO-137/cc-typology-csv-import-fails-with-500-official-names-exceed-the-200)).
* Production: v1.8.0 not yet deployed, so the seed runs straight after table creation. UAT already has the table — the merge updates it in place.
* Add to `docs/v1.8/Pre_Deployment_Checklist.md` if it lands before the release.

## Decisions (defaults proposed)

1. **Codes in the DB but not in the final list** (mostly FY2027-derived): *proposed* — mark inactive; they leave the pickers but still render on historical AIP activities.
2. **Overwrite config-page edits made before deploy?** *Proposed* — yes; the CSV is the source of truth.

## Acceptance

- [ ] No blank descriptions (or blanks explicitly accepted)
- [ ] Fresh DB after `dotnet ef database update` holds exactly the final list
- [ ] Existing DB (local/UAT) converges to the same list with no duplicate codes
- [ ] Codes outside the list handled per decision 1
