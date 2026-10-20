---
id: PPDO-70
title: 'V18-52 — Locking at SubmittedToPpdo, unlock on return'
status: Done
project: PPDO Portal
labels: 
milestone: v1.8.0 — Office Users, AIP Redesign & Reviewer Flow
created: 2026-09-08
completed: 2026-09-08
parent: PPDO-67
blocked_by: PPDO-68, PPDO-69
related: PPDO-119, PPDO-130
url: https://linear.app/ralphoksiprojects/issue/PPDO-70
uuid: 02095a89-fcdb-4710-836f-458299b01a39
---

# PPDO-70 — V18-52 — Locking at SubmittedToPpdo, unlock on return

> ✅ **Done — PR #313, verified live across all five states.** `IsEncoderEditable` → `IsOfficeEditable` (Draft, DepartmentReview, ReturnedByPpdo). The rename is part of the fix: in all three open states the encoder and the department head have identical content rights, so the rule is a property of the *state* — keeping "Encoder" in the name is what made the old rule look right.
>
> Encoder edits now return **200 in DepartmentReview and ReturnedByPpdo** where Phase 3 gave 400, and 400 at SubmittedToPpdo/Consolidated. Two existing tests moved rather than being deleted — one carried the real "document changes beneath the reviewer" concern, which now sits at the hop where it still applies.
>
> ⚠️ **Extra defect found by live-testing:** a locked office read *"2 items to fix before submitting"* while the work sat with PPDO — naming work the office cannot do, for a submit it cannot make. Fixed here; the checklist's single read-only stage split into `awaitingReviewer` (still editable) and `locked`.

**In one line:** once an office's work reaches PPDO nobody may edit it — and returning it hands editing back.

**Plan:** `Phase_Plan.md` §6, §12.6 · **Size:** S · **Blocked by:** [PPDO-68](https://linear.app/ralphoksiprojects/issue/PPDO-68/phase-4-spec-aip-review-specmd) (spec), [PPDO-69](https://linear.app/ralphoksiprojects/issue/PPDO-69/v18-51-the-second-submit-department-head-ppdo)

## The rule (tracker B3, answered 2026-08-25)

| State | Who may edit |
| -- | -- |
| `Draft` | Encoder |
| `DepartmentReview` | **Encoder and department head both** — ✅ settled 2026-09-08, see below |
| `SubmittedToPpdo` | **Nobody.** Office sees it read-only; PPDO reviewers comment but do not edit |
| `ReturnedByPpdo` | Encoder and department-head reviewer again |
| `Consolidated` | Nobody |

## ✅ Settled 2026-09-08 — this ticket carries a Phase 3 correction

§12.6 says that during `DepartmentReview` the work stays **editable by both encoder and reviewer**, and that it locks **only** on submit to PPDO.

Shipped Phase 3 code says otherwise. `AipWorkflowStatus.IsEncoderEditable(status) => status == Draft`, so `AipWriteGuard` refuses the encoder the moment they submit for department review, and its own remarks state *"once submitted the office is read-only to the encoder"*. The department-head reviewer's edit path was never built either — `ReviewerWriteGuard` permits them, but nothing in Phase 3 exercises it.

**Ralph settled it in favour of §12.6: relax** `DepartmentReview` **and move the lock to** `SubmittedToPpdo` (spec decision 4). The "keep the shipped behaviour" branch is **closed**.

The reasoning: the department head's remit is *"to update any minor details they found during review"* (tracker B11). If the encoder is frozen out at the first submit, every correction however small must be typed by the department head personally — which is not what reviewing someone else's work looks like in practice.

⚠️ **So this ticket edits Phase 3 code, not only new code.** `IsEncoderEditable` must accept `Draft`, `DepartmentReview` and `ReturnedByPpdo`, and its remarks — which currently assert the opposite in writing — must be rewritten. **Add the failing test first**: an encoder editing an activity in `DepartmentReview` should fail against current `main`.

## Scope

* Extend `AipWriteGuard` so the lock is keyed on the workflow state rather than on `Draft`-only, with the department-head reviewer's write path distinguished from the encoder's.
* Unlock on return (PPDO-72's transition) — the same guard, no separate path.
* ⚠️ **The refusal must say which state holds the work**, not merely disable the control. `AipWriteGuard.Describe` already renders all five states as a person reads them — reuse it.
* Tests pinning every (state × role) cell of the table above, including the PPDO reviewer being refused a content write while still being allowed to comment.

## Watch for

⚠️ **A comment is not a content write.** `ReviewerWriteGuard`'s remarks already warn that submitting, returning and commenting must **not** be routed through it — *"a comment-only reviewer who cannot comment is not a reviewer"*. The same applies to this lock: `SubmittedToPpdo` locks **content**, and is precisely the state in which commenting must work.
