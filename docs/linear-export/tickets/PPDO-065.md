---
id: PPDO-65
title: 'P3-b — close the ProgramDivision program-half FK'
status: Canceled
project: PPDO Portal
labels: 
milestone: v1.8.0 — Office Users, AIP Redesign & Reviewer Flow
created: 2026-09-07
completed: 2026-09-07
parent: PPDO-48
blocked_by: (none)
related: PPDO-1, PPDO-52, PPDO-61, PPDO-62
url: https://linear.app/ralphoksiprojects/issue/PPDO-65
uuid: 3ec148b8-4848-44b8-8ece-a66306b64854
---

# PPDO-65 — P3-b — close the ProgramDivision program-half FK

> ✅ **CLOSED WITH NO ACTION 2026-09-07 — and the fold-in below is withdrawn too.**
>
> The re-link was folded into [PPDO-52](https://linear.app/ralphoksiprojects/issue/PPDO-52/v18-42-three-stage-aip-entry-ui-sub-office-group-projectactivity) on the assumption that [PPDO-52](https://linear.app/ralphoksiprojects/issue/PPDO-52/v18-42-three-stage-aip-entry-ui-sub-office-group-projectactivity) would renumber program ref codes. **It does not.** Reading the code before building it showed AIP program ref codes are inherited verbatim from the LDIP (`RefCode = p.RefCode`, on both the seeding path and the new group path) and are never allocated or rewritten in the AIP. The numbering [PPDO-52](https://linear.app/ralphoksiprojects/issue/PPDO-52/v18-42-three-stage-aip-entry-ui-sub-office-group-projectactivity)'s acceptance list describes already exists in `LdipService.BuildHierarchy`, which is where programs come from.
>
> Ralph's call, 2026-09-07: **no renumbering in the AIP; codes keep matching the LDIP's.** So there is no trigger, no re-link, and `ProgramDivision` keeps matching a ref code that does not change. **P3-b needs nothing built.**
>
> Held in place by `AipProgramGroupTests`, whose fixture numbers LDIP programs `-003` and `-007` on purpose — a `-001`, `-002` fixture would pass whether codes were inherited or allocated. Spec decision 16 superseded, decision 18 added, §10 acceptance corrected.
>
> ---
>
> ❌ **Originally cancelled 2026-09-07 — folded into** [PPDO-52](https://linear.app/ralphoksiprojects/issue/PPDO-52/v18-42-three-stage-aip-entry-ui-sub-office-group-projectactivity) **as re-link-on-renumber. Nothing was implemented; there is no branch and no migration.**
>
> Ralph's call: **the ref-code string stays, and** [PPDO-52](https://linear.app/ralphoksiprojects/issue/PPDO-52/v18-42-three-stage-aip-entry-ui-sub-office-group-projectactivity) **re-links** `ProgramDivision` **when it renumbers.** Same transaction as the renumber — not a follow-up write, because a renumber that commits without its re-link leaves assignments pointing at codes no program carries, which is the original bug with extra steps.
>
> Spec corrected in **PR #305** (§2 decision 16, the P3-b row, §5.4, §9, §10, §11). §5.4's wrong recommendation is kept and marked rather than deleted, so the same conclusion is not reached again.
>
> The finding that killed this ticket is below, kept as the record.
>
> ---
>
> 🛑 **Why it was blocked — 2026-09-07, before any code was written.**
>
> Reading `ProgramDivision.cs` first (as this ticket's own §2 instructs) turned up a documented reason the program half must stay a string, which `AIP_Entry_Spec.md` **§5.4 does not address and this ticket therefore inherited**. The entity says, verbatim: *“Do not ‘finish the job’ by adding one.* [PPDO-1](https://linear.app/ralphoksiprojects/issue/PPDO-1/programdivision-replace-ref-code-string-keys-with-real-fks) *explored this and stopped here on purpose.”*
>
> **The spec answered only half the argument.** §5.4 says the string exists because a re-upload recreates programs with fresh IDs, and that FY2028+ has no upload — true. But there is a **second, independent** reason it missed:
>
> **The assignment is deliberately permanent across fiscal years** (`ProgramDivision.cs`, confirmed 2026-09-01 by [PPDO-1](https://linear.app/ralphoksiprojects/issue/PPDO-1/programdivision-replace-ref-code-string-keys-with-real-fks)): *“There is deliberately no fiscal year on this row: one assignment serves every FY whose program carries the same ref code.”*
>
> That is live in code, not just in a comment. `AllocationService.GetProgramAssignmentsAsync` scopes `ProgramDivision` rows by **office FK only** — no fiscal year — then matches them to the requested year's programs by `ProgramRefCode`. Since [PPDO-61](https://linear.app/ralphoksiprojects/issue/PPDO-61/reverse-the-office-owned-aip-record-shape-one-base-record-per-fiscal) there is **one AIP record per fiscal year**, so FY2029's `aip_programs` rows are different rows from FY2028's. **An FK to** `aip_programs.id` **pins the assignment to a single year**: assign in FY2028, and FY2029 comes back with no divisions assigned even though nothing changed. That is a worse and quieter bug than the one this ticket set out to fix.
>
> ## The defect has no live trigger today
>
> `UpdateProgramAsync` writes only `Name` and `FunctionBand`; `UpdateAipProgramRequest` carries only those two. `AipProgram.RefCode` **is set at creation and never edited by any path.** `DeleteProgramAsync` does not renumber siblings, and `RefCodeAllocator` says so explicitly: renumbering *“is a numbering question owned by V18-42”*.
>
> ## ⚠️ But [PPDO-52](https://linear.app/ralphoksiprojects/issue/PPDO-52/v18-42-three-stage-aip-entry-ui-sub-office-group-projectactivity) creates the trigger
>
> [PPDO-52](https://linear.app/ralphoksiprojects/issue/PPDO-52/v18-42-three-stage-aip-entry-ui-sub-office-group-projectactivity)'s acceptance list includes **“Removing a middle program renumbers without leaving a gap.”** Renumbering **changes ref codes** — and that is precisely when assignments start detaching. So the risk is real and imminent; it is just **created by** [PPDO-52](https://linear.app/ralphoksiprojects/issue/PPDO-52/v18-42-three-stage-aip-entry-ui-sub-office-group-projectactivity) **rather than pre-existing**, and the fix belongs with the renumbering that causes it.
>
> ## What this ticket should probably become
>
> **Re-link on renumber, not an FK.** When [PPDO-52](https://linear.app/ralphoksiprojects/issue/PPDO-52/v18-42-three-stage-aip-entry-ui-sub-office-group-projectactivity) renumbers a program's `RefCode`, update the matching `ProgramDivision.ProgramRefCode` rows in the same transaction. That keeps the cross-FY permanence rule intact, needs no migration, and fixes the actual failure. Needs Ralph's decision — see the alternatives below.
>
> **Nothing was implemented. No branch, no migration.** The rest of this description is the original prompt and is left intact for context; treat it as superseded until the shape is re-decided.

**In one line:** stop a program's division assignment from silently detaching when its ref code changes — before [PPDO-52](https://linear.app/ralphoksiprojects/issue/PPDO-52/v18-42-three-stage-aip-entry-ui-sub-office-group-projectactivity) makes that assignment the only thing deciding what a PPDO encoder can see.

**Spec:** `docs/v1.8/AIP_Entry_Spec.md` §5.4 and §2 decision 16 · **Size:** M · **Blocks:** [PPDO-52](https://linear.app/ralphoksiprojects/issue/PPDO-52/v18-42-three-stage-aip-entry-ui-sub-office-group-projectactivity) · **Blocked by:** —

✅ **P3-b answered 2026-09-07 — close it, and in its own ticket.** The spec's default said "yes, and this is the moment"; that is confirmed. What changed is the packaging: this lands **ahead of** [PPDO-52](https://linear.app/ralphoksiprojects/issue/PPDO-52/v18-42-three-stage-aip-entry-ui-sub-office-group-projectactivity) rather than inside it, because it is backend + a migration + a legacy path that must keep working, and [PPDO-52](https://linear.app/ralphoksiprojects/issue/PPDO-52/v18-42-three-stage-aip-entry-ui-sub-office-group-projectactivity) is already the largest item in the phase.

## The defect

`ProgramDivision` keys the program half on `ProgramRefCode` **(a string)**. [PPDO-1](https://linear.app/ralphoksiprojects/issue/PPDO-1/programdivision-replace-ref-code-string-keys-with-real-fks) converted the office half to a real FK and left this one. Change a program's ref code and its division assignment points at nothing.

**It fails silently, and that is the whole problem.** There is no error and no orphan row to notice — the program simply stops appearing for the division it belongs to. From [PPDO-52](https://linear.app/ralphoksiprojects/issue/PPDO-52/v18-42-three-stage-aip-entry-ui-sub-office-group-projectactivity) onward this assignment is **the sole carrier of PPDO's division visibility** (`Phase_Plan.md` V18-11, confirmed 2026-08-26 by tracker B12-b), so the symptom is an encoder who cannot see work that is theirs, with nothing in the logs.

## Why it is fixable now and was not before

[PPDO-1](https://linear.app/ralphoksiprojects/issue/PPDO-1/programdivision-replace-ref-code-string-keys-with-real-fks) kept the string deliberately: `ReplaceImportAsync` deletes and recreates the whole program subtree on re-upload with fresh surrogate IDs, so there was no durable identity to point at. **V18-38 froze the importer for FY2028+** — those years have no upload, so a stable program identity exists for the first time.

⚠️ **FY≤2027 stays re-uploadable.** The FK must tolerate the legacy path; this ticket does not get to assume every record is FY2028+.

## 1. Context docs to read

Read `CLAUDE.md` (EF Core rules and the snake_case convention for new columns) and `PROJECT_DOCUMENTATION_NET_AZURE.md`.
Read `docs/v1.8/AIP_Entry_Spec.md` §5.4 FULLY — it is the authority here — plus §2 decision 16.
Read `docs/v1.8/Phase_Plan.md` V18-11 for why this became load-bearing.
Read `docs/NAMING_CONVENTIONS.md` before adding the column.

## 2. Files to read before writing code

* `ProgramDivision` entity + its `IEntityTypeConfiguration` — the current string key and what already exists on the office half from [PPDO-1](https://linear.app/ralphoksiprojects/issue/PPDO-1/programdivision-replace-ref-code-string-keys-with-real-fks). [PPDO-1](https://linear.app/ralphoksiprojects/issue/PPDO-1/programdivision-replace-ref-code-string-keys-with-real-fks)'s office-side change is the pattern to copy; find its migration and read it first.
* `AipProgram` entity and config — the target of the new FK, and where `ProgramRefCode` is written.
* `ReplaceImportAsync` (the AIP importer) — **the one path that deletes and recreates programs.** Whatever this ticket does must survive it for FY≤2027.
* The AIP read-scope resolver and its tests (`AipReadScopeTests`) — the consumer that turns this assignment into "what a PPDO user sees".
* `seedAipProgramsFromLdip` / the year-opening path ([PPDO-62](https://linear.app/ralphoksiprojects/issue/PPDO-62/open-a-fiscal-year-admin-creates-the-base-aip-record-and-populates)) — the other writer of program rows, now the main one for FY2028+.

## 3. Current behaviour → target behaviour

**Current:** `ProgramDivision` finds its program by matching `ProgramRefCode` as text. A ref-code change orphans the assignment silently; a re-upload recreates programs with new IDs and the text match is what keeps assignments alive.

**Target:** the assignment survives a ref-code change, and re-upload behaviour for FY≤2027 is unchanged.

> Before writing any code, state in one sentence what `ReplaceImportAsync` does to `ProgramDivision` rows today. If that answer is "nothing", say so explicitly — it decides the migration's whole shape.

## 4. Working branch + PR target

Working branch: `release/1.8.0`. Create `feature/v1.8.0-ppdo-65-programdivision-program-fk` off `release/1.8.0` and open the PR against `release/1.8.0` **(NOT** `main`**)**.

## 5. TDD

Extend `AipReadScopeTests` (and the `ProgramDivision` tests, if any exist — check) with failing tests first:

* a ref-code change **keeps** the division assignment
* a PPDO encoder in division D sees programs assigned to D, and does **not** see others
* the division filter does **not** reach guest offices — it is host-office-only
* an FY≤2027 re-upload behaves exactly as it does today

⚠️ **Verify by mutation, not by assertion count** (§11). Phase 2 found a test that passed with its guard deleted. Delete the new FK handling and confirm exactly the intended tests go red.

## 6. Implementation steps

1. **Decide and write down the migration shape** before touching code: add `program_id` alongside `ProgramRefCode` and backfill by matching current codes, or replace the string outright. **Recommend keeping both during the transition** — the string is what the legacy re-upload path uses, and dropping it in the same migration couples this ticket to importer behaviour it does not otherwise touch. Put the reasoning in the PR body.
2. **⚠️ MIGRATION** — add the column + FK per `NAMING_CONVENTIONS.md` (snake_case), with the backfill.
   *Verify:* `dotnet ef migrations script` reads correctly; row counts before and after the backfill match, with any unmatched rows **reported, not dropped**.
3. Update the entity, its configuration and the repository query paths to resolve by FK.
   *Verify:* `dotnet test`.
4. Handle the FY≤2027 re-upload path explicitly — whatever `ReplaceImportAsync` does now, it must still do.
   *Verify:* re-upload an FY2027 record locally and confirm assignments behave exactly as on `release/1.8.0`.
5. Confirm the read scope resolves division visibility through the FK.
   *Verify:* `dotnet test --filter AipReadScope`, then log in as a PPDO encoder in one division and check the program list.

## 7. Risks, rollback, sign-off

⚠️ **Request explicit sign-off before running the migration** — it touches **division scope**, the guard that decides who sees whose data, and it carries a **backfill**. Both are on `TICKET_PROMPT_STANDARD.md` §7's sign-off list.

| Risk | Detail | Rollback |
| -- | -- | -- |
| **Backfill silently drops assignments** | Any `ProgramDivision` row whose `ProgramRefCode` no longer matches a program has nowhere to go. Dropping it looks identical to the bug being fixed | Report unmatched rows in the migration output and decide deliberately. **Snapshot the table before running it** — a down-migration cannot recreate rows the backfill discarded |
| **Over-tightening visibility** | A PPDO encoder who loses programs they used to see reads as data loss | `dotnet test --filter AipReadScope` plus the live check in step 5, before merge |
| **FY≤2027 re-upload regresses** | The legacy path is the reason the string existed | Step 4's live re-upload check |

**CI does not run EF migrations** — this needs a manual `dotnet ef database update` against Azure SQL at deploy, and that must reach the PR body.

## 8. Out of scope — do NOT

* **Changing what a division assignment *means*,** or adding a UI for assigning them — this is a key change only.
* **Touching the office half** — [PPDO-1](https://linear.app/ralphoksiprojects/issue/PPDO-1/programdivision-replace-ref-code-string-keys-with-real-fks) already did it; this ticket copies its pattern, it does not revisit it.
* **Removing or "fixing"** `ReplaceImportAsync` — V18-38 froze the importer deliberately.
* **Anything in** [PPDO-52](https://linear.app/ralphoksiprojects/issue/PPDO-52/v18-42-three-stage-aip-entry-ui-sub-office-group-projectactivity) — no entry UI, no `workflow_status`.

## Done when

* A program ref-code change leaves its division assignment intact
* FY≤2027 re-upload behaves identically to `release/1.8.0`
* `dotnet test` passes, and the new tests fail when the FK handling is removed
* The migration reports unmatched backfill rows rather than discarding them silently
* The PR body carries the ⚠️ MIGRATION note and the manual `dotnet ef database update` step

## Commit message

```
feat(aip): key ProgramDivision to the program by FK, not ref code (PPDO-65)
```
