---
id: PPDO-46
title: 'AIP write paths have no office ownership check — cross-office write gap'
status: Done
project: PPDO Portal
labels: 
milestone: v1.8.0 — Office Users, AIP Redesign & Reviewer Flow
created: 2026-09-03
completed: 2026-09-03
parent: PPDO-32
blocked_by: (none)
related: PPDO-38, PPDO-61, PPDO-72, PPDO-74, PPDO-82, PPDO-149
url: https://linear.app/ralphoksiprojects/issue/PPDO-46
uuid: d1f18663-8871-438d-8d11-1da17c76443a
---

# PPDO-46 — AIP write paths have no office ownership check — cross-office write gap

**In one line:** V18-39 scoped the AIP *reads*; the writes had no ownership check at all, so any caller with Budget Planning access could edit or delete another office's AIP node by supplying its id.

**Found:** 2026-09-03, while implementing V18-39 ([PPDO-38](https://linear.app/ralphoksiprojects/issue/PPDO-38/v18-39-aip-scope-resolver-officescope-divisionscope)). ⚠️ **Must land before guest-office accounts are created** — which is what v1.8.0 exists to do.

## The gap

`AipService.UpdateOfficeAsync` and its 15 siblings verified two things: that the node existed, and that its parent AIP record was still Draft. Nothing checked that the node belonged to the caller's office. `AipFunctions` authorized on `CanAccessBudgetPlanningAsync` and passed the route id straight through.

Not currently exploitable — no production guest-office accounts exist yet.

## The refusal shape, and why

Three options; the choice is the interesting part of this ticket.

| Option | Verdict |
| -- | -- |
| **Clamp**, as the reads do | Meaningless. A read clamps by narrowing a result set; a write names one node, and "clamping" it would redirect the update to a different node — silently writing to the wrong row is worse than any refusal |
| **403 Forbidden** | Honest, but confirms the node exists and belongs to another office — precisely the existence check the reads clamp to avoid |
| **404, byte-identical to a missing node** | ✅ Chosen. A node you may not touch and a node that does not exist are the same node from where you sit |

Cost: a puzzling error for a PPDO admin who mistypes an id. A support question, not a disclosure.

## Scope

* One choke point — `CheckWritableAsync` replaces `CheckDraftAsync`, doing ownership **then** draft state, covering the 11 write paths that already funnelled through it
* Each call site passes **its own** not-found message, so parity holds per endpoint rather than only at the office level
* Five outliers guarded individually: `AddOfficeAsync` (guards the office the DTO names — it creates the node, so there is nothing to walk up from), `UpdateProgramFunctionBandAsync` and `UpdateActivityIsCreationAsync` (never had a draft guard either), plus `CopyOfficeFromPriorYearAsync` and `SeedProgramsFromLdipAsync`
* `User caller` threaded through 16 service methods, the interface and the endpoints

## Done when

* A guest-office caller cannot update or delete another office's AIP office, program, project or activity
* The refusal is indistinguishable from a missing node, pinned by test
* An unowned AIP office (null FK from V18-32's backfill) is writable only by the host office
* Disabling the guard turns the scope tests red — verified by mutation, not assumed
