---
id: PPDO-119
title: 'V18-71.3 — Endpoints accept rowVersion and map the 409 (Deploy 1)'
status: Done
project: PPDO Portal
labels: 
milestone: v1.8.2 — Dashboard Redesign, Caching & Remaining Optimizations
created: 2026-09-22
completed: 2026-09-22
parent: PPDO-116
blocked_by: PPDO-118
related: PPDO-70, PPDO-121, PPDO-191
url: https://linear.app/ralphoksiprojects/issue/PPDO-119
uuid: efb5abb3-788d-4a7b-896e-bf862378dd32
---

# PPDO-119 — V18-71.3 — Endpoints accept rowVersion and map the 409 (Deploy 1)

Child 3 of [PPDO-116](<https://linear.app/ralphoksiprojects/issue/PPDO-116>). **This is Deploy 1** of the three-stage rollout. Blocked by [PPDO-118](<https://linear.app/ralphoksiprojects/issue/PPDO-118>).

The field is **optional** at this stage, so nothing breaks and nothing is protected yet. That is deliberate — see §7.

## 1. Context docs to read

Read `CLAUDE.md`, `PROJECT_DOCUMENTATION_NET_AZURE.md`, and `PPDO_PROJECT_CONTEXT.md`.

Read `docs/v1.8/AIP_Concurrent_Edit_Spec.md` **§4 and §8 FULLY** — authoritative. §4's check-ordering rule is the part that is easy to get wrong and impossible to see in review if you get it wrong.

## 2. Files to read before writing code

* `backend/PPDO.Functions/Functions/AipFunctions.cs` — the activity write handlers (`PUT .../activities/{activityId}`, `.../details`, `.../is-creation`).
* `backend/PPDO.Functions/Functions/AipExpenditureFunctions.cs` — `PUT`/`DELETE /budget-planning/aip/expenditures/{id}`, and the `POST .../activities/{activityId}/expenditures` create that takes **no** version.
* `backend/PPDO.Functions/Functions/ConfigHttp.cs` — ⚠️ **the pattern that matters.** `AuthorizeWriteAsync` runs the permission predicate *before* the reviewer guard, deliberately, so the denial never becomes an oracle. Your version check goes after **both**, for the same reason.
* `backend/PPDO.Functions/Functions/ConfigHttp.cs` → `FromResultAsync` — `ServiceErrorCode.Conflict` already maps to 409. You should not need to touch the mapping.
* `backend/PPDO.Tests/Functions/AipConsolidatedFunctionsTests.cs` + `FunctionHttpHarness.cs` — how handler tests are written here.

## 3. Current → target

**Current:** AIP write handlers take a DTO with no version field. Any caller's save is applied.

**Target:** each handler accepts an optional `rowVersion` (base64), passes it to the service, and returns 409 with the conflict payload on mismatch. A **missing** version logs a warning and proceeds — temporarily.

> State the current behaviour back in one sentence as output before you start.

## 4. Working branch + PR target

Integration branch `release/1.8.0`. Create `feature/v1.8.0-ppdo-<id>-aip-conflict-endpoints` off it; PR against `release/1.8.0` **(NOT** `main`**, NOT** `uat`**)**.

## 5. TDD

**TDD: extend the Functions tests with failing cases first**, especially the ordering case in step 3.

## 6. Implementation steps

1. **DTOs** — add `string? RowVersion` (base64) to the AIP activity and expenditure write DTOs. Decode to `byte[]`; a malformed base64 string is a **400**, not a silent null.
   *Verify:* `dotnet build`, plus a test posting `"rowVersion": "!!!not-base64"` → 400.
2. **Handlers** — thread it through to the service. `ConfigHttp.FromResultAsync` already maps `Conflict` → 409, so the payload should surface with no mapping work; confirm rather than assume.
   *Verify:* `curl` a stale version against a local activity → 409 with the `{ data.conflict, error, message }` shape from spec §4.
3. **⚠️ Check order — the one to get right.** Per handler: JWT → 401, permission + office scope → 403 *(including* [PPDO-70](https://linear.app/ralphoksiprojects/issue/PPDO-70/v18-52-locking-at-submittedtoppdo-unlock-on-return)*'s submitted-record lock)*, row exists → 404, **version → 409**, validation → 400.
   *Verify:* a test where the caller **both** lacks permission **and** sends a stale version, asserting **403**. Assert the exact status, not "not 200" — this is the oracle guard, and it fails silently if reversed.
4. **Missing version → warn and proceed.** Temporary, for Deploy 1 only. `LogWarning` with the function name so the count is visible in Application Insights. Child 5 removes this path.
   *Verify:* a test that omits the field → 200 + the warning fires.
5. **Create path untouched.** `POST .../expenditures` sends no version; a row that does not exist cannot conflict.
   *Verify:* an existing create test still passes unchanged.
6. **Full suite.**
   *Verify:* `dotnet test` green.

## 7. Risks, rollback, sign-off

🛑 **Sign-off before merge** — this touches authorization ordering on office-scoped write endpoints. `CLAUDE.md` lists both as sign-off triggers, and a missing check here is not caught by the compiler.

| Risk | Rollback |
| -- | -- |
| Version check placed **before** the permission check | The 409 becomes an oracle: an unauthorized caller can probe whether a record exists and recently changed. Step 3's test is the guard. Revert the commit; no migration has run. |
| Deploy 1 ships and is mistaken for "done" | **It is not.** The field is optional here, so nothing is protected. Do not close [PPDO-116](<https://linear.app/ralphoksiprojects/issue/PPDO-116>) at this stage. |
| Warning path floods App Insights | Expected until Deploy 2 lands. It is the signal child 5 waits on — do not silence it. |

## 8. Out of scope — do NOT

* **Do NOT** make `rowVersion` required. That is child 5, and only after the Deploy 1 warning has stopped appearing in production.
* **Do NOT** treat a missing version as a conflict — it must pass at this stage.
* **Do NOT** touch the frontend. Child 4.
* **Do NOT** add the field to program/project/office routes.

## 9. Commit message

```
feat(aip): accept rowVersion on AIP writes and return 409 on conflict (V18-71)
```
