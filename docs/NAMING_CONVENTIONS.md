# Naming Conventions

## Ralph's Personal Development Standard (RPDS)

> **Core principle:** Follow the industry standard for the language being used. This document defines the standard per language and the cross-language conventions that apply to all projects.

---

## C# / .NET

Follows [Microsoft C# Naming Guidelines](https://learn.microsoft.com/en-us/dotnet/csharp/fundamentals/coding-style/identifier-names).

|Element|Convention|Example|
|---|---|---|
|Classes|PascalCase|`TimekeepingUser`, `TimeLogService`|
|Interfaces|PascalCase with `I` prefix|`ITimeLogRepository`, `ITokenService`|
|Methods|PascalCase|`GetByPublicIdAsync`, `GenerateRefreshToken`|
|Properties|PascalCase|`PublicId`, `TaskDescription`|
|Fields (private)|camelCase with `_` prefix|`_unitOfWork`, `_tokenService`|
|Parameters|camelCase|`refreshToken`, `publicId`|
|Local variables|camelCase|`existingToken`, `newRefreshToken`|
|Constants|PascalCase|`MaxPageSize`|
|Enums|PascalCase (type + values)|`UserType.Ralphy`, `PostStatus.Draft`|
|DTOs|PascalCase with `Dto` suffix|`TimeLogDto`, `CreateTimekeepingUserDto`|
|Controllers|PascalCase with `Controller` suffix|`TimeLogController`|
|Services|PascalCase with `Service` suffix|`TimekeepingAuthService`|
|Repositories|PascalCase with `Repository` suffix|`TimeLogRepository`|

### Async Methods

Always suffix async methods with `Async`:

```csharp
Task<TimeLog?> GetByIdAsync(int id);
Task<PagedTimeLogResultDto> GetFilteredAsync(...);
```

### File Names

One class per file. File name matches class name exactly:

```
TimekeepingUser.cs
ITimeLogRepository.cs
TimeLogService.cs
```

---

## JavaScript / TypeScript / React

Follows [Airbnb JavaScript Style Guide](https://github.com/airbnb/javascript) conventions.

|Element|Convention|Example|
|---|---|---|
|Variables|camelCase|`accessToken`, `refreshToken`|
|Functions|camelCase|`fetchLogs`, `handleSubmit`|
|React components|PascalCase|`TimeLogPage`, `TopMenu`|
|React component files|PascalCase|`TimeLogPage.jsx`, `TopMenu.jsx`|
|Non-component files|camelCase|`timekeepingApi.js`, `helpers.js`|
|Constants|SCREAMING_SNAKE_CASE|`TK_ACCESS_TOKEN_KEY`, `BASE_URL`|
|CSS classes|kebab-case|`time-log-page`, `top-menu`|
|Props|camelCase|`darkMode`, `onToggleDarkMode`|
|Event handlers|camelCase with `handle` prefix|`handleSubmit`, `handleDelete`|
|Boolean variables|camelCase with `is/has/can` prefix|`isLoading`, `hasError`, `isActive`|

### File Structure Convention

```
ComponentName/
├── ComponentName.jsx     ← component
├── ComponentName.css     ← styles (if needed)
└── index.js             ← re-export (for larger components)
```

For simple components, a single file is fine:

```
TopMenu.jsx
TimeLogPage.jsx
```

---

## Python

Follows [PEP 8](https://peps.python.org/pep-0008/).

|Element|Convention|Example|
|---|---|---|
|Variables|snake_case|`access_token`, `refresh_token`|
|Functions|snake_case|`get_by_id`, `fetch_logs`|
|Classes|PascalCase|`TimeLogService`, `UserRepository`|
|Constants|SCREAMING_SNAKE_CASE|`MAX_PAGE_SIZE`, `BASE_URL`|
|Modules/files|snake_case|`time_log_service.py`, `auth_utils.py`|
|Private members|snake_case with `_` prefix|`_unit_of_work`, `_token_service`|

---

## Database

> **Convention (updated 2026-06-22):** new tables and columns use **snake_case**. C# entity
> properties stay PascalCase and are mapped to snake_case columns via `.ToTable("…")` /
> `.HasColumnName("…")` in each entity's `IEntityTypeConfiguration` (see `AccountConfiguration`
> for the canonical pattern). Tables that already exist in **PascalCase** are **left as-is — never
> rename them**; new columns added to such a table follow that table's existing PascalCase for
> intra-table consistency.

|Element|Convention|Example|
|---|---|---|
|Table names|snake_case, plural|`accounts`, `funding_sources`, `wfp_expenditure_lines`|
|Column names|snake_case (mapped from PascalCase property)|`account_title`, `is_active`, `created_at`|
|Primary keys|`id`|`id`|
|Foreign keys|`[entity]_id`|`office_id`, `created_by_id`|
|Indexes|`IX_[table]_[column]`|`IX_accounts_number`|
|Foreign key constraints|`FK_[Table]_[RefTable]_[Column]`|`FK_calendar_events_users_reviewed_by_id`|
|Migration names|PascalCase, descriptive|`AddPlanningTables`, `AddAnnouncements`|

**Legacy PascalCase tables (do not rename):** pre-v1.1 tables — e.g. `Users`, `CalendarEvents`,
`ResourceLinks`, `PermissionGroups`, and the v1.0 inventory tables — keep PascalCase table and
column names. The snake_case convention above applies to every table introduced from **v1.1
onward** (`accounts`, `offices`, `funding_sources`, the `ldip_*` / `aip_*` / `wfp_*` tables,
`announcements`, …). When you add a column to a legacy PascalCase table, match that table
(PascalCase) rather than introducing a mixed-casing table — e.g. the v1.1.1 approval columns added
to `CalendarEvents` are `Status` / `ReviewedById`, not `status` / `reviewed_by_id`.

---

## API Routes

|Convention|Example|
|---|---|
|kebab-case|`/api/timekeeping/auth`|
|Plural nouns for resources|`/api/time-logs`, `/api/trips`|
|Nested for relationships|`/api/timekeeping/logs/{id}`|
|Actions as sub-routes|`/api/timekeeping/users/{id}/reset-password`|
|HTTP verbs define the action — not the URL|`DELETE /api/logs/{id}` not `/api/logs/delete/{id}`|

---

## Environment Variables

|Convention|Example|
|---|---|
|SCREAMING_SNAKE_CASE|`JWT_SECRET_KEY`, `CLOUDINARY_API_KEY`|
|.NET nested config uses `__`|`Jwt__SecretKey`, `Anthropic__ApiKey`|
|Frontend uses `VITE_` prefix|`VITE_API_URL`, `VITE_SHOPPING_API_KEY`|
|Never commit actual values|Use `.env.example` with placeholders|

---

## Git Branch & File Names

|Element|Convention|Example|
|---|---|---|
|Branch names|kebab-case|`feature/v1.3-timekeeping`|
|Commit scopes|camelCase or single word|`feat(timekeeping):`, `fix(auth):`|
|Documentation files|SCREAMING_SNAKE_CASE|`PROJECT_DOCUMENTATION.md`, `CLAUDE.md`|
|Config files|lowercase with dots|`.env`, `.gitignore`, `vite.config.js`|

---

## General Rules (All Languages)

1. **Be descriptive** — `getUserByPublicId` not `getUser2`
2. **Avoid abbreviations** unless universally understood — `dto`, `id`, `url` are fine; `usrMgr` is not
3. **Boolean names should read as questions** — `isActive`, `hasError`, `canDelete`
4. **Collections should be plural** — `users`, `timeLogs`, `refreshTokens`
5. **Avoid magic numbers** — use named constants instead
6. **Consistency over cleverness** — match the existing codebase pattern

---

_Part of Ralph's Personal Development Standard (RPDS) v1.0_