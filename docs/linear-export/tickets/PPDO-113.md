---
id: PPDO-113
title: 'V18-66 — Ceiling cache with explicit staleness labelling'
status: Done
project: PPDO Portal
labels: 
milestone: v1.8.2 — Dashboard Redesign, Caching & Remaining Optimizations
created: 2026-09-21
completed: 2026-10-07
parent: (none)
blocked_by: PPDO-111
related: PPDO-85
url: https://linear.app/ralphoksiprojects/issue/PPDO-113
uuid: 06287c19-4162-41a7-a3bc-5864326af23e
---

# PPDO-113 — V18-66 — Ceiling cache with explicit staleness labelling

Spec: `docs/v1.8/AIP_Offline_Caching_Spec.md` — decision 3; §3 rows "Ceiling, online / offline warm / offline cold"; §5; §6.

**Blocked by** [PPDO-111](https://linear.app/ralphoksiprojects/issue/PPDO-111/v18-65-shared-reference-data-cache-idb-wired-into-aip-entry) **(V18-65)** — shares its cache module and database.

## Context to read first

* `docs/v1.8/AIP_Offline_Caching_Spec.md` decision 3 — read this one carefully, it is the whole ticket
* `docs/v1.8/Phase_Plan.md` DECISION C — the ceiling blocks **at submit**, server-side

## Files to read before coding

* The readiness / ceiling panel in AIP Entry
* `backend/PPDO.Application/Services/AipCeilingService.cs` and `AipSubmitService` — enough to confirm where the real gate lives

## Why this is not just "cache another list"

⚠️ **Ceiling data is live, not config.** Every other thing V18-65 caches changes on a config page; a ceiling changes when a colleague posts an expenditure. A stale ceiling read can **under-warn an encoder** — showing headroom that was spent ten minutes ago.

So the caching rule inverts: for reference data, cache-first is correct. Here, **always prefer the live fetch**, and cache only as a fallback for when it fails.

## Target behaviour

| Given | When | Then |
| -- | -- | -- |
| Online | Readiness panel loads | Live fetch wins; cache updated with the fresh value + timestamp; **no staleness marker** |
| Offline, cache warm | Live fetch fails | Cached figure shown **labelled** "as of `<relative time>`", in `text-amber-700` — the existing amber-caution token, matching `AipActivityNameCounter` |
| Offline, cache cold | Live fetch fails | Existing loading-failed state, unchanged |

⚠️ **Never present a cached ceiling as current.** The label is the point of the ticket, not a nicety.

⚠️ **This cache is informational only and must never become a gate.** The submit-time block lives entirely server-side in `AipSubmitService` (DECISION C) and stays there. If this ticket ends up influencing whether a submit succeeds, something has gone wrong.

## Steps

1. Object store `ceiling-cache`, key `<aipId>`, value `{ status, fetchedAt }`.
2. Live-first read: fetch, and on success update the cache and render unlabelled.
3. On fetch failure, fall back to the cached value **with** the timestamp caption.
4. Cold cache + failure: existing error state, untouched.

## Open question for implementation time

Whether to suppress the label below some freshness threshold — e.g. don't caption something fetched ten seconds ago. The spec deliberately leaves this open. Raise it with Ralph rather than picking a number silently; same call made for [PPDO-85](https://linear.app/ralphoksiprojects/issue/PPDO-85/long-aip-activity-names-keep-line-breaks-on-screen-grow-the-entry-box)'s threshold math.

## Out of scope

Any change to the server-side submit gate. Caching allocations independently of ceiling status. Making the cached value authoritative for anything.

## Acceptance

- [ ] With the network disconnected, the ceiling figure shows a visible "as of `<time>`" caption instead of presenting a stale number as current
- [ ] Online, the figure renders with no caption and the cache silently refreshes
- [ ] Cold cache + failed fetch shows the existing error state, not a blank or a zero
- [ ] Submitting an AIP is unaffected in every case — the server gate still decides
