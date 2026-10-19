---
id: PPDO-191
title: 'V18-71.4b — Wire rowVersion into AIP Entry and the WFP is-creation toggle'
status: Done
project: PPDO Portal
labels: 
milestone: v1.8.2 — Dashboard Redesign, Caching & Remaining Optimizations
created: 2026-10-05
completed: 2026-10-06
parent: PPDO-116
blocked_by: (none)
related: PPDO-117, PPDO-119, PPDO-120, PPDO-192, PPDO-193
url: https://linear.app/ralphoksiprojects/issue/PPDO-191
uuid: 65ddfb2b-a936-4a08-a136-6d7d207ae3dc
---

# PPDO-191 — V18-71.4b — Wire rowVersion into AIP Entry and the WFP is-creation toggle

Child of [PPDO-116](<https://linear.app/ralphoksiprojects/issue/PPDO-116>). **Blocks [PPDO-121](<https://linear.app/ralphoksiprojects/issue/PPDO-121>)**, the step that turns the guard on. Written 2026-10-05 against `origin/feature/v1.8.1-v18-71-concurrent-edit` @ `87adefb`.

[PPDO-120](https://linear.app/ralphoksiprojects/issue/PPDO-120/v18-714-conflict-panel-in-aip-entry-deploy-2) wired the conflict flow into one surface (the detail page's inline editor). AIP Entry, the screen encoders actually use, and the WFP is-creation toggle still send no `rowVersion`, so two encoders colliding there still get silent last-write-wins. This ticket wires them.

## 0. ⚠️ Read first — two things the [PPDO-121](https://linear.app/ralphoksiprojects/issue/PPDO-121/v18-715-make-rowversion-required-deploy-3-the-step-that-turns-the) text does not say

**A. V18-71's code is not on any release branch.** [PPDO-117](https://linear.app/ralphoksiprojects/issue/PPDO-117/v18-711-concurrency-columns-on-aip-activities-and-aip-expenditures)…120 are Done in Linear and PRs [obiken-01/ppdo-portal#367](https://linear.app/ralphoksiprojects/review/feataip-add-concurrency-tokens-to-activities-and-expenditures-ppdo-117-b720c78049e1)–[obiken-01/ppdo-portal#370](https://linear.app/ralphoksiprojects/review/feataip-conflict-panel-for-concurrent-aip-edits-ppdo-120-9d51812ae2b6) merged, but into `feature/v1.8.1-v18-71-concurrent-edit`, not into `release/1.8.0` or `release/1.8.1`. That branch is **164 commits behind** `release/1.8.1` and 9 ahead. Checked 2026-10-05: neither release branch contains `AddAipConcurrencyTokens`, the AIP `rowVersion` plumbing, `aipConflict()` or `AipConflictPanel`. So nothing in [PPDO-116](https://linear.app/ralphoksiprojects/issue/PPDO-116/v18-71-concurrent-edit-guard-for-aip-entry-epic) has deployed anywhere, Deploy 1 and Deploy 2 have not happened, and this ticket cannot start from `release/1.8.2` until that branch is brought in. **Still open** — see §4 for the recommendation.

**B. A design gap that makes the guard fire on the user's own save.** Every expenditure write calls `AipActivityTotalsService.RecalculateAsync`, which rewrites the activity row's PS/MOOE/CO. That bumps the **activity's** `rowversion`. `AipExpenditureWriteResult` returns the new totals but **not** the new activity `rowVersion`. So once AIP Entry sends versions on activity-details saves:

1. Encoder saves an expenditure line, and the activity row's version changes server-side.
2. The page still holds the old activity version.
3. Encoder edits the activity description and saves, and gets a **409 against their own earlier change**, with no other editor involved.

The spec (`AIP_Concurrent_Edit_Spec.md` §4) covers the expenditure-to-parent-row nesting for procurement items but not the totals recompute.

✅ **Decided 2026-10-05 (Ralph): return** `activityRowVersion` **in** `AipExpenditureWriteResult` **and have the page store it.**

## 1. Context docs to read

Read `CLAUDE.md`, `PROJECT_DOCUMENTATION_NET_AZURE.md`, and `PPDO_PROJECT_CONTEXT.md`.
Read `docs/v1.8/AIP_Concurrent_Edit_Spec.md` **§4 (coverage table and check ordering), §6, §8 and §10 FULLY**. It lives on the V18-71 branch, not yet on `release/*`. It is the authoritative spec; this ticket amends it (step 1).
Read `docs/PERFORMANCE_GUIDELINES.md` (frontend loading-state rules) and `docs/DESIGN_SYSTEM.md` (the conflict panel is reused, not restyled).

## 2. Files to read before writing code

Repo-relative, on the V18-71 branch.

* `frontend/src/components/aip/AipActivityRow.tsx`: **the worked example.** `handleSave(rowVersion = act.rowVersion ?? null)`, the `aipConflict()` check *before* `aipErrorMessage`, `setConflict`, and `onOverwrite={() => handleSave(conflict.currentRowVersion)}`.
* `frontend/src/components/aip/AipConflictPanel.tsx`: the shared panel and its `fields` prop.
* `frontend/src/lib/aip.ts`: `aipConflict`, and the four calls to change: `updateAipActivityDetails`, `updateAipActivityIsCreation` (returns `void`, takes no version), `updateAipExpenditure`, `deleteAipExpenditure` (already accepts `rowVersion`; nobody passes it).
* `frontend/src/types/budget-planning.ts`: `rowVersion` already exists on the activity and expenditure types and the save requests. `AipExpenditureWriteResult` (\~L1305) does not carry the activity's version.
* `frontend/src/components/aip/entry/AipActivityFields.tsx`: the `updateAipActivityDetails` call (\~L113).
* `frontend/src/components/aip/entry/AipExpenditureTable.tsx`: `save()` (\~L310), `changeActivityFund()` (\~L341, loops over lines), `remove()` (\~L365).
* `frontend/src/app/(portal)/budget-planning/wfp/entry/page.tsx`: `updateAipActivityIsCreation` (\~L1171). 1,847-line page: **change only that call site and its state; do not refactor.**
* `backend/PPDO.Application/Services/AipExpenditureService.cs`, `AipActivityTotalsService.cs`, `backend/PPDO.Infrastructure/Repositories/AipRepository.cs` (`ApplyActivityTotalsAsync`, \~L97): where the activity version changes as a side effect.
* `backend/PPDO.Application/DTOs/BudgetPlanning/AipExpenditureDtos.cs`: `AipExpenditureWriteResultDto` (\~L136).
* `backend/PPDO.Functions/Functions/AipFunctions.cs`, `AipExpenditureFunctions.cs`: the optional-`rowVersion` branches from [PPDO-119](https://linear.app/ralphoksiprojects/issue/PPDO-119/v18-713-endpoints-accept-rowversion-and-map-the-409-deploy-1) (do not change them here).
* `backend/PPDO.Tests/Application/AipExpenditureConflictTests.cs`, `AipActivityTotalsServiceTests.cs`: test patterns to extend.

## 3. Current → target behaviour

State each in one line before changing anything.

* **Current:** The detail page's inline editor sends `rowVersion` and handles 409. AIP Entry's activity-details save, expenditure save/delete, the fund-change loop and the WFP is-creation toggle send none, so two encoders colliding in Entry still get silent last-write-wins. The expenditure write result does not return the activity's new version.
* **Target:** Every AIP write the app issues sends the correct `rowVersion` and handles a 409 through `aipConflict()` and `AipConflictPanel`. After any write, the page holds the **new** version of every row that write changed, so the user's own sequential saves never conflict.

## 4. Working branch + PR target

Depends on §0A. **Recommended:** once `release/1.8.2` exists, merge `feature/v1.8.1-v18-71-concurrent-edit` into it first, via its own PR. That is the "bring [PPDO-117](https://linear.app/ralphoksiprojects/issue/PPDO-117/v18-711-concurrency-columns-on-aip-activities-and-aip-expenditures)…120 into a release" step and it carries the `AddAipConcurrencyTokens` **⚠️ MIGRATION**. Then:

`Create feature/v1.8.2-ppdo-XXX-aip-rowversion-wiring off release/1.8.2 and open the PR against release/1.8.2 (NOT main). Never branch off uat.`

If work starts before `release/1.8.2` is cut: branch off the V18-71 branch after merging `release/1.8.1` into it, and say so in the PR.

## 5. TDD

Backend: extend `AipExpenditureConflictTests.cs` with failing tests first (step 2). The frontend has no unit-test harness for these components, so its verification is `tsc`, eslint and the two-browser checks in the steps below.

## 6. Implementation steps

1. **Spec amendment.** Add to spec §4 the totals-recompute rule: an expenditure write also changes the activity row's version, and the response returns it. Add the self-conflict case to the §10 checklist.
   *Verify:* a reader of §4 can predict that "save a line, then save the description" must not 409.
2. **Backend: return the activity's new version.** Add `ActivityRowVersion` (base64, same encoding as everywhere else) to `AipExpenditureWriteResultDto`, populated **after** the totals recompute so it is the post-write value. No migration.
   *Verify:* new test in `AipExpenditureConflictTests`: add, update and delete a line; assert the returned `ActivityRowVersion` differs from the pre-write one and equals what a fresh read of the activity returns. `dotnet test backend/PPDO.slnx --filter AipExpenditureConflictTests`. Existing tests stay green.
3. `lib/aip.ts` **+ types.** Add `activityRowVersion` to `AipExpenditureWriteResult`. Make `updateAipActivityDetails` and `updateAipExpenditure` carry `rowVersion` in the body (types already allow it). Make `updateAipActivityIsCreation(id, isCreation, rowVersion)` send it **and return the updated activity** instead of `void`, so the caller can store the new version.
   *Verify:* `cd frontend && npx tsc --noEmit` is clean. Every call site of the changed functions fails to compile until updated, which is the checklist for steps 4–6.
4. `AipActivityFields.tsx`**.** Send the activity's `rowVersion`; handle 409 exactly as `AipActivityRow` does (conflict first, never fall through to `setError`, never clear the form); Overwrite resubmits with `conflict.currentRowVersion`. Store the returned activity's new version.
   *Verify:* two browsers on the same activity, both edit the description. The second save shows the panel with the first user's text; Overwrite saves; Discard reloads.
5. `AipExpenditureTable.tsx`**.** Send each line's `rowVersion` on `save()`, `remove()` (`deleteAipExpenditure` already accepts it) and each iteration of `changeActivityFund()`. On every result, update the parent's copy of the **line** and of the **activity** version (from step 2).
   ⚠️ `changeActivityFund()` saves N lines in a loop but today calls `onChanged` only with the *last* result. The earlier N−1 lines keep stale versions in page state, so editing any of them afterwards would 409 against the user's own fund change. Update every line's version from its own result.
   ⚠️ If the loop hits a 409 on line k, lines 1…k−1 are already saved. Decide and document the behaviour (stop and show the panel for line k; do not roll back) and make the error text say how many lines changed.
   *Verify:* (a) two browsers, same line: the second save is stopped. (b) Change an activity's fund with 3 lines, then immediately edit line 1 and line 3 in the same session: no conflict. (c) Delete a line someone else edited: stopped.
6. **WFP entry page.** At the single `updateAipActivityIsCreation` call (\~L1171), pass the activity's version, store the version from the returned activity, and surface a 409 in the page's existing error pattern. Touch nothing else in the file.
   *Verify:* toggle new/continuing twice in a row on one activity: no conflict. Toggle in two browsers: the second is stopped.
7. **End-to-end on a deployed environment (UAT).** Run spec §10 **in full**, including the nesting case (two encoders editing only procurement items under one expenditure) and the new self-conflict case.
   *Verify:* every line of §10 ticked, copied into the PR body.

## 7. Risks, rollback, sign-off

| Risk | Rollback |
| -- | -- |
| Stale version held after a write, so the user conflicts with themselves (the §0B class; also the fund-change loop) | Steps 2 and 5 exist to prevent it. Rollback is a frontend revert: omitting `rowVersion` returns to the [PPDO-119](https://linear.app/ralphoksiprojects/issue/PPDO-119/v18-713-endpoints-accept-rowversion-and-map-the-409-deploy-1) warn-and-proceed behaviour, so the app keeps working, unprotected. |
| Backend DTO change breaks an old cached frontend | The new field is additive; an old bundle ignores it. |
| The V18-71 branch merge brings in `AddAipConcurrencyTokens` | ⚠️ Manual `dotnet ef database update` **before** the code deploys (spec §8) or every AIP write fails on a missing column. The down-migration is only safe before any rows are edited by the new code; after that, restore forward. |
| Conflict panel mis-states what changed on a partial fund-change failure | Covered by the step 5 decision; verify the message. |

**Stop and get sign-off before:** running the migration against Azure SQL; anything that touches `AipConflictPanel` (shared with the detail page). The `AipExpenditureWriteResultDto` change in step 2 is already approved (§0B).

## 8. Do NOT

* Do **not** make `rowVersion` required. That is [PPDO-121](https://linear.app/ralphoksiprojects/issue/PPDO-121/v18-715-make-rowversion-required-deploy-3-the-step-that-turns-the), and it must wait for the evidence window.
* Do **not** add versions to program, project or office renames (spec §2 follow-up, decided out).
* Do **not** give procurement items their own version; the unconditional `UpdatedAt` write on the expenditure is what protects them (spec §4 warning).
* Do **not** refactor `wfp/entry/page.tsx` or `AipExpenditureTable.tsx` beyond the call sites above.
* Do **not** put the version in a global store or fetch it separately; it comes from the row the page already holds.
* Do **not** show a 409 as an ordinary error message. It must reach the panel.

## 9. Commit message

```
feat(aip): send rowVersion from AIP Entry and the WFP is-creation toggle (V18-71)
```

## Manual coding

Mostly not a candidate: a stale or missing version is not caught by the compiler or the happy path, only by two browsers, and `AipExpenditureTable.tsx` is a large file with the N-line loop. **Step 4 (**`AipActivityFields.tsx`**)** fits the criteria, with `AipActivityRow.tsx` as the sibling. Guidance ladder level 1: spec plus sibling, no code.
