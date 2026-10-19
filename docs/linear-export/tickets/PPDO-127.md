---
id: PPDO-127
title: 'Demo 2.5 — Per-division allocation breakdown for non-PPDO offices'
status: Done
project: PPDO Portal
labels: 
milestone: Demo 2 — change requests & fixes
created: 2026-09-23
completed: 2026-09-23
parent: PPDO-122
blocked_by: (none)
related: PPDO-126, PPDO-134
url: https://linear.app/ralphoksiprojects/issue/PPDO-127
uuid: c76677c0-4450-41e0-aa36-10c7c117476a
---

# PPDO-127 — Demo 2.5 — Per-division allocation breakdown for non-PPDO offices

*"Per division breakdown in dashboard for non-PPDO office. For both department head and division head's. Also in the AIP entry page."*

Scope confirmed with Ralph: **allocation only** — what each division was given. Not encoded AIP totals, so none of the ledger-attribution problem applies; `division_allocations` stores this explicitly per division.

## The component already exists

`app/(portal)/budget-planning/DivisionTable.tsx` does exactly this — one row per division, allocation and remaining, expanding to a per-fund breakdown. Its header says:

> *Host office only; a guest office has no division split.*

So most of this is lifting that restriction rather than building a view.

## ⚠️ The department head is blocked at the API

Both allocation GETs in `AllocationFunctions` end with:

```csharp
if (!await CanManagePpdoAllocation(caller))
    data = data.Where(a => a.DivisionId == caller.DivisionId).ToList();
```

`CanManagePpdoAllocation` is **exclusive to PPDO users**, so a department head falls into the encoder branch and sees only their own division's row — never their office's breakdown. And with no division assigned (Demo 2.1) their `DivisionId` is null, so the filter returns **nothing at all**.

The permission model is binary today — PPDO finance sees every division, everyone else sees one. This needs a third case: **a department head sees every division of their own office.** That's a backend change, not just a dashboard panel. The division head's half already works correctly under the existing clamp once they have a division.

Update `docs/v1.8/Permission_Matrix.md` — a new scope rule belongs in the matrix, and `PermissionMatrixTests` pins every row.

## ⚠️ Do not mix additive and non-additive columns

`DivisionTable`'s existing AIP-progress columns are deliberately **non-additive** — a PPA shared by two divisions counts in full against both, and its comment warns against adding a total row. Allocation figures **are** additive. If both appear in one table, say which is which, or the office total will be misread.

## Acceptance

- [ ] A department head sees every division of their own office, and no other office's
- [ ] A division head sees their own division only
- [ ] PPDO finance is unchanged
- [ ] An office with no divisions shows an empty state, not a blank panel
- [ ] Visible on the dashboard **and** the AIP entry page
- [ ] Permission matrix row added and pinned by a test

Blocked on Demo 2.1 and 2.4 — there is nothing to break down until offices have divisions with allocations.
