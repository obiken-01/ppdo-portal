---
id: PPDO-82
title: 'AIP Review: a cross-office reviewer outside PPDO gets 404 on every other office''s expenditure lines'
status: Done
project: PPDO Portal
labels: Bug
milestone: v1.8.0 — Office Users, AIP Redesign & Reviewer Flow
created: 2026-09-13
completed: 2026-09-13
parent: PPDO-67
blocked_by: (none)
related: PPDO-46, PPDO-74, PPDO-79
url: https://linear.app/ralphoksiprojects/issue/PPDO-82
uuid: 465131aa-3ce9-4a78-ab71-d95038fc0db4
---

# PPDO-82 — AIP Review: a cross-office reviewer outside PPDO gets 404 on every other office's expenditure lines

**Severity:** 🟡 Medium — latent. No local or production account triggers it today, but the configuration that does is explicitly supported.

**Where:** Budget Planning → AIP Review → one office (`/budget-planning/aip/review?officeId=…`) → open any activity → its expenditure lines.

**Environment:** All environments. The bug is in code, not data.

**Found:** while building [PPDO-79](https://linear.app/ralphoksiprojects/issue/PPDO-79/v18-83-split-the-aip-sidebar-item-into-entry-and-review)'s activity modal. **Introduced:** [PPDO-74](https://linear.app/ralphoksiprojects/issue/PPDO-74/v18-56-ppdo-cross-office-review-screen), which was the first caller of this read from another office.

## Description

The PPDO consolidated reviewer's one-office review screen loads each activity's expenditure lines through `GET /api/budget-planning/aip/activities/{activityId}/expenditures`. That endpoint was built for the entry page and checks scope with `OfficeScope.Resolve`. `Resolve` gives every **host-office** (PPDO) user all offices, and everyone else **only their own office**.

So a user holding `CanReviewAllOffices` who sits in a **guest** office is refused every other office's lines. `OfficeScope.ResolveForReview`'s own remarks name that case as supported: "a reviewer who sits in GSO reviews every office". The tree and comments render; the lines under each activity don't. It works today only because every current reviewer sits in PPDO.

## Steps to replicate

1. Give a guest-office account (e.g. `user.gso`) the **Can review all offices** override in User Management.
2. Sign in as that account and open AIP Review for another office's FY2028 AIP, e.g. OPA.
3. Expand an activity that has expenditure lines.

**Expected:** the activity's expenditure lines are listed.
**Actual:** the lines request answers **404** ("AIP activity {id} not found."), and the lines area renders empty.

## Root cause

`AipExpenditureService.GetByActivityAsync` used `OfficeScope.Resolve(caller)`. That is the entry page's resolver, not the review read's.

## Fix

In `GetByActivityAsync` only, use `OfficeScope.ResolveForReview(caller, await permissions.CanReviewAllOfficesAsync(caller, ct))`. `IPermissionService` is injected through the constructor and is already registered in DI.

⚠️ **Read path only.** Add, update and delete keep `Resolve` through `AipWriteGuard`. The cross-office grant is read scope, and letting it reach a write would make a comment-only reviewer an editor of every office (`OfficeScope` remarks, `Permission_Matrix.md`).

No frontend change is needed.

**Affected files:**

* `backend/PPDO.Application/Services/AipExpenditureService.cs`
* `backend/PPDO.Tests/Application/AipExpenditureProcurementTests.cs` (constructor)
* `backend/PPDO.Tests/Application/AipExpenditureReadScopeTests.cs` (new)

## Tests

`GetByActivityAsync` had no tests before this. The new `AipExpenditureReadScopeTests` covers:

* A cross-office reviewer sitting in a guest office reads another office's lines.
* A guest encoder asking for another office's lines gets a 404 worded identically to a missing activity ([PPDO-46](https://linear.app/ralphoksiprojects/issue/PPDO-46/aip-write-paths-have-no-office-ownership-check-cross-office-write-gap)).
* An encoder reading their own office's lines still works.
* The same reviewer still **cannot add** a line to another office, and nothing is written.

The full suite passes: 1,957 passed, 0 failed. With the old `Resolve` line temporarily restored, exactly the reviewer test fails, confirming it catches this bug.

🤖 Generated with [Claude Code](<https://claude.com/claude-code>)
