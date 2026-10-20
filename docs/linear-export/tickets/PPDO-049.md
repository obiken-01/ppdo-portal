---
id: PPDO-49
title: 'V18-81 — Block FY2028+ WFP creation as "not supported yet"'
status: Done
project: PPDO Portal
labels: 
milestone: v1.8.0 — Office Users, AIP Redesign & Reviewer Flow
created: 2026-09-03
completed: 2026-09-05
parent: PPDO-48
blocked_by: (none)
related: PPDO-41, PPDO-61
url: https://linear.app/ralphoksiprojects/issue/PPDO-49
uuid: 4a228bfb-dcd3-4f4f-8d06-0bb431aea5d3
---

# PPDO-49 — V18-81 — Block FY2028+ WFP creation as "not supported yet"

**In one line:** refuse to create a WFP for FY2028 or later, so the AIP/WFP double-count becomes impossible rather than silently wrong.

**Plan: **`docs/v1.8/Phase_Plan.md` §12.1 · **Size:** S · **Blocks:** V18-45

## Why

It is genuinely unknown whether the FY2028 WFP is built in this system or in **GSO's** — the approved AIP may be handed over, and GSO's system built the FY2027 WFP for other offices. Tracker **C2**, rated Blocker for that reason.

Until that is answered, an FY2028 WFP built here would draw on the same `DivisionAllocation` the AIP has already reserved against, and the two would **double-count silently**. V18-45 ships a reservation ledger with no relief/netting mechanism precisely because this ticket makes that safe: with FY2028 WFP creation blocked, there is nothing to net against, and the netting rule can stay written down and unbuilt until the answer arrives.

**This is the cheap move that buys the deferral.** Without it, V18-45's reduced scope leaves an open correctness question instead of a closed one.

## Scope

Refuse WFP creation where the fiscal year is ≥ the AIP break year. FY≤2027 WFP is **unchanged** — every existing path, every existing record.

⚠️ **Reuse** `AipShape.FirstOfficeOwnedFiscalYear`**; do not write** `2028` **again. **`AipShapeTests.TheBreakYear_IsHardcodedInExactlyOnePlace` scans all four production projects **and** `frontend/src`, and fails the build on a second literal. The break year lives in `AipShape` and nowhere else.

Whether the refusal belongs in `AipShape` beside `RefuseUpload` or in a WFP-side equivalent is an implementation call — but it must read from that one constant.

## ⚠️ Do not touch `WfpCeilingService`

**Zero diff.** The plan once proposed retiring its allocation check for FY2028+; that was reversed 2026-08-26 (§12.1). The check lives in four already-FY-parameterised methods, so "retire for FY2028+" would **add four conditionals rather than delete code** — the plan's "smallest change" claim was wrong about the codebase. It is also the only fund-scoped check in the system: step 1 (AIP budget) is aggregate across funding sources; only step 2 (allocation) is per-fund, which is exactly what the fund-mix caveat needs.

With this ticket in place, none of those four methods is ever called with `fiscalYear >= 2028`, so the check is **already inert** for those years. Retiring it buys nothing and costs the fund-scoped guard.

## UI

Disabled with a reason, not hidden — the user has the permission, the fiscal year forbids it (`Budget_Planning_Dashboard_Requirements.md` §6.1). **V18-38 (**[PPDO-41](https://linear.app/ralphoksiprojects/issue/PPDO-41/v18-38-freeze-xlsm-aip-upload-to-historical-fiscal-years)**) is the worked example**: `frontend/src/lib/aip-shape.ts` for the client-side mirror, and `aip/new/page.tsx` for the banner + disabled control + removed file input.

The message should say the FY2028 WFP is not built in this system **yet** — not that it is forbidden. The distinction matters because the answer to C2 may well make it supported.

## Done when

* FY2028+ WFP creation refused server-side, with a message naming the year
* FY≤2027 WFP creation unchanged, including every existing entry path
* The control is disabled with a visible reason
* `WfpCeilingService` has a zero diff
* No new `2028` literal — the break-year scan still passes

## Out of scope

The netting/relief mechanism (deferred with V18-45) · answering C2 · anything in `WfpCeilingService`.
