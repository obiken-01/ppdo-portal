---
id: PPDO-88
title: 'AIP delete: clear allocation ledger + comments, audit snapshot, renumber siblings (FY2028+)'
status: Done
project: PPDO Portal
labels: 
milestone: v1.8.0 — Office Users, AIP Redesign & Reviewer Flow
created: 2026-09-15
completed: 2026-09-15
parent: (none)
blocked_by: (none)
related: PPDO-101
url: https://linear.app/ralphoksiprojects/issue/PPDO-88
uuid: 069b0d6b-74c9-478d-8a85-9e87fdf4641a
---

# PPDO-88 — AIP delete: clear allocation ledger + comments, audit snapshot, renumber siblings (FY2028+)

**In one line:** make Project/Activity (and Program) delete correct and complete — today it can 500 — and renumber later siblings so ref codes have no gaps.

**Spec:** `docs/v1.8/AIP_Entry_Layout_Spec.md` §2 decisions 8–12a, §3 delete rows, §4, §11 · **Size:** M · **Blocks:** the delete-controls ticket

## Decided (Ralph, PDC meeting 2026-09-15)

* **Hard delete.** Recovery trail = an enriched audit-log snapshot (node, descendant activities, expenditure lines + amounts, removed comments).
* Encoder and department head; states Draft / DepartmentReview / ReturnedByPpdo (= existing `IsOfficeEditable`). No new permission — existing gates unchanged.
* Deleting a project deletes its activities; deleting either **deletes the comments** on everything removed (reverses the `AipReviewComment.NodeId` "left dangling" note).
* **Ref codes renumber** to close the gap; a renumbered project rewrites its activities' prefixes. **FY2028+ only** — FY≤2027 uploaded codes are the province file's. Programs never renumber.

## Bug this fixes

`aip_division_allocation_ledger.aip_activity_id` is `NoAction` and its config says the delete path clears those rows, but `IAipAllocationLedgerRepository.DeleteByActivityAsync` **has no caller**. Deleting an activity with a reservation — or its project/program — FK-fails.

## Scope

* One transaction via the EF execution strategy: guards → ledger rows → comments → snapshot → delete → renumber (two-phase temp codes to dodge the `(parent_id, ref_code)` unique index) → commit.
* Pure helper `RefCodeAllocator.Renumber(parentRefCode, siblings)`.
* DELETE project/activity/program return `ApiResponse<AipDeleteResultDto>` (`deletedNodeType, deletedId, removedActivityCount, removedCommentCount, renumbered[]`) instead of `bool`; unique-index race → **409**.
* No migration.

## Acceptance (from spec §10)

- [ ] Deleting activity `-002` of three renames the old `-003` to `-002`
- [ ] Deleting a project removes its activities; the next project's activities carry the new project segment
- [ ] Deleting an activity that holds a division allocation reservation succeeds and consumed allocation drops
- [ ] The deleted activity's comments are gone and the audit delete row lists them
- [ ] Deleting a middle activity on an FY2027 AIP leaves the gap

## Manual-coding candidate?

`RefCodeAllocator.Renumber` **alone — yes.** Pure, sibling `NextRefCode` in the same file, fully covered by `dotnet test`. The transaction, ledger and comment handling around it — no.
