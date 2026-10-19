---
id: PPDO-43
title: 'Unhandled exceptions reach the host unlogged — add exception + logging-scope middleware'
status: Done
project: PPDO Portal
labels: 
milestone: techdebt
created: 2026-09-03
completed: 2026-09-22
parent: (none)
blocked_by: (none)
related: PPDO-6, PPDO-42, PPDO-44, PPDO-45
url: https://linear.app/ralphoksiprojects/issue/PPDO-43
uuid: 03a4bf03-892c-4aeb-ac56-7414cb98410d
---

# PPDO-43 — Unhandled exceptions reach the host unlogged — add exception + logging-scope middleware

## Problem

208 HTTP-triggered endpoints, and the worker middleware pipeline has **exactly one stage** in it — CORS (`Program.cs:39`). There is no exception stage, so an unhandled failure (SQL timeout, null deref in a mapper, EF concurrency conflict) propagates to the Functions host, which returns a 500 in whatever shape it picks, with **no** `LogError` **and no business context attached**.

Supporting counts:

* **5** `catch (Exception` blocks across all of `PPDO.Application` + `PPDO.Functions` (`PRReportService`, `StockBalanceService`, `PurchaseRequestService`, `HealthFunctions`).
* **0** `BeginScope` calls anywhere in the solution.
* The dominant catch idiom is body-parse safety, duplicated across \~8 Function files, which swallows silently:

```csharp
// ConfigHttp.cs:136
try { return await JsonSerializer.DeserializeAsync<T>(req.Body, Json, cancellationToken); }
catch { return default; }
```

CLAUDE.md's logging rules ("log all caught exceptions at LogError", structured `{PropertyName}` parameters) have almost nothing enforcing them at the boundary.

**Note this is not a missing pipeline** — the pipeline exists and runs on every invocation. It just has one thing in it. The fix is a second `worker.Use` block, not new plumbing.

## Why the existing CORS block is the template

It already demonstrates both mechanics needed: writing a response from middleware (`http.Response.StatusCode = StatusCodes.Status204NoContent`, `Program.cs:65`) and short-circuiting without calling `next`.

## Scope

1. **Exception middleware** — wrap `await next(functionCtx)`, log structured (`LogError` with function name + caller id), return a consistent JSON error body with no internals.
2. **Logging scope** — push `UserId` + route into an `ILogger.BeginScope` so every log line under a request carries them. App Insights already supplies `operation_Id` correlation via `AddApplicationInsightsTelemetryWorkerService` (`Program.cs:88-89`); what is missing is *who* and *what*, not *which request*.

### Two things to get right

**Registration order.** Middleware runs outermost-first in registration order, so the exception stage goes **before** the CORS block. That ordering is also what keeps CORS headers on a 500 — the CORS block sets them on the way in, before `await next`, so they are already on the response when an exception unwinds back out. Register it after CORS and a failed request returns without CORS headers, which the browser surfaces as an opaque network error instead of a 500. Nasty to debug later.

**Guard** `Response.HasStarted`**.** The three Excel exports (`budget-planning/wfp/report/export`, `budget-planning/ppmp/report/export`, `purchase-requests/{id}/export`) stream file bytes. A failure partway through means the response has already started and the status code cannot be rewritten — log it and let it drop rather than throwing a second exception inside the handler.

## Ride-alongs (same PR, both trivial)

* **Rename** `JwtMiddleware` **→** `JwtValidator` (and `IJwtMiddleware`). Despite the name it is **not** pipeline middleware — it is a scoped DI service with no `Invoke(FunctionContext, FunctionExecutionDelegate)`, invoked by hand as `await _jwt.ValidateAsync(req)` in each handler. Its own doc comment says "Register as scoped in Program.cs." The name has already misled a reader into believing exception handling was covered.
* **Consolidate** `ReadBodyAsync<T>`**.** It is copy-pasted into \~8 Function files even though `ConfigHttp.ReadBodyAsync` exists. One copy also gives the parse-failure path a single place to log from.

## Out of scope

Consolidating the auth call sites. There are three entry styles across the 208 endpoints — `ConfigHttp.AuthorizeAsync` (79), `ConfigHttp.AuthorizeWriteAsync` (50, the one carrying the [RAL-256](https://linear.app/ralphoksiprojects/issue/PPDO-6/denyreviewerwrite-guard-the-first-subtractive-permission) reviewer guard), and inline `_jwt.ValidateAsync` (72) — with the remainder being the intentionally public endpoints. Worth unifying eventually, but it means touching auth on 72 endpoints while v1.8.0 is mid-flight, and `PermissionMatrixTests` pins behaviour without pinning call-site presence. **Post-v1.8.0.**

## Test focus

A handler that throws should produce: a 500 with the standard error envelope, CORS headers intact, one `LogError` carrying function name and caller id, and no stack trace in the body. Add a case for the already-started-response path.

## Origin

Found auditing the codebase against a Medium article on .NET Web API mistakes (2026-09-03) — its observability and "errors exposing internal details" points. See [PPDO-42](https://linear.app/ralphoksiprojects/issue/PPDO-42/generic-repository-is-guid-only-so-20-call-sites-load-a-whole-table-to) for the query-pattern finding from the same pass.

## Workflow

Backlog in `techdebt`. On pickup, move to the active version milestone and branch off its `release/X.Y.Z` per the standing convention.

---

# Build notes — 2026-09-22

PR [obiken-01/ppdo-portal#364](https://linear.app/ralphoksiprojects/review/featfunctions-worker-exception-logging-scope-middleware-ppdo-43-70fc1ca1b084) → `release/1.8.0`. Three commits: the middleware, then the two ride-alongs separately so a 61-file mechanical rename does not bury the one file that needs real review.

## Two corrections to this ticket

**1. ⚠️ The ReadBodyAsync consolidation was not trivial.** The ten copies were **not interchangeable**:

| Config | Files |
| -- | -- |
| camelCase only | Delivery, Distribution, Item, PurchaseRequest, ResourceLink |
| camelCase + case-insensitive | Auth, StockBalance, User |
| camelCase + case-insensitive + `JsonStringEnumConverter` | Announcement |
| camelCase + `JsonStringEnumConverter` | Dashboard |

Doing what this ticket implies — pointing them all at `ConfigHttp.Json` — would have stopped `AnnouncementStatus` and `CalendarEventStatus` deserializing from strings. And because the parse failure is swallowed and returns `default`, it would **not** have surfaced as a parse error: the handler would see a null body and answer **400 on a well-formed request**. Silent, and nothing in the 2,381-test suite covers it.

Shipped instead as `ReadBodyAsync(req, ct, options = null)` with every call site passing its own file's `_jsonOptions`. Behaviour identical, duplication gone, and the differences are now visible in one place instead of invisible across eleven.

**2. The "single place to log from" cannot be delivered where this ticket puts it.** `CLAUDE.md` forbids injecting `ILogger` into Function handlers, and `ConfigHttp` is a static helper with nothing to log through. The exception middleware is where that need is actually met — but only once the bare `catch` is narrowed (below).

## Deferred out of this PR

**Narrow** `ConfigHttp.ReadBodyAsync`**'s bare** `catch` **to** `JsonException`**.** The better end state: unexpected failures (IO, cancellation) would reach the new exception stage and be logged, instead of masquerading as a malformed body and returning 400. Deliberately not done here because it changes the failure status of **every write endpoint in the app**, which is not a change to land while v1.8.0 soaks in UAT. Worth its own small ticket once v1.8.0 reaches `main` — and it is what closes correction 2 above.

## Confirmed as still accurate

Everything else in this ticket held up against the code: one `worker.Use` stage, 240 HTTP triggers (all HTTP — no timer/queue), zero `BeginScope` calls, the CORS block as the working template, and both ride-alongs real.

## One design note worth keeping

`InvocationLogScope` is a class rather than a `Dictionary`, and that is the crux of the logging half. The scope must open **before** `await next` to cover the whole invocation, but `CallerContext.UserId` is not set until partway through the handler when the JWT check succeeds. A dictionary captures at scope-open, so it would record `null` on every request forever — present, and carrying nothing. Providers enumerate scope state at **log time**, so holding the `CallerContext` and reading `UserId` during enumeration gives each line the value as it stood when that line was written.

## Verification

2,393 tests passing (2,381 baseline + 12 new); the `HasStarted` guard is red-tested.

Also verified **live** via `func start` against a temporary throwing endpoint, because the registration order and the `GetHttpContext()` wiring are exactly what unit tests cannot reach: 500 with the `{ data, error, message }` envelope, **CORS headers present on the 500** (the ordering claim, proven rather than reasoned about), the exception text and stack trace in the host log and absent from the response body.

⚠️ **Not verified:** that scope fields arrive in Application Insights as custom dimensions. The Core Tools console logger does not render scopes and there is no AI connection string locally. Worth one glance in UAT after this deploys — the scope's *contents* are unit-tested, but its *delivery* to AI is not.
