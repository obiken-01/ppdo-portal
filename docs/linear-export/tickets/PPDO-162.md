---
id: PPDO-162
title: 'Demo 2.14 follow-up — Budget Ceilings ignores the dashboard link''s year/office; settings page missing from the Configuration sidebar'
status: Done
project: PPDO Portal
labels: 
milestone: Demo 2 — change requests & fixes
created: 2026-09-30
completed: 2026-09-30
parent: PPDO-136
blocked_by: (none)
related: PPDO-145
url: https://linear.app/ralphoksiprojects/issue/PPDO-162
uuid: 725d1d81-f419-4450-b0e0-c8890b28581a
---

# PPDO-162 — Demo 2.14 follow-up — Budget Ceilings ignores the dashboard link's year/office; settings page missing from the Configuration sidebar

Found by Ralph on 2026-09-30 during the §10 acceptance run of [PPDO-136](https://linear.app/ralphoksiprojects/issue/PPDO-136/demo-214-admin-configurable-default-fiscal-year-for-investment).

## 1. The allocation / Budget Ceilings page ignores the URL's `fiscalYear` and `officeId`

The dashboard links to the page with the year and office in the URL (`budget-planning/page.tsx`, `DivisionTable.tsx`, `OfficeTable.tsx`): e.g. `/budget-planning/allocation?officeId=7&fiscalYear=2028`. The page reads neither, so it opened on **FY 2027** (calendar year + 1) while the dashboard was showing FY 2028.

This predates [PPDO-145](https://linear.app/ralphoksiprojects/issue/PPDO-145/demo-214-t3-investment-planning-pages-open-on-the-default-fiscal-year): the page never read the URL. [PPDO-145](https://linear.app/ralphoksiprojects/issue/PPDO-145/demo-214-t3-investment-planning-pages-open-on-the-default-fiscal-year) spec §7 recorded "no URL year on this page" as a known gap. With a default fiscal year set, the mismatch is hidden only while the dashboard shows that same default year.

**Fix:** a valid URL `fiscalYear` wins over the admin default, same as AIP Review. A URL `officeId` preselects that office, but only for a user who can choose an office. Everyone else stays on their own office, and the backend's office scope is unchanged.

## 2. Investment Planning Settings has no sidebar entry

The page is reachable only from the tile on Configuration → Dashboard. Add it to the Configuration sidebar group. Gate it on the same rule as the page, `SuperAdmin || (canManageConfig && isHostOffice)`, so a guest-office SuperAdmin sees it as well.

## Acceptance

- [ ] Dashboard on FY 2028 → a division row / office link opens Budget Ceilings on FY 2028 and the linked office
- [ ] Opening `/budget-planning/allocation` with no parameters still uses the default year (or calendar year + 1)
- [ ] A guest-office user following a link with another office's id still sees only their own office
- [ ] The Configuration sidebar shows Investment Planning Settings for PPDO Admins with config access and for SuperAdmin, and hides it from guest-office Admins and Staff
