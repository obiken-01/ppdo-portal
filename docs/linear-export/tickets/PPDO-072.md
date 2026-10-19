---
id: PPDO-72
title: 'V18-54 — Return / send-back and the re-submit flow'
status: Done
project: PPDO Portal
labels: 
milestone: v1.8.0 — Office Users, AIP Redesign & Reviewer Flow
created: 2026-09-08
completed: 2026-09-09
parent: PPDO-67
blocked_by: PPDO-68, PPDO-70, PPDO-71
related: PPDO-46, PPDO-69, PPDO-122, PPDO-130, PPDO-133, PPDO-223
url: https://linear.app/ralphoksiprojects/issue/PPDO-72
uuid: 9ca46d8b-4c09-4ef0-9dfb-4ff10402c2ac
---

# PPDO-72 — V18-54 — Return / send-back and the re-submit flow

**In one line:** a PPDO reviewer sends a whole office's work back for changes, and the office re-submits it — with a warning about comments nobody has resolved.

**Plan:** `Phase_Plan.md` §6, §12.5, §12.6 · **Size:** M · **Blocked by:** [PPDO-68](https://linear.app/ralphoksiprojects/issue/PPDO-68/phase-4-spec-aip-review-specmd) (spec), [PPDO-70](https://linear.app/ralphoksiprojects/issue/PPDO-70/v18-52-locking-at-submittedtoppdo-unlock-on-return), [PPDO-71](https://linear.app/ralphoksiprojects/issue/PPDO-71/v18-53-row-anchored-review-comments-with-resolve)

## The transitions

`SubmittedToPpdo` → `ReturnedByPpdo` (the PPDO reviewer returns it) → `DepartmentReview`-equivalent editing → the department head re-submits, back to `SubmittedToPpdo`.

⚠️ **One office at a time is the confirmed granularity** (tracker B5) — the whole of one office's work goes back, not a program or an activity. Returning a subtree is not in scope.

⚠️ **Returning re-opens editing** for the encoder and the department-head reviewer both (§12.6). The unlock is [PPDO-70](https://linear.app/ralphoksiprojects/issue/PPDO-70/v18-52-locking-at-submittedtoppdo-unlock-on-return)'s guard, not a separate path here.

## The soft gate on re-submit

On re-submit the app **counts unresolved comments, warns, and lets the user proceed or cancel.** It is deliberately **soft, not hard** — B10 settled this.

The warning carries **the same two-way split** as the on-screen filters (department-head reviewer vs PPDO reviewer), for the reason given on [PPDO-71](https://linear.app/ralphoksiprojects/issue/PPDO-71/v18-53-row-anchored-review-comments-with-resolve): the reader cannot resolve either set themselves, so a single merged number would imply an action that does not exist for them.

⚠️ **Do not turn this into a hard gate**, however reasonable that looks while implementing it. An office may legitimately re-submit with a comment outstanding — the reviewer's remark may have been answered in a phone call, or overtaken by a change elsewhere. The gate exists to stop *accidental* re-submission by someone who never expanded a collapsed comment.

## Scope

* Return transition, gated on `CanReviewAllOffices`; re-submit reuses [PPDO-69](https://linear.app/ralphoksiprojects/issue/PPDO-69/v18-51-the-second-submit-department-head-ppdo)'s second-hop transition from `ReturnedByPpdo`.
* A reason/covering note on the return is **not** specified — the comments are the mechanism. Confirm in [PPDO-68](https://linear.app/ralphoksiprojects/issue/PPDO-68/phase-4-spec-aip-review-specmd) before adding a field.
* Every transition writes a history row ([PPDO-77](https://linear.app/ralphoksiprojects/issue/PPDO-77/v18-77-submission-history-and-show-history)).
