---
id: PPDO-78
title: 'V18-82 — Readiness board: a Board / Table switch on the dashboard''s Offices band'
status: Done
project: PPDO Portal
labels: 
milestone: v1.8.0 — Office Users, AIP Redesign & Reviewer Flow
created: 2026-09-08
completed: 2026-09-14
parent: PPDO-67
blocked_by: PPDO-68
related: PPDO-74, PPDO-76, PPDO-79, PPDO-94, PPDO-169
url: https://linear.app/ralphoksiprojects/issue/PPDO-78
uuid: be8a7a3d-20d8-4519-94f6-40637a9b0e90
---

# PPDO-78 — V18-82 — Readiness board: a Board / Table switch on the dashboard's Offices band

**In one line:** a board of office cards answering "who is done and who is not" at a glance, living on the Budget Planning dashboard as a Board / Table switch on the Offices band, and doubling as the way into the AIP Review search.

**Spec:** `AIP_Review_Spec.md` decision 18, §3.3, §4, §6.3, §10, §11 · **Wireframe:** [https://claude.ai/code/artifact/899e569d-f1a8-4436-9445-1be53f4ecb45](<https://claude.ai/code/artifact/899e569d-f1a8-4436-9445-1be53f4ecb45>) (source `docs/v1.8/wireframes/readiness-board/`) · **Size:** M · **Blocked by:** [PPDO-68](https://linear.app/ralphoksiprojects/issue/PPDO-68/phase-4-spec-aip-review-specmd) (spec) · Raised by Ralph 2026-08-26; **re-placed and settled 2026-09-13**

> ↩️ **What changed on 2026-09-13.** This ticket first put the board *below the AIP Review search filters*, with a new `readiness-board` endpoint. It now lives in the **dashboard's Offices band**, fed by the existing `dashboard/offices` read. Two reasons:
>
> * **One office read instead of two.** That read already carries activities, ceiling, costed and reviewer. The dashboard requirements already record two office reads drifting apart (140 vs 139 activities for one office).
> * **The dashboard is where reviewers land.**
>
> It is a **switch, not a replacement**, because the table is the budget officer's ceiling workspace. [PPDO-76](https://linear.app/ralphoksiprojects/issue/PPDO-76/v18-75-query-first-review-page) is therefore no longer a blocker.

## Who sees what

| Caller | Offices band |
| -- | -- |
| Cross-office reviewer (`CanReviewAllOffices`), or SuperAdmin | Board / Table switch. **Board by default**; the **last choice is remembered** on that device |
| Budget officer (`CanManagePboCeiling`) only | **Table only, no switch** |
| Holds both grants | The switch, board by default |
| Office encoders, host-office users with no cross-office grant | No Offices band — unchanged (403) |

The remembered view uses `localStorage`, keyed by user id, so two people sharing a PC keep their own choice. A value the caller can't use falls back to their default.

## Columns — five, not six

| Column | Rule |
| -- | -- |
| **Not Started** | `Draft` (or no AIP office row yet) and **zero activities** |
| **In Progress** | `Draft` and one or more activities |
| **Office Review** | `DepartmentReview`, **or** `ReturnedByPpdo` **carrying a Returned badge** |
| **PPDO Review** | `SubmittedToPpdo` |
| **Done** | `Consolidated` |

⚠️ **"Returned by PPDO" is not a sixth column.** Returned work re-enters the department-review condition, so it sits in Office Review with a *Returned* badge.

✅ **Not Started vs In Progress is decided by activity count**, not by seeded programs. Submission states win over both.

⚠️ **The column is computed by the server** (`ReadinessColumn`), so it is tested in C# and the board and table cannot disagree.

## Not Started — a compact list

Each row shows the office code, the assigned program count, and a red dot when nobody in the office can submit, with a one-line legend under the list. Early in the season 17 of 19 offices sit here; as full cards they would make the board one tall column.

## Card contents — settled

1. Office code, **Host** badge, **Returned** badge
2. Office name, truncated
3. `N programs · N activities`
4. Costed against ceiling, **abbreviated** (`₱1.12M of ₱10M`); "No ceiling published" when there is none; danger-coloured when over. The table keeps exact pesos
5. *Over ceiling* / *Cannot submit* pills, only when they apply
6. Reviewer name, or "No reviewer — assign"

The assigned program count uses the **same office resolution** as `AllocationService.GetProgramAssignmentsAsync` (groups matched on `aip_offices.office_id`), but is counted inside the band's existing rollup query. ↩️ Calling that method per office would be ~95 round trips per dashboard load (changed while building, 2026-09-14; spec §6.3 updated).

⚠️ **No "% complete", deliberately.** There is no denominator for activities — *"we don't know how many activities will be created per-office … this is up to them"*. Column position is the signal and the counts are context. Do not let it drift into a progress bar.

## Clicking an office

A card **or a Not Started row** opens the AIP Review search filtered to that office. Not Started offices are clickable too: their LDIP programs are already in the AIP, so the search has rows to show.

## Data — extend the existing read

`GET /api/budget-planning/dashboard/offices?fiscalYear=` keeps its gate and scope. `OfficeSummaryDto` gains `ReadinessColumn`, `IsReturned` and `AssignedProgramCount`.

⚠️ **The table's Submission column becomes real.** `SubmissionStatus` has been the constant `Todo` "until Phase 4". Left that way, PPDO would read *Returned* on the board and *Todo* in the table. Derive it from the same workflow state:

* `Draft` → Todo
* `DepartmentReview` and `ReturnedByPpdo` → In progress
* `SubmittedToPpdo` → Review
* `Consolidated` → Done

## States

* **Loading:** a skeleton shaped like the active view.
* **Empty column:** "No offices", and the column stays in place.
* **Empty band and Error:** unchanged.

## Acceptance (from spec §10)

- [ ] Reviewer opens on Board; switching to Table and reloading keeps Table
- [ ] Budget officer sees the table with no switch
- [ ] An office encoder sees no Offices band
- [ ] An office with LDIP programs and zero activities sits in Not Started
- [ ] A returned office sits in Office Review with a Returned badge
- [ ] No card shows a percentage
- [ ] A card and a Not Started row each open the search filtered to that office
- [ ] Board column and table Submission agree for every office
- [ ] Accepting an office moves it to Done
