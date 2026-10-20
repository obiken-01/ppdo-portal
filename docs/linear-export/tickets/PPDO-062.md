---
id: PPDO-62
title: 'Open a fiscal year — Admin creates the base AIP record and populates programs from every office''s LDIP'
status: Done
project: PPDO Portal
labels: 
milestone: v1.8.0 — Office Users, AIP Redesign & Reviewer Flow
created: 2026-09-05
completed: 2026-09-07
parent: PPDO-48
blocked_by: PPDO-61
related: PPDO-51, PPDO-60, PPDO-63, PPDO-65
url: https://linear.app/ralphoksiprojects/issue/PPDO-62
uuid: c091149c-1c98-4e44-8df6-b2133821e6f6
---

# PPDO-62 — Open a fiscal year — Admin creates the base AIP record and populates programs from every office's LDIP

**In one line:** one deliberate Admin action opens a fiscal year — creating the base AIP record and populating each office's programs from its own LDIP — so offices can start populating.

**Spec:** `docs/v1.8/AIP_Entry_Spec.md` §2 decision 12 · **Size:** M · **Blocked by:** [PPDO-61](https://linear.app/ralphoksiprojects/issue/PPDO-61/reverse-the-office-owned-aip-record-shape-one-base-record-per-fiscal)

## Why this exists

There is currently **no deliberate way to open a fiscal year**. Base records appear from four places — the Manual Entry tab, an `.xlsm` upload, and *as a side effect* of Carry Forward and Seed from LDIP. That side-effect creation is what made the missing model hard to notice.

Ralph's model (2026-09-05): an Admin opens the year once, programs arrive from each office's LDIP, and offices then populate their own subtree assuming PBO has set their ceiling.

## Scope

**1. The year-opening action.** Admin/SuperAdmin only. Creates the base `AipRecord` for the FY and, in the same operation, creates one `AipOffice` per configured active office with that office's LDIP programs as bare shells.

**2. Restrict create and archive.** Both `POST /budget-planning/aip` and the archive endpoint are gated on `CanAccessBudgetPlanning` today — **any** budget-planning user can open or archive a year. Archiving hides a whole year's work from every office at once.

⚠️ Use the **role** check `IsAdminOrAbove(user)`, not a new permission flag. A new flag needs a `Permission_Matrix.md` row and would fail `PermissionMatrixTests` without one (spec §2 decision 14).

**3. Seeding becomes a re-sync, not a create.** `SeedProgramsFromLdipAsync` stops find-or-creating a record. It requires the year to be open and adds only programs not already present — the one surviving case is a program added to an LDIP *after* the year was opened.

⚠️ **Reuse the existing LDIP resolution.** Two-tier (own records by sector, then multi-office uploads by computed ref code), already implemented on both the service and the panel. Do not re-derive it.

⚠️ **Set** `AipOffice.OfficeId` **on every row created.** `AipReadScope` filters on it, so a null makes the office's own programs invisible to it — no error, just absence. See [PPDO-60](https://linear.app/ralphoksiprojects/issue/PPDO-60/aipofficeofficeid-is-never-set-outside-the-importer-manually-added); after [PPDO-61](https://linear.app/ralphoksiprojects/issue/PPDO-61/reverse-the-office-owned-aip-record-shape-one-base-record-per-fiscal) this column is the *only* carrier of office identity.

## Behaviour to settle in the PR (defaults proposed)

| Question | Proposed default |
| -- | -- |
| Which offices get rows? | Every **active configured** office |
| An office with no LDIP at that moment? | **Skip it and report** — an empty office row is noise, but the Admin must be told which offices were skipped |
| Sectors? | Every sector the office's LDIP has a group for |
| Re-opening a year that already has a record? | Refused — one active record per FY |
| Amounts? | **None.** `LdipProgram.Budget` is a multi-year total, not a single-FY figure |

## Watch out

⚠️ **The skipped-offices report is the important output**, not a nicety. An office silently absent from the base record cannot build its AIP and has no way to discover why. Surface the list in the response and in the UI.

⚠️ **Seeding happens without the office touching anything**, which is why V18-82's kanban counts *activities*, not seeded programs, to decide Not Started vs In Progress.

## Done when

* An Admin can open FY2028 in one action; a non-Admin cannot, and is told why
* Every active office with an LDIP gets its programs; offices without one are named in the result
* A non-Admin cannot archive a record
* Re-syncing after an LDIP change adds only the new programs and leaves existing subtrees untouched
* No path creates a base record as a side effect any more
* Every created `AipOffice` carries its ownership FK

## Out of scope

The entry UI itself (V18-42 / [PPDO-52](https://linear.app/ralphoksiprojects/issue/PPDO-52/v18-42-three-stage-aip-entry-ui-sub-office-group-projectactivity)) · ceilings (V18-46/47) · removing carry-forward (PPDO-63).
