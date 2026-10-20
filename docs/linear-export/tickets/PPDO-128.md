---
id: PPDO-128
title: 'Demo 2.6 — Let PPDO edit which fund sources are shared'
status: Done
project: PPDO Portal
labels: 
milestone: Demo 2 — change requests & fixes
created: 2026-09-23
completed: 2026-09-24
parent: PPDO-122
blocked_by: (none)
related: PPDO-109
url: https://linear.app/ralphoksiprojects/issue/PPDO-128
uuid: 6114ddbf-9706-4419-80ec-a8467cea5d53
---

# PPDO-128 — Demo 2.6 — Let PPDO edit which fund sources are shared

*"We should be able to edit what fund source is shared. This is to be set on ppdo side."*

## ❓ Blocked — two readings, very different work

**(a) Flip an existing fund between province-wide and office-owned.** Small. `FundingSourceService` already validates a supplied office id on create; the update path just refuses to assign it.

**(b) PPDO picks, per office, which shared funds that office sees.** A new many-to-many table and a visibility rule on every fund read. Much larger.

Nothing proceeds until this is settled.

## Current model ([PPDO-109](https://linear.app/ralphoksiprojects/issue/PPDO-109/per-office-fund-sources-shared-province-wide-list-plus-an-offices-own), D5)

"Shared" is `FundingSource.OfficeId IS NULL` — province-wide, visible to every office, editable only by a config manager. A row **with** an office id belongs to that office alone.

Ownership is deliberately immutable after creation:

```csharp
// ⚠️ OfficeId is NOT assigned from the body. Ownership is set once, at creation: moving a
// fund between offices ...
```

And the config form has no office picker at all, on purpose — the server stamps the caller's own office on create and ignores the field on update.

## ⚠️ If (a): ownership changes are not cosmetic

Making a shared fund office-owned **hides it from every other office that already has data under it** — ceilings, division allocations, expenditures, WFP ledger rows. The "set once" rule exists for that reason, so reversing it needs a guard: refuse, or require explicit confirmation, when the fund already carries data outside the target scope. Silently orphaning another office's budget lines is the failure mode to design against.

Also note `Code` is globally unique across shared and office-owned rows alike (D6), so a flip can't collide on code — that part is already safe.

## Next step

Answer (a) or (b), then this becomes a normal ticket. If (a), the work is: allow `OfficeId` on update, add the guard, add an office picker for config managers, and audit the change (the service already snapshots `OfficeId` in its audit rows).
