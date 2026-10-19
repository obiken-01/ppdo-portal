---
id: PPDO-121
title: 'V18-71.5 — Make rowVersion required (Deploy 3) — the step that turns the guard on'
status: Done
project: PPDO Portal
labels: 
milestone: v1.8.4 — Minor fixes
created: 2026-09-22
completed: 2026-10-14
parent: PPDO-116
blocked_by: PPDO-120, PPDO-191, PPDO-193
related: PPDO-119, PPDO-195, PPDO-214
url: https://linear.app/ralphoksiprojects/issue/PPDO-121
uuid: 3699e95d-67ff-43e3-a9a1-8e7a9dbe1abf
---

# PPDO-121 — V18-71.5 — Make rowVersion required (Deploy 3) — the step that turns the guard on

Child 5 of [PPDO-116](<https://linear.app/ralphoksiprojects/issue/PPDO-116>). **Deploy 3.** Blocked by [PPDO-120](<https://linear.app/ralphoksiprojects/issue/PPDO-120>).

## ⚠️ Read this before treating it as paperwork

Tiny diff, and it is the ticket that actually turns the protection on.

After child 4, the schema exists, the UI exists, the 409 works — and **a client that omits** `rowVersion` **still gets the old last-write-wins behaviour**, because child 3 deliberately made a missing version pass with a warning so old clients would not break during the rollout.

**A deployment that stops here has every visible part of the feature and none of the protection, and will look finished.** That is precisely why it is a separate ticket with its own name rather than a checkbox on child 3.

## 1. Context docs to read

Read `CLAUDE.md`. Read `docs/v1.8/AIP_Concurrent_Edit_Spec.md` **§8 FULLY** — the staged rollout and why a missing version must not be a pass.

## 2. Files to read before writing code

* `backend/PPDO.Functions/Functions/AipFunctions.cs` and `AipExpenditureFunctions.cs` — the warn-and-proceed branches child 3 added.
* `backend/PPDO.Application/Common/AipUnguardedWrite.cs` ([PPDO-193](https://linear.app/ralphoksiprojects/issue/PPDO-193/v18-714c-activity-delete-checks-rowversion-409-on-a-stale-delete)): the warning to search Application Insights for (step 1). Removing the warn-and-proceed path removes these calls too.

## 3. Current → target

**Current:** a write with no `rowVersion` logs a warning and proceeds unprotected.

**Target:** a write with no `rowVersion` returns **400**. The warning path is deleted.

## 4a. 🔴 HARD PREREQUISITE — four call sites still send no version (found 2026-09-22)

⚠️ **Applying this ticket today would break the AIP Entry page.** Read this before anything else.

[PPDO-120](<https://linear.app/ralphoksiprojects/issue/PPDO-120>) wired the conflict flow into **one** surface. Four other call sites across three files still send no `rowVersion`, and this ticket turns an absent one into a **400** — so every one of those saves would fail outright.

| File | Calls | Sends `rowVersion`? |
| -- | -- | -- |
| `components/aip/AipActivityRow.tsx` (detail page) | `updateAipActivity` | ✅ |
| `components/aip/entry/AipActivityFields.tsx` | `updateAipActivity`, `updateAipActivityDetails` | ❌ |
| `components/aip/entry/AipExpenditureTable.tsx` | `updateAipExpenditure`, `deleteAipExpenditure` | ❌ |
| `app/(portal)/budget-planning/wfp/entry/page.tsx` | `updateAipActivityIsCreation` | ❌ |

⚠️ **The unwired ones include AIP Entry — the screen the encoders actually use.** The guard currently protects the detail page's inline editor; two encoders colliding in Entry still get silent last-write-wins. The mechanism is built and proven, its coverage is one screen.

**So the real order is:**

1. Wire the three remaining files. A wiring exercise, not new mechanism — `aipConflict()`, `AipConflictPanel` and the `rowVersion` plumbing are all shared, and `AipActivityRow` is the worked example to copy.
2. Deploy, and only then start the evidence window below.
3. This ticket.

Step 1 is a prerequisite in the strict sense: without it, step 2's warning count can never reach zero, because the app itself is the thing still omitting the field.

> ✅ **Step 1 done 2026-10-06** — [PPDO-191](<https://linear.app/ralphoksiprojects/issue/PPDO-191>), PR [obiken-01/ppdo-portal#455](https://linear.app/ralphoksiprojects/review/feataip-send-rowversion-from-aip-entry-and-the-wfp-is-creation-toggle-9a7bb8ecefda) (merged into `release/1.8.2`). AIP Entry details, expenditure save/delete/fund change and the WFP is-creation toggle all send `rowVersion` now. The four rows marked ❌ above are wired. One gap remains, see §4c.

---

## 4b. 🔴 Entry condition — evidence, not a date

**Do not start until the child-3 warning has stopped appearing in production/UAT Application Insights for a sustained period after child 4 deployed.**

A lingering warning means something still saves without a version — a cached SWA bundle, a missed call site, a script. Flipping to 400 while that is true breaks a real user's save. The warning count reaching zero **is** the entry condition; there is no calendar answer.

*How to check:* App Insights → Logs → filter on child 3's warning message, last 7 days, after child 4's deploy timestamp.

> ⚠️ **Found 2026-10-07: child 3 never logged that warning.** [PPDO-119](https://linear.app/ralphoksiprojects/issue/PPDO-119/v18-713-endpoints-accept-rowversion-and-map-the-409-deploy-1) made `rowVersion` optional but wrote nothing when it was missing, so this count would have read zero from day one. [PPDO-193](https://linear.app/ralphoksiprojects/issue/PPDO-193/v18-714c-activity-delete-checks-rowversion-409-on-a-stale-delete) **adds it** (`AipUnguardedWrite`, on activity update/delete and expenditure update/delete). The query is:
>
> ```
> traces | where message startswith "AIP write without rowVersion"
> ```
>
> Entity, EntityId, Operation and UserId are in `customDimensions`. The window starts only once [PPDO-193](https://linear.app/ralphoksiprojects/issue/PPDO-193/v18-714c-activity-delete-checks-rowversion-409-on-a-stale-delete) is deployed. Before that the count proves nothing.

## 4d. ✅ Decided 2026-10-07 (Ralph): the evidence window is the UAT test round, not production

**Why UAT and not production:** production goes straight from v1.7.4 to v1.8.0–v1.8.3 in one deploy, so no v1.8.x screen is ever in use there before this ticket. The only warnings production could show are from tabs left open across that deploy, and those clear within a day. The real question, "does every save path send `rowVersion`", is answered by testers saving in UAT.

**Facts checked:**

* v1.8.2, including [PPDO-193](https://linear.app/ralphoksiprojects/issue/PPDO-193/v18-714c-activity-delete-checks-rowversion-409-on-a-stale-delete)'s warning, has been in UAT since #462 (2026-10-07).
* `ppdo-portal-api-uat` has `APPLICATIONINSIGHTS_CONNECTION_STRING` set (Ralph checked in the Portal), so the warnings are recorded.
* Testing had not started on 2026-10-07. An empty count before then proves nothing.

**What the warning covers** (the four `AipUnguardedWrite.WarnIfMissing` calls):

* activity update: inline edit (`UpdateActivityAsync`), AIP Entry details (`UpdateActivityDetailsAsync`) and the WFP is-creation toggle (`UpdateActivityIsCreationAsync`), all through `SaveActivityAsync`;
* activity delete;
* expenditure update and delete.

**Entry condition (replaces the production wording in §4b):**

1. After the testers' first round, run this against the Application Insights resource UAT logs to:

   ```
   traces
   | where cloud_RoleName =~ "ppdo-portal-api-uat"
   | where message startswith "AIP write without rowVersion"
   | summarize count() by tostring(customDimensions.Operation), tostring(customDimensions.Entity), bin(timestamp, 1d)
   ```

   The `cloud_RoleName` line keeps it correct even if UAT and production share one Application Insights resource.
2. **Start this ticket when** that returns zero **and** the testers have done each of the following in UAT:
   * edited an activity inline;
   * saved its details in AIP Entry;
   * edited and deleted an expenditure;
   * deleted an activity;
   * toggled is-creation in WFP.
3. **Any rows:** `Operation` + `Entity` name the path, and `customDimensions.UserId` names who. Fix that first.
4. **Production then only confirms.** After the v1.8.0–v1.8.3 deploy, expect a short tail from stale tabs and nothing after the first day. See `Pre_Deployment_Checklist_v1.8.2.md` §5.

## 4c. Activity DELETE version check → moved to [PPDO-193](https://linear.app/ralphoksiprojects/issue/PPDO-193/v18-714c-activity-delete-checks-rowversion-409-on-a-stale-delete)

The gap found 2026-10-06 (activity DELETE checked no version) is now tracked in [PPDO-193](https://linear.app/ralphoksiprojects/issue/PPDO-193/v18-714c-activity-delete-checks-rowversion-409-on-a-stale-delete). It must be merged **and deployed** before this ticket's evidence window starts, so the activity delete gets the same warn-then-400 rollout as every other write.

## 5. Working branch + PR target

Whatever the active integration branch is at pickup — likely **not** `release/1.8.0` by then. Confirm before branching, and never branch off `uat`.

## 6. Implementation steps

1. **Confirm the entry condition** (§4). Paste the App Insights result into the PR. If the count is not zero, **stop and find out why** — that investigation is the ticket, not this change.
2. **Make it required.** Missing or malformed `rowVersion` → **400** with a clear message. Delete the warn-and-proceed branch entirely; do not leave it behind a flag, or it will be switched back on during an incident and quietly stay on.
   *Verify:* a test per affected route asserting 400 when the field is absent. `dotnet test` green.
3. **Re-verify the ordering still holds.** A caller lacking permission **and** omitting the version must still get **403**, not 400 — the same oracle guard as child 3, now with one more branch that could preempt it.
   *Verify:* the child-3 ordering test still passes, plus a case with both faults.
4. **End-to-end.** Two browsers, the full conflict flow, on the deployed environment.
   *Verify:* the acceptance checklist in spec §10 — **all of it**, including the last line, "a request with no `rowVersion` gets 400".
5. **Close the epic.** [PPDO-116](<https://linear.app/ralphoksiprojects/issue/PPDO-116>) closes here, not at child 4.

## 7. Risks, rollback, sign-off

🛑 **Sign-off before merge.** Small diff, real blast radius: every AIP write endpoint gains a way to fail.

| Risk | Rollback |
| -- | -- |
| A client somewhere still omits the field and now 400s | Revert the commit — no migration involved, so this is a genuine one-step revert. Then find the caller, fix it, and re-run §4. |
| A cached SWA bundle serves the pre-child-4 frontend | Exactly what §4's evidence check exists to catch. Do not skip it because child 4 "definitely deployed". |
| Treated as a formality and skipped | Then V18-71 is not done, whatever the board says. The epic stays open. |

## 8. Out of scope — do NOT

* **Do NOT** add new behaviour. This ticket removes a temporary branch and nothing else.
* **Do NOT** keep the warn-and-proceed path behind a config flag.
* **Do NOT** extend coverage to programs, projects, offices or WFP. Separate decision, separate ticket.

## 9. Commit message

```
feat(aip): require rowVersion on AIP writes (V18-71)
```
