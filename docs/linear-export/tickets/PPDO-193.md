---
id: PPDO-193
title: 'V18-71.4c — Activity DELETE checks rowVersion (409 on a stale delete)'
status: Done
project: PPDO Portal
labels: 
milestone: v1.8.2 — Dashboard Redesign, Caching & Remaining Optimizations
created: 2026-10-07
completed: 2026-10-07
parent: PPDO-116
blocked_by: (none)
related: PPDO-191
url: https://linear.app/ralphoksiprojects/issue/PPDO-193
uuid: 58827643-6ad3-431e-ae2a-bc081a814a31
---

# PPDO-193 — V18-71.4c — Activity DELETE checks rowVersion (409 on a stale delete)

Child of [PPDO-116](<https://linear.app/ralphoksiprojects/issue/PPDO-116>). **Blocks** [PPDO-121](https://linear.app/ralphoksiprojects/issue/PPDO-121/v18-715-make-rowversion-required-deploy-3-the-step-that-turns-the)**.** Split out of [PPDO-121](https://linear.app/ralphoksiprojects/issue/PPDO-121/v18-715-make-rowversion-required-deploy-3-the-step-that-turns-the) §4c on 2026-10-07 so it ships at least one deploy **before** the 400 switch and goes through the same warn-then-400 evidence window as every other AIP write.

## The gap (found 2026-10-06 while building [PPDO-191](https://linear.app/ralphoksiprojects/issue/PPDO-191/v18-714b-wire-rowversion-into-aip-entry-and-the-wfp-is-creation-toggle))

Spec §4 lists `DELETE /api/budget-planning/aip/activities/{id}` as guarded (activity row), but nothing checks it:

* `AipFunctions.DeleteActivity` reads no `rowVersion`.
* `AipService.DeleteActivityAsync(activityId, caller, ct)` takes no version.
* `lib/aip.ts` `deleteAipActivity(activityId)` sends none. Called from `AipActivityRow.tsx` (detail page) and `entry/AipDeleteNodeButton.tsx` (AIP Entry tree).

**Effect today:** Ana edits activity #12; Ben, on a stale copy, deletes it. Ben's delete succeeds and Ana's edit is lost with no conflict panel. Expenditure DELETE is guarded; activity DELETE is not.

## Target

* `DELETE …/activities/{id}?rowVersion=<base64>` — the same query-string pattern as expenditure DELETE (a DELETE has no body).
* Stale version → **409** with the standard activity conflict payload `{ current, currentRowVersion, changedByName, changedAtUtc }`; nothing deleted.
* Missing version → LogWarning + proceed (same as every other write until [PPDO-121](https://linear.app/ralphoksiprojects/issue/PPDO-121/v18-715-make-rowversion-required-deploy-3-the-step-that-turns-the)). Malformed base64 → 400.
* Check order unchanged: existence → permission → version, so a caller without permission and a stale version gets **403**, never 409.
* Both callers send the version they hold and show `AipConflictPanel` on a 409 ("Delete it" vs "Keep it"), the pattern of the expenditure-line delete in `AipExpenditureTable.tsx`. Overwrite retries with `conflict.currentRowVersion`.
* Note: an expenditure write also bumps the activity's version (totals recompute), so a stale activity version also catches "someone edited a line under it". Intended.

## Verify

Tests: stale → 409 and the row (and its expenditures) still exist; matching → deleted; missing → deleted + warning; no permission + stale → 403; not found → 404. Red-test the version check and the ordering.
Live: two sessions, edit in one, delete from the stale one → conflict panel, not a silent delete — on the detail page and in the AIP Entry tree.

## Out of scope

Making `rowVersion` required ([PPDO-121](https://linear.app/ralphoksiprojects/issue/PPDO-121/v18-715-make-rowversion-required-deploy-3-the-step-that-turns-the)). Program, project or office deletes. Changing `AipConflictPanel`.
