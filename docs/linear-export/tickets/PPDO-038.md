---
id: PPDO-38
title: 'V18-39 — AIP scope resolver: OfficeScope × DivisionScope'
status: Done
project: PPDO Portal
labels: 
milestone: v1.8.0 — Office Users, AIP Redesign & Reviewer Flow
created: 2026-09-02
completed: 2026-09-03
parent: PPDO-32
blocked_by: PPDO-33
related: PPDO-4, PPDO-30, PPDO-46, PPDO-57, PPDO-60
url: https://linear.app/ralphoksiprojects/issue/PPDO-38
uuid: 3c95b144-cb92-44d6-a9b1-37488950dcd9
---

# PPDO-38 — V18-39 — AIP scope resolver: OfficeScope × DivisionScope

**In one line:** AIP is the first feature where division scoping applies *only* to PPDO users — neither existing resolver expresses that, so build the two-axis one.

**Spec: **`docs/v1.8/AIP_Foundation_Spec.md` §3.2 · **Size:** M · **Blocked by:** V18-32 · ⚠️ **TDD mandatory** · ❌ **Not a hand-coding candidate**

## Why

Three features, three different scope rules:

| Feature | Office axis | Division axis |
| -- | -- | -- |
| WFP | always | **always** |
| LDIP | always | never |
| **AIP** | always | **only when the caller is PPDO** |

No existing resolver expresses the third. `BudgetPlanningScope` was built to pair the two axes and **still has no consumer** — every site pairs them by hand, which is exactly what [PPDO-30](https://linear.app/ralphoksiprojects/issue/PPDO-30/budget-planning-allocation-setup-status-divisionid-is-not-scoped-to)'s division-axis leak was. This is its first real use.

The rule itself is already settled and documented: `Permission_Matrix.md` §3.1 and [PPDO-4](https://linear.app/ralphoksiprojects/issue/PPDO-4/scope-rule-division-is-not-a-scoping-axis-for-non-ppdo-office-users) — office users are scoped by office only, division explicitly not a factor.

## Scope

Resolve AIP reads through the paired resolver. A guest-office caller's division id is **ignored**, not honoured. A PPDO caller's narrows further.

## Watch out

⚠️ **This is the class of change where a wrong choice compiles cleanly and leaks data.** Both failure directions are silent:

* Honouring division for a guest office → they see a fraction of their own AIP and think data is missing.
* Ignoring it for PPDO → a division-scoped encoder sees every division's figures.

Neither throws. Neither shows up in a diff at the call site. Hence TDD, and hence not a ticket to hand-code — the compiler will not help.

* **Never** `OfficeScope.Resolve` **on a cross-office read path.** Use `ResolveForReview` / `ResolveForCeiling`. Teaching `Resolve` either grant promotes a cross-office *reader* into a cross-office *editor* at every write path that shares it. Read `OfficeScope`'s own remarks before touching it.
* A null `office_id` → `NoOffice` (id 0) → **sees nothing**. It is an incomplete record, not a privileged one (DECISION F / [RAL-258](https://linear.app/ralphoksiprojects/issue/RAL-258/office-config-default-landing-page-host-office-flag-decision-f)). Older comments asserting the inverse are stale.
* Clamp a caller-supplied `officeId`; do not 403 it. A 403 confirms the other office exists.

## Out of scope

The ownership column (V18-32 — this ticket consumes it). Review-specific scope, which is Phase 4.

## Done when

* PPDO caller narrows by division; guest-office caller does not, pinned by test
* A guest office's supplied division id is demonstrably ignored
* A guest office's supplied `officeId` is clamped, and the response is *their* data
* `office_id` null sees nothing
* `BudgetPlanningScope` finally has a consumer, or there is a written reason it still does not
