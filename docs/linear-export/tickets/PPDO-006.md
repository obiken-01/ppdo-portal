---
id: PPDO-6
title: 'DenyReviewerWrite guard — the first subtractive permission'
status: Done
project: PPDO Portal
labels: 
milestone: v1.8.0 — Office Users, AIP Redesign & Reviewer Flow
created: 2026-08-20
completed: 2026-09-01
parent: PPDO-28
blocked_by: PPDO-3
related: PPDO-19, PPDO-43
url: https://linear.app/ralphoksiprojects/issue/PPDO-6
uuid: 24e946da-af31-48ab-b4c5-f6fbd00f2e51
---

# PPDO-6 — DenyReviewerWrite guard — the first subtractive permission

**V18-04** — plan: `docs/v1.8/Phase_Plan.md` §3.1; caveat first raised in `Office_User_Path_Findings.md` §6.2. Depends on V18-03 ([RAL-244](https://linear.app/ralphoksiprojects/issue/RAL-244/reviewer-permission-overridecanreviewbudgetplanning)).

⚠️ **Every permission flag in this codebase is purely *additive*** — each one only ever grants. A reviewer needs the opposite: **denied** write on content, while being the **only** role allowed to submit. The existing idiom

```csharp
await ConfigHttp.AuthorizeAsync(req, _jwt, p => p.CanX, ct)
```

cannot express *"deny if the caller has X"*. Bolting it on per-endpoint with an inline `if` is how one endpoint gets missed.

**Scope:**

* A companion guard — `DenyReviewerWriteAsync` (or a shape that composes with `AuthorizeAsync`) — living in `PPDO.Application/Common` so it is reachable from tests. ⚠️ `PPDO.Functions` has **no** `InternalsVisibleTo("PPDO.Tests")`, so keep the testable logic in Application and the `ConfigHttp` side a thin wrapper.
* Apply it to **every** budget-planning write endpoint (AIP, LDIP, WFP, allocation), and add a test per endpoint asserting a reviewer is refused.
* SuperAdmin behaviour must be decided explicitly and pinned by test — SuperAdmin resolves the reviewer flag to true, so a naive guard would lock SuperAdmin out of every write in the system.

**Done when:** a reviewer cannot write content anywhere in budget planning, no non-reviewer's behaviour changes, and the endpoint list is covered by tests rather than by inspection.
