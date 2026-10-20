---
id: PPDO-91
title: 'AIP Entry: Delete project / Delete activity controls'
status: Done
project: PPDO Portal
labels: 
milestone: v1.8.0 — Office Users, AIP Redesign & Reviewer Flow
created: 2026-09-15
completed: 2026-09-16
parent: (none)
blocked_by: PPDO-88, PPDO-89
related: PPDO-101
url: https://linear.app/ralphoksiprojects/issue/PPDO-91
uuid: 7cb3a35e-f6d2-49f7-8110-921fe6851cda
---

# PPDO-91 — AIP Entry: Delete project / Delete activity controls

**In one line:** Delete buttons on the new Project and Activity panels, with a confirm dialog that says what goes, and codes updated in place from the delete result.

**Spec:** `docs/v1.8/AIP_Entry_Layout_Spec.md` §2 decisions 8–11, §3 delete rows, §6 "Delete confirm" / Success / Read-only / Conflict · **Size:** S · **Blocked by:** [PPDO-88](https://linear.app/ralphoksiprojects/issue/PPDO-88/aip-delete-clear-allocation-ledger-comments-audit-snapshot-renumber) (backend delete), [PPDO-89](https://linear.app/ralphoksiprojects/issue/PPDO-89/aip-entry-wfp-style-program-project-activity-drill-down-layout) (layout)

## Scope

* **Delete project** on the Project panel header, **Delete activity** on the Activity panel header (`RowActions` / danger button).
* `ConfirmDialog` danger variant: "Delete project <ref>?" — lists activities, expenditure lines and **unresolved comments** that will be removed, plus "Later projects in this program will be renumbered. This cannot be undone." (omit zero counts; omit the renumber sentence when nothing follows).
* On success: toast, selection moves to the parent, splice the node out and patch codes from `renumbered[]` — no reload; refresh readiness.
* Read-only states: disabled with the existing `describeAipHolder` reason.
* 409: inline banner on the panel with a Reload action; other errors inline, selection kept.
* `lib/aip.ts` `deleteAipProject` / `deleteAipActivity` return `AipDeleteResultDto`.

## Acceptance

- [ ] The delete dialog states the number of activities and unresolved comments that will be removed
- [ ] After deleting activity `-002` of three, the lookup and child list show the old `-003` as `-002` without a reload
- [ ] A department head can delete in DepartmentReview; an encoder can delete in ReturnedByPpdo
- [ ] After Submit to PPDO the delete buttons are disabled with a reason naming PPDO

## Manual-coding candidate?

Possibly — small, the WFP entry page's expenditure Delete + `ConfirmDialog` is the sibling to copy. Feedback needs the app running, though.
