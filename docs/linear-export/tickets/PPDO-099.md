---
id: PPDO-99
title: 'AIP Project details: description and objective fields'
status: Done
project: PPDO Portal
labels: 
milestone: v1.8.0 — Office Users, AIP Redesign & Reviewer Flow
created: 2026-09-16
completed: 2026-09-16
parent: (none)
blocked_by: (none)
related: PPDO-89
url: https://linear.app/ralphoksiprojects/issue/PPDO-99
uuid: 78b991e7-a670-444a-a875-0e9ba44aaf17
---

# PPDO-99 — AIP Project details: description and objective fields

**In one line:** the Project panel's reserved **Project details** section gets its content — description and objective alongside the existing title.

**Source:** 2026-09-15 PDC/finance demo notes (field list supplied 2026-09-16) · **Size:** M · **Demo:** required on or before 2026-09-23

⚠️ **MIGRATION** — two nullable columns on `aip_projects`. CI does not run migrations; a manual `dotnet ef database update` against Azure SQL is needed at release.

## Scope

* `AipProject` gains `Description` and `Objective`, both nullable free text. `AipProjectPanel`'s reserved **Project details** section (spec §2 decision 3, empty since [PPDO-89](https://linear.app/ralphoksiprojects/issue/PPDO-89/aip-entry-wfp-style-program-project-activity-drill-down-layout)) renders them as textareas with the existing save-on-blur / inline-error pattern, behind the same `canEdit` gate as everything else on the panel.
* Both **optional** — no submit-gate change, nothing added to the checklist.
* **Not printed on Annex B.** They feed the separate, not-yet-named report the PDC mentioned; the AIP form's columns are fixed by the province's template.

## Acceptance

- [ ] A project shows Title, Description and Objective; the section is no longer empty
- [ ] Both save and survive a reload, and are editable in Draft, DepartmentReview and ReturnedByPpdo only
- [ ] The Annex B grid and export are byte-identical to before
- [ ] A project with neither field set renders without an empty-looking gap
