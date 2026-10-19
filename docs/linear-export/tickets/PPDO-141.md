---
id: PPDO-141
title: 'Auth: store refresh tokens hashed (SHA-256), not plaintext'
status: Done
project: PPDO Portal
labels: testing, backend, Improvement
milestone: v1.8.2 — Dashboard Redesign, Caching & Remaining Optimizations
created: 2026-09-27
completed: 2026-10-05
parent: (none)
blocked_by: (none)
related: PPDO-142
url: https://linear.app/ralphoksiprojects/issue/PPDO-141
uuid: 919f26a1-7174-49b2-8c46-5fe4da73f508
---

# PPDO-141 — Auth: store refresh tokens hashed (SHA-256), not plaintext

> **Scheduled into v1.8.2 (Ralph, 2026-10-05).** Re-checked against `origin/release/1.8.2` @ `4f181f71` on 2026-10-05. The original prompt (it targeted release/1.8.0) was replaced by **Implementation prompt (v1.8.2)** at the bottom. Line numbers in "Current → target" are stale; use the facts below.
>
> **Code facts as of 2026-10-05:**
>
> * Raw token written at `AuthService.cs:116` (login) and `:164` (rotation).
> * Lookup at `AuthService.cs:133` → `UserRepository.FindByRefreshTokenAsync` (`:52–59`).
> * Nulled (no change needed) at `AuthService.cs:153` (expired), `:180` (logout), `:333` (password change), and `UserService.cs:399` / `:499` (reset / deactivate).
> * `GenerateRefreshToken` at `AuthService.cs:458`.
> * Column: `UserConfiguration.cs:72–73`, `HasMaxLength(100)`.
> * **Sibling to copy:** `ApiKeyGenerator.Hash` (`PPDO.Application/Common/ApiKeyGenerator.cs:46`). It already does SHA-256 → lower-case hex for partner API keys. Reuse that shape; don't invent a second format.
> * The cookie is set from `result.RefreshToken` in `AuthFunctions.cs:88/121`. It must stay the **raw** token.
> * `docs/PGOM_Database_Sharing_Impact.md` is on `main` (PR [docs(integration): PGOM database-sharing impact study + learning bank B3](https://linear.app/ralphoksiprojects/review/docsintegration-pgom-database-sharing-impact-study-learning-bank-b3-f159d46c8498)), not on `release/1.8.2`. Read it with `git show origin/main:docs/PGOM_Database_Sharing_Impact.md`.
>
> **Deploy effect:** everyone signed in is logged out once, the first time v1.8.2 reaches an environment. That happens in UAT first (tell the testers), then in production. Add it to the v1.8.2 release notes.

## Why

Found in the PGOM database-sharing impact study (`docs/PGOM_Database_Sharing_Impact.md` §4.1, prep for NOM 2026-136 on Oct 6). `Users.RefreshToken` holds the **raw** refresh token. Anyone who can read the `Users` table can copy a live token and mint access tokens as that user for up to 7 days. Examples: a DBA, a read-only login handed to MIS, a leaked backup, or a shared PGOM database. This must be fixed before any database-level sharing (Options B/C in the study). It is good hygiene regardless.

Passwords are already BCrypt-hashed. The refresh token is the one credential still stored in the clear.

## Current → target

* **Current:** `AuthService` writes the Base64 token (88 chars) to `Users.RefreshToken` (`AuthService.cs:96`, and again on rotation \~line 142). `UserRepository.FindByRefreshTokenAsync` matches on the raw value.
* **Target:** the cookie still carries the raw token. The DB stores only `SHA-256(token)` (hex, 64 chars), and lookup hashes the incoming token first. A plain SHA-256 (not BCrypt) is correct here because the token is 64 random bytes and must support an equality lookup.

## Scope notes

* **No migration needed.** A 64-char hex hash fits the existing `nvarchar(100)` (`UserConfiguration.cs:58`). Confirm this, and don't add one.
* **One-time effect on deploy:** existing plaintext tokens will no longer match, so every signed-in user gets logged out **once** and has to log in again. Deploy outside office hours or announce it. Optional: a one-off `UPDATE Users SET RefreshToken = NULL, RefreshTokenExpiry = NULL` so the stale values don't sit around. ⚠️ That is prod data and needs sign-off.
* Logout/revoke paths that null the column keep working unchanged.

## Acceptance

- [ ] `Users.RefreshToken` holds a 64-char hex value after login, never the cookie value
- [ ] Silent refresh, rotation and logout behave as before
- [ ] New tests cover: hash stored, raw token refreshes, stored hash cannot be replayed
- [ ] PR body notes the one-time forced re-login

## Learning-ticket assessment

**Not a manual candidate.** It touches auth, and a missed write path isn't caught by the compiler (CLAUDE.md exclusion).

## Implementation prompt (v1.8.2)

```
Read CLAUDE.md, then this Linear ticket (PPDO-141) in full, including the "Code facts as of
2026-10-05" block at the top. Read docs/PGOM_Database_Sharing_Impact.md §4 and §6 for context;
it lives on main: git show origin/main:docs/PGOM_Database_Sharing_Impact.md

Branch: fix/v1.8.2-ppdo-141-hash-refresh-tokens off origin/release/1.8.2. PR into release/1.8.2
(NOT main, NOT release/1.8.1).

Read before coding: AuthService.cs (login, RefreshAsync, LogoutAsync, the password-change path,
GenerateRefreshToken), UserRepository.FindByRefreshTokenAsync, IUserRepository,
UserConfiguration.cs (RefreshToken max length), UserService.cs (reset/deactivate),
ApiKeyGenerator.Hash (the sibling), AuthFunctions.cs (the cookie), AuthServiceTests.cs and
UserServiceTests.cs.

State in one line each before changing anything: current behaviour, target behaviour.

TDD: write the failing AuthServiceTests first, then implement.
1. A hashing helper with the same output as ApiKeyGenerator.Hash: SHA-256 → lower-case hex,
   64 chars. Either reuse a shared helper or add one in PPDO.Application/Common. Hashing stays
   in the Application layer; the repository still just compares strings.
2. Login and rotation store the hash; the raw token is still returned to the Function for the
   cookie. RefreshAsync looks up FindByRefreshTokenAsync(hash(incoming)).
   Tests:
   - the stored value is not the raw token, and equals SHA-256 hex of it;
   - refreshing with the raw token succeeds;
   - refreshing with the stored hash itself FAILS (a DB reader can't replay it);
   - rotation: the old raw token is Superseded after a refresh;
   - expiry and logout still clear the column.
   Red-test the replay test: temporarily store the raw token again and watch it fail.
3. grep every `RefreshToken =` assignment (AuthService, UserService) and confirm each one is
   either hashed or null. List them in the PR.
4. No migration: 64 hex chars fit nvarchar(100). Prove it with
   `dotnet ef migrations has-pending-model-changes --project PPDO.Infrastructure --startup-project PPDO.Functions`
   (expect none). Do not add a migration.
5. Verify: `cd backend && dotnet test` all green.
6. Manual, local (func start + npm run dev):
   - log in with a local test account from your memory notes (never guess or reset passwords);
   - in SSMS, Users.RefreshToken for that user is 64 hex chars;
   - reload the page: the silent refresh keeps you signed in, and the stored value changes (rotation);
   - log out: the column is NULL;
   - other users' rows are untouched apart from being logged out once.

Do NOT change the cookie attributes, the token length, the expiry, the /auth/refresh response
contract, or RefreshResult's Superseded/Expired distinction (RAL-198). Never log tokens or hashes.
No UPDATE against UAT or prod data.

PR body: the assignment list from step 3, test names, and a "Deploy note: every signed-in user
is logged out once when this reaches an environment" line.
Stop after opening the PR. This is an auth change; Ralph reviews it before merge.

Commit: fix(auth): store refresh tokens as SHA-256 hashes (PPDO-141)
```
