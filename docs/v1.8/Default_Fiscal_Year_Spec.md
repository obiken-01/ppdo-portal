# v1.8.0 — Admin-configurable default fiscal year (PPDO-136)

> **Status: accepted** — 2026-09-27. Parent: PPDO-122 (Demo 2 change requests), item 2.14.
> Written to `docs/SPEC_STANDARD.md`. Decisions 1, 2 and 6 are Ralph's (Linear 2026-09-24; session
> 2026-09-27); decisions 3–5 and 7–9 are the defaults proposed in the PPDO-136 thread and accepted.

---

## 1. Goal

Investment Planning pages each decide "which fiscal year" on their own today — the calendar year, the
calendar year + 1, the newest AIP record, or the break year `FIRST_ENTERED_FISCAL_YEAR` — so as FY2028
entry ramps up beside FY2027 close-out, encoders and reviewers land on different years depending on
which page they open. This adds **one province-wide default fiscal year that a config manager sets**,
which every Investment Planning page opens on. Only the host office (PPDO) can set it. Users can still switch year on any page. When the
default is not set, every page behaves exactly as it does today.

---

## 2. Decisions (settled)

1. **One province-wide default, set by PPDO, overridable.** (Ralph, 2026-09-24.) Every in-scope page
   opens on it; the page's own year picker still works. Not per-office, not per-user.
2. **Allocation is in scope** (Ralph, 2026-09-27) — it is part of fiscal-year setup, and it is the
   page most out of step today (`new Date().getFullYear() + 1`). **Office Ceilings** comes with it:
   it is the ceiling half of the same setup flow and is linked from Allocation.
3. **A stored config value, not a computed one.** A computed default ("newest non-archived AIP
   record") cannot express the case the ticket names: moving everyone to FY2028 on October 1st
   *before* any FY2028 AIP record exists. A stored value can, and it is one row.
4. **Unset means today's behaviour, per page.** The setting is nullable and ships unset. Each page
   falls back to exactly what it does now, so deploying this changes nothing until someone sets it.
   This is why §4 exposes the raw setting (`defaultFiscalYear`) and not only a resolved year — the
   client pages each keep their own fallback.
5. **A year in the URL still wins.** `?fiscalYear=` (and WFP's `?aipId=`) are deep links from the
   kanban, review search, the returned-work banner and the WFP → Report link. The default only
   fills the gap when the URL says nothing — it never overrides an explicit link.
6. **Gate: `CanManageConfig` AND host office; SuperAdmin always.** (Ralph, 2026-09-27.) The value
   moves every office's pages, so it is PPDO's to set — a guest-office Admin, who holds
   `CanManageConfig` by role, must not be able to. SuperAdmin keeps it wherever their office points,
   for support access (the same load-bearing exemption as the per-user grants,
   `Permission_Matrix.md` §1). Resolved by a new **`PermissionService.CanManageInvestmentPlanningSettingsAsync`**
   — composed from existing inputs, **no new column or override** — because `CLAUDE.md` forbids
   inlining permission logic in a handler, and a method on `IPermissionService` gets a matrix row
   and a pinning test (`Matrix_CoversEveryFlagOnThePermissionService`) for free.

   ⚠️ Note the difference from `CanUploadAipAsync`, which is described as host-office-only but lets
   **any** Admin through before the host check. This method must check the office for Admin too —
   only SuperAdmin bypasses it.
7. **Read path: the existing `GET /api/budget-planning/fiscal-years`.** Every in-scope page's user
   already holds `CanAccessBudgetPlanning`, which that endpoint gates on, and three of the pages
   (dashboard, Office Ceilings, Report) already resolve their year through it. Readers get the
   default without being config managers.
8. **The default year is always offered in the pickers**, even with no AIP record for it yet —
   otherwise the October-1st case (decision 3) would select a year the dropdown cannot show.
9. **Fetched once per page load and shared** (`CLAUDE.md` — fetch shared state once). A new
   `useDefaultFiscalYear()` hook caches the `/fiscal-years` response at module level, the same
   pattern as `lib/me-cache`. A change takes effect for other users on their next full page load.

### Open follow-ups (not blocking)

- **LDIP pages and AIP New** are not in scope (see §7). Revisit if encoders ask for them.
- **A "Default" marker in the year pickers** (so a user can tell they have moved off it) — nice to
  have, not requested.

---

## 3. Behaviour

### 3.1 Setting the default

| Case | Given | When | Then |
|---|---|---|---|
| Happy path | Config manager, setting unset | Sets FY2028 and saves | Saved; toast "Default fiscal year set to FY 2028."; audit row written; `LogInformation` with `{FiscalYear}` and `{UserId}` |
| Change | Setting is FY2027 | Sets FY2028 | Saved; audit row old 2027 → new 2028 |
| Clear | Setting is FY2028 | Clicks **Clear default** and confirms | Saved as null; toast "Default cleared — pages use their own defaults."; every page reverts to today's behaviour |
| Edge: same value | Setting is FY2028 | Saves FY2028 again | 200, no audit row (no change) |
| Edge: no AIP for that year | No FY2029 AIP record exists | Sets FY2029 | Allowed (decision 3); pickers offer FY 2029 (decision 8) |
| Edge: concurrent edit | Two config managers save different years | Both save | Last write wins; both writes are audited. No lock — one scalar, and the audit log shows who did what |
| Failure: out of range | — | Saves 2019, or more than 3 years past the current Manila year | 400 "Fiscal year must be between 2020 and {currentYear + 3}." |
| Failure: malformed body | — | Body missing or `defaultFiscalYear` is not an integer or null | 400 "Request body is missing or malformed." |
| Failure: save fails | DB unavailable | Saves | 500 `LogError` with `ex`; UI shows the error banner and keeps the typed value |
| Role: SuperAdmin | — | Opens / saves | Allowed |
| Role: SuperAdmin in a guest office (or no office) | — | Opens / saves | Allowed — support exemption (decision 6) |
| Role: Admin, host office | — | Opens / saves | Allowed |
| Role: Admin, guest office | Holds `CanManageConfig` by role | Calls GET or PUT on the config route | **403**; no tile on `/config`; direct navigation shows the forbidden state |
| Role: Staff, host office, with `CanManageConfig` | Override or division flag true | Opens / saves | Allowed |
| Role: Staff, guest office, with `CanManageConfig` override | — | Calls GET or PUT | 403 — the office check applies whatever the override says |
| Role: Staff without `CanManageConfig` | — | Calls GET or PUT on the config route | 403; the config hub shows no tile; direct navigation shows the forbidden state |
| Role: user with no office (null `office_id`) | Not SuperAdmin | Calls GET or PUT | 403 — unassigned is not host (`OfficeScope.IsHostOfficeUser`) |
| Unauthenticated | — | Calls either route | 401 |

### 3.2 Reading the default (every in-scope page)

"Own default" means what the page does today, listed in §3.3.

| Case | Given | When | Then |
|---|---|---|---|
| Happy path | Default = FY2028, no year in URL | Opens any in-scope page | Page opens on FY2028 |
| URL wins | Default = FY2028 | Opens `…?fiscalYear=2027` (or WFP `?aipId=` of the FY2027 AIP) | Page opens on FY2027 |
| Override | Page opened on FY2028 | User picks FY2027 in the page's picker | Page switches to FY2027; the setting is unchanged; other pages still open on FY2028 |
| Unset | Setting null | Opens any in-scope page | Page opens on its own default — identical to today |
| Edge: default outside a page's range | Default = FY2027; AIP Entry/Review/Search only offer FY2028–2030 | Opens AIP Entry | Opens on `FIRST_ENTERED_FISCAL_YEAR` (2028) — FY2027 is uploaded, not entered, and cannot be selected there |
| Edge: WFP, no AIP record for the default | Default = FY2029, no FY2029 AIP | Opens WFP | Today's behaviour (auto-select when exactly one AIP exists, otherwise manual pick) |
| Edge: default year has no AIP (server-resolved pages) | Default = FY2029, no FY2029 AIP | Opens dashboard / Office Ceilings / Report | FY 2029 selected and listed in the picker; panels show their existing empty states |
| Failure: `/fiscal-years` fails | Network/API error | Opens a client-resolved page | Page falls back to its own default and loads normally — the default never blocks a page. No banner (nothing the user can act on). |
| Role: guest-office Staff | Default = FY2028 | Opens Budget Planning pages | Same FY2028 default — it is province-wide. Office/division scoping of the **data** is unchanged |
| Role: user without `CanAccessBudgetPlanning` | — | — | Unchanged — they cannot reach these pages today, and `/fiscal-years` still 403s |

### 3.3 In-scope pages and where each reads the default

| Page | Today's own default | How it gets the default |
|---|---|---|
| Budget Planning dashboard / readiness board (`budget-planning/page.tsx`) | Newest AIP year, else UTC year + 1 (server) | **Server** — `ResolveFiscalYearsAsync` change; no page edit |
| Office Ceilings (`office-ceilings/page.tsx`) | Same server resolver | **Server** — no page edit |
| Report (`report/page.tsx`, incl. the `aip/consolidated` redirect) | URL, else server resolver | **Server** — no page edit |
| Allocation (`allocation/page.tsx`) | Calendar year + 1 | Client — `defaultFiscalYear ?? year + 1` |
| AIP index (`aip/page.tsx`) | Calendar year; options = year −1 … +2 | Client — `defaultFiscalYear ?? year`; the default is added to `FY_OPTIONS` if missing |
| AIP Entry (`aip/entry/page.tsx`) | URL, else `FIRST_ENTERED_FISCAL_YEAR` | Client — URL, else default **if in `YEARS`**, else break year |
| AIP Review (`aip/review/page.tsx`) | Same as Entry | Same as Entry |
| AIP Review Search (`aip/review/search/page.tsx`) | Same as Entry | Same as Entry |
| WFP (`wfp/page.tsx`) | `?aipId=`, else no preselection | Client — `?aipId=`, else the AIP record whose `fiscalYear` = default, else today |
| WFP Entry (`wfp/entry/page.tsx`) | `?aipId=`, else the single AIP if only one | Client — `?aipId=`, else the default's AIP record, else today |

---

## 4. API contract

### `GET /api/config/investment-planning/default-fiscal-year` — JWT + `PermissionService.CanManageInvestmentPlanningSettingsAsync`

- 200: `ApiResponse<DefaultFiscalYearDto>`

  ```jsonc
  { "defaultFiscalYear": 2028,            // int | null
    "updatedAt": "2026-10-01T00:15:00Z",  // UTC | null when never set
    "updatedByName": "Juan Dela Cruz" }   // string | null
  ```
- 401 — no/invalid JWT. 403 — not a host-office config manager (and not SuperAdmin).

### `PUT /api/config/investment-planning/default-fiscal-year` — JWT + `CanManageInvestmentPlanningSettingsAsync`

- Request: `{ "defaultFiscalYear": 2028 }` — `null` clears it.
- 200: `ApiResponse<DefaultFiscalYearDto>` (the saved state).
- 400: `ApiResponse.Fail("Request body is missing or malformed.")`, or
  `ApiResponse.Fail("Fiscal year must be between 2020 and {max}.")` where `max` = current Manila year + 3
  (validator: `Validators/Config/UpdateDefaultFiscalYearValidator.cs`).
- 401 / 403 as above. 500 — save failure, generic message; exception logged, not returned.

### `GET /api/budget-planning/fiscal-years?fiscalYear={int?}` — **existing**, JWT + `CanAccessBudgetPlanningAsync`

Changed, not new. `FiscalYearsDto` gains one field:

```jsonc
{ "fiscalYear": 2028,                    // resolved — see below
  "availableFiscalYears": [2029, 2028, 2027],
  "defaultFiscalYear": 2028 }            // NEW — the raw setting, int | null
```

`ResolveFiscalYearsAsync` (shared by this endpoint and both dashboard endpoints) becomes:

```
resolved  = requested ?? setting ?? newest AIP year ?? UTC year + 1
available = distinct AIP years ∪ { setting }   (if set), newest first
```

The dashboard DTOs that already carry `fiscalYear` / `availableFiscalYears` pick this up with no
contract change. Errors unchanged (401 / 403).

---

## 5. Data model changes

Migration: **`AddInvestmentPlanningSettings`** — ⚠️ MIGRATION

### `investment_planning_settings` (new — snake_case)

A single-row settings table. Typed columns rather than a key/value table: one value today, and a
key/value store would turn the fiscal year into a string to parse.

| Column | Type | Null | Notes |
|---|---|---|---|
| `id` | `int` | no | PK. Always `1` — `CHECK (id = 1)` keeps it a single row |
| `default_fiscal_year` | `int` | yes | The setting. Null = unset |
| `updated_at` | `datetime2` | yes | UTC; null until first set |
| `updated_by_user_id` | `uniqueidentifier` | yes | FK → users, `ON DELETE SET NULL` |

C# entity `InvestmentPlanningSettings` with `InvestmentPlanningSettingsConfiguration`
(`.ToTable` / `.HasColumnName`, per `docs/NAMING_CONVENTIONS.md`).

**Backfill:** the migration seeds the single row `(1, NULL, NULL, NULL)` via `HasData`. No existing
data changes. No index needed — the only query is `WHERE id = 1`.

Repository: `IInvestmentPlanningSettingsRepository` with `GetAsync()` (the row) and `SaveAsync()`.
Service: `InvestmentPlanningSettingsService` (get / update with audit), and
`BudgetPlanningDashboardService.ResolveFiscalYearsAsync` reads the setting through the repository —
sequential awaits, never `Task.WhenAll` on the shared context.

Audit: `IAuditService.LogAsync("investment_planning_settings", 1, AuditAction.Update, { DefaultFiscalYear = old }, { DefaultFiscalYear = new })`.

---

## 6. UI states

### 6.1 New config page — `/config/investment-planning` ("Investment Planning Settings")

One section, **Default fiscal year**: help text *"Investment Planning pages open on this year. Users
can still switch year on any page. Leave unset to let each page choose."*, a number input (FY), a
**Save** button, a **Clear default** link-button (shown only when set), and a last-changed line.

| State | Content |
|---|---|
| Loading | Page header renders immediately; the section shows a skeleton of the input row + last-changed line, same height as loaded |
| Empty (unset) | Input empty with placeholder `e.g. 2028`; line reads "Not set — each page uses its own default." No Clear link |
| Error (load) | Inline error banner "Could not load the setting." with **Retry**; input hidden |
| Success | Toast on save/clear (copy in §3.1); last-changed line updates: "Last changed {Manila date/time} by {name}" |
| Read-only / forbidden | Tile **hidden** unless the user is SuperAdmin, or a config manager whose `/auth/me` has `isHostOffice: true` (courtesy only — the server enforces). Direct URL → the config section's existing forbidden state |
| Validation | Under the input: "Fiscal year must be between 2020 and {max}." — shown client-side before submit, and the server's 400 message rendered in the same place |

**Clear default** opens `ConfirmDialog`: *"Clear the default fiscal year? Every Investment Planning
page goes back to choosing its own year."*

Components: `ConfirmDialog`, `useToast` from `components/ui/`; page shell and flat input styling per
`docs/DESIGN_SYSTEM.md` (no `rounded-*`, PPDO tokens only, no `text-slate-700`). New tile on
`config/page.tsx` beside the other config tiles. **No new shared component.**

### 6.2 In-scope pages

No new controls. The only visible change is which year is selected on arrival.

| State | Content |
|---|---|
| Loading | **Client-resolved pages must not flash the wrong year.** Pages without a URL year hold the year-dependent fetch until `useDefaultFiscalYear()` settles, showing their existing skeleton. Pages with a URL year do not wait |
| Error | `/fiscal-years` failure → the page's own default, silently (§3.2) |
| Everything else | Unchanged |

---

## 7. Non-goals

- **Not a per-user "remember my last year" preference.** Decision 1.
- **Not per-office.** Decision 1 — a guest office still closing FY2027 switches year with the picker.
- **Does not open, close or archive a fiscal year**, and does not touch `AipRecord.Status` or what may
  be entered/uploaded for a year. It only chooses which year loads first. `FIRST_ENTERED_FISCAL_YEAR`
  stays a code constant — the break year is a process rule, not a preference.
- **LDIP pages, AIP New, and the main Dashboard calendar are out of scope.** LDIP spans a multi-year
  window; AIP New is a create form where the year is a deliberate input; the main Dashboard is the
  events calendar, not Investment Planning.
- **Does not add `?fiscalYear=` support to Allocation.** It reads no URL year today; this spec does
  not change that.
- **No live push of a changed default** to users already on a page — next page load (decision 9).

---

## 8. Deployment notes

- ⚠️ **Migration `AddInvestmentPlanningSettings`** — run `dotnet ef database update` manually against
  Azure SQL before or with the deploy. **CI does not run migrations.** Deploying the code without it
  breaks `/fiscal-years`, and with it the dashboard, Office Ceilings and Report.
- Ships **unset** — no behaviour change until a config manager sets it. After deploy, set it on
  `/config/investment-planning` when PPDO wants everyone moved (e.g. FY2028 on October 1st).
- No new NuGet/NPM dependencies, environment variables, or CORS changes.

---

## 9. Ticket split

| Ticket | Scope | Blocked by |
|---|---|---|
| **T1 — backend** | Entity + configuration + migration (⚠️), repository, service + validator, `CanManageInvestmentPlanningSettingsAsync` + `Permission_Matrix.md` row, `ConfigInvestmentPlanningFunctions.cs` (GET/PUT), `FiscalYearsDto.DefaultFiscalYear`, `ResolveFiscalYearsAsync` change, tests (§11) | — |
| **T2 — config page** | `/config/investment-planning` page + config hub tile, `lib/config.ts` client functions | T1 |
| **T3 — page readers** | `useDefaultFiscalYear()` hook; Allocation, AIP index, AIP Entry, Review, Review Search, WFP, WFP Entry per §3.3; verify dashboard / Office Ceilings / Report pick it up with no edit | T1 (T2 not required — can be tested by setting the row directly) |

**Manual-implementation candidate:** T3's per-page wiring once the hook exists — seven small,
near-identical edits with `office-ceilings/page.tsx` (`current ?? data.fiscalYear`) as the sibling to
copy. T1 is **not** a candidate: it carries an EF migration and a permission gate. T2 is borderline
(new page, but a plain single-field form).

---

## 10. Acceptance checklist

- [ ] With the setting unset, AIP Entry opens on FY 2028, Allocation opens on the calendar year + 1, and the dashboard opens on the newest AIP year — same as before the release
- [ ] As SuperAdmin, `/config` shows an **Investment Planning Settings** tile; opening it shows "Not set — each page uses its own default."
- [ ] Saving 2028 shows the toast "Default fiscal year set to FY 2028." and the line "Last changed … by {your name}"
- [ ] Entering 2019 and saving shows "Fiscal year must be between 2020 and {max}." under the input, and nothing is saved
- [ ] With the default at FY 2028: Allocation, AIP index, AIP Entry, Review, Review Search, dashboard, Office Ceilings and Report all open on FY 2028 when reached from the sidebar
- [ ] With the default at FY 2028: WFP and WFP Entry open with the FY 2028 AIP preselected
- [ ] With the default at FY 2028, opening `/budget-planning/aip/review?fiscalYear=2029` opens FY 2029
- [ ] On Allocation, switching to 2027 loads FY 2027; reloading `/config/investment-planning` still shows 2028
- [ ] With the default at FY 2029 and no FY 2029 AIP, the dashboard picker lists FY 2029 and it is selected
- [ ] With the default at FY 2027, AIP Entry opens on FY 2028 (2027 is not an entry year)
- [ ] **Clear default** asks for confirmation; after confirming, pages behave as in the first line
- [ ] A guest-office Staff user opening Budget Planning lands on the default year and still sees only their own office's data
- [ ] A Staff user without `CanManageConfig` sees no tile on `/config`, and opening `/config/investment-planning` directly shows the forbidden state
- [ ] An Admin account tied to a guest office sees no tile on `/config`, and opening `/config/investment-planning` directly shows the forbidden state
- [ ] A SuperAdmin account tied to a guest office can still open the page and save
- [ ] Loading AIP Entry from the sidebar never shows one year and then jumps to another
- [ ] After a save, the Recent Activity / audit log shows the change with old and new year

---

## 11. Test focus

- **`InvestmentPlanningSettingsServiceTests`** (new) — get when unset; set; change; clear; same value
  writes no audit row; audit row carries old/new; save failure returns a failed `ServiceResult` and logs.
- **`UpdateDefaultFiscalYearValidatorTests`** (new, TDD) — null accepted; 2020 and current+3 accepted;
  2019 and current+4 rejected with the §4 message; the bound uses Manila time.
- **`BudgetPlanningDashboardServiceTests`** — resolver order: requested beats setting; setting beats
  newest AIP; unset falls through to newest AIP, then UTC year + 1; setting with no AIP record is
  added to `availableFiscalYears` without duplicating an existing year; `DefaultFiscalYear` echoes
  the raw setting (including null).
- **`PermissionMatrixTests`** (TDD) — `CanManageInvestmentPlanningSettingsAsync`: SuperAdmin true in
  host, guest and no office; Admin true in host, **false in guest and no office**; host Staff follows
  `CanManageConfig` (override/division); guest Staff with override `true` is false. Add the matching
  §2 row to `Permission_Matrix.md`.
- **Functions (integration)** — GET/PUT: 401 without JWT; 403 for guest-office Admin and for Staff
  without `CanManageConfig`; 200 for SuperAdmin and host-office Admin; 400 for malformed body.
- **Frontend** — no unit harness for pages; covered by §10.
