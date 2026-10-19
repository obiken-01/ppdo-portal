---
id: PPDO-164
title: 'DB-read timestamps serialize without Z — display 8h early in Manila'
status: Done
project: PPDO Portal
labels: 
milestone: v1.8.0 — Office Users, AIP Redesign & Reviewer Flow
created: 2026-09-30
completed: 2026-09-30
parent: (none)
blocked_by: (none)
related: PPDO-163
url: https://linear.app/ralphoksiprojects/issue/PPDO-164
uuid: 0f7e9990-e449-42ad-90d8-33c8a6226fbe
---

# PPDO-164 — DB-read timestamps serialize without Z — display 8h early in Manila

**Problem (found 2026-09-30 in** [PPDO-163](https://linear.app/ralphoksiprojects/issue/PPDO-163/demo-214-follow-up-office-ceilings-errors-on-a-year-with-no-aip-a)**)**

EF Core reads SQL `datetime2` back as `DateTimeKind.Unspecified`; System.Text.Json then emits no trailing `Z` (e.g. `"2026-09-30T02:05:38.9"`). The frontend does `new Date(iso).toLocaleString("en-PH", { timeZone: "Asia/Manila" })`, and a zone-less ISO string is parsed as browser-local time, so every UTC stamp read from the DB shows 8h early for Manila users. Values written in the same request carry `Z`, so a PUT response looks right and the next GET is wrong.

[PPDO-163](https://linear.app/ralphoksiprojects/issue/PPDO-163/demo-214-follow-up-office-ceilings-errors-on-a-year-with-no-aip-a) (PR [obiken-01/ppdo-portal#399](https://linear.app/ralphoksiprojects/review/fixbudget-planning-office-ceilings-loads-a-year-with-no-aip-saved-f0c59a8bb366)) fixed only `InvestmentPlanningSettingsService` with a local `AsUtc()`. No global convention exists.

**Fix**

* `AppDbContext.ConfigureConventions`: map every `DateTime` / `DateTime?` through a ValueConverter that reads back `DateTimeKind.Utc`. No schema change, no migration.
* Audit for columns that are not UTC instants. Finding: `CalendarEvent.StartDate/EndDate` are documented "midnight UTC" (frontend uses `.slice(0,10)`), so no exemption needed. `PurchaseRequest.PRDate` is `DateOnly`, unaffected.
* Remove the redundant `AsUtc()` in `InvestmentPlanningSettingsService` (tests stay).
* Test: every DateTime property in the model converts provider values to Kind=Utc.

**Verify (manual)**: Audit Log page + one history view show correct Manila times vs `audit_log.changed_at`.

Branch: `fix/v1.8.0-<ticket>-utc-timestamps` -> PR into `release/1.8.0`.
