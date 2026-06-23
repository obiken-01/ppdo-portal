# Performance & Scalability Guidelines

Lessons distilled from the v1.1.0 production performance audit (2026-06-22). Each rule below
exists because we hit (or were about to hit) the problem in real code. Read this before adding a
new query, endpoint, list page, or load state. `CLAUDE.md` carries the short version; this doc is
the "why" and the worked examples.

> The golden rule: **let the database do the filtering, counting, and limiting — not C# memory, and
> not the browser.** Today's tables are small, so the cost is invisible; the audit log and the
> AIP/WFP activity tables grow without bound, so the same code gets linearly slower forever.

---

## 1. Query at the database, not in memory

**Anti-pattern (do NOT do this):**

```csharp
// Loads the ENTIRE table over the wire, then filters in C#.
var all = await _repo.GetAllAsync(ct);          // SELECT * FROM big_table
var mine = all.Where(x => x.OfficeId == id);    // filtered in process memory
```

`GetAllAsync()` is `ToListAsync()` on the whole table. Filtering, counting, and joining after it all
happen in the Function's memory. The generic `Repository<T>.Query()` (`IQueryable`) exists precisely
so you don't have to — but historically **no Application service used it**, so every list/detail
request scanned a full table.

**Do this instead** — add a scoped method to the *feature* repository and let SQL filter:

```csharp
// In a feature repository (e.g. AipOfficeRepository : Repository<AipOffice>)
public async Task<IReadOnlyList<AipOffice>> GetByAipRecordIdAsync(int aipRecordId, CancellationToken ct)
    => await _context.AipOffices
        .Where(o => o.AipRecordId == aipRecordId)   // WHERE in SQL
        .ToListAsync(ct);
```

**Reference implementations already in the repo** — copy their shape:
`PurchaseRequestRepository.GetByDivisionAsync`, `CalendarEventRepository.GetByDateRangeAsync`,
`ItemMasterRepository`, `DeliveryRepository`, `UserRepository`.

- Counts → `CountAsync(predicate)`, never `(await GetAllAsync()).Count(...)`.
- Existence / uniqueness checks → `AnyAsync(x => x.Field == value)`, never `GetAllAsync().Any(...)`.
  (Applies to account-number, username, and email uniqueness checks.)
- Single record by id → a `WHERE Id = @id` query, never `(await GetAllAsync()).FirstOrDefault(r => r.Id == id)`.
  Note the generic `Repository<T>.GetByIdAsync` is **Guid-keyed**; entities with `int` keys (AIP/WFP)
  need a feature-repo by-id method.
- Name/label lookups → fetch only the rows you need with `WHERE Id IN (...)`, not a dictionary built
  from the whole `users` table.

> *Incident:* `AipService.GetByIdAsync` loaded all five AIP tables in full and filtered in memory;
> `WfpService` had 13 such calls; the dashboard recent-activity feed loaded the **entire** `audit_log`
> plus all users to show 10 rows. Tracked in RAL-92 (audit) and RAL-93 (AIP/WFP).

---

## 2. Never run concurrent queries on one DbContext

`DbContext` is **not thread-safe**. Each HTTP request gets one context for the whole request scope.
Kicking off two queries on it concurrently throws
`InvalidOperationException: A second operation was started on this context instance…`.

```csharp
// ❌ BROKEN — two queries race on the same DbContext
var prsTask   = _prs.GetAllAsync(ct);
var itemsTask = _items.GetAllAsync(ct);
await Task.WhenAll(prsTask, itemsTask);

// ✅ Sequential awaits
var prs   = await _prs.GetAllAsync(ct);
var items = await _items.GetAllAsync(ct);
```

If you genuinely need parallel I/O, each branch must use its **own** context (a separate DI scope /
`IDbContextFactory`) — not the shared injected one.

> *Incident:* `DashboardService.GetStatsAsync` used `Task.WhenAll` on two repo calls and returned a
> 500 in production. Fixed in commit `64f2f6f` (sequential awaits).

---

## 3. Return only the fields the consumer needs

A DTO that mirrors the entity ships every column — including heavy free-text fields the caller never
reads. For list/grid views, build a **slim DTO** with just the columns that view uses.

> *Incident:* `GET /api/budget-planning/aip/{id}` returned the full hierarchy with an 18-field
> activity DTO (including `ExpectedOutputs`, climate-change codes, dates). The WFP grid only needs
> ref code, name, budget totals, and funding source — but the response was **~1.2 MB / 6.7 s** and
> tanked the WFP page's LCP. Fixed by adding a slim summary endpoint (RAL-89), leaving the full-detail
> endpoint for the one screen that actually needs it.

Rule of thumb: if a list/grid endpoint's DTO carries a `string?` free-text column the grid doesn't
render, it probably doesn't belong in that DTO.

---

## 4. Paginate large lists on the server

`DataTable` paginates **client-side** (`pageSize` 25) — but that only controls what's *displayed*. If
the API returns every row, the browser still downloads the entire table and pages it locally. Fine for
16 offices or 143 accounts; not fine for an item ledger, PR register, or AIP activity list after a year
of data.

For endpoints backing tables that grow over time, return a page from the server: `Skip`/`Take` + a
total count, and have the frontend request pages. Small, bounded config tables can stay
fetch-all + client-page.

---

## 5. Frontend: fetch shared state once

Every component that independently calls `GET /api/auth/me` issues its own network round-trip for the
same value. A page composed of several such components fires N identical requests.

- The current user (`/auth/me`) is shared state — fetch it **once** via a context/provider
  (`useCurrentUser()`) mounted in the portal layout, and read from context elsewhere.
- The same principle applies to any "fetched once, read everywhere" value (permission flags, config
  lists used across a page).

> *Incident:* the WFP page fired `/auth/me` **four times** per load (~1.85 s wasted). Tracked in RAL-90.

---

## 6. Frontend: loading states must preserve layout (CLS)

A loading state that occupies a different amount of vertical space than the loaded content causes a
**layout shift** when the real content arrives (bad Cumulative Layout Shift score, jarring UX).

- Don't gate a whole page on a tiny centered spinner and then drop in a full table — render the page
  shell immediately and show a **skeleton that matches the final structure** (same header, same row
  height, N placeholder rows).
- Give images explicit `width`/`height` (or use `next/image`) so they reserve their space before they
  load.

> *Incident:* `/config/accounts` scored CLS 0.082 because a small auth-gate spinner was replaced by a
> 143-row table. Fixed in commit `65f3c93` — `DataTable`'s loading state now renders header + skeleton
> rows, and the page shell renders without waiting on the auth check.

---

## 7. Frontend: optimize images

Serve images through `next/image` (auto WebP/AVIF, responsive sizes, lazy-load, explicit dimensions)
rather than raw `<img>` to large PNGs. Reserves layout space (helps §6) and cuts bytes on the wire.

> *Incident:* the login page shipped ~184 KB of raw PNG seal/logo. Tracked in RAL-91.

---

## Quick checklist for any new feature

Backend:
- [ ] No `GetAllAsync()` followed by in-memory `.Where`/`.Count`/`.Any`/`.FirstOrDefault(id)` — push it to SQL via a feature-repo method.
- [ ] Counts use `CountAsync`; existence/uniqueness uses `AnyAsync`.
- [ ] No `Task.WhenAll` over queries sharing one `DbContext`.
- [ ] List/grid endpoints return slim DTOs — no unused heavy columns.
- [ ] Endpoints backing growing tables paginate server-side (`Skip`/`Take` + total).

Frontend:
- [ ] Shared state (current user, etc.) fetched once via context, not per component.
- [ ] Loading state matches the loaded layout (skeleton, not a height-changing spinner).
- [ ] Images via `next/image` with explicit dimensions.
