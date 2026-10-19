---
id: PPDO-77
title: 'V18-77 — Submission history and "Show History"'
status: Done
project: PPDO Portal
labels: 
milestone: v1.8.0 — Office Users, AIP Redesign & Reviewer Flow
created: 2026-09-08
completed: 2026-09-14
parent: PPDO-67
blocked_by: PPDO-68, PPDO-72
related: PPDO-8, PPDO-71, PPDO-74
url: https://linear.app/ralphoksiprojects/issue/PPDO-77
uuid: 7961b42c-2cde-416f-8b02-bf739c520fbb
---

# PPDO-77 — V18-77 — Submission history and "Show History"

**In one line:** who submitted, who returned it, when it came back — readable on the review page itself, not only in an audit table.

**Plan:** `Phase_Plan.md` §6, §12.5, §12.6 · **Size:** M · **Blocked by:** [PPDO-68](https://linear.app/ralphoksiprojects/issue/PPDO-68/phase-4-spec-aip-review-specmd) (spec), [PPDO-72](https://linear.app/ralphoksiprojects/issue/PPDO-72/v18-54-return-send-back-and-the-re-submit-flow)

## ⚠️ This replaces V18-57's deadline gate — there is no deadline in the system

Tracker B7: the submission deadline is **communicated to offices outside the system**. Build **no date gate, no overdue state, no reminder-by-date.** What was wanted instead is the history — *"let's document the history so we can track"*.

June 7 is a real statutory deadline but it applies to the **Sanggunian** step, two stages downstream of anything this system models. Do not find that date in the plan later and conclude B7 was answered wrongly.

## What it records

Every workflow transition: submitted for department review, submitted to PPDO, returned by PPDO, re-submitted — **by whom and when**. Plus comments, including resolved ones.

## The surface matters as much as the table

Ralph: *"As long as the history is preserved and can be displayed in the review page … then they can backtrack the comments and changes made."*

So this is a **"Show History" control on the review page**, not an entry in the config audit log that someone would have to know to go looking for. The submit/return/re-submit chain and the comment trail read **in place**.

⚠️ **Resolved comments are never deleted, only marked** ([PPDO-71](https://linear.app/ralphoksiprojects/issue/PPDO-71/v18-53-row-anchored-review-comments-with-resolve)) — this is what they feed. A resolve is a state change, not a removal, precisely so the history stays complete.

## Watch for

⚠️ **Check** `AuditLog` **before adding a table.** v1.7.0 added audit-trail logging across PRs, deliveries, distributions, items and stock balances, and [PPDO-8](https://linear.app/ralphoksiprojects/issue/PPDO-8/audit-log-coverage-for-permission-role-office-and-division-changes) extended it to permission changes. If the existing mechanism can carry workflow transitions, use it and spend the ticket on the *display*; a second parallel history table is the outcome to avoid.
