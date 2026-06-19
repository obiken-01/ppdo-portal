# PPDO Portal — User Roles, Permissions & Access Model
_Version 1.1 | Date: June 11, 2026 | Decisions confirmed by Ralph 2026-06-11_
_This is the reference for how users, roles, divisions, offices, and permission flags work — including non-PPDO (office) users introduced in v1.1._

---

## 1. Background

v1.0 was designed as a **PPDO-internal tool** — every user was assumed to be a PPDO employee. v1.1 Budget Planning introduces **non-PPDO users**: staff from the 16 provincial offices who log in to encode their office's WFP. This document defines how both user populations are modeled.

> Terminology: earlier docs used "Visitor" for non-PPDO users. That term is retired — use **office user** (encoder or viewer). The code role `Observer` is unchanged.

---

## 2. Identity Model

A user is identified by **role** (how permissions resolve) + **division** (PPDO-internal data scope) + **office** (provincial office data scope, new in v1.1).

| Field | Type | Notes |
|---|---|---|
| `role` | enum, required | SuperAdmin / Admin / Staff / Observer (unchanged from v1.0) |
| `division` | enum, **nullable from v1.1** | PPDO's 5 internal divisions; **null for office users** |
| `office_id` | INT FK → `offices`, nullable, **new in v1.1** | **The PPDO / non-PPDO discriminator** |
| `group_id` | FK → permission_groups | Auto-assigned from role + division/office |
| `Override*` flags | nullable bools | Per-user overrides; null = inherit group flag |

**Discriminator rule:** `office_id == null` → PPDO user · `office_id` set → non-PPDO office user.

---

## 3. Roles (unchanged enum, expanded usage)

| Role | PPDO usage (v1.0) | Office-user usage (v1.1) | Permission resolution |
|---|---|---|---|
| `SuperAdmin` | Developer / MIS | — (never an office user) | Bypasses all checks |
| `Admin` | Division heads | — (never an office user) | All flags effectively true |
| `Staff` | Regular PPDO employee | **Office encoder** — enters WFP data for their office | `Override ?? Group flag` |
| `Observer` | Read-only provincial admin | **Office viewer** — read-only on their office's data | `Override ?? Group flag`; can NEVER create/edit/delete; hard-blocked from manage-type flags |

The Observer read-only invariant is **not** relaxed for budget planning — office users who need to write are created as Staff.

---

## 4. Two Scoping Dimensions

| Dimension | Values | Scopes | Applies to |
|---|---|---|---|
| **Division** | Admin / Planning / RM / MIS / SPD (PPDO-internal) | Inventory data (PRs, deliveries, distributions) | PPDO Staff/Observer only |
| **Office** | 16 provincial offices (`offices` config table) | Budget Planning data (WFP, dashboard, activity) | Office users |

Rules:
- **PPDO users manage all offices** in Budget Planning (decided 2026-06-10)
- Office users access **only their own office's** budget planning data
- Division scoping for inventory is unchanged — office users never reach inventory (no flag)

> ⚠️ **Implementation guard (nullable Division):** in `InventoryService` / `DistributionService`, a null division scope currently means "no filter — see all divisions" (the Admin path). With Division nullable, a Staff/Observer user with null division must resolve to an **empty** scope, never an open one. Likewise `GetByDivisionAsync` must never default a null division to a real division.

---

## 5. Permission Flags

Resolution chain (PermissionService): **SuperAdmin/Admin → always true** · Staff/Observer → `Override ?? Group flag` · Observer hard-blocked where noted.

### Existing (v1.0)

| Flag | Grants | Observer |
|---|---|---|
| `CanAccessInventory` | Full inventory module | allowed (read-only by role) |
| `CanAccessReports` | PR Report + export | allowed |
| `CanManageUsers` | User Management | **never** |
| `CanManageResourceLinks` | Resource Links management | **never** |

### New (v1.1)

| Flag | Grants | Observer |
|---|---|---|
| `CanAccessBudgetPlanning` | Budget Planning module (dashboard, LDIP, AIP view, WFP) | allowed (read-only by role) |
| `CanUploadAip` | AIP upload / import preview / confirm | **never** — and granted to **PPDO users only** (the file contains all offices' records) |
| `CanManageConfig` | All config pages (Accounts, Offices, Funding Sources) — one flag for the whole section, not per page | **never** |

---

## 6. Permission Groups (seeded)

| Group | Division/Office | Key flags |
|---|---|---|
| Admin Division Staff, Planning Staff, RM Staff, MIS Staff, SPD Staff | per PPDO division | v1.0 flags per division + new v1.1 flags per PPDO defaults |
| Observer Default | none | all false |
| **Office User Default** (new in v1.1) | office users (any office) | `CanAccessBudgetPlanning = true`, everything else false |

Group auto-assignment (`GroupIdFor`): SuperAdmin/Admin → none · user with `office_id` set → **Office User Default** · PPDO Staff → division group · PPDO Observer → Observer Default.

> Without the Office User Default group, a division-less Staff user would get a null group and silently resolve every flag to false — this group is mandatory, not optional.

---

## 7. Office User Experience

| Aspect | Behavior |
|---|---|
| Login redirect | Straight to **Budget Planning dashboard** (`/budget-planning`) — not the Home Dashboard |
| Available features | **Budget Planning only** (v1.1) |
| Sidebar | Budget Planning group only; no Inventory, no Resource Links (PPDO-internal documents), no Configuration, no User Management, no Reports (PPDO) |
| Office selector | Locked to their own office everywhere |
| Dashboard | No counters row, no office-status table, activity scoped to own office (see Penpot Page 4, Screen 21) |
| WFP | Full entry for own office (Staff); read-only (Observer) |
| AIP | List/detail read-only for context; **never upload** |
| Account creation | By PPDO admins via User Management (role + office assignment) |

---

## 8. Access Matrix

| Feature | PPDO SuperAdmin/Admin | PPDO Staff* | PPDO Observer* | Office Staff (encoder) | Office Observer (viewer) |
|---|---|---|---|---|---|
| Inventory | ✓ all divisions | own division | own division, read-only | — | — |
| Resource Links | ✓ | per flag | read-only | — | — |
| User Management | ✓ | per override | — | — | — |
| Configuration | ✓ | per `CanManageConfig` | — | — | — |
| Budget Planning dashboard | ✓ full (all offices) | ✓ full | read-only | own office (reduced) | own office (reduced, read-only) |
| AIP upload/import | ✓ | per `CanUploadAip` | — | — | — |
| WFP entry | ✓ any office | ✓ any office | read-only | own office only | own office, read-only |

\* PPDO Staff/Observer flags resolve via group + overrides as usual.

---

## 9. Deferred Items (notes for future versions)

| Item | Status |
|---|---|
| **Forced password change on first login** | Approved 2026-06-11, **not a priority** — new accounts currently get a shared default password, which is riskier for external office accounts. Implement when convenient |
| `UserType` / `OfficeStaff` role refactor | Only if office users ever get more modules than Budget Planning |
| Self-registration / email invitations | Manual account creation by PPDO is fine at 16 offices |
| Per-office admin (office manages its own users) | Deferred |
| JWT `off` (office) claim alongside the existing `div` claim | Optional optimization — scoping can read office_id from the loaded user |
| Office users on Home Dashboard / calendar | Skipped — office users go straight to Budget Planning |

---

## 10. Related

- `docs/v1.1/DB_Model.md` — table schemas (note 10 = access control summary)
- Linear RAL-81 — implementation ticket for the flags, nullable Division, office_id, and the scoping guards
- `backend/PPDO.Application/Services/PermissionService.cs` — resolution logic
- Penpot Page 4, Screens 20–21 — PPDO vs office-user dashboard mockups
