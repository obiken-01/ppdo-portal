---
id: PPDO-74
title: 'V18-56 — PPDO cross-office review screen'
status: Done
project: PPDO Portal
labels: 
milestone: v1.8.0 — Office Users, AIP Redesign & Reviewer Flow
created: 2026-09-08
completed: 2026-09-09
parent: PPDO-67
blocked_by: PPDO-68, PPDO-71, PPDO-72
related: PPDO-5, PPDO-46, PPDO-64, PPDO-69, PPDO-75, PPDO-76, PPDO-77, PPDO-78, PPDO-79, PPDO-82, PPDO-214
url: https://linear.app/ralphoksiprojects/issue/PPDO-74
uuid: 8db99711-fc0a-4f34-bd2e-82eb943637c3
---

# PPDO-74 — V18-56 — PPDO cross-office review screen

**In one line:** the screen where a designated PPDO user reads a submitted office's AIP, comments on it, and sends it back — the reviewer's actual workplace.

**Plan:** `Phase_Plan.md` §6, §12.4, §12.5 · **Size:** L · **Blocked by:** [PPDO-68](https://linear.app/ralphoksiprojects/issue/PPDO-68/phase-4-spec-aip-review-specmd) (spec), [PPDO-71](https://linear.app/ralphoksiprojects/issue/PPDO-71/v18-53-row-anchored-review-comments-with-resolve), [PPDO-72](https://linear.app/ralphoksiprojects/issue/PPDO-72/v18-54-return-send-back-and-the-re-submit-flow)

## ⚠️ Rewritten 2026-08-25 — this is PPDO review, not LFC review

The plan and the meeting whiteboard both originally drew this step as the **Local Finance Committee**. That is withdrawn (§12.4): *"LFC will no longer [be] reviewing. Certain PPDO users will be the reviewer."* The DBM Budget Operations Manual corroborates it — the LFC's statutory role is upstream, identifying investible funds at the LDIP stage, not AIP review.

The mechanics that mattered survive unchanged: this is the first permission in the system that deliberately **bypasses** `OfficeScope`, and it is held by `CanReviewAllOffices` (shipped, [RAL-257](https://linear.app/ralphoksiprojects/issue/PPDO-5/lfc-permission-the-first-cross-office-flag-bypasses-officescope)).

## What this reviewer may and may not do

|  |  |
| -- | -- |
| Read any office's submitted AIP | ✅ |
| Comment ([PPDO-71](https://linear.app/ralphoksiprojects/issue/PPDO-71/v18-53-row-anchored-review-comments-with-resolve)) | ✅ |
| Return a whole office's work ([PPDO-72](https://linear.app/ralphoksiprojects/issue/PPDO-72/v18-54-return-send-back-and-the-re-submit-flow)) | ✅ |
| **Accept an office** — 🆕 | ✅ `SubmittedToPpdo` → `Consolidated`, one office at a time (spec decision 6, settled 2026-09-08). This action did not exist when the ticket was written |
| **Edit values** | ❌ **Never** — *"they won't be able to apply any update, just comment"* (tracker B11). `ReviewerWriteGuard` already enforces this |

⚠️ **This is the half of the reviewer split that is easy to get backwards.** The *department-head* reviewer edits; the *PPDO* reviewer does not. Two roles, same word.

## Scope

* The office-scoped review surface: one office's tree, read-only, with comment gutters and the return action.
* 🆕 **The Accept action** — confirm dialog naming the office, then `SubmittedToPpdo → Consolidated`. ⚠️ Refused from `ReturnedByPpdo`; a second reviewer acting on an office someone already moved gets a **409** naming the current state, not a silent overwrite.
* Reached from [PPDO-76](https://linear.app/ralphoksiprojects/issue/PPDO-76/v18-75-query-first-review-page)'s query results and [PPDO-78](https://linear.app/ralphoksiprojects/issue/PPDO-78/v18-82-office-kanban-the-readiness-board)'s kanban cards — this ticket owns the detail screen, not the finding.
* ⚠️ **Do not build this on** `aip/detail/page.tsx`**.** [PPDO-64](https://linear.app/ralphoksiprojects/issue/PPDO-64/extract-aipdetailpagetsx-into-components-and-lib-pure-move-no) extracted it into components precisely so the review surface could reuse the pieces rather than fork the 2,000-line page.

## Watch for

⚠️ **These users see every office's budget figures** — the first permission in the system that does. Test the negative case explicitly: a user *without* the flag must not reach another office's tree by URL.
