---
id: PPDO-163
title: 'Demo 2.14 follow-up — Office Ceilings errors on a year with no AIP; a saved default doesn''t reach the saver''s own pages'
status: Done
project: PPDO Portal
labels: 
milestone: Demo 2 — change requests & fixes
created: 2026-09-30
completed: 2026-09-30
parent: PPDO-136
blocked_by: (none)
related: PPDO-58, PPDO-164
url: https://linear.app/ralphoksiprojects/issue/PPDO-163
uuid: b060eb76-d2b0-4abd-a608-8b19f6a08126
---

# PPDO-163 — Demo 2.14 follow-up — Office Ceilings errors on a year with no AIP; a saved default doesn't reach the saver's own pages

Found by Ralph on 2026-09-30 in the §10 run of [PPDO-136](https://linear.app/ralphoksiprojects/issue/PPDO-136/demo-214-admin-configurable-default-fiscal-year-for-investment), step D.1 (default set to FY 2029, and no FY 2029 AIP exists).

## 1. Office Ceilings shows "Could not load this year's ceilings." for FY 2029

Every request returns 200. `getCeilingUsage` (`lib/allocation.ts`) passes the body through `unwrap()`, and `unwrap()` throws when `data` is null. The endpoint returns `data: null` by design when no AIP record exists for the year ([PPDO-58](https://linear.app/ralphoksiprojects/issue/PPDO-58/v18-48-pbo-ceiling-management-ui-picker-half-already-shipped), and the helper's own comment says so). One throw rejects the page's `Promise.all`, so the whole grid errors out.

This is older than [PPDO-136](https://linear.app/ralphoksiprojects/issue/PPDO-136/demo-214-admin-configurable-default-fiscal-year-for-investment): any year before its AIP is created was affected. The default year made it visible, and spec §3.2 expects "FY 2029 selected; panels show their existing empty states". The Allocation page already swallows the error, so only Office Ceilings breaks.

**Fix:** `getCeilingUsage` returns `data ?? null` instead of throwing.

## 2. After saving a new default, the admin's own client-side pages keep the old year

`useDefaultFiscalYear()` reads a per-tab module cache. The settings page saved to the server but never updated that cache, so AIP Records and AIP Entry in the same tab stayed on the previous default (2028) until a full reload. The server-resolved pages (dashboard, Office Ceilings, Report) switched to 2029, so the pages disagreed.

**Fix:** after a successful save or clear, the settings page writes the new value into the cache. Other users still pick it up on their next page load (decision 9 is unchanged).

## 3. "Last changed" shows the time 8 hours early after a reload

The GET returned `updatedAt` as `2026-09-30T02:05:38` with no `Z`. EF reads a SQL `datetime2` as `DateTimeKind.Unspecified`, and the browser parses a zone-less ISO string as local (Manila) time, so a save at 10:05 AM showed "2:05 AM" once the page was reloaded. The PUT response was already correct (`…Z`), because it is built from the clock.

**Fix (this page only):** `InvestmentPlanningSettingsService` marks the stamp `DateTimeKind.Utc` before returning it. It is TDD-covered by two new tests.

⚠️ **The same pattern is app-wide.** No EF convention or JSON converter marks any `DateTime` read from SQL as UTC, and pages such as the Audit Log format with `new Date(iso)`. That is tracked separately rather than widened here.

## Acceptance

- [ ] Default FY 2029, no FY 2029 AIP: Office Ceilings lists every office with no ceiling and no encoded figure, and no error
- [ ] Save 2029, then go to AIP Records / AIP Entry through the sidebar without reloading: both open on FY 2029
- [ ] Clear the default, then go to AIP Entry without reloading: it opens on FY 2028
- [ ] Reload the settings page after a save: "Last changed" shows the Manila time of the save, not 8 hours earlier
