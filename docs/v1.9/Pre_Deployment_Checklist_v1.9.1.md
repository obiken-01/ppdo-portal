---
type: checklist
version: v1.9.1
status: current
tickets: [PPDO-231, PPDO-235, PPDO-206, PPDO-44, PPDO-236, PPDO-234]
updated: 2026-10-20
status_note: "final — release/1.9.1 frozen 2026-10-20"
---

# v1.9.1 — Pre-Deployment Checklist

Up: [v1.9 hub](README.md)

> Work through this **before** `release/1.9.1` goes to UAT, and again before it goes to `main`.
> Pushing to `uat` or `main` auto-deploys both frontend and backend.
>
> v1.9.1 is cut from the frozen `release/1.9.0`, so **everything in the
> [v1.9.0 checklist](Pre_Deployment_Checklist_v1.9.0.md) must already be done on the target.**
> v1.9.1 itself has **no migration** (checked at the freeze, 2026-10-20). Re-check before each deploy:
>
> ```bash
> git diff --name-only origin/release/1.9.0...origin/release/1.9.1 -- backend/PPDO.Infrastructure/Data/Migrations | grep -v Designer | grep -v Snapshot
> ```
>
> What it does have is a **runtime change**: the backend moves from .NET 9 to **.NET 10 isolated**
> (PPDO-231). The code and the Function App's .NET setting must move together, in the same deploy.

## 0. Before you start

- [ ] **The Function App runs on Windows.** Portal → Function App → **Overview** → *Operating System*
  must read **Windows**. ⚠️ If it reads **Linux**, STOP: .NET 10 is not offered on the Linux Consumption
  plan (.NET 9 is the last), and moving needs a Flex Consumption migration first. Check both apps:
  `ppdo-portal-api-uat` and `ppdo-portal-api-sea`.
- [ ] **.NET 10 Isolated is offered.** Configuration → **General settings** → *.NET version* lists
  **.NET 10 Isolated**. (Microsoft lists .NET 10 isolated as GA on Functions 4.x, supported until
  2028-11-14.)
- [ ] **Runtime version is `~4`.** Configuration → *Function runtime settings*. Nothing else changes.

## 1. Function App runtime → .NET 10 Isolated (PPDO-231)

**Order: code first, then the setting** (Microsoft's documented order: publish the updated app,
then change the stack setting). The setting change restarts the app, typically 30–60 s.

⚠️ Between the code landing and the setting changing, the API may not serve: the new build targets
.NET 10 and the app may still be launching it as .NET 9. Keep that gap short, and do it outside
office hours on production.

### UAT (`ppdo-portal-api-uat`), when `release/1.9.1` merges to `uat`

- [ ] **Edit `deploy-uat.yml` in the same push as the merge.** It exists only on `uat`, so the
  merge does not carry the change. In `.github/workflows/deploy-uat.yml`, under `setup-dotnet`:

  ```diff
  -          dotnet-version: '9.0.x'
  +          dotnet-version: '10.0.x'
  ```

  Without it the UAT publish step fails (the .NET 9 SDK cannot build `net10.0`). That failure is
  safe, nothing is deployed, but the deploy does not happen.
- [ ] **Push to `uat`** and watch *Deploy UAT* → *Deploy Functions to Azure (UAT)*.
- [ ] **As soon as the "Deploy to Azure Functions via ZIP deploy" step is green**, while the smoke
  test is still polling (it waits up to 5 minutes): Configuration → General settings →
  *.NET version* → **.NET 10 Isolated** → **Save** → **Continue**.
- [ ] **The smoke test step goes green.** If it ran out of time before the restart finished,
  re-run the job (*Re-run failed jobs*); it redeploys the same commit and polls again.
- [ ] **Record what happened** in PPDO-231 (did the API serve between the ZIP deploy and the setting
  change? how long until health was 200?). PPDO-234 copies the order proven here.

### Production (`ppdo-portal-api-sea`), when v1.9.x merges to `main`

- [ ] **Production is already on .NET 10.** PPDO-234 (the v1.7.5 hotfix on `main`) moves production
  before v1.9.x gets there, so this deploy should find it done. Check, don't repeat:
  Configuration → General settings → *.NET version* reads **.NET 10 Isolated**, and
  `deploy.yml` on `main` already has `dotnet-version: '10.0.x'`.
- [ ] ⚠️ **If it still reads .NET 9** (PPDO-234 not deployed yet), do the UAT steps above against
  `ppdo-portal-api-sea`, with `deploy.yml` (already `10.0.x` in this release), in a quiet window.

### Rollback

No migration is involved, so a code rollback is a real rollback.

1. Configuration → General settings → *.NET version* → **.NET 9 Isolated** → Save.
2. Redeploy the previous build. **UAT:** open the last green *Deploy UAT* run from before the
   merge → **Re-run all jobs** (a re-run uses that run's commit, the .NET 9 build). **Production:**
   the same with the last green *Deploy* run on `main`.
3. `GET /api/health` returns 200.

## 2. After the deploy (UAT, then production)

- [ ] `GET /api/health` → 200 with `"database":"ok"`.
- [ ] Log in; refresh after the access token expires (15 min) without being sent to the login page.
- [ ] AIP Entry, AIP Review (an office in `SubmittedToPpdo`) and the WFP report preview and
  `.xlsx` export open without errors.
- [ ] As `ppdo.finance`, request a bulk initial WFP export; it reaches **Done** and the ZIP downloads.
  In Application Insights, `Functions.ExportJobTimer` runs every 30 s and succeeds.
- [ ] **PPDO-235 — rich text.** Open an Investment Proposal with formatted text (bold, lists) as its encoder
  and as a PPDO reviewer: the formatting shows, and nothing renders blank. (Stored HTML is not rewritten; the
  browser now also cleans it with DOMPurify.)
- [ ] **PPDO-206 — AIP workflow.** As a **division** encoder, submit with a warning: ONE dialog lists every
  warning. ⚠️ This path was unit-tested but **not run live** locally (no submittable division there), so UAT is
  its first live check. As a department head, Send to PPDO always confirms. Delete an activity carrying an
  unresolved comment: the comment stays, labelled "On a deleted activity".
- [ ] **PPDO-44 — config caching.** Edit a division, office or funding source in Config, then open a screen
  that shows it: the new value appears at once (no wait for the 10-min cache).
- [ ] **PPDO-236 — PPMP hidden.** Budget Planning → Report: no PPMP option; the WFP and AIP reports still
  work, and the WFP export downloads (this click was not made live during the ticket).
- [ ] Application Insights → Failures: no new exception types since the deploy. ⚠️ Watch for SQL
  errors in particular: EF Core 10 translates some queries differently (for example `.Contains()`
  on a list now sends one parameter per item), and the tests run on SQLite, not SQL Server.

## Rollback summary

| Layer | How |
|---|---|
| Runtime + code | .NET version back to **.NET 9 Isolated**, then re-run the previous green deploy |
| Database | Nothing to roll back (no v1.9.1 migration) |

## Related

- [v1.9.0 — Pre-Deployment Checklist](Pre_Deployment_Checklist_v1.9.0.md) — checklist, final (must be done first)
