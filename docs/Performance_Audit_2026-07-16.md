# Full-application performance audit — findings

> **Status: findings only — no implementation yet.** Written 2026-07-16, triggered by the
> Budget Planning Dashboard rework (v1.4.5, RAL-161) surfacing 8 unfiltered full-table scans on
> one endpoint. Ralph asked for a whole-application pass to see how widespread this bug class is,
> before deciding what to fix. Audited against the project's own documented rules in
> `docs/PERFORMANCE_GUIDELINES.md` — read that first if any of the terminology below is unfamiliar.

---

## 1. Backend — query anti-patterns

Audited every file in `backend/PPDO.Application/Services/*.cs` for: (1) `GetAllAsync()` +
in-memory filter/count/lookup, (2) `Task.WhenAll` over a shared `DbContext`, (3) fat DTOs on
list/grid endpoints, (4) missing server-side pagination on growing tables.

### High severity

| Finding | File:line | Why it matters |
|---|---|---|
| `GeneratePRNoAsync` scans all of `purchase_requests` | `PurchaseRequestService.cs:443-465` | Runs on **every PR creation**, not just reads — the only finding in this audit that gets slower on writes, not just page loads. Worst instance found. |
| `GetStatsAsync` full-scans `purchase_requests` + `item_master` | `DashboardService.cs:234-244` | Main portal Dashboard's stat cards — hit on every visit to the app's actual landing page. |
| `InventoryService.GetStatsAsync` / `GetItemLedgerAsync` full-scan `purchase_requests` + `item_master` | `InventoryService.cs:61-78, 147` | Admin/SuperAdmin inventory dashboard + item ledger view. |
| `DeliveryService.GetAllAsync` — full PR scan + N+1 per-PR delivery query, no pagination | `DeliveryService.cs:56-81` | Compounds two problems: full scan *and* one query per row. |
| `GetPendingEventsAsync` full-scans `calendar_events` | `DashboardService.cs:151-166` | Unbounded table, grows forever. |
| No server-side pagination: PR list, Delivery list, Item Master list | `PurchaseRequestService.cs:63-85`, `DeliveryService.cs:56-81`, `ItemService.cs:33-38` | These three tables grow the fastest of anything in the system — every list endpoint returns every row today. |

### Medium severity

| Finding | File:line | Note |
|---|---|---|
| `AipService.GetAllAsync` loads entire `users` table for a name lookup | `AipService.cs:63-64` | Exactly the pattern the doc calls out by name ("not a dictionary built from the whole users table"). |
| `AllocationService` reloads full `budget_ceilings`/`division_allocations` tables repeatedly | `AllocationService.cs:79-84, 121-141, 160-176, 339-395, 472-474` | Grows with office × fiscal-year × fund combinations; hit on nearly every Allocation-page call. |
| `ProcurementPresetService` full-scans `price_index_items` on every preset save | `ProcurementPresetService.cs:183, 213` | GSO catalogue can run to hundreds/thousands of rows. |
| AIP/LDIP full-scan-for-sequence patterns | `AipService.cs:229-231`, `WfpReportService.cs:126-130`, `LdipService.cs:103-104, 564-565` | Bounded by fiscal-year count today; will compound as years accumulate. |

### Low severity (small config tables — matches the doc's own accepted precedent)

`AccountService`, `PriceIndexService`, `OfficeService`, `DivisionService`, `FundingSourceService`,
`AnnouncementService`, `ResourceLinkService`, plus scattered `Division`/`Office`/`Account`/
`FundingSource` lookups inside `WfpService`/`WfpCeilingService`/`WfpExpenditureService`. These are
all genuinely small (tens to a few hundred rows), matching the doc's own stated carve-out.

### No violations found

- **`Task.WhenAll` over a shared `DbContext`** — zero matches anywhere in the backend. The one
  documented past incident (`DashboardService.GetStatsAsync`) is already fixed to sequential
  `await`s and nothing has reintroduced the pattern.
- **Fat list DTOs** — the documented past incident (18-field AIP/WFP activity DTO, RAL-89) is
  already fixed with a slim summary endpoint; nothing else audited carries unused heavy fields on
  a list response.

---

## 2. Frontend — performance anti-patterns

Audited every page under `frontend/src/app/(portal)/**` plus the public landing/login pages, for:
(1) `/auth/me` calls bypassing the shared `useMe()`/`fetchMe()` cache, (2) loading states that
cause layout shift, (3) raw `<img>` tags, (4) client-side N+1 fetching.

### 1. Duplicate `/auth/me` calls (bypassing `useMe()`)

The portal shell (`layout.tsx:121`) already calls `fetchMe()` once per portal mount. Any page that
*also* calls `api.get("/auth/me")` directly fires a redundant second request — the exact RAL-90
bug, reintroduced page-by-page.

| Page | Severity |
|---|---|
| `dashboard/page.tsx:39` | **High** — the actual landing page for office users; double-fetches on every visit |
| `admin/users/page.tsx:482` | Medium |
| `announcements/page.tsx:63` | Medium |
| `resource-links/page.tsx:225` | Medium |
| `inventory/pr-report/page.tsx:236` | Medium |
| `budget-planning/aip/detail/page.tsx:152` | Medium |
| `budget-planning/aip/new/page.tsx:36` | Low-Medium |
| `budget-planning/aip/import-preview/page.tsx:66` | Low-Medium |

Confirmed correct (`useMe()`): `budget-planning/wfp/page.tsx`, `wfp/entry/page.tsx`,
`report/page.tsx`, `aip/page.tsx`, `ldip/page.tsx` + its subforms, `allocation/page.tsx`,
`budget-planning/page.tsx` itself. `login/page.tsx`'s direct `/auth/me` call is fine — it runs
pre-portal-mount, one-time only.

### 2. Loading states causing layout shift (CLS)

**Good pattern already in place** (copy this, don't reinvent it): `DataTable.tsx:135-169` renders
the real `<thead>` immediately with skeleton `<tbody>` rows sized to `pageSize` — header and row
height never shift. `config/accounts`, `config/divisions`, `budget-planning/aip`,
`budget-planning/ldip` already build on this correctly (no full-page auth-gate spinner blocking
the shell).

**Failing — full-page auth-gate spinner swapped for the entire page** (identical shape to the
`/config/accounts` regression the doc documents as already fixed elsewhere):

- High: `inventory/items-master`, `inventory/pr-register`, `inventory/item-ledger`,
  `inventory/distribution`, `admin/users/page.tsx:672-681`
- Medium: `config/offices`, `config/funding-sources`, `config/price-index`,
  `config/procurement-presets`, `inventory/create-pr`, `inventory/receive-delivery`,
  `inventory/pr-report`, `inventory/page.tsx`, `config/page.tsx`

**Failing — section-level spinner swapped for a much larger block** (same CLS shape, smaller
blast radius):

- **High**: `budget-planning/wfp/entry/page.tsx:1400-1404` — WFP entry is the same page RAL-90 was
  about; high traffic.
- Medium: `budget-planning/allocation/page.tsx:801-816`, `budget-planning/report/page.tsx:658-663`

Low/framework-level: `announcements/page.tsx:269` (returns `null`, no visible jump), the
route-segment `loading.tsx` (brief, framework-managed).

### 3. Raw `<img>` tags

**No raw `<img>` tags exist anywhere in the portal** (`(portal)/**`) — clean. All instances are on
the **public** site, and only the login page was actually fixed:

- **High**: `(public)/page.tsx:33-39` — `Ph_seal_occidental_mindoro.png`, raw PNG **100,878
  bytes** vs. 26,644 bytes for the `.webp` version already in use on the login page for the *same
  asset*.
- **High**: `(public)/page.tsx:57-63` — `Bagong_Pilipinas_logo.png`, raw PNG **88,466 bytes** vs.
  27,188 bytes `.webp`.
- Low: `(public)/page.tsx:42-48` and `components/landing/Navbar.tsx:23-29` — same
  `ppdo-logo-placeholder.png`, genuinely tiny (1.9 KB), low value in converting.

Together the two large landing-page PNGs total **~189 KB** — essentially the identical "184 KB of
raw PNG" figure the guidelines already cite as fixed (RAL-91) — but that fix only touched
`login/page.tsx`. The landing page and `Navbar.tsx` ship the same source images unconverted, with
`eslint-disable-next-line @next/next/no-img-element` suppressing the lint rule that would have
caught it.

### 4. Client-side N+1 fetching

One instance found: `inventory/receive-delivery/page.tsx:269-282` — after loading a PR's delivery
summaries, fires one `GET /deliveries/{id}` **per delivery** via `Promise.all` to sum
`qtyDelivered`, instead of one batched call. **Medium** — delivery counts per PR are small today
but grow over time, same risk shape as the backend N+1 findings above.

No other list page (PR Report, Items Master, Item Ledger, PR Register, Distribution, Create PR,
Admin Users, Announcements, Resource Links, any Budget Planning or Config page) does this — all
consume a single batched/detail response already.

---

## 3. Prioritization (confirmed with Ralph 2026-07-16)

Feature-area order, not just severity: **Budget Planning first, every other current feature
second, Inventory last** (Inventory is due for a heavier separate pass later, not bundled with
this general cleanup). Within each tier, highest-traffic/highest-severity first.

### Tier 1 — Budget Planning

- `AllocationService` repeated full-table `budget_ceilings`/`division_allocations` reloads
  (`AllocationService.cs:79-84, 121-141, 160-176, 339-395, 472-474`) — Medium severity but hit on
  nearly every Allocation-page call, the single biggest Budget Planning finding.
- `ProcurementPresetService` full `price_index_items` scan per preset save
  (`ProcurementPresetService.cs:183, 213`) — feeds WFP procurement line items.
- AIP/LDIP full-scan-for-sequence patterns (`AipService.cs:229-231`, `WfpReportService.cs:126-130`,
  `LdipService.cs:103-104, 564-565`) and the `users`-table name-lookup scan
  (`AipService.cs:63-64`).
- Frontend CLS: `budget-planning/wfp/entry/page.tsx:1400-1404` (**High** — same page RAL-90 was
  about), `budget-planning/allocation/page.tsx:801-816`, `budget-planning/report/page.tsx:658-663`.
- Frontend duplicate `/auth/me`: `budget-planning/aip/detail/page.tsx:152`,
  `budget-planning/aip/new/page.tsx:36`, `budget-planning/aip/import-preview/page.tsx:66`.

### Tier 2 — everything else except Inventory

- `DashboardService.GetStatsAsync` full-scans `purchase_requests`/`item_master`
  (`DashboardService.cs:234-244`) and `GetPendingEventsAsync` full-scans `calendar_events`
  (`DashboardService.cs:151-166`) — main portal Dashboard, hit on literally every login. (Reads
  Inventory tables, but the page itself is Dashboard/Calendar, not Inventory.)
- Frontend duplicate `/auth/me`: `dashboard/page.tsx:39` (**High** — pairs with the finding
  above), `admin/users/page.tsx:482`, `announcements/page.tsx:63`, `resource-links/page.tsx:225`.
- Frontend CLS full-page spinners: `admin/users/page.tsx:672-681`, `config/offices`,
  `config/funding-sources`, `config/price-index`, `config/procurement-presets`, `config/page.tsx`.
- Landing-page PNG → WebP (`(public)/page.tsx:33-39, 57-63`) — smallest, most mechanical fix in
  the whole audit; the conversion recipe already exists from the login-page fix (RAL-91), just
  needs re-running against these two files.

### Tier 3 — Inventory (last, expect a dedicated future pass)

- `PurchaseRequestService.GeneratePRNoAsync` full scan on **every PR creation** — despite being
  the single worst finding in the whole audit (only write-path bug found), it's Inventory-owned
  and stays queued behind Tiers 1-2 per this ordering.
- `InventoryService.GetStatsAsync`/`GetItemLedgerAsync`, `DeliveryService.GetAllAsync` (full scan
  + N+1), no pagination on PR/Delivery/Item Master lists.
- Frontend CLS full-page spinners: `inventory/items-master`, `inventory/pr-register`,
  `inventory/item-ledger`, `inventory/distribution`, `inventory/create-pr`,
  `inventory/receive-delivery`, `inventory/pr-report`, `inventory/page.tsx`.
- Frontend duplicate `/auth/me`: `inventory/pr-report/page.tsx:236`.
- Client-side N+1: `inventory/receive-delivery/page.tsx:269-282`.

Everything else (Medium/Low within each tier) is real but lower-traffic — worth folding in
opportunistically while working through its tier, not a blocker.
