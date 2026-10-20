---
id: PPDO-21
title: 'UAT environment — Azure resources + deploy workflow (for user-guide authoring)'
status: Done
project: PPDO Portal
labels: 
milestone: v1.8.0 — Office Users, AIP Redesign & Reviewer Flow
created: 2026-08-09
completed: 2026-09-21
parent: (none)
blocked_by: (none)
related: RAL-202, RAL-274, RAL-275
url: https://linear.app/ralphoksiprojects/issue/PPDO-21
uuid: e8255902-8f1e-417b-815d-91ea9e1703fc
---

# PPDO-21 — UAT environment — Azure resources + deploy workflow (for user-guide authoring)

> ↩️ **Reopened 2026-09-21 — cancelled 2026-08-12, and the reason it was cancelled no longer holds.**
>
> **The purpose changed.** This was filed so a colleague could click through the app while writing the end-user guide. Ralph's call today: stand it up so **people can test v1.8.0 before it reaches production**. That is a stronger case than the original one, and it is time-sensitive — `release/1.8.0` is complete and holds the largest change the project has made.
>
> **Two of the three open questions below are now settled by that change of purpose:**
>
> * **Which branch?** ✅ `release/**`**.** The whole point is exercising v1.8.0 *before* it merges; `main` is still v1.7.4 and would show testers nothing new. The workflow draft in §7 already assumes this.
> * **Azure login for the tester?** ✅ **No** — in-app account only.
> * Custom domain: still no, `*.azurestaticapps.net` is fine.
>
> **⚠️ The blocking code change is DONE** ([PPDO-21](https://linear.app/ralphoksiprojects/issue/PPDO-21/uat-environment-azure-resources-deploy-workflow-for-user-guide) branch, PR pending): `NEXT_PUBLIC_NOINDEX` now gates **four** SEO surfaces, not the two §8 named — `robots.ts`, `sitemap.ts`, the root layout's `robots` metadata, and by extension `metadataBase`. Verified by building both ways and reading the real output: with the flag set, robots.txt is `Disallow: /` with no sitemap line, sitemap.xml is empty, every page carries `<meta name="robots" content="noindex, nofollow">`, and **no production URL appears anywhere in the build**. With the flag unset, production output is byte-for-byte what [RAL-202](https://linear.app/ralphoksiprojects/issue/RAL-202/seo-make-the-public-site-discoverable-and-indexable-by-search-engines) shipped.
>
> **⚠️ Do NOT add** `.github/workflows/deploy-uat.yml` **until the Azure resources exist.** It triggers on `release/**`, which includes the branch active right now — committing it with placeholder hostnames would fail on every push.
>
> **⚠️ The seed-data question in §9 needs revisiting for this purpose.** §9 was written for a user guide: "a small AIP + WFP set". Testing the v1.8.0 AIP redesign needs more structure than that — offices, divisions, an office ceiling, an FY2028 AIP record with programs seeded from an LDIP, and at least two office accounts so the cross-office scope rules can actually be exercised. **The "do not restore production" rule still stands** (§1): real personnel names and budget figures on a second internet-facing instance is the thing this environment exists to avoid.
>
> ---

Stand up a UAT instance so a colleague can click through the app while writing the end-user guide, without touching production data.

**Deferred — Ralph's call 2026-08-10: documented now, probably not built for a while.** Full setup reference: `docs/UAT_ENVIRONMENT_SETUP.md` (drafted, nothing provisioned).

## Why not just use prod

Production holds real personnel names and real budget figures, and `obiken-01/ppdo-portal` is a **public repository**. Guide screenshots taken against prod would carry that data wherever the guide goes. Seeding UAT with fabricated data is safer and makes a cleaner guide. Secondary benefit: the guide writer can't create junk records in prod.

## Cost — approved as acceptable

\~**$5/month**, and only the database costs anything. Static Web App, Functions (Consumption), storage, and App Insights all sit inside free grants.

Azure SQL is the exception: the free offer is **one database per subscription** and `ppdo-portal-db` holds it — that database also exhausted its 100K vCore-second allowance on 2026-07-15 and has been on pay-as-you-go overage since.

Recommended tier is **Basic** ($0.161/day ≈ $4.90/month, rates confirmed live against Azure's retail prices API for `southeastasia` on 2026-07-22). Because it bills daily, a three-week engagement is \~$3.40 — delete the resource group when the guide is done. Watch for two things: 5 DTU may throttle the AIP tree / price index / report-generation paths the guide writer will be exercising, and 2GB is a hard cap. Serverless GP (prod's tier) is the fallback at \~$12.40/month.

## ⚠️ Blocking prerequisite — a code change, not just infra

[RAL-202](https://linear.app/ralphoksiprojects/issue/RAL-202/seo-make-the-public-site-discoverable-and-indexable-by-search-engines) made the public site deliberately indexable, and that behaviour is currently unconditional:

* `frontend/src/app/robots.ts` always emits `allow` for the public marketing pages, with no environment check.
* `frontend/src/lib/seo.ts:13` falls back to the **production** URL when `NEXT_PUBLIC_SITE_URL` is unset.

Deploying UAT as-is gets it indexed by Google as a second public copy of the PPDO site, or emits a sitemap and canonicals pointing at prod. Needs a `NEXT_PUBLIC_NOINDEX` build flag gating `robots.ts` (plus a `noindex` meta tag as belt-and-braces) before anything goes up. Sketch in `docs/UAT_ENVIRONMENT_SETUP.md` §8.

## Scope

1. Provision the Azure resources — checklist in §4, all in their own `ppdo-portal-uat-rg` so teardown is one delete.
2. Add the `NEXT_PUBLIC_NOINDEX` flag (§8).
3. Add `.github/workflows/deploy-uat.yml` — full draft in §7. Triggers on `release/**` + `workflow_dispatch`; reuses prod's `curl` ZIP-deploy (`Azure/functions-action@v1` is blocked by this repo's Actions policy).
4. Two new GitHub secrets, separate from prod's, so a UAT misconfiguration can never deploy over production (§6).
5. Run migrations and seed fabricated data — **do not restore the prod database** (§9).

## Open question to settle before building

Should UAT track `release/**` (guide writer sees changes ahead of prod) or `main` (UAT matches what users actually run)? The workflow draft assumes `release/**`, but for a *user guide* specifically, `main` is arguably the better choice. Two other minor ones in §11.

## Not in scope

A custom domain. The default `*.azurestaticapps.net` hostname is fine for internal use.
