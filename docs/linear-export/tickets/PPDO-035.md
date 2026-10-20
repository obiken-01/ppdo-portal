---
id: PPDO-35
title: 'V18-36 — Pin the AIP↔WFP numeric boundary with tests'
status: Done
project: PPDO Portal
labels: 
milestone: v1.8.0 — Office Users, AIP Redesign & Reviewer Flow
created: 2026-09-02
completed: 2026-09-03
parent: PPDO-32
blocked_by: (none)
related: (none)
url: https://linear.app/ralphoksiprojects/issue/PPDO-35
uuid: 1b1b7b2f-277c-429c-9285-580b7ff61490
---

# PPDO-35 — V18-36 — Pin the AIP↔WFP numeric boundary with tests

**In one line:** AIP and WFP meet at exactly one number — the ceiling check — and nothing guards it; write the test that makes the conversion factor impossible to drift again.

**Spec: **`docs/v1.8/AIP_Foundation_Spec.md` §11 · **Size:** S · **Not blocked — do this BEFORE V18-35** · 🎓 **Good manual-implementation candidate**

## ✅ Unblocked 2026-09-02 — and the order was backwards

This was originally marked blocked by V18-35. It is not, and doing it first is better.

`WfpCeilingStatusDto.AipBudget` is **pesos on both sides of the migration**:

|  | `activity.Total` | Conversion | `AipBudget` |
| -- | -- | -- | -- |
| Today | `250` (thousands) | `× 1000` | **₱250,000** |
| After V18-35 | `250000` (pesos) | none | **₱250,000** |

So `Assert.Equal(250_000m, status.AipBudget)` is the **same assertion either way** — only the test *fixture* changes (`250` → `250000`).

Written now, this is a characterization test that **guards** the migration: if V18-35 gets the conversion wrong, this turns red and says so. Written afterwards, it only documents whatever V18-35 happened to do. Pin behaviour before you change it.

## Why

`WfpCeilingService` compares a WFP expenditure against its AIP activity's total. That comparison is the only place the two documents meet numerically, it has silently carried a `×1000` for months, and no test asserts the resulting peso figure. After V18-35 deletes the factor, nothing would notice if it came back.

The failure mode is **permissive and silent**: a wrong factor means the ceiling stops tripping, and the first symptom is a WFP over its AIP activity, discovered by someone adding up a printed report by hand.

## Scope

New `AipWfpBoundaryTests`. An explicit **accept** case and an explicit **reject** case against a known AIP activity total, asserting the peso figure **literally**.

## Watch out

⚠️ **Assert the number, not the relationship.** A test written as:

```csharp
Assert.Equal(activity.Total * factor, ceiling.AipBudget);   // passes whatever `factor` is
```

proves nothing — it restates the implementation. Write the literal:

```csharp
// A ₱250,000 AIP activity is ₱250,000 of ceiling. Not ₱250. Not ₱250,000,000.
Assert.Equal(250_000m, status.AipBudget);
```

## Why this is a good one to hand-code

Small blast radius · `dotnet test` feedback with no app running · fully reversible · `WfpCeilingServiceTests` is a near-identical sibling to pattern-match against. The hard part is judgement about *what to assert*, not mechanics — which is the part worth practising.

## Out of scope

Changing `WfpCeilingService` itself. If the test fails, that is V18-35's bug, not this ticket's.

## Done when

* An accept case and a reject case both assert a literal peso amount
* Deliberately reintroducing a `×1000` anywhere in the ceiling path turns the suite red
