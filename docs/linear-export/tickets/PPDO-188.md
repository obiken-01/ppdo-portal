---
id: PPDO-188
title: 'Perf: small tidy-ups (config by-id lookups, LDIP list count, PR lines, /auth/me, lazy editors, dead endpoint)'
status: Done
project: PPDO Portal
labels: 
milestone: v1.8.2 — Dashboard Redesign, Caching & Remaining Optimizations
created: 2026-10-04
completed: 2026-10-05
parent: (none)
blocked_by: (none)
related: (none)
url: https://linear.app/ralphoksiprojects/issue/PPDO-188
uuid: 114e29d0-8c76-4a94-881b-5b69b43f5f47
---

# PPDO-188 — Perf: small tidy-ups (config by-id lookups, LDIP list count, PR lines, /auth/me, lazy editors, dead endpoint)

From the performance audit, 2026-10-04 (findings **O9–O11, O13–O15**). Optimization ticket 7 of 8. Both sides, size S each. These can be split into separate PRs.

## Items

* **O9 — config by-id lookups:** about 30 call sites do `(await _repo.GetAllAsync()).FirstOrDefault(x => x.Id == id)` on small tables (accounts, offices, divisions, funding sources, eSRE, CC typologies). Replace them with feature-repository by-id methods. The tables are 4–286 rows, so this is about consistency more than speed.
* **O10 — LDIP list:** `LdipService.GetAllAsync` (line 70) loads each record's full program tree just to count programs. Use one grouped `COUNT`.
* **O11 — PR creation:** `PurchaseRequestService.cs:882` looks up the item master once per line. Use one `WHERE StockNo IN (…)`.
* **O13 —** `/auth/me`**:** `admin/users/page.tsx:668`, `announcements/page.tsx:64` and `resource-links/page.tsx:241` call `/auth/me` directly. Use `fetchMe()` from `lib/me-cache.ts`.
* **O14 — lazy editors:** load FullCalendar (`DashboardCalendar.tsx`, dashboard at 198 kB first load) and TipTap (`RichTextEditor.tsx`; announcements 265 kB, proposal editor 283 kB) with `next/dynamic`, plus a skeleton of the loaded size.
* **O15 — dead and unscoped endpoints:**
  * Remove `GET /dashboard/stats` and `DashboardService.GetStatsAsync`. It has no frontend caller and scans all PRs and items.
  * Scope `GetPendingEventsAsync` (`DashboardService.cs:153`) in SQL; today it reads every calendar event.

## Acceptance

- [ ] Each item's page or endpoint behaves the same; tests pass.
- [ ] O13: one `/auth/me` per page load on those three pages.
- [ ] O14: the `next build` table shows the dashboard and announcements first-load JS drop; no layout shift when the editor loads.

## Manual-coding note

**O13 and O15 are good hand-coding candidates:** small, frontend or deletion, easy to check. O9 is too, one service at a time, with `dotnet test` feedback.
