---
type: plan
version: v1.9.2
status: current
tickets: [PPDO-252, PPDO-253, PPDO-254, PPDO-255, PPDO-256, PPDO-257, PPDO-258, PPDO-259]
updated: 2026-10-25
status_note: "proposal; starts 2026-10-26"
---

# v1.9.2 — UAT Polish Plan

Up: [v1.9 hub](README.md)

> **Epic: PPDO-252** · Linear milestone **v1.9.2 — UAT Polish** (target 2026-11-13)
>
> Stop adding features for a while and make the version already on UAT (**v1.9.1**) solid: find and
> fix bugs and regressions, make the UI consistent, and confirm performance on UAT. Use the pause
> to lay the **Playwright** foundation so later releases get regression checks for free.

---

## 1. Why now

- **The fixes keep coming after the freeze.** `release/1.9.1` was frozen 2026-10-20 and has since
  taken PPDO-237 through PPDO-245 (session restore, notification audience, report visibility,
  loading guards, toasts). That pattern means the app has not been swept, not that it is done.
- **The retrospective's main finding still holds.** Large releases produce patch trains (v1.4 and
  v1.7). A deliberate polish pass now costs less than v1.10 patches later.
- **No E2E safety net.** vitest covers `lib/` helpers and xUnit covers the backend (1,000+ tests),
  but nothing exercises a page in a browser. Every UI regression so far was caught by a person.
- **v1.10.0 (SAIP) only has specs and its model ticket so far** (PPDO-246). Pausing it now is cheap.

## 2. Settled decisions

| # | Decision | Detail |
|---|---|---|
| D1 | **v1.10.0 is paused** | PPDO-248…251 stay in Backlog. No new `feature/v1.10.0-*` branches until PPDO-252 closes. Before resuming, merge `release/1.9.2` into `release/1.10.0` (the usual `merge/*` branch). |
| D2 | **Fixes ship as v1.9.2** | Cut `release/1.9.2` from `release/1.9.1`. Its first commit bumps `APP_VERSION` in `frontend/src/lib/version.ts` and the CLAUDE.md status. Fix branches: `fix/v1.9.2-ppdo-XXX-…` → `release/1.9.2` → `merge/v1.9.2-…-into-uat` → `uat`. `release/1.9.1` stays frozen, so it can still be promoted on its own date. |
| D3 | **Tester: Ralph, solo, scripted** | Run by persona (§3.1) and by checklist (§3.2). Record results in the tracker (§3.3), not from memory. |
| D4 | **Scope: bugs, UI/UX, performance** | No new features, tables, endpoints or permissions. A finding that needs one gets a spec and goes to v1.10+ (CLAUDE.md "spec before build" rule). |
| D5 | **Severity decides what gets fixed** | Per `docs/BUG_REPORT_STANDARD.md`: 🔴/🟠 fixed in v1.9.2. 🟡 fixed if time allows, otherwise backlog. 🟢 backlog. |
| D6 | **Playwright grows gradually** | D1: foundation and read-only smoke tests. D2: first journeys. After that, **every bug fix a browser can see gets a spec.** No big-bang test-writing sprint. |
| D7 | **UAT data is fabricated** | Same rule as `docs/UAT_ENVIRONMENT_SETUP.md` §1. Screenshots attached to tickets must come from UAT, never production. |

## 3. Workstream A — role × page sweep (PPDO-253, -254, -255)

### 3.1 Personas

Create (or confirm) one UAT account per persona. Write the emails in the tracker; keep passwords
in your password manager, never in the repo.

| Key | Persona | What it is for |
|---|---|---|
| `SA` | SuperAdmin | Sees everything; support access |
| `AD` | Admin (no per-user grants) | Checks that Admin is **not** auto-granted the five per-user authorities (Permission Matrix §1) |
| `RV` | PPDO Staff + reviewer grants | Reviewer edits, comment threads, consolidated review |
| `ST` | PPDO Staff, plain | Default staff experience, division scope |
| `EN` | Guest-office encoder (office user) | Office scope; must not see the Report page or other offices (PPDO-239, -240) |
| `XD` | Staff whose division ≠ the record's | **Scope-leak probe.** Open another division's records by URL |

### 3.2 Per-page checklist

Run on every page, for every persona that can reach it. Pages a persona *cannot* reach get a
single check: open the URL directly and confirm it redirects or shows a forbidden message.

1. **Loads.** No error boundary, no 5xx in DevTools → Network, no red Console errors.
2. **Loading state.** The skeleton matches the loaded layout (no jump). Buttons are disabled until data arrives.
3. **Empty state.** With no records, the page says what to do next.
4. **Error state.** Use DevTools → *Block request URL* on the main GET. The page shows an error, not a blank screen or an endless skeleton.
5. **Happy path.** Create, edit and delete (where the page allows), then reload. The data persisted and the toast appeared.
6. **Validation.** Submit empty, too long and duplicate values. The message appears next to the field.
7. **Scope.** The page only shows rows from the persona's division or office. `XD` and `EN` try another record's ID in the URL.
8. **Read-only.** A persona without write rights sees no edit buttons, rather than buttons that fail.
9. **Double submit.** Double-click the main save → one request (PPDO-244 regression check).
10. **Mobile.** At 390 px: the sidebar is off-canvas, tables scroll in their own container, no horizontal page scroll.
11. **Copy and style.** Terms are consistent, there are no typos, and the page follows `docs/DESIGN_SYSTEM.md` (flat corners, slate tokens, `ConfigPageHeader`).
12. **Exports.** Excel/PDF files open, and their totals match the screen.

### 3.3 Tracker

Copy this table into the ticket (or a sheet) and fill it in as you go: ✅ pass · ticket ID for a finding · `—` not reachable (and the redirect/forbidden check passed).

| Area | Page | SA | AD | RV | ST | EN | XD |
|---|---|---|---|---|---|---|---|
| BP | `budget-planning` (dashboard) | | | | | | |
| BP | `aip` list · `aip/new` · `aip/entry` · `aip/detail` | | | | | | |
| BP | `aip/review` · `aip/review/search` · `aip/consolidated` · `aip/import-preview` | | | | | | |
| BP | `ldip` · `ldip/new` · `ldip/edit` · `ldip/import-preview` | | | | | | |
| BP | `wfp` · `wfp/entry` | | | | | | |
| BP | `proposals` · `proposals/edit` | | | | | | |
| BP | `report` (incl. initial WFP + bulk export) | | | | | | |
| BP | `allocation` · `office-ceilings` | | | | | | |
| INV | `inventory` · `create-pr` · `receive-delivery` · `distribution` | | | | | | |
| INV | `item-ledger` · `items-master` · `pr-register` · `pr-report` · `pr-report/[prNo]` · `stock-balances` | | | | | | |
| CFG | `config` · accounts · api-access · audit-log · cc-typologies · divisions | | | | | | |
| CFG | esre-codes · funding-sources · investment-planning · office-users · offices · price-index · procurement-presets | | | | | | |
| ADM | `admin/users` | | | | | | |
| SHELL | home · dashboard · notifications · profile · account · announcements · resource-links | | | | | | |
| PUBLIC | landing · about · services · contact · login · reconnecting · offline | | | | | | |

Split a row when one page has its own findings. The rows group pages to keep the table readable.

### 3.4 Filing a finding

One ticket per finding, under PPDO-252, milestone v1.9.2, label `Bug` or `Improvement`. Title is
the symptom, not the fix. Body follows `docs/BUG_REPORT_STANDARD.md`: severity, persona, steps,
expected vs actual, and a UAT screenshot. Batch tiny copy and style nits into B1 instead of filing
each one.

## 4. Workstream B — UI/UX consistency (PPDO-256)

B1 collects the UI findings from the sweep and fixes them **by pattern**, so one shared-component
change closes many pages:

- Empty, error, forbidden and validation states (checklist items 3, 4, 6, 8)
- Copy and terminology
- Mobile table overflow
- A grep sweep for leftover drift under `(portal)/`: `rounded-lg|rounded-xl`, `text-slate-700`, hard-coded hex
- **Close the last two open `DESIGN_SYSTEM.md` §7 items.** #8 `lucide-react` and #9 dark mode. Recommendation: remove both, and re-add when there is a real use. Unused scaffolding invites half-use.

## 5. Workstream C — performance (PPDO-257)

The 2026-10-04 audit tickets (PPDO-182…189) have all shipped. C1 is a **re-measure, not a backlog**:

1. Re-run the 2026-10-04 §2 measurement table on UAT, for before/after.
2. Add the v1.9.0 endpoints (notifications, comment threads, initial WFP, bulk export, ceiling check).
3. Do the **throttled pass** the loading-guard audit left out (Fast 4G + 4× CPU) on the ten heavy pages.
4. Run Lighthouse on landing, login and dashboard.
5. Pull the 10 slowest operations by p95 from App Insights (`ppdo-portal-api-uat`), and re-check PPDO-205.

Output: `docs/v1.9/Performance_Audit_2026-11.md`. Fix P1s in v1.9.2. P2/P3 become tickets unless they are S-sized.

## 6. Workstream D — Playwright foundation (PPDO-258, -259)

### 6.1 Shape

```
frontend/
├── playwright.config.ts        baseURL from E2E_BASE_URL (default http://localhost:3000), Chromium only
└── e2e/
    ├── global-setup.ts         logs in once per persona → e2e/.auth/<persona>.json (git-ignored)
    ├── personas.ts             persona keys → env var names (E2E_SA_EMAIL / E2E_SA_PASSWORD …)
    ├── routes.ts               route table: path, expected h1, which personas may reach it
    ├── smoke.spec.ts           persona × route: renders, no 5xx, no console error, h1 visible / forbidden
    └── journeys/               D2: aip-submit-review, wfp-entry, pr-partial-delivery, scope-leak
```

- **Auth.** Log in through the real login form in `globalSetup`, then reuse `storageState`.
  The refresh cookie is httpOnly and scoped to `/api/auth/refresh`, so it is captured with the
  state. The access token is in memory, so each test starts with a silent refresh. That is the
  same path a real reload takes, so the test covers it too.
- **Where it runs.** Locally against `npm run dev` + `func start`, and against UAT through a
  manual `workflow_dispatch` workflow (`e2e-uat.yml`). Credentials are in the GitHub `uat`
  environment secrets. Not on every PR yet: UAT is shared, and the suite needs to prove it is
  stable first.
- **Data.** Smoke tests are read-only. Journey specs (D2) write only to a dedicated **E2E office**
  and use unique `E2E-<timestamp>` names, so they never collide with your manual sweep data.
- **The browser is already there.** Chromium and Playwright come pre-installed in the Claude Code
  cloud sessions, so Claude can run the suite while working a ticket.

### 6.2 Growth rule

After D1 and D2, the suite grows by one rule only: **every bug fixed in v1.9.2+ that a browser can
see gets a spec named after its ticket.** This adds about 5–15 specs per release, all aimed at
proven failure points. Add the rule to `docs/TEST_CONVENTIONS.md` in D2.

### 6.3 Manual-coding candidates

| Item | Candidate? | Why |
|---|---|---|
| `smoke.spec.ts` + `routes.ts` (D1) | ✅ **Good** | Isolated from the app, reversible, fast feedback via `npx playwright test --ui`. Sibling: Playwright docs "Authentication → multiple signed-in roles". Claude sets up the config and auth; Ralph writes the spec |
| Journey specs (D2 items 2–3) | ✅ Good | Once fixtures exist, each spec is self-contained |
| E2E office and user seeding (D2 item 1) | ❌ No | Touches auth and permissions, where a mistake is not caught by the compiler |
| Bug fixes from the sweep | Case by case | Flag each one when the ticket is planned (CLAUDE.md rule) |

## 7. Schedule

Assumes about 6 productive hours a day, solo. Days are working days.

| When | Focus | Tickets |
|---|---|---|
| **Mon 10-26** | Cut `release/1.9.2`; create the persona accounts on UAT; start D1 (config + auth) | PPDO-252, -258 |
| **Tue 10-27 – Wed 10-28** | Sweep Budget Planning (largest surface, newest code); write D1 smoke routes between personas | PPDO-253, -258 |
| **Thu 10-29** | Sweep Inventory + Config/Admin/Shell/Public | PPDO-254, -255 |
| **Fri 10-30** | Triage: give every finding a severity; plan the fix order. C1 measurements | PPDO-257 |
| **Week of 11-02** | Fix 🔴/🟠, then the B1 patterns; P1 perf fixes; D2 journeys | fixes, -256, -259 |
| **Week of 11-09** | Fix 🟡 if time allows; re-sweep the changed pages; deploy to UAT; close | PPDO-252 |

⚠️ **Not part of this plan, but keep it on track:** .NET 9 isolated end of support is
**2026-11-10**. Production depends on the v1.7.5 hotfix (PPDO-234) and its Stack-settings switch
on `ppdo-portal-api-sea`. That deadline falls inside the polish window. Don't let the sweep push it back.

## 8. Exit checklist (closes PPDO-252)

- [ ] Every tracker cell (§3.3) is ✅, `—`, or a ticket ID
- [ ] Zero open 🔴/🟠 findings in milestone v1.9.2
- [ ] Every 🟡 finding is fixed or moved to Backlog with a one-line reason
- [ ] `Performance_Audit_2026-11.md` is committed, and its P1s are fixed or have a written reason
- [ ] `npm run e2e:uat` passes for all six personas on the final UAT deploy
- [ ] At least the three D2 journeys plus the scope-leak spec exist and pass
- [ ] `release/1.9.2` is merged to `uat`, and `dotnet test`, `npm test` and the Playwright smoke run are all green
- [ ] `release/1.9.2` is merged into `release/1.10.0`, so SAIP resumes on the polished base
- [ ] CLAUDE.md Implementation Status is updated (release ritual)

## 9. Risks

| Risk | Mitigation |
|---|---|
| The sweep turns up a large defect (e.g. a scope leak) that needs a redesign | 🔴 security/scope issues are fixed immediately with the smallest safe guard. A redesign gets a spec and goes to v1.10+ |
| Fixes drift into features | D4: no new table, endpoint or permission without a spec. Raise it in the ticket, don't build it |
| Flaky E2E against a shared UAT | Smoke tests are read-only; journeys use the E2E office only; manual trigger until 10 runs in a row pass |
| Solo testing misses what real users would hit | Personas cover roles, not habits. Later, give one PPDO user a guided session on UAT using the same checklist |
| Two release lines diverge (1.9.2 vs 1.10.0) | The 1.10 work is paused (D1), and the exit checklist merges 1.9.2 forward |

## 10. Out of scope

- Promoting v1.9.x to `main` (a release-readiness decision, made separately)
- New features, including anything in the SAIP spec
- End-user guide writing
- Running Playwright on every PR, cross-browser runs, and visual regression
- Decommissioning the old Central US Function App (RAL-237 cleanup)

## Related

- [Loading guard audit](Loading_Guard_Audit.md): its "Not checked" section feeds C1
- [Performance audit 2026-10-04](../v1.8/Performance_Audit_2026-10-04.md): the baseline for C1
- [Permission Matrix](../v1.8/Permission_Matrix.md): source for the personas in §3.1
- [`DESIGN_SYSTEM.md`](../DESIGN_SYSTEM.md) · [`BUG_REPORT_STANDARD.md`](../BUG_REPORT_STANDARD.md) · [`TEST_CONVENTIONS.md`](../TEST_CONVENTIONS.md)
- [v1.8 Retrospective](../v1.8/RETROSPECTIVE.md): the patch-train finding behind §1
