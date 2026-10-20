---
id: PPDO-147
title: 'Demo 2.8 T1 — Data model: aip_activities.division_id + aip_division_submissions (⚠️ migration)'
status: Done
project: PPDO Portal
labels: 
milestone: Demo 2 — change requests & fixes
created: 2026-09-28
completed: 2026-09-28
parent: PPDO-130
blocked_by: (none)
related: (none)
url: https://linear.app/ralphoksiprojects/issue/PPDO-147
uuid: f69f84d7-2fc8-4498-8d82-485089fe5958
---

# PPDO-147 — Demo 2.8 T1 — Data model: aip_activities.division_id + aip_division_submissions (⚠️ migration)

Spec: `docs/v1.8/Division_Submit_Spec.md` §5, §8 (merged in [obiken-01/ppdo-portal#386](https://linear.app/ralphoksiprojects/review/docsv18-spec-division-heads-submitting-to-the-department-head-ppdo-130-504d29c87106)).

## Scope

* `aip_activities.division_id` (int, nullable). It's an FK to `divisions` with `ON DELETE NO ACTION`, plus an index.
* New table `aip_division_submissions` (snake_case) and entity `AipDivisionSubmission` with its configuration. There is a unique index on `(aip_record_id, division_id)`, a CHECK constraint on `status` (Draft/Submitted), and FKs per §5. No row means Draft.
* Repository methods scoped to `(aipRecordId, officeId)`: one query per office, never one per division.
* `AuditAction`: `SubmitDivision`, `ReturnDivision`, `RetagActivityDivision`.
* **Backfill in the same migration:** for FY ≥ 2028 only, set the tag from `program_divisions` when the activity's program has exactly one assignment for its office. Everything else stays null.

## ⚠️ MIGRATION

* Migration `AddAipDivisionSubmit`. Apply it manually with `dotnet ef database update`, **before** the code deploys (CI does not run migrations).
* Add it to `docs/v1.8/Pre_Deployment_Checklist.md`, rechecking the count (expected to be migration no. 26).

## Tests

Backfill test on a seeded context:

* one assignment → tagged
* two assignments → null
* FY2027 → untouched

Not a manual-implementation candidate: EF migration with a backfill.
