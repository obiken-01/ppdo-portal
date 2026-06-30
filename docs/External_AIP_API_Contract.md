# External AIP API — Contract (DRAFT for discussion)

> **Status:** DRAFT v0.5 — proposed contract for review with GSO. Nothing is implemented yet.
> **Audience:** GSO development team (consumer) + PPDO Portal team (provider).
> **Purpose:** Let an authorized external system (GSO) **read finalized AIP records for an
> office** so it can build its own WFP. Read-only, server-to-server.
> **Date:** 2026-06-30

Items marked **(confirm)** are PPDO's proposed default and are open for GSO's input.

---

## 1. Overview

- **Direction:** GSO's backend calls the PPDO Portal API. One-directional **read**; GSO does
  not write anything back to PPDO.
- **Transport:** HTTPS only. Server-to-server (the API key must never be used from a browser).
- **Data returned:** only AIP records with status **`Final`** are exposed. Draft and Archived
  records are never returned.
- **Versioned:** the path is versioned (`/v1`) so the contract can evolve without breaking GSO.

---

## 2. Base URL

```
https://<ppdo-portal-domain>/api/external/v1
```

(The exact production host will be provided separately.)

---

## 3. Authentication

Every data request must include a PPDO-issued **API key** in a request header:

```
X-Api-Key: <the key PPDO issues to GSO>
```

- **PPDO issues the key together with the office code(s) GSO is authorized to read.** GSO does
  not need to discover offices — it already knows which `officeCode` value(s) to use.
- The key can be rotated or revoked by PPDO at any time.
- No username/password, no OAuth, no tokens to refresh — a single static key per consumer.
- Missing/invalid key → `401`. Valid key but office not in the key's scope → `403`.
- Exception: the **health endpoint** (§4.1) does not require the key.

> **(confirm)** Header name `X-Api-Key`. Alternative commonly used: `Authorization: Bearer <key>`.

---

## 4. Endpoints

### 4.1 Health check (availability / wake-up)

```
GET /api/external/v1/health
```

A lightweight liveness check with **no authentication required**. Two uses:

1. **Availability** — confirm the API is reachable before attempting a real request.
2. **Wake-up** — the portal runs on Azure Functions (Consumption plan), which **scales to zero
   after inactivity**. The first request after an idle period (a "cold start") can take
   **~20–30 seconds**. Calling this endpoint first warms the server, so the subsequent AIP fetch
   responds promptly. GSO may also poll it as a readiness check.

Returns `200` with a small body (intentionally **not** wrapped in the `{data,error,message}`
envelope — a health check stays minimal):

```json
{ "status": "ok", "timestamp": "2026-06-30T08:15:00Z" }
```

> Note: because of cold starts, treat a slow first response as normal, not a failure. We
> recommend a generous client timeout (e.g. 30–40s) on the first call after idle, then normal
> timeouts thereafter.

### 4.2 Get AIP for an office  ⟵ the main endpoint

```
GET /api/external/v1/aip?officeCode={code}&fiscalYear={year}
```

**Query parameters**

| Param | Required | Type | Description |
|-------|----------|------|-------------|
| `officeCode` | yes | string | The office to fetch, e.g. `PEO` — the code PPDO issued with the key. |
| `fiscalYear` | yes | integer | Fiscal year, e.g. `2027`. |

**Behaviour**

- Returns the **finalized** AIP for that office and fiscal year as the full PPA hierarchy —
  **sector → Program → Project → Activity** (an office can appear in 1–4 sectors) — with amounts
  on the leaf activities, the granularity needed to build a WFP.
- If no `Final` AIP exists for that office/year → `200` with `data: null` (not an error).
- Requires a valid `X-Api-Key` scoped to the requested `officeCode`.

### 4.3 List fiscal years (discovery)

```
GET /api/external/v1/aip/fiscal-years?officeCode={code}
```

Returns the fiscal years that have a `Final` AIP for the given office, so GSO can fetch the
right year without guessing. Requires the key. See [§6.2](#62-fiscal-years-response).

---

## 5. Money & units — please read

- All monetary amounts are returned in **Philippine pesos (PHP), as the full peso value**
  (e.g. `1500000` means ₱1,500,000.00).
- Amounts are JSON numbers (decimals). A missing component is returned as `0`.

> **Internal note (PPDO):** AIP amounts are stored internally in **thousands**; the API converts
> them to full pesos (×1000) before returning. GSO consumes pesos and need not know the internal
> representation. **This conversion must be locked in tests** — a silent 1000× error here would be
> severe.

---

## 6. Response structures

Data responses use the envelope:

```json
{ "data": <payload or null>, "error": <string or null>, "message": <string or null> }
```

(The health endpoint in §4.1 is the only exception — it returns a bare `{ status, timestamp }`.)

### 6.1 AIP response

`GET /api/external/v1/aip?officeCode=PEO&fiscalYear=2027`

The response preserves the full AIP **PPA hierarchy**:

```
office → sector → Program → Project → Activity (leaf, carries the amounts)
```

> ⚠️ **A single office can have AIP items in more than one sector** (e.g. the Provincial
> Governor's Office spans all four: General / Social / Economic / Others). Internally each
> office-and-sector pair is a separate grouping, so the response nests the hierarchy under a
> `sectors[]` array.

```json
{
  "data": {
    "fiscalYear": 2027,
    "status": "Final",
    "office": {
      "code": "PGO",
      "name": "Office of the Provincial Governor"
    },
    "sectors": [
      {
        "sector": "General",
        "aipOfficeRefCode": "1000-000-1-01-001",
        "aipOfficeName": "Office of the Provincial Governor",
        "programs": [
          {
            "refCode": "1000-000-1-01-001-001",
            "name": "Executive Governance Program",
            "projects": [
              {
                "refCode": "1000-000-1-01-001-001-001",
                "name": "Exercise general supervision and control over all programs, projects, services and activities of the Local Government Unit.",
                "activities": [
                  {
                    "refCode": "1000-000-1-01-001-001-001-001",
                    "name": "Creation of Plantilla Positions: (1) Photographer SG 7 … (1) Project Development Officer III SG 18",
                    "esreCode": "ID",
                    "implementingOffice": "PGO, HR",
                    "fundingSource": "GF",
                    "schedule": { "start": "January", "end": "December" },
                    "amounts": { "ps": 8798650, "mooe": 845500, "co": 0, "total": 9644150 },
                    "climateChange": { "adaptation": 0, "mitigation": 0, "typologyCode": null },
                    "expectedOutputs": "Plantilla positions created"
                  }
                ]
              }
            ]
          }
        ]
      }
    ]
  },
  "error": null,
  "message": null
}
```

> The `amounts` above show the unit conversion: the source file lists PS `8,798.65` **in thousands**
> → the API returns `8798650` **pesos**.

**Field reference**

| Field | Type | Notes |
|-------|------|-------|
| `fiscalYear` | int | AIP fiscal year. |
| `status` | string | Always `"Final"` (only finalized records are exposed). |
| `office.code` | string | PPDO office code echoed back (the canonical office). |
| `office.name` | string | PPDO office name. |
| `sectors[]` | array | One entry per sector the office has items in (1–4: `General` / `Social` / `Economic` / `Others`). |
| `sectors[].sector` | string | `General` / `Social` / `Economic` / `Others`. |
| `sectors[].aipOfficeRefCode` | string | 5-segment AIP ref code for this office-and-sector grouping. |
| `sectors[].aipOfficeName` | string | Office name as written in the AIP for this grouping (may differ slightly per sector). |
| `sectors[].programs[]` | array | Level-2 programs (6-segment ref code). |
| `…programs[].refCode` / `.name` | string | Program reference code and name. |
| `…programs[]` line-item fields | optional | **Rare.** A program may carry the same `amounts` / `esreCode` / `implementingOffice` / `schedule` / `fundingSource` / `climateChange` / `expectedOutputs` as an activity — see "Program- and project-level values" below. Omitted when absent. |
| `…programs[].projects[]` | array | Level-3 projects (7-segment ref code). |
| `…projects[].refCode` / `.name` | string | Project reference code and name. |
| `…projects[]` line-item fields | optional | **Rare.** Same optional line-item fields as a program (see below). Omitted when absent. |
| `…projects[].activities[]` | array | Level-4 activities (8-segment ref code) — the leaf, carries the amounts. |
| `…activities[].refCode` / `.name` | string | Activity reference code and description. |
| `…activities[].esreCode` | string\|null | ESRE classification: `SS` / `ES` / `ID` / `EN`. |
| `…activities[].implementingOffice` | string\|null | Implementing office/department as written in the AIP. |
| `…activities[].fundingSource` | string\|null | Funding-source code snapshot at import time (e.g. `GF`, `20%DF`). |
| `…activities[].schedule.start` / `.end` | string\|null | Stored as month names (e.g. `"January"`), not dates. |
| `…activities[].amounts.ps` | number | Personal Services, **pesos**. |
| `…activities[].amounts.mooe` | number | Maintenance & Other Operating Expenses, **pesos**. |
| `…activities[].amounts.co` | number | Capital Outlay, **pesos**. |
| `…activities[].amounts.total` | number | `ps + mooe + co`, **pesos**. |
| `…activities[].climateChange.adaptation` / `.mitigation` | number | CC amounts, **pesos**. |
| `…activities[].climateChange.typologyCode` | string\|null | CC typology code. |
| `…activities[].expectedOutputs` | string\|null | Free text. |

> **(confirm)** Shape follows the AIP **PPA hierarchy** (sector → programs → projects → activities) —
> it mirrors the AIP document, the portal's WFP grid, and PPDO's internal AIP DTOs, so the
> implementation maps almost 1:1. Alternative: flat — one activity list with program/project and
> sector as fields on each row. GSO to indicate preference.

#### Program- and project-level values (rare)

Normally only **activities** (the leaf) carry amounts. But the AIP source occasionally records a
line item **directly at the program or project level**, with no child activity. The Provincial
Legal Office does this — e.g. program `1000-000-1-01-011-004` "DISASTER RESILIENT HUMAN RIGHTS AND
JUSTICE PROGRAM" carries ₱50,000 on its own.

To handle this, a `programs[]` or `projects[]` node **may** carry the **same optional line-item
fields as an activity** (`amounts`, `esreCode`, `implementingOffice`, `schedule`, `fundingSource`,
`climateChange`, `expectedOutputs`). These fields are **omitted when the node carries no value of
its own**. GSO consumers should read amounts **wherever they appear in the tree**, not only on
activities.

```json
{
  "refCode": "1000-000-1-01-011-004",
  "name": "DISASTER RESILIENT HUMAN RIGHTS AND JUSTICE PROGRAM",
  "esreCode": "ID",
  "implementingOffice": "PLO",
  "fundingSource": "GF",
  "schedule": { "start": "January", "end": "December" },
  "amounts": { "ps": 50000, "mooe": 0, "co": 0, "total": 50000 },
  "climateChange": { "adaptation": 0, "mitigation": 0, "typologyCode": null },
  "expectedOutputs": "Human rights protected and assist in the prosecution of …",
  "projects": []
}
```

> ⚠️ **Known PPDO-side limitation (logged, deferred — NOT a Phase-1 fix).** The portal's current
> data model stores amounts only on activities (`AipProgram` / `AipProject` have no amount
> columns), so these program/project-level values are **not captured today**. This is a known gap,
> deferred while PPDO data is the priority (the only case seen so far is the non-PPDO Provincial
> Legal Office). **Phase 2 must resolve it** — either extend the model to hold values at those
> levels, or normalize each into a synthetic leaf activity — before the API can return them. The
> contract reserves the shape now so GSO can design for it.

### 6.2 Fiscal-years response

`GET /api/external/v1/aip/fiscal-years?officeCode=PEO`

```json
{ "data": [2027, 2026], "error": null, "message": null }
```

---

## 7. Status codes & errors

| Code | When | `error` body |
|------|------|--------------|
| `200` | Success (including "no Final AIP found" → `data: null`), and the health check. | `null` |
| `400` | Missing/invalid `officeCode` or `fiscalYear`. | message describing the bad parameter |
| `401` | Missing or invalid `X-Api-Key` (on key-protected endpoints). | `"Invalid API key."` |
| `403` | Valid key, but not authorized for the requested office. | `"Office not authorized for this key."` |
| `429` | Rate limit exceeded (per key). Includes `Retry-After`. | `"Too many requests."` |
| `500` | Unexpected server error. | generic message (no internals leaked) |

Error example:

```json
{ "data": null, "error": "Office not authorized for this key.", "message": null }
```

---

## 8. Example requests

**Warm up / check availability (no key):**

```bash
curl -s "https://<ppdo-portal-domain>/api/external/v1/health"
# { "status": "ok", "timestamp": "2026-06-30T08:15:00Z" }
```

**Fetch AIP for an office:**

```http
GET /api/external/v1/aip?officeCode=PEO&fiscalYear=2027 HTTP/1.1
Host: <ppdo-portal-domain>
X-Api-Key: ppdo_live_xxxxxxxxxxxxxxxxxxxxxxxx
Accept: application/json
```

```bash
curl -s "https://<ppdo-portal-domain>/api/external/v1/aip?officeCode=PEO&fiscalYear=2027" \
  -H "X-Api-Key: ppdo_live_xxxxxxxxxxxxxxxxxxxxxxxx"
```

---

## 9. Open items to settle with GSO

1. **Response shape** — nested PPA hierarchy `sector → programs → projects → activities`
   (proposed) vs a flat activity list (program/project/sector as fields on each row).
2. **Auth header** — `X-Api-Key` (proposed) vs `Authorization: Bearer`.
3. **Scope** — one office per key, or a set of offices per key?
4. **Fields** — does GSO need everything in §6.1, or a subset? Anything missing for WFP building
   (e.g. account-level breakdown beyond PS/MOOE/CO totals)?
5. **Volume / pagination** — expected number of activities per office/year; whether pagination is
   needed (proposed: return the full set per office/year, no pagination).
6. **Environment** — will GSO test against a staging URL with sample data before production?

(Office identification is settled: PPDO issues the office code(s) with the key — no discovery
endpoint needed.)

---

## 10. Delivery plan (proposed two-phase split)

- **Phase 1 (now): this contract.** Agree the shape with GSO while the integration is still in the
  talking stage. No code.
- **Phase 2 (later): implementation.** API-key infrastructure (issue/hash/scope/revoke + per-key
  rate limiting), the health endpoint, the external read endpoints, audit logging, and tests —
  scoped as its own set of Linear tickets once this contract is signed off.
