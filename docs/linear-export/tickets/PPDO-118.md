---
id: PPDO-118
title: 'V18-71.2 — Service layer: detect the conflict and build the 409 payload'
status: Done
project: PPDO Portal
labels: 
milestone: v1.8.2 — Dashboard Redesign, Caching & Remaining Optimizations
created: 2026-09-22
completed: 2026-09-23
parent: PPDO-116
blocked_by: PPDO-117
related: PPDO-115
url: https://linear.app/ralphoksiprojects/issue/PPDO-118
uuid: 8e0a75da-0b3f-47fc-8323-05618d93ab03
---

# PPDO-118 — V18-71.2 — Service layer: detect the conflict and build the 409 payload

Child 2 of [PPDO-116](<https://linear.app/ralphoksiprojects/issue/PPDO-116>). **This ticket carries the real logic and is where the tests live.** Blocked by [PPDO-117](<https://linear.app/ralphoksiprojects/issue/PPDO-117>).

## 1. Context docs to read

Read `CLAUDE.md`, `PROJECT_DOCUMENTATION_NET_AZURE.md`, and `PPDO_PROJECT_CONTEXT.md`.

Read `docs/v1.8/AIP_Concurrent_Edit_Spec.md` **§3, §4 and §11 FULLY** — authoritative for this ticket. Read `docs/TEST_CONVENTIONS.md`.

## 2. Files to read before writing code

* `backend/PPDO.Application/Services/AipService.cs` — the activity write paths. Note how `ServiceResult` failures are returned, and where `_audit.LogAsync` fires.
* `backend/PPDO.Application/Services/AipExpenditureService.cs` — the expenditure write paths. ⚠️ **Read** `UpdateAsync` **closely**: it sets `line.UpdatedAt = DateTime.UtcNow` **unconditionally** before any item handling. Spec §4 explains why that one line is what makes procurement items safe. **Do not make it conditional.**
* `backend/PPDO.Application/Common/ServiceResult.cs` — `Conflict()` already exists and maps to 409.
* `backend/PPDO.Application/Services/UserService.cs` — `ResetPasswordAsync` (\~line 367) is a worked example of a `Conflict` returned for a state reason, from [PPDO-115](<https://linear.app/ralphoksiprojects/issue/PPDO-115>).
* `backend/PPDO.Tests/Application/AipServiceTests.cs` and `AipExpenditureProcurementTests.cs` — where the tests go, and the existing fixture shapes.
* `backend/PPDO.Application/Common/CallerContext.cs` — the authenticated caller's id, for stamping `UpdatedById`.

## 3. Current → target

**Current:** an AIP write loads the row, mutates it, and calls `SaveChangesAsync`. A second writer's save overwrites the first with no detection. `DbUpdateConcurrencyException` is never thrown (no token) and never caught.

**Target:** with a stale token the save throws `DbUpdateConcurrencyException`, the service catches it and returns `ServiceResult.Conflict` carrying the changer's name, the change time, and the current values. Every AIP write stamps `UpdatedAt`/`UpdatedById`.

> State the current behaviour back in one sentence as output before you start.

## 4. Working branch + PR target

Integration branch `release/1.8.0` while v1.8.0 soaks. Create `feature/v1.8.0-ppdo-<id>-aip-conflict-service` off it; PR against `release/1.8.0` **(NOT** `main`**, NOT** `uat`**)**.

## 5. TDD

**TDD: extend** `backend/PPDO.Tests/Application/AipServiceTests.cs` **with failing tests first, then implement.**

⚠️ **Red-test the guard.** Every existing AIP write test saves against an unchanged row, so a conflict path that never fires would pass vacuously — exactly the trap [PPDO-115](<https://linear.app/ralphoksiprojects/issue/PPDO-115>)'s inactive-target test hit. Remove the catch and watch your new test fail before you trust it.

## 6. Implementation steps

1. **Conflict DTO** — `AipConflictDto { ChangedByName, ChangedAtUtc, Current }`, matching the §4 payload. `Current` carries the fresh values **and the fresh** `rowVersion`, so an Overwrite is one round trip rather than a reload-then-save.
   *Verify:* `dotnet build`.
2. **Stamp on write.** Every AIP activity and expenditure write sets `UpdatedAt = DateTime.UtcNow` and `UpdatedById` from `CallerContext`. ⚠️ **Every path, not just the inline editor** — `details`, `is-creation`, expenditure create and update.
   *Verify:* a test per write path asserting `UpdatedById` is set. Prefer one sweep test over the endpoint list, in the shape of `backend/PPDO.Tests/Functions/ReviewerWriteGuardCoverageTests.cs`, rather than N copies that will drift.
3. **Catch and translate.** Wrap the `SaveChangesAsync` on those paths: catch `DbUpdateConcurrencyException`, reload the current row, resolve the changer's display name via `UpdatedById`, return `ServiceResult<T>.Conflict` with the DTO.
   *Verify:* `dotnet test --filter AipService`. Then delete the catch and confirm the new test **fails**.
4. **Deleted-row case.** If the reload finds nothing, the row was deleted, not edited → return `NotFound`, not `Conflict` (spec §3).
   *Verify:* a test that deletes the row between load and save.
5. **Log it.** `LogWarning` with `{ActivityId}`, `{AttemptedByUserId}`, `{ChangedByUserId}` — structured, per `CLAUDE.md`. Conflicts are a signal about how the office actually works; if they are frequent the answer may be a UI change, not a better warning.
   *Verify:* the line appears in the local console when you force a conflict.
6. **Full suite.**
   *Verify:* `dotnet test` green.

## 7. Risks, rollback, sign-off

🛑 **Sign-off before merge**: the changed paths are office-scoped writes. Confirm the conflict path cannot leak another office's data — the changer's name is only ever from the **same office** (spec §3), and a cross-office caller already reads the whole record.

| Risk | Rollback |
| -- | -- |
| Catch swallows a **real** save failure and reports it as a conflict | Catch `DbUpdateConcurrencyException` **only** — never `DbUpdateException`, which is its base and covers FK and constraint failures. Getting this wrong turns every constraint violation into a baffling "changed by someone else". |
| The reload inside the catch throws | Guard it; fall back to a conflict with a null changer rather than a 500. A conflict you cannot attribute is still a conflict. |
| Name resolution adds a query to a hot path | It is on the **failure** path only. Do not pre-fetch the changer on every successful save to "save a query" — that is a query on every write to serve the rare case. |

## 8. Out of scope — do NOT

* **Do NOT** touch the endpoints. Child 3.
* **Do NOT** make `line.UpdatedAt = DateTime.UtcNow` conditional in `AipExpenditureService.UpdateAsync`. Spec §4 — it is what keeps procurement items protected.
* **Do NOT** catch `DbUpdateException`. See the risk table.
* **Do NOT** add retry-on-conflict. The user decides, not the server.
* **Do NOT** extend this to WFP, LDIP, programs, projects or offices.

## 9. Commit message

```
feat(aip): detect concurrent edits and return a conflict payload (V18-71)
```

---

# Build notes — 2026-09-22

Shipped across two PRs. [obiken-01/ppdo-portal#368](https://linear.app/ralphoksiprojects/review/feataip-concurrent-edit-detection-and-409-on-aip-writes-ppdo-118-ppdo-1f816739a4f8) (**merged**) carried the shared seam and the activity paths; [obiken-01/ppdo-portal#369](https://linear.app/ralphoksiprojects/review/feataip-extend-the-concurrent-edit-guard-to-expenditures-ppdo-118-10f852d6ba6d) completes the scope with expenditures, their procurement items, and delete.

## Four things that were not obvious

**1.** `ExpectRowVersion` **sets** `OriginalValue`**, not** `CurrentValue`**.** EF builds the UPDATE's `WHERE` from the *original* value, and the one it read moments ago in the same request is by definition current — so the obvious implementation passes every time and protects nothing.

**2.** `ReloadAsync` **was needed because a re-query will not do it.** After a failed save the change tracker still holds the entity with the caller's attempted values, and a fresh query identity-resolves straight back to it. The conflict payload would have shown the user **their own rejected edit, labelled as somebody else's**.

**3. The actor stamp must be read AFTER the reload.** The write paths stamp the current caller before saving, so reading `UpdatedById` earlier names the person being refused as the person who made the change. This surfaced as a failing test, not by inspection.

**4. ⚠️** `ExpectRowVersion` **must be declared immediately before the save that matters.** An intervening `SaveChanges` — and `AuditService` performs its own — calls `AcceptAllChanges`, copying current values over original ones and silently discarding the expectation. Invisible afterwards: the save just succeeds.

## ↩️ One behaviour change beyond the ticket

`AipExpenditureService.DeleteAsync`**'s audit call moved after the save.** It ran first, which was harmless while a delete could not fail — this ticket gives it a way to. Since `AuditService` performs its own `SaveChangesAsync`, the two are not one transaction, so logging first would leave a permanent audit row saying "deleted" for a row that still exists.

## Two behaviours the spec did not anticipate

* **The same account can conflict with itself** — two tabs, or a shared login ([RAL-198](<https://linear.app/ralphoksiprojects/issue/RAL-198>)). "Changed by you" reads as a bug, so that case has its own message.
* **An unresolvable editor still conflicts.** A row untouched since [PPDO-117](https://linear.app/ralphoksiprojects/issue/PPDO-117/v18-711-concurrency-columns-on-aip-activities-and-aip-expenditures) has no `updated_by_id`; a 500 there would turn "someone else edited this" into "the app broke".

## Procurement items — confirmed, and fragile

Protected by the **parent line's** token, exactly as the spec predicted. That holds solely because `line.UpdatedAt = DateTime.UtcNow` is unconditional. ⚠️ **Anyone who makes it conditional as an optimisation reintroduces this ticket's own bug one level down** — now guarded by a red-tested case.

## Verification

**2425 passed, 0 failed** (2381 original baseline + 44 new across the family).

**Four guards red-tested**, not assumed: the conflict catch, the authorization-before-version ordering, the unconditional `UpdatedAt`, and the audit ordering. Each was broken deliberately and each test failed.

⚠️ The conflict is **injected, not provoked** — SQLite has no rowversion so `DbUpdateConcurrencyException` cannot fire in those fixtures ([PPDO-117](<https://linear.app/ralphoksiprojects/issue/PPDO-117>)). The database half was verified against real SQL Server there.

## Not covered

Program, project and office renames — deliberately out of scope per the spec's open follow-ups.
