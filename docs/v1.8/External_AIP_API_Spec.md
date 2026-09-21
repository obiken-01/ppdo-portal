---
status: draft
version: v1.8.0
tickets: PPDO-16 (epic), PPDO-15, PPDO-13, PPDO-14, PPDO-12, API Access page ticket
supersedes: External_AIP_API_Contract.md §4.2, §5, §6.1 (with docs/external-api/)
---

# External AIP API + partner API keys — build spec

> **Phase 5, step 2** of the v1.8.0 plan (epic **PPDO-83**, sub-epic **PPDO-16**). Prepared
> 2026-09-14 so implementation can start the next day. Nothing here is built yet.
>
> **Three documents, three jobs:**
> - **This spec** — who may call what, how keys are issued and checked, what is logged, the screens,
>   the tickets. Authoritative for everything except the payload.
> - **[`docs/external-api/`](../external-api/README.md)** (draft 1.0.0) — the **payload**: JSON Schema,
>   samples, and the open questions for GSO and MIS. Authoritative for the response shape.
> - **[`External_AIP_API_Contract.md`](../External_AIP_API_Contract.md)** (v0.6) — base URL, health
>   endpoint and status codes still stand; §4.2, §5 and §6.1 are superseded.

---

## 1. Goal

Other provincial systems — GSO first, PBO and others later — read a fiscal year's AIP from the
portal server-to-server, instead of re-typing it from the printed form. PPDO decides which systems
may read, which offices each may read, and can cut one off at any time from a new **Configuration →
API Access** page. The figures they receive are the same ones the Consolidated AIP page and the
Annex B Excel show, so no two copies of the AIP disagree.

---

## 2. Decisions (settled 2026-09-14)

1. **The payload is `docs/external-api/` draft 1.0.0** — `aip-response.schema.json` is the contract,
   the samples are the examples. The README's §4 questions for GSO are **not answered yet**; the build
   uses each question's proposed default, and every one is a one-place change later:
   - **Released = `Consolidated`** (PPDO accepted it). The response says plainly this is before PDC
     endorsement and SP approval. *(README §4 Q1)*
   - **An office appears only when every one of its groups is `Consolidated`**; until then it is in
     `pendingOffices`. Never a partial office. *(Q2)*
   - **`printedAmounts` included** for FY2028+, from `AipPrintedFigures` — the Excel's source, never
     re-derived. *(Q3)*
   - Money as **decimal strings**, pesos; nested tree; procurement items included. *(Q4–Q6, Q12)*
   - `pendingOffices` carries code and name only. *(Q10)*
2. **FY2027 and earlier are exposed as `aipFormat: "Legacy"`** when the record's status is `Final`
   (the v0.6 rule, which those years still follow). FY2028+ uses the workflow rule in decision 1.
3. **Authentication is an API key in `X-Api-Key`, behind one interface.** The endpoints ask
   `IPartnerCredentialValidator` who is calling; the key is today's only implementation. If MIS's SSO
   offers machine-to-machine credentials (README §5.2 Q4), a second implementation replaces it
   without touching an endpoint. *(Ralph: "API key, swappable".)*
4. **Keys are issued by PPDO, not requested by partners.** One key belongs to one partner system (a
   name, e.g. "GSO WFP system") and carries a scope: **a list of offices, or all offices**. Optional
   expiry. *(Ralph: "Admin issues keys".)*
5. **Key format `ppdo_<prefix>_<secret>`** — an 8-character public prefix for lookup and display, a
   32-byte random secret (base64url). **Only a SHA-256 hash of the whole key is stored**; the plaintext
   is returned once, in the create response, and never again. SHA-256 rather than BCrypt: the key is
   high-entropy, so a slow hash adds nothing against guessing and costs every request.
6. **Rotation is issue-then-revoke.** No "rotate" button that invalidates a key a partner is still
   using: the admin issues a second key for the same partner, the partner switches, the admin revokes
   the old one. Revocation takes effect on the next request.
7. **Who manages keys: SuperAdmin, plus a new per-user grant `CanManageApiKeys`.** It follows the
   per-user chain in `Permission_Matrix.md` §2.4 — `SuperAdmin → true`, else `Override ?? false`;
   **Admin is not auto-granted**, and no division flag carries it. A key reads AIP data across offices
   from outside the portal; it should be a named person's job. *(Ralph: "SuperAdmin + new flag".)*
8. **The whole-year call needs an all-offices key — otherwise 403.** Quietly returning only the
   covered offices would look like the complete provincial AIP. *(README decision 13.)*
9. **Every external data request is logged in `partner_api_requests`, not `audit_log`.**
   `audit_log.changed_by_id` is a required user, and a partner system is not a user. Issuing and
   revoking a key **is** audited in `audit_log`, against the admin who did it.
10. **Per-key rate limit: 60 requests per minute**, fixed window in `IMemoryCache` (the RAL-58 login
    pattern) → `429` with `Retry-After`. Per Functions instance, which is acceptable at this volume.
11. **Routes live under `/api/external/v1`.** The health route needs no key and returns a bare
    `{ status, timestamp }`; data routes use the `{ data, error, message }` envelope. No CORS change —
    server-to-server only.
12. **Never exposed:** internal ids, divisions, users, review comments, audit data, workflow states
    other than released/pending *(README decision 9)*.
13. **Built with a handful of set-based queries** — one per tree level, for all released offices at
    once — never a loop per office. Measure the whole-year FY2027 response before merging.

### Open follow-ups (not blocking)

- **GSO's answers** to README §4 — each default above is isolated to one method.
- **MIS SSO** (README §5) — a second `IPartnerCredentialValidator`, when and if.
- **Tracker G5** — moves `printedAmounts` by at most ₱1,000 per figure.
- **An "SP-approved" flag** if GSO needs the approved AIP rather than the accepted one.
- **`?releasedSince=`** if GSO polls often.
- **Retention of `partner_api_requests`** — no purge yet; revisit at 100k rows.
- **Legacy AIP groups with no office link** (`AipOffice.OfficeId` null) — excluded, since they have
  no office code to address them by. Count them in the measurement step.

---

## 3. Behaviour

### 3.1 External data calls

| Case | Given | When | Then |
|---|---|---|---|
| One office, released | FY2028; PPDO's two groups both `Consolidated`; key scoped to PPDO | `GET …/aip?fiscalYear=2028&officeCode=PPDO` | `200`, `offices` = [PPDO] with `groups[]`, `amounts` and `printedAmounts`; `officeCode: "PPDO"` |
| One office, partly accepted | One PPDO group `Consolidated`, the other `SubmittedToPpdo` | Same call | `200`, `offices: []`, `pendingOffices: [PPDO]` — never half an office |
| One office, not started | PPDO has groups, none released | Same call | `200`, `offices: []`, `pendingOffices: [PPDO]` |
| Office with no AIP this year | Record exists; no PPDO group | Same call | `200`, `offices: []`, `pendingOffices: []` |
| Year not opened | No AIP record for FY2030 | `…?fiscalYear=2030` | `200`, `data: null` |
| Whole year | All-offices key; 3 offices released, 16 pending | `GET …/aip?fiscalYear=2028` | `200`, `officeCode: null`, 3 `offices`, 16 `pendingOffices`, totals over the 3 only |
| Whole year, scoped key | Key scoped to PPDO | `GET …/aip?fiscalYear=2028` | `403` `This key can read only some offices; request one office with officeCode.` |
| Out of scope | Key scoped to PPDO | `…&officeCode=PEO` | `403` `Office not authorized for this key.` |
| Unknown office | All-offices key | `…&officeCode=NOPE` | `400` `Unknown officeCode 'NOPE'.` (a scoped key gets the 403 above — an unknown code is never in scope) |
| Legacy year | FY2027 record `Final` | `…?fiscalYear=2027&officeCode=PGO` | `200`, `aipFormat: "Legacy"`, money on activities, no `printedAmounts`, no `expenditures` |
| Legacy draft | FY2027 record still `Draft` | Same | `200`, `data: null` |
| Sent back after release | PPDO `Consolidated`, then re-opened | Next call | PPDO moves to `pendingOffices`; `releasedAt` reappears as the **latest** acceptance when re-accepted |
| Fiscal years | All-offices key | `GET …/aip/fiscal-years` | `200`, years with at least one released office, newest first |
| Fiscal years, scoped | Key scoped to PPDO | `GET …/aip/fiscal-years?officeCode=PPDO` | Years in which PPDO is released; without `officeCode` → `403` as the whole-year case |
| Missing key | No header | Any data route | `401` `Invalid API key.` |
| Wrong / revoked / expired key | Header present | Any data route | `401` `Invalid API key.` — the same message for all three, so a caller cannot probe which |
| Inactive office in scope | Office deactivated after the key was issued | Call for it | `403` as out of scope |
| Rate limit | 61st request inside one minute | Any data route | `429` `Too many requests.`, `Retry-After: <seconds>`; not logged as a data pull |
| Bad parameter | `fiscalYear` missing or not a number | Data route | `400` `fiscalYear is required.` |
| Health | No key | `GET …/health` | `200` `{ "status": "ok", "timestamp": "…" }` |
| Server error | Database unreachable | Data route | `500` generic message, nothing internal |

Every data call that passes authentication — `200`, `400`, `403` — writes one `partner_api_requests`
row and sets the key's `last_used_at`. `401` and `429` write nothing (no key to attribute, or a
flood).

### 3.2 Managing keys

| Case | Given | When | Then |
|---|---|---|---|
| Issue, some offices | SuperAdmin | Creates "GSO WFP system", offices PPDO + PEO, no expiry | Key shown once in a dialog; list shows prefix, offices, **Active**; `audit_log` CREATE row |
| Issue, all offices | Holder of `CanManageApiKeys` | Creates with **All offices** | As above, scope reads "All offices" |
| No scope | Any manager | Neither all offices nor any office chosen | `400` `Choose at least one office, or all offices.` under the office picker |
| Past expiry | Any manager | Expiry before today (Manila) | `400` `Expiry must be a future date.` |
| Blank partner name | Any manager | Name empty or over 100 characters | `400` `Partner name is required (up to 100 characters).` |
| Lost key | Partner lost the plaintext | Admin looks for it | Not recoverable; issue a new key, revoke the old |
| Revoke | Active key | Admin confirms revoke | Status **Revoked**, next external call `401`; `audit_log` UPDATE row |
| Revoke again | Already revoked | Revoke | `409` `This key is already revoked.` |
| Expired | `expires_at` passed | List loads | Status **Expired** (computed); no action needed |
| Usage | Key with requests | Open its usage | Newest 50 requests: time, route, office, fiscal year, status |
| Two keys, one partner | Rotation in progress | Both active | Both work until one is revoked |

### 3.3 Per role — key management

| Account | API Access page | Config endpoints |
|---|---|---|
| SuperAdmin | ✅ | ✅ |
| Admin **without** the grant | Not in the sidebar; direct URL → redirected like other config pages | `403` |
| Admin **with** `CanManageApiKeys` | ✅ | ✅ |
| Staff with the grant (host or guest office) | ✅ — office and division are not read | ✅ |
| Staff without | Hidden / redirected | `403` |
| No token | Login | `401` |

⚠️ **`CanManageApiKeys` implies nothing else, and nothing implies it** — not `CanManageConfig`, not
`CanManageUsers`, not `CanReviewAllOffices`. Pinned by `PermissionMatrixTests` in both directions.

---

## 4. API contract

### 4.1 External — `/api/external/v1`

Payload shapes: `docs/external-api/aip-response.schema.json`. Errors are the envelope with `data: null`.

#### `GET /api/external/v1/health` — public

`200` `{ "status": "ok", "timestamp": "<ISO-8601 UTC>" }`. No database call — it wakes the Functions
host only (the internal `/api/health` already checks SQL).

#### `GET /api/external/v1/aip?fiscalYear=&officeCode=` — `X-Api-Key`

| Param | Required | Notes |
|---|---|---|
| `fiscalYear` | yes | integer |
| `officeCode` | no | omitted = whole year, needs an all-offices key |

`200` envelope with `data` per the schema, or `data: null` when the year has no AIP.
`400` · `401` · `403` · `429` · `500` as §3.1. Response compressed when the caller sends
`Accept-Encoding: gzip` (verify the Functions host does this; if not, note it — §2 decision 13).

#### `GET /api/external/v1/aip/fiscal-years?officeCode=` — `X-Api-Key`

`200` `{ "data": [2028, 2027], … }`. Same scope rules as `/aip`.

### 4.2 Configuration — JWT + `PermissionService.CanManageApiKeysAsync`

Envelope `ApiResponse<T>`. Routes follow `config/<resource>` (as `config/esre-codes`).

#### `GET /api/config/api-keys`

`200` `ApiKeyListItemDto[]`, newest first — **slim**, never the hash:

`id` · `partnerName` · `keyPrefix` · `allOffices` · `offices[] { code, name }` · `status`
(`Active` | `Expired` | `Revoked`) · `expiresAt?` · `lastUsedAt?` · `createdAt` · `createdByName` ·
`revokedAt?` · `revokedByName?`

#### `POST /api/config/api-keys`

Body `{ partnerName: string, allOffices: bool, officeIds: int[], expiresAt?: "yyyy-MM-dd" }`
(`officeIds` ignored when `allOffices`). `201` `{ key: ApiKeyListItemDto, plaintextKey: string }`.
`400` validation messages as §3.2.

#### `POST /api/config/api-keys/{id:int}/revoke`

`200` `ApiKeyListItemDto` · `404` `API key not found.` · `409` `This key is already revoked.`

#### `GET /api/config/api-keys/{id:int}/requests?page=1`

`200` `{ items: [{ requestedAt, route, officeCode?, fiscalYear?, statusCode }], total }`, 50 per page,
newest first · `404`.

### 4.3 `/auth/me`

Adds `canManageApiKeys: boolean` — the sidebar and the page guard read it from `me-cache`.

---

## 5. Data model — migration `AddPartnerApiKeys`

New tables are **snake_case**. `Users` is a legacy PascalCase table, so its new column is PascalCase.

### `partner_api_keys`

| Column | Type | Null | Notes |
|---|---|---|---|
| `id` | int identity | no | PK |
| `partner_name` | nvarchar(100) | no | |
| `key_prefix` | nvarchar(16) | no | **unique index** — the lookup |
| `key_hash` | nvarchar(64) | no | SHA-256, hex |
| `all_offices` | bit | no | |
| `expires_at` | datetime2 | yes | UTC; end of the chosen Manila day |
| `last_used_at` | datetime2 | yes | |
| `revoked_at` | datetime2 | yes | |
| `revoked_by_id` | uniqueidentifier | yes | FK `Users.Id` |
| `created_at` | datetime2 | no | |
| `created_by_id` | uniqueidentifier | no | FK `Users.Id` |

### `partner_api_key_offices`

`key_id` int FK `partner_api_keys.id` (cascade delete) · `office_id` int FK `offices.id` · **PK
(`key_id`, `office_id`)**. Empty when `all_offices`.

### `partner_api_requests`

| Column | Type | Null | Notes |
|---|---|---|---|
| `id` | bigint identity | no | PK |
| `key_id` | int | no | FK `partner_api_keys.id` |
| `requested_at` | datetime2 | no | |
| `route` | nvarchar(100) | no | e.g. `aip`, `aip/fiscal-years` |
| `office_code` | nvarchar(20) | yes | as requested |
| `fiscal_year` | int | yes | |
| `status_code` | int | no | |

Index `(key_id, requested_at DESC)` for the usage list.

### `Users.OverrideCanManageApiKeys`

`bit NULL` — the per-user grant. No backfill: `null` resolves to no access for everyone but SuperAdmin.

---

## 6. UI states

### 6.1 Configuration → API Access (`/config/api-access`)

Sidebar item under Configuration, shown when `me.canManageApiKeys`; the Configuration group's own
visibility condition gains that flag too. Page guard via `useMe` with the same rule.
Components: `DataTable`, `Modal`, `ConfirmDialog`, `useToast`.

| State | Content |
|---|---|
| **Loading** | Page header and **New API key** button render at once; table skeleton, 5 rows, same columns |
| **Empty** | "No partner system has an API key yet." + **New API key** |
| **Loaded** | Columns: Partner · Key (`ppdo_ab12cd34_…`) · Offices ("All offices" or up to three codes + "+N") · Status pill (Active green / Expired amber / Revoked slate) · Expires · Last used ("Never") · Created by · actions (**Usage**, **Revoke** — Revoke hidden when not Active) |
| **Error** | Red line "API keys could not be loaded." + **Try again** |
| **New key** | Modal: Partner name · **All offices** checkbox · office multi-select (disabled while All offices is ticked) · Expiry date (optional). **Issue key** |
| **Validation** | Messages under the field (§3.2); the modal stays open |
| **Key issued** | Second modal, cannot be dismissed by backdrop or Esc: the key in a monospace box with **Copy**, and "Copy this key now. It will not be shown again — if it is lost, issue a new one." Close button reads **I have copied it** |
| **Revoke** | `ConfirmDialog`: "Revoke the key for GSO WFP system? Their system will be refused on its next request." → toast "Key revoked." |
| **Usage** | Modal listing the newest 50 requests; empty: "This key has not been used yet." |
| **Forbidden** | Not in the sidebar; direct URL redirects like the other config pages |

### 6.2 User management (`/admin/users`)

One new toggle in the per-user grants block: **Manage API keys**, `adminOnly` like "Review All Offices".

---

## 7. Non-goals

- **Partner self-service or a request/approval flow** — keys are issued by PPDO (decision 4).
- **SSO or OAuth client credentials** — the interface allows it later; nothing is built for it.
- **Writes from partners**, webhooks, or a change feed (`releasedSince`).
- **Per-key rate limits, field filtering or IP allow-lists.**
- **A purge job** for `partner_api_requests`.
- **An "SP-approved" state** — the system records neither PDC endorsement nor SP approval.
- **Showing a key again**, or storing it encrypted so it could be — a lost key is replaced.
- **Changing the internal `/api/health`** or any existing endpoint's auth.

---

## 8. Deployment notes

- ⚠️ **Migration `AddPartnerApiKeys`** — run `dotnet ef database update` against Azure SQL by hand.
  **CI does not run migrations.**
- No new NuGet or npm package (`System.Security.Cryptography` is in the framework).
- No new app setting; no CORS change (server-to-server).
- After deploy: grant `CanManageApiKeys` to the named PPDO person, issue GSO's key from the page, and
  hand it over outside email and chat.
- `docs/v1.8/Permission_Matrix.md` §2.4 gains the fifth per-user grant in the same PR as the flag.

---

## 9. Ticket split

| # | Ticket | Covers | Blocked by |
|---|---|---|---|
| 1 | **PPDO-15** — Partner API keys: schema, issuing service, `CanManageApiKeys` | §5, decisions 4–7, §3.2 service rules, Permission Matrix row | — |
| 2 | **PPDO-13** — Key authentication, scope, rate limit, request log | Decisions 3, 8–10, §3.1 auth rows | #1 |
| 3 | **PPDO-14** — External AIP read service (schema 1.0.0) | Decisions 1, 2, 12, 13; §3.1 data rows | — (parallel with #1) |
| 4 | **PPDO-12** — External endpoints + schema contract test | §4.1, decision 11 | #2, #3 |
| 5 | **PPDO-86** — API Access config page + config endpoints | §4.2, §4.3, §6 | #1 |

---

## 10. Acceptance checklist

```
- [ ] As SuperAdmin, Configuration → API Access shows the page; issuing "GSO WFP system" for PPDO shows
      the key once, and the list shows it Active with prefix and "PPDO"
- [ ] Closing the key dialog and reopening the page never shows the full key again
- [ ] An Admin without "Manage API keys" does not see API Access, and /config/api-access redirects
- [ ] Granting "Manage API keys" to a Staff user on /admin/users gives them the page after re-login
- [ ] Issuing with no office and All offices unticked shows "Choose at least one office, or all offices."
- [ ] curl …/external/v1/health with no key returns {"status":"ok",…}
- [ ] curl …/external/v1/aip?fiscalYear=2028&officeCode=PPDO with the key returns PPDO when both its
      groups are accepted, and lists it under pendingOffices when one is not
- [ ] An activity with ₱1,000,400 MOOE shows "1000400.00" in amounts and "1301000.00" in printedAmounts
- [ ] The same key asking for officeCode=PEO gets 403; asking without officeCode gets 403
- [ ] An all-offices key without officeCode gets every released office and the rest under pendingOffices
- [ ] fiscalYear=2027 returns aipFormat "Legacy" for a Final FY2027 record
- [ ] A wrong key, a revoked key and an expired key each get 401 "Invalid API key."
- [ ] The 61st call in a minute gets 429 with Retry-After
- [ ] After revoking, the next call with that key gets 401, and the list shows Revoked
- [ ] The key's Usage shows each successful call with route, office and fiscal year
- [ ] audit_log has a row for issuing and for revoking, against the admin who did it
```

---

## 11. Test focus

| Class | Cover |
|---|---|
| `PartnerApiKeyServiceTests` | Issue returns plaintext once and stores only the hash; prefix unique; validation (no scope, past expiry, name); revoke and revoke-twice `409`; status computed Active/Expired/Revoked; audit rows on issue/revoke |
| `PermissionMatrixTests` / `PermissionServiceTests` | `CanManageApiKeys`: SuperAdmin ✅, Admin null ❌, Admin true ✅, Staff true ✅, Staff false ❌; independent of `CanManageConfig`, `CanManageUsers`, `CanReviewAllOffices` both ways |
| `ApiKeyCredentialValidatorTests` | Valid key; missing; malformed; wrong secret with a real prefix; revoked; expired — all `401` with one message; constant-time hash compare; `last_used_at` set |
| `PartnerApiScopeTests` | Scoped key + in-scope office ✅; out of scope `403`; whole-year with scoped key `403`; unknown code `400` for all-offices, `403` for scoped; inactive office |
| `PartnerRateLimiterTests` | 60 pass, 61st `429` with Retry-After; window resets; keys counted separately |
| `ExternalAipReadServiceTests` | Released = every group `Consolidated`; partial → pending; `amounts` pesos as strings; `printedAmounts` equal `AipPrintedFigures` (₱1,000,400 → `1301000.00`); FY2027 Legacy from `Final` only; groups keyed by sector + name; synthetic flags; `releasedAt` = latest `ACCEPT_PPD`; offices with no link excluded; query count constant with office count |
| `ExternalAipFunctionsTests` | Health public; `401`/`403`/`400`/`429` envelopes; request row written for `200`/`400`/`403` and not for `401`/`429` |
| Schema contract test | A real response from the read service validates against `aip-response.schema.json` (same as the README §7 check) |
| `ConfigApiKeyFunctionsTests` | Gate on `CanManageApiKeys`; `201` create shape; `404`/`409` revoke; list never contains a hash |
