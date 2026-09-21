---
status: draft
version: v1.8.0
tickets: C1 = PPDO-106 (office ceilings page) · C2 = PPDO-107 (office-setup grant) · C3 = PPDO-108 (division config, blocked by 107) · C4 = PPDO-109 (per-office fund sources ⚠️ MIGRATION, blocked by 107)
supersedes: nothing — extends Permission_Matrix.md §4 (new grant), §4a (scope table) and the Allocation page's ownership split
---

# v1.8.0 — Office setup: finance sets ceilings, department heads set up their own office

## 1. Goal

The 2026-09-15 PDC/finance demo moved two kinds of authority apart:

- **PPDO finance** sets the **office ceiling** for every office, on a page of its own.
- **A department head** sets up **their own office** underneath that ceiling: the division split,
  the programme → division assignment, their own divisions, and their own fund sources.

Today both halves live on one PPDO-only Allocation page, and the permission that covers the
division split is deliberately host-office only. This spec adds one new grant, moves the ceiling
out to its own page, and opens the office-scoped half to department heads.

⚠️ **The permission direction is additive.** No existing grant widens; `CanManagePpdoAllocation`
stays exclusive to host-office users (`Permission_Matrix.md` §4a), which is what closed the
`pto.user` leak on 2026-09-02.

## 2. Decisions (settled)

| # | Decision | Source |
|---|---|---|
| D1 | **One new per-user grant, `CanManageOfficeSetup`**, covers all four office-scoped surfaces: the division split, programme assignment, division config and fund-source config. Not four flags. | Ralph, 2026-09-19 (Q1 default accepted) |
| D2 | The grant is **not implied by any reviewer flag**. A department head who reviews does not automatically set up their office; the grant is given per user. | Ralph, 2026-09-19 |
| D3 | **Ceilings move to their own page** for PPDO finance, listing every office. The Allocation page still **shows** the ceiling, read-only. | Demo item 8 |
| D4 | **The ceilings page is built with one tab per funding source, but only the General Fund tab is shown.** The other funds' tabs are written and kept behind a constant, because the current direction is that only the General Fund carries a ceiling. Turning them on later is a one-line change, not a rebuild. | **Ralph, 2026-09-19** |
| D5 | **Fund sources: shared list + office additions** (Option 2). `funding_sources.office_id` is nullable: null = province-wide, PPDO-owned, read-only to offices; a row with an office id belongs to that office. | Ralph, 2026-09-17 |
| D6 | **Fund codes stay globally unique.** The existing unique index `IX_funding_sources_code` is unchanged, so an office cannot reuse `GF`, and two offices cannot give the same code different meanings. Province-wide totals group by code and stay correct. | This spec |
| D7 | `GetGeneralFundIdAsync` resolves **only shared rows** (`office_id IS NULL`), so the ceiling arithmetic keeps one canonical General Fund. | This spec |
| D8 | Department heads **never see the seven feature switches** on the division form, and the server **rejects them** from a non-admin caller — hiding alone is not enforcement. | Ralph, 2026-09-16 |
| D9 | A department head creates divisions with **all seven switches off**. An admin turns them on. | Q4 default accepted |
| D10 | Office-scoped setup writes are allowed **only while the office's AIP is office-editable** (Draft, DepartmentReview, ReturnedByPpdo). | Q6 default accepted |
| D11 | Office funds carry **no ceiling check**. The ceiling applies to the General Fund only, as now. | Q5 default accepted |

### Open follow-ups (not blocking)

- Renaming `CanManageOfficeCeilings` again is not proposed; it was renamed in PPDO-87.
- Whether finance also wants a per-office **ceiling history**. Not in scope here.

## 3. Behaviour

### 3.1 Office ceilings page (finance)

| Given | When | Then |
|---|---|---|
| A user holds `CanManageOfficeCeilings` | They open Investment Planning → Office Ceilings | Every active office is listed for the chosen fiscal year with its General Fund ceiling, the amount already encoded against it, and the remainder |
| The same user | They edit one office's ceiling and save | `PUT /allocation/ceiling` is called with that office id; the row updates in place |
| The same user | They use bulk edit | Each changed office is saved; a failure on one office reports that office by name and leaves the others saved |
| The ceiling is cut below what the office already encoded | Save | ⚠️ It **succeeds** — a ceiling cut is non-destructive (A5-b). The row shows the over-ceiling state, and the office fails its own submission gate, as today |
| A user without the grant | Opens the URL | Redirected, exactly as other gated pages behave |
| A fiscal year with no ceilings yet | Page load | Every office shows a blank ceiling and an explicit "not set" state, never ₱0.00 |

### 3.2 Office setup (department head)

| Given | When | Then |
|---|---|---|
| A department head with `CanManageOfficeSetup` | Opens Allocation | They see **their own office only**, with no office picker |
| The same | Edits the division split | `PUT /allocation/divisions` succeeds for their own office |
| The same | Targets another office (crafted request) | **403** |
| A department head **without** the grant | Opens Allocation | The page is read-only; the sidebar link is absent |
| A guest-office user who is not a department head | Any of these writes | **403**, unchanged |
| The office's AIP has been accepted by PPDO | Any office-setup write | **409**, naming the state (D10) |
| The division split exceeds the ceiling | Save | Rejected with the existing over-allocation message |

### 3.3 Division config (department head)

| Given | When | Then |
|---|---|---|
| A department head with the grant | Opens Configuration → Divisions | Only their own office's divisions are listed; no office column or picker |
| The same | Creates a division | It is created in **their** office with all seven switches off (D9) |
| The same | Sends a request setting a switch | The switch is **ignored**, and the response carries the stored value |
| The same | Deactivates a division | ↩️ **Deviation, PPDO-108:** it deactivates, as it always has for an admin. The spec first said "blocked when users or allocations use it" — but delete here is already a reversible soft delete (`IsActive = false`), nothing cascades, and adding an in-use block would change how admins have always worked. Revisit as its own ticket if an office deactivates a division out from under live allocations. |
| An admin | Opens the same page | Unchanged: every office, switches visible |

### 3.4 Fund sources (department head)

| Given | When | Then |
|---|---|---|
| A department head with the grant | Opens Configuration → Fund Sources | The shared funds are listed **read-only**, their own office's funds are editable |
| The same | Adds a fund with the code `GF` | Rejected: the code is taken (D6) |
| The same | Adds a fund with a free code | Created against their office, visible only to their office |
| The same | Deletes a fund used by a WFP or AIP line | Blocked, with the count of rows using it. ↩️ **Clarified, PPDO-109:** the block applies to the **department head only**. A config manager keeps the unconditional soft delete, because that is what soft delete is FOR — retiring a fund from the pickers while decades of records keep resolving through it. Applying the guard to PPDO would make every fund that was ever used undeletable, which is all of them. |
| An encoder in that office | Opens a WFP or AIP expenditure line | The fund picker shows shared funds **plus** their office's own |
| An encoder in another office | The same picker | The first office's funds are absent |

↩️ **Closed in the PPDO-109 follow-up — the expenditure WRITE is scoped too.** The fund LIST every
surface reads was office-scoped in PPDO-109, so no office could pick another's fund through the UI;
the AIP/WFP save paths still resolved any `fundingSourceId` they were handed, so a hand-crafted
request could name another office's fund and have it snapshotted. That was deferred at the time (a
wrong label on the caller's own line, not a read of anyone else's data) and has since been fixed through
`Application/Common/FundingSourceScope.cs`: an activity or expenditure may name a province-wide fund
or one of its own office's, and nothing else.

The audit found **five** caller-fed write sites, not the four first listed — `WfpService.SaveAsync`
(the WFP grid save) writes line snapshots the same way and was missed. The other fund writes are
safe for reasons worth recording, so nobody re-checks them: the `AipCeilingService` and
`WfpCeilingService` ledger rows use a fund id DERIVED from the record's own expenditures rather than
one the caller sent, and `LdipService` resolves through a lookup PPDO-109 already restricted to
shared funds. `AllocationService`'s ceiling and division-allocation writes do take a caller-supplied
fund id, but they sit behind host-office-only grants and D11 gives office funds no ceiling at all —
left alone deliberately.

Three things about that fix are worth knowing before changing it:

- **The refusal is worded as "not found"**, identically to an id that does not exist. Naming the real
  reason would turn a rejected save into a way to enumerate other offices' funds one id at a time.
- **A record with no resolvable office gets the shared funds only** — it stays saveable, and a
  forgotten office id degrades to less access rather than to all of it.
- **`FundingSourceRaw` on `AddActivityAsync` is the one exception**: it is free text, so an
  unrecognised code keeps the text and leaves the FK null, exactly as it always has for a typo.
  Another office's code simply joins that set instead of resolving.

It also closed a latent bug on the same lines: `FundingSourceId` used to be copied from the request
before the row was looked up, so an id matching nothing at all was stored anyway with both snapshots
null — a dangling FK that rendered as a line with no fund and was reported nowhere.

## 4. API contract

### `GET /api/budget-planning/allocation/ceilings?officeId=&fiscalYear=` — JWT + `CanAccessBudgetPlanning`
Unchanged. The new page calls it per office, or a new list endpoint is added if the per-office call
proves slow — measure before adding one (`PERFORMANCE_GUIDELINES.md`).

### `PUT /api/budget-planning/allocation/ceiling` — JWT + `CanManageOfficeCeilings`
Unchanged, including its deliberate lack of an office clamp and its exemption from
`ReviewerWriteGuard` (PPDO-87).

### `PUT /api/budget-planning/allocation/divisions` — JWT + (`CanManagePpdoAllocation` **or** `CanManageOfficeSetup`)
- Host-office caller with `CanManagePpdoAllocation`: any office, as now.
- Caller with `CanManageOfficeSetup`: `body.OfficeId` **must equal** `caller.OfficeId`, else `403`.
- Neither: `403`.
- Office not in an editable state: `409 { error: "…" }` (D10).

### `PUT /api/budget-planning/allocation/programs` — same gate and the same two rules

### `GET|POST|PUT|DELETE /api/config/divisions[/{id}]` — JWT + (`CanManageConfig` **or** `CanManageOfficeSetup`)
- With `CanManageOfficeSetup` only: reads are filtered to the caller's office; writes must target it.
- The seven `Can*` fields in the body are **dropped** for such a caller (D8), never a 400 — an
  older client sending them must not break.

### `GET|POST|PUT|DELETE /api/config/funding-sources[/{id}]` — JWT + (`CanManageConfig` **or** `CanManageOfficeSetup`)
- Reads return shared rows **plus** the caller's office's rows. Shared rows carry
  `"isShared": true` so the UI can render them read-only.
- A `CanManageOfficeSetup` caller writing a shared row: `403`.
- Creates from such a caller are stamped with the caller's office id; a body office id is ignored.
- Duplicate code: `409`, as today.

## 5. Data model changes

### `funding_sources` (snake_case)

| Column | Type | Notes |
|---|---|---|
| `office_id` | `int NULL` | FK → `offices.id`, `NoAction`. **Null = province-wide.** |

- Migration: `AddFundingSourceOfficeId`. ⚠️ **MIGRATION** — additive, nullable, no backfill: every
  existing row stays shared, which is exactly today's behaviour.
- `IX_funding_sources_code` is **unchanged** (D6).
- CI does not run migrations — apply by hand to Azure SQL at release
  (`ci-does-not-run-ef-migrations`).

### `divisions`, `budget_ceilings`
No change. `divisions.office_id` already exists.

## 6. UI states

### Office Ceilings page (`/budget-planning/office-ceilings`)
- **Loading:** the table shell with a skeleton row per office — no centred spinner (CLS rule).
- **Empty:** never empty; every active office has a row. A missing ceiling shows "Not set".
- **Error:** an inline banner with retry; the already-loaded rows stay.
- **Read-only:** a user who may read but not set ceilings sees the amounts without edit controls.
- **Tabs:** the funding-source tabs are built but only the General Fund tab renders (D4). The
  constant that hides them is commented with this decision.

### Allocation page (`/budget-planning/allocation`)
- The ceiling becomes **read-only** with a link to the new page for those who may edit it.
- For a department head: no office picker, their office's name shown as a heading.

### Divisions and Fund Sources config
- A department head sees a scope note: "You are managing <Office> only."
- Shared funds show a lock affordance and a tooltip: maintained by PPDO.

## 7. Non-goals

- No per-office General Fund, and no change to `generalFundId`.
- No change to who reviews what; `ReviewerWriteGuard` is untouched.
- Department heads do not manage users, offices, accounts, or any other config page.
- No ceiling for office funds (D11).
- No new report.

## 8. Deployment notes

- ⚠️ One migration, `AddFundingSourceOfficeId`, applied by hand to Azure SQL.
- The new grant is off for everyone until an admin sets it, so the release changes nothing until
  it is granted.
- Route access must be updated in **three** places for every new or widened page: the sidebar, the
  page's own `useMe` gate, and `(portal)/layout.tsx`'s `ppdoOnlyBudgetPlanning` list.

## 9. Ticket split

| Ticket | Scope | Depends on |
|---|---|---|
| **C1** — PPDO-106 | Office Ceilings page for finance; Allocation's ceiling becomes read-only; funding-source tabs built, General Fund only shown (D4) | — |
| **C2** — PPDO-107 | `CanManageOfficeSetup`: permission service, `/auth/me`, admin toggle, `Permission_Matrix.md` rows, and the two allocation writes accepting an own-office department head | — |
| **C3** — PPDO-108 | Division config scoped to a department head's own office; switches hidden and server-side dropped | PPDO-107 |
| **C4** — PPDO-109 ✅ | ⚠️ MIGRATION. `funding_sources.office_id`, filtered reads, office-stamped writes, shared rows read-only, the WFP/AIP pickers | PPDO-107 |

C1 is independent and is the one finance needs first for UAT.

## 10. Acceptance checklist

1. A finance user sets three offices' ceilings from the new page, and the Allocation page shows
   them read-only.
2. Only the General Fund tab is visible on the ceilings page.
3. A department head with the grant splits their own office's ceiling and assigns programmes.
4. The same department head gets a 403 when the request targets another office.
5. A department head creates a division; it lands in their office with every switch off.
6. A department head adds a fund source; an encoder in that office sees it in the picker, and an
   encoder in another office does not.
7. `GF` cannot be reused as an office fund code.
8. After PPDO accepts the office's AIP, office-setup writes return 409.
9. A user without the grant sees none of the new controls, and the URLs redirect.

## 11. Test focus

- `PermissionMatrixTests` gains a row for the new flag — a flag without one fails the build.
- The 403 case: a department head targeting a foreign office, on both allocation writes.
- `CanManagePpdoAllocation` still refuses a guest-office caller for their own office
  (`UpsertDivisions_AsOfficeUser_TargetingOwnOffice_IsAlsoForbidden` must keep passing) — the new
  grant must not become a back door into it.
- The division switches are dropped, not honoured, for a department head.
- `GetGeneralFundIdAsync` ignores an office fund coded `GF` if one is ever forced in (D7).
- The fund picker query returns shared + own office only.
- Every fund WRITE refuses another office's fund, at all five caller-fed sites, and refuses it with
  the same wording as a fund that does not exist. `FundingSourceScopeTests` pins the rule itself;
  each service suite pins its own door into it. ⚠️ Each of those guard tests was confirmed to FAIL
  with its guard removed — a guard test that passes either way proves nothing.
