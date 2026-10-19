---
id: PPDO-40
title: 'V18-37 — FY partition: FY≤2027 old shape, FY≥2028 new'
status: Done
project: PPDO Portal
labels: 
milestone: v1.8.0 — Office Users, AIP Redesign & Reviewer Flow
created: 2026-09-02
completed: 2026-09-03
parent: PPDO-32
blocked_by: PPDO-39
related: (none)
url: https://linear.app/ralphoksiprojects/issue/PPDO-40
uuid: 5fb71e85-05ec-4ad1-accf-3ab0ae237f80
---

# PPDO-40 — V18-37 — FY partition: FY≤2027 old shape, FY≥2028 new

**In one line:** the clean break — historical fiscal years keep the shape they were imported in, FY2028 onward uses the redesigned one, and no record ever converts between them.

**Spec: **`docs/v1.8/AIP_Foundation_Spec.md` §5.5 · **Size:** M · **Blocked by:** V18-40 · **Blocks:** V18-38

## Why

The v1.8.0 approach is **redesign, not retrofit** — decided early and the reason the whole version is tractable. A multi-office record with no ownership FK genuinely cannot be retrofitted; that is structural. So FY2027 stays as imported and FY2028+ gets the new shape, with **no data migration** between them.

## ⚠️ The distinction people get wrong

**Shape partitions. Units do not.**

`AipActivity.Total` means **pesos on every row, every fiscal year** after V18-35. Anyone reading "clean break" as covering units will reintroduce exactly the bug V18-35 exists to remove. This ticket partitions *structure only*.

## ⚠️ Open: P2-b — decide before writing code

How does the gate work?

| Option | Trade-off |
| -- | -- |
| **New endpoints beside untouched old ones ***(spec default)* | More surface, but the old path is provably unchanged and the new one has no legacy branch |
| A literal `fiscalYear` check inside shared endpoints | Less surface, but it is a branch that must stay correct forever in a file nobody re-reads |

The spec leans to new endpoints for the reason in the right-hand column. Engineering call — settle it in writing before starting, because it shapes every ticket after this one.

## Scope

The gate itself, whichever mechanism P2-b picks, plus the read/write paths that need to respect it.

## Watch out

* **No record changes shape when its fiscal year changes.** There is no conversion path and there should not be one.
* FY2027 rows in the database were imported by the **pre-**[RAL-238](https://linear.app/ralphoksiprojects/issue/RAL-238/aip-import-detect-hierarchy-level-from-the-description-column-not-the) parser and are **not** a faithful copy of the province's file (Phase_Plan §4b). Do not treat them as reference data for what the old shape "should" be.
* Whatever mechanism is chosen, write down *why* next to it. This is the decision most likely to be second-guessed in six months.

## Out of scope

Freezing the `.xlsm` upload (V18-38 — it consumes this gate). Migrating any historical data.

## Done when

* An FY2027 record opens in the v1.6 shape, unchanged
* An FY2028 record opens in the new shape
* Neither can be converted into the other
* The gate mechanism and its rationale are documented in the spec
