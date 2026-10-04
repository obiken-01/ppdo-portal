---
status: findings
version: v1.8.x
date: 2026-10-04
supersedes: none (follows ../Performance_Audit_2026-07-16.md)
---

# Performance audit — 2026-10-04

A whole-codebase pass for parts that need optimization, after v1.8.0 reached UAT. **No code was changed.**
It follows the [2026-07-16 audit](../Performance_Audit_2026-07-16.md) and the rules in
[PERFORMANCE_GUIDELINES.md](../PERFORMANCE_GUIDELINES.md).

How it was checked:

- **Static scan** of every Application service and repository: full-table reads, queries inside loops,
  `Task.WhenAll` on the shared `DbContext`, tracking, and payload shape.
- **Measured** on local dev (`release/1.8.0` @ `6d7c87fc`, warm, three runs each), logged in as `jose.santos`
  (Admin, PPDO host office), against the real local data: 6,398 price index items, 2,567 AIP activities.
- **Production check:** one read-only request to the production API's public `/announcements` endpoint, to
  see whether responses are compressed.
- **Frontend:** a production `next build` for bundle sizes, plus the request order on AIP Entry and WFP Entry
  in the browser.

## 1. Summary

- **Most endpoints are fast and small.** Of the 27 endpoints that returned data, 23 answer in under 0.2 s with
  under 80 KB. (Seven more answered 400 or 403 for this account and are not counted.)
- **The cost is payload size, and the production API does not compress it.** Three responses are 0.7–1.6 MB.
  They are about 10× smaller gzipped (1.5 MB → 155 KB), but production sends no `Content-Encoding`.
- **AIP Entry loads in a five-step chain.** At production's ~0.4 s per request, that is about 2 s before the
  page has its data.
- **Bundles are fine.** Shared JS is 87 KB; the heaviest pages are the rich-text editor ones (265–283 KB).
- **Old bug class, mostly gone.** The full-table reads that remain are on small config tables (13–286 rows),
  except the price index (6,400 rows), which still does it on every single-item read and save.

## 2. Measurements

Local dev, warm. Sizes are uncompressed, which is what production sends today.

| Request | Time | Size | Note |
|---|---|---|---|
| `GET /budget-planning/aip/13` (FY 2027, 2,375 activities, 34 offices) | 0.51 s | **1,520 KB** | 155 KB gzipped. The detail page then shows 13 collapsed office rows |
| `GET /budget-planning/aip/13/summary` | 0.28 s | **724 KB** | WFP pages use this one |
| `GET /config/price-index/picker?active=true` | 0.10–0.20 s | **713 KB** | AIP Entry, WFP Entry, the review modal (editors), procurement presets |
| `GET /config/price-index` (full list) | 0.24 s | 1,585 KB | No page calls it any more (CSV export path only) |
| `GET /config/price-index/picker?search=paper` | 0.11 s | 14 KB | Server-side search already works |
| `GET /budget-planning/aip/33` (FY 2028, 135 activities) | 0.05 s | 96 KB | |
| `GET /config/cc-typologies` | 0.03 s | 78 KB | |
| `GET /config/accounts` | 0.06 s | 70 KB | Loaded on AIP Entry, WFP Entry and every review-modal open |
| The other 20 endpoints that returned data | 0.01–0.17 s | 0.2–39 KB | Dashboard, lists, config, inventory, users |

What fills the 1.5 MB AIP response, per activity (×2,375):

- Text the detail grid shows: `name` 214 KB, `expectedOutputs` 170 KB, `refCode` 97 KB.
- **Fields that are empty on almost every row:** `isSynthetic`, `divisionName`, `divisionId`, `isCreation`,
  `ccMitigation`, `ccAdaptation`, `ccTypologyCode`. Together that is about 320 KB of `null`/`false`.

**Production compression check:** `GET /api/announcements` with `Accept-Encoding: gzip, deflate, br` came
back with no `Content-Encoding` header. That response is only 5 KB. Re-check on a large authenticated
response before acting on it, because a 5 KB body could be below a compression threshold.

## 3. Findings

Priority: **P1** large, measured cost; **P2** real but bounded; **P3** tidy-up.

| # | P | Finding | Where | Suggested fix |
|---|---|---|---|---|
| O1 | P1 | **API responses are not compressed.** The AIP detail is 1.5 MB on the wire; gzip makes it 155 KB | `PPDO.Functions/Program.cs` (ASP.NET Core integration is already on) | Compress JSON responses over ~1 KB. **Spike first:** confirm compression survives the Functions host proxy, or find where it is lost |
| O2 | P1 | **The price-index picker ships the whole catalogue (713 KB)** on every visit to AIP Entry and WFP Entry, and on each review-modal open for an editor | `aip/entry/page.tsx:376`, `wfp/entry/page.tsx:984`, `AipActivityReviewModal.tsx:108`, `procurement-presets/page.tsx:190` | Type-ahead: call `/picker?search=` as the user types (14 KB per search, already supported). Or let V18-65's cache serve it, with an ETag so a refresh that finds no change costs a 304 |
| O3 | P2 | **AIP Entry loads in a chain:** `aip?fiscalYear` → `aip/{id}` → `readiness` → `divisions` → `comments`. Each waits for the one before | `aip/entry/page.tsx:338-361` | After the record id is known, fire detail, readiness, divisions and comments together. About 2 s → about 1 s at production latency |
| O4 | P2 | **AIP Entry downloads the whole province's AIP for a PPDO user.** A host-office caller gets every office; the page edits one. FY 2028 will be the size of record 13 once every office has encoded | `AipService.GetByIdAsync`, `aip/entry/page.tsx:352` | Optional `officeId` on `GET /aip/{id}`, narrowing (never widening) the existing scope. Entry passes its own office |
| O5 | P2 | **Empty fields fill a fifth of the AIP payload** (about 320 KB of `null`/`false` on record 13) | `ConfigHttp.Json` and the per-function `JsonSerializerOptions` | `DefaultIgnoreCondition = WhenWritingNull` on the AIP detail and summary responses. ⚠️ The frontend must treat a missing field as `null`: check the types before switching it on |
| O6 | P2 | **Price index single-item reads and writes load all 6,400 rows, tracked:** get by id, create, update and delete each call `GetAllAsync` | `PriceIndexService.cs:92, 109, 146, 184` | By-id query; duplicate check as an `AnyAsync` on name + unit |
| O7 | P2 | **Stock balance import runs 3–4 queries per row inside one transaction:** item check, existing row, on-hand total | `StockBalanceService.cs:359-380` | Load the item masters, existing balances and on-hand totals for all stock numbers in the file up front, then loop in memory. Inventory, so last in line |
| O8 | P2 | **Read queries are tracked.** Only 11 queries in 5 repositories use `AsNoTracking`, so every list and detail read builds change-tracking state it never uses (2,375 activities on record 13) | `Repository.GetAllAsync`, `AipRepository` reads | `AsNoTracking()` on read-only repository methods, starting with the AIP hierarchy reads and `GetAllAsync`. ⚠️ Not on reads whose entities a service then edits and saves |
| O9 | P3 | **By-id lookups on small config tables use `GetAllAsync().FirstOrDefault`**, about 30 call sites (accounts, offices, divisions, funding sources, eSRE, CC typologies) | `AccountService`, `OfficeService`, `FundingSourceService`, `WfpCeilingService`, `LdipService`, others | Feature-repository by-id methods. The tables are 4–286 rows, so this is about consistency more than speed |
| O10 | P3 | **The LDIP list loads every record's full program tree just to count programs** (one query per record) | `LdipService.cs:70-75` | One grouped `COUNT` for the listed records |
| O11 | P3 | **PR creation looks up the item master once per line** | `PurchaseRequestService.cs:882` | One `WHERE StockNo IN (…)` for all lines |
| O12 | P3 | **Every audited write is a second `SaveChanges`**: one round trip more per write, and the audit row is not in the same transaction as the change | `AuditService.cs:90` | Add the audit row before the service's own save. Changes the audit contract, so it needs its own spec line |
| O13 | P3 | **Three pages fetch `/auth/me` themselves** instead of using the shared cache | `admin/users/page.tsx:668`, `announcements/page.tsx:64`, `resource-links/page.tsx:241` | `fetchMe()` from `lib/me-cache.ts` |
| O14 | P3 | **No lazy-loaded heavy components.** FullCalendar (dashboard, 198 kB first load) and TipTap (announcements 265 kB, proposal editor 283 kB) are in the page bundle | `DashboardCalendar.tsx`, `RichTextEditor.tsx` | `next/dynamic` with a skeleton of the loaded size. Small win; the shared JS is already lean |
| O15 | P3 | **Carried from the 2026-07-16 audit, still open:** `GetStatsAsync` scans all PRs and items, but `GET /dashboard/stats` has no frontend caller any more; `GetPendingEventsAsync` scans all calendar events; the PR list has no paging | `DashboardService.cs:153, 278`, `PurchaseRequestService.cs:142` | Remove `/dashboard/stats` (dead endpoint); scope pending events in SQL; page the PR list when the inventory pass comes round |

Things that are already right and should stay:

- No `Task.WhenAll` on the shared `DbContext`.
- The AIP hierarchy reads are set-based, one query per level.
- The dashboard bands, review search and notifications are grouped queries.
- Delivery list and price-index grid are paged server-side.
- PR numbering uses `MAX`, not a scan.
- Notifications refetch on tab focus rather than polling.

## 4. Suggested tickets

Ordered by value for effort. None needs a migration. Filed 2026-10-04 in milestone "v1.8.1 — Caching,
Dashboard Improvements & Optimization". **Ralph: optimizations go first in v1.8.1**, ahead of the dashboard
redesign and the caching groundwork. Priorities: 1–3 High, 4–6 Medium, 7–8 Low.

| Order | Ticket | Findings | Scope | Size |
|---|---|---|---|---|
| 1 · **PPDO-182** | Compress API responses (spike, then switch on) | O1 | Backend | S |
| 2 · **PPDO-183** | Price-index picker as type-ahead, or cached with ETag | O2 | Frontend (+ backend for ETag) | M |
| 3 · **PPDO-184** | AIP Entry: parallel loads and an office-scoped detail | O3, O4 | Backend + frontend | M |
| 4 · **PPDO-185** | Slim the AIP detail/summary JSON (omit empty fields) | O5 | Backend + frontend types | S |
| 5 · **PPDO-186** | Price index: by-id and duplicate checks in SQL | O6 | Backend | S |
| 6 · **PPDO-187** | `AsNoTracking` on read-only repository methods | O8 | Backend | S |
| 7 · **PPDO-188** | Small tidy-ups: config by-id lookups, LDIP list count, PR line lookup, `/auth/me` callers, lazy editors, dead `/dashboard/stats` | O9–O11, O13–O15 | Both | S each |
| 8 · **PPDO-189** | Stock balance import, set-based | O7 | Backend | M |

O12 (audit row in the same save) is left out of the list: it changes the audit contract and wants a spec line first.

**Overlap with v1.8.1:**

- **O2 overlaps V18-65**, the shared reference-data cache, which already lists the price-index picker. If V18-65 is
  built first, ticket 2 becomes "add ETag/304 to the cached endpoints".
- **O1 and O5 shrink everything V18-65 caches**, so they are worth doing first either way.
- **Dashboard requests** are already covered by PPDO-177 (parallel loading), so they are not repeated here.

**Manual-coding candidates (CLAUDE.md):**

- **Good ones for you:**
  - Ticket 5 (price index). `ItemMasterRepository.GetByStockNoAsync` is the sibling to copy, and
    `PriceIndexServiceTests` gives `dotnet test` feedback.
  - The `/auth/me` and dead-endpoint parts of ticket 7.
- **Not candidates:**
  - Ticket 3 touches AIP read scope, where a mistake is a data leak the compiler does not catch.
  - Ticket 6 can silently break a save if applied to a read that is later edited.

## 5. Open questions

1. **Ticket 2: type-ahead or cache?** Default: cache with ETag, since V18-65 is already planned and type-ahead
   changes how the picker feels to encoders.
2. **Ticket 3: does a PPDO reviewer on AIP Entry ever need other offices' rows?** Default: no. Entry edits one
   office; review has its own screens.
3. **Should production compression be checked on UAT first?** Default: yes, UAT is the deploy rehearsal.
