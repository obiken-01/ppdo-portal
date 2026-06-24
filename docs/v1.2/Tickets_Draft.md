# v1.2.0 — Linear Ticket Drafts (for review before creating in Linear)

These are the **Claude Code implementation prompts** per `docs/TICKET_PROMPT_STANDARD.md`.
Authoritative spec for all of them: `docs/v1.2/Allocation_Requirements.md`.

> **RAL numbers are placeholders** (`RAL-XX`) — assign the next free Linear numbers on creation and
> substitute into the branch names. **Dependency chain (set `blockedBy` in Linear):**
> T1 → T2 → T3 → T4; T1 → T5; T6 is independent (do early — T4/T5 consume it).
> All branches are `feature/v1.2-ral-XX-…` off `release/1.2.0`, PRs target `release/1.2.0` (**NOT main**).
> New tables/columns are **snake_case**.

| # | Title | Blocked by |
|---|---|---|
| T1 | Divisions + permission-model rebuild (retire PermissionGroup/enum/Observer) | — |
| T2 | Division config page (CRUD + CSV) | T1 |
| T3 | Allocation backend (ceiling, allocation, program-division) | T1 |
| T4 | Allocation page UI (ceiling + allocation + PPA→division tabs) | T2, T3, T6 |
| T5 | WFP division scoping (per-division records, filter, validation, setup gate) | T1, T6 |
| T6 | Shared `MoneyInput` component | — |

---

## T1 — Divisions + permission-model rebuild

```
Read CLAUDE.md, PROJECT_DOCUMENTATION_NET_AZURE.md, and PPDO_PROJECT_CONTEXT.md.
Read docs/v1.2/Allocation_Requirements.md §5 + §6 + §10 FULLY — authoritative for the data model,
permission resolution, and migration ordering. Read docs/v1.1/User_Roles_Permissions.md for the
model being REPLACED (PermissionGroup, Division enum, Observer all go away).

Read these files before writing code:
- backend/PPDO.Domain/Enums/Division.cs (the enum being retired)
- backend/PPDO.Domain/Entities/PermissionGroup.cs (entity being deleted)
- backend/PPDO.Domain/Entities/User.cs (Division enum + GroupId → division_id FK; add OverrideCanManageAllocation)
- backend/PPDO.Domain/Entities/Office.cs (offices is the FK parent of divisions)
- backend/PPDO.Application/Services/PermissionService.cs (resolution rules to rewrite)
- backend/PPDO.Application/Services/UserService.cs (GroupIdFor to delete; create/update division handling)
- backend/PPDO.Application/Common/DivisionScope.cs (rework onto division_id)
- backend/PPDO.Application/Services/InventoryService.cs, DistributionService.cs, PurchaseRequestService.cs (enum→division_id)
- backend/PPDO.Domain/Entities/PurchaseRequest.cs, Distribution.cs (Division enum column → division_id)
- backend/PPDO.Infrastructure/Repositories/PurchaseRequestRepository.cs, DeliveryRepository.cs (GetByDivisionAsync)
- backend/PPDO.Infrastructure/Services/AuthService.cs + JwtMiddleware (loads Group nav → load Division nav; div claim)
- backend/PPDO.Application/DTOs/Auth/MeResponseDto.cs (drop group, expose division flags)
- backend/PPDO.Infrastructure/Data/Configurations/OfficeConfiguration.cs (config pattern for new DivisionConfiguration)
- backend/PPDO.Tests/Application/PermissionServiceTests.cs, UserServiceTests.cs, InventoryServiceTests.cs,
  DistributionServiceTests.cs, PurchaseRequestServiceTests.cs (all assert on the old model — rewrite)
- frontend/src/types/user.ts (Division union → fetched list; drop groupId)
- frontend/src/app/(portal)/admin/users/page.tsx (group UI → division dropdown; remove Observer from role list)

Working branch: release/1.2.0.
Create feature/v1.2-ral-XX-divisions-permission-core off release/1.2.0 and open the PR against
release/1.2.0 (NOT main).

TDD: rewrite PermissionServiceTests + UserServiceTests with the new resolution first, then implement.

1. Migration (snake_case new table; inventory CLEAN-SLATE — prod inventory is empty, local will be wiped):
   - Create `divisions` (id, office_id FK→offices, code NVARCHAR(20) NULL, name NVARCHAR(200) NOT NULL,
     is_active, + 7 flag bits: can_access_inventory, can_access_reports, can_manage_users,
     can_manage_resource_links, can_access_budget_planning, can_upload_aip, can_manage_config;
     UNIQUE(office_id, name)). NO seed rows (loaded via CSV in T2).
   - users: add `division_id` INT NULL FK→divisions; drop `Division` (enum) column; drop `GroupId` + FK;
     add `OverrideCanManageAllocation` BIT NULL.
   - purchase_requests + distributions: replace `Division` (enum) column with `division_id` INT FK→divisions
     (drop the old data; no back-fill).
   - Drop the `PermissionGroups` table.
2. Domain: delete Division enum + PermissionGroup entity + PermissionGroupConfiguration; add Division entity
   + DivisionConfiguration; update User/PurchaseRequest/Distribution navs.
3. PermissionService: SuperAdmin → all true. Admin → all true EXCEPT CanManageAllocation. Staff →
   `OverrideX ?? user.Division?.<flag> ?? false`. New CanManageAllocationAsync: SuperAdmin→true,
   else `OverrideCanManageAllocation ?? false` (Admin NOT auto). Remove all Observer branches.
4. DivisionScope.Resolve → use division_id: Admin/SuperAdmin = All; Staff with division_id = For(id);
   Staff with null division_id = Nothing. Update repos' GetByDivisionAsync(int divisionId).
5. UserService: delete GroupIdFor; create/update set division_id directly (validate it exists & belongs to
   the user's office for office users); require division_id for Staff (not an enum); SuperAdmin/Admin null.
6. AuthService/JwtMiddleware/MeResponseDto: load Division nav instead of Group; /me exposes effective flags.
7. Minimal endpoint `GET /api/config/divisions?active=true&officeId=` (ApiResponse envelope) so the user
   form + later pages have a list. (Full CRUD is T2.)
8. UserRole enum: remove Observer. Frontend: types/user.ts Division → fetched list; user form uses a
   division dropdown (fetched) + remove Observer from the role options + drop the group UI.

Do NOT build the division config CRUD page (T2), the allocation tables (T3), or any WFP changes (T5).
Do NOT leave any reference to PermissionGroup, GroupId, the Division enum, or Observer anywhere
(acceptance: solution-wide search returns none). Do NOT keep a null-division "see all" path in
inventory — null division_id for Staff must resolve to EMPTY (DivisionScope.Nothing).

When done, commit with:
refactor(auth): replace PermissionGroup/Division enum/Observer with configurable divisions (RAL-XX)
```

---

## T2 — Division config page (CRUD + CSV)

```
Read CLAUDE.md, PROJECT_DOCUMENTATION_NET_AZURE.md, and PPDO_PROJECT_CONTEXT.md.
Read docs/v1.2/Allocation_Requirements.md §5 "Division config page & seeding" FULLY — authoritative for
the CSV column order, name-as-upsert-key, and the flag set.

Read these files before writing code (mirror the Offices config exactly):
- backend/PPDO.Application/Services/OfficeService.cs + IOfficeService.cs (CRUD + CSV upsert/export pattern)
- backend/PPDO.Functions/Functions/ConfigOfficeFunctions.cs (7-endpoint shape, ApiResponse envelope)
- backend/PPDO.Application/Common/Csv.cs + CsvImportResult + ApiResponse (shared helpers)
- backend/PPDO.Domain/Entities/Division.cs + DivisionConfiguration.cs (from T1)
- backend/PPDO.Tests/Application/OfficeServiceTests.cs (CSV upsert/export test pattern)
- frontend/src/app/(portal)/config/offices/page.tsx (page pattern: DataTable + Modal + CsvUpload/Download)
- frontend/src/lib/config.ts + frontend/src/types/config.ts (client + types)
- frontend/src/components/layout/Sidebar.tsx (add "Divisions" under the Configuration group)

Working branch: release/1.2.0.
Create feature/v1.2-ral-XX-division-config off release/1.2.0 and open the PR against release/1.2.0 (NOT main).

TDD: extend DivisionServiceTests with failing tests first (CSV upsert by name, flag round-trip), then implement.

1. DivisionService + IDivisionService: list (active/office filter), get, create, update, soft-delete,
   CSV upsert (key = name within office_code; code nullable), CSV export. Audit-log all writes (RAL-77 pattern).
2. ConfigDivisionFunctions: the 7 endpoints under /api/config/divisions, ApiResponse envelope,
   gated by CanManageConfig (list also allow CanManageUsers, matching offices).
   CSV columns EXACTLY: office_code, code, name, is_active, can_access_budget_planning,
   can_access_inventory, can_access_reports, can_manage_config, can_upload_aip, can_manage_users,
   can_manage_resource_links (flags TRUE/FALSE). Extend the T1 GET, don't duplicate it.
3. Frontend /config/divisions page: DataTable (Code · Name · Office · active flags · Status · Actions),
   create/edit Modal with the 7 flag checkboxes, name readonly on edit (it's the key), CSV upload/download.
   Add "Divisions" to the Configuration sidebar group + a tile on the /config dashboard.

Do NOT add seed rows in a migration — Ralph uploads the seed CSV via this page
(D:\RalphFiles\PPDO\PPDO\divisions_seed_template.csv). Do NOT expose can_manage_allocation as a
division flag (it is a per-user grant). Do NOT let CSV upload duplicate rows when a name already exists
(upsert by name).

When done, commit with:
feat(config): division config page with CRUD and CSV upsert/export (RAL-XX)
```

---

## T3 — Allocation backend (ceiling, allocation, program-division)

```
Read CLAUDE.md, PROJECT_DOCUMENTATION_NET_AZURE.md, and PPDO_PROJECT_CONTEXT.md.
Read docs/v1.2/Allocation_Requirements.md §3 + §5 + §7 FULLY — authoritative for the tables, the
ceiling≥Σallocation rule, the gross-total validation, and the supplemental-AIP ref-code carry-forward.

Read these files before writing code:
- backend/PPDO.Domain/Entities/AipProgram.cs, AipOffice.cs (ref-code structure for program_divisions)
- backend/PPDO.Application/Services/AipService.cs (where supplemental upload re-creates aip_programs;
  add ref-code carry-forward for program_divisions)
- backend/PPDO.Application/Services/WfpService.cs (units: AIP totals stored in THOUSANDS; ceilings/allocations in pesos)
- backend/PPDO.Application/Services/OfficeService.cs (config-CRUD + audit pattern to mirror)
- backend/PPDO.Application/Common/ServiceResult.cs + ApiResponse.cs
- backend/PPDO.Tests/Application/AipServiceTests.cs, WfpServiceTests.cs (patterns)

Working branch: release/1.2.0.
Create feature/v1.2-ral-XX-allocation-backend off release/1.2.0 and open the PR against release/1.2.0 (NOT main).

TDD: add AllocationServiceTests with failing tests first (Σ allocation ≤ ceiling; program_divisions
carry-forward by ref code), then implement.

1. Migration (snake_case): budget_ceilings(office_id, fiscal_year, amount; UNIQUE office+FY);
   division_allocations(division_id, fiscal_year, amount; UNIQUE division+FY);
   program_divisions(office_ref_code, program_ref_code, division_id; UNIQUE(office_ref_code,program_ref_code,division_id)).
2. AllocationService + interface: get/upsert ceiling per office+FY; list/upsert division allocations per
   office+FY with the rule Σ(allocations) ≤ ceiling (reject otherwise); list/set program→division
   assignments (Sector→Program only); a "setup status" query (ceiling? allocation? ≥1 assigned program?)
   per (office, FY, division) for the WFP gate. Audit-log all writes.
3. Supplemental-AIP carry-forward: when AipService re-creates aip_programs on a supplemental upload,
   program_divisions stays valid because it keys on (office_ref_code, program_ref_code) — confirm new
   programs surface as unassigned, existing ones keep their division. Add the matching logic/test.
4. AllocationFunctions: endpoints under /api/budget-planning/allocation (ApiResponse envelope), gated by
   CanManageAllocation. (UI is T4.)

Do NOT build the Allocation page UI (T4). Do NOT change WfpService validation here (T5). Keep ceiling/
allocation amounts in PESOS; do NOT multiply by 1000 (that conversion lives in the WFP page layer only).

When done, commit with:
feat(budget-planning): allocation backend — ceiling, division allocation, PPA assignment (RAL-XX)
```

---

## T4 — Allocation page UI

```
Read CLAUDE.md, PROJECT_DOCUMENTATION_NET_AZURE.md, and PPDO_PROJECT_CONTEXT.md.
Read docs/v1.2/Allocation_Requirements.md §3 FULLY — authoritative for the two tabs, the stacked bar,
and the PPA→division grid behavior.

Read these files before writing code:
- frontend/src/app/(portal)/budget-planning/wfp/page.tsx (the Sector→Program→… grid + collapse pattern to reuse/collapse to Sector→Program)
- frontend/src/components/ui/Modal.tsx, DataTable.tsx, Toast.tsx (reuse)
- frontend/src/components/ui/MoneyInput.tsx (from T6 — use for all money fields)
- frontend/src/lib/wfp.ts + lib/aip.ts (client patterns for the AIP hierarchy + blob/JWT calls)
- frontend/src/lib/me-cache.ts (useMe; gate on canManageAllocation)
- frontend/src/components/layout/Sidebar.tsx (add "Allocation" BETWEEN AIP and WFP)

Working branch: release/1.2.0.
Create feature/v1.2-ral-XX-allocation-ui off release/1.2.0 and open the PR against release/1.2.0 (NOT main).

1. Route /budget-planning/allocation gated by canManageAllocation (hidden in sidebar otherwise);
   add the sidebar link between AIP and WFP. FY + office selectors (office locked for non-PPDO finance).
2. Tab 1 — Ceiling & Division Allocation: ceiling MoneyInput; one MoneyInput per division; live
   "Allocated ₱X of ₱Y · Remaining ₱Z"; block save + red when over ceiling; stacked horizontal bar
   (segments per division + grey remainder + red overflow), % of ceiling per division.
3. Tab 2 — PPA→Division: reuse the WFP hierarchy collapsed to Sector→Program; columns = ref code,
   program name, "Multi-division?" toggle (OFF = radio/one division, ON = multi), one checkbox column
   per division; "Unassigned" filter/badge + bulk-assign + per-division assigned counts in headers.
4. lib/allocation.ts client + types; wire to the T3 endpoints.

Do NOT introduce a new money-formatting helper — use MoneyInput / formatMoney from T6. Flat design
(no rounded corners); reuse components/ui. Keep list/grid payloads slim (PERFORMANCE_GUIDELINES).

When done, commit with:
feat(budget-planning): allocation page — ceiling, division allocation, PPA assignment UI (RAL-XX)
```

---

## T5 — WFP division scoping

```
Read CLAUDE.md, PROJECT_DOCUMENTATION_NET_AZURE.md, and PPDO_PROJECT_CONTEXT.md.
Read docs/v1.2/Allocation_Requirements.md §4 + §7 FULLY — authoritative for per-division WFP records,
the division filter (skip for Admin/SuperAdmin/allocation holders), gross-total division-budget
validation, and the setup-complete gate.

Read these files before writing code:
- backend/PPDO.Domain/Entities/WfpRecord.cs + WfpRecordConfiguration.cs (UNIQUE becomes aip+office+division)
- backend/PPDO.Application/Services/WfpService.cs (SaveAsync full-replace; add division scope + 3rd validation)
- backend/PPDO.Domain/Interfaces/IWfpRepository.cs + WfpRepository.cs (FindByAipAndOfficeAsync → +division)
- backend/PPDO.Application/Services/AllocationService.cs (from T3 — setup-status + allocation lookups)
- backend/PPDO.Tests/Application/WfpServiceTests.cs (extend)
- frontend/src/app/(portal)/budget-planning/wfp/page.tsx (office selector + grid; add division scope + banner)
- frontend/src/components/ui/MoneyInput.tsx (from T6)
- frontend/src/lib/me-cache.ts (useMe — role, division_id, canManageAllocation, officeId)

Working branch: release/1.2.0.
Create feature/v1.2-ral-XX-wfp-division-scoping off release/1.2.0 and open the PR against release/1.2.0 (NOT main).

TDD: extend WfpServiceTests first (per-division uniqueness; division-budget gross validation; setup gate),
then implement.

1. Migration: wfp_records UNIQUE → (aip_record_id, office_id, division_id); add division_id FK→divisions.
2. WfpService: scope save/read to (aip, office, division); SaveAsync no longer wipes other divisions'
   records. New rule: Σ GROSS total_appropriation for the division ≤ that division's division_allocation
   (reject; backend-enforced). Block save when setup incomplete (no ceiling / no allocation / no assigned
   program) with a clear error. Finalize/unlock are per (aip, office, division).
3. WFP read endpoints: enforce the division filter server-side — non-Admin Staff without
   CanManageAllocation see only their division's programs; Admin/SuperAdmin/allocation holders see all
   divisions within their office scope.
4. Frontend: build the grid from program_divisions for the user's division (Admin/allocation → all +
   a division picker); "Division budget: allocated / used / remaining" banner; "Setup incomplete" banner
   listing what's missing; localStorage draft key → wfp_draft_{aip}_{office}_{division}.

Do NOT change the WFP Excel report columns (out of scope for v1.2). Keep the AIP-budget (×1000) and
per-line quarterly≤net rules as-is — the division-budget rule is ADDITIONAL.

When done, commit with:
feat(budget-planning): per-division WFP scoping, division-budget validation, setup gate (RAL-XX)
```

---

## T6 — Shared `MoneyInput` component

```
Read CLAUDE.md, PROJECT_DOCUMENTATION_NET_AZURE.md, and PPDO_PROJECT_CONTEXT.md.
Read docs/v1.2/Allocation_Requirements.md §8 FULLY — authoritative for the component contract.

Read these files before writing code:
- frontend/src/components/ui/Modal.tsx (components/ui conventions + flat design)
- frontend/src/app/(portal)/budget-planning/wfp/page.tsx (current hand-rolled money inputs/fmtNum it will replace)
- frontend/src/components/ui/DataTable.tsx (for the read-only formatMoney usage in cells)

Working branch: release/1.2.0.
Create feature/v1.2-ral-XX-money-input off release/1.2.0 and open the PR against release/1.2.0 (NOT main).

1. components/ui/MoneyInput.tsx: ₱ prefix label; type="text" inputMode="decimal"; holds a number;
   onChange(value: number | null) emits the CLEAN numeric (commas display-only, never leave the
   component); accepts digits + one decimal, caps 2 decimals; live comma formatting with caret-position
   preservation (fallback: format-on-blur). Props: value, onChange, disabled, placeholder, className, min.
2. lib/money.ts: formatMoney(n) / parseMoney(str) shared util so read-only table cells and the input
   share one formatter.
3. Adopt it in the WFP popup money fields (totalAppropriation, Q1–Q4) as the first consumer; replace the
   local fmtNum there with formatMoney.

Do NOT use type="number" (it cannot render commas). Do NOT mass-migrate inventory money inputs in this
ticket — budget planning first; inventory adopts opportunistically later.

When done, commit with:
feat(ui): shared MoneyInput component with peso prefix and comma formatting (RAL-XX)
```

---

## After review
Once approved, create the six issues in Linear (assign real RAL numbers, set the `blockedBy` chain above),
paste each prompt as the kickoff, and substitute the numbers into the branch names. Suggested build order:
**T1 → T6 → T2 → T3 → T4 → T5** (T6 early so T4/T5 can consume `MoneyInput`).
