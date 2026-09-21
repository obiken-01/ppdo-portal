# UAT Environment — Setup Reference

> **Status: NOT BUILT. Reference only.**
> Drafted 2026-08-10 after a cost review. Nothing in this document has been
> provisioned, and the workflow in §7 is deliberately **not** committed to
> `.github/workflows/` — a live workflow file would fire on every release-branch
> push and fail on missing secrets. Copy it into place only when you actually
> build this.
>
> Original driver: give a colleague a safe instance to click through while
> writing the end-user guide, without them touching production data.

---

## 1. Why a separate instance (and not just prod)

Production data contains real personnel names and real budget figures, and
`obiken-01/ppdo-portal` is a **public GitHub repository**. If user-guide
screenshots are taken against prod and the guide is ever committed, attached to
a Linear ticket, or shared outside the office, that data goes with it.

Seeding UAT with fabricated data is therefore not just safer — it produces a
better guide, because screenshots can show clean, illustrative values instead of
whatever half-finished FY figures happen to be in prod that week.

Secondary benefit: the guide writer cannot create junk records in production.

---

## 2. What it costs

Only the database costs anything. Everything else falls inside free grants.

| Resource | Monthly | Notes |
| --- | --- | --- |
| Static Web App | **$0** | Free plan; multiple instances allowed per subscription |
| Function App (Consumption) | **~$0** | Free grant is per-subscription and shared with prod; one guide writer is negligible against it |
| Storage account | **~$0.05–0.50** | Required by Functions, trivial usage |
| Application Insights | **$0** | Free monthly data grant (shared with prod) |
| **Azure SQL (Basic)** | **$4.90** | $0.161/day — the entire real cost |
| **Total** | **~$5/month** | |

### Why SQL is not free

The Azure SQL free offer is **one database per subscription**, and
`ppdo-portal-db` (production) holds it. That database also exhausted its 100K
vCore-second monthly allowance on 2026-07-15, after which pay-as-you-go overage
was enabled. So the subscription has no free database left to give.

### Tier choice

Rates confirmed 2026-07-22 against Azure's retail prices API for
`southeastasia` — not estimates. Re-verify if this sits unused for months:

```bash
curl -s "https://prices.azure.com/api/retail/prices?\$filter=armRegionName%20eq%20'southeastasia'%20and%20serviceName%20eq%20'SQL%20Database'"
```

| Tier | Rate | Monthly | Trade-off |
| --- | --- | --- | --- |
| **Basic** (5 DTU, 2GB) | $0.161/day | **$4.90** | Predictable, billed daily. 5 DTU is low. |
| Serverless GP (prod's tier) | $0.0001725/vCore-sec | ~$12.40 @ 40 active hrs | Matches prod behaviour; auto-pause makes idle free |

**Recommendation: Basic**, with two caveats to watch:

1. **5 DTU may throttle** on this app's heavy read paths — the AIP tree, the
   ~6,400-row price index, WFP/PPMP report generation, Excel export. That is
   precisely what a guide writer will be clicking through. If report pages crawl,
   switch the tier in place rather than redesigning anything.
2. **2GB is a hard cap.** Check prod's current size before assuming it fits;
   a UAT seeded with fabricated data should be far smaller, but verify.

Because Basic bills **per day**, a short engagement is genuinely cheap — three
weeks is roughly **$3.40**. Delete it when the guide is done (§9).

### Before committing

Check **Azure Portal → Cost Management + Billing → Cost analysis** for actual
July/August charges first. Production has been in SQL overage since 2026-07-15,
so the current baseline is not $0 and should be known before adding to it.

---

## 3. Naming and region plan

Everything goes in its **own resource group** so teardown is a single delete.

| Resource | Production | UAT |
| --- | --- | --- |
| Resource group | `ppdo-portal-rg` | `ppdo-portal-uat-rg` |
| Static Web App | `ppdo-portal` | `ppdo-portal-uat` |
| Function App | `ppdo-portal-api-sea` | `ppdo-portal-api-uat` |
| SQL Server | `ppdo-portal-server` | `ppdo-portal-server-uat` |
| SQL Database | `ppdo-portal-db` | `ppdo-portal-db-uat` |
| Storage | `ppdoportalstorage` | `ppdoportaluatstorage` |
| App Insights | `ppdo-portal-api` | `ppdo-portal-api-uat` |

**Regions — everything in Southeast Asia.**

↩️ ⚠️ **Corrected 2026-09-21. This section used to say "Functions and App Insights in
Central US as prod does" — that was true when it was drafted on 2026-08-10 and stopped being true a
week later.** RAL-237 (2026-08-17) relocated the production Function App from Central US to
Southeast Asia precisely *because* every request was crossing the Pacific to reach the database.
Measured effect: **~2–6s per request → ~0.4s.** Production's Function App is now
`ppdo-portal-api-sea`; the old Central US app is superseded.

**Following the old instruction would have rebuilt the exact bug RAL-237 fixed**, and UAT testers
would have judged v1.8.0's performance against a handicap production no longer has. CLAUDE.md states
the rule plainly: *any new Azure resource goes in Southeast Asia.*

| Resource | Region |
|---|---|
| SQL Server + Database | Southeast Asia |
| Function App | Southeast Asia — **colocated with the database** |
| Storage | Southeast Asia |
| Static Web App | Free tier has no region choice; the edge serves globally |
| App Insights | Southeast Asia (prod's still sits in Central US; telemetry is not latency-critical, but there is no reason to repeat it) |

Fidelity still matters, and this *is* the faithful setup: matching production means matching
production **as it is now**, not as it was before the relocation.

---

## 4. Azure resource checklist

> ➡️ **Working through this for real? Use [`UAT_PROVISIONING_STEPS.md`](UAT_PROVISIONING_STEPS.md)
> instead.** Same information as an ordered do-list with the decisions already made, plus the seed
> data v1.8.0 testing actually needs and the deploy rehearsal. This section stays as the reference.

Create in this order — later resources need values from earlier ones.

- [ ] **Resource group** `ppdo-portal-uat-rg`
- [ ] **Storage account** `ppdoportaluatstorage` — StorageV2, Standard, LRS
- [ ] **SQL Server** `ppdo-portal-server-uat`
  - [ ] Admin login + strong password → store in a password manager, **never** in this repo
  - [ ] Networking → **Allow Azure services and resources to access this server** = ON
  - [ ] Add your own client IP for running migrations from the dev machine
- [ ] **SQL Database** `ppdo-portal-db-uat` — **Basic** tier (5 DTU, 2GB)
- [ ] **Function App** `ppdo-portal-api-uat`
  - [ ] Runtime .NET 9 **isolated**, Consumption plan, matching prod's OS
  - [ ] ⚠️ **Region: Southeast Asia** — colocated with the SQL database (see §3)
  - [ ] Application settings per §5
  - [ ] **CORS** → add the UAT Static Web App URL
        (Portal only — `host.json` CORS does **not** work for the isolated worker)
  - [ ] Download the publish profile → GitHub secret (§6)
- [ ] **Static Web App** `ppdo-portal-uat` — **Free** plan, deployment source
      "Other" (the workflow below deploys it, not SWA's own generated pipeline)
  - [ ] Copy the deployment token → GitHub secret (§6)
- [ ] **Application Insights** `ppdo-portal-api-uat` (optional — leaving the
      connection string blank still gives console logging via `ILogger<T>`)

---

## 5. Function App application settings

Mirror production, with **two deliberate differences** flagged below.

| Setting | Value |
| --- | --- |
| `FUNCTIONS_WORKER_RUNTIME` | `dotnet-isolated` |
| `AzureWebJobsStorage` | *(connection string for `ppdoportaluatstorage`)* |
| `SqlConnectionString` | *(connection string for `ppdo-portal-db-uat`)* |
| `Jwt__SecretKey` | ⚠️ **Generate a NEW value — must differ from prod** |
| `Jwt__Issuer` | ⚠️ The **UAT** Static Web App URL |
| `Jwt__Audience` | `ppdo-portal` |
| `Jwt__AccessTokenExpiryMinutes` | `15` |
| `Jwt__RefreshTokenExpiryDays` | `7` |
| `APPLICATIONINSIGHTS_CONNECTION_STRING` | *(UAT App Insights, or blank)* |

**`Jwt__SecretKey` must be unique to UAT.** Sharing prod's signing key would mean
a token minted by UAT validates against production. Generate a fresh 32+
character value.

> No real secret values belong in this file, in `local.settings.json`, or in any
> committed file. The repo is public.

---

## 6. GitHub secrets required

| Secret | Source |
| --- | --- |
| `AZURE_FUNCTIONS_PUBLISH_PROFILE_UAT` | Function App → Get publish profile |
| `AZURE_STATIC_WEB_APPS_API_TOKEN_UAT` | Static Web App → Manage deployment token |

Kept separate from the production secrets so a UAT misconfiguration can never
deploy over prod.

---

## 7. Deployment workflow

Save as `.github/workflows/deploy-uat.yml` **when building this**, not before.

⚠️ **"Not before" is load-bearing, not tidiness.** This workflow triggers on `release/**`, and
`release/1.8.0` is an active branch. Committing it with the `<<<...>>>` placeholders still in place
means a failed deploy on **every push to the release branch** until the Azure resources exist. Add
it only once you have the real hostnames.

Structural notes:

- ↩️ **Trigger on the `uat` branch, not `release/**`** (decision revised 2026-09-21 — see
  §11). The draft below still says `release/**`; change it to `uat` when you save the file. Keep
  `workflow_dispatch` for manual runs.

  ⚠️ The `release/**` trigger is also why this file must not be committed before the Azure
  resources exist — see the warning above.
- Uses the same `curl` ZIP-deploy as production. `Azure/functions-action@v1` is
  **blocked by this repo's Actions policy** — do not "simplify" it back.
- Replace both `<<<...>>>` placeholders with the real UAT hostnames.
- Depends on the `NEXT_PUBLIC_NOINDEX` change in §8 — without it, UAT is
  crawlable.

```yaml
name: Deploy UAT

on:
  push:
    branches:
      - 'release/**'
  workflow_dispatch:

jobs:
  deploy-api:
    name: Deploy Functions to Azure (UAT)
    runs-on: ubuntu-latest

    steps:
      - uses: actions/checkout@v4

      - uses: actions/setup-dotnet@v4
        with:
          dotnet-version: '9.0.x'

      - name: Publish Functions
        run: |
          dotnet publish backend/PPDO.Functions/PPDO.Functions.csproj \
            --configuration Release \
            --output ${{ github.workspace }}/publish/api

      - name: Zip published output
        run: |
          cd ${{ github.workspace }}/publish/api
          zip -r ${{ github.workspace }}/publish/function.zip .

      - name: Deploy to Azure Functions via ZIP deploy
        env:
          PUBLISH_PROFILE: ${{ secrets.AZURE_FUNCTIONS_PUBLISH_PROFILE_UAT }}
        run: |
          # Extract MSDeploy credentials from publish profile XML
          USER=$(echo "$PUBLISH_PROFILE" | python3 -c "
          import sys, xml.etree.ElementTree as ET
          root = ET.parse(sys.stdin).getroot()
          p = root.find('.//publishProfile[@publishMethod=\"MSDeploy\"]')
          print(p.get('userName'))
          ")
          PASS=$(echo "$PUBLISH_PROFILE" | python3 -c "
          import sys, xml.etree.ElementTree as ET
          root = ET.parse(sys.stdin).getroot()
          p = root.find('.//publishProfile[@publishMethod=\"MSDeploy\"]')
          print(p.get('userPWD'))
          ")

          curl -X POST \
            "https://<<<UAT-FUNCTION-SCM-HOSTNAME>>>/api/zipdeploy" \
            -u "${USER}:${PASS}" \
            --data-binary @${{ github.workspace }}/publish/function.zip \
            --fail \
            --silent \
            --show-error

  deploy-frontend:
    name: Deploy Frontend to Azure Static Web Apps (UAT)
    runs-on: ubuntu-latest

    steps:
      - uses: actions/checkout@v4

      - name: Deploy to Azure Static Web Apps
        uses: azure/static-web-apps-deploy@v1
        with:
          azure_static_web_apps_api_token: ${{ secrets.AZURE_STATIC_WEB_APPS_API_TOKEN_UAT }}
          repo_token: ${{ secrets.GITHUB_TOKEN }}
          action: upload
          app_location: frontend
          output_location: out
        env:
          NEXT_PUBLIC_API_BASE_URL: https://<<<UAT-FUNCTION-HOSTNAME>>>/api
          NEXT_PUBLIC_SITE_URL: https://<<<UAT-SWA-HOSTNAME>>>
          # Keeps UAT out of search engines — see docs §8.
          NEXT_PUBLIC_NOINDEX: 'true'
```

---

## 8. Required code change — keep UAT out of search results

✅ **DONE 2026-09-21 (PPDO-21).** `NEXT_PUBLIC_NOINDEX` is implemented and verified. What follows
is the original sketch, kept because the reasoning still explains *why*; the shipped version differs
in one way worth knowing.

⚠️ **It gates FOUR surfaces, not the two sketched below.** The sketch named `robots.ts` and a
meta tag. Two more leak:

| Surface | Why it matters |
|---|---|
| `robots.ts` | Emits `Disallow: /` **and drops the `Sitemap:` line** — advertising a sitemap while disallowing the site hands a crawler the URLs anyway |
| `sitemap.ts` | Returns `[]`. A sitemap is the one surface that actively **invites** indexing; leaving it populated undoes the other three |
| Root layout `robots` metadata | `<meta name="robots" content="noindex, nofollow">` — **the half with teeth**; robots.txt is a request, this is an instruction Google documents as honoured |
| `metadataBase` / `og:url` | Follows `SITE_URL`, so setting `NEXT_PUBLIC_NOINDEX` **without** `NEXT_PUBLIC_SITE_URL` still emits production-pointing canonicals. The two env vars are a pair |

**Verified by building both ways and reading the output**, not by inspection:

- `NEXT_PUBLIC_NOINDEX=true` → `robots.txt` is `Disallow: /` with no sitemap line; `sitemap.xml` is an
  empty urlset; every page carries the noindex meta; **zero occurrences of the production hostname
  anywhere in `out/`**.
- Flag unset → output byte-for-byte what RAL-202 ships today.

---

### Original sketch (reasoning preserved)

**This is a prerequisite, not an optional extra.** RAL-202 made the public site
deliberately indexable, and that behaviour is currently unconditional:

- `frontend/src/app/robots.ts` always emits `allow` for the public marketing
  pages, with no environment check.
- `frontend/src/lib/seo.ts` falls back to the **production** URL when
  `NEXT_PUBLIC_SITE_URL` is unset.

Deploying UAT as-is therefore produces one of two bad outcomes: Google indexes a
second public copy of the PPDO site, or UAT emits a sitemap and canonical tags
pointing at production.

Minimal fix — gate on a new build-time flag:

```ts
// frontend/src/lib/seo.ts
export const NOINDEX = process.env.NEXT_PUBLIC_NOINDEX === "true";
```

```ts
// frontend/src/app/robots.ts
export default function robots(): MetadataRoute.Robots {
  if (NOINDEX) {
    return { rules: { userAgent: "*", disallow: ["/"] } };
  }
  // ...existing production rules unchanged
}
```

Consider also emitting `<meta name="robots" content="noindex">` via the root
layout's metadata when `NOINDEX` is set, as belt-and-braces — `robots.txt` is a
request, not an enforcement mechanism.

Production behaviour is unchanged: the flag is absent there, so `NOINDEX` is
`false`.

---

## 9. Database initialisation and seed data

Apply the schema against UAT:

```bash
cd backend
SqlConnectionString="<<<UAT-CONNECTION-STRING>>>" dotnet ef database update --project PPDO.Infrastructure --startup-project PPDO.Functions
```

**Do not restore or copy the production database.** That reintroduces the exact
data-exposure problem UAT exists to avoid (§1).

⚠️ **Revisit this list for v1.8.0 testing.** It was written for a user guide, where a token
record was enough to photograph. Exercising the AIP redesign needs real structure: offices,
divisions, an office ceiling, an FY2028 AIP record with programs seeded from an LDIP, and **at least
two office accounts** — otherwise the cross-office scope rules, which are the largest and riskiest
part of v1.8.0, cannot be tested at all. The rule above still stands regardless: fabricated, not a
production restore.

Seed instead with fabricated data covering what the guide needs to show:

- [ ] Divisions — realistic names are fine; these aren't sensitive
- [ ] One user per role (SuperAdmin / Admin / Staff) so the guide can show how
      the interface differs by permission
- [ ] A small AIP + WFP set — enough to render the report pages meaningfully
      without approaching the 2GB Basic cap
- [ ] A handful of inventory items, one PR, one delivery, one distribution

The seeded SuperAdmin credentials are documented in `CLAUDE.md`. Change the
password on the UAT instance anyway — those values are published in a public
repository, and UAT will be internet-facing.

---

## 10. Teardown — and the cheaper middle option

⚠️ **"Down" means DELETE, not pause.** Azure SQL **Basic cannot be paused** — it is DTU-based
and always-provisioned. Only *Serverless* auto-pauses, and production deliberately moved **off**
serverless in August 2026 (see CLAUDE.md). There is no stop button to reach for here.

Stopping the **Function App** is not worth doing either: Consumption bills per execution against a
1M/month free grant, so an idle Function App already costs nothing.

### What actually costs money

**SQL is the entire bill.** Everything else sits inside a free grant at this load.

| Resource | Cost while UAT sits idle |
|---|---|
| **SQL Database (Basic)** | **$0.161/day ≈ $4.90/month** — verified against Azure's retail prices API for `southeastasia`, 2026-09-21 |
| Static Web App (Free) | $0 |
| Functions (Consumption) | $0 idle — 1M executions/month free |
| Application Insights | $0 — 5 GB/month free |
| Storage | Cents |

A three-month run is roughly **91 days × $0.161 ≈ $14.65**, total.

Billing is **per day**, so there is no reason to wait for a month boundary to tear down — you are
charged to the day you delete. The same fact cuts the other way: ⚠️ **a UAT database left up
"just in case" quietly costs ~$4.90/month forever.** Put a calendar note at teardown rather than
trusting memory.

### Three levels of down

| | What you delete | Cost while down | What you lose | Back up in |
|---|---|---|---|---|
| **1. Full teardown** | The whole resource group | $0 | Everything — URLs, config, secrets, data | Full §4 checklist, ~1–2 hrs |
| **2. Drop the database** | `ppdo-portal-db-uat` only | ~$0 (cents) | The data. URLs, Function App settings, CORS and GitHub secrets all survive | Create DB, migrate, re-seed — ~20 min |
| **3. Export, then drop** ⭐ | Same, after a `.bacpac` export to the storage account | ~$0 + a fraction of a cent for the blob | **Nothing** | Create DB, import bacpac — ~15 min |

**Prefer level 3 whenever UAT might come back** (a later release, a second round of testing). The
saving from a full teardown is effectively zero, and the seeded fixture — offices, divisions,
ceilings, an FY2028 AIP, two office accounts — is the slow part to rebuild.

### ⚠️ What does NOT survive a full teardown

The resource **group** name is freely reusable with no cooldown, and an empty resource group costs
nothing — so you never have to delete the group itself, only what is in it. That is not the part
that bites. These are:

| Resource | On recreation |
|---|---|
| **Static Web App** | ⚠️ **A brand-new random hostname.** The generated name (production's is `jolly-sky-0e3a2e310…`) is assigned at creation and **does not come back**. You will not get the old UAT URL |
| Function App | Globally unique on `*.azurewebsites.net`. Released on delete, but reuse is not instantaneous and the name is not reserved for you |
| SQL Server | Same, on `*.database.windows.net` |
| Storage account | Same, globally unique |
| Resource group | ✅ Reusable immediately |

**The Static Web App hostname is the expensive one**, because that URL is wired into four places
that all have to be re-done by hand:

1. `Jwt__Issuer` on the Function App (§5)
2. The Function App's **CORS** allow-list (§4)
3. `NEXT_PUBLIC_SITE_URL` in `deploy-uat.yml` (§7)
4. Whatever bookmark the testers saved

Plus **both GitHub secrets change** — a recreated Function App issues a new publish profile, and a
recreated Static Web App issues a new deployment token (§6).

So a rebuild is the §4 checklist **plus** those six items, not the checklist alone. That is the real
argument for level 2 or 3 over level 1.

### If you do tear down completely

- [ ] Delete the two GitHub secrets (§6)
- [ ] Delete `.github/workflows/deploy-uat.yml` — ⚠️ it triggers on `release/**`, so leaving it
      behind means a failed deploy on every push to the release branch
- [ ] Remove the UAT origin from the **production** Function App's CORS list if it was ever added
      there

### A note on the bill after teardown

Azure bills **in arrears**. Delete at the end of November and you will still see a charge land in
early December — **that is November's usage, not a teardown that failed.** The first genuinely empty
invoice is the one covering December, which arrives in January.

---

## 11. Open questions

- ~~Which branch should UAT track?~~ ✅ **A dedicated `uat` branch — Ralph, 2026-09-21.**

  ↩️ **This supersedes an earlier answer of `release/**` made the same day.** That was right
  about rejecting `main` (still v1.7.4 — testers would see nothing new) but wrong about the
  alternative. Tracking `release/**` means **UAT redeploys under the testers every time a branch is
  pushed**, so a tester mid-session can have the ground move, and a bug report cannot be pinned to a
  known build. A dedicated branch makes promotion deliberate: merge into `uat` when you want testers
  to see something, and it sits still until you do.

  Cost of the change: one `git merge` per promotion. Worth it.

  ↩️ **And `uat` is cut from `main`, not from `release/1.8.0`** — Ralph, 2026-09-21. This is
  the better shape and it changes what UAT is worth: starting on the code production runs *today*
  makes the whole exercise a **dress rehearsal of the production deploy**, not a preview of the
  destination. The migrations and the v1.8.0 merge happen in the same order they will on release
  day, which exercises the one window that has no safe answer — migration applied, code not yet
  deployed, AIP amounts reading 1000× high — on a database nobody minds breaking.

  ⚠️ **One deliberate exception to "uat = main": the `NEXT_PUBLIC_NOINDEX` change is applied on
  top.** Without it, the first UAT deploy puts a crawlable duplicate of a government site on the
  public internet, and that cannot wait for the v1.8.0 merge. It touches four frontend files and
  nothing the migration rehearsal depends on.
- ~~Does the guide writer need an Azure login?~~ ✅ **No** — in-app account only.
- Custom domain for UAT? Not assumed; the default `*.azurestaticapps.net`
  hostname is fine for internal use.

---

*Drafted 2026-08-10 — reference only, nothing provisioned.*
