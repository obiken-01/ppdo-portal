---
id: PPDO-3
title: 'Reviewer permission — OverrideCanReviewBudgetPlanning'
status: Done
project: PPDO Portal
labels: 
milestone: v1.8.0 — Office Users, AIP Redesign & Reviewer Flow
created: 2026-08-20
completed: 2026-09-01
parent: PPDO-28
blocked_by: (none)
related: (none)
url: https://linear.app/ralphoksiprojects/issue/PPDO-3
uuid: d3728259-87c1-48e0-b3e0-957fcce21e6b
---

# PPDO-3 — Reviewer permission — OverrideCanReviewBudgetPlanning

**V18-03** — plan: `docs/v1.8/Phase_Plan.md` §3.1; model confirmed by Ralph in `Office_User_Path_Findings.md` §6.2.

Each office gets **reviewer** users: read-only on content, and the sole authority to submit their office's work. This ticket adds the flag and its resolution only — the write-denial guard is V18-04, and the submit/lock workflow is Phase 4.

* **Per-user flag**, not a division flag: `User.OverrideCanReviewBudgetPlanning`.
* Resolution: SuperAdmin → true; everyone else → `Override ?? false`. **Admin is NOT auto-granted** — mirrors `CanManageAllocation` exactly.
* Write the backend permission **generically** (budget planning, not AIP-only) so LDIP and WFP can reuse it. Scope of *use* is AIP-first, but the flag is not named or resolved AIP-specifically.
* Expose on `MeResponse`; add to the User Management form (V18-08 restructures that form, but the field lands here).

TDD: `PermissionService` tests for SuperAdmin / Admin / Staff / office user × null / true / false.

**Done when:** the flag resolves correctly and is settable per user. Behaviour changes land in V18-04.
