# PGOM Database Sharing: Impact Study & Meeting Brief

> **Status:** Analysis and options. No decision yet and no code changed.
> **Date:** 2026-09-27
> **For:** Notice of Meeting 2026-136. Capitol Training Center, **Oct 6, 2026, 1:00–5:00 PM**.
> **Related:** Memorandum PGO-2026-67 (iDTMS / IOKSI, in effect since 2026-09-01) ·
> `docs/External_AIP_API_Contract.md` (draft v0.6)

---

## 1. Why this matters

The meeting agenda has three items that bear directly on the PPDO Portal:

| # | Agenda item | What it could mean for us |
|---|---|---|
| 1 | Memo PGO-2026-67: iDTMS, **IOKSI as the official gateway and SSO**, Governor Portal, EMS | The portal may have to be **registered with MIS** and may be expected to log in through **IOKSI SSO**. Any document tracking the portal does must not stand in for iDTMS. |
| 2 | **Structure and design of the PGOM database** | Two possibilities: a shared provincial database that systems read from or write to, **or** shared master data (offices, employees) that every system has to align with. |
| 3 | **Hosting** of PGOM systems | Our system runs on Azure (Southeast Asia), paid out of a personal subscription. PGOM may want systems hosted on-premises or in a hosting setup it controls. |
| 4 | **System-development policies** | New rules (ICT standards, registration, code custody) will apply to how we release. |
| 5a | **PIA per the Data Privacy Act** | The portal holds personal data. We will likely be asked for a PIA. |

The memo's own wording is *"coordinate with MIS for system registration including identity
licensing, interoperability, technical integration."* That reads as **interoperability**,
not as a mandate that every system use one physical database. Clarifying which one they
mean is the most important question to settle at the meeting (§7).

---

## 2. What we would be sharing: data inventory

Azure SQL `ppdo-portal-db` (Basic tier, 5 DTU, Southeast Asia). There are 37 entities and 40
migrations, and EF Core owns the schema.

| Area | Tables (entities) | Sensitivity | Worth sharing? |
|---|---|---|---|
| **Identity** | `Users` (FullName, Username, Email, ContactNo, Position, Office/Division, **PasswordHash**, **RefreshToken**, permission overrides) | **High.** Personal data plus credentials | ❌ Never raw. At most, a directory projection without credentials |
| **Audit** | `AuditLog` (OldValues/NewValues JSON snapshots, ChangedById) | **High.** The snapshots can contain personal data | ❌ No. Share by request only (IAS) |
| **Reference / master** | `Office`, `Division`, `FundingSource`, `Account` (chart of accounts), `PriceIndexItem`, `ProcurementPreset*` | Low | ✅ Good candidates. These may be **PGOM-wide master data** already held somewhere else |
| **Planning** | `LdipRecord/Office/Program`, `AipRecord/Office/Program/Project/Activity`, `WfpRecord/Activity/Expenditure*`, `BudgetCeiling`, `DivisionAllocation`, ledger | Medium. Finalized AIPs are effectively public documents; drafts are not | ✅ **Final AIP** (already drafted for GSO), WFP/PPMP summaries |
| **Inventory** | `PurchaseRequest`, `PRItem`, `Delivery*`, `Distribution`, `ItemMaster`, `StockBalance` | Medium | ⚠️ Only if GSO/Accounting ask for it |
| **Content** | `Announcement`, `CalendarEvent`, `ResourceLink` | Low | ⚠️ Announcements are already public (`GET /api/announcements`) |

---

## 3. Options for sharing

### Option A: API-first (other systems call our versioned endpoints)
Other systems call `/api/external/v1/...` with an issued API key (or an IOKSI service token).
This is the pattern already drafted for GSO in `External_AIP_API_Contract.md`.

- **Pros:** We keep control of the schema, so EF migrations can keep changing tables without
  breaking consumers. Access is scoped per consumer and per office. Every read can be audited.
  Draft data is never exposed. Unit conversions (AIP ×1000) happen in one place. Hosting-neutral.
- **Cons:** We have to build and maintain every endpoint. Consumers depend on our uptime and cold
  starts (Consumption plan, ~5–30 s the first time). With 5 DTU, bulk pulls need pagination or
  scheduling.
- **Effort:** Medium. Phase 2 of the AIP contract is largely this work.

### Option B: Read-only SQL access (a login restricted to published views)
MIS or other systems get a SQL login that can `SELECT` only from views in a dedicated `share`
schema (for example `share.v_offices`, `share.v_aip_final_activities`).

- **Pros:** Fastest for BI and reporting tools. No endpoint to maintain per request.
- **Cons:** **The views become a frozen contract**, so every migration has to preserve them.
  Adds load on a 5 DTU database: a heavy consumer query can slow the portal for everyone.
  Opens the Azure SQL firewall to outside IPs. Harder to audit who read what. Credentials are
  handed to other teams.
- **Effort:** Low to build, but ongoing effort for every migration.

### Option C: Shared physical database (co-hosted with other PGOM systems)
Our tables move into a PGOM-wide database, or other systems' tables move into ours.

- **Pros:** One place for backups and hosting. Joins across systems are possible.
- **Cons:** **Highest risk.** Our EF migrations assume we own the schema. Everything is in `dbo`
  with legacy PascalCase names (`Users`, `CalendarEvents`), which is very likely to collide with
  names in other systems. Credentials (`Users.PasswordHash`, `Users.RefreshToken`) would be
  readable by any DBA or account with access to the shared database. Performance and outages are
  shared. It would also likely force a move off Azure SQL (see §5).
- **Effort:** High. A migration project, not a ticket.

### Option D: We consume PGOM master data (the reverse direction)
If PGOM builds a central directory of offices, employees and chart of accounts, we **sync from
it** instead of maintaining our own `Office`/`Account` seeds.

- **Pros:** Removes duplicate master data. Office codes line up with iDTMS and the Governor Portal.
- **Cons:** We depend on their data quality and on a stable key. It needs a mapping table from
  our `OfficeId` to the PGOM office id.
- **Effort:** Medium. A sync job plus a mapping table.

These options can be combined. **A (outbound) + D (inbound master data)** is the lowest-risk
combination. **B** is the fallback if MIS insists on direct SQL access. **C** is the option to
push back on unless PGOM is also taking over hosting.

| | A: API | B: RO views | C: Shared DB | D: Consume master data |
|---|---|---|---|---|
| Schema freedom | ✅ | ⚠️ views frozen | ❌ | ✅ |
| Credential exposure | ✅ none | ⚠️ SQL login | ❌ hashes + tokens | ✅ none |
| Load on 5 DTU DB | ⚠️ controllable | ❌ uncontrolled | ❌ shared | ✅ small sync |
| Auditability | ✅ per request | ⚠️ SQL audit only | ❌ | ✅ |
| Build effort | Medium | Low | High | Medium |

---

## 4. Changes needed, by option

### Needed under **any** option (do these regardless)
1. **Store refresh tokens hashed.** `Users.RefreshToken` is stored as plaintext
   (`AuthService.cs:96`). Anyone with DB read access could hijack a session. Store a SHA-256 hash
   and look up by hash. *(Required before Option B or C; good hygiene for A.)* ⚠️ MIGRATION
   (the column holds a hash; existing sessions are invalidated once).
2. **Stop leaking exception text from `/api/health`.** `HealthFunctions.cs` returns `error =
   ex.Message` anonymously. That can expose server or DB names to the public. Log the exception
   and return only `database: "unavailable"`.
3. **Stable office codes.** Confirm our `Office.OfficeCode` values match the codes PGOM, iDTMS and
   IOKSI use. If they don't, add an `external_office_code` column (snake_case, per
   `NAMING_CONVENTIONS.md`). ⚠️ MIGRATION
4. **PIA documentation.** A data inventory (§2), purpose, retention, who has access, the
   processor (Microsoft Azure, SEA region), and the breach process. Also needed: a privacy notice
   on the login page.

### Option A (API): extends the existing AIP contract
- API-key infrastructure (issue, hash, scope, revoke, rate limit) as planned in the contract §10.
- Generalize the contract to `/api/external/v1/*` so the AIP is one resource among several
  (offices, and later WFP/PPMP summaries).
- Log external reads to `AuditLog` (or a separate `external_access_log`) for IAS.
- Consider the Functions **Flex Consumption / always-ready** option, or a warm-up schedule, so
  cold starts don't hit consumer SLAs.

### Option B (RO views): additionally
- A `share` schema plus views, created through an EF migration (raw SQL), and a DB role with
  `SELECT` on `share` only. ⚠️ MIGRATION
- A CI check (or test) that fails if a migration breaks a `share.*` view.
- Azure SQL firewall rules for MIS IPs. Evaluate going up from **Basic 5 DTU** to S0/S1.

### Option C (shared DB): additionally
- Move all our tables into a dedicated schema (`ppdo.*`) and set `HasDefaultSchema("ppdo")`.
  This touches every migration going forward and needs a data move. ⚠️ MIGRATION, high risk.
- If the host isn't Azure SQL: re-test the EF provider, retry policy and connection string.
  Compatibility risk if it's not SQL Server.
- Agree who runs migrations on a database we don't own.

### Option D (consume master data): additionally
- A timer-triggered Function to sync offices (and later employees) into our tables, with a
  mapping table. Deactivated upstream records become `IsActive = false`; never hard-delete.

### IOKSI SSO (from Memo PGO-2026-67 §2): separate from database sharing
- Today: our own login (username/password, BCrypt, JWT, httpOnly refresh cookie).
- Likely target: add IOKSI as an **OpenID Connect / OAuth2 identity provider** alongside local
  login. Match on email or employee ID, and **keep roles and division scope in the portal**
  (`PermissionService` stays the source of authorization).
- Unknowns to ask about: protocol (OIDC? SAML? custom?), which claims come through (employee
  ID? office?), a test tenant, and whether local login must be switched off.
- This is a **spec-level feature** (auth + a new endpoint). Write it through
  `SPEC_STANDARD.md` before any code.

---

## 5. Hosting impact (agenda item 3)

| If PGOM requires… | Impact |
|---|---|
| Registration only, stay on Azure | None beyond documentation. **Best case.** |
| Ownership moved to a PGOM Azure tenant/subscription | Move resources (SWA, Functions, SQL, Storage, App Insights), and redo the publish profile and CI secrets in `deploy.yml`. Moderate effort, low risk. |
| On-premises / PGOM data center | Big change. Functions → self-hosted Functions runtime or ASP.NET host; Azure SQL → SQL Server on-prem; SWA → IIS/nginx for the Next.js static export; our own TLS, backups and uptime. The RAL-237 lesson applies: **keep the API next to the DB.** |
| Government cloud (e.g. DICT GovCloud) | Similar to on-prem; depends on the services offered. |

Points to raise: the current cost (Azure SQL Basic ≈ US$5/mo plus Functions Consumption and SWA Free), who pays after handover, and who holds
the admin credentials (currently one developer, which is a continuity risk).

---

## 6. Data privacy / PIA (agenda item 5a)

- **Personal data held:** employee full name, username, email, contact number, position,
  office/division. Audit snapshots may repeat these. No sensitive personal information (as
  defined under the DPA) that we know of.
- **Safeguards already in place:** BCrypt passwords, short-lived JWT (15 min), httpOnly/Secure/
  SameSite refresh cookie, login rate limiting, CORS allowlist, role and division scoping,
  audit trail.
- **Gaps to close:** plaintext refresh tokens (§4.1), the health endpoint error leak (§4.2), no
  written retention policy, no privacy notice, no documented breach process, a single admin
  account holder.
- Sharing personal data with another system (Options B and C) **widens the PIA scope**. Each
  consumer then needs its own lawful basis and a data-sharing agreement. Options A and D avoid
  this by exposing only non-personal data.

---

## 7. Questions to ask at the meeting

1. By "PGOM Database", does MIS mean **one physical database**, or a **common data standard and
   master data** (office codes, employee IDs)?
2. Is there, or will there be, a **central office/employee registry**? Which key is authoritative?
3. **IOKSI SSO:** protocol, claims, a test environment, a timeline, and whether local login must
   be retired.
4. **Hosting:** is Azure acceptable if registered, or is migration to PGOM-controlled hosting
   required? Who funds it?
5. **Development policy:** code custody (repo ownership), required documentation, and a
   change-approval process before a production release.
6. **PIA:** template, deadline, and who signs as PIC/DPO.
7. Does iDTMS need anything **from** our system (for example PR numbers, delivery refs), or is
   it separate from our inventory records?

## 8. Suggested position (for discussion; not decided)

> *"The PPDO Portal can interoperate through versioned, authenticated APIs. One read endpoint
> contract (Final AIP) is already drafted, and a public health endpoint is live. We're prepared
> to adopt PGOM office codes and to integrate with IOKSI SSO once its specs are available. For
> security and stability we propose not to give direct database access. If required, we can
> expose read-only views of non-personal data."*

## 9. What we already have

- `GET /api/health`: anonymous liveness check for API and DB. Live.
- `GET /api/announcements`: public.
- `External_AIP_API_Contract.md`: draft v0.6, versioned `/api/external/v1`, API key, Final-only
  AIP data. **Not implemented yet (Phase 2).**
- A clean layered architecture (Functions → Application → Infrastructure), so new external
  endpoints are additive and don't need a rewrite.

## 10. After the meeting

- Record the decisions in this file (add a "Decisions" section) and update the AIP contract to
  reflect them.
- Turn the chosen option(s) into specs (`SPEC_STANDARD.md`) and Linear tickets. §4 items 1–2
  are small, spec-free hardening tickets and can go into the next patch whatever is decided.
