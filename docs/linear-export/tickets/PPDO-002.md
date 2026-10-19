---
id: PPDO-2
title: 'PBO ceiling permission — OverrideCanManagePboCeiling (set BudgetCeiling for any office)'
status: Done
project: PPDO Portal
labels: 
milestone: v1.8.0 — Office Users, AIP Redesign & Reviewer Flow
created: 2026-08-20
completed: 2026-09-01
parent: PPDO-28
blocked_by: (none)
related: PPDO-17, PPDO-18
url: https://linear.app/ralphoksiprojects/issue/PPDO-2
uuid: e40603db-06bd-49b2-b946-1c2a556bb2f8
---

# PPDO-2 — PBO ceiling permission — OverrideCanManagePboCeiling (set BudgetCeiling for any office)

**V18-02** — plan: `docs/v1.8/Phase_Plan.md` §3.1; reasoning in `AIP_Requirements_Review.md` §4.3 item 4.

The Provincial Budget Office sets each office's `BudgetCeiling`. Today the only allocation grant is the PPDO finance-officer one, which is not the same authority: PBO sets ceilings **for any office**, PPDO splits its own ceiling across its divisions.

Add a new **per-user** flag `OverrideCanManagePboCeiling`, mirroring `CanManageAllocation`'s plumbing exactly:

* resolution: SuperAdmin → true; everyone else → `Override ?? false`. **Admin is NOT auto-granted** — same deliberate choice as the allocation flag.
* it is *not* a division flag, so it does not go on `Division`.
* expose on `MeResponse`; gate the ceiling write endpoints in `AllocationFunctions` on it.

TDD: extend the `PermissionService` tests with the resolution cases first.

**Done when:** a user with only this flag can set any office's ceiling and nothing else; an Admin without it cannot; the PPDO allocation flag's behaviour is untouched.
