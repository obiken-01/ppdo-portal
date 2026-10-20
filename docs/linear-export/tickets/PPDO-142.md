---
id: PPDO-142
title: 'Health: stop returning raw exception text from anonymous GET /api/health'
status: Done
project: PPDO Portal
labels: backend, Improvement
milestone: v1.8.2 — Dashboard Redesign, Caching & Remaining Optimizations
created: 2026-09-27
completed: 2026-10-05
parent: (none)
blocked_by: (none)
related: PPDO-141
url: https://linear.app/ralphoksiprojects/issue/PPDO-142
uuid: 1b9dba7f-0190-42e1-81a4-4bd9fa6bc781
---

# PPDO-142 — Health: stop returning raw exception text from anonymous GET /api/health

> **Scheduled into v1.8.2 (Ralph, 2026-10-05), to be done by a Sonnet session.** Re-checked against `origin/release/1.8.2` @ `4f181f71`. The original prompt (it targeted release/1.8.0) was replaced by **Implementation prompt (v1.8.2)** at the bottom.
>
> **Code facts as of 2026-10-05:**
>
> * `HealthFunctions.cs` still injects `AppDbContext` and puts `ex.Message` in `error`. Its XML doc still says "Free tier auto-pauses".
> * ⚠️ **Second bug:** the probe is `ExecuteSqlRawAsync(...).ContinueWith(t => !t.IsFaulted)`. A **faulted** query (the usual DB-down case) becomes `false` without throwing, so the `catch` never runs: no `error` and no log. The new service must `await` the probe directly inside `try/catch`, so the failure is actually caught and logged.
> * **Sibling:** `IHolidayProvider` lives in `PPDO.Application/Services/IHolidayProvider.cs` (the interface is in **Application**, not Domain). `NagerHolidayProvider` is in `PPDO.Infrastructure/Services/`, and the registration is in `Program.cs`. Put `IDatabaseProbe` in the same places.
> * **Consumers checked, none reads** `error`**:**
>   * `login/page.tsx:352` reads only `res.data.database === "ok"`;
>   * `lib/auth-redirect.ts` uses the timeout only;
>   * the `deploy.yml` smoke test checks the HTTP code and `cat`s the body for the log.
>   * Removing the key is safe.
> * `GET /api/external/v1/health` (`ExternalAipFunctions.cs:50`) never touches the DB and returns no error text. It's out of scope; leave it.
> * The impact study is on `main` only: `git show origin/main:docs/PGOM_Database_Sharing_Impact.md`.

## Why

Found in the PGOM database-sharing impact study (`docs/PGOM_Database_Sharing_Impact.md` §4.2, prep for NOM 2026-136 on Oct 6). `GET /api/health` is **public** (no JWT). When the DB check fails, it puts `ex.Message` into the response body as `error`. SqlClient messages can include the server name, the database name, the login name, or network detail. Anyone on the internet can read that during an outage, and the endpoint is about to be named to other PGOM systems as our availability/wake-up probe. It will also come up in the privacy impact assessment.

A second, smaller issue: the exception is **not logged anywhere**. It goes to the caller and not to Application Insights. So we currently leak the detail publicly and still lose it ourselves.

## Current → target

* **Current:** `HealthFunctions.cs` runs `SELECT 1` through an injected `AppDbContext`. On failure the 503 body is `{ status: "degraded", api: "ok", database: "unavailable", error: "<ex.Message>", utc }`.
* **Target:** the 503 body has the same shape **minus** `error`. The exception is logged at `LogError` with `ex` as the first argument and is visible in App Insights. The 200 body is unchanged.

## Design note (layering)

`HealthFunctions` references `PPDO.Infrastructure` (`AppDbContext`) directly, which breaks the CLAUDE.md rule *"Never reference PPDO.Infrastructure from PPDO.Functions."* Logging also belongs in Application services, not handlers. Fix both the same way the rest of the codebase does:

* Domain/Application: `IHealthService.CheckDatabaseAsync(ct)` → returns `bool` (or a small result).
* Infrastructure: the `SELECT 1` probe behind an interface (e.g. `IDatabaseProbe`), registered in `Program.cs`.
* `HealthService` (Application) calls the probe, catches, and does `_logger.LogError(ex, "Health check: database unreachable")`.
* The handler calls the service and shapes the response. Nothing else.

Also drop the stale "Free tier auto-pauses" line from the XML doc (we've been on Basic since 2026-08-12).

## Acceptance

- [ ] With the DB unreachable, `/api/health` returns 503 and the body has no exception text
- [ ] The failure appears in Application Insights (or the local console) as an error with the exception
- [ ] `HealthFunctions` no longer references `AppDbContext`
- [ ] Login-page health indicator behaves as before

## Learning-ticket assessment

✅ **Good manual candidate.** Small blast radius, reversible, `dotnet test`-verifiable through `HealthService`, and it copies the shape of any existing small Application service. Suggested guidance level 1: this ticket plus the sibling service to pattern-match. **Banked as B3** in `docs/LEARNING_TICKET_BANK.md` (level-1 guidance: sibling = `IHolidayProvider` / `NagerHolidayProvider`). If you hand-code it, work from B3. The implementation prompt above is roughly a level-3 step outline, so skip it unless you want to escalate.

## Implementation prompt (v1.8.2)

```
Read CLAUDE.md, then this Linear ticket (PPDO-142) in full, including the "Code facts as of
2026-10-05" block at the top. Context (on main only):
git show origin/main:docs/PGOM_Database_Sharing_Impact.md  (§4.2)

Branch: fix/v1.8.2-ppdo-142-health-no-error-leak off origin/release/1.8.2. PR into release/1.8.2
(NOT main, NOT release/1.8.1).

Read before coding: HealthFunctions.cs, Program.cs (DI), the sibling IHolidayProvider (Application)
+ NagerHolidayProvider (Infrastructure) and how DashboardServiceTests mock it, and
frontend/src/app/(public)/login/page.tsx around the health check.

State in one line each before changing anything: current behaviour (incl. the ContinueWith
swallowing a faulted query), target behaviour.

TDD: HealthServiceTests first, failing, then implement.
1. IDatabaseProbe in PPDO.Application/Services (next to IHolidayProvider); SqlDatabaseProbe in
   PPDO.Infrastructure/Services running SELECT 1 via AppDbContext (plain await, no ContinueWith).
   Register both in Program.cs alongside the holiday provider.
2. IHealthService / HealthService in Application with ILogger<HealthService>:
   await the probe inside try/catch; on exception
   _logger.LogError(ex, "Health check: database unreachable") and return false.
   Don't catch OperationCanceledException from the request's own token — let it propagate.
   Tests (Moq the probe): ok → true; throws → false AND LogError was called with that exception
   (verify the ILogger.Log call with LogLevel.Error and the exception). Red-test it: remove the
   LogError line and watch the test fail.
3. HealthFunctions depends on IHealthService only; no AppDbContext, no PPDO.Infrastructure using.
   The payload drops `error`; everything else stays byte-for-byte: status, api, database, utc,
   200/503, camelCase, Content-Type. Fix the stale "Free tier auto-pauses" doc line (Basic tier
   since 2026-08-12).
4. Verify: `cd backend && dotnet test` green. Then func start and:
   - curl -i http://localhost:7071/api/health → 200 {status:"ok",api:"ok",database:"ok",utc}
   - stop the SQL Server (SQLEXPRESS) service, curl again → 503, body has no `error` key, and
     the func console shows the logged error with the exception
   - start SQL Server again → 200
   - login page: the status dot is red while SQL is down and green after.
   Paste both curl outputs into the PR.

Do NOT add auth to /api/health (it must stay public: login warm-up, deploy smoke test, external
probes), and do NOT change the 200 shape or the status codes. Never log the connection string.
Rollback is a plain revert (no migration).

Commit: fix(health): stop leaking exception text from /api/health and log it instead (PPDO-142)
```
