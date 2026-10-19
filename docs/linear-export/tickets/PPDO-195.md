---
id: PPDO-195
title: 'Our own UAT test pass on v1.8.2 using PTO as the test office (weekend 10–11 Oct)'
status: Done
project: PPDO Portal
labels: 
milestone: v1.8.2 — Dashboard Redesign, Caching & Remaining Optimizations
created: 2026-10-08
completed: 2026-10-11
parent: (none)
blocked_by: (none)
related: PPDO-121, PPDO-135, PPDO-148, PPDO-150, PPDO-155, PPDO-182, PPDO-196, PPDO-198, PPDO-200, PPDO-201, PPDO-202, PPDO-203, PPDO-204, PPDO-205, PPDO-206
url: https://linear.app/ralphoksiprojects/issue/PPDO-195
uuid: f57e2592-cb7e-4ade-bd79-5aa34dab9e69
---

# PPDO-195 — Our own UAT test pass on v1.8.2 using PTO as the test office (weekend 10–11 Oct)

## Why

The formal UAT round with the PDC and finance users hasn't started, and v1.8.3 has few tickets left. We use the gap to run the guest-office flow end to end in UAT ourselves, so problems surface before the finance users find them.

**Office: PTO (Provincial Treasurer's Office).** The finance users are testing with **SPO**. Don't touch SPO data.

**When:** Saturday 2026-10-10 or Sunday 2026-10-11.

**Build:** UAT is on v1.8.2 (#462 merged 2026-10-07; `AddAipConcurrencyTokens` applied first). Check the version in the sidebar before starting.

## Accounts Ralph creates in UAT

⚠️ Never put passwords on this ticket, in the repo, or in chat logs that get posted anywhere. Each account gets its own one-time password from the portal ([RAL-254](https://linear.app/ralphoksiprojects/issue/RAL-254/security-admin-reset-sets-a-random-password-forces-a-change-not-the)).

| Username (suggested) | Role | Office | Division | Overrides to set | What it tests |
| -- | -- | -- | -- | -- | -- |
| `pto.head.test` | Staff | PTO | none | `CanReviewBudgetPlanning` = true, `CanManageOfficeSetup` = true | Department head: office setup, assigning staff to divisions, division review/return/re-tag, submit to PPDO, reopen a final investment proposal |
| `pto.encoder1.test` | Staff | PTO | set by `pto.head.test` during the test ([PPDO-135](https://linear.app/ralphoksiprojects/issue/PPDO-135/demo-213-let-a-department-head-assign-divisions-to-their-own-offices)) | none | Encoder in division A |
| `pto.encoder2.test` | Staff | PTO | set by `pto.head.test` during the test | none | Encoder in division B: division lock, concurrent edit |
| `ppdo.reviewer.test` | Staff | PPDO (host) | any PPDO division | `CanReviewAllOffices` = true, `CanManageOfficeCeilings` = true | PPDO finance seat: sets PTO's ceiling, reviews/comments/returns/accepts PTO's submission. Mirrors the real finance users' grants |

**Labels in Edit User:**

| Account | Set to **Grant** | Leave everything else on Inherit |
| -- | -- | -- |
| `pto.head.test` | **Review Investment Planning (reviewer)** + **Set Up Own Office (department head)** |  |
| `pto.encoder1.test`, `pto.encoder2.test` | nothing |  |
| `ppdo.reviewer.test` | **Review All Offices (cross-office)** + **Set Office Ceilings (PPDO finance)** | ⚠️ not *Manage PPDO Allocation*, which is PPDO's own internal split |

Notes:

* **Staff, not Admin, for all four.** Admin/SuperAdmin bypass most scope rules, so an Admin account would hide exactly the leaks we're looking for.
* `CanAccessBudgetPlanning` defaults **on** for guest-office Staff (Permission Matrix §2.2). Leave it blank; don't set it.
* The PPDO reviewer account **can see SPO's submissions** (read only). Don't comment on, return or accept SPO.
* If PTO already has divisions in UAT, make sure there are at least two, so the division lock can be tested.

## Before the weekend: setup checklist (Ralph)

Without these, the test stalls on an empty state at step 3 or 4.

**Office and structure**

- [X] **PTO office** is active and has its **office ref code** set (`01-005`, verified 2026-10-08). Without it, the AIP summary shows "not in AIP" and the program picker is empty.
- [X] **At least two PTO divisions**, created ahead of time (done 2026-10-08; encoders were also pre-assigned, see step 2). The department head re-exercises assignment in step 2 instead, which is the [PPDO-135](https://linear.app/ralphoksiprojects/issue/PPDO-135/demo-213-let-a-department-head-assign-divisions-to-their-own-offices) path.

**Budget-planning data** (from `docs/UAT_PROVISIONING_STEPS.md` §5, for PTO specifically)

- [X] **A PTO LDIP covering FY2028**, with **at least two programs** (4 programs under `1000-000-1-01-005`, verified 2026-10-08), so each division can own one. AIP programs are seeded only from the LDIP. Without one, encoders get the "LDIP is the prerequisite" empty state.
- [X] **FY2028 is opened** (Admin), and **PTO appears in it** (on the dashboard board under Not started, verified 2026-10-08). If FY2028 was opened before PTO's LDIP existed, run the **re-sync**, otherwise PTO sees "office has no LDIP".
- [X] **Default fiscal year** (Configuration → Investment Planning settings) is 2028 (set 2026-10-04, verified 2026-10-08), or the test uses the picker. Check what SPO's testers expect before changing it; it's province-wide.
- [ ] **General Fund** is active and shared. Leave the ceiling unset: `ppdo.reviewer.test` sets it in step 3. PTO's own fund source is added by the department head in step 2.
- [ ] **Reference config** (accounts/ESRE, CC typology, price index) is in UAT. If SPO's testers can add expenditure lines, it is.

**Baseline noted 2026-10-08:** on the dashboard board PTO is under *Not started* with the red "no reviewer, cannot submit" dot. ✅ **Cleared once** `pto.head.test` **was created** (count went 17 → 16 no-reviewer offices), seen from `ppdo.reviewer.test`, which also sees Investment Planning and Office Ceilings in its menu. PTO's four programs already carry amounts (₱194,731.84 / ₱14,546.00 / ₱5,000.00 / ₱5,911.00); record them before the test so any change made by the test is visible.

**Accounts**

- [X] Create the four accounts above, and **sign in to each once** to replace the one-time password (both done 2026-10-08). That keeps password changes out of the test day.

**Monitoring and logistics**

- [X] **UAT App Insights is connected** (`ppdo-portal-api-uat`, Southeast Asia, receiving requests, verified 2026-10-08) (`APPLICATIONINSIGHTS_CONNECTION_STRING` set on the UAT Function App; it was optional at provisioning). Without it, step 5's concurrent-edit warning can't be seen. You also need portal access to run the query.
- [X] UAT shows **v1.8.2** in the sidebar.
- [ ] Two browsers ready: the browser pane plus Chrome (or an incognito window), for the two-person steps.
- [X] **Tell the finance users** that PTO test data will appear on the PPDO review board and in the consolidated view this weekend (told 2026-10-08).
- [X] **Cleanup decided: keep the PTO test values** (Ralph, 2026-10-08). Nothing is deleted after the test.

Optional: on Friday, sign in as an Admin in the browser pane and Claude checks the data items above (read only).

## Test data plan

Amounts are General Fund (shared). The FY2028 ceiling rule counts **MOOE + CO only, each rounded up to the thousand, PS exempt**.

**Divisions (Ralph creates before the weekend).** Mark them "(test)" so the finance users can tell them apart and cleanup is obvious:

|  | Code | Name | Encoder |
| -- | -- | -- | -- |
| A | `CASH` | Cash Management Division (test) | `pto.encoder1.test` |
| B | `REV` | Revenue Division (test) | `pto.encoder2.test` |
| C | `ACCT` | Accounts Division (test) | **created by** `pto.head.test` **during step 2** (tests the create path); stays empty |

**Ceiling (**`ppdo.reviewer.test`**, step 3, on the day):** GF FY2028 = **₱1,500,000**. Before setting it, write down PTO's current "costed" figure on its office dashboard; the four programs already hold amounts.

**Division allocation (**`pto.head.test`**, step 3):**

| Division | GF allocation |
| -- | -- |
| A — Cash | ₱900,000 |
| B — Revenue | ₱500,000 |
| C — Accounts | ₱0 |
| **Remaining** | **₱100,000**, so the Remaining figure is non-zero and visible |

Then try raising B to ₱700,000 (total ₱1,600,000 > ceiling). Note whether it's blocked or saved with an over-allocated warning; the dashboard has an over-allocated flag. Put B back to ₱500,000.

**Program → division assignment (**`pto.head.test`**, step 3):**

| Program | Assigned to | Why |
| -- | -- | -- |
| `…-01-005-001` Treasury Administrative and Support Services | A |  |
| `…-01-005-002` Revenue Intensification (RISE) | B |  |
| `…-01-005-003` Financial Integrity and Safekeeping of Treasury Funds | A |  |
| `…-01-005-004` Treasury Operations | **A and B** | a shared program; FY2028 counts each activity once, by its own division tag ([PPDO-150](https://linear.app/ralphoksiprojects/issue/PPDO-150/follow-up-attribute-the-aip-ceiling-ledger-by-activity-division-not)) |

**Activities (encoders, step 4):**

| Who | Program | Activity | GF lines | Tests |
| -- | -- | -- | -- | -- |
| encoder1 | 001 | "Test — office supplies" | MOOE ₱150,000 | normal entry |
| encoder1 | 004 | "Test — vault upgrade" | CO ₱200,000 | shared program, tagged A |
| encoder2 | 002 | "Test — collection drive" | MOOE ₱250,500 | **rounds up to ₱251,000** in the ceiling figure |
| encoder2 | 004 | "Test — queue system" | MOOE ₱100,000 | same shared program, tagged B; encoder1 must not be able to edit it |
| encoder2 | 002 | "Test — over-ceiling" | CO ₱1,500,000 | pushes PTO over the ceiling, so the **submit gate must block**. Then delete it |

The step 5 collision: both encoders open **"Test — office supplies"**. encoder2 can't edit it (division lock), so use a second browser signed in as `pto.head.test`. The department head may edit any division's work.

## Test cases (weekend run)

**Who:** **C** = Claude drives Chrome, after Ralph signs in as the named account. **R** = Ralph. **R+C** = two browsers at once.

| \# | Who | Account | Do | Expect |
| -- | -- | -- | -- | -- |
| T0 | R (Edge) | Ralph's SuperAdmin (test Staff have no Configuration access) | Open Configuration: Funding Sources, Accounts, Price Index | GF is active and shared; accounts and price index have rows |
| T1 | C | each PTO account | Open Dashboard, AIP Entry, AIP Review, Annex B | Only PTO data. **No SPO or other office** anywhere |
| T2 | C | `pto.head.test` | Add own fund source; create division `ACCT`; move encoder2 → ACCT → REV | All saves succeed; no user outside PTO is offered |
| T3 | C | `ppdo.reviewer.test` | Set PTO GF FY2028 ceiling ₱1,500,000 | Saved; PTO card shows the ceiling |
| T4 | C | `pto.head.test` | Allocate CASH ₱900K / REV ₱500K / ACCT ₱0; assign programs 001→A, 002→B, 003→A, 004→A+B. Then try REV ₱700K | Remaining = ₱100K. Over-allocation is blocked or flagged (record which). Then revert |
| T5 | C | encoder1, then encoder2 | Add the 4 activities from the data plan | Each saves under its own division; ₱250,500 counts as ₱251,000 |
| T6 | C | encoder1 | Try to edit encoder2's "queue system" activity | Refused: "belongs to REV" |
| T7 | C | encoder2 | Add ₱1,500,000 CO activity, try to submit | Submit **blocked** (over ceiling). Delete the activity |
| T8 | **R+C** | encoder1 (Chrome) + `pto.head.test` (an **Edge InPrivate** window) | Both open "office supplies", both edit, both save | 1st saves; 2nd gets the **conflict panel** |
| T9 | C | encoders → `pto.head.test` | Each division submits; head returns REV with a comment; encoder2 fixes and resubmits | Board: In progress → Office review; comment visible to encoder2 |
| T10 | C | `pto.head.test` → `ppdo.reviewer.test` | Head submits to PPDO; reviewer comments on a row, returns; head resubmits; reviewer accepts. Reviewer tries to edit a value | Board moves to PPDO review → Done. Reviewer edit **refused** (comment only) |
| T11 | C | head, then reviewer | Open Annex B and the consolidated view; check the notifications bell | PTO figures match AIP Entry totals; counts are right |
| T12 | C | encoder1 → `pto.head.test` | Finalize an investment proposal; head reopens it | Final → Draft; encoder can't reopen |
| T13 | R | Azure portal | Run the 3 App Insights queries | 0 "without rowVersion" rows; 1 × 409 from T8; **no 500s** |
| T14 | C + R | C: `pto.head.test` and `ppdo.reviewer.test` in Chrome. R: Azure portal | **Performance.** C reloads Dashboard, AIP Entry, AIP Review and Annex B, recording page load time, the slowest API call per page, and whether responses are compressed ([PPDO-182](https://linear.app/ralphoksiprojects/issue/PPDO-182/perf-compress-api-responses-spike-then-switch-on)). R runs query 4 below | Pages usable in **< 3 s** warm; no endpoint **p95 > 2 s**; JSON responses compressed. The first call after idle (cold start) is noted separately, not counted as a fail |

**Browsers:** **Chrome** = the test accounts (Claude drives). **Edge** = Ralph's SuperAdmin, **watch only** (dashboard board + Audit Log), plus the **Azure portal** for the App Insights queries (T13, T14). Claude works only in its own Chrome tab and never touches the Azure portal. It does no writes during the run, so every audit row and every conflict belongs to a test account. **Never sign the same account into two browsers at once**, so each test account lives in one place.

**Pass** = every Expect holds. Each miss → a bug ticket linked here.

## Test script (draft — Claude expands this into steps before the weekend)

 1. **Login and isolation.** Each PTO account lands on its office's pages and sees **no SPO or other-office data** anywhere: dashboard, AIP list, Annex B, notifications.
 2. **Office setup** (`pto.head.test`): add PTO's own fund source; create division C `ACCT`. The encoders are **already assigned** by an Admin, so to exercise the department-head path ([PPDO-135](https://linear.app/ralphoksiprojects/issue/PPDO-135/demo-213-let-a-department-head-assign-divisions-to-their-own-offices)), the head **moves encoder2 to C and back to B**, and confirms they can't assign a user outside PTO.
 3. **Ceiling** (`ppdo.reviewer.test`): set PTO's FY2028 ceiling. Then the department head does the division split and programme → division assignment.
 4. **AIP entry** (encoders): each encoder adds activities in their own division. Encoder2 can't edit A's activities ([PPDO-148](https://linear.app/ralphoksiprojects/issue/PPDO-148/demo-28-t2-activity-division-tagging-division-lock-in-the-write-guard) division lock).
 5. **Concurrent edit** (V18-71): two browsers open the **same activity**, and both edit it. The first save wins. The second save is **rejected with the conflict panel** (who saved last and when), and that request shows in App Insights as a **409**. Separately, **no** "AIP write without rowVersion" warning should appear for any of our saves; that warning is what [PPDO-121](https://linear.app/ralphoksiprojects/issue/PPDO-121/v18-715-make-rowversion-required-deploy-3-the-step-that-turns-the) waits on. Queries are in "Reading App Insights" below.
 6. **Division submit → department head**: return one division with a comment, re-tag an activity, then resubmit.
 7. **Submit to PPDO**, then `ppdo.reviewer.test` comments on a row, returns, PTO resubmits, reviewer accepts. Reviewer **can't edit content** (ReviewerWriteGuard).
 8. **Reports:** Annex B for PTO (department head view and PPDO view), consolidated view, submission history, notifications count.
 9. **Investment proposal:** encoder finalizes, department head reopens ([PPDO-155](https://linear.app/ralphoksiprojects/issue/PPDO-155/demo-215-t2-investment-proposal-service-api-lifecycle-scope)).
10. **Dashboard Offices band:** PTO's progress shows correctly for the reviewer.

Log each failure as a bug per `docs/BUG_REPORT_STANDARD.md` against v1.8.2. Screenshots go to chat only, never GitHub (the repo is public).

## Reading App Insights (on the day)

**Where:** Azure portal → `ppdo-portal-api-uat` (Application Insights) → **Logs** in the top bar. Close the query-pack popup, switch the editor from **Simple** to **KQL** mode, paste a query, and press **Run**.

⏱️ Data takes **2–5 minutes** to arrive, so wait before concluding "nothing there". Set the time range in each query to cover just our test session, because the SPO testers' traffic lands in the same tables.

1\. The missing-rowVersion warning ([PPDO-121](https://linear.app/ralphoksiprojects/issue/PPDO-121/v18-715-make-rowversion-required-deploy-3-the-step-that-turns-the)'s evidence). Expect zero rows from our saves.

```kusto
traces
| where timestamp > ago(3h)
| where message startswith "AIP write without rowVersion"
| project timestamp, message, customDimensions
```

A row here means some screen saved without sending a version. `customDimensions` names the entity, operation and UserId, so we can find which screen. That's a bug to file against [PPDO-121](https://linear.app/ralphoksiprojects/issue/PPDO-121/v18-715-make-rowversion-required-deploy-3-the-step-that-turns-the).

**2. The concurrent-edit conflict (step 5). Expect one 409 per collision we cause.**

```kusto
requests
| where timestamp > ago(3h)
| where resultCode == "409"
| project timestamp, name, url, resultCode, duration
```

**3. Anything that broke during the test. Run it at the end.**

```kusto
requests
| where timestamp > ago(3h)
| where toint(resultCode) >= 400   // NOT `success == false`: in classic `requests` success is a string, so that filter matches nothing (found 2026-10-11)
| summarize count() by name, resultCode
| order by count_ desc
```

400/403/409 can be expected (validation, forbidden-by-design, conflicts). **Any 500 is a bug.** Pair it with:

```kusto
exceptions
| where timestamp > ago(3h)
| project timestamp, type, outerMessage, operation_Name
```

**4. Performance (T14): slowest endpoints during our run.**

```kusto
requests
| where timestamp > ago(3h)
| summarize calls = count(), p50 = percentile(duration, 50), p95 = percentile(duration, 95), max = max(duration) by name
| order by p95 desc
| take 15
```

For comparison, production after [RAL-237](https://linear.app/ralphoksiprojects/issue/RAL-237/move-function-app-to-southeast-asia-fix-cross-region-latency) runs **\~0.4 s per request** and **\~400 ms** for the dashboard. UAT's database tier may differ from production, so treat the numbers as indicative and look for outliers rather than absolute values.

## How Claude can help on the day

Claude can't type passwords into UAT; that's only allowed on localhost. **So the passwords never need to be shared with Claude, in chat or anywhere else.** Ralph signs each account in, either in the browser pane or in Chrome, and Claude drives the steps from there. Steps 5 and 6 need two accounts signed in at once (two browsers).

## Afterward

- [X] PTO test data stays, as decided 2026-10-08 and already told to the finance users
- [ ] Disable the four test accounts, or keep them for the formal round
- [ ] Bugs filed and linked here
