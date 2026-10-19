---
id: PPDO-42
title: 'Generic repository is Guid-only, so 20 call sites load a whole table to find one row by int id'
status: Done
project: PPDO Portal
labels: 
milestone: v1.8.3 — Concurrent-edit guard: turn it on
created: 2026-09-03
completed: 2026-10-07
parent: (none)
blocked_by: (none)
related: PPDO-43, PPDO-45
url: https://linear.app/ralphoksiprojects/issue/PPDO-42
uuid: 7f44f692-4e7d-4976-bed2-560eee82338a
---

# PPDO-42 — Generic repository is Guid-only, so 20 call sites load a whole table to find one row by int id

## Problem

`Repository<T>.GetByIdAsync` is **Guid-keyed**:

```csharp
// PPDO.Infrastructure/Repositories/Repository.cs:29
public async Task<T?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    => await _context.Set<T>().FindAsync(new object?[] { id }, cancellationToken);
```

Every int-keyed entity — the whole config layer plus most of Budget Planning — therefore has **no by-id path on the generic repository**, and services fall back to loading the entire table and filtering in memory:

```csharp
Account? a = (await _repo.GetAllAsync(cancellationToken)).FirstOrDefault(x => x.Id == id);
```

This is the exact anti-pattern CLAUDE.md bans ("Query at the database, not in memory") and `docs/PERFORMANCE_GUIDELINES.md` was written for. It is not carelessness — the base class gives no alternative.

**20 confirmed sites across 12 services** (`AccountService` ×2, `OfficeService` ×2, `FundingSourceService` ×2, `EsreCodeService` ×2, `ClimateChangeTypologyService` ×2, `WfpCeilingService` ×4, `PriceIndexService` ×2, `LdipService`, `PpmpReportService`, `WfpExpenditureService` ×2). For context there are ~145 `GetAllAsync` references in `PPDO.Application/Services` overall.

## The one that is a live performance bug

```csharp
// PriceIndexService.cs:92  (GetById)
// PriceIndexService.cs:184 (Delete)
PriceIndexItem? p = (await _repo.GetAllAsync(cancellationToken)).FirstOrDefault(x => x.Id == id);
```

The price index is the table we deliberately paginate because it is large. The **v1.4.6 milestone already recorded that it holds ~6,400 rows in this environment**, after the 2026-07-16 audit under-rated it as "tens to a few hundred rows". Read-one and delete-one both materialise all 6,400.

The rest (divisions, offices, accounts, funding sources) are small tables — pattern debt, not a live fire. Fix them because the pattern spreads by copy-paste, not because they are slow today.

## Also in scope — same root cause, different shape

```csharp
// AipService.cs:69-73 — fiscalYear and status are query parameters, filtered in memory
IEnumerable<AipRecord> q = await _aipRepo.GetAllAsync(ct);
if (fiscalYear.HasValue) q = q.Where(r => r.FiscalYear == fiscalYear.Value);
if (!string.IsNullOrWhiteSpace(status)) q = q.Where(r => r.Status.Equals(...));
```

Should be a `WHERE` clause. Cheap today (one row per AIP record); wrong pattern. Note the rest of that method is already careful — office counts and uploader names are both scoped to the returned ids.

## The fix has a sibling already in the tree

`GetByIntIdAsync` **already exists**, hand-written per repo — `WfpExpenditureRepository.cs:14` and `AipRepository`. So this is not "introduce int-key lookups", it is "promote an established pattern into the generic base and retire the fallbacks".

1. Add int-key support to `Repository<T>` / `IRepository<T>` (mirroring the existing `GetByIntIdAsync` shape).
2. Replace the 20 `GetAllAsync().FirstOrDefault()` sites.
3. Push the `AipService.GetAllAsync` filters into the repository query.

## Out of scope

* Any change to the Guid-keyed `GetByIdAsync` signature or its existing callers.
* The remaining ~125 `GetAllAsync` calls that legitimately want the whole (small) table — config dropdown lists and similar.
* Pagination. This was checked separately and there is nothing to add: the grid-shaped endpoints (PRs, deliveries, items, price index, audit log) are already paginated, and WFP expenditures / distributions are reached through activity- or item-scoped endpoints, not flat lists.

## Test focus

Existing service tests should pass unchanged — this is a query-shape change with identical semantics. Add a repository-level test per key type. Locally, `logging.AddFilter("Microsoft.EntityFrameworkCore.Database.Command", …)` (already wired in `Program.cs` for blank-App-Insights runs) lets you count round trips before and after.

## Origin

Found auditing the codebase against a Medium article on .NET Web API mistakes (2026-09-03). Most of its ten points were already closed here — zero sync-over-async, `CancellationToken` plumbed throughout, thin handlers, pagination in place — and its "parallelise independent calls" advice is actively wrong for us (shared `DbContext`). This is the finding that survived verification.

## Workflow

Backlog in `techdebt`. On pickup, move to the active version milestone and branch off its `release/X.Y.Z` per the standing convention.

**Manual-implementation candidate** — small blast radius, a sibling to pattern-match, `dotnet test` closes the loop without running the app, fully reversible.
