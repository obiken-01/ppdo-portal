---
id: PPDO-102
title: 'External AIP API: program and project totals (schema 1.1.0)'
status: Done
project: PPDO Portal
labels: 
milestone: v1.8.0 — Office Users, AIP Redesign & Reviewer Flow
created: 2026-09-16
completed: 2026-09-17
parent: (none)
blocked_by: (none)
related: PPDO-98
url: https://linear.app/ralphoksiprojects/issue/PPDO-102
uuid: 947b7af1-e6fa-4587-95a3-e2a021a25bb5
---

# PPDO-102 — External AIP API: program and project totals (schema 1.1.0)

**In one line:** `/external/v1` program and project nodes carry `totals` and `printedTotals`, the way group nodes already do.

**Source:** 2026-09-15 demo notes ("and probably in the json too?"), confirmed 2026-09-16 · **Size:** S · **Blocked by:** [PPDO-98](https://linear.app/ralphoksiprojects/issue/PPDO-98/program-and-project-subtotals-on-the-aip-form-grid-annex-b-excel-and) (the same subtotals, on the grid and the Excel) · **After the 2026-09-23 demo**

## Scope

`ExternalGroupDto` carries `Totals` + `PrintedTotals`; `ExternalProgramDto` and `ExternalProjectDto` carry none. `ExternalAipReadService` already accumulates `groupPrintedParts` per program and per project on its way up, so the figures exist at both levels — they are simply not put on the DTO.

* Add `Totals` and `PrintedTotals` to both records, populated from the sums the service already builds.
* ⚠️ **Additive change to a published contract.** Bump the schema to **1.1.0**, add a version note to `docs/v1.8/External_AIP_API_Spec.md`, and update the schema contract test. An existing partner ignoring unknown fields keeps working; that is what makes it a minor rather than a major.
* Match [PPDO-98](https://linear.app/ralphoksiprojects/issue/PPDO-98/program-and-project-subtotals-on-the-aip-form-grid-annex-b-excel-and)'s rule: a node with no activity beneath it reports **null**, not zero.

## Acceptance

- [ ] A program node's `printedTotals` equals the sum of its projects', and a project's the sum of its activities'
- [ ] A program whose only project is synthetic still totals that project's activities
- [ ] A node with nothing costed under it reports null
- [ ] The contract test pins 1.1.0 and the spec says what changed between 1.0.0 and it
