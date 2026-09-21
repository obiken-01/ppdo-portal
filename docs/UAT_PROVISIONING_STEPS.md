---
status: ready to execute
version: v1.8.0 UAT (PPDO-21)
tickets: PPDO-21
supersedes: nothing — the ordered runbook for `UAT_ENVIRONMENT_SETUP.md`, which stays the reference
---

# UAT — provisioning steps, in order

> **What this is.** `UAT_ENVIRONMENT_SETUP.md` is the reference: why, what it costs, every setting,
> teardown. **This is the do-list** — the same information as an ordered sequence you work through
> once, with the decisions already made.
>
> **Why UAT exists now:** so people can exercise v1.8.0 **before** it reaches production. That is a
> different goal from the original one (screenshotting a user guide) and it changed two decisions —
> see `UAT_ENVIRONMENT_SETUP.md` §11.

---

## 0. The shape of this

⚠️ **UAT is a dress rehearsal of the production deploy, not a preview of the destination.** The `uat`
branch is cut from **`main` (v1.7.4)** — the code production runs right now — not from
`release/1.8.0`. So the sequence mirrors release day exactly:

```
uat = main (v1.7.4)          →  deploy  →  UAT runs what prod runs today
  ↓ apply the 23 migrations              →  rehearse the risky half
  ↓ merge release/1.8.0 into uat         →  deploy v1.8.0 code
  →  and you have just performed the production deploy, on a database nobody minds breaking
```

That is worth more than it sounds. It exercises the **ordering problem** that has no safe answer
(§4 below) on data you can throw away.

⚠️ **One deliberate exception to "uat = main".** The `NEXT_PUBLIC_NOINDEX` change is applied on top,
because without it the first deploy puts a crawlable duplicate of a government site on the public
internet. It touches four frontend files and nothing the migration rehearsal depends on.

---

## 1. Azure — create in this order

Later resources need values from earlier ones. All in **Southeast Asia**, all in one resource group
so teardown is a single delete.

- [ ] **Resource group** — `ppdo-portal-uat-rg`, Southeast Asia

- [ ] **Storage account** — `ppdoportaluatstorage`
      StorageV2 · Standard · **LRS** · Southeast Asia

- [ ] **SQL Server** — `ppdo-portal-server-uat`, Southeast Asia
  - [ ] Admin login + strong password → **password manager, never this repo** (it is public)
  - [ ] Networking → **Allow Azure services and resources to access this server** = ON
        (without this the Function App cannot reach the database)
  - [ ] Add **your own client IP** — you need it to run migrations from this machine

- [ ] **SQL Database** — `ppdo-portal-db-uat` on that server
      **Basic** tier (5 DTU, 2 GB) · ~$0.161/day

- [ ] **Function App** — `ppdo-portal-api-uat`
  - [ ] ⚠️ **Region: Southeast Asia.** Colocated with the database. RAL-237 moved production here
        because every request was crossing the Pacific — it cut requests from ~2–6s to ~0.4s. Put
        UAT in Central US and testers will judge v1.8.0's performance against a handicap production
        no longer has.
  - [ ] .NET 9 **isolated** · Consumption plan
  - [ ] Application settings — §2 below
  - [ ] **CORS** → add the UAT Static Web App URL once you have it (step below), **and tick
        "Enable Access-Control-Allow-Credentials"**. ⚠️ Two settings in two places are needed, and
        each is silently useless without the other — see §2. `host.json` CORS does **not** work for
        the isolated worker.
  - [ ] ⚠️ **Configuration → General settings → SCM Basic Auth Publishing Credentials = On**, then
        Apply. Azure defaults new Function Apps to **off**; `deploy-uat.yml` (like `deploy.yml`)
        POSTs the zip to the SCM endpoint using MSDeploy credentials taken from the publish profile,
        so with basic auth off the profile carries no usable password and the deploy returns **401**.
        Production predates that default, which is why this is not a problem there.
  - [ ] Download the **publish profile** → GitHub secret (§3) — *after* the step above; the
        credentials are absent from a profile downloaded while basic auth is off

- [ ] **Static Web App** — `ppdo-portal-uat`
      **Free** plan · deployment source **"Other"** (the workflow deploys it, not SWA's own pipeline)
  - [ ] Copy the **deployment token** → GitHub secret (§3)
  - [ ] ⚠️ **Note the generated hostname.** It is random (production's is `jolly-sky-0e3a2e310…`)
        and does **not** come back if you ever delete and recreate the app. It gets wired into four
        places below.

- [ ] **Application Insights** — `ppdo-portal-api-uat`, Southeast Asia *(optional — leaving the
      connection string blank still gives console logging via `ILogger<T>`)*

---

## 2. Function App application settings

Mirror production, with **two deliberate differences**:

| Setting | Value |
|---|---|
| `FUNCTIONS_WORKER_RUNTIME` | `dotnet-isolated` |
| `AzureWebJobsStorage` | connection string for `ppdoportaluatstorage` |
| `SqlConnectionString` | connection string for `ppdo-portal-db-uat` |
| `Cors__AllowedOrigins` | ⚠️ The **UAT** Static Web App URL — see below |
| `Jwt__SecretKey` | ⚠️ **A NEW value, 32+ chars — must differ from production** |
| `Jwt__Issuer` | ⚠️ The **UAT** Static Web App URL |
| `Jwt__Audience` | `ppdo-portal` |
| `Jwt__AccessTokenExpiryMinutes` | `15` |
| `Jwt__RefreshTokenExpiryDays` | `7` |
| `APPLICATIONINSIGHTS_CONNECTION_STRING` | UAT App Insights, or blank |

⚠️ **`Jwt__SecretKey` must be unique to UAT.** Share production's signing key and a token minted by
UAT validates against production. That is the single most damaging thing you could copy across.

⚠️ **`Cors__AllowedOrigins` is what actually governs CORS — not the portal's CORS blade.** RAL-58
replaced the old origin echo-back with an allowlist read from this app setting
(`Program.cs`, a comma-separated list; double underscore, no trailing slash, compared with
`OrdinalIgnoreCase` string equality against the `Origin` header). **Leave it unset and it falls back
to `http://localhost:3000,http://localhost:4280`** — the Static Web App then gets no CORS headers at
all and every browser request is blocked, which presents as a working API that the site cannot talk
to.

### ⚠️ The app setting alone is not enough — the portal blade overrides it

**Both layers are live, and the platform one wins.** Once *any* origin is listed in
**Function App → API → CORS**, Azure's platform CORS answers the `OPTIONS` preflight at the front
end and the request never reaches the worker — so `Program.cs` never runs and never adds
`Access-Control-Allow-Credentials: true`. Login then fails in the browser with:

```
Response to preflight request doesn't pass access control check: The value of the
'Access-Control-Allow-Credentials' header in the response is '' which must be 'true'
when the request's credentials mode is 'include'.
```

The app *does* emit that header itself, which is why this looks like it should work — but only for
requests that get as far as the app. So, on the CORS blade:

- [ ] Add the UAT Static Web App origin (no trailing slash)
- [ ] ⚠️ **Tick "Enable Access-Control-Allow-Credentials"** — required, not optional. The
      refresh-token cookie is sent with `withCredentials: true`, and without this the preflight
      fails before any credential is ever checked.

⚠️ **Do not try to fix this by emptying the blade instead.** With origins configured the platform
layer is *subtractive*: an unlisted origin gets `400 The origin '…' is not allowed` from Azure, not
a fall-through to the app's own allowlist.

**Diagnosing it.** `x-ms-middleware-request-id` in the response identifies the platform layer as the
one answering, and Azure's 400 wording gives it away too:

```bash
curl -s -o /dev/null -D - -X OPTIONS \
  "https://<function-app>.azurewebsites.net/api/auth/login" \
  -H "Origin: https://<swa-host>" \
  -H "Access-Control-Request-Method: POST" \
  -H "Access-Control-Request-Headers: content-type"
```

Healthy output carries **both** `Access-Control-Allow-Origin` and
`Access-Control-Allow-Credentials: true`. Repeat against a real endpoint (a wrong-password `POST` to
`/api/auth/login` is ideal — a `401` with both headers present proves the request reached the app).

> ⚠️ `CLAUDE.md` says to add new origins in **Azure Portal → Function App → CORS**. That is correct
> but incomplete: it predates RAL-58, and doing only that leaves `Cors__AllowedOrigins` on its
> localhost fallback. Both places need the origin, and the blade needs the credentials checkbox.

---

## 3. GitHub secrets

Two new ones, **suffixed `_UAT`** and separate from production's, so a UAT misconfiguration can
never deploy over prod:

| Secret | Source |
|---|---|
| `AZURE_FUNCTIONS_PUBLISH_PROFILE_UAT` | Function App → Get publish profile |
| `AZURE_STATIC_WEB_APPS_API_TOKEN_UAT` | Static Web App → Manage deployment token |

---

## 4. The deploy workflow

⚠️ **Do not add `.github/workflows/deploy-uat.yml` until the resources above exist.** The draft in
`UAT_ENVIRONMENT_SETUP.md` §7 triggers on a branch push; committing it with `<<<placeholder>>>`
hostnames means a failed deploy on every push until Azure is ready.

When you add it, change two things from the draft:

1. **Trigger on `uat`**, not `release/**`. A dedicated branch means UAT sits still until you
   deliberately promote — testers mid-session do not get the ground moved, and a bug report can be
   pinned to a known build.
2. Replace both `<<<...>>>` placeholders with the real UAT hostnames.

It must set **both** env vars on the frontend build:

```yaml
NEXT_PUBLIC_NOINDEX: "true"
NEXT_PUBLIC_SITE_URL: "https://<the UAT Static Web App hostname>"
NEXT_PUBLIC_API_BASE_URL: "https://<the UAT Function App hostname>/api"
```

⚠️ `NOINDEX` and `SITE_URL` are a **pair**. `SITE_URL` falls back to the *production* host, so
setting one without the other still emits production-pointing canonical tags from UAT.

---

## 5. Database

```bash
cd backend
dotnet ef database update --project PPDO.Infrastructure --startup-project PPDO.Functions \
  --connection "<UAT connection string>"
```

⚠️ **Do not restore or copy the production database.** Real personnel names and real budget figures
on a second internet-facing instance is the exact thing this environment exists to avoid.

### Seed enough to actually test v1.8.0

`UAT_ENVIRONMENT_SETUP.md` §9's list was written for a user guide, where one token record was enough
to photograph. Exercising the AIP redesign needs structure:

- [ ] **Offices** — at least **three**: the host office (PPDO) and two guest offices
- [ ] **Divisions** under each
- [ ] **Users — at least four**: a PPDO admin, a PPDO reviewer, and **one encoder in each of two
      different guest offices**
      ⚠️ Two guest offices is the minimum that makes the cross-office scope rules testable at all —
      they are the largest and riskiest part of v1.8.0, and with one office everything passes
      trivially
- [ ] **An office ceiling** for FY2028, so the ceiling/submit gate has something to gate on
- [ ] **An LDIP** for FY2028 — AIP programs are seeded from it, so without one there is nothing to
      add programs from
- [ ] **An FY2028 AIP record** with programs seeded from that LDIP
- [ ] A **funding source owned by one office** (PPDO-109), so the per-office fund rules are visible
- [ ] Change the seeded SuperAdmin password — the bootstrap hash is committed to a public repo

---

## 6. Rehearse the deploy

This is the part worth doing carefully, because it is the production sequence.

- [ ] **Deploy `uat` (= v1.7.4 + noindex).** Confirm the site loads and behaves like production.
- [ ] **Confirm it is not indexable** — visit `/robots.txt`, expect `Disallow: /`; view source on the
      landing page, expect `<meta name="robots" content="noindex, nofollow">`.
- [ ] **Capture the baseline** (`Pre_Deployment_Checklist.md` §1) against the UAT database.
- [ ] **Apply the v1.8.0 migrations.** All 23.
- [ ] ⚠️ **Observe the broken window.** Between the migration and the code deploy, AIP amounts
      display **1000× too large** — v1.7.4 reads them as ₱000 and the migration has made them pesos.
      **This is expected**, and seeing it here is the point: it tells you how long production will
      look wrong, so you can plan the real window.
- [ ] **Merge `release/1.8.0` into `uat`** and let it deploy.
- [ ] **Confirm the amounts read correctly again** — this is the check that the display half and the
      data half shipped together.
- [ ] Work `Pre_Deployment_Checklist.md` §3's manual test cases against UAT.

If all of that passes here, production is the same sequence against data that matters.

---

## 7. When you are done

See `UAT_ENVIRONMENT_SETUP.md` §10 — it covers what can and cannot be paused (Basic SQL cannot),
the three levels of teardown, what does not survive a full delete (the Static Web App hostname), and
why an Azure charge appears the month *after* you tear down.

---

*Written 2026-09-21 alongside PPDO-21's noindex change. The reference doc is
`UAT_ENVIRONMENT_SETUP.md`; this is the sequence.*
