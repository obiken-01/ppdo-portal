---
id: PPDO-204
title: 'Find the EF Core query using First/FirstOrDefault without OrderBy'
status: Done
project: PPDO Portal
labels: backend, Improvement
milestone: v1.8.3 — Concurrent-edit guard: turn it on
created: 2026-10-11
completed: 2026-10-11
parent: (none)
blocked_by: (none)
related: PPDO-195
url: https://linear.app/ralphoksiprojects/issue/PPDO-204
uuid: 64dec0e6-0d41-4e21-85ef-dea46d352044
---

# PPDO-204 — Find the EF Core query using First/FirstOrDefault without OrderBy

Seen in UAT App Insights during the [PPDO-195](https://linear.app/ralphoksiprojects/issue/PPDO-195/our-own-uat-test-pass-on-v182-using-pto-as-the-test-office-weekend-10) run (2026-10-11, v1.8.2): **7 warnings** (first 02:08 UTC):

> The query uses the 'First'/'FirstOrDefault' operator without 'OrderBy' and filter operators. This may lead to unpredictable results.

Some repository query picks "the first" row with no sort order, so which row comes back can change between executions or after an index/plan change. Not failing today.

## To do

1. Find it: `traces | where message startswith "The query uses the 'First'" | project timestamp, operation_Name` gives the Function name; or set EF to throw on this warning in a local run (`ConfigureWarnings(w => w.Throw(CoreEventId.FirstWithoutOrderByAndFilterWarning))`).
2. Add the intended `OrderBy` (or a filter that makes it unique).
3. Consider leaving the warning set to throw in tests so it cannot come back.
