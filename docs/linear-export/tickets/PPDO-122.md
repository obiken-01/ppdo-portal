---
id: PPDO-122
title: 'Demo 2 — change requests and issues (epic)'
status: Done
project: PPDO Portal
labels: 
milestone: Demo 2 — change requests & fixes
created: 2026-09-23
completed: 2026-10-05
parent: (none)
blocked_by: (none)
related: PPDO-72
url: https://linear.app/ralphoksiprojects/issue/PPDO-122
uuid: 7c39813e-4926-488d-ba5f-ddb2a769a843
---

# PPDO-122 — Demo 2 — change requests and issues (epic)

Thirteen findings from the second demo, raised 2026-09-22. Each was triaged against the code before filing, so the split below is verified rather than assumed.

## Two were already answered

**Ceiling checking for programs with multiple divisions** — no work needed, the behaviour just wasn't documented. `AipCeilingService` gates submit at **office level for every office**: the office's whole encoded total against its General Fund ceiling. Divisions never enter the check. The division dimension exists only in the reservation ledger, and there a multi-division program is attributed to the **lowest division id**, in full:

```csharp
// A program may span several divisions. The reservation is attributed to the first by id,
// deterministically — splitting one activity's money across divisions is a modelling
// question nobody has answered, and guessing a split here would be inventing policy.
return assignments.Count == 0 ? null : assignments.Min(a => a.DivisionId);
```

⚠️ That unanswered modelling question is a real blocker for the division-level submit flow — see the child ticket.

**Unresolved comment count on submit** — already built ([PPDO-72](https://linear.app/ralphoksiprojects/issue/PPDO-72/v18-54-return-send-back-and-the-re-submit-flow)), but scoped to the re-submit-to-PPDO hop only. Widening it is a small change, filed separately.

## The rest

| Finding | Kind |
| -- | -- |
| Division assignment for office users (also reported as "department head can't assign division") | Bug — **fixed**, PR [obiken-01/ppdo-portal#371](https://linear.app/ralphoksiprojects/review/fixusers-let-office-users-be-assigned-a-division-demo-2-112-4d43fd956f55) |
| eSRE config unused in the AIP dropdowns | Bug |
| CC typology is free text, not a lookup | Bug |
| Division allocations not saved | Bug |
| Per-division allocation breakdown for non-PPDO offices | Feature |
| Editable fund-source sharing | Feature — needs a decision |
| One fund source per activity | Change |
| Division head submits to department head, who can return | Feature — large, needs a spec |
| Draft feature | Feature — large, low priority |
| Project proposal file attachments | Feature — large, needs a spec |
| Unresolved comment count on every submit hop | Change — small |

## One thread runs through several of these

Guest offices are half-built on the division axis. **15 of 19 offices have no divisions configured**, users couldn't be assigned to one at all, allocations therefore save nothing, and there is no per-division view. Fixing the assignment bug first is what makes the others testable.
