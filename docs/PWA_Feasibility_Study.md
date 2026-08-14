# Progressive Web App — Feasibility Study

> Study date: **2026-08-14**. Branch: `claude/progressive-web-apps-9kym78`. Baseline: `v1.7.2`.
> Scope requested by Ralph: *"can we implement this and what needs to be done — study only, no code change."*
>
> **No production code was changed by this study.** One file (`src/app/manifest.ts`) was created
> temporarily to verify a build behaviour, then deleted — see §10 for the verification log.
> Nothing here is committed to a milestone.
>
> **Addendum 2026-08-14 — §11.** Ralph raised a concrete v1.8.0 driver: users install the web app,
> **do AIP creation work locally with no server round-trip, and upload it later.** AIP creation is
> itself being reworked in v1.8.0 (details to follow), so §11 records the constraints the rework
> must be designed against rather than a design for the flow as it stands today.

---

## 1. Verdict

**Yes — and the expensive prerequisites are already paid for.** The frontend is a pure static
export served over HTTPS from a CDN, which is the single easiest possible starting point for a
service worker. Phase 1 (installable + fast repeat loads + honest offline screen) is roughly a
one-ticket change with **one hard blocker: there is no logo asset large enough to install with.**

The part people usually *mean* by "PWA" — **use it offline** — is **not** cheap here, and no
amount of service-worker work delivers it on its own. The portal's session model requires a
network round-trip on every single launch (§5). That is a deliberate security design, not a bug,
and unwinding it is a much bigger decision than adding a manifest.

### 1.1 Headline findings

| # | Finding | Impact | Where |
|---|---|---|---|
| 1 | No icon asset ≥ 512×512 exists. Largest is the **placeholder** logo at 256×256. Chrome will not offer install without a 512px icon. | **Blocker (Phase 1)** | `public/images/`, §4.1 |
| 2 | Every portal launch requires a live `POST /auth/refresh`. Offline launch → `/reconnecting`, never the app. Caching HTML/JS does not create a session. | **Caps the whole feature** | `(portal)/layout.tsx:90-144`, §5 |
| 3 | Refresh cookie is a **third-party cookie** (`SameSite=None` to a different origin). Safari/iOS blocks these by default — "stay logged in" may already be broken on iPhone, before any PWA work. | **High — verify on device** | `AuthFunctions.cs:206`, §5.3 |
| 4 | `app/manifest.ts` works under `output: "export"` — **verified by building it**. Emits `/manifest.webmanifest` and auto-injects `<link rel="manifest">` into all 44 pages. | ✅ Enabler | §10 |
| 5 | The API is **cross-origin** in production, so every API call passes through the SW's fetch handler as a cross-origin request. The SW must explicitly skip it — the default "cache everything" recipe would be both useless (opaque responses) and a data-leak risk. | Design constraint | `deploy.yml`, §6 |
| 6 | `staticwebapp.config.json` has no `mimeTypes` block and no cache header for `/sw.js`. A CDN-cached stale service worker can pin users to an old build. | Config gap | `staticwebapp.config.json`, §7 |
| 7 | Responsive shell (RAL-187 drawer) already shipped — 30 files in `(portal)/` now carry responsive utilities, up from 14 at the July audit. An installed app will not look broken on a phone. | ✅ Enabler | §3 |
| 8 | `APP_VERSION` is hardcoded in **three** places, one of them a bare string literal. A SW update prompt needs one shared constant. | Cleanup | §4.4 |

---

## 2. What a PWA is, in this project's terms

A Progressive Web App is the existing website plus three additive browser capabilities. Nothing
is replaced; the site keeps working exactly as it does now for anyone who ignores it.

| Capability | Mechanism | What PPDO would actually get |
|---|---|---|
| **Installable** | `manifest.webmanifest` — name, icons, colours, launch mode | Portal icon on a phone home screen / Windows Start menu. Opens without address bar or tabs. Looks and launches like an app. |
| **Offline / caching** | **Service worker** — a script the browser runs in the background, independent of any page, that can intercept every network request | Instant repeat loads (assets served from disk, not Azure). A real "you're offline" screen instead of a spinner. *Offline data — see §5.* |
| **Push notifications** | Service worker + Push API + backend push service | PR-approval and low-stock alerts to a phone even with the portal closed. Requires backend work; see §8, Phase 3. |

The service worker is the engine for all three. Note that Chrome will not offer installation from
a manifest alone — it wants a registered service worker **with a `fetch` handler**. So even an
"install only, no offline" scope still ships a service worker.

### 2.1 Why this is worth considering here specifically

- **Azure Functions cold start.** The Consumption plan scales to zero after ~10 minutes; the first
  request after that takes 5–20 s. That's a backend problem a SW can't fix — but a SW *can* remove
  the frontend's ~2.9 MB of JS from the critical path on every repeat visit, so the app shell
  paints instantly while the API wakes up. Today both happen serially over the network.
- **Provincial connectivity.** Field/municipal users on intermittent mobile data currently get a
  blank page or a spinner on a flaky connection. A cached shell degrades far more gracefully.
- **No app store.** Install is a browser action. No Play Store account, no Apple Developer
  Program, no review, no separate codebase, no MDM. For a provincial office this is the only
  realistic route to a home-screen icon.

---

## 3. What is already in place

Genuinely unusual how much of the groundwork is done:

| Requirement | Status | Evidence |
|---|---|---|
| HTTPS | ✅ | Azure Static Web Apps serves TLS by default. Service workers refuse to register otherwise. |
| Static asset pipeline | ✅ | `next.config.mjs:3` — `output: "export"`. Everything is a plain file on a CDN. No SSR, no edge runtime, no revalidation semantics to reason about. |
| Content-hashed filenames | ✅ | `out/_next/static/chunks/*` are all `name-<hash>.js`. Immutable by construction → cache-first is trivially safe, and **no precache manifest is needed**. |
| Metadata routes under static export | ✅ | `src/app/robots.ts` and `src/app/sitemap.ts` already emit correctly. `manifest.ts` verified to behave identically (§10). |
| Responsive layout | ✅ | Sidebar drawer + hamburger shipped (RAL-187, `(portal)/layout.tsx:236-244`). 30 of the portal's files carry `sm:`/`md:`/`lg:` utilities. |
| Viewport meta | ✅ | Next App Router injects `width=device-width, initial-scale=1` by default; root layout doesn't override it. |
| Icon generation toolchain | ✅ | `sharp` is already a devDependency (`package.json:56`). No new dependency needed to produce icon sizes. |
| Update-prompt UI | ✅ | `ToastProvider` is already mounted in the portal shell — the natural host for a "New version available — Reload" toast. |
| Local draft persistence precedent | ✅ | WFP already persists drafts to `localStorage` (`wfp/page.tsx:819-934`). 37 storage references across the app. Offline-draft patterns are not foreign to this codebase. |

### 3.1 Measured build output (2026-08-14)

`npm run build` on this branch:

```
out/                    4.3 MB total
  *.js      78 files    2.9 MB    ← _next/static/chunks, all content-hashed
  *.html    44 files    508 KB    ← one per route
  *.css      1 file      52 KB
  images     6 files    284 KB    ← 3 PNG + 3 WebP
```

Largest single chunk: 248 KB. Shared first-load JS: 87.4 KB.

**Reading:** 4.3 MB is comfortably inside every browser's storage quota, so precaching the whole
app is *technically* fine. But precaching means downloading all of it at install time on a
provincial mobile connection, most of which the user will never visit — `/announcements` alone is
144 KB and Budget Planning is 76 KB. **Runtime caching (cache-as-you-go) is the better fit here**
and is what §6 recommends.

---

## 4. What is missing

### 4.1 Icons — the one real Phase 1 blocker

Current assets, measured:

| File | Size | Notes |
|---|---|---|
| `ppdo-logo-placeholder.png` | **256×256** | The green-circle placeholder. `CLAUDE.md` states the official logo is still pending. |
| `Ph_seal_occidental_mindoro.png` | 270×270 | Provincial seal — fine detail, not designed for a 48px home-screen tile. |
| `Bagong_Pilipinas_logo.png` | 389×362 | Not square. |

A PWA needs **192×192 and 512×512** PNGs, plus ideally a **maskable** variant (Android crops
icons to a device-chosen shape — a maskable icon must keep all meaning inside a centre circle at
~80% of the width) and an `apple-touch-icon` (iOS ignores the manifest's icons for the home
screen). Nothing on hand reaches 512 without upscaling, which will look visibly soft.

Two sub-problems, and the second is the more important one:

1. **Technical:** need a ≥512×512 source. `sharp` generates every derived size from it in seconds.
2. **Institutional:** the icon currently on offer is a *placeholder*. Shipping "install this on
   your phone" and having a grey-green placeholder circle land on a provincial officer's home
   screen is a worse outcome than not shipping it. **This should wait for the official PPDO logo**,
   or use the provincial seal as a deliberate interim decision — Ralph's call.

### 4.2 The manifest

New file `frontend/src/app/manifest.ts` (~25 lines). Verified working — see §10. Open decisions in
§9.

### 4.3 The service worker

New file `frontend/public/sw.js`, plus a small registration component mounted in the root layout.
See §6 for the recommended shape and why it's hand-written rather than generated.

### 4.4 Version-constant cleanup (small, worth folding in)

The SW update flow should tell the user *which* version they're getting. Today the version string
lives in three places and has already drifted once:

| Location | Form |
|---|---|
| `components/layout/Sidebar.tsx:32` | `const APP_VERSION = "v1.7.2"` |
| `app/(public)/login/page.tsx:90` | `const APP_VERSION = "v1.7.2"` (duplicate const) |
| `components/landing/Footer.tsx:12` | `Portal v1.7.2` — **bare string literal, not a constant** |

`CLAUDE.md` already flags that these must move together. Consolidating to a single exported
constant is a prerequisite for a coherent update prompt, and closes a known drift risk anyway.

---

## 5. The auth wall — why "offline" is not free

This is the finding that determines the honest scope of the whole feature.

### 5.1 What happens today when the portal launches

From `frontend/src/lib/auth.ts:23` and the header comment above it:

```
Access token  → in-memory only. Cleared on page reload — intentional.
Refresh token → httpOnly, Secure cookie. NOT accessible to JavaScript by design.
```

So on **every** launch, the in-memory token is gone, and `(portal)/layout.tsx:90-144` must call
`POST /auth/refresh` before rendering anything. Offline, that call produces a network error,
`classifyRefreshFailure` returns `unreachable`, and the user is sent to `/reconnecting`.

**Consequence for an installed PWA:** tapping the home-screen icon with no signal shows the amber
"Reconnecting to the server…" card, retries twice with backoff, then offers "Try Again / Cancel".
It never reaches the app. **A service worker cannot change this** — it can serve the cached HTML
and JS instantly, but the JS's first act is to demand a network round-trip it cannot fake.

### 5.2 What it would take to change

Any of these is a security decision, not a coding task:

- Cache the `/auth/me` response and let the shell render from it while offline — accepting that a
  revoked or expired account still renders a usable-looking UI until reconnection.
- Persist a session marker outside memory — directly contradicting the documented threat model in
  `auth.ts:13` ("Never store tokens in localStorage or sessionStorage where injected third-party
  scripts could read them").
- Keep the auth wall and scope offline to **read-only cached data behind a re-login**, i.e. the
  app opens, shows "offline — showing data from 3:40 PM", and blocks all writes.

The third is the only one that doesn't weaken the current model, and it still needs §6.2's
data-at-rest decision answered first.

### 5.3 iOS-specific risk — verify before promising anything

Two iOS behaviours interact badly here, and both need checking on a real device:

1. **The refresh cookie is third-party.** `AuthFunctions.cs:206` sets it with `SameSite=None`
   because the frontend (`*.azurestaticapps.net`) and the API (`*.azurewebsites.net`) are
   different sites — the code comment says exactly this. Safari blocks third-party cookies by
   default. If that block applies, **silent refresh already fails on iPhone today**, independent
   of any PWA work, and every reload forces a re-login. Chrome's own third-party cookie
   restrictions point the same direction over time.
2. **Installed iOS PWAs have a separate storage jar from Safari.** Cookies and storage are not
   shared with the browser, so a user already logged in via Safari will have to log in again
   inside the installed app. Expected behaviour, but it will be reported as a bug if nobody says
   it up front.

The durable fix for (1) is a **custom domain** putting frontend and API on the same site
(e.g. `portal.ppdo.gov.ph` + `api.ppdo.gov.ph` with a shared parent), which makes the cookie
first-party. That is worth doing on its own merits and is arguably a prerequisite for taking a
mobile-install story seriously. `seo.ts:14` already anticipates a custom-domain move.

---

## 6. Implementation approach

### 6.1 Recommended: a hand-written service worker

| Option | Assessment |
|---|---|
| **Hand-written `public/sw.js`** ✅ **recommended** | ~80–120 lines, zero new dependencies, fully auditable, no build-step coupling. Works precisely because `_next/static/*` filenames are content-hashed, so runtime cache-first needs no generated precache manifest. |
| `@ducanh2912/next-pwa` | Maintained `next-pwa` fork with App Router support. Injects a Workbox webpack plugin. `next.config.mjs` is currently **12 lines with no custom webpack config at all** — adding a build plugin is a bigger change to this repo than the SW it generates. |
| `@serwist/next` | The modern successor; better maintained. Same objection: buys precaching sophistication this app doesn't need, at the cost of a build-pipeline dependency. |

Recommendation is the hand-written SW **for Phase 1 only**. If Phase 2/3 (offline data, background
sync, push) is ever approved, revisit — Workbox's queue and expiration plugins genuinely earn
their keep there.

Honest tradeoff of hand-rolling: **no precaching**, so the first launch after install caches only
what that session actually fetched. A user who installs and immediately goes offline gets less
than they would with Workbox precaching. Given §3.1's 4.3 MB and provincial bandwidth, that's the
right trade.

### 6.2 Caching policy — what the SW must and must not touch

| Request | Policy | Why |
|---|---|---|
| `/_next/static/*` (JS, CSS) | **Cache-first**, permanent | Content-hashed. A given URL's bytes never change. |
| `/images/*`, `favicon.ico` | **Cache-first** | Stable, small (284 KB total). |
| Navigations (`*.html`) | **Network-first**, cache fallback | Not hashed — must not pin a user to a stale build. Falls back to a cached page, then to an offline screen. |
| **Anything to the API origin** | **Never touched — skip the handler entirely** | See below. |

That last row is the important one. In production the API is
`https://ppdo-portal-api-….azurewebsites.net/api`, baked in at build time by `deploy.yml` — a
**different origin** from the SWA site. Cross-origin requests still reach the SW's `fetch`
handler, so a naive "cache all GETs" recipe would try to cache them. Two reasons not to:

1. **It wouldn't work.** Cross-origin responses without permissive CORS are *opaque* — status and
   body unreadable to the SW, and they consume padded quota. Useless as a cache.
2. **It shouldn't work.** Caching API responses writes provincial budget figures, purchase
   requests, and personnel data into Cache Storage as **plaintext, readable by any script on the
   origin, surviving logout**, on machines that in a provincial office are frequently shared. That
   directly contradicts the threat model `auth.ts` is built around — the app currently refuses to
   put even the *access token* in `localStorage`.

**Therefore: Phase 1 caches static assets only, never API responses.** Any future API caching
(Phase 2) must be an explicit per-endpoint allowlist with a hard cache-purge on logout, and needs
a written decision from Ralph, not a default.

### 6.3 Offline UX — a small, high-value win available today

Independent of everything above: `/reconnecting` currently tells every disconnected user *"The
PPDO Portal server may be waking up after a period of inactivity."* When the user's own phone has
no signal, that message blames the wrong party. A `navigator.onLine` check there — "You appear to
be offline. The portal will reconnect automatically." — is a few lines, needs no service worker,
and is worth doing whether or not the PWA work proceeds.

---

## 7. Azure Static Web Apps configuration

Three changes to `frontend/staticwebapp.config.json`. None is difficult; all three are easy to
miss and each produces a confusing failure.

1. **MIME type for `.webmanifest`.** SWA has no built-in mapping for the extension, so it may be
   served as `application/octet-stream`. Add:
   ```jsonc
   "mimeTypes": { ".webmanifest": "application/manifest+json" }
   ```
2. **Cache header for `/sw.js`.** A service worker cached by the CDN can keep users on an old
   worker after a deploy. Add a route rule setting `Cache-Control: no-cache` on `/sw.js` — the
   browser revalidates on every check, which is the standard recommendation for SW scripts.
3. **Navigation fallback.** The current exclude list is
   `["/api/*", "/_next/*", "/favicon.ico", "/*.{png,jpg,svg,ico,css,js}"]`. Existing files are
   still served correctly (the fallback only fires for paths that don't resolve), so this is
   hygiene rather than a live bug — but `webmanifest` and `webp` should be added so a missing
   asset 404s honestly instead of silently returning `index.html`.

**Scope note:** the SW file must sit at `frontend/public/sw.js` so it is served from `/sw.js` and
takes the root scope `/`. A service worker can only control paths at or below its own URL.

---

## 8. Proposed phasing

| Phase | Scope | Depends on | Size |
|---|---|---|---|
| **1 — Installable + fast** | Manifest, icon set, `apple-touch-icon`, theme colour, static-asset SW, offline fallback page, update toast, SWA config, `APP_VERSION` consolidation | **§4.1 icon asset** | 1 ticket |
| **1.5 — Offline honesty** | `navigator.onLine` on `/reconnecting`; correct wording for a genuinely offline user | none — can ship independently | trivial |
| **2 — Offline read** | Per-endpoint API cache allowlist, staleness banner, cache purge on logout, session-render decision | §5.2 + §6.2 answered by Ralph | 2–3 tickets |
| **3 — Push + background sync** | VAPID keys, subscription table + endpoints, Web Push send path, offline write queue with server-side idempotency keys | Phase 2; iOS 16.4+; backend milestone | own milestone |
| **3-AIP — offline AIP authoring** | The v1.8.0 driver: author AIP work locally, upload later. Jumps straight to offline writes. | §5 auth wall + the AIP rework — see **§11** | own milestone |

**Recommendation: do 1.5 now, and 1 as soon as there's a real logo.** Treat Phase 2 as a separate
decision on its merits — it is where the security tradeoff actually lives, and it should not ride
in on Phase 1's coattails.

Phase 1 delivers, concretely: a home-screen icon, a chrome-less standalone window, repeat loads
served from disk instead of Azure, and a truthful offline screen. It delivers **no** offline data
access. That distinction should be stated plainly to stakeholders before anyone demos it, or the
first question after install will be "why doesn't it work offline?"

---

## 9. Open questions — these block ticket-writing

1. **Logo.** Is the official PPDO logo available yet, at ≥512×512? If not: ship Phase 1 with the
   provincial seal as the app icon, or hold Phase 1 entirely? *(This is the Phase 1 blocker.)*
2. **`start_url`.** PPDO users land on `/dashboard`, non-PPDO office users on `/budget-planning`
   (`(portal)/layout.tsx:208-217`), and an unauthenticated launch of either bounces to `/login`.
   Manifest `start_url` is a single fixed value. `/login` is the honest choice (it already runs the
   health check that warms the Functions app) but adds a hop for logged-in users. Preference?
3. **Who is this for?** Office desktops, field staff phones, or municipal LGU users? "Fast repeat
   loads on an office PC" and "usable on a phone with no signal in Sablayan" are different
   features with different costs. The answer decides whether Phase 2 is ever worth its risk.
4. **Offline data — acceptable at all?** Is caching PPDO budget/inventory data unencrypted on a
   possibly shared device acceptable? A "no" is a perfectly good answer and permanently caps scope
   at Phase 1, which simplifies everything downstream.
5. **iOS.** Are there iPhone users to support? If yes, §5.3 needs verifying on a real device
   *before* any PWA work — if third-party-cookie blocking already breaks silent refresh there, the
   custom domain is the actual first ticket, not the manifest.
6. **Push — is there demand?** Which events would justify it (PR approved, low stock, WFP deadline)?
   Phase 3 is a backend milestone; it shouldn't be started speculatively.

---

## 10. Verification log

What was actually run for this study, on this branch, 2026-08-14:

| Check | Method | Result |
|---|---|---|
| Static export builds clean | `npm ci && npm run build` | ✅ 44 routes, all `○ (Static)` / `● (SSG)` |
| Build output size | `du` / `find` over `out/` | 4.3 MB; see §3.1 |
| `manifest.ts` works under `output: "export"` | Created `src/app/manifest.ts`, rebuilt, inspected `out/` | ✅ `/manifest.webmanifest` emitted (264 B, valid JSON); `<link rel="manifest">` auto-injected into `out/dashboard.html`. **File deleted afterwards — no code change remains.** |
| Metadata-route precedent | `ls out/robots.txt out/sitemap.xml` | ✅ both emitted, confirming the pattern predates this study |
| Icon dimensions | `sharp(...).metadata()` on all three PNGs | 256×256 / 270×270 / 389×362 — none ≥512 |
| Cookie attributes | `grep SameSite backend/` | `SameSite=None; Secure; HttpOnly` — third-party by construction |
| Responsive coverage | `grep -rl "sm:\|md:\|lg:" src/app/(portal)` | 30 files (was 14 at the 2026-07-29 audit) |
| Client storage usage | `grep -rn "localStorage\|sessionStorage"` | 37 references; WFP drafts + LDIP import preview |

`git status` is clean apart from this document.

---

---

## 11. Addendum — v1.8.0: offline AIP creation

> Added 2026-08-14 after Ralph described the actual v1.8.0 driver: *"users download the web app and
> do AIP creation work locally without sending to server, then later they upload their work."*
>
> **AIP creation is being reworked in v1.8.0; details to follow.** So this section deliberately
> stops at *constraints and shapes* — the things that stay true regardless of what the new entry UI
> looks like — rather than designing against a flow that is about to change. Nothing here is a
> ticket yet.

### 11.1 What this changes about the study above

It moves the goalposts from **Phase 1** (install + fast loads) to **Phase 3** (offline writes),
skipping Phase 2 entirely. That is the most demanding version of this feature, and it makes two
things non-negotiable that were previously optional:

1. **§5's auth wall becomes the critical path, not a caveat.** A user cannot "work locally" in an
   app that refuses to render until `POST /auth/refresh` succeeds. This is now the first problem to
   solve, and it is completely independent of the AIP rework — worth starting on regardless of what
   the new AIP UI looks like.
2. **§6.2's "never cache API data" rule needs a scoped exception.** Local AIP work *is*
   provincial budget data sitting on a laptop. That is the security decision from §9 Q4, and this
   requirement answers it with "yes, for AIP drafts" — which means it now needs an explicit,
   written scope (which data, how long, purged when) rather than a blanket refusal.

### 11.2 Findings from the current implementation that should survive the rework

Read as *design constraints for the rework*, not as descriptions of what to keep.

**① The right upload shape already exists — preserve it.**
`AipService.ConfirmImportAsync` (`AipService.cs:263`) accepts a complete hierarchy —
`sectorOffices: Record<string, ParsedAipOfficeResponse[]>`, offices → programs → projects →
activities — containing **no server-assigned IDs at all**, and commits the entire graph in a single
`SaveChangesAsync`. The code comment at `:291` says why it was built that way: per-level saves used
to leave orphan rows when a deep insert failed.

That is exactly the contract an offline client needs: author a whole document locally, POST it once,
all-or-nothing. It also already round-trips through the browser — `aip/new/page.tsx:82` stashes the
preview in `sessionStorage` and `import-preview` posts it back — so the server **already accepts a
client-held, client-editable hierarchy payload**. The offline path is a longer-lived version of a
trip the data already makes.

**② The per-node path is the wrong shape — don't carry it into the rework.**
The manual-entry flow builds the tree through ~20 chained endpoints (`lib/aip.ts`), each depending
on the parent's **server-assigned integer ID**: `addAipProgram(officeId)` → `addAipProject(programId)`
→ `addAipActivity(projectId)`. Offline there is no `officeId` to hang a program off, so a naive
"queue the failed requests and replay them" service worker **cannot work** — it would need
client-generated temporary IDs plus server-side ID remapping on replay, which is materially harder
and easier to get wrong than ①. If the rework keeps a per-node API for online editing, offline work
should still upload via a bulk endpoint.

**③ The hardest problem is merge, not queueing — and it is a policy question.**
`ConfirmImportAsync:279` enforces **one active (Draft or Final) AIP per fiscal year**, provincewide.
An `AipRecord` is a single shared provincial document; each office is an `AipOffice` node *inside*
it. So if five offices each work offline on FY2027 and then upload:

- the first upload creates the record;
- the other four hit `An AIP for FY 2027 already exists with status 'Draft'.`

This is not a bug to route around — it is the data model correctly saying that offline users are
not editing separate documents, they are editing **disjoint subtrees of one shared document**.

The structurally clean answer, and the thing the rework should make possible: **scope both the
offline unit of work and the upload to a single `AipOffice` subtree.** "Upsert my office's programs,
projects and activities into the FY2027 record" is atomic, conflict-free between offices, and is a
near-copy of the machinery `ReplaceImportAsync` (`AipService.cs:318`) already uses to swap one
record's hierarchy. Whether it should also allow *replacing* an office subtree the user has already
uploaded — and what happens if two people worked offline on the same office — is Ralph's call
(§11.4 Q2).

**④ Two guards will reject offline work that was valid when it was written.**

| Guard | Where | Offline consequence |
|---|---|---|
| Mutations require `Status == Draft` | `AddOfficeAsync:425` and every sibling | A record finalized while a user was offline rejects their upload wholesale. Weeks of work, one 400. |
| One active AIP per fiscal year | `ConfirmImportAsync:279` | As ③. |

Neither is wrong; both need a **defined recovery path** rather than an error toast. At minimum the
local draft must survive a rejected upload intact and stay re-uploadable — never "submit, fail,
lose it."

**⑤ No office-ownership enforcement exists today.** `AddOfficeAsync` takes any `officeConfigId`
from any budget-planning user; nothing ties it to the caller's own office. Fine while every edit is
online, immediate, and audited. If uploads start *merging subtrees*, "may this user overwrite this
office's subtree?" becomes a real authorization question that has no answer in the code today.

**⑥ Related gate:** `AipConfirm` requires `CanUploadAip`, while manual entry requires only
`CanAccessBudgetPlanning` (`AipFunctions.cs:15`, `:128`). Non-PPDO office users — the most likely
offline audience — **cannot** call confirm today. An offline upload path for them needs either a new
permission or a separate endpoint; reusing `AipConfirm` as-is would lock out exactly the users the
feature is for.

**⑦ Offline entry needs reference data cached.** Local AIP authoring validates against server-held
lookups the detail page fetches on mount (`detail/page.tsx:1802-1803`): active offices
(`listOffices`) and funding sources (`listFundingSources`), plus sector prefixes (`AipSector.Prefixes`)
and ref-code derivation (`AddOfficeAsync:441` builds `{prefix}-000-1-{OfficeRefCode}` server-side).
These are small, slow-changing, and not sensitive — a good first cache. But note that ref-code
composition currently happens **on the server**; offline authoring either replicates that rule
client-side or defers it to upload.

**⑧ The `.xlsm` upload path cannot go offline.** Parsing is `AipXlsmParser` in Infrastructure
(server-side, ClosedXML). Offline "AIP creation" therefore means the *manual/local authoring* path
only, unless a parser is reimplemented in the browser — which would be a second source of truth for
the file format and is not recommended. Worth confirming this matches what Ralph has in mind: if
the intent is "fill in the Excel file offline and upload the file later", that is a much smaller
feature — the file just sits on disk and gets uploaded when there's signal, needing no PWA at all.

### 11.3 What the shape of a solution looks like

Sequenced by dependency, not priority:

| Step | Work | Notes |
|---|---|---|
| A | **Session-without-network** (§5) | Prerequisite for everything. Also the piece most likely to be wrong on iOS (§5.3) — verify on a device first. |
| B | **Local draft store** — IndexedDB, not `localStorage` | An AIP hierarchy is deep and can be large; `localStorage` is a synchronous 5 MB string store and the wrong tool. The WFP draft pattern (`wfp/page.tsx:819`) is the right *idea* at the wrong scale. |
| C | **Reference-data cache** (⑦) | Small, independent, useful on its own. |
| D | **Bulk scoped upload endpoint** (①+③) | The one piece that must land in the AIP rework itself rather than beside it. |
| E | **Upload UX** — explicit "Upload my work" button, not silent background sync | For a document a user spent days on, silent replay is the wrong model: they need to see what will be sent, what came back, and what to do if it's rejected (④). |
| F | **Idempotency** — client-generated draft ID sent with the upload | A retried upload over a flaky link must not create two records. Nothing in the current AIP write path is idempotent. |

Deliberately **not** recommended: Workbox Background Sync replaying queued POSTs. It is the wrong
primitive here — see ② (chained server IDs) and E (silent replay of a multi-day document).

### 11.4 Open questions — these block scoping, and most are for the rework

1. **What is the offline unit of work?** One office's subtree, or a whole AIP record? This decides
   whether ③ is a merge problem or a simple create.
2. **Two users, one office, both offline.** Last-write-wins, reject-second, or merge? Cheapest
   honest answer is a lock or a warning; the rework should at least not make this *impossible*.
3. **How long does offline work live?** Hours (a site visit) or weeks (a budget season)? Weeks means
   ④'s recovery path is a certainty, and iOS storage-eviction behaviour needs verifying.
4. **Is provincial budget data on a personal/shared laptop acceptable?** (§9 Q4, now unavoidable.)
   If the answer is "only on office-issued devices", say so explicitly — it changes the risk
   calculus, not the code.
5. **Does "AIP creation work" mean authoring in the portal UI, or filling in the Excel file?** (⑧.)
   If it's the file, this may not need a PWA at all — worth settling before anything is built.
6. **Who is the offline user?** Non-PPDO office users are the likely answer, which immediately
   raises ⑥'s permission gap.

### 11.5 Honest assessment

Offline AIP creation is **feasible**, and the backend's existing bulk-commit contract (①) means it
is better-positioned than most apps attempting this. It is not, however, a PWA feature with an AIP
component — it is an **AIP feature with a PWA component**, and the PWA part (manifest, service
worker, install) is the small half. The hard half is session-without-network, a merge policy, and a
rejection-recovery path.

Because AIP creation is being reworked anyway, the timing is good: ①, ③ and ⑥ are cheap to design
in now and expensive to retrofit. **The single most useful thing to carry into the rework is that
the offline unit of work and the upload endpoint should be the same scoped, ID-free, atomic
document** — everything else follows from that.

---

*Study only — no implementation. Phase 1 is blocked on §9 Q1; §6.3 can ship independently today.*
*§11 (offline AIP) is provisional pending the v1.8.0 AIP rework details.*
