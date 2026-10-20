---
id: PPDO-90
title: 'AIP report scope: open the consolidated AIP endpoints to department heads (own office)'
status: Done
project: PPDO Portal
labels: 
milestone: v1.8.0 — Office Users, AIP Redesign & Reviewer Flow
created: 2026-09-15
completed: 2026-09-16
parent: (none)
blocked_by: (none)
related: (none)
url: https://linear.app/ralphoksiprojects/issue/PPDO-90
uuid: 0dbe5f0d-1a35-4cd2-a51e-72ac917dacba
---

# PPDO-90 — AIP report scope: open the consolidated AIP endpoints to department heads (own office)

**In one line:** `GET …/aip/consolidated` and `…/consolidated/export` accept either reviewer flag and an optional `officeId`, so a department head can see and export their own office's Annex B.

**Spec:** `docs/v1.8/AIP_Report_Spec.md` §2 decisions 2–6, §3, §4, §11 · **Size:** M · **Blocks:** the Report page ticket

## Scope

* Gate: `CanReviewAllOffices` **or** `CanReviewBudgetPlanning`; neither → 403.
* Cross-office reviewer: `officeId` null = consolidated (unchanged, with-PPDO offices only); set = that office, with-PPDO only.
* Department head: office = `caller.OfficeId`, **param ignored (clamped, not refused)**, any workflow state.
* ⚠️ **Pin the department head to** `users.office_id` **explicitly — never via** `OfficeScope.Resolve`**.** A host-office (PPDO) department head resolves to `SeeAll` there and would see every office. Test this case by name.
* Both flags / SuperAdmin → cross-office behaviour.
* DTO gains `scope` ("Consolidated" | "Office") and `office { officeId, officeCode, officeName, workflowStatus }`; counts describe the one office when scoped.
* One-office export: same four sheets filtered to the office, preamble names the office, filename `AIP{FY}_{OfficeCode}_{timestamp}.xlsx`.
* Add the widened read to `Permission_Matrix.md` §4. No migration.

## To confirm with Ralph (spec §2 decision 3)

Department head sees own office in **any** state — assumed from "so they can see their own office's work". Flipping to with-PPDO-only is a one-line filter.

## Manual-coding candidate?

No — permission gate and scope resolution. Sign-off before merge.
