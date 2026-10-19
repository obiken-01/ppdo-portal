---
id: PPDO-126
title: 'Demo 2.4 — Division allocations are not saved'
status: Done
project: PPDO Portal
labels: 
milestone: Demo 2 — change requests & fixes
created: 2026-09-23
completed: 2026-09-23
parent: PPDO-122
blocked_by: (none)
related: PPDO-108, PPDO-123, PPDO-127, PPDO-134
url: https://linear.app/ralphoksiprojects/issue/PPDO-126
uuid: 223aa75a-fdab-444c-a394-33174983f665
---

# PPDO-126 — Demo 2.4 — Division allocations are not saved

*"Division allocation are not saved. I didn't check if the ppa assignment is saved or not."*

## Leading hypothesis — a silent no-op when the office has no divisions

⚠️ **Needs confirming against the office actually demoed** before building the fix.

The allocation table renders `divisions.map(...)` with **no empty state**, and the save maps over that same list:

```ts
const allocs = divisions.map((d) => ({ divisionId: d.id, amount: inputs[d.id] ?? 0 }));
await upsertAllocations({ officeId, fiscalYear, fundingSourceId, allocations: allocs });
toast.success("Saved", "Division allocations saved.");
```

With no divisions, `allocs` is `[]`. `UpsertAllocationsAsync` passes both guards (a ceiling exists; `Σ 0 ≤ ceiling`), loops over nothing, and returns `Ok([])`. **The toast says "Saved" and nothing was written.**

**15 of 19 offices have no divisions configured**, and until Demo 2.1 nobody could be assigned to one — so this is the common state, not an edge case.

Against that: PTO does have 2 divisions and 2 saved FY2028 allocations totalling ₱3,000,000, so the write path itself works. Which office was on screen decides whether this is the whole story.

## If confirmed

* The empty case must not report success. Either disable Save with an explanation, or return a distinguishable result.
* The table needs an empty state pointing at Config → Divisions.
* Consider refetching after save — `handleSaveAllocations` doesn't, so the grid shows local input state and a silent failure looks identical to a success until reload.

## Also unchecked

PPA assignment on the same page, explicitly not verified during the demo. Check it while here.

## Acceptance

- [ ] Root cause confirmed against the demoed office, not assumed
- [ ] Saving with no divisions cannot report success
- [ ] An office with no divisions says so
- [ ] Saving with divisions persists across a reload
- [ ] PPA assignment verified persisting

Blocked on Demo 2.1 for a realistic test — a guest office needs divisions **and** users before this is exercisable end to end.
