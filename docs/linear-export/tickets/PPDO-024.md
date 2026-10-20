---
id: PPDO-24
title: 'Inventory: replace hardcoded division lists with configurable divisions (Create PR, Distribution, PR List)'
status: Canceled
project: PPDO Portal
labels: 
milestone: Backlog — Unscheduled
created: 2026-07-13
completed: 2026-08-05
parent: (none)
blocked_by: (none)
related: RAL-97
url: https://linear.app/ralphoksiprojects/issue/PPDO-24
uuid: 5aee223b-a377-4221-a938-8a0b7629f96e
---

# PPDO-24 — Inventory: replace hardcoded division lists with configurable divisions (Create PR, Distribution, PR List)

## Problem

Three Inventory pages each hardcode an identical, stale 5-item division list instead of reading from the configurable `divisions` table (Config → Divisions) introduced in v1.2 ([RAL-97](https://linear.app/ralphoksiprojects/issue/RAL-97/divisions-permission-model-rebuild-retire-permissiongroupdivision), which retired the old `Division` enum). If an admin renames, deactivates, or adds a division via Config → Divisions, these pages silently go out of sync with the real config.

## Where it's hardcoded (frontend only)

* `frontend/src/app/(portal)/inventory/create-pr/page.tsx:54` — `const DIVISIONS: string[] = ["Admin", "Planning", "RM", "MIS", "SPD"];`, consumed at line 782 to populate the required "Division" `<select>` in the PR header form.
* `frontend/src/app/(portal)/inventory/distribution/page.tsx:36` — same literal array, consumed at lines 161 and 581 (the "Add Division" split-distribution row select, and a filter select).
* `frontend/src/app/(portal)/inventory/pr-register/page.tsx:69` — same literal array, consumed at line 722 (Division filter dropdown, admin-only).

Git blame: the [RAL-97](https://linear.app/ralphoksiprojects/issue/RAL-97/divisions-permission-model-rebuild-retire-permissiongroupdivision) migration commit (`a55aef6`) touched `create-pr/page.tsx:54` but only removed the deleted `Division` TS type annotation (`Division[]` → `string[]`) — it left the hardcoded literal values untouched. `distribution/page.tsx` (blame `fa86e79`) and `pr-register/page.tsx` (blame `d485be6`) predate [RAL-97](https://linear.app/ralphoksiprojects/issue/RAL-97/divisions-permission-model-rebuild-retire-permissiongroupdivision) entirely and were never touched by that migration — pure leftovers.

**Practical impact:** the backend already resolves division by name against the real `divisions` table and rejects unknown names — see `PurchaseRequestService.cs:130-133`, which throws `"Division '{dto.Division}' was not found. Configure it in Config → Divisions first."` — so a stale hardcoded option can pass client-side validation but fail at submission with a confusing error, instead of the dropdown simply reflecting reality.

## Backend — already correctly migrated, no changes needed

* `backend/PPDO.Domain/Entities/PurchaseRequest.cs:34` — `public int DivisionId { get; set; }` (proper FK, nav property `Division? Division` at line 107).
* `backend/PPDO.Domain/Entities/Distribution.cs:25` — same pattern.
* `backend/PPDO.Application/Services/PurchaseRequestService.cs:52-65, 130-140, 231-237` — `ResolveDivisionByNameAsync`, loads `IRepository<Division>` and matches by name.
* `backend/PPDO.Application/Services/DistributionService.cs:149-154` — identical resolve-by-name pattern.

This is purely a frontend data-source fix.

## Correct pattern to follow (already used elsewhere in the app)

`frontend/src/lib/config.ts:200-213` exports `listDivisions(params: { active?: ActiveFilter; officeId?: number })` → `GET /api/config/divisions`, returning `DivisionResponse[]` (`frontend/src/types/config.ts:198-213`).

Correctly-aligned consumers to model the fix on:

* `frontend/src/app/(portal)/admin/users/page.tsx:29,506` — `setDivisions(await listDivisions({ active: "true" }))`
* `frontend/src/app/(portal)/budget-planning/wfp/page.tsx:696` and `wfp/entry/page.tsx:783` — `listDivisions({ active: "true", officeId: selectedOfficeId })`
* `frontend/src/app/(portal)/budget-planning/allocation/page.tsx:261` — same
* `frontend/src/app/(portal)/config/divisions/page.tsx:212` — the config CRUD page itself

## Scope check — rest of Inventory is unaffected

Confirmed no hardcoded division arrays in `receive-delivery/page.tsx`, `items-master/page.tsx`, `item-ledger/page.tsx` (no division references at all), or `pr-report/page.tsx` / `pr-report/[prNo]/page.tsx` (only display `pr.division` / `dist.division` as read-only strings from the API response — no dropdown to fix).

## Suggested fix

1. Replace the three local `DIVISIONS` constants with a fetch via `listDivisions({ active: "true" })` on mount, rendering `<option>`s from `DivisionResponse.name`.
2. Consider extracting a shared `useDivisions()` hook — the same fetch-active-divisions-for-dropdown logic is now duplicated across ~5 pages (the 3 above plus the already-correct `admin/users`, `budget-planning/*` pages).
3. Remove the dead literal arrays at `create-pr/page.tsx:54`, `distribution/page.tsx:36`, `pr-register/page.tsx:69`.

## Out of scope

Backend/domain layer — already correctly migrated to the `divisions` table, no changes needed there.
