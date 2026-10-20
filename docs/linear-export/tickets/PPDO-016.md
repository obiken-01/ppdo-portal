---
id: PPDO-16
title: 'Epic: External AIP API + partner API keys (v1.8.0 Phase 5, step 2)'
status: Done
project: PPDO Portal
labels: 
milestone: v1.8.0 — Office Users, AIP Redesign & Reviewer Flow
created: 2026-06-30
completed: 2026-09-15
parent: PPDO-83
blocked_by: (none)
related: PPDO-32, PPDO-83, RAL-58, RAL-108
url: https://linear.app/ralphoksiprojects/issue/PPDO-16
uuid: a6a6fe1e-daeb-4ff3-be06-042ae6ea78b2
---

# PPDO-16 — Epic: External AIP API + partner API keys (v1.8.0 Phase 5, step 2)

**In one line:** other provincial systems (GSO first) read a fiscal year's AIP server-to-server, with keys PPDO issues, scopes and revokes from a new Configuration → API Access page.

**Spec (authoritative):** `docs/v1.8/External_AIP_API_Spec.md` · **Payload:** `docs/external-api/` draft 1.0.0 (JSON Schema + samples) · **Still standing:** `docs/External_AIP_API_Contract.md` base URL, health, status codes · **Parent:** [PPDO-83](https://linear.app/ralphoksiprojects/issue/PPDO-83/epic-v180-phase-5-outputs)

> ⚠️ Rewritten 2026-09-14. The June version of this epic assumed amounts stored in thousands (×1000 in the API), a GSO-only single-office call, `sectors[]`, and **no** admin UI — all superseded. Amounts are stored in pesos (DECISION E), the payload is draft 1.0.0, and Ralph wants key-management pages.

## Settled with Ralph, 2026-09-14

* **GSO/MIS answers not in yet → build on the README §4 defaults:** released = `Consolidated`; an office appears only when every group is accepted (else `pendingOffices`); `printedAmounts` included from `AipPrintedFigures`; money as decimal strings; nested tree.
* **Auth:** `X-Api-Key`**, hashed (SHA-256), behind** `IPartnerCredentialValidator` so MIS SSO client credentials can replace it later.
* **Keys are admin-issued** per partner system, scoped to a list of offices or all offices, optional expiry. Rotation = issue new, then revoke old.
* **Who manages keys: SuperAdmin + new per-user grant** `CanManageApiKeys` (per-user chain; Admin not auto-granted).
* Whole-year call needs an all-offices key (else 403). Every external data pull logged in `partner_api_requests`; issue/revoke audited in `audit_log`. 60 req/min per key.

## Tickets (order)

| # | Ticket | Blocked by |
| -- | -- | -- |
| 1 | [PPDO-15](https://linear.app/ralphoksiprojects/issue/PPDO-15/external-aip-api-1-partner-api-keys-schema-key-generation) — schema, issuing service, `CanManageApiKeys` ⚠️ MIGRATION | — |
| 2 | [PPDO-13](https://linear.app/ralphoksiprojects/issue/PPDO-13/external-aip-api-2-api-key-authentication-validate-scope-per-key-rate) — key auth, scope, rate limit, request log | [PPDO-15](https://linear.app/ralphoksiprojects/issue/PPDO-15/external-aip-api-1-partner-api-keys-schema-key-generation) |
| 3 | [PPDO-14](https://linear.app/ralphoksiprojects/issue/PPDO-14/external-aip-api-3-external-read-service-dtos-final-only-sector) — external AIP read service (schema 1.0.0) | — (parallel with [PPDO-15](https://linear.app/ralphoksiprojects/issue/PPDO-15/external-aip-api-1-partner-api-keys-schema-issuing-service)) |
| 4 | [PPDO-12](https://linear.app/ralphoksiprojects/issue/PPDO-12/external-aip-api-4-external-endpoints-aip-aipfiscal-years-health-audit) — external endpoints + schema contract test | [PPDO-13](https://linear.app/ralphoksiprojects/issue/PPDO-13/external-aip-api-2-api-key-authentication-validate-scope-per-key-rate), [PPDO-14](https://linear.app/ralphoksiprojects/issue/PPDO-14/external-aip-api-3-external-read-service-dtos-final-only-sector) |
| 5 | [PPDO-86](https://linear.app/ralphoksiprojects/issue/PPDO-86/external-aip-api-5-configuration-api-access-page-config-endpoints) — API Access config page + config endpoints | [PPDO-15](https://linear.app/ralphoksiprojects/issue/PPDO-15/external-aip-api-1-partner-api-keys-schema-key-generation) |

## Deployment

⚠️ Migration `AddPartnerApiKeys` — manual `dotnet ef database update` against Azure SQL. No new package, no CORS change.

## Open (not blocking)

GSO's §4 answers · MIS SSO · tracker G5 · "SP-approved" flag · `?releasedSince=` · request-log retention.
