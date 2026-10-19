---
id: PPDO-117
title: 'V18-71.1 — Concurrency columns on aip_activities and aip_expenditures'
status: Done
project: PPDO Portal
labels: 
milestone: v1.8.2 — Dashboard Redesign, Caching & Remaining Optimizations
created: 2026-09-22
completed: 2026-09-22
parent: PPDO-116
blocked_by: (none)
related: PPDO-115, PPDO-191
url: https://linear.app/ralphoksiprojects/issue/PPDO-117
uuid: 7ac3863a-50d2-47cd-b417-5ca5979a7d1a
---

# PPDO-117 — V18-71.1 — Concurrency columns on aip_activities and aip_expenditures

Child 1 of [PPDO-116](<https://linear.app/ralphoksiprojects/issue/PPDO-116>). Schema only — no behaviour change, nothing detects a conflict yet.

✅ **Unblocked 2026-09-22.** Decision 🅐 confirmed — Ralph: *"go with soft warning, optimistic concurrency"*. 🅑/🅒/🅓 adopted with it. This ticket is ready to start.

> ⚠️ **"Soft warning" does not mean the save goes through.** It describes the *locking model* — nothing reserved, no lease, the user may choose to overwrite. The conflicting save is still **rejected** with a 409. Nothing in this ticket changes, but read it that way when the later children land.

## 1. Context docs to read

Read `CLAUDE.md`, `PROJECT_DOCUMENTATION_NET_AZURE.md`, and `PPDO_PROJECT_CONTEXT.md`.

Read `docs/v1.8/AIP_Concurrent_Edit_Spec.md` **§5 FULLY** — it is the authoritative spec for this ticket. Read `docs/NAMING_CONVENTIONS.md` before naming a single column: `aip_activities` is a **legacy PascalCase** table and `aip_expenditures` is **snake_case**. They do not get matching column names, and that is correct.

## 2. Files to read before writing code

* `backend/PPDO.Domain/Entities/AipActivity.cs` — the target entity. ⚠️ It has **no update tracking at all** today: no `UpdatedAt`, no `UpdatedBy`, not even `CreatedAt`.
* `backend/PPDO.Domain/Entities/AipExpenditure.cs` — already has `CreatedAt` and `UpdatedAt`. The asymmetry with the above is pre-existing.
* `backend/PPDO.Infrastructure/Data/Configurations/AipActivityConfiguration.cs` — the mapping to extend.
* `backend/PPDO.Infrastructure/Data/Configurations/AipExpenditureConfiguration.cs` — same, and it shows the snake_case `.HasColumnName()` pattern.
* `backend/PPDO.Infrastructure/Data/Configurations/AccountConfiguration.cs` — the reference for snake_case mapping (named in `CLAUDE.md`).
* `backend/PPDO.Infrastructure/Data/Migrations/` — for the naming convention and a recent example.

## 3. Current → target

**Current:** no concurrency token exists anywhere in the solution — zero `IsRowVersion()` / `IsConcurrencyToken()` calls. `AipActivity` carries no update-tracking columns.

**Target:** both tables carry a `rowversion` EF treats as a concurrency token, and activities carry `UpdatedAt`/`UpdatedById`. Nothing reads them yet.

> State the current behaviour back in one sentence as output before you start. An implementer who can do that has actually read the file.

## 4. Working branch + PR target

While v1.8.0 soaks in UAT, the integration branch is `release/1.8.0`. Create `feature/v1.8.0-ppdo-<id>-aip-concurrency-columns` off it and open the PR against `release/1.8.0` **(NOT** `main`**, NOT** `uat`**)**.

⚠️ `uat` is a deploy sink — nothing merges out of it. If v1.8.0 has reached `main` by pickup time, use the then-active `release/X.Y.Z` instead.

## 5. Implementation steps

1. `AipActivity` — add `RowVersion` (`byte[]`), `UpdatedAt` (`DateTime?`), `UpdatedById` (`Guid?`) + the `User?` navigation.
   *Verify:* `dotnet build`.
2. `AipExpenditure` — add `RowVersion` (`byte[]`) and `UpdatedById` (`Guid?`). **Do not touch its existing** `CreatedAt`**/**`UpdatedAt`**.**
   *Verify:* `dotnet build`.
3. **EF configuration.** `AipActivityConfiguration`: `.Property(x => x.RowVersion).IsRowVersion()`, PascalCase column names (`RowVersion`, `UpdatedAt`, `UpdatedById`) matching that legacy table. `AipExpenditureConfiguration`: same token, snake_case (`row_version`, `updated_by_id`). FK on both `UpdatedById` → `Users.Id`, `DeleteBehavior.Restrict` — a deleted user must not cascade away AIP rows.
   *Verify:* `dotnet ef migrations add AddAipConcurrencyTokens --project PPDO.Infrastructure --startup-project PPDO.Functions` and **read the generated SQL** — confirm `rowversion` (not `varbinary`), no unintended drops, no table rebuild.
4. **⚠️ MIGRATION —** `AddAipConcurrencyTokens`**.** No backfill: SQL Server populates `rowversion` for existing rows automatically, and `UpdatedAt`/`UpdatedById` stay null until a row is next edited. The UI reads null as "not edited since this shipped".
   *Verify:* `dotnet ef database update` against **local** `PPDOPortalDev`, then `SELECT TOP 5 Id, RowVersion, UpdatedAt FROM aip_activities` and confirm `RowVersion` is populated and non-identical across rows.
5. **Regression sweep.** Nothing should behave differently yet.
   *Verify:* `dotnet test` — full suite green, count unchanged from baseline.

## 6. Risks, rollback, sign-off

🛑 **Sign-off required before running the migration against any shared database** — it is an EF migration, which `CLAUDE.md` lists as a sign-off trigger, and **CI does not run migrations**.

| Risk | Rollback |
| -- | -- |
| Migration generates a table rebuild instead of `ADD COLUMN` (possible on legacy tables) | Do not apply. Delete the migration, fix the config, regenerate. Catch this by reading the SQL at step 3, not after applying. |
| Applied to a shared DB and needs reverting | `dotnet ef database update <PreviousMigration>`. Additive columns only, so the down-migration is clean — **but any row written since would lose its** `UpdatedById`**.** |
| FK on `UpdatedById` blocks a user delete | Deliberate (`Restrict`). Users are deactivated, not deleted ([PPDO-115](<https://linear.app/ralphoksiprojects/issue/PPDO-115>)), so this should never fire. If it does, that is a signal worth reading, not a bug to cascade away. |

## 7. Out of scope — do NOT

* **Do NOT** add `CreatedAt` to `AipActivity`. Tempting while in the migration; unrelated, and the spec says so explicitly.
* **Do NOT** "tidy" the naming asymmetry between the two tables. `NAMING_CONVENTIONS.md` requires it.
* **Do NOT** add columns to programs, projects, offices — deliberately excluded (spec §4).
* **Do NOT** write any detection logic. That is child 2.
* **Do NOT** set `UpdatedAt`/`UpdatedById` from any service yet. Also child 2.

## 8. Commit message

```
feat(aip): add concurrency tokens to activities and expenditures (V18-71)
```

---

# Build notes — 2026-09-22

PR [obiken-01/ppdo-portal#367](https://linear.app/ralphoksiprojects/review/feataip-add-concurrency-tokens-to-activities-and-expenditures-ppdo-117-b720c78049e1) → `release/1.8.0`.

## ↩️ Two corrections to this ticket

Both found by reading the files §2 said to read, before writing code.

**1. ⚠️** `aip_activities` **is snake_case, NOT legacy PascalCase.** §1 of this ticket and §5 of the spec both said otherwise and instructed PascalCase columns on it. Wrong: the table post-dates that convention, `AipActivityConfiguration` maps every column to snake_case, and it is not in `CLAUDE.md`'s legacy list (`Users`, `CalendarEvents`, `ResourceLinks`, the v1.0 inventory tables). **Both tables got snake_case** — `row_version`, `updated_at`, `updated_by_id`. The only genuine asymmetry is that expenditures already carried `created_at`/`updated_at`. Spec corrected in PR [obiken-01/ppdo-portal#366](https://linear.app/ralphoksiprojects/review/docsaip-spec-the-concurrent-edit-guard-v18-71-7be9a9a37b06).

*(*`Users` *is itself legacy PascalCase, so both FKs point at* `Users.Id` *in PascalCase from snake_case columns. That mixture is what the convention produces, not a defect.)*

**2. The test fixtures hand-roll their SQLite schemas.** Six `CREATE TABLE` sites across four Infrastructure test files build tables with raw DDL rather than from the model. Neither this ticket nor the spec mentioned them, and they all had to learn the new columns.

SQLite needs more than the columns existing. It has no `rowversion` and nothing populates the token, so a plain nullable `BLOB` makes EF fail reading the value back after an insert — `The data is NULL at ordinal 3`**, 38 failures, none of which look concurrency-related.** Fixtures now use `NOT NULL DEFAULT x'0000000000000001'`, with a comment at each site saying the default is meaningless and that the real behaviour is SQL Server's.

⚠️ **Consequence worth carrying into [PPDO-118](<https://linear.app/ralphoksiprojects/issue/PPDO-118>): the conflict path cannot be tested on SQLite.** Nothing bumps the token there, so a `DbUpdateConcurrencyException` will never fire in those fixtures. [PPDO-118](https://linear.app/ralphoksiprojects/issue/PPDO-118/v18-712-service-layer-detect-the-conflict-and-build-the-409-payload)'s tests must either mock the exception or run against SQL Server — plan for that rather than discovering it mid-ticket.

## Migration review — done before applying

`AddAipConcurrencyTokens`: pure `AddColumn` / `AddForeignKey` / `CreateIndex`, real `rowversion` type rather than `varbinary`, **no table rebuild, no drops**. EF added indexes on both FK columns, which is what the conflict path will join on to resolve the changer's name.

## Verification against local `PPDOPortalDev`

| Check | Result |
| -- | -- |
| Row counts unchanged | activities **2562**, expenditures **18** — same before and after |
| Token populated | **2562 distinct values across 2562 rows**, zero nulls |
| No backfill | all `updated_at` null, as designed |
| **Token bumps on update** | `0x000000000009CBE3` → `0x000000000009EB11` |

The bump was checked inside a transaction that was **rolled back**, so the table was not mutated — `PPDOPortalDev` holds real data, not fixtures. That check matters because it is the behaviour the entire feature rests on.

**Tests:** 2394 passed, 0 failed — unchanged from the branch baseline, correct for a ticket that adds none of its own.

## Still outstanding

⚠️ Migration applied to **local dev only**. UAT and production both still need a manual `dotnet ef database update` — CI does not run migrations.
