---
id: PPDO-68
title: 'Phase 4 spec — AIP_Review_Spec.md'
status: Done
project: PPDO Portal
labels: 
milestone: v1.8.0 — Office Users, AIP Redesign & Reviewer Flow
created: 2026-09-08
completed: 2026-09-08
parent: PPDO-67
blocked_by: (none)
related: PPDO-50
url: https://linear.app/ralphoksiprojects/issue/PPDO-68
uuid: 895f899e-1a08-41f2-9efb-b37d845abf48
---

# PPDO-68 — Phase 4 spec — AIP_Review_Spec.md

> ✅ **Written 2026-09-08 — PR #311.** Both open questions were answered by Ralph and are settled in the spec as decisions 4 and 6:
>
> * `DepartmentReview` **is editable by encoder AND department head**; the lock falls at `SubmittedToPpdo`. ⚠️ This **reverses shipped Phase 3 behaviour** — [PPDO-70](https://linear.app/ralphoksiprojects/issue/PPDO-70/v18-52-locking-at-submittedtoppdo-unlock-on-return) now carries a correction to `AipWorkflowStatus.IsEncoderEditable`, and its "keep shipped behaviour" branch is closed.
> * **An office reaches** `Consolidated` **by an explicit per-office Accept** by a PPDO reviewer. ⚠️ [PPDO-74](https://linear.app/ralphoksiprojects/issue/PPDO-74/v18-56-ppdo-cross-office-review-screen) gains that action.
>
> Two findings from reading the code: **V18-76 is not needed and was never ticketed** (`AipOffice.OfficeId` + `.Sector` already answer every query §12.5 asks for — spec §5.3), and **history reuses** `AuditLog` rather than a new table (spec §5.2).

**In one line:** collect Phase 4's already-settled decisions into one spec, the way Phases 2 and 3 each got one before their tickets were worked.

**Standard:** `docs/SPEC_STANDARD.md` · **Size:** M · **Blocks:** every other Phase 4 child

## Why this is a ticket rather than an assumption

Phase 4 adds a table, endpoints, a screen and permission behaviour — the exact list CLAUDE.md says not to start without a spec. Both prior phases followed that order (`AIP_Foundation_Spec.md` PR #294, `AIP_Entry_Spec.md` PR #295) and both were better for it: [PPDO-50](https://linear.app/ralphoksiprojects/issue/PPDO-50/v18-44-make-aip-ref-code-allocation-safe-under-concurrent-creates) was rescoped after reading the code, and P3-a/P3-b were caught as open questions before they became rework.

**This is not a research ticket.** The decisions exist and are answered. The problem is that they are spread across §6, §12.4, §12.5, §12.5a, §12.6, §12.7 and §12.8 of `Phase_Plan.md`, several were revised in place (V18-53 was narrowed twice in one day), and §6's table still carries superseded wording — it names two "shaping questions" that §12.8 answered, and V18-50/V18-57 rows that Phase 3 has since overtaken. Anyone reading §6 alone gets it wrong.

## What it must settle beyond transcription

Most of the spec is collection. These are the places the plan genuinely stops short:

1. ⚠️ **Who may edit during** `DepartmentReview` **— the plan and the shipped code disagree.** §12.6 says *"Encoder and reviewer both"*, and that locking happens **only** at `SubmittedToPpdo`. Shipped Phase 3 code says otherwise: `AipWorkflowStatus.IsEncoderEditable` returns true for `Draft` only, so `AipWriteGuard` already locks the encoder out the moment they submit for department review. One of the two is wrong. **Resolve it explicitly** — the answer changes V18-52's scope and possibly a one-line fix to Phase 3 code.
2. **The comment table's shape** — what a row anchors to, given §12.5 settled "the row" rather than a field. AIP rows are programs, projects and activities in one tree, so the anchor is a node reference, not a grid coordinate.
3. **API contract and error shapes** for submit-onward, return, comment, resolve — including what happens when two reviewers act on the same office concurrently.
4. **UI states** for the review page: empty (the default, and deliberate), loading, no-results, forbidden, and the read-only rendering an office sees while its own work sits at PPDO.
5. **Per-role behaviour table** covering encoder, department-head reviewer, PPDO reviewer, and a user whose office does **not** match the record — scope leaks have been a real defect class here.

## Done when

The spec satisfies `SPEC_STANDARD.md` §2, every child ticket can point at a section instead of at six plan sections, and the `DepartmentReview` editability contradiction is closed one way or the other with the reason written down.
