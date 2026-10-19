---
id: PPDO-13
title: 'External AIP API #2 — key authentication, office scope, per-key rate limit, request log'
status: Done
project: PPDO Portal
labels: 
milestone: v1.8.0 — Office Users, AIP Redesign & Reviewer Flow
created: 2026-06-30
completed: 2026-09-15
parent: PPDO-16
blocked_by: PPDO-15
related: PPDO-83, RAL-58
url: https://linear.app/ralphoksiprojects/issue/PPDO-13
uuid: 04441285-1873-4548-bbea-1d90d7b06e3d
---

# PPDO-13 — External AIP API #2 — key authentication, office scope, per-key rate limit, request log

**In one line:** decide, on every external request, which partner is calling, whether it may read the office or the whole year, whether it is over its rate, and record the pull.

**Spec:** `docs/v1.8/External_AIP_API_Spec.md` §2 decisions 3, 8, 9, 10 and §3.1 (auth, scope, rate-limit rows) · **Epic:** [PPDO-16](https://linear.app/ralphoksiprojects/issue/PPDO-16/epic-external-aip-api-partner-api-keys-v180-phase-5-step-2) · **Blocked by:** [PPDO-15](https://linear.app/ralphoksiprojects/issue/PPDO-15/external-aip-api-1-partner-api-keys-schema-issuing-service) · **Size:** M · 🔒 security sign-off

> ⚠️ Rewritten 2026-09-14 — adds the swappable credential interface (MIS SSO may replace keys), the whole-year all-offices rule and the request log.

## Implementation prompt

```
Read CLAUDE.md, PROJECT_DOCUMENTATION_NET_AZURE.md, and PPDO_PROJECT_CONTEXT.md.
Read docs/v1.8/External_AIP_API_Spec.md FULLY — it is the authoritative spec for this ticket.
Read docs/external-api/README.md §2 decision 13 and §5.2 Q4 (why the check sits behind an interface).

Read these files before writing code:
- backend/PPDO.Application/Services/IPartnerApiKeyService.cs + the repository from PPDO-15 (by-prefix lookup, touch last_used_at, insert request row)
- backend/PPDO.Application/Common/PartnerApiKeyGenerator.cs (Hash — reuse, never re-implement)
- backend/PPDO.Infrastructure/Services/JwtMiddleware.cs + backend/PPDO.Domain/Interfaces/IJwtMiddleware.cs (manual auth precedent, header read from HttpRequestData)
- backend/PPDO.Application/Services/AuthService.cs lines ~36–100, ~390–400 (IMemoryCache fixed-window limiter + RetryAfterSeconds — mirror it)
- backend/PPDO.Functions/Functions/ConfigHttp.cs (envelope helpers)

Before changing anything, state in one line each:
- Current behaviour: nothing reads X-Api-Key; no partner scope or request log exists.
- Target behaviour: IPartnerCredentialValidator resolves a caller from a request; PartnerApiScope and PartnerRateLimiter decide 401/403/429; each authenticated pull is logged.

Working branch: release/1.8.0. Create feature/v1.8.0-ppdo-13-partner-key-auth off release/1.8.0
and open the PR against release/1.8.0 (NOT main).

TDD: ApiKeyCredentialValidatorTests, PartnerApiScopeTests, PartnerRateLimiterTests failing first.

Steps:
1. PartnerCaller record (keyId, partnerName, allOffices, officeCodes) + IPartnerCredentialValidator
   (Application) with ApiKeyCredentialValidator: parse ppdo_<prefix>_<secret>, look up by prefix, compare
   SHA-256 with CryptographicOperations.FixedTimeEquals, reject revoked/expired — one 401 message for
   every failure. Verify: tests for valid, missing, malformed, wrong secret with a real prefix, revoked, expired.
2. PartnerApiScope: officeCode null → requires allOffices (403 whole-year message); code not in scope
   → 403; unknown code → 400 for all-offices keys only; inactive office → 403.
   Verify: dotnet test PPDO.Tests --filter PartnerApiScope.
3. PartnerRateLimiter: 60/min per key, IMemoryCache fixed window, returns Retry-After seconds.
   Verify: 60 pass, 61st limited, next window resets, keys independent.
4. IPartnerRequestLogger: writes partner_api_requests + last_used_at for 200/400/403 only.
   Verify: tests that 401/429 write nothing.
5. Register everything in Program.cs. Verify: full dotnet test.

Risks / sign-off (🔒 required before merge):
- New external auth surface on a government system — review against spec §3.1 row by row.
- Timing: never short-circuit the hash comparison; never log the header value.
- Memory limiter is per Functions instance — acceptable per spec decision 10; note it in the PR.

Do NOT: build the endpoints (PPDO-12), accept the key in a query string, support Authorization: Bearer,
or add CORS for /external routes.

When done, commit with:
feat(external-api): partner key authentication, office scope, rate limit and request log (PPDO-13)
```

## Manual-coding candidate?

**No** — auth (CLAUDE.md exclusion list): a missing check is not caught by the compiler.
