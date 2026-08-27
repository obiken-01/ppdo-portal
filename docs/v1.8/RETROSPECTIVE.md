# PPDO Portal — Project Retrospective (Whole Project to Date)

**Period covered:** 2026-05-26 (initial commit) → 2026-08-20 (v1.7.4 merged to `main`)
**Elapsed:** 86 days
**Prepared:** 2026-08-27, ahead of v1.8.0 (AIP redesign) execution
**Scope:** Everything from project inception through the v1.7.x release train. Written at a release
boundary so its findings can change how the next one is run.

> **Method.** Every claim below is derived from the repository itself — commit history, branch
> and release topology, file sizes, CI/deploy configuration, and grep-based convention audits.
> Where a number appears, the command that produced it is reproducible against this repo.
> No claim here rests on recollection alone.

> **Scope note.** This document covers **shipped history — v0.1 through v1.7.4 — and process.** It
> deliberately contains no v1.8.0 planning detail: that lives in `docs/v1.8/Phase_Plan.md` on the
> release branch, and nothing here should require a v1.8.0 change to act on. Findings are stated so
> they apply to *the next large release*, whichever it is.

> **Revised 2026-08-27, same day.** The first draft audited `main` only and got §3.7 wrong as a
> result; it is corrected in place, with action item 9 narrowed. Corrections are marked rather than
> silently edited — a retrospective that quietly rewrites itself is worth less than one that shows
> where it was wrong.

---

## 1. The project at a glance

| Metric | Value |
|---|---|
| Total commits | 805 (556 non-merge, 249 merges) |
| Merged pull requests | 236 |
| Distinct releases merged to `main` | 19 (v1.1.0 → v1.7.4), plus v1.0 / v1.0.1 |
| Average release interval | ~4.5 days |
| Contributors | 1 (`obiken01`) |
| EF Core migrations | 37 |
| Backend source | ~35,700 lines (Domain 4.0k / Infrastructure 8.4k / Application 17.1k / Functions 6.3k) |
| Backend tests | 24,053 lines — 1,061 test methods across 40 test classes |
| Frontend source | 36,138 lines across 110 files |
| Standards docs | 11 in `docs/` + per-version subdirectories |
| Reverts | 3 |

**Commit composition (556 non-merge commits):**

| Type | Count | Share |
|---|---|---|
| `fix` | 198 | 35.6% |
| `feat` | 176 | 31.7% |
| `docs` | 76 | 13.7% |
| `chore` | 40 | 7.2% |
| `perf` | 26 | 4.7% |
| `style` | 14 | 2.5% |
| `refactor` | 10 | 1.8% |
| `ci` / `test` / other | 16 | 2.9% |

**Delivery arc by month:** May 34 → June 342 → July 321 → August 108. June and July carried the
bulk of the build (v1.1 through v1.6); August was consolidation, patching, and v1.8.0 planning.

---

## 2. What went well

### 2.1 Test discipline was real, not aspirational

`CLAUDE.md` mandates 80% Application-layer and 90% Domain coverage. That mandate held:

- **1,061 test methods** across 40 service test classes
- **24,053 lines of test code against 17,062 lines of Application code — a 1.41:1 ratio**
- Every substantial service has a matching `*ServiceTests.cs`: `AipServiceTests`,
  `WfpCeilingServiceTests`, `AllocationServiceTests`, `PermissionServiceTests`,
  `PurchaseRequestServiceTests`, `StockBalanceServiceTests`, and 34 more
- Parsers and Excel builders — the highest-risk, most fiddly code — are covered too
  (`AipXlsmParserTests`, `LdipXlsmParserTests`, `WfpReportExcelServiceTests`,
  `PpmpReportExcelServiceTests`)

This is unusual for a solo project under delivery pressure, and it is the single strongest asset
the codebase has. **An ambitious change is only safe because this net exists.**

### 2.2 CI gates every PR on both stacks

`.github/workflows/ci.yml` runs on *every* pull request with no branch filter — deliberately, so
it covers `main`, `release/**`, and any future integration branch. Backend restores, builds in
Release, and runs the full test suite; frontend runs `npm ci`, a production build, and lint. A
red test blocks the merge. For a single-developer project with no second reviewer, **CI is the
only real gate — and it was set up correctly and early.**

### 2.3 Written conventions were actually obeyed

The `What NOT to Do` list in `CLAUDE.md` is not decoration. Auditing the entire codebase against it:

| Rule | Violations found |
|---|---|
| No `any` type in TypeScript | **0** |
| No `text-slate-700` (non-token colour) | **0** |
| No `rounded-lg` / `rounded-xl` in `(portal)` pages | **0** |
| No `Console.WriteLine` for logging | **0** |
| No `DateTime.Now` | **1** (`ExcelService.cs:1361`, a report footer timestamp) |

Five prohibitions, one violation across ~72,000 lines. The convention documents earned their keep.

### 2.4 Incidents were converted into written rules

The project has a consistent reflex: when something broke, the lesson was written down rather than
remembered. `docs/PERFORMANCE_GUIDELINES.md` came out of the v1.1.0 production audit and encodes
specific incidents (the 1.2 MB AIP response, the `Task.WhenAll` shared-`DbContext` 500, the WFP page
firing `/auth/me` four times). `docs/NAMING_CONVENTIONS.md`, `docs/TEST_CONVENTIONS.md`,
`docs/TICKET_PROMPT_STANDARD.md`, `docs/BUG_REPORT_STANDARD.md`, and `docs/GIT_CONVENTIONS.md`
followed the same pattern. **76 `docs` commits — 13.7% of all work — is a deliberate investment,
and it is why §2.3 shows the numbers it does.**

### 2.5 Changes stuck

**3 reverts in 805 commits (0.37%).** Two were UI adjustments, one was a deliberate feature
("Unmark Completed"). Work that landed stayed landed — decisions were sound even when the code
needed follow-up fixes.

### 2.6 Shipping cadence was sustained, not bursty

19 releases in 86 days, averaging one every 4.5 days, sustained across three months without a
stall. The version-string ritual — three separate files (`Sidebar.tsx`, `Footer.tsx`,
`login/page.tsx`) that must be bumped together and had drifted before — **held: all three read
`v1.7.4` today.** A known trap was contained by discipline.

---

## 3. What did not go well

### 3.1 Fixes outnumbered features — defects were found after merge, not before

**198 `fix` commits vs 176 `feat` commits (1.13:1).** Combined with the patch-release pattern,
this is the central finding of this retrospective:

| Release train | Span | Patch releases |
|---|---|---|
| v1.4.0 → v1.4.8 | 2026-07-12 → 07-21 (9 days) | **8** |
| v1.7.0 → v1.7.4 | 2026-08-04 → 08-20 (16 days) | **4** |

v1.4 shipped a patch release roughly every 27 hours for nine consecutive days. Release branches had
to be *reopened after merging* — `release/1.4.1` merged three separate times, `release/1.7.2`
twice, and v1.7.2 needed an entirely re-cut `release/1.7.2B` branch that itself merged twice.

Where the fixes landed:

| Scope | `fix` commits |
|---|---|
| `ui` | 51 |
| `budget-planning` | 42 |
| `inventory` | 21 |
| `auth` | 11 |
| `aip` | 10 |
| `wfp` | 9 |

**Root cause: there is no environment between a developer's machine and production.** `main`
auto-deploys to Azure on push, so *`main` is the staging environment*. Defects that a UAT pass
would have caught for free were instead caught by users, in production, and paid for with a patch
release. RAL-221 (UAT environment) was scoped and then deferred. **The 12 patch releases across
v1.4 and v1.7 are the invoice for that deferral.**

Note also that unit tests, however plentiful, structurally cannot catch this class of defect —
51 `ui` fixes are layout, wiring, and interaction bugs. The test suite is strong at the service
layer and absent at the integration and UI layer.

### 3.2 Frontend page components have outgrown maintainability

| File | Lines |
|---|---|
| `budget-planning/aip/detail/page.tsx` | **2,057** |
| `budget-planning/wfp/entry/page.tsx` | **1,790** |
| `budget-planning/wfp/page.tsx` | **1,506** |
| `inventory/create-pr/page.tsx` | **1,312** |
| `budget-planning/allocation/page.tsx` | **1,209** |
| `budget-planning/ldip/LdipForm.tsx` | 999 |

Six components over 1,000 lines; five over 1,200. This is not a cosmetic concern — **it maps
directly onto the defect distribution in §3.1.** The two scopes with the largest files
(`budget-planning`, `ui`) are the two scopes with the most fix commits (42 and 51). Churn confirms
it: `wfp/entry/page.tsx` changed 27 times, `budget-planning/report/page.tsx` 26, `wfp/page.tsx` 25.

The cause is structural: **features were added to existing pages rather than extracted into
components.** `docs/DESIGN_SYSTEM.md` documents a shared-component inventory, but nothing enforces
extraction, and no file-size ceiling exists in lint.

The risk compounds on the next feature to touch any of these files: work landing inside a
2,000-line component makes it a 2,400-line component, and lifting a 999-line component into a
second caller makes two of them.

### 3.3 `CLAUDE.md` — the file read at the start of every session — went stale

The `Implementation Status` section is stamped **"Updated: 2026-06-05"** and the file footer reads
**"PPDO Portal v1.0.1 — 2026-06-08"**. Production is at **v1.7.4 (2026-08-20)**.

**The canonical onboarding document is six minor versions and eleven weeks behind the code it
describes.** It documents v1.0/v1.1 as the frontier while v1.2 (Employee Profiles), v1.3, v1.4
(WFP), v1.5 (PPMP), v1.6 (AIP editing), and v1.7 (Inventory) have all shipped. It still lists
v1.2 as "📋 Planned". Its production-deployment table also predates the RAL-237 Function App
relocation to Southeast Asia.

The contrast with §2.3 is the lesson: **conventions a tool can check held perfectly; the section a
human had to hand-maintain rotted.** `CLAUDE.md` itself changed 19 times — the file was edited
often; the status section just was not part of the release ritual.

### 3.4 Branch sprawl

**252 remote branches and 201 local branches** for 19 releases. A prior audit established that only
11 carried unmerged commits and introduced an `archive/*` tag scheme — 11 such tags exist. But
**archiving never became routine**, so the tag scheme captured one cleanup pass and then the
sprawl resumed. The practical cost is that `git branch -r` is no longer a usable way to see what
is in flight, and stale local branches invite the base-drift trap (reusing an unpushed branch whose
merge-base is months old).

### 3.5 The deploy pipeline has three gaps

`.github/workflows/deploy.yml` publishes the Functions ZIP and the SWA frontend on push to `main`.
It does **not**:

1. **Run EF migrations.** A release containing a migration requires a manual
   `dotnet ef database update` against Azure SQL. With **37 migrations shipped**, this has been a
   standing manual step — and a standing opportunity for a release to reach production against a
   schema that cannot serve it.
2. **Smoke-test after deploy.** `GET /api/health` exists and returns DB reachability, but nothing
   calls it post-deploy. A broken deploy is discovered by a user.
3. **Provide a rollback path.** Recovery from a bad deploy means a forward fix — which is precisely
   the mechanism that produced the patch-release churn in §3.1.

### 3.6 Infrastructure problems were found late and reactively

Three examples, all discovered by investigation rather than monitoring:

- **Cross-region latency.** Azure Functions ran in **Central US** while Azure SQL ran in
  **Southeast Asia** — every query crossed the Pacific. This survived roughly three months of
  production before a latency investigation found it, and it was a larger performance factor than
  the database tier that had been scrutinised first. Fixed under RAL-237.
- **Azure SQL tier.** The free serverless vCore-second grant was exhausted earlier each month —
  June, then July, then August — from baseline overhead alone, independent of real traffic. The
  move to Basic tier came **after** the third month of overage.
- **Database tier investigated before the real bottleneck.** Telemetry was checked before an S0
  upgrade and showed 0–20% DTU baseline — no capacity problem. The real cost was the region split.

The pattern: **no proactive cost or latency alerting.** Application Insights is provisioned and
collecting, but nothing watches it. Every infrastructure finding to date was triggered by someone
deciding to look.

### 3.7 Planning artifacts live outside version control

`docs/v1.8/` currently holds **eleven `.backup*.xlsx` copies** of a single open-items tracker,
generated across two days (2026-08-25 → 08-26), versioned by filename suffix
(`before-formspec`, `before-g7`, `before-a5b`, `before-bom`, `before-pdc`, …) — and **untracked in
git.** The versioning instinct is right; the mechanism is manual filename copies of a binary file
that git cannot diff, sitting outside version control entirely.

> **Correction (2026-08-27).** This section originally claimed the current version's plan had no
> Markdown representation in the repo. **That was wrong** — it and five sibling planning documents
> are tracked, on the release branch rather than on `main`, which is why an audit run against
> `main` did not find them. The finding that stands is narrower: **only the `.xlsx` tracker is
> versioned by filename copy.** The miss is itself an instance of the drift described in §3.3, and
> is what action item 12 addresses.

### 3.8 Single contributor, no second reviewer

**551 of 556 non-merge commits are from one author.** All 236 PRs were self-merged. This is not a
criticism of the individual — the discipline evident in §2 is genuinely high for solo work — but it
means CI is the *only* independent check. Nothing catches a design decision that is internally
consistent and wrong, and the bus factor is 1 across a codebase now carrying a province's budget
planning workflow.

---

## 4. Cross-cutting root causes

Four patterns explain most of §3:

| # | Pattern | Produces |
|---|---|---|
| **A** | **Verification happens after merge.** No environment sits between local and production; `main` *is* staging. | §3.1 patch churn, §3.5 deploy gaps |
| **B** | **Features are added to pages, not extracted into components.** No size ceiling, no extraction trigger. | §3.2 giant files, and the `ui`/`budget-planning` fix concentration |
| **C** | **Rules a machine checks survive; rules a human maintains decay.** | §2.3 (0 violations) vs §3.3 (stale status), §3.4 (archiving lapsed) |
| **D** | **Infrastructure is invisible until it hurts.** Telemetry is collected but unwatched. | §3.6 region split, tier overage |
| **E** | **The unit of release is a milestone, not a change.** Release branches run for weeks and batch unrelated work together, so the batch's slowest blocker becomes everything's blocker — and independent work inherits blockers it has nothing to do with. | §3.1 — v1.4 batched a whole rework, then paid it back as 8 patches in 9 days; v1.7 as 4 in 16 |

Pattern **C** is the most useful lever, because it is also the explanation for the successes.
**The reliable move on this project has been to convert intent into an automated check.** Every
convention that got one held at zero violations. Every one that did not, drifted. The action items
below are weighted accordingly.

---

## 5. Action items

Ordered by leverage. Items 1–3 are the ones worth landing **before** the next large release starts, not after it ships.

| # | Action | Addresses | Effort |
|---|---|---|---|
| **1** | **Stand up the UAT environment (RAL-221 — un-defer it).** A second SWA + Functions slot against a UAT database, deployed from `release/*` before the merge to `main`. The `noindex` blocker is solved with an `X-Robots-Tag` header in SWA config. | §3.1, §3.5 — the 12-patch-release problem | M |
| **2** | **Add a post-deploy smoke test to `deploy.yml`.** Poll `GET /api/health` until 200 (with a timeout) and fail the workflow on 503. One step, catches a dead deploy before a user does. | §3.5.2 | S |
| **3** | **Gate migrations in CI, and flag the manual step in the deploy job.** Fail the build if a migration appears in the diff without a corresponding checklist acknowledgment; echo an explicit "MIGRATION REQUIRED — run `dotnet ef database update`" banner in the deploy log. Automating the run against Azure SQL is the better end state, but the banner is today's work. | §3.5.1 | S |
| **4** | **Set a frontend file-size ceiling in lint.** Warn at 600 lines, error at 900, with existing offenders grandfathered via an explicit allowlist that may only shrink. This converts a good intention into a machine-checked rule — the move that has worked on this project. | §3.2, Pattern B | S |
| **5** | **When a release is going to rewrite one of the §3.2 files, extract it *first*, as part of that work — not as cleanup after.** A redesign landing inside a 2,000-line component produces a 2,400-line component. The same applies to reusing one: lifting an existing large component into a second caller is the moment to extract it, or there are now two of them drifting apart. | §3.2 | M |
| **6** | **Make the `CLAUDE.md` Implementation Status update part of the release checklist.** Every `release/*` branch's first commit already bumps `APP_VERSION` in three files — add the status section and the footer date stamp to that same commit. Then bring the section current to v1.7.4 now. | §3.3, Pattern C | S |
| **7** | **Add cost and latency alerts in Azure.** A budget alert on the resource group at a monthly threshold, and an Application Insights alert on p95 request duration. Both are configuration, not code. | §3.6, Pattern D | S |
| **8** | **Make branch archiving part of the release ritual.** After each release merge, tag and delete merged branches. A scheduled workflow that archives branches merged more than 30 days ago would remove the discipline requirement entirely — again, Pattern C. | §3.4 | S |
| **9** | ~~Move the version plan into a tracked Markdown file~~ — **superseded:** it already is tracked. What remains is narrower: add `docs/**/*.backup*.xlsx` to `.gitignore` and stop versioning the tracker by filename copy. | §3.7 | XS |
| **10** | **Add a thin integration-test layer for the highest-churn pages.** Playwright against the top five components from §3.2 — load, submit, verify — would cover the class of defect that produced 51 `ui` fixes and that unit tests structurally cannot reach. | §3.1, §3.2 | L |
| **11** | **Carve independent fixes out of milestone branches by default.** A security or correctness fix that depends on nothing in the release should ship as a patch off `main`, not wait in the queue. Batch-size discipline is worth more where the cost of waiting is a live defect. | Pattern E | S |
| **12** | **Decide what `main` is for.** A long-running release branch means `main` and "the project" drift apart, and documents describing the project can end up on only one of them. Either merge forward more often, or keep the "what is this project" docs where both branches see them. | §3.3 | M |

---

## 6. Carrying forward — what applies to the next large release

Stated generally on purpose. Planning detail for any specific version belongs in that version's own
docs, not here.

- **The bigger the release, the more of §3.1 you should expect.** v1.4 produced 8 patch releases in
  9 days; v1.7 produced 4 in 16. That is the observed price of a large batch reaching production
  without an intermediate environment — and it scales with batch size, so a release larger than
  either should be planned assuming a patch train, or given the UAT gate in action item 1.
- **If a release rewrites or reuses one of the §3.2 files, extract before building** (action item
  5). Extraction is cheap while you are already working in the file and expensive afterwards.
- **The 1,061-test suite is what makes ambitious changes tractable.** Hold the ratio. Service-layer
  coverage is the reason a clean-break redesign is a reasonable plan rather than a reckless one —
  and it is the one asset here that would take months to rebuild if it were allowed to lapse.
- **Independent fixes should not inherit a milestone's blockers** (action item 11).

---

## 7. Honest summary

**This project has been executed with unusually good engineering discipline for a solo build under
delivery pressure** — the test suite, the CI gate, the convention documents, and a near-zero
violation rate against those conventions are all genuinely strong, and the 0.37% revert rate says
the decisions were sound.

**The weakness is uniformly one of verification timing, not of care.** Defects were caught after
merge rather than before, files were allowed to grow past the point of safe modification, and
infrastructure was examined only when it hurt. None of these stem from carelessness; all of them
stem from the same missing structural piece — a gate between "it works on my machine" and "it is
live for the province."

The project has repeatedly proven it can hold a rule once a machine checks it. **The work now is
to give the remaining intentions the same treatment.**

---

*Retrospective — PPDO Portal — prepared 2026-08-27 at the v1.7.4 → v1.8.0 boundary.*
