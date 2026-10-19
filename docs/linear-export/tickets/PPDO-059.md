---
id: PPDO-59
title: 'V18-49 — Completeness checklist before submit (this IS the ceiling gate)'
status: Done
project: PPDO Portal
labels: 
milestone: v1.8.0 — Office Users, AIP Redesign & Reviewer Flow
created: 2026-09-03
completed: 2026-09-07
parent: PPDO-48
blocked_by: PPDO-52, PPDO-56
related: PPDO-69
url: https://linear.app/ralphoksiprojects/issue/PPDO-59
uuid: c6a96b3f-b7d2-4dea-86f2-2459a9b52664
---

# PPDO-59 — V18-49 — Completeness checklist before submit (this IS the ceiling gate)

> ➖ **Scope removed 2026-09-07 — the** `workflow_status` **migration moved to** [PPDO-52](https://linear.app/ralphoksiprojects/issue/PPDO-52/v18-42-three-stage-aip-entry-ui-sub-office-group-projectactivity) (spec §2 decision 17, §5.1).
>
> `AIP_Entry_Spec.md` §5.1 filed `AddAipOfficeWorkflowStatus` here. Wrong ordering: [PPDO-52](https://linear.app/ralphoksiprojects/issue/PPDO-52/v18-42-three-stage-aip-entry-ui-sub-office-group-projectactivity) ships the submit action and the read-only state that follows it, and cannot be built against a column that does not exist. [PPDO-52](https://linear.app/ralphoksiprojects/issue/PPDO-52/v18-42-three-stage-aip-entry-ui-sub-office-group-projectactivity) **now adds the column and the** `Draft → DepartmentReview` **transition; this ticket keeps the rules for when that transition is allowed** — which is the whole point of it.
>
> ✅ **This ticket therefore no longer carries a migration.**

**In one line:** the one moment an office's AIP is checked — and the only thing standing between over-ceiling encoding and a submitted document.

**Plan:** `docs/v1.8/Phase_Plan.md` §5 · **Spec:** `docs/v1.8/AIP_Entry_Spec.md` §6.2 · **Size:** M · **Blocked by:** V18-42 ([PPDO-52](https://linear.app/ralphoksiprojects/issue/PPDO-52/v18-42-three-stage-aip-entry-ui-sub-office-group-projectactivity)), V18-46 ([PPDO-56](https://linear.app/ralphoksiprojects/issue/PPDO-56/v18-46-aip-ceiling-service-validate-at-submit-upsert-the-ledger-expose)) · **No migration**

## ⚠️ This is not a courtesy checklist

✅ **DECISION C makes this the enforcement point.** Over-ceiling entry is **allowed while encoding** and **blocked at submit** — so this checklist *is* the ceiling gate. If it is built as a friendly summary the user can dismiss, there is no ceiling enforcement anywhere in the system.

The same is true of the ceiling-cut case: ✅ **A5-b — a cut ceiling does not destroy or flag encoded work.** It stands, and **fails here**. This is the only place an office learns.

## What it checks

* ≥ 1 expenditure per activity
* Totals > 0
* CC typology and eSRE code present
* Ceiling respected — via V18-46, with its four traps (GF only, PS exempt, rounded, base not uplifted)

## ⚠️ Two states that look alike and are not

Phase 2's V18-34 established the distinction and it matters here:

| State | `LineCount` | Meaning |
| -- | -- | -- |
| Lines all deleted | 0 | `Total == 0` — costed at nothing |
| Never had lines | 0 | Untouched — never costed |

An activity that **never had lines** is incomplete. One whose lines were **deliberately removed** is costed at zero. The checklist must tell them apart, and `LineCount` alone does not.

## ⚠️ Do not clamp `remaining` at zero

The standing warning reaches its point here: a negative remaining is precisely what must block submit. Clamped to zero, submit passes and the office never learns the ceiling was cut.

## Watch out

⚠️ **There are two submits, not one** — encoder → department head → PPDO. This ticket is the **first**. The second is Phase 4.

⚠️ **Do not gate on unresolved comments here.** V18-53 settled that as a **soft** warning the user may override on re-submit, split by authoring role — not a hard block. That is Phase 4's surface, but it is worth knowing that this gate and that warning are deliberately different in kind.

## Done when

* Submit is blocked when any check fails, and the message says which and where
* An over-ceiling office cannot submit, and can see by how much
* A never-costed activity is distinguished from one costed at zero
* Passing submit hands the whole office's work to department review
* Tests cover each failing condition individually, not just the happy path

## Out of scope

**The** `workflow_status` **column and the state transition itself** — both are [PPDO-52](https://linear.app/ralphoksiprojects/issue/PPDO-52/v18-42-three-stage-aip-entry-ui-sub-office-group-projectactivity)'s, as of 2026-09-07. This ticket decides *whether* the transition may happen, not how it is stored · department-head and PPDO review (Phase 4) · the unresolved-comment warning (V18-53) · the printable form (Phase 5).
