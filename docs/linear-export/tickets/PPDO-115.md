---
id: PPDO-115
title: 'Resetting a deactivated user''s password appears to succeed, but they can never log in'
status: Done
project: PPDO Portal
labels: 
milestone: techdebt
created: 2026-09-22
completed: 2026-09-22
parent: (none)
blocked_by: (none)
related: PPDO-116, PPDO-117, PPDO-118
url: https://linear.app/ralphoksiprojects/issue/PPDO-115
uuid: 62036d86-db15-4ce7-a989-9d82e7974605
---

# PPDO-115 — Resetting a deactivated user's password appears to succeed, but they can never log in

Found 2026-09-22 while diagnosing a real UAT login failure (user `mj.ppdo`). **Live in production as well** — both halves of the defect are on `main` unchanged, so this is not a v1.8.0 regression.

## Symptom

An admin resets a deactivated user's password. The reset **succeeds**: a new one-time password is generated, shown once in the modal, and handed to the user. The user then gets `401` on every login attempt, and after 5 attempts a `429` lockout for 15 minutes.

There is no hint anywhere — not in the reset confirmation, not in the modal, not in the login response, not on the user's screen — that the account is deactivated. The `401` is byte-identical to "wrong password", so the admin and the user both reasonably conclude the new password is broken and keep retrying, which produces the `429`.

**This is not hypothetical.** It cost a UAT session on 2026-09-22. The User Management row for `mj.ppdo` showed a red **Inactive** badge and a **Reset** button sitting directly beside **Activate**, and the reset was taken in good faith.

## Root cause — three gaps that combine

**1.** `ResetPasswordAsync` **never checks** `IsActive`**.**

`backend/PPDO.Application/Services/UserService.cs:367` loads the target via `GetByIdWithDivisionAsync`, checks `CanRequesterManageTarget`, then resets. `IsActive` is never consulted, so a deactivated account is reset exactly like an active one.

**2. The login lookup filters on** `IsActive`**.**

`backend/PPDO.Infrastructure/Repositories/UserRepository.cs:26` — `FindByUsernameAsync`:

```csharp
u => u.Username == username && u.IsActive
```

A deactivated user therefore resolves to `null`, and `AuthService.LoginAsync` returns `LoginResult.Invalid()` — the same result as a wrong password, by design (it must not leak account existence).

**3. The UI offers Reset unconditionally.**

`frontend/src/app/(portal)/admin/users/page.tsx` — the row actions gate Deactivate/Activate on `isActive` but not Reset:

```tsx
{ key: "reset", label: "Reset", onClick: () => setResetTarget(user) },
user.isActive
  ? { key: "deactivate", label: "Deactivate", … }
  : { key: "activate",   label: "Activate",   … },
```

Individually each of these is defensible — the login lookup *should* exclude inactive users, and the 401 *should not* distinguish causes. The bug is that the reset path does not know about the rule the login path enforces.

## Reproduce

1. User Management → deactivate any user (row badge flips to a red **Inactive**)
2. Row actions → **Reset** on that same user — it succeeds and shows a one-time password
3. Log out, attempt login as that user with the issued password → `401`
4. Repeat 5× → `429`, `Retry-After` \~15 min

## Suggested fix

Smallest correct change is **both** ends, since either alone leaves a hole:

* **Backend** — `ResetPasswordAsync` returns a `ServiceResult` failure for an inactive target, with a message naming the reason (e.g. *"This account is deactivated. Reactivate it before resetting the password."*). This is the real guard; the UI change alone can be bypassed by a direct `PUT /api/users/{id}/reset-password`.
* **Frontend** — either disable the Reset row action when `!user.isActive`, or keep it and surface the backend's message in the confirm modal. Disabling with a tooltip is probably kinder than hiding it, since hiding gives no explanation for why the button vanished.

⚠️ **Do not "fix" this by dropping the** `IsActive` **filter from** `FindByUsernameAsync` — that would let deactivated users log in, which is the whole point of deactivation.

### Worth considering, separately

The `401`-is-indistinguishable property is correct for the *public* login endpoint and should stay. But the admin has no way to tell these apart either, and that is the part that cost real time here. The `AuthService` log lines already distinguish them precisely:

| Log line | Meaning |
| -- | -- |
| `Login failed — username not found or user inactive. Username: {Username}` | wrong username, **or deactivated** |
| `Login failed — wrong password. UserId: {UserId}` | found and active, password mismatch |

That is how this was diagnosed (App Insights on `ppdo-portal-api-uat` → Performance → Login → the 401 transaction's traces). Splitting "or user inactive" into its own log line would make the next occurrence a one-glance answer rather than a two-possibility one.

## Test focus

* `ResetPasswordAsync_InactiveTarget_ReturnsFailure` — add beside the existing `ResetPasswordAsync` block in `backend/PPDO.Tests/Application/UserServiceTests.cs` (\~line 829)
* Red-test it: remove the guard and confirm the test fails. The existing `ResetPasswordAsync_ValidTarget_*` tests all build **active** targets, so a guard that never fires would otherwise pass vacuously.

## Branch

Retrospective action item 11 says an independent correctness fix ships as a patch off `main` rather than waiting behind a milestone. This depends on nothing in v1.8.0, so `fix/v1.7.5-*` → `main` is the natural shape — but note UAT currently runs `release/1.8.0` content, so the fix needs to reach that branch too (merge `main` forward, or cherry-pick) for the UAT tester to see it.
