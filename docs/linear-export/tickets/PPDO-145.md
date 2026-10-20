---
id: PPDO-145
title: 'Demo 2.14 T3 — Investment Planning pages open on the default fiscal year'
status: Done
project: PPDO Portal
labels: 
milestone: Demo 2 — change requests & fixes
created: 2026-09-27
completed: 2026-09-29
parent: PPDO-136
blocked_by: PPDO-143
related: PPDO-162
url: https://linear.app/ralphoksiprojects/issue/PPDO-145
uuid: a944919f-23cc-467b-9538-5c17d3478621
---

# PPDO-145 — Demo 2.14 T3 — Investment Planning pages open on the default fiscal year

**Spec:** `docs/v1.8/Default_Fiscal_Year_Spec.md`, sections §3.2, §3.3, §6.2 and §10. This ticket is blocked by [PPDO-143](https://linear.app/ralphoksiprojects/issue/PPDO-143/demo-214-t1-backend-default-fiscal-year-setting-host-office-gate). It does **not** need T2: for testing, set the row directly in the database.

## Scope

1. `useDefaultFiscalYear()` **hook.** It calls `GET /budget-planning/fiscal-years` once per page load, caches the result at module level (same pattern as `lib/me-cache`), and returns `defaultFiscalYear` (which may be null).
2. **Wire the default into seven pages.** On every page a year in the URL wins, and when the setting is unset the page keeps today's behaviour.
   * **Allocation:** `defaultFiscalYear ?? year + 1`.
   * **AIP index:** `defaultFiscalYear ?? year`. If the default isn't in `FY_OPTIONS`, add it.
   * **AIP Entry, Review and Review Search:** use the URL year first. Otherwise use the default, but only if it's in `YEARS`; if it isn't, use `FIRST_ENTERED_FISCAL_YEAR`.
   * **WFP and WFP Entry:** use `?aipId=` first. Otherwise preselect the AIP record whose `fiscalYear` equals the default. If there is no such record, keep today's behaviour.
3. **Verify only, no edits:** the dashboard, Office Ceilings and Report pick up the default through T1's server-side resolver.

## Rules

* **No wrong-year flash.** When the URL has no year, hold the year-dependent fetch behind the page's existing skeleton until the hook settles. When the URL has a year, don't wait.
* **Fail open.** If `/fiscal-years` fails, the page uses its own default silently.

## Acceptance (from spec §10)

* With the setting unset, every page behaves as before.
* With the default at FY 2028:
  * all ten pages open on FY 2028;
  * WFP preselects the FY 2028 AIP;
  * `?fiscalYear=2029` still opens FY 2029.
* With the default at FY 2027, AIP Entry opens on FY 2028.
* A guest-office Staff user lands on the default and still sees only their own office's data.
* AIP Entry never jumps from one year to another while loading.

**Manual-implementation candidate: good fit.** Once the hook exists, these are seven small, near-identical edits. Copy the pattern from `office-ceilings/page.tsx` (`current ?? data.fiscalYear`). Guidance level 1.

Commit: `feat(budget-planning): open Investment Planning pages on the default fiscal year (PPDO-136)`
