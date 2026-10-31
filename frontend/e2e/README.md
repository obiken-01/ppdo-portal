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

## Rules

- **One spec per fixed bug.** Every bug fixed in v1.9.2+ that a browser can see gets a spec named
  after its ticket (e.g. `ppdo-244-double-submit.spec.ts`). No big-bang test-writing sprint.
- Smoke specs are read-only. Journey specs write only to the dedicated E2E office, using unique
  `E2E-<timestamp>` names.
- Locate by label/role first; add a `data-testid` only when there is no accessible handle.
