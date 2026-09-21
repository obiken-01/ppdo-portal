---
status: ready — open questions resolved and ticketed 2026-09-21
version: v1.8.1 (deferred out of v1.8.0)
tickets: PPDO-112 (V18-64) · PPDO-111 (V18-65) · PPDO-113 (V18-66) · PPDO-114 (V18-67)
milestone: v1.8.1 — AIP Offline Caching Groundwork
supersedes: nothing — narrows `Phase_Plan.md` §8, which lists V18-64…69
---

# v1.8.0 Phase 6 (partial) — AIP Offline Caching Groundwork

> **Scope note (2026-09-15, Ralph):** Phase 6 in `Phase_Plan.md` §8 is six items, V18-64 through
> V18-69. **This spec covers only V18-64, V18-65, V18-66 and V18-67** — the caching/IndexedDB/perf
> groundwork. **V18-68 (upload + reconciliation) and V18-69 (session persistence + wipe policy) —
> the actual "work while disconnected, sync later" mechanics — are deliberately deferred to
> v1.8.1** and are not designed here. Nothing in this spec enables editing while offline; it only
> makes the online experience faster and more resilient to a lost connection or a crashed tab
> during an active session, and gives v1.8.1's real offline-entry work a foundation to build on.

## 1. Goal

AIP Entry re-fetches every reference list (accounts, funding sources, price index, offices,
divisions, eSRE and CC-typology codes) on every visit, even though this data
changes rarely — config pages, not daily encoding. On a shared provincial-office machine over a
weak connection, that is repeated multi-second waits for data that was identical five minutes ago.
Two further gaps compound it: a browser crash or an accidental tab close loses whatever an encoder
was mid-typing, with no recovery; and the one existing local-persistence pattern in the app (WFP's
`localStorage` draft mirror) is a per-page ad hoc solution, not a shared mechanism the rest of the
app can reuse.

This pass gives AIP Entry a read-through IndexedDB cache for reference and ceiling data (instant
render on a repeat visit, graceful degradation to stale data when offline), and a proper
IndexedDB-backed draft mirror for in-progress typing (survives a crash or an accidental close,
regardless of connectivity) — building the shared caching layer v1.8.1's real offline-entry work
will sit on top of.

## 2. Decisions (settled)

1. **IndexedDB via the `idb` npm package, not raw `indexedDB`.** The native API is callback-based,
   error-prone to hand-roll correctly (versioning, transaction lifetimes, promisifying), and no
   wrapper exists anywhere in this codebase today (confirmed — zero hits for `indexedDB`, `idb`,
   `IDBDatabase` under `frontend/src`). `idb` (Jake Archibald, ~1.2 KB gzipped) is a thin
   promise-based wrapper over the same native API, not a new storage engine or a sync framework —
   it removes boilerplate, not judgment calls. New dependency, stated in §8.
2. **Reference data uses stale-while-revalidate, not a hard TTL.** Serve whatever is cached
   immediately (near-zero render delay), then always re-fetch in the background; if the response
   differs from what's cached, update the store and re-render already-mounted pickers. Offline: the
   background re-fetch simply fails silently and the cached value keeps serving — there is nothing
   else to show. This mirrors how the existing service worker already treats static assets
   (`public/sw.js`: cache-first, revalidate in the background) rather than inventing a second
   caching philosophy for the same app.
3. **Ceiling/allocation data (V18-66) is cached differently from reference data, because it is
   live, not config.** A stale ceiling read while a colleague has just posted an expenditure could
   under-warn an encoder. Cache the last-known-good ceiling status, but **never treat it as
   authoritative** — always prefer a live fetch when online, and when serving from cache (because
   the live fetch failed or the client is offline) show it labeled with the timestamp it was read,
   not silently as current. The actual submit-time block already lives entirely server-side
   (DECISION C, `AipSubmitService`) and stays there — this cache is informational only, never a
   gate.
4. **V18-67 narrows to one concrete fix: source eSRE codes from the existing config API instead of
   the hardcoded `AIP_ESRE_OPTIONS` array in `lib/aipConstants.ts`.** Investigation for this spec
   found eSRE already has a real config table and API (`GET /config/esre-codes`, RAL-248, already
   shipped and already used as a reference pattern in the PPDO-86 build) — the frontend constant is
   dead weight that can silently drift from `AipEsreCode.AllowedValues`, the list the backend
   actually validates against. Fold it into the same reference-data cache as everything else in
   V18-65. The other candidate duplication found, `AIP_SECTOR_PREFIX` in the same file, is already
   explicitly commented as an intentional preview-only mirror (the server computes the real ref
   code) and stays as-is — not a bug, not in scope. `AIP_EXCEL_CHARS_PER_LINE`/`MAX_PRINTABLE_LINES`
   (PPDO-85) is a soft, approximate warning heuristic; turning it into a server round-trip would be
   more machinery than the feature it serves justifies — left alone.
5. **What gets cached, exactly (V18-65):** accounts, funding sources, price index (picker shape),
   offices, divisions, eSRE codes, CC typologies. ✅ **Auth confirmed for all seven, 2026-09-21** —
   every one authorises with `ConfigHttp.Authenticated`, i.e. any authenticated user, on the same
   "reference data for a picker" rationale documented on each endpoint. Nothing needs loosening.

   ↩️ **"LDIP programs" was in the phase plan's list and is dropped here** — see decision 9.
   ⚠️ **Funding sources is not a plain `<kind>` key** — see decision 8.

6. **The AIP record tree itself is NOT cached in this pass.** Only reference data, ceiling status,
   and in-progress drafts. A user who goes offline mid-session keeps whatever is already in React
   state (unaffected), but a hard reload or fresh navigation while offline will still fail — there
   is no cached copy of "my office's programs and activities" to fall back to. That is explicitly
   V18-68's job, not this one's. Pretending otherwise here would ship a spec that reads as offline
   support and isn't.
7. **The draft mirror (V18-64) follows the existing WFP pattern, not a new UX.** `wfp/page.tsx`
   already mirrors in-progress edits to `localStorage`, compares a saved timestamp against the
   server record's own `updatedAt`, and offers "Restore?" when the local copy is newer. Port that
   exact behaviour to AIP Entry's activity name/description/expected-outputs fields, on IndexedDB
   instead of `localStorage` — same UX, better storage (debounced writes off the main thread's
   synchronous-block risk, no ~5 MB ceiling, structured data instead of a string blob). This is a
   crash/accidental-close safety net that works **whether or not the user is offline** — it is not
   itself an offline-editing feature, just a good one to have regardless.

8. ⚠️ **Funding sources must be cached PER OFFICE, not globally — this is the one entry in the list
   that cannot use a bare `<kind>` key.** PPDO-109 (merged 2026-09-21, after this spec was drafted)
   made `funding_sources` office-scoped: a row with a null `office_id` is province-wide, a row with
   one belongs to that office alone, and `GET /api/config/funding-sources` now returns *shared plus
   the caller's office's* — clamped server-side, and steerable by the picker via `?officeId=` so a
   PPDO reviewer opening another office's AIP sees that office's funds.

   A cache keyed on `"funding-sources"` alone would therefore serve **office A's private funds to
   office B** on a shared provincial-office machine where two encoders use the same browser profile.
   That is a data leak introduced by caching, not present in the live path. **Key the entry
   `funding-sources:<officeId>`** (and treat "no office resolved" as its own key, never as a
   wildcard). Same question applies to any reference list that later becomes office-scoped — the
   cache key must carry every scope axis the endpoint reads.

9. ↩️ **"LDIP programs" is dropped from the cache list entirely.** Investigation 2026-09-21 found
   AIP Entry has no LDIP-programs list to cache. What it actually calls is
   `GET /budget-planning/aip/addable-programs?aipRecordId=&officeConfigId=&sector=`, and that
   response **flags which programs the AIP already carries** — so it is live AIP state wearing a
   reference-data shape, not config. Caching it would re-offer a program the office just added or
   hide one just removed, which is precisely the bug PPDO-95 was filed to fix. The other LDIP→AIP
   path, `seedAipProgramsFromLdip`, is a server-side `POST`, so there is no client list there
   either. Nothing to cache; nothing to confirm.

### ✅ Resolved 2026-09-21 (were open follow-ups)

- ~~Confirm `GET /config/offices`, `GET /config/divisions` and the LDIP-programs endpoint are
  readable by any authenticated user.~~ **Offices and divisions: confirmed**, both
  `ConfigHttp.Authenticated`. No endpoint needs loosening and no `/picker` sibling is needed.
  **LDIP programs: the question dissolved** — there is no such list to cache (decision 9).
- ~~Whether funding sources can be cached like the other reference lists.~~ **No — key per office**
  (decision 8). This was not an open question when the spec was drafted; PPDO-109 made it one.

### Open follow-ups (not blocking)

- Whether the reference-data cache should eventually generalize beyond AIP Entry (WFP, PPMP entry
  screens fetch overlapping reference lists) is left open. This spec scopes to AIP Entry only,
  since that is the only screen investigated in depth; a shared module is the natural shape either
  way, so extending its use later should not require redesigning it.
- ⚠️ **Re-check the scope axes of every cached endpoint at ticket time.** Decision 8 exists because
  an endpoint changed shape six days after this spec was written. The same could happen again
  between now and implementation — for each entry in the §4 table, confirm what the response varies
  by before choosing its cache key.

## 3. Behaviour

| Case | Given | When | Then |
|---|---|---|---|
| Happy path, repeat visit | Cache populated from an earlier visit, online | AIP Entry mounts | Reference pickers render instantly from cache; a background refresh runs and silently updates the cache + any open picker if the data changed |
| First visit, cache cold | No cache entry for this reference kind, online | AIP Entry mounts | Behaves exactly as it does today — live fetch, populate the cache once it resolves |
| Offline, cache warm | Reference data cached from an earlier session, no network | AIP Entry mounts | Pickers render from cache immediately; the background refresh fails silently, cache keeps serving, no error shown for reference data specifically |
| Offline, cache cold | Never cached this reference kind, no network | AIP Entry mounts | That picker shows its existing empty/error state (unchanged) — there is nothing to serve |
| Ceiling, online | Ceiling cache from an earlier read exists | Readiness panel loads | Live fetch wins; cache is updated with the fresh value + timestamp, panel shows the live figure with no staleness marker |
| Ceiling, offline, cache warm | A ceiling value was cached earlier this session or a prior one | Readiness panel loads, live fetch fails | Panel shows the cached figure labeled "as of `<cached time>` — may be out of date," never presented as current |
| Ceiling, offline, cache cold | No ceiling ever cached | Readiness panel loads, live fetch fails | Panel shows its existing loading-failed state (unchanged) |
| Draft recovery | An encoder was typing an activity's name/description/expected outputs, then the tab crashed or closed before saving | They reopen the same activity later (same browser, same device) | If the IndexedDB draft's timestamp is newer than the field's last-saved value, prompt "A local draft was saved at `<time>`. Restore it?" — same UX as the WFP pattern |
| Draft cleared | A draft exists for an activity | The encoder successfully saves that activity (via the normal Save button) | The IndexedDB draft entry for that activity is deleted — a stale draft must never resurface after a real save |
| Edge: two tabs, same activity | Encoder has the same activity open in two tabs | Both type into the name field | Each tab keeps its own draft keyed by activity id — the last one to save wins for the *server* record, same as today; the draft mirror does not attempt cross-tab merging (that is V18-71's concurrency problem, not this one's) |
| Role: any authenticated AIP Entry user | Staff, Admin, or SuperAdmin with `CanAccessBudgetPlanning` | Any of the above | Caching behaviour is identical for every role — this is a performance/resilience layer, not a permission surface. Cached reference data is the same global config every role already reads live today |

## 4. API contract

No new backend endpoints. This spec is a client-side caching layer over endpoints that already
exist:

✅ **Auth verified 2026-09-21** against the handlers, not assumed.

| Cached data | Existing endpoint | Auth | Cache key |
|---|---|---|---|
| Accounts | `GET /api/config/accounts` | Any authenticated user | `accounts` |
| **Funding sources** | `GET /api/config/funding-sources` | Any authenticated user | ⚠️ **`funding-sources:<officeId>`** — the response is office-scoped (decision 8) |
| Price index (picker) | `GET /api/config/price-index/picker` | Any authenticated user | `price-index` |
| eSRE codes | `GET /api/config/esre-codes` | Any authenticated user | `esre-codes` |
| CC typologies | `GET /api/config/cc-typologies` | Any authenticated user | `cc-typologies` |
| Offices | `GET /api/config/offices` | ✅ Any authenticated user (`ConfigHttp.Authenticated`) | `offices` |
| Divisions | `GET /api/config/divisions` | ✅ Any authenticated user (`ConfigHttp.Authenticated`) | `divisions` |
| ~~LDIP programs~~ | — | — | ↩️ **Dropped** — no such list exists to cache (decision 9) |
| Ceiling status | `GET /api/budget-planning/aip/{aipId}/ceiling` | JWT + office scope (existing) | `<aipId>`, separate store |

No request or response shape changes on any of these — the cache stores exactly what each endpoint
already returns today.

⚠️ **The cache key must carry every axis the response varies by**, which is not always the
endpoint path. Funding sources is the worked example: same URL, different body per caller's office.
Getting this wrong does not look like a bug in testing — it looks correct until two offices share a
browser profile.

## 5. Data model changes

None server-side. All storage is browser-local (IndexedDB), not a database change, so no
migration and no `dotnet ef database update` step.

**IndexedDB schema** (client-side, one database, versioned):

- **Database name:** `ppdo-aip-cache`
- **Object store `reference-data`:** key = `<kind>` (e.g. `"accounts"`, `"esre-codes"`) — or
  `<kind>:<scope>` for a scoped list, currently only `funding-sources:<officeId>` (decision 8).
  Value = `{ data: T[], fetchedAt: string (ISO) }`
- **Object store `ceiling-cache`:** key = `<aipId>`, value = `{ status: AipCeilingStatus,
  fetchedAt: string (ISO) }`
- **Object store `activity-drafts`:** key = `<activityId>`, value =
  `{ name, esreCode, implementingOffice, startDate, endDate, expectedOutputs, savedAt: string (ISO) }`

## 6. UI states

### AIP Entry — reference-data pickers (accounts, funding sources, price index, eSRE)

| State | Content |
|---|---|
| Loading (cache cold, first ever visit) | Unchanged from today — existing skeleton/disabled-picker state |
| Loading (cache warm) | Not applicable — cache serves synchronously, no loading state is shown |
| Empty | Unchanged — existing "no results" copy per picker |
| Error | Unchanged for the cold-cache case; **new**: if a background refresh fails while a cached value is already showing, do nothing visible — no error toast for a silent background failure |
| Success | Picker populated, either from cache (instant) or from the live fetch (as today) |
| Read-only / forbidden | Unaffected — this is a read cache, not a permission surface |
| Validation | Unaffected |

### AIP Entry — ceiling / readiness panel

| State | Content |
|---|---|
| Loading | Unchanged — existing skeleton |
| Live figure available | Current behaviour, no change |
| Cached figure shown (live fetch failed or offline) | **New**: figure renders as today, plus a small caption "as of `<relative time>`" in `text-amber-700`, matching the existing amber-caution token used elsewhere for non-blocking staleness/warning copy (e.g. `AipActivityNameCounter`) |
| No figure available at all (cold cache + fetch failed) | Unchanged — existing empty/error state |

### AIP Entry — draft restore prompt

| State | Content |
|---|---|
| Draft found, newer than last save | A dismissible inline banner above the activity's edit form: "A local draft from `<time>` was found for this activity. Restore it? [Restore] [Discard]" — matches the WFP pattern's wording and placement |
| No draft, or draft is older than the last save | No banner — normal edit form |
| Draft discarded | Deleted from IndexedDB immediately, banner disappears, form shows the server value |

Components: no new shared `components/ui/` component required — the draft-restore banner is a
small inline block, following the same inline pattern WFP already uses rather than promoting it to
a shared component prematurely (only one consumer exists in this pass).

## 7. Non-goals

- **Editing while offline.** Nothing in this spec lets an encoder create or modify an activity with
  no connection — every write still requires a live `PUT`/`POST`. That is V18-68, deferred.
- **Caching the AIP record tree itself** (programs/projects/activities/expenditures for the current
  office). Only reference data, ceiling status, and in-progress drafts are cached. A reload while
  offline still fails for the record content.
- **Session wipe / "sign out and clear local work" policy** (V18-69) — deferred with V18-68.
- **Cross-tab or cross-device draft sync.** A draft written in one browser tab/device is not visible
  from another; the mirror is local-storage-shaped, not a sync service.
- **A shared cache module used by WFP or PPMP entry.** This pass wires the cache into AIP Entry
  only; whether to retrofit WFP's existing `localStorage` draft pattern onto the same IndexedDB
  module is a separate, later decision (see Open follow-ups).
- **Cache eviction / size limits.** Reference data here is small (the price index is the largest at
  ~6,400 rows) and drafts are per-activity text fields — no eviction policy is needed at this scale.
  Revisit if that assumption stops holding.

## 8. Deployment notes

- **New npm dependency:** `idb` (add to `frontend/package.json`). No backend dependency changes.
- No migrations, no environment variables, no Azure configuration changes.
- Safe to deploy independently of anything else — purely additive, and if the `idb` wrapper fails
  to open a database for any reason (private browsing, disabled storage), every code path must fall
  back to today's live-fetch-only behaviour rather than breaking the page. State this fallback
  requirement explicitly in the ticket.

## 9. Ticket split

| Ticket | Scope | Blocked by |
|---|---|---|
| **V18-64** — PPDO-112 | IndexedDB draft mirror for AIP Entry's activity descriptive fields (name, eSRE, implementing office, dates, expected outputs), restore-prompt UX ported from the WFP pattern | — |
| **V18-65** — PPDO-111 | Shared reference-data cache module (`idb`-backed), wired into AIP Entry for accounts, funding sources, price index, offices, divisions, CC typologies. ⚠️ Funding sources keys per office (decision 8) | — |
| **V18-66** — PPDO-113 | Ceiling/allocation cache with explicit staleness labeling, wired into the readiness panel | PPDO-111 (shares the cache module) |
| **V18-67** — PPDO-114 | eSRE codes sourced from `GET /config/esre-codes` via the same cache instead of the hardcoded `AIP_ESRE_OPTIONS` array | PPDO-111 |

V18-65 is the natural first ticket (the shared module), with 66 and 67 building on it. V18-64 (the
draft mirror) is independent and can land in either order.

## 10. Acceptance checklist

- [ ] Opening AIP Entry a second time (same browser, reference data unchanged) renders the account,
      funding-source, price-index and eSRE pickers with no visible loading delay
- [ ] Changing an account's name in Configuration, then reopening AIP Entry, shows the updated name
      within one page load (background refresh caught it) without a manual cache-clear
- [ ] Disconnecting the network, then reopening a previously-visited AIP Entry session, still shows
      the reference pickers populated (from cache) rather than empty or erroring
- [ ] With the network disconnected, the ceiling figure shows a visible "as of `<time>`" caption
      instead of presenting a stale number as current
- [ ] Typing into an activity's name field, then closing the tab without saving, and reopening the
      same activity later shows a "Restore draft from `<time>`?" prompt with the typed text intact
- [ ] Clicking Restore fills the field with the draft text; clicking Discard clears the draft and
      leaves the server's last-saved value showing
- [ ] Successfully saving an activity clears any draft that existed for it — reopening afterward
      shows no restore prompt
- [ ] The `AIP_ESRE_OPTIONS` hardcoded array is gone from `lib/aipConstants.ts`; the eSRE picker's
      options come from the cached config list and match what Configuration → eSRE Codes shows
- [ ] ⚠️ **Two offices, one browser profile:** sign in as an encoder in office A, open AIP Entry so
      the fund picker caches, sign out, sign in as an encoder in office B, open AIP Entry — B must
      **not** see A's office-only funds in the picker. This is the check that catches a
      `funding-sources` cache key missing its office (decision 8), and it passes trivially if you
      only ever test with one account
- [ ] With `idb` unable to open a database (simulate via a private/incognito window with storage
      disabled, if reproducible, or a mocked failure), AIP Entry still loads and functions —
      falling back to live fetches with no draft mirror, not a broken page

## 11. Test focus

No frontend test runner currently exists in this repo (`frontend/package.json` has no `test`
script, no Vitest/Jest dependency — confirmed during PPDO-85). The pieces of this spec that are
pure logic and would benefit most from real tests if a runner is added at ticket time:

- The stale-while-revalidate merge logic (does the cache actually get replaced only when the fresh
  response differs; does a failed background fetch leave the cache untouched)
- The draft-vs-server "which is newer" comparison (mirrors the WFP pattern's existing untested
  logic — worth covering both here since it's being ported, not copied blind)
- The ceiling staleness-label threshold (if any — e.g. "don't bother labeling something fetched 10
  seconds ago")
- ⚠️ **Cache-key construction for a scoped list** — that `funding-sources` resolves to a
  different key per office, and that a missing/unresolved office produces its own key rather than
  colliding with a real office's entry

Flag the test-runner question to Ralph at ticket time rather than deciding it inside this spec —
same open point raised for PPDO-85's threshold math.
