---
status: built
version: 1.1.0
built: release/1.8.0 — PPDO-15, PPDO-13, PPDO-14, PPDO-12 (PRs #328–#331); 1.1.0 — PPDO-102
spec: ../v1.8/External_AIP_API_Spec.md
supersedes: External_AIP_API_Contract.md §3, §4.2, §5 and §6.1 (v0.6)
---

# External AIP API — response schema & samples (1.1.0)

> ✅ **Built in v1.8.0** — merged to `release/1.8.0` (PRs #328–#331). ⚠️ **Not live yet:** it reaches
> production when v1.8.0 merges to `main`, and no partner key can be issued until the API Access page
> (PPDO-86) is merged. See §0.
>
> This is the payload returned for a **whole fiscal year** (`GET /api/external/v1/aip?fiscalYear=…`)
> or for **one office** (`…&officeCode=…`). **Both calls return the same shape** (§1). Keys, scope,
> rate limit and logging are defined by the build spec,
> [External_AIP_API_Spec.md](../v1.8/External_AIP_API_Spec.md); this folder is authoritative for the
> response shape. Prepared 2026-09-14 for the GSO + MIS meeting — **§5 also lists the
> hosting-migration and SSO questions for MIS.**
>
> ⚠️ **All sample data is fictional.** Office names and reference codes follow the real layout, but
> every amount, activity, and item is invented. This repository is public.

| File | What it is |
|---|---|
| [aip-response.schema.json](aip-response.schema.json) | JSON Schema (Draft 2020-12) — the contract in machine-checkable form |
| [aip-response.sample.fy2028.json](aip-response.sample.fy2028.json) | FY2028+, **one office** (`officeCode=PPDO`): expenditure lines, procurement items, printed figures |
| [aip-response.sample.fy2028.all-offices.json](aip-response.sample.fy2028.all-offices.json) | FY2028+, **whole fiscal year**: two released offices, one still pending, province totals |
| [aip-response.sample.fy2027-legacy.json](aip-response.sample.fy2027-legacy.json) | Legacy AIP (FY2027 and earlier): money on the activity, no line detail |

---

## 0. Build status

| Piece | Ticket | State |
|---|---|---|
| Key storage, issuing and revoking service, `CanManageApiKeys` grant — ⚠️ migration `AddPartnerApiKeys` | PPDO-15 | ✅ Merged (#328) |
| `X-Api-Key` check, office scope, 60 requests/minute per key, request log | PPDO-13 | ✅ Merged (#329) |
| Read service producing this schema | PPDO-14 | ✅ Merged (#330) |
| Endpoints + schema contract test (`ExternalAipSchemaContractTests`) | PPDO-12 | ✅ Merged (#331) |
| **1.1.0** — `totals` and `printedTotals` on every program and project | PPDO-102 | 🚧 In progress |
| Configuration → API Access page, to issue and revoke keys | PPDO-86 | ⏳ Not merged — until it is, no partner key can be issued |
| Production | — | ⏳ Ships with v1.8.0 → `main`; run the migration by hand (CI does not) |

---

### Schema changelog

| Version | Change |
|---|---|
| **1.1.0** | Adds `totals` and `printedTotals` to every `program` and `project`, the same shape the `group` already carries. A consumer no longer has to add up activities to show a program or project figure. `printedTotals` is the sum of the activities' printed figures — **never a rounding of `totals`**, because the form rounds each line up. Null for legacy AIPs. Additive only. |
| 1.0.0 | First built shape (PPDO-12, PPDO-14). |

---

## 1. Calls and shape

**One endpoint, one optional filter, one response shape** — all under `/api/external/v1`:

| Call | Returns | Key |
|---|---|---|
| `GET /aip?fiscalYear=2028` | Every released office for FY2028, plus the offices still pending | All-offices key, otherwise `403` |
| `GET /aip?fiscalYear=2028&officeCode=PPDO` | The same shape, holding just that office — `offices` has 0 or 1 entry | A key covering that office |
| `GET /aip/fiscal-years?officeCode=PPDO` | `{ "data": [2028, 2027] }` — years with a released AIP, newest first; same scope rule | As above |
| `GET /health` | Bare `{ "status": "ok", "timestamp": … }` — wakes the host, no database call | None |

Every data call sends `X-Api-Key: ppdo_<prefix>_<secret>`. Status codes and exact error messages are
in the build spec §3.1.

An office's entry is **identical** in both responses (the sample check asserts it), so a consumer
writes one parser and can switch between the two calls freely.

```
data
├── fiscalYear · aipFormat (Legacy | Fy2028) · officeCode (null = whole year)
├── totals · printedTotals · totalsByFundingSource[]      ← over the released offices
├── pendingOffices[]              ← have an AIP this year, not released yet
└── offices[]                     ← released, in AIP document order
    ├── office · status · releasedAt
    ├── totals · printedTotals · totalsByFundingSource[]
    └── groups[]                  ← one office-level AIP row (sector + name)
        └── programs[]
            └── projects[]
                └── activities[]  ← ALL money lives here
                    └── expenditures[]          (FY2028+)
                        └── procurementItems[]  (FY2028+)
```

`data` is `null` only when the fiscal year has no AIP at all. A year that exists but has nothing
released yet returns empty `offices` and a filled `pendingOffices` — so "not started" and "in
review" are distinguishable.

### 1a. Alternatives considered

| Option | How it works | Better when | Why it is not the default |
|---|---|---|---|
| **One shape, optional `officeCode`** ✅ | As above | GSO builds WFPs one office at a time and sometimes wants the whole year | — |
| Summary + detail | `?fiscalYear=` returns each office's status and totals only, no tree; the tree comes from `?officeCode=` | The whole-year response is too large, or GSO mostly needs totals | Two shapes to parse, and ~30 calls to assemble a full year |
| Flat rows | The whole-year call returns one row per expenditure line, with office, sector, program, project and activity repeated as columns | GSO loads everything into its own database or a spreadsheet | Loses the tree, repeats every name on every row, and is a second format to maintain |
| Change feed | `?releasedSince=<time>` returns only offices released after that time | GSO polls often to stay in sync | An addition rather than a replacement — `releasedAt` already prepares for it, and adding it later breaks nothing |

Summary + detail and flat rows are only worth their cost if the meeting answers open questions 9 and
12 that way. Pick one then, not in advance.

---

## 2. Decisions baked in — and why

| # | Decision | Why |
|---|---|---|
| 1 | **Money is a decimal string** — `"1250000.00"` | A JSON number becomes a binary float in JavaScript and many other stacks, and sums drift by centavos. A string makes the consumer choose a decimal type on purpose. **Changed from v0.6**, which used numbers |
| 2 | **Pesos everywhere, never thousands** | AIP amounts are now *stored* in pesos (DECISION E, v1.8.0). The v0.6 note that "the API multiplies by 1000" is obsolete. There is no conversion left to get wrong |
| 3 | **Two sets of figures: `amounts` and `printedAmounts`** | `amounts` holds the base values as encoded. Use them for **ceilings and WFP limits**. `printedAmounts` (FY2028+ only) matches the Annex B form: MOOE and CO get **+30%** (DECISION G), and everything is **rounded up to the thousand**. Without both, GSO would see figures that disagree with the signed AIP and report a discrepancy |
| 4 | **`groups[]` replaces v0.6's `sectors[]`** | One sector can hold **several sub-office groups sharing the same ref code** (e.g. the Governor's Office – Housing / – Warden). A group is identified by `(sector.code, name)`, not by its ref code |
| 5 | **Money only on activities, with an `isSynthetic` flag** | Some uploaded (FY2027 and earlier) workbooks put money directly on a program or project row. The importer turns that into a stand-in activity — inside a stand-in project when the row was a program — that **reuses the row's own ref code and name** (RAL-108). So consumers read money in **one** place, and v0.6's optional program/project amounts are gone. **Always `false` from FY2028**, because those AIPs are entered, not uploaded |
| 6 | **FY2028+: funding source per expenditure line**; activity `fundingSource` is null | One fund per line is the redesign rule. It answers v0.6 open item 4: the account-level breakdown is now included |
| 7 | **`totalsByFundingSource` at the office level** | GSO can check General Fund MOOE+CO against the ceiling without walking the tree |
| 8 | **Snapshots, not live config** | Account titles, fund names, and item prices are what was recorded at entry, so renaming something in PPDO's settings does not rewrite a published AIP |
| 9 | **Not exposed:** internal ids, divisions, users, review comments, audit data | None of these appear on the AIP document. Internal ids change on every re-upload; ref codes do not |
| 10 | **`schemaVersion` + `aipFormat`** | Adding fields bumps the minor version. Shape changes go to `/v2`. `aipFormat` tells the consumer which half of the schema applies |
| 11 | **One shape for both calls; `officeCode` is an optional filter, not a second endpoint** | One parser for GSO, one handler to secure and test for PPDO. A single office is just a filtered fiscal year |
| 12 | **`status` and `releasedAt` live on each office**, with `pendingOffices` at the top | In FY2028 each office is accepted separately, so a whole-year response is partial for most of the review season. Without `pendingOffices`, a partial year looks complete |
| 13 | **Whole-year call needs a key authorised for every office — otherwise `403`** | Quietly returning only the offices a key covers would look like the complete provincial AIP when it is not |
| 14 | **Province totals are over released offices only** | Said in the schema, so nobody quotes a mid-season total as the provincial AIP |

---

## 3. Rules for the consuming system

1. **Ignore fields you do not recognise.** New fields arrive in minor versions.
2. **Parse money as a decimal, never a float.**
3. **Do not infer a node's level from its ref code length.** 82 rows in the FY2027 file disagree
   with their own code depth. The level is where the node sits in the tree. **Nor treat `refCode`
   as unique** — in legacy data a synthetic project or activity carries its parent's code exactly.
4. **Do not assume `printedAmounts = 1.3 × amounts`.** Rounding breaks the ratio (₱1,000,400 base
   prints as ₱1,301,000, not ₱1,300,520).
5. **Limits use `amounts`, never `printedAmounts`.**
6. **`status` is not legal approval.** See open question 1.
7. **A whole-year response is complete only when `pendingOffices` is empty.**
8. **To stay in sync, re-fetch offices whose `releasedAt` is newer than your last pull** rather than
   re-importing the whole year.

---

## 4. Open questions for the meeting

> **The build uses each proposed default below** (spec §2 decision 1). None is answered by GSO yet,
> and each is a one-place change if the meeting decides otherwise.

| # | Question | Proposed default |
|---|---|---|
| 1 | **When is an AIP released to the API?** `Consolidated` means PPDO accepted it. It does **not** mean the PDC endorsed it or the Sangguniang Panlalawigan approved it (on or before June 7), and this system records neither step | Release at `Consolidated`, and state plainly that this is pre-approval. If GSO needs the approved version, add a manual "SP-approved" flag later |
| 2 | An office with two groups where only one is `Consolidated` | Return **nothing** for that office until every group is consolidated — never a partial AIP |
| 3 | Does GSO want `printedAmounts` at all? | Keep them. ⚠️ The rounding order (tracker G5) is still provisional, so these figures could move by ₱1,000 per line before FY2028 prints |
| 4 | Are procurement items and `stockCardNo` useful to GSO (supply side)? | Include — they are the closest link to PPMP/PR |
| 5 | Can their stack read **string** money? (PGOM Connect / WFP system language?) | Yes, with a documented decimal parse |
| 6 | Nested tree (this draft) or flat activity list? *(carried from v0.6)* | Nested — it matches the AIP document |
| 7 | Auth header, per-key office scope, staging environment *(carried from v0.6 §9)* | **Built:** `X-Api-Key`, scoped to a list of offices or all offices, behind a swappable credential check — if MIS's SSO offers machine-to-machine credentials (§5.2 Q4) it can replace the key without touching the endpoints. Staging environment still open |
| 8 | **If MIS hosts the system:** does the base URL and cold-start note in the contract still apply? | Revisit after the migration decision (§5.1); the payload shape does not change |
| 9 | **What will GSO actually do with the whole-year call** — load everything into their own database, or build one office's WFP at a time? | Decides whether the alternatives in §1a are needed. Default: the single shape as drafted |
| 10 | Should `pendingOffices` say *where* each office is in review (with the office, returned, at PPDO)? | Code and name only. Review progress is internal |
| 11 | How often will they poll, and do they want a "released since" filter? | Start with `releasedAt` in the payload; add `?releasedSince=` if they poll often |
| 12 | Does the whole-year call need procurement items, or would activity and line totals do? | Include them; revisit if the response is too large (§6) |

---

## 5. Migration and SSO — questions for MIS (same meeting)

> MIS has offered to host the database, and possibly the backend and frontend too. They are also
> building an SSO that the portal may connect to later, and which may manage permissions as well.
> **The details are not known yet**, so this section is a list of things to find out. Each question
> says why it matters and what we would pick if nobody has a strong view.

### 5.1 Hosting and database migration

| # | Question | Why it matters | Proposed default |
|---|---|---|---|
| 1 | **Which database do they already run, back up and patch?** | The engine their staff know matters more than the engine's features. Moving to PostgreSQL is doable but not free: date defaults, duplicate-record detection and some raw SQL need rewriting, and PostgreSQL treats upper/lower case differently, so **searches can quietly miss results** | Stay on SQL Server unless MIS runs PostgreSQL |
| 2 | Where will it run (their server room or a cloud), and who restores it if it fails? Has a restore ever been tested? | A backup nobody has restored is not a backup | Ask for a restore test before cutover |
| 3 | **Will other systems read or write the portal's database directly?** | Every schema change could break their system, and a direct query skips the office/division rules — any office could read any office's AIP | **No direct access.** Use the API; at most read-only views with a read-only login |
| 4 | Backend and frontend too, or database only? | Azure Functions would become a normal ASP.NET Core Web API (most code carries over; the cold-start delay disappears). The frontend needs a Node host or static export, and the automatic deploy pipeline must be rebuilt | Decide per piece; database first |
| 5 | Who deploys releases and who has production access afterwards? | Database migrations are run by hand today; someone on their side must know the release steps | Written runbook + one named person on each side |
| 6 | Cutover plan: downtime window, rollback, a period running both? | The AIP deadline is statutory (PDC in May, Sanggunian by June 7) | **Not during v1.8.0** (its release rewrites AIP amounts), and never in April–June |

### 5.2 The SSO itself

| # | Question | Why it matters | Proposed default |
|---|---|---|---|
| 1 | **Is it built on OpenID Connect (OIDC)**, or on a product such as Keycloak or Microsoft Entra ID? | A standard is supported out of the box by .NET and by every other system they connect. A home-made login protocol spreads its security bugs to every system that uses it | Ask them to use OIDC |
| 2 | **What does a login token say about the user?** | The portal must link each SSO user to its own account by a **permanent ID** that never changes. Email and username change and get reused — linking on them can hand one person another's account | Link on the SSO's permanent user ID |
| 3 | How are portal accounts created: pre-created and linked on first login, or created automatically? | Automatic creation must not give access by itself | Auto-create with **no access** until an admin assigns office and role |
| 4 | **Does it support system-to-system logins** (OAuth2 "client credentials")? | GSO's system could then call the AIP API with an SSO credential instead of a PPDO API key | Use it if available; otherwise the API key in the contract |
| 5 | **What happens when the SSO is down?** | The whole portal would be locked out | Keep **one local SuperAdmin login** as a break-glass account |
| 6 | Logout: does logging out of the portal log out of the SSO, and vice versa? | On shared office computers, a session left open is someone else's access | Logout ends both |
| 7 | Is there a test environment, and a timeline? | The portal can't be tested against production logins | A test SSO before any code is written |

### 5.3 If the SSO also manages permissions

Today the portal decides access with **three roles** (SuperAdmin / Admin / Staff), an **office**, a
**division**, **11 per-user permission switches**, and a cross-office review rule. Every combination
is written down in [Permission_Matrix.md](../v1.8/Permission_Matrix.md) and checked by automated
tests. Moving that elsewhere is the riskiest part of an SSO. The options are:

| Option | What the SSO decides | What the portal decides | Trade-off |
|---|---|---|---|
| **A** ✅ *proposed* | **Whether** a person may open the PPDO Portal (and possibly their role) | Office, division, feature permissions, review rights | MIS controls who gets in; PPDO keeps control of what they see. Permission rules stay covered by the existing tests |
| B | Everything, sent inside the login token | Only reads what the token says | One place to manage people, but every PPDO permission change needs the SSO team, a change only takes effect at next login, and the portal's tests can no longer confirm who sees what. A wrong value in the token can open another office's data |
| C | Nothing — login only | Everything, as today | Simplest, but MIS cannot remove someone's access centrally |

Questions to ask:

1. **How detailed are the planned permissions** — "can open app X", or feature-level switches inside each app?
2. **Who administers them** — MIS staff, or each office's own admin?
3. **When someone is removed, how fast does it take effect?** (Seconds, or when their login expires?)
4. Is there a record of who changed whose permission, and when?

**One rule regardless of the option chosen:** if a permission value is missing or unrecognised, the
portal gives **no access**. It must never fall back to "sees everything" — the portal already had
that bug once, with an empty office field (DECISION F, RAL-258).

### 5.4 Suggested order

1. Ship v1.8.0.
2. Move hosting/database; confirm the portal behaves exactly as before.
3. Add SSO login **alongside** passwords; link accounts.
4. Turn off password login, except the break-glass account.
5. Only then move permissions (if Option A or B is chosen), one at a time.

Doing two of these at once means that when something breaks, nobody can tell which change caused it.

**Until then:** avoid building new features around the portal's own password login, and don't lock
the external API into static API keys.

---

## 6. Settled during the build, and still to check

**Settled in PPDO-14:**

- **`releasedAt`** is the latest `ACCEPT_PPD` acceptance in `audit_log` for the office's groups, read
  in one batched query, so it survives a return-and-resubmit.
- **Legacy AIP office rows with no office link** (`AipOffice.OfficeId` null) are **excluded** — they
  have no office code to address them by.
- **FY2028+ activity amounts** are summed from the expenditure lines; `printedAmounts` come from
  `AipPrintedFigures`, the same source as the Excel export, never re-derived.
- **Queries** are set-based (one per tree level, across all released offices), never a loop per
  office.

**Still to check before production:**

- **Whole-year response size and compression.** FY2027 alone has ~2,900 AIP rows; FY2028 adds
  expenditure lines and procurement items. Measure the response against real data, and confirm the
  Functions host gzips it when the caller sends `Accept-Encoding: gzip`
  ([PERFORMANCE_GUIDELINES.md](../PERFORMANCE_GUIDELINES.md)). Page by office only if it is too large.
- **Legacy FY2027 rows with blank or `"None"` ref codes** (4 rows carrying ₱29.35M in the province's
  file). Confirm what ref code the RAL-238 importer gives them — the schema's `refCode` pattern
  rejects a blank.
- **CC typology codes** are still a free-text column on the activity. The schema's array assumes
  the comma-split planned in the join-table backfill.

---

## 7. Validating the samples

```bash
python -m pip install jsonschema
python -c "import json,jsonschema;s=json.load(open('docs/external-api/aip-response.schema.json'));[jsonschema.Draft202012Validator(s,format_checker=jsonschema.FormatChecker()).validate(json.load(open(f'docs/external-api/aip-response.sample.{n}.json'))) for n in ('fy2028','fy2028.all-offices','fy2027-legacy')];print('ok')"
```

The build asserts the same schema against real output: `ExternalAipSchemaContractTests` (PPDO-12)
validates a response from the read service — Fy2028, Legacy and `data: null` — exactly as the
endpoint serializes it. ⚠️ If this schema changes, run the backend tests: the contract test is what
catches the code and this file drifting apart.
