---
id: PPDO-94
title: 'AIP Review: same drill-down as AIP Entry, with a Full office view'
status: Done
project: PPDO Portal
labels: 
milestone: v1.8.0 — Office Users, AIP Redesign & Reviewer Flow
created: 2026-09-16
completed: 2026-09-17
parent: (none)
blocked_by: PPDO-89
related: PPDO-76, PPDO-78, PPDO-105
url: https://linear.app/ralphoksiprojects/issue/PPDO-94
uuid: 02a071b4-60a1-4576-ac3a-27b8a5c12db9
---

# PPDO-94 — AIP Review: same drill-down as AIP Entry, with a Full office view

**In one line:** give the one-office review screen [PPDO-89](https://linear.app/ralphoksiprojects/issue/PPDO-89/aip-entry-wfp-style-program-project-activity-drill-down-layout)'s Program → Project → Activity picker as its default view, and keep today's whole tree behind a **Full office** switch.

**Spec:** `docs/v1.8/AIP_Review_Layout_Spec.md` · **Size:** L · **Blocked by:** [PPDO-89](https://linear.app/ralphoksiprojects/issue/PPDO-89/aip-entry-wfp-style-program-project-activity-drill-down-layout)

## Why

A reviewer almost always arrives **targeting a node** — a search result, a kanban card, an unresolved comment they wrote last week — and reaching it means scrolling the tree. [PPDO-89](https://linear.app/ralphoksiprojects/issue/PPDO-89/aip-entry-wfp-style-program-project-activity-drill-down-layout) solved exactly this on AIP Entry and the picker makes moving to a specific program, project or activity immediate.

⚠️ **But review is not entry.** Accept and Send back are decisions about the **whole office**, and a reviewer has to be able to satisfy themselves they have seen all of it. `AIP_Entry_Layout_Spec.md` §2 flagged this ("that screen shows the whole structure on purpose"). So the overview is kept as a second view, not removed.

## Scope

* **Two views, one screen.** Segmented control in the office header. **Drill-down is the default**; **Full office** is today's tree, moved not rewritten.
* Reuse [PPDO-89](https://linear.app/ralphoksiprojects/issue/PPDO-89/aip-entry-wfp-style-program-project-activity-drill-down-layout)'s components unchanged in shape — `AipEntryPicker`, `AipSelectedPanel`, the three panels, `AipEntrySelection`, `AipOfficeHeader`. A reviewer and an encoder discussing "the second project" must be looking at the same thing.
* **Read-only is a third state, not** `canEdit={false}`**.** The panels' `lockedReason` becomes `string | null`, where null **omits** the add/delete control rather than disabling it with a reason. "With PPDO — activities cannot be added here" is false for a reviewer who was never going to add one.
* Sticky office header carrying FY · office · state chip · History · Send back / Accept / Re-open, plus the view switch — same argument as the entry page's decision 4.
* Selection and view in the URL (`?officeId=&fiscalYear=&view=&programId=&projectId=&activityId=`); stale id falls back to the deepest valid ancestor with a notice.
* Unresolved comments **select** their node in the drill-down (`AipCommentFilterBar`'s `onSelectNode`, already built); in Full office the anchors keep opening in place.
* [PPDO-76](https://linear.app/ralphoksiprojects/issue/PPDO-76/v18-75-query-first-review-page) **search and** [PPDO-78](https://linear.app/ralphoksiprojects/issue/PPDO-78/v18-82-readiness-board-a-board-table-switch-on-the-dashboards-offices) **kanban deep-link the selection** — this is the payoff; a link that lands on the office and leaves the reviewer to find the row is the problem restated.
* Every reader gets both views: PPDO cross-office reviewer, department head, and an office reading its own submitted work.
* ⚠️ `aip/review/page.tsx` is **535 lines** — it must come out **smaller**. The tree moves to `components/aip/review/AipReviewFullTree.tsx`.

## Not in this ticket

No change to any gate, action or comment rule — `canOpenAipOfficeReview`, `ReviewerWriteGuard`, Accept / Send back / Re-open all behave exactly as they do now. No API or migration. The activity modal (`AIP_Review_Spec.md` §6.1a), the consolidated grid, and editing on this screen are untouched. Remembering the view per reader is a follow-up.

## Manual-coding candidate?

No — same reason as [PPDO-89](https://linear.app/ralphoksiprojects/issue/PPDO-89/aip-entry-wfp-style-program-project-activity-drill-down-layout): a large stateful page with a CLS-sensitive loading shape.

## Watch for

Live-test the **department head reading their own office's submitted work**. It is the reader most easily broken by a `canEdit` / read-only mix-up, and it is not the one anybody opens first.
