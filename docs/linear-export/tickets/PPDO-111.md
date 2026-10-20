---
id: PPDO-111
title: 'V18-65 — Shared reference-data cache (idb), wired into AIP Entry'
status: Done
project: PPDO Portal
labels: 
milestone: v1.8.2 — Dashboard Redesign, Caching & Remaining Optimizations
created: 2026-09-21
completed: 2026-10-05
parent: (none)
blocked_by: (none)
related: PPDO-109
url: https://linear.app/ralphoksiprojects/issue/PPDO-111
uuid: 0573e1fd-0301-4bf4-93d9-df06191d5341
---

# PPDO-111 — V18-65 — Shared reference-data cache (idb), wired into AIP Entry

Spec: `docs/v1.8/AIP_Offline_Caching_Spec.md` — decisions 1, 2, 5, 8; §3 rows 1–4; §4; §5; §6.

**This is the foundation ticket.** V18-66 and V18-67 both build on the module it creates. V18-64 is independent of it.

## Context to read first

* `docs/v1.8/AIP_Offline_Caching_Spec.md` (whole thing — it is short)
* `docs/PERFORMANCE_GUIDELINES.md` — the frontend half
* `frontend/public/sw.js` — the existing cache-first-revalidate-in-background pattern this mirrors rather than competing with

## Files to read before coding

* `frontend/src/app/(portal)/budget-planning/aip/entry/page.tsx` — the reference-data fetch block near the top (`listAccounts`, `listOffices`, `listFundingSources`, `listPriceIndexForPicker`)
* `frontend/src/lib/config.ts` — every list function this will wrap
* `frontend/package.json` — no `idb` yet, no test runner either

## Current behaviour

AIP Entry re-fetches every reference list on every visit. On a shared provincial-office machine over a weak connection that is repeated multi-second waits for data that was identical five minutes ago. Config data changes rarely — these are config pages, not daily encoding.

## Target behaviour

A read-through IndexedDB cache with **stale-while-revalidate**: serve the cached value immediately (no loading state), always re-fetch in the background, update the store and re-render open pickers only if the response differs. Offline, the background fetch fails silently and the cache keeps serving.

Cached lists: accounts, funding sources, price index (picker), offices, divisions, eSRE codes, CC typologies. ✅ All seven read as any authenticated user — verified 2026-09-21, nothing needs loosening.

## ⚠️ The one thing that will bite

**Funding sources must be keyed** `funding-sources:<officeId>`**, not** `funding-sources`**.**

[PPDO-109](https://linear.app/ralphoksiprojects/issue/PPDO-109/per-office-fund-sources-shared-province-wide-list-plus-an-offices-own) made `funding_sources` office-scoped: same URL, different body per caller's office (province-wide rows plus that office's own). A global key serves **office A's private funds to office B** when two encoders share a browser profile — a leak introduced *by* caching, which the live path does not have.

Treat "no office resolved" as its own key, never a wildcard. Before choosing a key for any other list, confirm what its response varies by — the spec's §4 table records this per endpoint.

## Steps

1. Add `idb` to `frontend/package.json` (\~1.2 KB gzipped, thin promise wrapper over the native API — not a sync framework).
2. Build the cache module: open/version `ppdo-aip-cache`, object store `reference-data`, key `<kind>` or `<kind>:<scope>`, value `{ data, fetchedAt }`.
3. ⚠️ **Every path must fall back to today's live-fetch-only behaviour if IndexedDB is unavailable** (private browsing, storage disabled, quota). A broken cache must never break the page — wrap every open/read/write.
4. Wire the seven lists into AIP Entry's existing fetch block, replacing direct `lib/config.ts` calls.
5. Re-render already-mounted pickers when a background refresh changes the data.

## Risks

Low blast radius (one page, additive), with two real failure modes: the per-office key above, and a cache that throws instead of degrading. Both are covered by acceptance checks.

## Out of scope

The AIP record tree (V18-68). Ceiling caching (V18-66). eSRE sourcing (V18-67). Generalising the module to WFP/PPMP. Cache eviction — the data is small and the price index at \~6,400 rows is the largest.

## Acceptance

- [ ] Second visit renders account / funding-source / price-index pickers with no visible loading delay
- [ ] Changing an account name in Configuration then reopening AIP Entry shows it within one page load, no manual cache clear
- [ ] Offline with a warm cache: pickers populate rather than erroring
- [ ] ⚠️ **Two offices, one browser profile:** sign in as office A, open AIP Entry, sign out, sign in as office B — B must **not** see A's office-only funds. This passes trivially if you only ever test with one account
- [ ] With IndexedDB unavailable, AIP Entry still loads and functions on live fetches

## Test focus

No frontend test runner exists (`frontend/package.json` has no `test` script). Flag that to Ralph at implementation time rather than deciding it here. If one is added, the stale-while-revalidate merge logic and cache-key construction for a scoped list are the two pieces worth real tests.
