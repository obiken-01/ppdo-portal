# External AIP API — Contract (DRAFT for discussion)

> **Status:** DRAFT v0.2 — proposed contract for review with GSO. Nothing is implemented yet.
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

- Returns the **finalized** AIP for that office and fiscal year, as a flat list of **activities**
  (the leaf level), each carrying its full program/project lineage — the granularity needed to
  build a WFP.
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

```json
{
  "data": {
    "fiscalYear": 2027,
    "status": "Final",
    "office": {
      "code": "PEO",
      "name": "Provincial Engineering Office",
      "aipRefCode": "01-010",
      "sector": "Economic"
    },
    "activities": [
      {
        "refCode": "01-010-001-001-001-001-001-001",
        "name": "Concreting of Barangay Access Road",
        "program":  { "refCode": "01-010-001", "name": "Infrastructure Program" },
        "project":  { "refCode": "01-010-001-001", "name": "Local Roads Project" },
        "esreCode": "ID",
        "implementingOffice": "Provincial Engineering Office",
        "fundingSource": "20%DF",
        "schedule": { "start": "January", "end": "December" },
        "amounts": {
          "ps": 0,
          "mooe": 0,
          "co": 1500000,
          "total": 1500000
        },
        "climateChange": {
          "adaptation": 1500000,
          "mitigation": 0,
          "typologyCode": "A-3"
        },
        "expectedOutputs": "1.2 km concrete road completed"
      }
    ]
  },
  "error": null,
  "message": null
}
```

**Field reference**

| Field | Type | Notes |
|-------|------|-------|
| `fiscalYear` | int | AIP fiscal year. |
| `status` | string | Always `"Final"` (only finalized records are exposed). |
| `office.code` | string | PPDO office code echoed back. |
| `office.name` | string | Office name as written in the AIP. |
| `office.aipRefCode` | string | 5-segment AIP reference code for the office grouping. |
| `office.sector` | string | `General` / `Social` / `Economic` / `Others`. |
| `activities[]` | array | One entry per AIP activity (leaf). |
| `activities[].refCode` | string | 8-segment activity reference code (unique within its project). |
| `activities[].name` | string | Activity description. |
| `activities[].program` | object | Parent program `{ refCode, name }`. |
| `activities[].project` | object | Parent project `{ refCode, name }`. |
| `activities[].esreCode` | string\|null | ESRE classification: `SS` / `ES` / `ID` / `EN`. |
| `activities[].implementingOffice` | string\|null | Implementing office as written in the AIP. |
| `activities[].fundingSource` | string\|null | Funding-source code snapshot at import time (e.g. `20%DF`). |
| `activities[].schedule.start` / `.end` | string\|null | Stored as month names (e.g. `"January"`), not dates. |
| `activities[].amounts.ps` | number | Personal Services, **pesos**. |
| `activities[].amounts.mooe` | number | Maintenance & Other Operating Expenses, **pesos**. |
| `activities[].amounts.co` | number | Capital Outlay, **pesos**. |
| `activities[].amounts.total` | number | `ps + mooe + co`, **pesos**. |
| `activities[].climateChange.adaptation` / `.mitigation` | number | CC amounts, **pesos**. |
| `activities[].climateChange.typologyCode` | string\|null | CC typology code. |
| `activities[].expectedOutputs` | string\|null | Free text. |

> **(confirm)** Shape is **flat activities with lineage** (recommended — easiest to map into WFP).
> Alternative: a nested tree `office → programs → projects → activities`. GSO to indicate preference.

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

1. **Response shape** — flat activities (proposed) vs nested tree.
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
