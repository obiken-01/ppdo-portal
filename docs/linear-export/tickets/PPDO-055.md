---
id: PPDO-55
title: 'V18-45 — AIP reservation ledger (AipDivisionAllocationLedger)'
status: Done
project: PPDO Portal
labels: 
milestone: v1.8.0 — Office Users, AIP Redesign & Reviewer Flow
created: 2026-09-03
completed: 2026-09-07
parent: PPDO-48
blocked_by: PPDO-49
related: (none)
url: https://linear.app/ralphoksiprojects/issue/PPDO-55
uuid: e10560d9-00c9-4827-aab3-e5567d6b8d70
---

# PPDO-55 — V18-45 — AIP reservation ledger (AipDivisionAllocationLedger)

**In one line:** the AIP's own draw-down ledger against the allocation — mirroring the WFP one, **not** generalising it.

**Plan: **`docs/v1.8/Phase_Plan.md` §12.1 · **Size:** M · **Blocked by:** V18-81 · **Blocks:** V18-46

## ⚠️ MIGRATION

## Settled — DECISION A, 2026-08-25

**Build** `AipDivisionAllocationLedger` **mirroring the WFP one; do NOT generalise the existing ledger.** Ralph, verbatim: *"create new tables and fields for AIP, and not reuse the fields used by WFP … in the future, WFP itself will be updated."*

Same reasoning as `aip_expenditures` (V18-33): the two documents answer different questions, and a shared table would have to serve both, badly.

## Scope was REDUCED 2026-08-26 — read this before building the netting

This is **an AIP-only reservation ledger with NO relief/netting mechanism.** The netting rule stays **written down and unbuilt**.

That deferral is only safe because **V18-81** blocks FY2028+ WFP creation in this system — with no FY2028 WFP here, there is nothing to net against. **V18-81 must land first**; without it this reduced scope leaves an open correctness question rather than a closed one.

## The rule being deferred, recorded so it is not re-derived wrongly

The AIP row is a **reservation** that the WFP **relieves** as it commits. The two ledgers must **net, not add**:

```
allocation consumed = WFP committed + AIP reserved not yet converted
```

⚠️ **Relief must be per ACTIVITY, not per fund.** Per-fund relief strands reservations whenever the fund mix changes: ₱6M reserved as GF, later detailed as ₱4M GF + ₱2M GAD, leaves ₱2M of stale GF reserved forever. This is the subtlety flagged as "easy to get subtly wrong"; it is written out in §12.1.

⚠️ **The gap this leaves given General-Fund-only ceilings:** an activity **planned under an unchecked fund and detailed under GF** consumes GF allocation with no AIP reservation behind it. Known, accepted for now, and worth re-reading when netting is built.

## ⚠️ Do not clamp `remaining` at zero

Anywhere — ledger, DTO or UI. After a ceiling cut an office's remaining allocation is legitimately **negative**, and that is exactly the state that must block submit. A `Math.Max(0, …)` hides the only signal the office has.

## Done when

* `AipDivisionAllocationLedger` exists as its own table, mirroring the WFP ledger's shape
* Reserving against an allocation is recorded per activity
* `remaining` can go negative and does
* `WfpDivisionAllocationLedger` and `WfpCeilingService` have a zero diff
* The netting rule is recorded in code comments where the ledger lives, marked deferred

## Out of scope

Relief/netting (deferred) · the ceiling *check* (V18-46) · the submit gate (V18-49) · reworking the WFP ledger.
