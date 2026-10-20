---
id: PPDO-12
title: 'External AIP API #4 — /external/v1 endpoints (health, aip, fiscal-years) + schema contract test'
status: Done
project: PPDO Portal
labels: 
milestone: v1.8.0 — Office Users, AIP Redesign & Reviewer Flow
created: 2026-06-30
completed: 2026-09-15
parent: PPDO-16
blocked_by: PPDO-13, PPDO-14
related: PPDO-83
url: https://linear.app/ralphoksiprojects/issue/PPDO-12
uuid: cc1eafda-57f4-4536-9f18-ed5b3849c50c
---

# PPDO-12 — External AIP API #4 — /external/v1 endpoints (health, aip, fiscal-years) + schema contract test

**In one line:** wire [PPDO-13](https://linear.app/ralphoksiprojects/issue/PPDO-13/external-aip-api-2-key-authentication-office-scope-per-key-rate-limit)'s checks and [PPDO-14](https://linear.app/ralphoksiprojects/issue/PPDO-14/external-aip-api-3-external-aip-read-service-schema-100-pesos-as)'s read service into three versioned HTTP routes, and prove a real response matches the published schema.

**Spec:** `docs/v1.8/External_AIP_API_Spec.md` §2 decision 11, §3.1, §4.1, §10 (external lines) · **Contract still standing:** `docs/External_AIP_API_Contract.md` §2, §4.1, §7 · **Epic:** [PPDO-16](https://linear.app/ralphoksiprojects/issue/PPDO-16/epic-external-aip-api-partner-api-keys-v180-phase-5-step-2) · **Blocked by:** [PPDO-13](https://linear.app/ralphoksiprojects/issue/PPDO-13/external-aip-api-2-key-authentication-office-scope-per-key-rate-limit), [PPDO-14](https://linear.app/ralphoksiprojects/issue/PPDO-14/external-aip-api-3-external-aip-read-service-schema-100-pesos-as) · **Size:** S–M

> ⚠️ Rewritten 2026-09-14 — `officeCode` is now optional (whole year), health does not touch SQL, and audit goes to `partner_api_requests` ([PPDO-13](https://linear.app/ralphoksiprojects/issue/PPDO-13/external-aip-api-2-key-authentication-office-scope-per-key-rate-limit)), not `audit_log`.

## Implementation prompt

```
Read CLAUDE.md, PROJECT_DOCUMENTATION_NET_AZURE.md, and PPDO_PROJECT_CONTEXT.md.
Read docs/v1.8/External_AIP_API_Spec.md FULLY — it is the authoritative spec for this ticket.
Read docs/external-api/README.md §1 and §7.

Read these files before writing code:
- backend/PPDO.Functions/Functions/HealthFunctions.cs (existing public health route — the new one must NOT call SQL)
- backend/PPDO.Functions/Functions/AipConsolidatedFunctions.cs (thin handler, query parsing, envelope on refusal)
- backend/PPDO.Functions/Functions/ConfigHttp.cs (EnvelopeAsync; camelCase JSON)
- backend/PPDO.Application/Services/IExternalAipReadService.cs (PPDO-14)
- IPartnerCredentialValidator, PartnerApiScope, PartnerRateLimiter, IPartnerRequestLogger (PPDO-13)
- backend/PPDO.Tests/Functions/AipConsolidatedFunctionsTests.cs + FunctionHttpHarness.cs (endpoint test pattern, headers, body)

Before changing anything, state in one line each:
- Current behaviour: no /api/external routes exist.
- Target behaviour: three /api/external/v1 routes per spec §4.1, each authenticated pull logged.

Working branch: release/1.8.0. Create feature/v1.8.0-ppdo-12-external-endpoints off release/1.8.0
and open the PR against release/1.8.0 (NOT main).

TDD: ExternalAipFunctionsTests failing first.

Steps:
1. ExternalHealthFunctions — GET external/v1/health, public, bare { status, timestamp }.
   Verify: test + curl with no header.
2. ExternalAipFunctions — GET external/v1/aip and external/v1/aip/fiscal-years: validator (401) →
   rate limiter (429 + Retry-After) → fiscalYear parse (400) → scope (403/400) → read service → log →
   envelope. Handlers hold no business rules. Verify: dotnet test --filter ExternalAipFunctions
   covering every §3.1 status row, and that 401/429 write no request row.
3. Schema contract test: call the handler over a seeded fixture and validate the body's data against
   docs/external-api/aip-response.schema.json. Verify: fails if a field is renamed.
4. Local manual run with a key from the API Access page (or PPDO-15's service in a test): the curl
   lines in spec §10. Check whether the host gzips with Accept-Encoding: gzip; record the answer.

Risks / sign-off:
- Order of checks matters: never read AIP data before the key and scope pass.
- Route collision: "external/v1/aip" and "external/v1/aip/fiscal-years" are literal; confirm func start lists both.
- Error bodies: no stack traces, no internal ids.

Do NOT: add CORS, pagination, releasedSince, a Bearer header, or change /api/health.

PR body: manual test plan = spec §10 external lines; note the gzip finding and the FY2027 whole-year size.

When done, commit with:
feat(external-api): /external/v1 health, AIP and fiscal-years endpoints with schema contract test (PPDO-12)
```

## Manual-coding candidate?

Step 1 (external health endpoint + its test) — small, has a sibling (`HealthFunctions.cs`), verifiable with `dotnet test`. The data routes are not (auth ordering).
