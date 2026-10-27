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

Or run the **E2E (UAT)** workflow from the Actions tab (manual only; credentials come from the
`uat` environment secrets `E2E_*` and the `E2E_BASE_URL` variable/secret).

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

## Rules

- **One spec per fixed bug.** Every bug fixed in v1.9.2+ that a browser can see gets a spec named
  after its ticket (e.g. `ppdo-244-double-submit.spec.ts`). No big-bang test-writing sprint.
- Smoke specs are read-only. Journey specs write only to the dedicated E2E office, using unique
  `E2E-<timestamp>` names.
- Locate by label/role first; add a `data-testid` only when there is no accessible handle.
