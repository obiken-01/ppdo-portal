---
id: PPDO-17
title: 'Allocation page: office picker for cross-office ceiling holders (pull V18-48 forward)'
status: Done
project: PPDO Portal
labels: 
milestone: v1.8.0 — Office Users, AIP Redesign & Reviewer Flow
created: 2026-09-01
completed: 2026-09-02
parent: (none)
blocked_by: PPDO-18
related: PPDO-2, PPDO-4, PPDO-20, PPDO-29, PPDO-58
url: https://linear.app/ralphoksiprojects/issue/PPDO-17
uuid: a8a8527e-3696-430f-bef5-4b1b49cd3d83
---

# PPDO-17 — Allocation page: office picker for cross-office ceiling holders (pull V18-48 forward)

## Problem

[RAL-243](https://linear.app/ralphoksiprojects/issue/RAL-243/pbo-ceiling-permission-overridecanmanagepboceiling-set-budgetceiling) shipped `OverrideCanManagePboCeiling` — authority to set a `BudgetCeiling` for **any** office. But the allocation page pins the office selector to the caller's own office for every non-host user:

```
isOfficeUser = me != null && !me.isHostOffice
```

When true it renders a static office name instead of `OfficeSelect`. A PBO officer is, realistically, a **non-PPDO user** sitting in the Provincial Budget Office — so today the grant is real and **unreachable through the UI**: they can only reach the one office whose ceiling they have no reason to set.

The backend is already fine — the endpoints take `officeId` and the ceiling PUT is gated on `CanManagePboCeiling` with no clamp. This is a frontend gate only.

## Scope

Pulls the **office-picker half** of `V18-48` forward from Phase 4. The rest of V18-48 (PBO ceiling management UI) stays in Phase 4.

## ✅ Partly done already — read before starting

The **gate and the labels are written and committed**, in `ef3d51e` on `feature/v1.8.0-ral-246-audit-coverage` (PR #273, the top of the Phase 1 stack). They were done while testing [RAL-243](https://linear.app/ralphoksiprojects/issue/RAL-243/pbo-ceiling-permission-overridecanmanagepboceiling-set-budgetceiling) and committed there rather than split out. So this ticket does **not** start from a clean page.

**Already done:**

* The division split and the `PPA → Division` tab are gated on `canSetAllocations && selectedOffice.isHostOffice` — division is a host-office-only axis ([RAL-250](https://linear.app/ralphoksiprojects/issue/RAL-250/scope-rule-division-is-not-a-scoping-axis-for-non-ppdo-office-users)). Guest offices get ceiling controls and nothing else.
* `effectiveTab` fallback so switching office off PPDO while on the PPA tab does not strand the user.
* Collapsed fund rows drop `Allocated · Remaining` for offices with no split, and read **Set** rather than **Ceiling only**.
* Role-based labelling: a PBO-only caller sees **Budget Ceilings** in sidebar, breadcrumb, header and tab, with no division vocabulary. New `frontend/src/lib/budget-planning-labels.ts` is the single source for all four surfaces.
* Fixed a latent bug: the Save Allocations button had `hidden={!canSetAllocations}` while its `className` set `display:flex`, so it painted next to the read-only notice. The branch was hoisted; the attribute is no longer relied on.
* New `frontend/src/components/ui/InfoTip.tsx`, used on the General Fund `Required` badge and the read-only ceiling line.

⚠️ That work is `tsc`/lint clean but **was never browser-verified** — see PR #273. A manual pass on the allocation page belongs in this ticket's test plan.

**What remains — the actual scope of this ticket:**

1. **The office picker itself** (step 1 of the prompt). This is the part that makes [RAL-243](https://linear.app/ralphoksiprojects/issue/RAL-243/pbo-ceiling-permission-overridecanmanagepboceiling-set-budgetceiling)'s grant reachable, and none of it is written.
2. **The default-office decision** (step 3 of the prompt).
3. Re-confirming the labelling still holds once the picker exists (step 2) — verification, not new code.

## Why not model the PBO officer as a PPDO admin instead

Considered and rejected — it is mechanically impossible. Seven flags return `true` for Admin **before any override is read** (`if (IsAdminOrAbove(user)) return true;` at the top of each method in `PermissionService`):

`CanAccessInventory` · `CanAccessReports` · `CanManageUsers` · `CanAccessProfile` · `CanManageResourceLinks` · `CanAccessBudgetPlanning` · `CanUploadAip`

So "create a PPDO admin and restrict its other permissions" cannot be expressed — the override is never consulted. The only subtractive mechanism in the codebase is `ReviewerWriteGuard`, which is specific to cross-office reviewers. Identity points the same way: `OfficeId` drives audit attribution, landing page and every office-scoped view, so filing a PBO employee under PPDO misleads code that has not been written yet.

## ⚠️ Ordering

Blocked by the allocation clamp ticket, whose PBO carve-out must exist before the clamp lands — otherwise the clamp makes this grant unreachable again. This ticket shipping **first** regresses nothing, since nothing is clamped today.

## Spec status

No `SPEC_STANDARD.md` spec of its own; authority is `docs/v1.8/Phase_Plan.md` V18-48. Adds no table, endpoint, permission or screen — it changes gating on an existing one.

---

## Implementation prompt

```
Read CLAUDE.md, PROJECT_DOCUMENTATION_NET_AZURE.md, and PPDO_PROJECT_CONTEXT.md.
Read docs/v1.8/Phase_Plan.md V18-48 and V18-02, and docs/v1.8/Permission_Matrix.md rows for
CanManagePboCeiling and CanManagePpdoAllocation, FULLY — they are the authority for this ticket.

Read these files before writing code:
- frontend/src/app/(portal)/budget-planning/allocation/page.tsx (the isOfficeUser branch that
  swaps OfficeSelect for a static office name; also canSetCeiling / canSetAllocations /
  showDivisionSplit, which already distinguish the two grants)
- frontend/src/lib/budget-planning-labels.ts (the page renames itself for a PBO-only caller —
  the picker must not undo that)
- frontend/src/types/auth.ts (MeResponse.isHostOffice, canManagePboCeiling,
  canManagePpdoAllocation)
- frontend/src/components/ui/OfficeSelect.tsx (the control being conditionally rendered)
- backend/PPDO.Functions/Functions/AllocationFunctions.cs (confirm the endpoints already take
  officeId and the ceiling PUT is gated on CanManagePboCeiling — no backend change here)
- backend/PPDO.Application/Common/OfficeScope.cs (IsHostOfficeUser — the one place "is this
  caller cross-office?" is answered; do not compare office codes to "PPDO")

NOTE: the gate and the labels described in the ticket body are ALREADY COMMITTED — see
commit ef3d51e. budget-planning-labels.ts exists; the division split and PPA tab are already
gated on the office; InfoTip exists. Read them first and do not re-implement them. Step 1 below
is the only part that is unwritten.

Before changing anything, state in one line each:
- Current behaviour: <what the office selector does today for a non-host user>
- Target behaviour: <what it must do after>

Working branch: release/1.8.0.
Create feature/v1.8.0-ral-277-allocation-office-picker off release/1.8.0 and open the PR
against release/1.8.0 (NOT main).

1. Replace the page's office-axis condition. Today it is "is this caller the host office?"
   (isOfficeUser = me != null && !me.isHostOffice). It must become "is this caller
   cross-office?" — host-office membership OR canManagePboCeiling widens the axis. Introduce a
   single named derived flag for it rather than inlining the OR at the two use sites.
   Verify: log in as a Staff user in Provincial Budget Office with Manage PBO Ceiling = Yes and
   everything else Inherit. The office selector renders and lists every active office. Log in as
   a plain office user (user.gso) — still pinned to their own office, no selector.

2. Confirm the re-labelling still holds for that caller. A PBO-only holder must still see
   "Budget Ceilings" in sidebar, breadcrumb, page header and tab, with no division vocabulary
   anywhere on the page, and no PPA tab.
   Verify: same login as step 1, read all four surfaces.

3. Confirm the default office selection is sane for the new caller. A cross-office ceiling
   holder who is NOT the host office should not silently default to their own office (the one
   office whose ceiling they have no reason to set) — leave the selector unset with the existing
   "Select an office to configure allocation" empty state, or default to the host office.
   Pick one, and say which in the PR.
   Verify: first page load as the PBO user shows the chosen state, not a half-loaded panel.

Risks and rollback:
- Widening the office axis on a page that writes ceilings for any office is a scope change →
  rollback: revert the commit; no migration, no data written by this change.
- The condition is used for more than the picker if inlined — that is why step 1 asks for one
  named flag. A second inlined OR is how the WFP division clamp ended up one dimension short.

Stop and get my sign-off before: changing anything in OfficeScope, PermissionService, or any
backend gate. This ticket is frontend-only — if it appears to need a backend change, it has
collided with the clamp ticket; stop and say so.

Do NOT clamp `remaining` at zero anywhere (V18-48's standing warning — a cut ceiling leaves a
legitimately negative remaining, and that is the signal that must block submit). Do NOT add the
division split or the PPA tab for guest offices — division is a host-office-only axis (RAL-250).
Do NOT touch the ceiling PUT's permission gate.

When done, commit with:
feat(budget-planning): office picker for cross-office ceiling holders (RAL-277)
```
