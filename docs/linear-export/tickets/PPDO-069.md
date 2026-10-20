---
id: PPDO-69
title: 'V18-51 — The second submit: department head → PPDO'
status: Done
project: PPDO Portal
labels: 
milestone: v1.8.0 — Office Users, AIP Redesign & Reviewer Flow
created: 2026-09-08
completed: 2026-09-08
parent: PPDO-67
blocked_by: PPDO-68
related: PPDO-52, PPDO-59, PPDO-72, PPDO-74
url: https://linear.app/ralphoksiprojects/issue/PPDO-69
uuid: 6bdddbc5-1abe-40b5-a0c0-02bc738f3911
---

# PPDO-69 — V18-51 — The second submit: department head → PPDO

> ✅ **Done — PR #312, verified against the running app.** Both group rows moved together, the gate re-ran and refused a submit with two outstanding issues, a double submit was refused naming the state, another office's id returned 404 (not 403), and an encoder without the flag got 403.
>
> ⚠️ **Spec correction made here:** `audit_log.action` is `nvarchar(10)`, so `AIP_Review_Spec.md` §5.2's proposed `SubmitToPpdo` / `ReturnByPpdo` / `AcceptByPpdo` (12 chars each) would have failed on the first real write to SQL Server — not in any test, since the in-memory provider does not enforce the width. Now `SUBMIT_DH` / `SUBMIT_PPD`, with `RETURN_PPD` / `ACCEPT_PPD` reserved in `AuditAction` for [PPDO-72](https://linear.app/ralphoksiprojects/issue/PPDO-72/v18-54-return-send-back-and-the-re-submit-flow) and [PPDO-74](https://linear.app/ralphoksiprojects/issue/PPDO-74/v18-56-ppdo-cross-office-review-screen), and a test pinning every action to the column width.

**In one line:** the office's department head sends the reviewed work on to PPDO — the second of the two submits, and the one that has not been built.

**Plan:** `Phase_Plan.md` §6, §12.5, §12.6 · **Size:** M · **Blocked by:** [PPDO-68](https://linear.app/ralphoksiprojects/issue/PPDO-68/phase-4-spec-aip-review-specmd) (spec)

## ⚠️ Half of V18-51 already shipped — read this before scoping

§6's original "encoders cannot submit" was corrected 2026-08-25: **there are two submits.**

| Hop | Who | State change | Status |
| -- | -- | -- | -- |
| Encoder → department head | the encoder | `Draft` → `DepartmentReview` | ✅ **shipped in** [PPDO-52](https://linear.app/ralphoksiprojects/issue/PPDO-52/v18-42-three-stage-aip-entry-ui-sub-office-group-projectactivity) — `AipSubmitService.SubmitAsync`, with the completeness checklist and ceiling gate of [PPDO-59](https://linear.app/ralphoksiprojects/issue/PPDO-59/v18-49-completeness-checklist-before-submit-this-is-the-ceiling-gate) |
| **Department head → PPDO** | the department-head reviewer, **solely** | `DepartmentReview` → `SubmittedToPpdo` | ◻️ **this ticket** |

So this is an extension of an existing service, not a new one. The original "encoders cannot submit" rule holds **only for this second hop** — an encoder performing the first submit is correct and already works.

## Scope

* A second transition on `AipSubmitService`, gated on `CanReviewBudgetPlanning` **for that office** — the sole authority for this hop.
* ⚠️ **Re-run the ceiling and completeness gate here too.** The department head may have edited values during review (§12.5 role table), so the figures that passed the first gate are not necessarily the figures being sent to PPDO. Reuse [PPDO-59](https://linear.app/ralphoksiprojects/issue/PPDO-59/v18-49-completeness-checklist-before-submit-this-is-the-ceiling-gate)'s checklist rather than trusting the earlier pass.
* The re-submit case after a PPDO return is **the same transition** from `ReturnedByPpdo` — it carries the unresolved-comment warning, which is PPDO-72's half.

## Watch for

⚠️ `AipWorkflowStatus` **already has all five states** — Phase 3 introduced them in one migration deliberately. **No migration here.**

⚠️ **PPDO's own divisions run this same ladder** (tracker B12): they submit to a PPDO department-head reviewer who is a *different person* from the PPDO consolidated reviewer. Do not shortcut PPDO straight to `SubmittedToPpdo`.
