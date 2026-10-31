# Browser tests (Playwright)

Up: [UAT Polish Plan §6](../../docs/v1.9/UAT_Polish_Plan.md) · PPDO-258

Chromium only. Read-only specs until the suite has proven it is stable on the shared UAT.

## Run locally

```bash
# Terminal 1 — backend
cd backend/PPDO.Functions && func start
# Terminal 2 — frontend
cd frontend && npm run dev
# Terminal 3
cd frontend && npx playwright install chromium   # first time only
npm run e2e          # or: npm run e2e:ui
```

`public.spec.ts` needs no backend and no accounts. Anything signed-in needs persona credentials.

**Before you push a change and open its PR, run the whole suite locally** (`npm run e2e`) and say in
the PR body that it passed. Write the spec for a browser-visible fix in the same PR. The suite does
not run in GitHub yet (see [Running it in GitHub Actions](#running-it-in-github-actions-not-set-up-yet)),
so this local run is the check (Ralph, 2026-10-31).

**Before a version's PR into `uat` or `main`, run it again with every other test** on the combined
code: backend, frontend unit, lint, build and this suite. Steps:
[GIT_CONVENTIONS.md → Release PRs](../../docs/GIT_CONVENTIONS.md#release-prs-a-version-to-uat-or-production-run-every-test-first).

## Personas and credentials

Six personas (plan §3.1): `SA`, `AD`, `RV`, `ST`, `EN`, `XD`. Each reads two environment variables:

```
E2E_<KEY>_EMAIL      E2E_<KEY>_PASSWORD      e.g. E2E_SA_EMAIL, E2E_SA_PASSWORD
```

The login form's field is labelled *Username* — put what you type there in `*_EMAIL`.
A persona missing either variable is **skipped**, not failed, so start with one or two.
`auth.setup.ts` signs each configured persona in through the real login form and saves
`e2e/.auth/<KEY>.json` (git-ignored). Each configured persona gets a project named `as-<KEY>`;
signed-in specs run once per project. Keep passwords in your password manager, never in the repo.

## Run against UAT

```bash
E2E_BASE_URL=https://<uat-frontend-host> E2E_SA_EMAIL=… E2E_SA_PASSWORD=… npm run e2e:uat
```

### Running it in GitHub Actions (not set up yet)

`.github/workflows/e2e-uat.yml` exists, but it **is not used for now** (Ralph, 2026-10-31): changes are
checked by the local run above. This is what it would take to turn it on.

1. **Put the workflow file on `main`.** It is manual (`workflow_dispatch`), and GitHub shows the
   *Run workflow* button only for a workflow file on the default branch. While it lives only on a
   release branch, the Actions tab doesn't list it and `gh workflow run` returns 404. A PR to `main`
   with just that one file is enough; it changes nothing in the app.
2. **Create the `uat` environment:** Settings → Environments → New environment → `uat`. The repo has no
   environments today. Optionally restrict it to the release branches.
3. **Add the secrets below to that environment** (environment secrets, not repository secrets). Only
   Ralph enters them. A persona without its two secrets is skipped, not failed.
4. **Make sure the accounts are ready on UAT:** each persona and the seed Admin exists, has done its
   first sign-in by hand (new password, recovery question), and the seed Admin holds only the grant
   described under *AIP journeys*.
5. **Run it:** Actions → **E2E (UAT)** → Run workflow. `suite: all` is the normal run; `suite: perf`
   is the PPDO-257 measurement. Keep `bulk_export` off unless one export job on UAT is wanted.

⚠️ **Never run it while a local run against UAT is going, or two at once.** A second sign-in replaces
the first user's session, so both runs fail at random.

Secrets:

| Secret | For |
|---|---|
| `E2E_BASE_URL` | UAT frontend URL |
| `E2E_<KEY>_EMAIL` / `E2E_<KEY>_PASSWORD` | Each persona (`SA`, `AD`, `RV`, `ST`, `EN`, `XD`); missing ones are skipped |
| `E2E_API_URL` | The UAT Function App's `/api` URL (journeys) |
| `E2E_SEED_EMAIL` / `E2E_SEED_PASSWORD` | The seed Admin (journeys): no cross-office review grant, not a persona. Missing → journeys skipped |

The workflow always sets `E2E_AIP_CLEANUP=1`. Traces are off in CI: they keep typed passwords and
request tokens, and the report is uploaded as an artifact of this **public** repo.

## Using a preinstalled browser

If a Chromium is already on the machine and `playwright install` isn't wanted, set
`E2E_CHROMIUM_PATH=/path/to/chromium`. Local only; CI installs its own.

## Signed-in specs: import from `./fixtures`

```ts
import { expect, test } from "./fixtures";
```

A user has **one** live refresh token, and every page load rotates it. With the plain
`@playwright/test` fixtures, the cookie saved by `auth.setup.ts` dies after the first test, and every
later test for that persona lands on `/login?reason=token_superseded`. `fixtures.ts` keeps one browser
context per persona, saves the rotated cookie after each test, and the `as-<KEY>` projects run one
test at a time (`workers: 1`). Different personas still run in parallel.

## Smoke suite

`smoke.spec.ts` opens every route in `routes.ts` as each configured persona: no crash, no 5xx, no
console error, and the access rule holds (`allow` renders, `deny` redirects away). The allow/deny
lines follow the persona **definitions** in the plan (§3.1). An account with extra grants (e.g. a
local Admin who is also a cross-office reviewer) fails the matching `deny` line, which is correct.
A new page gets a row in `routes.ts`.

## Sweep (`sweep.spec.ts`, PPDO-272)

Automates three items of the plan's per-page checklist (§3.2) on every route a persona may open.
Read-only: it only holds back, blocks or reads GETs.

| Check | What it does | Passes when |
|---|---|---|
| Item 4, error state | Aborts the page's main GET (`mainApi` in `routes.ts`), as DevTools "Block request URL" does | Within 15 s the page shows an error or goes to `/reconnecting`. Fails on a blank main area, an endless skeleton, placeholder dashes with no message, or content that reads as real data |
| Item 2, loading state | Holds the main GET back until the checks are done | A skeleton, spinner, "Loading…" line, or placeholder (`—`, `FY …`) shows; and the page's `primary` button, when set, is disabled |
| Item 10, phone width | Opens the page at 390 px | No sideways page scroll, sidebar off-canvas until "Open menu" is pressed, no console error, no "Application error" after a reload |

`mainApi` is the page's own data call, found by watching what the page requests on load (not the
shell's auth and notification calls). A page with no data call on load has none, and its error and
loading checks are skipped. A new page gets `mainApi` (and `primary`, if it has a main button) in `routes.ts`.

**Findings.** A failing page is a bug: file it under PPDO-252 and add it to `KNOWN` in the spec. That
marks the test `test.fail()`, so the suite stays green, and it fails with "expected to fail" once the
page is fixed, which is the cue to delete the entry.

Heuristics to know: "loading" and "error" are recognised by class names (`animate-pulse`, `animate-spin`),
`role="alert"`, and wording such as "Couldn't load…" / "Failed…". A page that shows its problem in
other words will read as a miss; widen `ERROR_TEXT` in the spec rather than weaken the check. The
check cannot tell zeros from real zeros; that stays a manual look.

## AIP journeys (`e2e/aip/`, PPDO-259)

These change data, so they run in their own **E2E office** that the suite builds itself, never in a
real office. They follow PPDO-195's T0–T14 plan.

| File | What it covers |
|---|---|
| `aip.setup.ts` → `seed.ts` | Builds the E2E office through the API: office `E2E` (ref `01-099`), its own LDIP (2 programs), its rows in the open FY2028 AIP, divisions A/B, GF ceiling ₱1.5M, split A ₱900K / B ₱500K, program 1 → A and 2 → B, and four accounts: `e2e.head` (department head), `e2e.encoder.a`, `e2e.encoder.b`, `e2e.reviewer` (PPDO, cross-office reviewer). Find-or-create: a second run changes nothing. |
| `aip-review-journey.spec.ts` | Encoders add activities and submit (₱250,500 counts as ₱251,000) → head sends to PPDO → reviewer comments and sends back → encoder sees the return and the comment (bell included), replies, resubmits → head resends → reviewer sees the reply and accepts. |
| `aip-v19-checks.spec.ts` | Over a division's allocation: warned, not blocked (PPDO-237). Over the office ceiling: encoder may submit, head may not send (PPDO-146). Reviewer edits an amount with a reason (PPDO-214). Office sees "PPDO made N changes" and marks it seen (PPDO-213). |
| `aip-scope.spec.ts` | Office users asking for another office's data get their own office, a refusal or a redirect, never the other office's name or ref code. Encoder A can't write to division B's activity (checked against a control call from encoder B). |

Each spec first **resets** the E2E office (`reset.ts`): it reopens or returns it as the reviewer,
returns submitted divisions as the head, and deletes its projects. So a failed run never blocks the
next one. Comments on deleted rows are kept by design (PPDO-206), so "N unresolved comments" grows from
run to run; the specs never assert those counts.

**Run it**

```bash
# E2E_SEED_*: an Admin with exactly one per-user grant, "Set Office Ceilings (PPDO finance)":
# the seed sets the E2E office's GF ceiling. It must NOT hold the cross-office review grant,
# which makes an account a comment-only reviewer and the seed's writes are refused
# (ReviewerWriteGuard). Do its first sign-in (new password, recovery question) by hand once.
E2E_SEED_EMAIL=<admin username> E2E_SEED_PASSWORD=<password> npx playwright test --project=aip
```

Watch it: add `--headed --workers=1`, or use `npm run e2e:ui` and pick the `aip` project.

**State is kept per environment**, in `e2e/.auth/<api host>/` (git-ignored): the seeded ids
(`aip-world.json`, `inventory-world.json`) and each test account's generated password
(`aip-accounts.json`, `inventory-account.json`). A local run and a UAT run never read each other's
ids; with one shared file, a UAT teardown once read the local office id. If a saved password stops
working, the next run resets that account.

**On UAT:** also set `E2E_BASE_URL` (frontend) and `E2E_API_URL` (the Function App's `/api`). The
seed then creates the E2E office, an LDIP, two divisions and four users **on UAT**. That is the point,
but it is a decision: tell the finance testers that an "E2E Test Office (automated)" will appear on
their review board. Add `E2E_AIP_CLEANUP=1` to take the office back out of the AIP when the run ends;
otherwise it stays in the consolidated AIP (accepted) until the next run resets it.

## Inventory journey (`e2e/inventory/`, PPDO-259)

| File | What it covers |
|---|---|
| `inventory.setup.ts` → `seed.ts` | A PPDO division **"E2E Inventory (test)"** (code `E2EINV`) with inventory access only, a Staff account `e2e.inventory` in it, and the Items Master item `E2E-0001`. Same seed Admin and find-or-create rules as the AIP seed. |
| `inventory-journey.spec.ts` | Staff create a PR for 10 × `E2E-0001` (division filled in and locked; picking the Stock No. fills the row) → status **Open** → receive 4 → **Partially Delivered** → receive 6 → **Fully Delivered**. |

Inventory is division-scoped, so the test PRs and deliveries only ever appear in the E2E division,
never in a real division's lists. Each run adds one PR and two deliveries there; there is no cleanup.

```bash
E2E_SEED_EMAIL=<admin username> E2E_SEED_PASSWORD=<password> npx playwright test --project=inventory
```

Both journey suites at once: `--project=aip --project=inventory` (they use different accounts).

**The seed Admin must not also be a persona.** The setups and each AIP reset log in as
`E2E_SEED_EMAIL` through the API, and a user has one live session, so that persona's browser would
be signed out mid-run. The setups refuse to start when `E2E_SEED_EMAIL` matches a configured
`E2E_<KEY>_EMAIL`. Use a different seed Admin, or run the two halves separately:

```bash
npx playwright test --project=aip --project=inventory     # seed vars set, persona vars unset
npx playwright test --project=public --project="as-*"     # persona vars set
```

## Bad-path specs (PPDO-271)

The happy paths were covered; these cover what a user gets wrong. Each assertion was red-tested.

| File | Project | What it covers |
|---|---|---|
| `public-login.spec.ts` | `public` | A wrong sign-in shows the error and stays on `/login`. Uses a username that does not exist, so no real account collects a failed attempt toward the rate limit. |
| `aip/ppdo-271-aip-line-bad-paths.spec.ts` | `aip` | AIP expenditure line: no account / no amount refused next to the field with nothing sent; a negative amount can't be typed and the API refuses one; double-click Save sends one POST (PPDO-244); a save against a line another session changed gets the conflict panel (PPDO-121). |
| `aip/ppdo-271-funding-sources.spec.ts` | `aip` | Funding Sources (as the E2E department head, own-office view): empty form, duplicate code. A duplicate *name* is accepted today: `test.fail()` with PPDO-282. |
| `inventory/ppdo-271-inventory-bad-paths.spec.ts` | `inventory` | Create PR with no items is refused and nothing is sent; PR report exports an `.xlsx` holding the total on screen. |
| `ppdo-271-report-exports.spec.ts` | `as-*` | Report page exports (initial WFP FY 2028, PPMP FY 2028, WFP FY 2027): `.xlsx` arrives and holds the grand / fund totals shown. Needs a persona that may choose the office (SA, AD); others skip. FY 2027 skips where no office has WFP lines. |

`xlsx.ts` is a ~100-line reader (zip + sheet XML) so the export specs need no new dependency. It answers
"is this a workbook, and does it hold the number the screen shows", not cell-by-cell comparisons.

## Perf (`e2e/perf/`, PPDO-257)

A measuring tool, not a test: it records numbers for §3 and §4 of
[`docs/v1.9/Performance_Audit_2026-11.md`](../../docs/v1.9/Performance_Audit_2026-11.md) and **does not
fail on a slow page**. It is its own Playwright project, `perf`, left out of the config unless asked for,
so `npx playwright test` and `--list` never see it.

**On UAT (the real measurement):** run it from your machine against UAT, since the GitHub workflow
is not used yet:

```bash
E2E_BASE_URL=https://<uat-frontend-host> E2E_API_URL=https://<uat-api-host>/api \n  E2E_SA_EMAIL=<user> E2E_SA_PASSWORD=<password> npm run e2e:perf
```

Add `E2E_PERF_BULK_EXPORT=1` only if you want the bulk WFP export timed: it writes one export job row
on UAT. The tables print in the terminal and are saved to `e2e-results/perf/` (`perf-<time>.md` and
`.json`). Paste the two tables into §3 and §4 of the audit doc. Once the workflow is set up (see
[Running it in GitHub Actions](#running-it-in-github-actions-not-set-up-yet)), the same run is
Actions → **E2E (UAT)** → *Run workflow* → `suite: perf`, with a **perf-results** artifact.

**Against local** (backend and frontend running, persona credentials set):

```bash
E2E_SA_EMAIL=<user> E2E_SA_PASSWORD=<password> npm run e2e:perf
```

Local numbers only prove the script works. The output says so on its first line.

| Part | What it records |
|---|---|
| §3 API | Each endpoint in the two §3 tables, one discarded warm-up then 3 runs, median. Wire bytes (CDP `encodedDataLength`, the DevTools "transferred" figure), decoded bytes, `Content-Encoding`, time. Ids (largest AIP record, an office, a proposal) are looked up from list endpoints at run time. GETs only; the bulk export is off unless `E2E_PERF_BULK_EXPORT=1` |
| §4 pages | The 12 pages of the §4 table, each in a fresh tab at DevTools "Fast 4G" plus 4x CPU, cache off. Time until the route's marker (from `routes.ts`) is visible, time until no API call has run for 0.5 s, layout shift (`layout-shift` observer, "yes" above CLS 0.1), and the enabled buttons in the page body when the title showed while API calls were still running |

Signs in as **SA**; if `E2E_SA_*` is not set it uses **AD** and says so in the output. `E2E_PERF_PAGE_RUNS=3`
repeats each page and takes the median time and the worst CLS. The output goes to `e2e-results/perf/`
(git-ignored) and contains no URLs, ids, usernames or tokens.

What it cannot do: Lighthouse scores (§5) and App Insights (§6) stay manual. The "early-clickable" column
lists *candidates*: a Filter or Export button that works without data is fine, a Submit that works before
its form loaded is not. Read the page to tell them apart. Run nothing else in Playwright at the same time:
the sign-in takes the persona's one live session.

## Rules

- **One spec per fixed bug.** Every bug fixed in v1.9.2+ that a browser can see gets a spec named
  after its ticket (e.g. `ppdo-244-double-submit.spec.ts`). No big-bang test-writing sprint.
- Smoke specs are read-only. Journey specs write only to the dedicated E2E office, using unique
  `E2E-<timestamp>` names.
- Locate by label/role first; add a `data-testid` only when there is no accessible handle.
