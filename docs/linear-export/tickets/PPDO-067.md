---
id: PPDO-67
title: 'Epic: v1.8.0 Phase 4 — Review & Consolidation'
status: Done
project: PPDO Portal
labels: 
milestone: v1.8.0 — Office Users, AIP Redesign & Reviewer Flow
created: 2026-09-08
completed: 2026-09-14
parent: (none)
blocked_by: (none)
related: PPDO-48, PPDO-52, PPDO-61
url: https://linear.app/ralphoksiprojects/issue/PPDO-67
uuid: 3caa0197-14f0-411f-b714-42405d3fc80e
---

# PPDO-67 — Epic: v1.8.0 Phase 4 — Review & Consolidation

**In one line:** what happens to an office's AIP after it is submitted — two review hops, row-level comments, a return path, and the consolidated view PPDO reviews across all 19 offices.

Plan: `docs/v1.8/Phase_Plan.md` §6, §12.4, §12.5, §12.5a, §12.6, §12.7 · Follows: [PPDO-48](https://linear.app/ralphoksiprojects/issue/PPDO-48/epic-v180-phase-3-aip-entry) (Phase 3 — AIP Entry), complete and merged to `release/1.8.0`

## ⚠️ No spec exists yet — [PPDO-68](https://linear.app/ralphoksiprojects/issue/PPDO-68/phase-4-spec-aip-review-specmd) writes it, and it blocks every other child

Phases 2 and 3 each had a spec written and merged **before** their tickets were worked (`AIP_Foundation_Spec.md` PR #294, `AIP_Entry_Spec.md` PR #295), and CLAUDE.md forbids starting a feature that adds a table, an endpoint, a permission or a screen until one exists. Phase 4 adds all four. The decisions are unusually well settled — §12.4 through §12.6 are dense with answered tracker items — but they are spread across six sections of the plan and have been revised in place several times. Collecting them is [PPDO-68](https://linear.app/ralphoksiprojects/issue/PPDO-68/phase-4-spec-aip-review-specmd).

## ✅ Nothing is blocked by an open question

All ten items in §12.8 were answered 2026-08-26, including the two §6 named as shaping questions (comment threading → **none**; PPDO's own divisions → **same ladder, division-scoped via** `ProgramDivision`). The record-shape question underneath Q8 was closed separately by [PPDO-61](https://linear.app/ralphoksiprojects/issue/PPDO-61/reverse-the-office-owned-aip-record-shape-one-base-record-per-fiscal).

## Three things Phase 3 already settled — do not re-ticket them

| §6 item | State |
| -- | -- |
| **V18-50** — extend the status vocabulary | ✅ **Shipped in** [PPDO-52](https://linear.app/ralphoksiprojects/issue/PPDO-52/v18-42-three-stage-aip-entry-ui-sub-office-group-projectactivity), and *not* by extending `PlanningStatus` as §6 proposed. `AipWorkflowStatus` is a separate vocabulary with **all five states present from the first migration** (`AddAipOfficeWorkflowStatus`), deliberately, so Phase 4 needs no second migration on that column. `PlanningStatus` stayed the *year*'s state, shared with LDIP and WFP |
| **V18-57** — readiness view + history | ➗ **Split and absorbed** — the readiness half is [PPDO-78](https://linear.app/ralphoksiprojects/issue/PPDO-78/v18-82-office-kanban-the-readiness-board) (kanban), the history half is [PPDO-77](https://linear.app/ralphoksiprojects/issue/PPDO-77/v18-77-submission-history-and-show-history). §12.7 says so explicitly; there is no residue |
| **V18-04 reviewer split** (§12.7's "Phase 1 gains real work") | ✅ **Already correct in shipped code.** `ReviewerWriteGuard` keys on `CanReviewAllOffices`, not on the department-head flag, and its remarks carry the B11 table. The blanket-denial premise §12.7 warned about never shipped |

## Children — 12 tickets

Order: [PPDO-68](https://linear.app/ralphoksiprojects/issue/PPDO-68/phase-4-spec-aip-review-specmd) **(spec) blocks everything.** Then `PPDO-76 + PPDO-79` (the review page and its way in) → `PPDO-69 → PPDO-70` (the second hop and its lock) → `PPDO-71` (comments) → `PPDO-72 → PPDO-74` (return, then the reviewer's screen) → `PPDO-73`, `PPDO-75`, `PPDO-77`, `PPDO-78`.

| Ticket | # | What | Size |
| -- | -- | -- | -- |
| [PPDO-68](https://linear.app/ralphoksiprojects/issue/PPDO-68/phase-4-spec-aip-review-specmd) | — | Phase 4 spec — `AIP_Review_Spec.md` | M |
| [PPDO-69](https://linear.app/ralphoksiprojects/issue/PPDO-69/v18-51-the-second-submit-department-head-ppdo) | V18-51 | The second submit: department head → PPDO | M |
| [PPDO-70](https://linear.app/ralphoksiprojects/issue/PPDO-70/v18-52-locking-at-submittedtoppdo-unlock-on-return) | V18-52 | Locking at `SubmittedToPpdo`, unlock on return | S |
| [PPDO-71](https://linear.app/ralphoksiprojects/issue/PPDO-71/v18-53-row-anchored-review-comments-with-resolve) | V18-53 | Row-anchored review comments with resolve | L |
| [PPDO-72](https://linear.app/ralphoksiprojects/issue/PPDO-72/v18-54-return-send-back-and-the-re-submit-flow) | V18-54 | Return / send-back and the re-submit flow | M |
| [PPDO-73](https://linear.app/ralphoksiprojects/issue/PPDO-73/v18-55-the-consolidated-view-partial-by-design) | V18-55 | The consolidated view — partial by design | M |
| [PPDO-74](https://linear.app/ralphoksiprojects/issue/PPDO-74/v18-56-ppdo-cross-office-review-screen) | V18-56 | PPDO cross-office review screen | L |
| [PPDO-75](https://linear.app/ralphoksiprojects/issue/PPDO-75/v18-58-in-app-notifications-pending-count-review-queue) | V18-58 | In-app notifications — pending count + review queue | M |
| [PPDO-76](https://linear.app/ralphoksiprojects/issue/PPDO-76/v18-75-query-first-review-page) | V18-75 | Query-first review page | L |
| [PPDO-77](https://linear.app/ralphoksiprojects/issue/PPDO-77/v18-77-submission-history-and-show-history) | V18-77 | Submission history + "Show History" | M |
| [PPDO-78](https://linear.app/ralphoksiprojects/issue/PPDO-78/v18-82-office-kanban-the-readiness-board) | V18-82 | Office kanban — the readiness board | M |
| [PPDO-79](https://linear.app/ralphoksiprojects/issue/PPDO-79/v18-83-split-the-aip-sidebar-item-into-entry-and-review) | V18-83 | Split the AIP sidebar item | S |

## Five things to know before starting

1. **The reviewer is a PPDO user; the LFC is out of the system** (§12.4). Two reviewer flags, already shipped: `CanReviewBudgetPlanning` (the office's own department head — **may edit values**) and `CanReviewAllOffices` (PPDO consolidated — **comment only**). The powers genuinely differ; §12.5's role table is the reference.
2. ⚠️ **There are two submits, not one.** Encoder → department head shipped in Phase 3 (`AipSubmitService`). Department head → PPDO is V18-51 and has not been built. A ticket that says "the submit" without saying which hop is ambiguous.
3. ⚠️ **The review page lists nothing by default** (§12.5) — it is query-first. That is a deliberate design, and the kanban (V18-82) exists precisely because an empty page is a poor landing for a reviewer whose first question is "who is done". OR within a field, AND across fields, **no boolean expression language**; copy the PR List status-chip interaction, counts included.
4. ⚠️ **Only the authoring side resolves a comment** (§12.5). An office may not resolve a PPDO reviewer's comment, and an encoder may not resolve their department head's. Without this the soft re-submit gate is self-marking — the recipient clears everything and re-submits having addressed nothing. This is the single most load-bearing rule in the phase and it is easy to implement backwards.
5. ⚠️ **No deadline gate** (tracker B7). The office-to-PPDO deadline is communicated outside the system. June 7 is statutory but applies to the **Sanggunian** step, downstream of everything this system models — do not find that date later and conclude B7 was answered wrongly.

## Out of scope

The printable AIP form (Phase 5, V18-60) · offline (Phase 6) · recording PDC / Sangguniang Panlalawigan outcomes (tracker B14 — generation is in scope, outcome tracking is not) · amendment after approval (V18-73) · "PBO Connect" / "Recall AIP" (tracker W10 — a later stage, outside PPDO).
