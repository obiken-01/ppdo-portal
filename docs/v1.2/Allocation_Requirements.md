# PPDO Portal — v1.2.0: Budget Allocation, Configurable Divisions & Permission Model

_Version 1.0 (draft) | Date: June 24, 2026 | Target: PPDO Portal v1.2.0_
_Covers: Budget Planning › Allocation page · configurable per-office Divisions · permission-model simplification (retire PermissionGroup + Division enum + Observer role) · division-scoped WFP · shared MoneyInput component_

> Status: **requirements confirmed in discussion 2026-06-24** (Ralph + Claude). Open items are tracked in §12. No code written yet. Next step: create `release/1.2.0` off `main` (once v1.1.1 is the prod baseline) and break this into Linear tickets following `docs/TICKET_PROMPT_STANDARD.md`.

---

## 1. Background & Goal

v1.1 shipped Budget Planning (LDIP / AIP / WFP) with **office-level** scoping only — WFP is scoped by office via the office ref-code suffix match. v1.2 introduces a **division** dimension so that, within an office, work is split among its divisions, with a finance officer controlling the money.

Three new capabilities, plus the structural changes they force:

1. A new **Allocation** page (finance-officer-only) for: PBO budget ceiling per office, budget allocation per division, and assigning PPAs (Programs) to divisions.
2. **Division-scoped WFP** — a regular user sees only their division's PPAs; the finance officer sees everything in scope.
3. **Configurable, per-office Divisions** — replacing the hardcoded 5-value `Division` enum. This cascades into a deliberate **simplification of the whole permission model**.

> **PPA = Program, Project, Activity.** Division assignment happens at the **Program** level and cascades to that program's projects and activities.

---

## 2. Locked Decisions (2026-06-24)

| # | Decision |
|---|---|
| D1 | **Division model = configurable table** (was: C5 "phased"). Now full alignment: a single `divisions` table is the source of truth **everywhere, including inventory**. The `Division` enum is retired. |
| D2 | **Clean slate for inventory** — Ralph will wipe local inventory test data; prod inventory is empty. So inventory's enum→`division_id` swap needs **no data migration**. |
| D3 | **Per-division WFP records** — WFP uniqueness becomes `(aip_record_id, office_id, division_id)`. Kills the full-replace save data-loss bug; finalize/unlock become per-division. |
| D4 | **No "Finance" division/role.** Finance officer = existing role + a per-user **`CanManageAllocation`** grant. PPDO finance = Admin role; non-PPDO finance = Staff role. The grant is assigned to the user **regardless of role/division**. |
| D5 | **WFP expenditure validation uses GROSS total** (total appropriation), not net. |
| D6 | **Supplemental AIP re-upload** keeps the same AIP ref code + name; `program_divisions` keys off **ref code**, so assignments survive re-upload. New programs appear as "unassigned." |
| D7 | **Retire `PermissionGroup` entirely.** Feature flags move onto the `divisions` table; each division is its own "group," edited in the division config page. `GroupIdFor` mapping is deleted. |
| D8 | **Retire the `Observer` role.** Roles become **SuperAdmin / Admin / Staff**. Read-only is deferred (optional future per-user `view_only` flag). |
| D9 | **WFP Excel report** — no new columns in v1.2. |
| D10 | New shared **`MoneyInput`** component (+ `formatMoney`/`parseMoney` util) used by all budget-planning money fields; inventory adopts later. |

---

## 3. The Allocation Page

**Route:** `/budget-planning/allocation` · **Sidebar:** Budget Planning group, **between AIP and WFP**.
**Access:** `CanManageAllocation` (finance officer only). Hidden from the sidebar otherwise.

Tabbed layout:

### Tab 1 — Ceiling & Division Allocation (one screen)
- **Fiscal-year selector** at top (reuse the dashboard's FY-resolution pattern from RAL-80).
- **Office selector** — PPDO finance (Admin) can pick any office; a non-PPDO finance officer is locked to their own office.
- **PBO Budget Ceiling** input for the selected office + FY (a `MoneyInput`).
- **Division allocation list** — one `MoneyInput` per division of that office, with a live running total: `Allocated ₱X of ₱Y · Remaining ₱Z`. Save is blocked (and the total turns red) when allocations exceed the ceiling.
- **Stacked bar chart** — one horizontal bar = the ceiling; coloured segments = each division's allocation; trailing grey = unallocated remainder; red overflow segment when over-allocated. Shows `% of ceiling` per division.
- Future nicety (not v1.2-blocking): overlay "WFP encoded so far" per division so finance sees allocation vs. actual consumption.

### Tab 2 — PPA → Division Assignment
- Reuse the **WFP grid hierarchy** but collapse to **Sector → Program** only (Program is the assignable level; projects/activities inherit).
- Columns: `[row checkbox] · AIP Ref Code · Program Name · [Multi-division?] · <one checkbox column per division of the office>`.
- **"Multi-division?"** per-program toggle: OFF → the division checkboxes behave like radio buttons (exactly one division); ON → multiple allowed. Default OFF for PPDO (1 PPA → 1 division). ON supports the future PHO case (1 PPA → many divisions).
- UX adds: an **"Unassigned" filter/badge** (programs with no division are invisible to everyone in WFP — finance must see and fix these); **bulk-assign** a division to all checked programs; per-division **assigned-count** in each column header.

---

## 4. The "Setup-complete" gate

WFP expenditure entry for a `(office, FY, division)` is **blocked** until all three exist:
1. A `budget_ceiling` row for the office + FY,
2. A `division_allocation` for the user's division + FY, and
3. At least one program assigned to that division.

Surface an explicit **"Setup incomplete"** banner on WFP listing which of the three is missing (don't just show an empty grid — a user can't otherwise tell "nothing assigned to me" from "page broken"). Enforced on the backend `SaveAsync` too.

---

## 5. Data Model (new / changed tables)

New tables follow snake_case (`docs/NAMING_CONVENTIONS.md`).

```sql
-- Configurable division = data scope + feature flags ("the grouping").
-- Office-scoped: PPDO's divisions differ from other offices'.
divisions
  id                         INT PK
  office_id                  INT NOT NULL FK -> offices
  code                       NVARCHAR(20)  NOT NULL
  name                       NVARCHAR(200) NOT NULL
  is_active                  BIT NOT NULL DEFAULT 1
  -- feature flags (edited in the division config page):
  can_access_inventory       BIT NOT NULL DEFAULT 0
  can_access_reports         BIT NOT NULL DEFAULT 0
  can_manage_users           BIT NOT NULL DEFAULT 0
  can_manage_resource_links  BIT NOT NULL DEFAULT 0
  can_access_budget_planning BIT NOT NULL DEFAULT 0
  can_upload_aip             BIT NOT NULL DEFAULT 0
  can_manage_config          BIT NOT NULL DEFAULT 0
  created_at                 DATETIME2 NOT NULL
  updated_at                 DATETIME2 NOT NULL
  UNIQUE (office_id, code)

-- Feature 1: PBO ceiling per office per FY.
budget_ceilings
  id           INT PK
  office_id    INT NOT NULL FK -> offices
  fiscal_year  INT NOT NULL
  amount       DECIMAL(18,2) NOT NULL
  UNIQUE (office_id, fiscal_year)

-- Feature 2: allocation per division per FY (Σ <= ceiling, enforced in service).
division_allocations
  id           INT PK
  division_id  INT NOT NULL FK -> divisions
  fiscal_year  INT NOT NULL
  amount       DECIMAL(18,2) NOT NULL
  UNIQUE (division_id, fiscal_year)

-- Feature 3: PPA -> division (many-to-many at PROGRAM level).
-- Keyed by ref code so it survives supplemental AIP re-uploads (D6).
program_divisions
  id              INT PK
  office_ref_code NVARCHAR(50) NOT NULL   -- stable AIP office ref-code suffix
  program_ref_code NVARCHAR(50) NOT NULL  -- stable AIP program ref code
  division_id     INT NOT NULL FK -> divisions
  UNIQUE (office_ref_code, program_ref_code, division_id)
```

### Changed: `users`
- **Add** `division_id INT NULL FK -> divisions`. Replaces the `Division` enum column.
- **Drop** `GroupId` + FK (PermissionGroup retired, D7).
- **Add** `OverrideCanManageAllocation BIT NULL`.
- SuperAdmin/Admin keep `division_id = null` (they bypass/default). Staff require a `division_id`.

### Changed: inventory (clean slate, D2)
- `purchase_requests`: `Division` enum column → `division_id INT FK -> divisions`.
- `distributions`: `Division` enum column → `division_id INT FK -> divisions`.

### Dropped
- Table `PermissionGroups` (+ `Users.GroupId` FK).
- Enum `Division` (`PPDO.Domain/Enums/Division.cs`).

### PPDO Divisions (seed list — confirmed 2026-06-24)

`code` is nullable (some divisions have no official short code → full **name** is the identifier; `UNIQUE` falls back to `(office_id, name)` when code is null). The short labels below are **optional display aids** for the PPA→division grid headers (long names won't fit 6 columns) — adopt or drop.

| Name | Optional short label | Rough old-enum lineage (back-fill hint only) |
|---|---|---|
| Administrative Division | ADMIN | Admin |
| Sectoral Planning Division | SECTORAL | Planning |
| Statistics, Monitoring and Evaluation Division | SMED | RM |
| Fiscal Planning and Investment Programming Division | FPIP | _(new — finance officer's home division)_ |
| Information and Communications Technology Division | ICT | MIS |
| Open Governance and Civil Society Organization Engagement Division | OG-CSO | _(new)_ |

**Seed flags (CONFIRMED 2026-06-24):**

| Division | budget_planning | inventory | reports | config | upload_aip | manage_users | resource_links |
|---|:-:|:-:|:-:|:-:|:-:|:-:|:-:|
| Administrative | ✓ | ✓ | ✓ | – | – | – | ✓ |
| Sectoral Planning | ✓ | – | – | – | – | – | – |
| Statistics, Monitoring & Evaluation | ✓ | – | – | – | – | – | – |
| Fiscal Planning & Investment Programming | ✓ | – | – | – | – | – | – |
| Information & Communications Technology | ✓ | – | – | – | – | – | – |
| Open Governance & CSO Engagement | ✓ | – | – | – | – | – | – |

`config`, `upload_aip`, and `manage_users` are off at the division level for everyone — they're held by Admin-role users (all-true-by-default) or granted per-user via overrides (e.g. the FPIP finance officer gets `CanUploadAip`/`CanManageAllocation` as personal grants). `CanManageAllocation` is never a division flag. This filled CSV is the seed (`D:\RalphFiles\PPDO\PPDO\divisions_seed_template.csv`).

### Division config page & seeding
- The division config page gets the same **CSV upload/download** as Accounts/Offices/Funding (RAL-72 pattern). **No EF seed migration** — divisions are loaded via CSV upload, consistent with the 2026-06-10 seeding decision. Seed CSV stays outside the repo (template: `D:\RalphFiles\PPDO\PPDO\divisions_seed_template.csv`).
- **CSV column order:** `office_code, code, name, is_active, can_access_budget_planning, can_access_inventory, can_access_reports, can_manage_config, can_upload_aip, can_manage_users, can_manage_resource_links`. Flags are `TRUE`/`FALSE`. `can_manage_allocation` is **not** a CSV/division column (per-user grant only).
- **Upsert key = `name`** (within `office_code`), because `code` is nullable. `name` is readonly on edit (the key); `code` + flags are editable — so codes can be changed anytime.
- **Migration ordering (no lockout):** run migration → upload divisions CSV → assign each user a `division_id`. Between migration and upload, existing users have null `division_id`; SuperAdmin bypasses all checks so admin access is never lost.

> 🔴 **Supplemental AIP carry-forward (D6).** `program_divisions` is keyed by `(office_ref_code, program_ref_code)`, **not** the surrogate `aip_programs.id` (which is recreated on every upload). On a supplemental upload, existing programs re-link automatically by ref code; genuinely new programs surface as "unassigned" on the Allocation page. **Do not key assignments off `aip_program_id`** — that was the data-loss trap.

---

## 6. Permission Model (simplified)

### Roles: SuperAdmin / Admin / Staff (Observer retired, D8)

### Resolution (in `PermissionService`)

| Role | Normal feature flags | `CanManageAllocation` (special) |
|---|---|---|
| **SuperAdmin** | always true (full bypass) | always true (bypass) |
| **Admin** | always true by default | **NOT auto** — needs explicit grant |
| **Staff** | `OverrideX ?? user.Division.<flag> ?? false` | per-user grant only |

- A Staff user's flags come from **their division row** (`user.division_id` → `divisions.can_*`), with per-user `OverrideCan*` taking precedence when non-null.
- `CanManageAllocation` is a **per-user grant** (`User.OverrideCanManageAllocation`), not a division flag — because it's assigned to one specific person "regardless of role/division" (D4). Resolution: `SuperAdmin → true; else → OverrideCanManageAllocation ?? false`.
- **No `GroupIdFor`** — `user.division_id` is set directly on the user form from the fetched divisions list.

### Office vs. division scoping (two independent dimensions)
- **Office scope** (which office's data): Admin/SuperAdmin → all offices; user with `office_id` set → own office only. (Unchanged concept; `office_id` is still the PPDO/non-PPDO discriminator — PPDO users have `office_id = null`.)
- **Division scope** (which divisions inside the office, for WFP/Allocation): the WFP/allocation division filter is **skipped** when the user is **Admin/SuperAdmin OR holds `CanManageAllocation`**; otherwise filtered to `user.division_id`.
  - PPDO finance = Admin → all offices, all divisions.
  - Non-PPDO finance = Staff + allocation grant → their one office, all its divisions.
  - Everyone else (Staff) → their office + their own division only.

> ⚠️ **Consequence:** any Admin-role user (e.g. a division head) sees **all** divisions in WFP. Accepted for v1.2 (finance needs all-access; most users are Staff). If division heads ever need to be scoped to their own division, they must be Staff, not Admin.

### Division config page = the "grouping" UI
A new config page (under Configuration, gated by `CanManageConfig`) CRUDs divisions per office and edits each division's feature flags — this **replaces** the old permission-group admin. Mirrors the existing offices / funding-sources config pages.

---

## 7. WFP changes

- **Per-division WFP records** (D3): unique key `(aip_record_id, office_id, division_id)`. The full delete-and-reinsert `SaveAsync` is now safe because each division owns its own record; divisions can't wipe each other.
- **Division filter** on the grid: build the program list from `program_divisions` for the user's division (skipped for Admin/SuperAdmin/allocation holders — they see all and pick a division to view/edit).
- **Backend enforcement** of the division filter on all WFP read endpoints (not just UI hiding) — today WFP filters by office only.
- **Division budget banner + 3rd validation tier**: show `Division budget: allocated / used / remaining`. New rule — Σ **gross** total appropriation for the division ≤ that division's `division_allocation` (D5). Enforced on backend `SaveAsync`. (Existing rules stay: per line `quarterly ≤ net`; per activity `total ≤ AIP budget`.)
- **Finalize/unlock** now per-division (a side benefit of D3).
- **localStorage draft key** moves from `wfp_draft_{aip}_{office}` → `wfp_draft_{aip}_{office}_{division}`.
- **Units guard:** AIP totals are stored in **thousands** (page multiplies ×1000). Ceilings/allocations are in pesos. Keep the ×1000 conversion in the page layer only; pin `division_allocations.amount` / `budget_ceilings.amount` as pesos. Avoid 1000× validation bugs.

---

## 8. Shared `MoneyInput` component (D10)

No existing money component or shared formatter — formatting is hand-rolled across ~20 files with raw `<input type="number">`. Build one reusable component for `components/ui/`.

**Spec:**
- Left **₱** prefix label; input fills the rest. `type="text"` + `inputMode="decimal"` (a number input can't render commas).
- Holds a **number** in state; displays with thousands commas. `onChange(value: number | null)` emits the **clean numeric value** — commas are display-only and never leave the component (backend/validation always get `1234567.5`).
- Accepts digits + one decimal point; strips other chars; caps to 2 decimals.
- **Live comma formatting** with caret-position preservation (count digits left of caret, re-map after format). Fallback if jank appears: format-on-blur (raw while focused) with no behavior loss.
- Ship with a shared `formatMoney`/`parseMoney` util so read-only table cells and the input share one formatter — retires the scattered duplication over time (budget planning first, inventory later).
- **Performance:** pure per-keystroke string op (`Intl.NumberFormat`); negligible even in the WFP grid popup (which already formats every render). No concern.

Used in v1.2 by: ceiling input, division allocation inputs, and the WFP expenditure money fields.

---

## 9. Audit logging
Wire the new tables (`budget_ceilings`, `division_allocations`, `program_divisions`, `divisions`) into `IAuditService` (RAL-77 pattern).

---

## 10. Migration & clean-slate notes
- Inventory data wiped (D2) → enum→`division_id` swap is a clean column change, no back-fill of PR/Distribution rows.
- **Seed divisions:** PPDO's real (renamed) division list from the finance officer (see §12 open item). Each non-PPDO office needs at least one division row so its users have something to point at (non-PPDO is otherwise deferred).
- **User back-fill:** Ralph sets `division_id` manually for any user the migration can't map (the enum→new-division match is imperfect because divisions were renamed). Low volume.
- **Existing Observer users in prod (D8):** convert to Staff with appropriately restricted division flags **before/at** migration — a silent convert would grant write access. Review the few accounts post-migration.
- **`PermissionGroup` clean removal (D7):** drop FK `Users.GroupId`, drop column, drop table; delete entity + config + `PermissionGroupFunctions` + `PermissionGroupResponseDto` + `GroupIdFor`; remove group-loading from `AuthService`/JwtMiddleware/`/me`; strip group UI/types from the user form. Acceptance check: **no dangling references to PermissionGroup/GroupId anywhere.**

---

## 11. Effort / blast radius (for ticket breakdown)

| Area | Work |
|---|---|
| **Permission core** | `PermissionService` reads division flags; `AuthService`/JWT/`/me` load `division` nav; delete `PermissionGroup` + `GroupIdFor`; retire Observer + its hard-blocks. |
| **Inventory align** | enum→`division_id` on `PurchaseRequest`/`Distribution`, `DivisionScope.Resolve`, both repos' `GetByDivisionAsync(int)`, ~8 DTO/parse spots; wipe data; reset tests. |
| **New tables/services** | `divisions` config CRUD + page; `budget_ceilings`; `division_allocations`; `program_divisions` + ref-code carry-forward on AIP import. |
| **Allocation page** | 2 tabs (ceiling+allocation w/ stacked bar; PPA→division grid). |
| **WFP** | per-division records, division filter (FE+BE), division-budget validation + banner, setup-complete gate, draft key change. |
| **Frontend shared** | `MoneyInput` + `formatMoney`/`parseMoney`; `Division` union type → fetched list; user-form division dropdown; division config UI. |

All mechanical, de-risked by the clean slate, but this is the largest single piece of v1.2 (the permission/division realignment).

---

## 12. Open items (to resolve before/within ticketing)

1. ~~Exact PPDO division list~~ — **captured 2026-06-24** (see §5 "PPDO Divisions"). 6 divisions, no official codes (name is the key).
2. ~~Per-division default flags~~ — **CONFIRMED 2026-06-24** (see §5 seed-flags table). Administrative = budget+inventory+reports+resource-links; all others = budget-planning only.
3. **Dashboard (RAL-80) scope** — whether to add allocation-vs-utilization tiles / WFP-by-division, and the finance/office dashboard scope. Not yet specified.
4. **Deferred read-only** — if a true view-only user appears, add a per-user `view_only` flag (cheaper than a role). Not built in v1.2.
5. **Non-PPDO offices** — division setup for other offices is deferred; v1.2 focuses on PPDO. Confirm a single default division per non-PPDO office is enough for now.
6. **`release/1.2.0`** — create off `main` once v1.1.1 is the confirmed prod baseline; PRs target `release/1.2.0`.

---

## 13. Related
- `docs/v1.1/DB_Model.md` — existing planning schema.
- `docs/v1.1/User_Roles_Permissions.md` — the v1.1 model this supersedes (PermissionGroup, Division enum, Observer).
- `docs/v1.1/Budget_Planning_Pages_Design_Spec.md` — WFP grid layout reused by the Allocation PPA tab.
- `docs/NAMING_CONVENTIONS.md` · `docs/TICKET_PROMPT_STANDARD.md`.
- `backend/PPDO.Application/Common/DivisionScope.cs` — to be reworked onto `division_id`.
