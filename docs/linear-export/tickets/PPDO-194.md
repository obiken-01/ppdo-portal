---
id: PPDO-194
title: 'Budget Planning dashboard reads the same config tables up to 3× per request — load each once'
status: Done
project: PPDO Portal
labels: 
milestone: v1.8.3 — Concurrent-edit guard: turn it on
created: 2026-10-08
completed: 2026-10-08
parent: (none)
blocked_by: (none)
related: PPDO-44
url: https://linear.app/ralphoksiprojects/issue/PPDO-194
uuid: a0122a8b-9f01-4f09-8398-b2fe496c621a
---

# PPDO-194 — Budget Planning dashboard reads the same config tables up to 3× per request — load each once

## Problem

Found while doing [PPDO-44](https://linear.app/ralphoksiprojects/issue/PPDO-44/cache-config-reference-data-imemorycache-is-registered-but-used-only)'s before-count (2026-10-08, local, `admin`). The two heaviest Budget Planning dashboard endpoints re-read the same small config tables several times **inside a single request**:

| Endpoint | SQL cmds | Config-table reads |
| -- | -- | -- |
| `GET budget-planning/dashboard` | 28 | funding_sources ×3, divisions ×3, offices ×2 |
| `GET budget-planning/dashboard/office` | 36 | funding_sources ×3, divisions ×3, offices ×3 |

Whole-table reads in `BudgetPlanningDashboardService.cs` as of `release/1.8.3`:

* `divisions`: L119 (`GetDashboardAsync`), L751 (`BuildOfficeDivisionsAsync`)
* `funding_sources`: L134, L765, L876
* `offices`: L519, plus by-id reads at L761 and L833, and `GetHostOfficeAsync` at L108

That's only two division sites and three funding-source sites in the service, but the log shows three reads of each per request. So part of the repetition comes from a **collaborator** the dashboard calls during the same request. Step 1 is to find which one.

## Target

Within `BudgetPlanningDashboardService`, `funding_sources` and the office row are each read **once per request**. Use the same pattern as `AllocationService.cs:59-82`: private nullable instance fields filled with `??=`. That's safe because the service is registered **Scoped** (`Program.cs:194`), so the fields last exactly one request.

↩️ *Revised 2026-10-08 after tracing the code: the earlier "parameters, not instance fields" wording was overly cautious. Passing the lists as parameters means reordering both entry points, because the AIP summary is built before the funds are loaded.*

**Not a cache.** Nothing outlives the request. Don't use `IMemoryCache` ([PPDO-44](https://linear.app/ralphoksiprojects/issue/PPDO-44/cache-config-reference-data-imemorycache-is-registered-but-used-only) explains why that's deferred).

## Why it's small

Each read costs \~1 ms locally and a few ms in production. The gain is \~5 fewer round trips on the two dashboard calls, not a visible speed-up. Do it when you're already in this file, or as a hand-coding exercise. It's not urgent.

## Acceptance

- [ ] Before/after `Executed DbCommand` count on both endpoints. Expect **−2 per endpoint**: one `funding_sources` read and one `offices` by-id read. The rest of the repetition is `AllocationService`'s own per-request copies (a separate service instance's fields), which is **out of scope** here. Sharing them would mean exposing config lists on `IAllocationService`. Use the same method as the [PPDO-44](https://linear.app/ralphoksiprojects/issue/PPDO-44/cache-config-reference-data-imemorycache-is-registered-but-used-only) comment (call each endpoint twice; count the second).
- [ ] Dashboard JSON for `admin` and for an office user is byte-identical before and after
- [ ] Existing `BudgetPlanningDashboardService` tests pass; signature changes are reflected in the tests

## Manual-coding candidate

Good fit for Ralph to hand-code. Small blast radius, the pattern to copy is in `AllocationService`, it's testable with `dotnet test`, and it's reversible. Guidance level 1: this spec plus the sibling.

## Out of scope

* Cross-request caching ([PPDO-44](https://linear.app/ralphoksiprojects/issue/PPDO-44/cache-config-reference-data-imemorycache-is-registered-but-used-only))
* Any change to what the dashboard returns
* Office/division scope logic. Do not move scope decisions; only change where the lists are loaded.
