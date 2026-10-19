---
id: PPDO-61
title: 'Reverse the office-owned AIP record shape — one base record per fiscal year'
status: Done
project: PPDO Portal
labels: 
milestone: v1.8.0 — Office Users, AIP Redesign & Reviewer Flow
created: 2026-09-05
completed: 2026-09-05
parent: PPDO-48
blocked_by: (none)
related: PPDO-41, PPDO-46, PPDO-49, PPDO-52, PPDO-60, PPDO-64, PPDO-65, PPDO-67, PPDO-73, PPDO-81
url: https://linear.app/ralphoksiprojects/issue/PPDO-61
uuid: 39f80f99-53e9-4581-a346-5fd1e8fce0bc
---

# PPDO-61 — Reverse the office-owned AIP record shape — one base record per fiscal year

**In one line:** undo V18-40's office-owned record shape — every fiscal year uses one base AIP record holding every office.

**Spec:** `docs/v1.8/AIP_Foundation_Spec.md` §2 decision 4 and §5.5 (both revised 2026-09-05) · **Size:** M · **Blocks:** PPDO-62, PPDO-63, [PPDO-52](https://linear.app/ralphoksiprojects/issue/PPDO-52/v18-42-three-stage-aip-entry-ui-sub-office-group-projectactivity)

## ⚠️ MIGRATION — and it is a *drop*, not a down-migration

`AddAipRecordOwningOffice` (`20260903045149`) **has never run against production.** v1.8.0 has not deployed. So delete the migration file rather than writing a reversal, and let the model snapshot regenerate. That is the whole reason this reversal is cheap and is being done now.

⚠️ **It HAS run against the local dev DB.** Dropping the file does not undo that — the dev database needs the column dropped by hand, or the DB rebuilt. Say which you did in the PR.

## Why

V18-40's office-owned shape was an engineering inference from the §12.6 workflow: per-office states looked like they implied a per-office record. The inference was wrong about **where** the state lives, not about the state. `AipOffice` already carries the ownership FK, so per-office status, locking and submission all work on the office row.

Ralph settled the model 2026-09-05: **one base record per FY holding every office**, opened by an Admin, populated from each office's LDIP, then filled in by the offices.

## Scope

| Remove | Keep |
| -- | -- |
| `AipRecord.OfficeId` + its migration + the `(office_id, fiscal_year)` index | `AipOffice.OfficeId` — **now the only carrier of office identity** |
| `AipShape.Required` / `.Of` / `.Mismatch` / `.RefuseForeignOffice` | `AipShape.FirstOfficeOwnedFiscalYear` — see below |
| The shape-aware create guard (`GetByOfficeAndFiscalYearAsync` branch) | `AipReadScope`, `AipOfficeOwnership`, the write guards ([PPDO-46](https://linear.app/ralphoksiprojects/issue/PPDO-46/aip-write-paths-have-no-office-ownership-check-cross-office-write-gap)) |
| `AddOfficeAsync`'s foreign-office gate | `AipShape.RefuseUpload` — behaviour unchanged |

The create guard reverts to **one question per year**: is there a non-Archived AIP for FY N?

## ⚠️ The constant survives, and should be renamed

`FirstOfficeOwnedFiscalYear` no longer names a *shape* — it names **the first year on the new process**: entered not uploaded, no WFP built in this portal. Two shipped guards read it and both keep their behaviour:

* `AipShape.RefuseUpload` (V18-38 / [PPDO-41](https://linear.app/ralphoksiprojects/issue/PPDO-41/v18-38-freeze-xlsm-aip-upload-to-historical-fiscal-years)) — rationale rewords from *"the importer can only produce the old shape"* to *"FY2028 is entered, not uploaded"*
* `WfpSupportedYears.RefuseCreate` (V18-81 / [PPDO-49](https://linear.app/ralphoksiprojects/issue/PPDO-49/v18-81-block-fy2028-wfp-creation-as-not-supported-yet))

Suggested name: `FirstEnteredFiscalYear`, or move it to a type that is no longer called `AipShape` once the shape logic is gone. **Keep the one-place-only rule and its build-failing scan exactly as they are** — it now covers `frontend/src` too.

## ⚠️ Tests to expect

V18-37's partition tests (`AipFyPartitionTests`) largely go with the partition. **Do not simply delete them** — read each one first and ask whether it pins something that still matters. Two already needed replacing rather than deleting during [PPDO-51](https://linear.app/ralphoksiprojects/issue/PPDO-51/v18-41-aip-programs-sourced-from-a-valid-ldip-closed-list), for exactly this reason.

## Done when

* No `AipRecord.OfficeId` anywhere; the migration file is gone and the snapshot regenerated
* An FY2028 base record with a null owner is created and read normally — it is now the correct shape
* Create still refuses a second active record for a year that already has one
* `AipReadScope` is untouched and its tests still pass
* The break-year constant reads correctly for its new meaning, and the one-place scan still passes
* [PPDO-41](https://linear.app/ralphoksiprojects/issue/PPDO-41/v18-38-freeze-xlsm-aip-upload-to-historical-fiscal-years)'s and [PPDO-49](https://linear.app/ralphoksiprojects/issue/PPDO-49/v18-81-block-fy2028-wfp-creation-as-not-supported-yet)'s behaviour is unchanged — their tests pass without edits

## Out of scope

The year-opening action (PPDO-62) · removing carry-forward (PPDO-63) · `AipOffice.OfficeId` gaps ([PPDO-60](https://linear.app/ralphoksiprojects/issue/PPDO-60/aipofficeofficeid-is-never-set-outside-the-importer-manually-added)).
