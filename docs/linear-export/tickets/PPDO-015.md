---
id: PPDO-15
title: 'External AIP API #1 — partner API keys: schema, issuing service, CanManageApiKeys grant'
status: Done
project: PPDO Portal
labels: 
milestone: v1.8.0 — Office Users, AIP Redesign & Reviewer Flow
created: 2026-06-30
completed: 2026-09-15
parent: PPDO-16
blocked_by: (none)
related: PPDO-14, PPDO-83, PPDO-132
url: https://linear.app/ralphoksiprojects/issue/PPDO-15
uuid: d641e0ff-fdef-4e8c-a611-0059288ddd81
---

# PPDO-15 — External AIP API #1 — partner API keys: schema, issuing service, CanManageApiKeys grant

**In one line:** the tables, the key generator and issue/revoke/list service, and the new per-user grant — everything the auth check ([PPDO-13](https://linear.app/ralphoksiprojects/issue/PPDO-13/external-aip-api-2-api-key-authentication-validate-scope-per-key-rate)) and the API Access page ([PPDO-86](https://linear.app/ralphoksiprojects/issue/PPDO-86/external-aip-api-5-configuration-api-access-page-config-endpoints)) build on.

**Spec:** `docs/v1.8/External_AIP_API_Spec.md` §2 decisions 4–7 and 9, §3.2, §3.3, §5, §8 · **Epic:** [PPDO-16](https://linear.app/ralphoksiprojects/issue/PPDO-16/epic-external-aip-api-partner-api-keys-v180-phase-5-step-2) · **Size:** M · ⚠️ MIGRATION

> ⚠️ Rewritten 2026-09-14 — the June draft (BCrypt-or-SHA, `scope_office_codes` column, no UI, first key via seed) is superseded by the spec.

## Implementation prompt

```
Read CLAUDE.md, PROJECT_DOCUMENTATION_NET_AZURE.md, and PPDO_PROJECT_CONTEXT.md.
Read docs/v1.8/External_AIP_API_Spec.md FULLY — it is the authoritative spec for this ticket.
Read docs/v1.8/Permission_Matrix.md §1 and §2.4, and docs/NAMING_CONVENTIONS.md.

Read these files before writing code:
- backend/PPDO.Domain/Entities/User.cs (OverrideCanReviewAllOffices — the per-user grant shape to copy)
- backend/PPDO.Domain/Interfaces/IPermissionService.cs + backend/PPDO.Application/Services/PermissionService.cs (per-user chain: SuperAdmin → true, else Override ?? false)
- backend/PPDO.Tests/Application/PermissionMatrixTests.cs + PermissionServiceTests.cs (a flag without a matrix row fails the build)
- backend/PPDO.Application/DTOs/Users/SetPermissionsDto.cs, UserResponseDto.cs, UpdateUserDto.cs + UserService.cs (where OverrideCanReviewAllOffices is read and written)
- backend/PPDO.Infrastructure/Data/Configurations/OfficeConfiguration.cs ("offices") + UserConfiguration.cs ("Users", legacy PascalCase)
- backend/PPDO.Application/Common/PasswordGenerator.cs (RandomNumberGenerator usage)
- backend/PPDO.Application/Services/IAuditService.cs (Guid-actor overload; never pass the key or hash)

Before changing anything, state in one line each:
- Current behaviour: no partner keys, no CanManageApiKeys.
- Target behaviour: three new tables, a service that issues (plaintext once), revokes and lists keys, and a fifth per-user grant.

Working branch: release/1.8.0. Create feature/v1.8.0-ppdo-15-partner-api-keys off release/1.8.0
and open the PR against release/1.8.0 (NOT main).

TDD: PermissionMatrixTests/PermissionServiceTests rows and PartnerApiKeyServiceTests failing first.

Steps:
1. ⚠️ MIGRATION AddPartnerApiKeys — partner_api_keys, partner_api_key_offices, partner_api_requests
   (snake_case, spec §5) + Users.OverrideCanManageApiKeys (PascalCase, legacy table). Entities + EF
   configs, unique index on key_prefix, (key_id, requested_at) index.
   Verify: dotnet ef migrations add … then dotnet ef database update locally; inspect in SSMS.
2. CanManageApiKeysAsync in IPermissionService/PermissionService (per-user chain); matrix row in
   Permission_Matrix.md §2.4; independence tests vs CanManageConfig, CanManageUsers, CanReviewAllOffices.
   User DTOs + UserService carry OverrideCanManageApiKeys.
   Verify: dotnet test PPDO.Tests --filter "PermissionMatrix|PermissionService|UserService".
3. PartnerApiKeyGenerator (Application/Common): ppdo_<8-char prefix>_<32-byte base64url secret>;
   Hash(key) = SHA-256 hex. Verify: unit tests — prefix charset/length, two keys never equal, hash stable.
4. IPartnerApiKeyRepository (Domain interface, Infrastructure impl) — scoped queries only: by prefix,
   list with offices + creator names, requests page (Skip/Take + count), insert request row, touch last_used_at.
   Verify: SQLite repository tests for the list and the requests page.
5. IPartnerApiKeyService / PartnerApiKeyService — IssueAsync (validation per spec §3.2, returns
   plaintext once, stores hash, audit CREATE), RevokeAsync (409 if already revoked, audit UPDATE),
   ListAsync (status computed Active/Expired/Revoked, Manila end-of-day expiry), GetRequestsAsync.
   Every method checks CanManageApiKeysAsync itself too. Register in Program.cs.
   Verify: dotnet test PPDO.Tests --filter PartnerApiKeyService.

Risks / sign-off:
- PermissionService change — sign-off before merge (Permission_Matrix.md is the reference).
- EF migration — additive only; rollback = dotnet ef database update <previous migration> (drops the three tables and the column; no data loss elsewhere).
- The plaintext key and the hash must never reach a log line, an audit row or a list DTO.

Do NOT: build endpoints or the page (PPDO-86), the request-time validator (PPDO-13), or a seed key.
Do NOT auto-grant Admin, or add the flag to Division.

PR body: carry the ⚠️ MIGRATION note into the deploy section.

When done, commit with:
feat(external-api): partner API key schema, issuing service and CanManageApiKeys grant (PPDO-15)
```

## Manual-coding candidate?

**No** — PermissionService and an EF migration (both on the CLAUDE.md exclusion list). Step 3 (the generator + its tests) could be split off for hand-coding if wanted.
