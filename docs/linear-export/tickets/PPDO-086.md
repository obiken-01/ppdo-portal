---
id: PPDO-86
title: 'External AIP API #5 — Configuration → API Access page + config endpoints'
status: Done
project: PPDO Portal
labels: 
milestone: v1.8.0 — Office Users, AIP Redesign & Reviewer Flow
created: 2026-09-14
completed: 2026-09-15
parent: PPDO-16
blocked_by: PPDO-15
related: PPDO-83
url: https://linear.app/ralphoksiprojects/issue/PPDO-86
uuid: 42879d85-d3b4-4d3e-88c5-c73f5d2ce707
---

# PPDO-86 — External AIP API #5 — Configuration → API Access page + config endpoints

**In one line:** a SuperAdmin or a holder of `CanManageApiKeys` issues, scopes, revokes and inspects partner API keys from a new Configuration → API Access page.

**Spec:** `docs/v1.8/External_AIP_API_Spec.md` §3.2, §3.3, §4.2, §4.3, §6 · **Epic:** [PPDO-16](https://linear.app/ralphoksiprojects/issue/PPDO-16/epic-external-aip-api-for-gso-phase-2-implementation) (under [PPDO-83](https://linear.app/ralphoksiprojects/issue/PPDO-83/epic-v180-phase-5-outputs)) · **Blocked by:** [PPDO-15](https://linear.app/ralphoksiprojects/issue/PPDO-15/external-aip-api-1-partner-api-keys-schema-key-generation) · **Size:** M

## Implementation prompt

```
Read CLAUDE.md, PROJECT_DOCUMENTATION_NET_AZURE.md, and PPDO_PROJECT_CONTEXT.md.
Read docs/v1.8/External_AIP_API_Spec.md FULLY — it is the authoritative spec for this ticket
(§3.2, §3.3, §4.2, §4.3 and §6 are this ticket's). Read docs/DESIGN_SYSTEM.md before the page.

Read these files before writing code:
- backend/PPDO.Application/Services/IPartnerApiKeyService.cs + PartnerApiKeyService.cs (from PPDO-15 — issue/revoke/list rules live there, not in the handler)
- backend/PPDO.Functions/Functions/ConfigEsreCodeFunctions.cs (config/<resource> route + gate + envelope pattern)
- backend/PPDO.Functions/Functions/ConfigHttp.cs (AuthorizeAsync / FromResultAsync / EnvelopeAsync)
- backend/PPDO.Application/Services/AuthService.cs + the /auth/me DTO (where canReviewAllOffices is emitted — add canManageApiKeys the same way)
- frontend/src/app/(portal)/config/esre-codes/page.tsx (config page shell, DataTable, modal, guard)
- frontend/src/components/layout/Sidebar.tsx (Configuration group: showConfig/showManageUsers/showAuditLog and the /config links)
- frontend/src/app/(portal)/admin/users/page.tsx line ~76 (per-user grant toggles, adminOnly) + frontend/src/types/user.ts
- frontend/src/types/auth.ts + frontend/src/lib/me-cache.ts (MeResponse, useMe guard)

Before changing anything, state in one line each:
- Current behaviour: no API Access page or config API-key endpoints exist.
- Target behaviour: /config/api-access lists keys and issues/revokes them; four config endpoints gated on CanManageApiKeys.

Working branch: release/1.8.0. Create feature/v1.8.0-ppdo-86-api-access-page off release/1.8.0
and open the PR against release/1.8.0 (NOT main).

TDD: ConfigApiKeyFunctionsTests with failing tests first (gate 401/403, 201 create shape incl.
plaintextKey, 404/409 revoke, list never contains a hash), then implement.

Steps:
1. ConfigApiKeyFunctions — GET/POST config/api-keys, POST config/api-keys/{id:int}/revoke,
   GET config/api-keys/{id:int}/requests?page= (50/page). Gate CanManageApiKeysAsync. Handlers call
   the service only. Verify: dotnet test PPDO.Tests --filter ConfigApiKeyFunctions.
2. /auth/me gains canManageApiKeys. Verify: AuthService tests + curl /api/auth/me as SuperAdmin.
3. Frontend lib/api-keys.ts + types. Verify: npm run build.
4. Page /config/api-access per spec §6.1 — skeleton, empty copy, list columns, status pills, New key
   modal, key-issued modal (no backdrop/Esc close, Copy, "I have copied it"), revoke ConfirmDialog +
   toast, usage modal. Verify: run locally, issue a key, reload — full key never shown again.
5. Sidebar: API Access under Configuration when me.canManageApiKeys; the group's own visibility
   condition includes it. Verify: an Admin without the grant does not see it; direct URL redirects.
6. /admin/users: "Manage API keys" toggle (adminOnly) wired to OverrideCanManageApiKeys.
   Verify: grant a Staff user, re-login, page appears.

Risks / sign-off:
- Permission gate on new endpoints — sign-off before merge; a missing check is not a compile error.
- The plaintext key must appear ONLY in the POST response and the one-time modal: never in list DTOs,
  logs, toasts, audit rows, or localStorage.

Do NOT: add partner self-service, a "show key again", a rotate button that invalidates in place,
or any change to existing config pages' gates.

PR body: manual test plan = spec §10 lines for the page and user toggle.

When done, commit with:
feat(config): API Access page to issue, scope and revoke partner API keys (PPDO-86)
```

## Manual-coding candidate?

Not as a whole — it wires a new permission gate. Step 5 (sidebar item) alone is small enough, with the Consolidated AIP item as the sibling to copy.
