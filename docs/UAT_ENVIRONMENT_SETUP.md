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
| Function App | `ppdo-portal-api` | `ppdo-portal-api-uat` |
| SQL Server | `ppdo-portal-server` | `ppdo-portal-server-uat` |
| SQL Database | `ppdo-portal-db` | `ppdo-portal-db-uat` |
| Storage | `ppdoportalstorage` | `ppdoportaluatstorage` |
| App Insights | `ppdo-portal-api` | `ppdo-portal-api-uat` |

**Regions — mirror production.** SQL in Southeast Asia (matches the confirmed
pricing above and is closest to users); Functions and App Insights in Central US
as prod does. Fidelity matters here: if UAT sits in a different region to prod,
its latency and cold-start behaviour differ, and the guide ends up documenting
loading states that users will never actually see.

---

## 4. Azure resource checklist

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

Structural notes:

- Triggers on `release/**` rather than a pinned `release/1.7.1`, so it survives
  version bumps. `workflow_dispatch` allows manual runs.
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

## 10. Teardown

Delete the resource group `ppdo-portal-uat-rg`. That removes every resource in
one action, and Basic-tier SQL is billed per day, so a partial month is prorated.

Also remember to:

- [ ] Delete the two GitHub secrets (§6)
- [ ] Delete `.github/workflows/deploy-uat.yml`
- [ ] Remove the UAT origin from the **production** Function App's CORS list if
      it was ever added there

---

## 11. Open questions

- Which branch should UAT track? `release/**` is assumed above, so the guide
  writer sees changes ahead of production. Pinning to `main` instead would mean
  UAT matches what users actually run — arguably better for a *user guide*.
  **Decide before building.**
- Does the guide writer need an Azure login, or only an in-app account? Only
  in-app, on the assumption above.
- Custom domain for UAT? Not assumed; the default `*.azurestaticapps.net`
  hostname is fine for internal use.

---

*Drafted 2026-08-10 — reference only, nothing provisioned.*
