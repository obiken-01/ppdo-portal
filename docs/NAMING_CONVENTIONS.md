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

|Element|Convention|Example|
|---|---|---|
|Table names|PascalCase (match entity)|`TimekeepingUsers`, `TimeLogs`|
|Column names|PascalCase (match property)|`PublicId`, `TaskDescription`|
|Primary keys|`Id`|`Id`|
|Foreign keys|`[EntityName]Id`|`TimekeepingUserId`, `TripId`|
|Indexes|`IX_[Table]_[Column]`|`IX_TimekeepingUsers_PublicId`|
|Foreign key constraints|`FK_[Table]_[RefTable]_[Column]`|`FK_TimeLogs_TimekeepingUsers_TimekeepingUserId`|
|Migration names|PascalCase, descriptive|`AddTimekeepingTables`, `RemoveRefreshTokenUserForeignKey`|

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