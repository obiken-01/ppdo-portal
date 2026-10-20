---
id: PPDO-190
title: 'CI: upgrade GitHub Actions off Node 20 (checkout, setup-dotnet, setup-node) and the frontend build Node version'
status: Done
project: PPDO Portal
labels: devops, Improvement
milestone: v1.8.2 — Dashboard Redesign, Caching & Remaining Optimizations
created: 2026-10-05
completed: 2026-10-05
parent: (none)
blocked_by: (none)
related: (none)
url: https://linear.app/ralphoksiprojects/issue/PPDO-190
uuid: 5d18866f-12a7-4680-a5a3-55dfeaf0a83e
---

# PPDO-190 — CI: upgrade GitHub Actions off Node 20 (checkout, setup-dotnet, setup-node) and the frontend build Node version

## Why

Every UAT/CI run now carries the annotation *"Node.js 20 is deprecated. The following actions target Node.js 20 but are being forced to run on Node.js 24: actions/checkout@v4, actions/setup-dotnet@v4"*. It is **not failing** (the 2026-10-05 UAT deploy was green on both jobs) — GitHub is forcing those actions onto Node 24 as a stopgap. When GitHub removes Node 20 support outright, anything still on the old majors can break the deploy path. Check the changelog linked in the annotation for the exact removal date.

Found while deploying the price-index PGOM import to UAT (PR [obiken-01/ppdo-portal#441](https://linear.app/ralphoksiprojects/review/release-181-uat-price-index-pgom-import-name-unit-stock-card-key-1ba2572aa1c2)). Deliberately **not** done in v1.8.1: that release is frozen, and a workflow change reaches UAT and production deploys untested.

## Scope

Files: `.github/workflows/ci.yml`, `.github/workflows/deploy.yml`.

| Action | Now | Target | Notes |
| -- | -- | -- | -- |
| `actions/checkout` | `@v4` (3 uses) | `@v5` or newer | verified `runs.using: node24` |
| `actions/setup-dotnet` | `@v4` (2 uses) | `@v5` or newer | verified `node24` |
| `actions/setup-node` | `@v4` (1 use, `ci.yml`) | `@v5` or newer | verified `node24` |
| `azure/static-web-apps-deploy` | `@v1` | no change | Docker action — not affected |

Separate but related: `ci.yml` sets `node-version: '20'` for the **frontend build**. That is the app's own Node version, not the deprecation warning, and Node 20 reached end of life in April 2026. Move it to 22 (LTS) in the same change **only if** `npm run build` and `npm run lint` pass on 22 — Next.js 14 builds can be sensitive to the Node version. If it fails, split it into its own ticket rather than blocking the action upgrades.

## Steps

1. Branch `feature/v1.8.2-ppdo-XXX-github-actions-node24` off `release/1.8.2`; first commit bumps `APP_VERSION` if this is the first on the branch (see the v1.8.2 milestone note).
2. Bump the action majors in both workflow files.
3. Bump `node-version` 20 → 22 in `ci.yml`.
4. Open the PR and confirm the **CI** run is green and the Node 20 annotation is gone.

## Verification

* PR CI run: both jobs green, no "Node.js 20 is deprecated" annotation.
* Merge `release/1.8.2` → `uat` **first** and watch one full deploy (Functions + SWA) go green before it ever reaches `main`. A workflow bug only shows on a real deploy.
* Confirm the deployed UAT site still builds with the same frontend output (spot-check a page).

## Risks / rollback

* Low code risk, but these files control UAT and production deploys. Rollback is reverting the commit; the old versions still run today.
* `checkout@v5+` needs a recent runner — fine on GitHub-hosted `ubuntu-latest`.
* If Node 22 breaks the Next 14 build, revert only that line.

## Out of scope

* Changing the Azure SWA deploy action or the Functions publish step.
* Upgrading .NET (`9.0.x`) or Next.js.
* The `ubuntu-latest` → Ubuntu 26 migration notice (GitHub announced it for 2026-10-19) — worth a glance in the same PR, but only act on it if a build actually breaks.

## Sooner if

The annotation starts showing a removal date, or a deploy fails with a Node-related error.
