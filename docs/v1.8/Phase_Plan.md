# v1.8.0 — Phase Plan & Work Items

> Drafted 2026-08-20 from `AIP_Requirements_Review.md`, `AIP_Redesign_Notes.md`,
> `Office_User_Path_Findings.md`, `Monday_Questions.md` and `../PWA_Feasibility_Study.md`,
> plus Ralph's additions on 2026-08-20 (configuration work, the user/division/permission change,
> landing-page configuration at office and division level, the three dashboards, user profile).
>
> **This is a plan, not a set of decisions.** Work items are numbered `V18-nn` as placeholders —
> they become RAL numbers when created in Linear. Every item marked 🔴 is blocked by a decision
> listed in §9 and must not be ticketed until that decision lands.

---

## 0. Headline

**Phase 1 is entirely unblocked and is roughly a third of the version.** Identity, configuration,
landing pages and password reset depend on none of the open AIP decisions (§9) — six of which are
still open. Everything from Phase 2 onward waits on at least one of them.

That gives a clean recommendation, expanded in §10: **start Phase 1 now, in parallel with chasing
the decisions**, rather than treating the AIP answers as a gate on all movement.

Second observation, offered before ticketing rather than after: **v1.8.0 as currently scoped is the
largest version in this project's history** — larger than v1.4 (WFP rework) and v1.7 (inventory)
combined. §10 proposes splitting it.

---

## 1. Scope inventory

### 1.1 Already shipped on `release/1.8.0`

| RAL | What | Phase it belongs to |
|---|---|---|
| RAL-228 | `OfficeScope` primitive (`All`/`For`/`Resolve`/`Clamp`/`Permits`) | Phase 0 |
| RAL-229 | Office-dashboard IDOR clamp (+ sibling `budget-planning/activity`) | Phase 0 |
| RAL-230 | PPDO dashboard no longer fetched for office users | Phase 0 |
| RAL-231/232/233 | Price-index payload slimming, count endpoint, server pagination + sort | Phase 0 |
| RAL-234 | PWA Phase 1 — installable shell, offline page | Prereq for Phase 6 |
| RAL-235 | Validators reorganised into per-feature subfolders | Phase 0 |
| RAL-238 | AIP `.xlsm` importer — level from description column, not ref-code segments | Phase 0 |

**All three 🔴 office-isolation leaks are closed.** The remaining isolation gap is AIP itself, which
is deliberately folded into the redesign (Phase 2) rather than retrofitted.

### 1.2 What remains, by theme

| Theme | Phase | Blocked? |
|---|---|---|
| A. Permission model — reviewer, LFC, PBO, rename | 1 | No |
| B. Configuration — new config tables, office/division config, FK cleanup | 1 | No |
| C. Landing page — per user / division / office, three dashboards + profile | 1 | No |
| D. Password reset (Option B) + `MustChangePassword` | 1 | No |
| E. AIP data model — ownership FK, expenditure lines, storage units | 2 | 🔴 A, E, F |
| F. AIP entry flow — programs, ref codes, two-stage entry, ceilings | 3 | 🔴 5, C, D |
| G. Review / consolidation workflow — submit, lock, comments, LFC | 4 | 🔴 10 |
| H. Outputs — official AIP form, project profile, office data files | 5 | 🔴 9 |
| I. Offline entry | 6 | 🔴 7 |
| J. Hardening — DB tier, concurrency, approval snapshot | 7 | No |

---

## 2. Phase map

```
Phase 0  ✅ shipped — office isolation, perf, PWA shell, importer fix
   │
Phase 1  ── Identity, Configuration & Landing ──────────── unblocked, start now
   │        permissions · config pages · landing resolution · password reset
   │
Phase 2  ── AIP Foundation ────────────────────────────── 🔴 A, E, F
   │        ownership FK · expenditure lines · pesos · FY partition
   │
Phase 3  ── AIP Entry ─────────────────────────────────── 🔴 5, C, D   (needs 1 + 2)
   │        programs · ref codes · two-stage entry · ceiling service
   │
Phase 4  ── Review & Consolidation ────────────────────── 🔴 10        (needs 1 + 3)
   │        states · locking · comments · PPDO consolidation · LFC · deadline
   │
Phase 5  ── Outputs ───────────────────────────────────── 🔴 9         (needs 2 + 3)
   │        official AIP form · project profile · canonical dataset · office files
   │
Phase 6  ── Offline Entry ─────────────────────────────── 🔴 7         (needs 3)
   │        IndexedDB · cached reference data · upload · session policy
   │
Phase 7  ── Hardening & Ops ───────────────────────────── unblocked, runs alongside
            DB tier for AIP season · concurrency · approval snapshot
```

Phase 1 has no upstream dependency on any other phase, and Phases 3–6 all depend on its permission
model — which is the strongest argument for doing it first regardless of the split in §10.

---

## 3. Phase 1 — Identity, Configuration & Landing

The phase Ralph asked to have detailed. Nothing here waits on the open AIP answers.

### 3.1 Permission model (theme A)

Today's model: `UserRole` → `Division.Can*` flags → `User.Override*` flags, resolved in
`PermissionService`. Two structural additions are needed, and one of them breaks an assumption that
has held since v1.0.

| # | Work item | Notes | Size |
|---|---|---|---|
| **V18-01** | Rename `CanManageAllocation` → **Manage PPDO Allocation** | Mechanical but wide: `PermissionService`, Functions gates, `MeResponse`, user form, frontend types. Its own commit, no behaviour change | S |
| **V18-02** | New per-user flag **`OverrideCanManagePboCeiling`** | PBO finance officer — may set `BudgetCeiling` for **any** office. Mirrors `CanManageAllocation`'s plumbing exactly; Admin **not** auto-granted | S |
| **V18-03** | New per-user flag **`OverrideCanReviewBudgetPlanning`** (reviewer) | Resolution: SuperAdmin → true, else `Override ?? false`. Written generically for LDIP/WFP reuse, not AIP-only | M |
| **V18-04** | **`DenyReviewerWriteAsync` guard** + apply to every budget-planning write endpoint | ⚠️ The codebase's **first subtractive permission**. Every existing flag only grants; `ConfigHttp.AuthorizeAsync(req, _jwt, CanX, ct)` cannot express "deny if caller has X". Needs its own helper and its own tests | M |
| **V18-05** | New flag **`OverrideCanReviewLfc`** — cross-office reviewer | The first permission that deliberately **bypasses** `OfficeScope` rather than combining with it. Must not be built as "reviewer + all offices" — that would also inherit reviewer's write-denial | M |
| **V18-06** | Permission matrix doc + `PermissionService` test sweep | One table covering role × division flag × override × office/division scope for all 11 flags. The model is now large enough that "read the code" is no longer a reasonable answer | S |
| **V18-07** | Audit-log coverage for permission, role, office and division changes | Confirm every write on `users`/`divisions` lands in the audit log; add what is missing. A precondition for accepting self-service password reset (§3.4) | S |
| **V18-08** | User Management form restructure | The form now carries role, office, division, ~11 permission flags and a landing page. Group into sections (Access · Budget Planning · Inventory · Admin) rather than one flat list | M |

**✅ DECIDED 2026-08-25 (DECISION F) — PPDO becomes a real office link plus a host-office flag.**
Ralph chose the full change over the half-measure, and it lands in **Phase 1 under V18-12**, not
later. Until now PPDO-internal users were identified by `OfficeId == null`, pinned by RAL-228's
tests (null = full access — the *opposite* of `DivisionScope`'s null rule), while the PPDO office
*row* was found by three hardcoded `"PPDO"` string lookups. Two mechanisms for one concept, neither
pointing at the other. From V18-12 there is one: PPDO users carry a real `office_id`, and the office
row carries the flag. **Why now:** there are no production office accounts yet, so the backfill is a
single `UPDATE`; and Phase 2 builds AIP ownership (V18-32) directly on the scope resolver, so
changing after that means re-touching every ownership check. See V18-12 in §3.2 for the scope.

### 3.2 Configuration (theme B)

| # | Work item | Notes | Size |
|---|---|---|---|
| **V18-09** | `climate_change_typologies` config table + page | Replaces the free-text `AipActivity.CcTypologyCode`. Follows the existing config-page pattern (`config/funding-sources`) | M |
| **V18-10** | `esre_codes` config table + page | Replaces the free-text eSRE field (`SS`/`ES`/`ID`/`EN`). Same pattern; can share V18-09's ticket if kept small | S |
| **V18-11** | `ProgramDivision` string keys → real FKs | Keys on `OfficeRefCode` + `ProgramRefCode` **strings** today. Program→division assignment becomes load-bearing for PPDO visibility in Phase 3, so this stops being untidiness and becomes a correctness risk | M |
| **V18-12** | Office config: extend the entity + page — **plus the DECISION F host-office change** | Adds the default landing page (V18-16), and carries DECISION F: the `IsHostOffice` flag, PPDO users gaining a real `office_id`, and the retirement of `OfficeId == null`. Includes the CSV download/upload round-trip that page already uses. **Size raised S → M** — it is now a migration + scope-resolver change, detailed below the table | M |
| **V18-13** | Division config: extend the entity + page | Adds the default landing page (V18-16). Confirm the flag set is still right now that reviewer/LFC/PBO are per-user, not per-division | S |
| **V18-14** | Config dashboard tiles for the new pages | `config/page.tsx` — one tile per new config area, counts served by count endpoints, not full lists (the RAL-232 lesson) | S |
| **V18-15** | Division-as-scope for non-PPDO offices — document and enforce | Divisions are office-scoped, but the AIP requirement says office users are scoped by **office only, division explicitly not a factor**. Write that into the scope resolver and its tests now, before Phase 3 reads it | S |

**V18-12 detail — the DECISION F host-office change.** Recorded here because it is the one item in
§3.2 that is not a config-page CRUD job.

| Step | What | Notes |
|---|---|---|
| a | `offices.is_host_office` **BIT NOT NULL DEFAULT 0**, `Office.IsHostOffice` | snake_case column per `docs/NAMING_CONVENTIONS.md`; exactly one row may be true — enforce with a filtered unique index, not application code |
| b | Backfill migration | `UPDATE users SET office_id = @ppdoId WHERE office_id IS NULL`, then set the flag on the `PPDO` row. Trivial **only because no production office accounts exist yet** — this step gets expensive the moment they do |
| c | `OfficeScope.Resolve` reads the flag, not the null | The single backend chokepoint (RAL-228). Delete the null-means-everything branch and its 15-line warning comment — the inversion it was defending against no longer exists |
| d | `.Include(u => u.Office)` on `UserRepository`'s by-id / by-username paths | Lines 22/35/46 include `Division` but not `Office`. Depth 1, allowed by CLAUDE.md. ⚠️ This is a per-request join on **every authenticated call** — the one genuine ongoing cost of the change |
| e | Direct `OfficeId is null` readers | `LdipFunctions.cs:48`, `LandingPageResolver.cs:72` and `:91`. Small list precisely because `OfficeScope` exists |
| f | Retire the three hardcoded `"PPDO"` lookups | `BudgetPlanningDashboardService` (which throws `"Office 'PPDO' is not seeded"` today), `PurchaseRequestService:67`, and `config.ts:127`'s `PPDO_OFFICE_CODE` → resolve via the flag instead |
| g | Frontend `user?.officeId == null` → an `isHostOffice` boolean off `/auth/me` | `layout.tsx:211`, `budget-planning/page.tsx:531`, `budget-planning/report/page.tsx:458` |
| h | Rewrite the RAL-228 tests that pin the null rule | Including `Resolve_AdminOrAboveWithOfficeIdSet_IsStillScopedToThatOffice` and the asymmetry doc block. Keep the *office-wins-over-role* rule — only the discriminator changes |

**Naming still flippable:** the tracker calls it `IsPpdo`; this plan says **`IsHostOffice`** because
what the flag actually governs is cross-office authority, not which office it happens to be — it
survives the office being renamed or restructured. Cheap to flip while it is still a plan; settle it
before the migration is written.

### 3.3 Landing page (theme C)

Requirement: a settable landing page — Main Dashboard, Inventory Dashboard, Budget Planning
Dashboard, User Profile. Ralph's addition: configurable **per office and per division** too, not
only per user.

**Resolution chain** (recommended): `user preference → division default → office default → role
default → first permitted from an ordered list → /account`. `/account` is the terminal fallback
because it is the one page every authenticated user can always reach — which is exactly why the
existing office-user gate already falls back to it.

| # | Work item | Notes | Size |
|---|---|---|---|
| **V18-16** | Schema + resolver: `LandingPage` on `users`, `divisions`, `offices` | Store a stable enum key (`MainDashboard` / `InventoryDashboard` / `BudgetPlanningDashboard` / `Profile`), never a raw path — paths change, and a stored `/inventory` would silently rot | M |
| **V18-17** | Resolve server-side; expose `landingPath` on `/auth/me` | One authority. If each client resolves it, the PWA, the login page and the layout will drift apart the way `APP_VERSION` did | S |
| **V18-18** | Frontend: single `resolveLandingPath(me)` helper, replacing five hardcoded sites | `login/page.tsx:137`, `(portal)/layout.tsx:215`, `Sidebar.tsx:167` (logo link), `manifest.ts:36` (`start_url`), `/reconnecting`'s `?next=` default — all five verified present on `release/1.8.0` | M |
| **V18-19** | PWA `start_url` → neutral resolver | `start_url` is a **single fixed value for everyone** and cannot vary per user. Point it at `/` or a small `/home` that redirects through the resolver. One line, easy to forget, and it breaks the whole feature for installed users if missed | S |
| **V18-20** | Permission-aware landing selector — User form, Division config, Office config, `/account` | The options list must be filtered by what that user/division/office can actually reach, and validated again on the backend. Saving "Inventory Dashboard" for a user without `CanAccessInventory` produces a redirect loop, not an error | M |
| **V18-21** | ✅ **Decided 2026-08-20 — go with `/account`.** Delete the `(portal)/profile` stub and redirect `/profile` → `/account` | `(portal)/profile/page.tsx` is a **stub** ("coming soon"); `/account` is the real page with Profile and Security tabs. "User Profile" as a landing target resolves to `/account`. Keep the redirect rather than deleting the route outright — anything bookmarked or linked to `/profile` should land somewhere sensible | S |

### 3.4 Password reset — Option B (theme D)

Settled 2026-08-14; ticket-ready per `AIP_Requirements_Review.md` §2.6. Independent of everything
else in the version.

| # | Work item | Notes | Size |
|---|---|---|---|
| **V18-22** | Schema: `RecoveryQuestionKey`, `RecoveryAnswerHash`, `MustChangePassword`, attempt counter | The answer is hashed through the same BCrypt path as a password — it *is* a credential | S |
| **V18-23** | Backend: request-reset + verify-answer endpoints | Random temporary password shown once; identical response for a wrong answer and an unknown username (no enumeration oracle); lock after 5 failures in an hour; audit entry on every reset | M |
| **V18-24** | Frontend: "Forgot password?" flow on the login page | Username + recovery answer → temporary password shown once | M |
| **V18-25** | Forced recovery-answer setup at next login (one pass over ~52 accounts) | Users without an answer fall back to admin one-click reset — needed during rollout, and permanently for anyone who forgets | M |
| **V18-26** | 🔒 Fix the shared default password | `UserService.ResetPasswordAsync` sets every reset account to the same documented default (`TamarawUser2026!`) with nothing forcing a change. Replace with a random password + `MustChangePassword`; update `CLAUDE.md` | S |
| **V18-27** | "Your password was reset on …" notice at next login | The detective control that makes self-service acceptable — a colleague who used someone's answer does not go unnoticed | S |

### 3.5 The three dashboards (theme C, continued)

Ralph: *"so far i think it will be the 3 dashboard, maybe we can also add user profile in there too."*
Read as the landing-target set. Each needs one thing done to it before it can be a landing target.

| # | Work item | Notes | Size |
|---|---|---|---|
| **V18-28** | Main Dashboard as a landing target | Currently unreachable for office users — `(portal)/layout.tsx:208-217` bounces them out of everything outside Budget Planning. Either that gate relaxes, or Main Dashboard is simply not offered to office users (recommended: not offered) | S |
| **V18-29** | Inventory Dashboard as a landing target | Offered only when `CanAccessInventory` resolves true. No page change expected — a filter on the options list plus a guard | S |
| **V18-30** | Budget Planning Dashboard as a landing target — and the office view | Office users currently get the readiness hub only (RAL-230, deliberately — an office dashboard "belongs to the redesign"). This is where that promise comes due: does Phase 1 ship a real office dashboard, or keep the hub? | M |
| **V18-31** | User Profile as a landing target → resolves to `/account` | Depends on V18-21. Always available: `/account` is the one page every authenticated user can reach, which is also why it is the terminal fallback in §3.3's chain | S |

**Phase 1 total: 31 work items**, in four clusters — permissions, configuration, landing, password
reset.

### 3.6 Linear ticket linkage

Created 2026-08-20 under milestone *v1.8.0 — Office Users, AIP Redesign & Reviewer Flow*, all
children of the epic **RAL-241** (*v1.8.0 Phase 1 — Identity, Configuration & Landing*). Ticket
descriptions carry scope, not implementation prompts — those follow `docs/TICKET_PROMPT_STANDARD.md`
and get written when a ticket moves to In Progress.

| Item | Ticket | Item | Ticket | Item | Ticket |
|---|---|---|---|---|---|
| V18-01 | RAL-242 | V18-12 | RAL-258 | V18-23 | RAL-265 |
| V18-02 | RAL-243 | V18-13 | RAL-259 | V18-24 | RAL-269 |
| V18-03 | RAL-244 | V18-14 | RAL-260 | V18-25 | RAL-266 |
| V18-04 | RAL-256 | V18-15 | RAL-250 | V18-26 | RAL-254 |
| V18-05 | RAL-257 | V18-16 | RAL-251 | V18-27 | RAL-267 |
| V18-06 | RAL-245 | V18-17 | RAL-261 | V18-28 | RAL-270 |
| V18-07 | RAL-246 | V18-18 | RAL-263 | V18-29 | RAL-271 |
| V18-08 | RAL-268 | V18-19 | RAL-264 | V18-30 | RAL-255 |
| V18-09 | RAL-247 | V18-20 | RAL-262 | V18-31 | RAL-272 |
| V18-10 | RAL-248 | V18-21 | RAL-252 | | |
| V18-11 | RAL-249 | V18-22 | RAL-253 | | |

All are **Todo** except **RAL-255** (V18-30), left in Backlog because it needs Ralph's call first.
Suggested starting order: **RAL-254** (the live shared-password finding) → **RAL-251** (landing
schema + resolver, which five other tickets sit on) → **RAL-244**/**RAL-256** (reviewer flag and its
guard, the longest pole in the permission cluster).

Phases 2–7 are **not** ticketed — every one of them has at least one open decision in §9.

---

## 4. Phase 2 — AIP Foundation 🔴

Blocked by **DECISION A** (one pot or two) — the last one standing. ~~DECISION F (PPDO office
identity)~~ ✅ answered 2026-08-25 and absorbed into V18-12, so V18-32 now builds on a settled
office identity. ~~DECISION E (storage units)~~ ✅ answered 2026-08-25 — pesos everywhere, migrated
not partitioned (see V18-35). Do not ticket before A lands.

| # | Work item | Notes |
|---|---|---|
| **V18-32** | Ownership FK: `AipOffice` → `offices.id` | Office identity is `RefCode` **string matching** today — there is no column to scope on. This is where ownership is created |
| **V18-33** | `aip_expenditures` child table, mirroring `WfpExpenditure` | `activity_id`, `account_id` + snapshots, `funding_source_id` + snapshot, `ps`, `mooe`, `co`, `total`. Keep the snapshot pattern so historical AIPs survive config edits |
| **V18-34** | `AipActivity.Ps/Mooe/Co/Total` become derived, recomputed sums | Whiteboard W2's "cost auto generated". Keep them stored — every report reads them |
| **V18-35** | ⚠️ Storage units: AIP moves from thousands to pesos — **all fiscal years, migrated** | **✅ DECISION E answered 2026-08-25.** Was the most dangerous change in the version; the answer defuses it rather than managing it. `UPDATE aip_activities SET total = total * 1000` for every year, then **delete all six ×1000 sites** — not make them FY-conditional. Detail below the table |
| **V18-36** | Pin the AIP↔WFP numeric boundary with tests | The one place the two documents meet numerically. An explicit accept/reject test against a known AIP activity total, so the factor can never drift again |
| **V18-37** | FY partition: FY≤2027 keeps the v1.6 **shape**, FY≥2028 the new one | Per `AIP_Redesign_Notes.md` §4a — clean break, no migration. ⚠️ **Shape only — the partition does NOT extend to units** (DECISION E, V18-35): units are migrated across all years, so `total` means pesos on every row regardless of fiscal year. Decide the gate's mechanism: a literal `fiscalYear` check on shared endpoints, or new endpoints beside untouched old ones |
| **V18-38** | Freeze the `.xlsm` upload to historical years | `AipXlsmParser` produces the old shape only. ⚠️ Per §4b, the FY2027 rows in the database were imported by the **pre-RAL-238** parser and are not a faithful copy of the province's file |
| **V18-39** | AIP scope resolver: `OfficeScope` × `DivisionScope` | The genuinely two-axis model — division participates **only** when the caller is PPDO. Neither WFP (always both) nor LDIP (office only) does this |
| **V18-40** | New AIP record shape — office-owned, LDIP-like | Includes the open question of one record per PPDO division vs one record with division-tagged rows |


**V18-35 detail — DECISION E, answered 2026-08-25 (units ≠ shape).**

§4a's clean break was decided for the record *shape*, and the tracker (D2) correctly caught that it
was never stated for **units**. It is now: **shape partitions, units do not.** A multi-office record
with no ownership FK genuinely cannot be retrofitted — that is structural. Units are not structural:
`total = total * 1000` restructures nothing, is verifiable (sum before × 1000 = sum after) and is
reversible. RAL-108 synthetic leaves, RAL-180 carry-forward and RAL-181 seeding are all indifferent
to magnitude.

**Why migrate rather than partition.** Under a partition, `AipActivity.Total` stops being readable
without knowing the fiscal year — permanently, not for a migration window — and the six conversion
sites become six FY-conditional branches that must stay correct forever. Getting one wrong in the
*permissive* direction is silent: the ceiling simply never trips again, and the first symptom is a
WFP over its AIP activity, found by someone adding up a printed report by hand. Migrating deletes
the failure mode instead of managing it.

**All six ×1000 sites** — ⚠️ `WfpCeilingService`'s class doc claims the conversion happens *only*
there. That is true of backend services and **false of the codebase**; the frontend triple is
currently undocumented and is exactly what a partition would leave behind:

| Layer | Site |
|---|---|
| Backend | `WfpCeilingService.cs:60` (status), `:106` (save validation), `:220` (finalize) |
| Frontend | `budget-planning/wfp/page.tsx:274`, `:377`, `:1409` |

Display in thousands stays available — it becomes formatting at the report edge (e.g. the AIP detail
page's `Amount (in ₱000)` headers, `aip/detail/page.tsx:2015`), which is where a presentation
concern belongs, rather than a storage invariant every reader must remember.

**LDIP is deliberately NOT moving — and that is not an inconsistency.** The rule being applied is
not *one unit everywhere*; it is **units may differ, but only where the value never crosses a
boundary, and every boundary is named**. AIP had to move because AIP↔WFP is a live numeric boundary
crossed six times. LDIP has **zero**: `SeedProgramsFromLdipAsync` copies `RefCode`/`Name`/
`FunctionBand` and explicitly no amounts (`AipService.cs:785` — an LDIP budget is a multi-year
total, not a valid FY figure), and both dashboard summaries carry counts and statuses only, no money
(`PlanningDashboardDtos.cs:50-63`). LDIP amounts are entered, stored and displayed in ₱000
throughout — the province's own LDIP form is denominated that way. Moving it would relocate a
conversion to multiply-on-save/divide-on-display across ~15 form sites plus `LdipXlsmParser`, for
zero correctness gain.

⚠️ **The invariant that makes that safe is unwritten, so write it:** *`LdipProgram.Budget` is
stored in thousands and must never be compared to, or copied into, a peso amount. If a future
feature needs that, convert at the call site and name the boundary.* DECISION 5 (offices adding
programs outside the LDIP) and V18-40's LDIP-like AIP are the two things that could create that
seam for the first time — this note is what should stop it happening silently.

## 5. Phase 3 — AIP Entry 🔴

Blocked by #5 (programs outside the LDIP), C (block or warn) and D (allocations vs ceiling).

| # | Work item |
|---|---|
| **V18-41** | Programs sourced from a valid LDIP (reuses `seedAipProgramsFromLdip`, RAL-181) 🔴 #5 |
| **V18-42** | Two-stage entry UI — create Project and Activity first, then enter expenditures against them |
| **V18-43** | Multi-fund toggle, default single (whiteboard W8); one fund source per line (decision 4, settled) |
| **V18-44** | Server-side, concurrency-safe ref-code generation — scoped per office/program, computed in SQL. ⚠️ Do not repeat `GeneratePRNoAsync`'s full-table-scan-per-create bug; and offline clients **cannot** mint ref codes safely (a hard constraint on Phase 6) |
| **V18-45** | AIP draw-down ledger 🔴 DECISION A — either `AipDivisionAllocationLedger` mirroring the WFP one, or generalise the existing ledger to `(sourceType, sourceId)` |
| **V18-46** | AIP ceiling service — validate on save/submit, upsert the ledger, expose remaining. ⚠️ The check sums **`mooe + co` of General-Fund lines only** — PS is exempt as an expense *class*, not as a fund source |
| **V18-47** | Office-level ceiling checks — non-PPDO offices have ceilings but no divisions |
| **V18-48** | Allocation page: office picker + PBO ceiling management (the endpoints already take `officeId`) |
| **V18-49** | Completeness checklist before submit — ≥1 expenditure per activity, totals > 0, CC/eSRE present, ceiling respected |

## 6. Phase 4 — Review & Consolidation 🔴

Blocked by #10 (reject/return, comment level) and the five §6.4 questions in the findings doc.

| # | Work item |
|---|---|
| **V18-50** | Extend `PlanningStatus` (Draft/Final/Archived today) to the multi-stage flow |
| **V18-51** | Submit gate — the reviewer is the sole submitter; encoders cannot submit |
| **V18-52** | Locking on submit (whiteboard W4) — against the encoder, the reviewer, or both |
| **V18-53** | Review comments (whiteboard W3) — per submission or per node; per-node is far more useful and materially more work |
| **V18-54** | Return / send-back path with a resubmit flow |
| **V18-55** | PPDO internal consolidation (divisions → PPDO reviewer) |
| **V18-56** | LFC review across all offices, and return-to-office |
| **V18-57** | Submission deadline per fiscal year + readiness view (who has submitted, who has not) |
| **V18-58** | In-app notifications — sidebar pending count + review queue page. No email infrastructure exists; push is PWA Phase 3, and the in-app queue is its prerequisite either way |

## 7. Phase 5 — Outputs 🔴

Blocked by #9 (must printed rows add up to the printed total) and the A2 ①–⑤ rounding details.

| # | Work item |
|---|---|
| **V18-59** | Shared rounding/thousands formatter — `formatThousands()` beside `formatMoney()` in `lib/money.ts`, plus a backend twin so the UI and the Excel output cannot disagree. Store exact; round only at the boundary |
| **V18-60** | **Official AIP form export** — the document the province actually submits, and the single largest missing deliverable in the draft. Build programmatically from a style catalogue (the v1.4.4 / v1.5 lesson), against RAL-238's description-column rule |
| **V18-61** | Project Profile output (whiteboard W5) — a separate per-project document |
| **V18-62** | One canonical dataset: one row per expenditure line, then filter it per office. Five bespoke reports from five conversations will drift; one dataset with five column selections will not |
| **V18-63** | PBO / PACCO / PTO / GSO data files. ⚠️ Check `docs/External_AIP_API_Contract.md` first — a live read-only API for GSO may supersede a file export |

## 8. Phases 6–7 — Offline & Hardening

| # | Work item | Phase |
|---|---|---|
| **V18-64** | IndexedDB draft store — not localStorage, which is synchronous, ~5 MB, string-only, and blocks the UI mid-typing | 6 |
| **V18-65** | Cached reference data — accounts, price index, funding sources, offices, divisions, LDIP programs, eSRE + CC codes | 6 |
| **V18-66** | Cached allocations — a ceiling cannot be evaluated offline without the numbers | 6 |
| **V18-67** | Serve validation rules as cached data rather than hand-copying them into TypeScript | 6 |
| **V18-68** | Upload: per-node errors, never lose the local draft, server-assigned ref codes on arrival | 6 |
| **V18-69** | Session persistence policy + "Sign out & clear local work" + auto-wipe after N days | 6 🔴 #7 |
| **V18-70** | Azure SQL tier review for AIP season | 7 |
| **V18-71** | Concurrent-edit guard within an office — soft lock or "changed by someone else" warning | 7 |
| **V18-72** | Approval snapshot — preserve what was approved when a record is returned and edited | 7 |
| **V18-73** | Amendment readiness — don't make LFC approval terminal (RAL-78 already exists) | 7 |

---

## 9. Decisions still blocking work

From `AIP_Requirements_Review.md` §10, with state as of 2026-08-20. **Monday's meeting answers are
not recorded in the repo** — if they were given, this table needs updating before anything below
Phase 1 is ticketed.

| # | Decision | Blocks | State |
|---|---|---|---|
| **A** | One pot or two — do AIP and WFP draw on the same division allocation? | V18-45, V18-46 | 🔴 open |
| **C** | Ceiling: hard block or warning; at save or at submit? | Phase 3, Phase 6 | 🔴 open |
| **D** | Must division allocations fit inside the office ceiling? | V18-47, V18-48 | 🔴 open |
| **E** | Storage units — migrate 2027 to pesos, or partition by FY? | V18-35, V18-37 | ✅ answered 2026-08-25 — **migrate, all years to pesos**; partition is shape-only. LDIP stays in ₱000 (§4) |
| **5** | Can offices add programs outside the LDIP? | V18-41 | 🔴 open |
| **7** | Offline: personal/shared device, or office-issued? | V18-69 | 🔴 open |
| **9** | Must printed rows add up to the printed total? | every report in Phase 5 | 🔴 open |
| **10** | Reviewer: can they reject/return, and comments at what level? | all of Phase 4 | 🔴 open |
| **11** | 2027 AIP + `.xlsm` upload — migrate, keep, or retire? | V18-37, V18-38 | ✅ answered (§4a clean break; §4b no re-import) |
| **F** | Make PPDO an explicit office (host-office flag) instead of `OfficeId == null`? | V18-32 and every scope check | ✅ answered 2026-08-25 — yes, full change; lands in Phase 1 under V18-12 (§3.1) |
| B, 4, 6, 8b, 12 | Ceiling rule · multi-fund granularity · W1 · round-up · password reset | — | ✅ settled |

**A is now the most dangerous open decision**, and the reason is unchanged: it fails **silently** —
double-counting every peso planned once and detailed once, producing no error, only budget numbers
that look plausible and are wrong. ~~E~~ shared that shape (ceiling checks 1000× too permissive) and
was answered on 2026-08-25 by removing the failure mode rather than managing it — see V18-35.

---

## 10. Sequencing recommendation

**Recommendation: split the version.**

- **v1.8.0 = Phase 1, plus Phase 7's cheap items.** Identity, configuration, landing pages, password
  reset, the three dashboards. ~31 work items, zero open decisions, independently shippable, and
  every later phase depends on its permission model. It also closes the live V18-26 security finding.
- **v1.9.0 = Phases 2–5.** The AIP redesign proper, starting once DECISIONS A, E, F, 5 and 10 land.
- **v1.10.0 = Phase 6.** Offline entry, on top of a working online flow. Building offline against an
  entry flow that is still changing shape means building it twice.

Why not one version: v1.8.0 as scoped is ~73 work items on a milestone that would not ship for
months, with six open decisions inside it. Splitting costs nothing structurally — the phases are
already dependency-ordered — and it stops the office-user path (the thing that started this whole
track) from sitting behind the AIP redesign's open questions.

Counter-argument, stated fairly: the milestone is named *"Office Users, AIP Redesign & Reviewer
Flow"*, and office users are not genuinely usable in production until AIP has ownership (Phase 2) —
an office account today still has destructive access to PPDO's AIP via `DELETE /aip/{id}`. So
"ship Phase 1 and create office accounts" is **not** safe on its own. The split is about release
cadence and reviewability, not about unblocking production office accounts, which wait for Phase 2
either way.

---

## 11. Questions for Ralph

1. **Did Monday's meeting produce answers to A, C, D, 5, 9 and 10?** If so they need recording — §9
   is the table to update. With E and F both now green, **DECISION A is all that stands between
   here and ticketing Phase 2**.
2. ~~**DECISION F** (§3.1) — is PPDO becoming a real office row with `IsPpdo`, or does
   `OfficeId == null` stay?~~ ✅ **Answered 2026-08-25: the full change**, folded into V18-12 and
   sized S → M. Remaining sub-question: the flag's name (`IsHostOffice` vs `IsPpdo`) — see §3.2.
3. **V18-30** — does Phase 1 ship a real Budget Planning dashboard for office users, or keep the
   readiness hub until the redesign?
4. ~~**V18-21** — delete the `/profile` stub and redirect to `/account`, or build `/profile` out?~~
   ✅ **Answered 2026-08-20: `/account`.** The stub goes; `/profile` redirects.
5. **§10** — split into v1.8.0 / v1.9.0 / v1.10.0, or keep one milestone?

---

*Plan only — no tickets created, no decisions taken. Companion to `AIP_Requirements_Review.md` (the
technical reasoning), `Monday_Questions.md` (the meeting version) and `AIP_Redesign_Notes.md` (the
record of Ralph's description and the decisions settled so far).*
