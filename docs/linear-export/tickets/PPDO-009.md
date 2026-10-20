---
id: PPDO-9
title: 'Rename CanManageAllocation → Manage PPDO Allocation'
status: Done
project: PPDO Portal
labels: 
milestone: v1.8.0 — Office Users, AIP Redesign & Reviewer Flow
created: 2026-08-20
completed: 2026-08-28
parent: PPDO-28
blocked_by: (none)
related: PPDO-87
url: https://linear.app/ralphoksiprojects/issue/PPDO-9
uuid: 28041528-907f-468a-a763-2dc216c1fa27
---

# PPDO-9 — Rename CanManageAllocation → Manage PPDO Allocation

**V18-01** — plan: `docs/v1.8/Phase_Plan.md` §3.1.

Rename the existing per-user grant `CanManageAllocation` to **"Manage PPDO Allocation" (PPDO finance officer)**, so it reads unambiguously once the PBO ceiling permission (V18-02) exists beside it. Requested in the v1.8.0 requirements; reasoning in `AIP_Requirements_Review.md` §4.3 item 5.

**No behaviour change** — resolution stays SuperAdmin → true, everyone else → `OverrideCanManageAllocation ?? false` (Admin is *not* auto-granted).

Mechanical but wide. Touches at least: `User.OverrideCanManageAllocation`, `PermissionService`, the Functions gates that call it, `MeResponse`, the User Management form labels, and the frontend types. Worth its own commit so the rename diff stays readable and doesn't hide a behaviour change inside V18-02's.

**Done when:** the flag reads as "Manage PPDO Allocation" everywhere a human sees it, the existing allocation tests still pass unchanged, and `grep` finds no stale label.
