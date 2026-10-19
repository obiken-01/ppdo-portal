---
id: PPDO-18
title: 'Office clamp on AllocationFunctions — cross-office read leak, with a PBO ceiling carve-out'
status: Done
project: PPDO Portal
labels: 
milestone: v1.8.0 — Office Users, AIP Redesign & Reviewer Flow
created: 2026-09-01
completed: 2026-09-02
parent: (none)
blocked_by: (none)
related: PPDO-2, PPDO-5, PPDO-30, PPDO-135, RAL-274
url: https://linear.app/ralphoksiprojects/issue/PPDO-18
uuid: 1cfef96b-a144-4a26-8102-5656143077f4
---

# PPDO-18 — Office clamp on AllocationFunctions — cross-office read leak, with a PBO ceiling carve-out

## Problem

Every GET in `AllocationFunctions` reads `officeId` straight from the query string with **no office clamp**, gated only on `CanAccessBudgetPlanning` — which office users have **on by default**. Any office user can read any other office's ceilings and division allocations by editing the URL.

Same class as the [RAL-229](https://linear.app/ralphoksiprojects/issue/RAL-229/clamp-get-budget-planningdashboardoffice-to-the-callers-own-office) dashboard IDOR, which was fixed. These endpoints were not, and no existing ticket or `V18-` item covers them (checked Linear and `docs/v1.8/Phase_Plan.md`).

The broad *permission* is correct and must stay: the comment at `AllocationFunctions.cs:50` explains that every WFP user, including non-finance office users, needs to know whether a ceiling exists for the setup-complete gate. **The missing piece is the office clamp, not the grant.**

## ⚠️ The carve-out is part of this ticket, not a follow-up

[RAL-243](https://linear.app/ralphoksiprojects/issue/RAL-243/pbo-ceiling-permission-overridecanmanagepboceiling-set-budgetceiling) shipped `OverrideCanManagePboCeiling` — authority to set a ceiling for **any** office. A naive clamp catches that holder and makes the grant unreachable, breaking the office-picker ticket this one blocks. The carve-out must land with the clamp.

## Spec status

No `SPEC_STANDARD.md` spec of its own. Authority is the [RAL-228](https://linear.app/ralphoksiprojects/issue/RAL-228/officescope-primitive-office-clamp-helper-for-budget-planning)/229 pattern plus `OfficeScope`'s class remarks. Adds no table, endpoint, permission or screen — it changes gating on existing ones — so the "write the spec first" rule does not trigger.

---

## Implementation prompt

```
Read CLAUDE.md, PROJECT_DOCUMENTATION_NET_AZURE.md, and PPDO_PROJECT_CONTEXT.md.
Read docs/v1.8/Permission_Matrix.md FULLY, and the class remarks on
backend/PPDO.Application/Common/OfficeScope.cs in full — including the paragraph explaining why
ResolveForReview is a separate entry point from Resolve. That paragraph is the design this
ticket has to extend without breaking.

Read these files before writing code:
- backend/PPDO.Functions/Functions/AllocationFunctions.cs (six GETs taking a caller-supplied
  officeId with no clamp; the ceiling PUT gated on CanManagePboCeiling; the division-allocation
  and PPA PUTs gated on CanManagePpdoAllocation)
- backend/PPDO.Functions/Functions/ConfigHttp.cs (ClampOfficeId for caller-supplied ids on
  list/query endpoints, DenyForeignOffice for known-owner records — the two are not
  interchangeable; read both doc comments)
- backend/PPDO.Application/Common/OfficeScope.cs (Resolve, Clamp, Permits, ResolveForReview,
  IsHostOfficeUser)
- backend/PPDO.Functions/Functions/BudgetPlanningDashboardFunctions.cs (RAL-229 clamp on the
  same shape of endpoint — copy this idiom, do not invent a second one)
- backend/PPDO.Tests/Functions/BudgetPlanningDashboardFunctionsTests.cs (the test pattern:
  assert the officeId that reaches the SERVICE, not the status code — a regression still
  returns 200, just with someone else's data)
- backend/PPDO.Tests/Functions/FunctionHttpHarness.cs (in-memory HttpRequestData harness)

Before changing anything, state in one line each:
- Current behaviour: <what an office user can read via officeId on these endpoints today>
- Target behaviour: <what they can read after>

Working branch: release/1.8.0.
Create feature/v1.8.0-ral-276-allocation-office-clamp off release/1.8.0 and open the PR
against release/1.8.0 (NOT main).

TDD: add backend/PPDO.Tests/Functions/AllocationFunctionsTests.cs with failing tests first,
then implement. There is no such file today.

1. Add a ceiling-scope entry point to OfficeScope — a third resolver alongside Resolve and
   ResolveForReview, taking the caller plus canManagePboCeiling, returning All for a holder and
   Resolve(user) otherwise. Do NOT teach Resolve the flag: OfficeScope's own remarks explain
   that Resolve feeds the write paths through Clamp, so a branch there silently promotes the
   grant everywhere with no diff at any write site to notice it. Same reasoning as RAL-257,
   applied to a grant that legitimately covers reads AND ceiling writes — which is why it needs
   its own entry point rather than reusing ResolveForReview.
   Verify: dotnet test backend/PPDO.slnx --filter OfficeScopeTests

2. Clamp the six GETs in AllocationFunctions through ConfigHttp using the new resolver. A
   host-office caller and a PBO ceiling holder keep cross-office reads; every other caller is
   clamped to their own office regardless of the officeId they send. Clamp AFTER the existing
   int.TryParse validation so a malformed officeId is still a 400, not a silent clamp.
   Verify: dotnet test backend/PPDO.slnx --filter AllocationFunctionsTests — one test per
   endpoint asserting the officeId passed to the service, plus a foreign-office case for each
   caller kind (host / PBO holder / plain office user).

3. Apply the same treatment to the writes, per grant. The ceiling PUT stays cross-office for a
   PBO holder — that is the grant. The division-allocation and PPA PUTs are on
   CanManagePpdoAllocation, a host-office authority over PPDO's own split; clamp those.
   Verify: dotnet test backend/PPDO.slnx --filter AllocationFunctionsTests, plus curl the
   ceiling PUT as the PBO test user against another office's id and confirm 200.

4. Full suite, and confirm no existing test changed behaviour. The GETs' permission gate
   (CanAccessBudgetPlanning) must be untouched — this ticket adds a clamp, it does not narrow
   a grant.
   Verify: dotnet test backend/PPDO.slnx

Risks and rollback:
- A clamp that catches the PBO holder makes RAL-243 unreachable and breaks the picker ticket.
  This is the most likely defect; step 1's separate entry point and step 2's per-caller-kind
  tests exist for it. Rollback: revert the commit — no data is affected.
- Clamping a read that WFP's setup-complete gate depends on would block WFP entry for office
  users. Guarded by leaving the permission gate alone and clamping only the office axis; if WFP
  entry breaks, that is the symptom to look for.
- No migration. Nothing is written or altered.

Stop and get my sign-off before: any change to PermissionService, to OfficeScope.Resolve itself
(as opposed to adding a new entry point), or to the GETs' permission gate. All three are
office-scope guards where a mistake is not caught by the compiler.

Do NOT merge the new resolver back into Resolve or ResolveForReview "for simplicity" — read the
warning in OfficeScope's remarks first. Do NOT change which permission gates any endpoint. Do
NOT add office scoping to AIP endpoints here, even though they need it — that is V18-39, a
different and larger design.

When done, commit with:
fix(auth): clamp office on allocation endpoints, with a PBO ceiling carve-out (RAL-276)
```
