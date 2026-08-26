# v1.8.0 — Phase Plan & Work Items

> ⚠️ **Two of the documents cited below do not exist in this repository and never have:**
> **`AIP_Requirements_Review.md`** and **`Monday_Questions.md`**. They are referred to throughout
> this plan because it was drafted against them, but nobody should go looking — **this file is the
> authoritative source** for everything they covered, and the open questions live in
> `v1.8.0_Open_Items_Tracker.xlsx`. Corrected 2026-08-26, after the references sent someone hunting.
>
> Drafted 2026-08-20 from `AIP_Requirements_Review.md` (missing), `AIP_Redesign_Notes.md`,
> `Office_User_Path_Findings.md`, `Monday_Questions.md` (missing) and `../PWA_Feasibility_Study.md`,
> plus Ralph's additions on 2026-08-20 (configuration work, the user/division/permission change,
> landing-page configuration at office and division level, the three dashboards, user profile).
>
> **This is a plan, not a set of decisions.** Work items are numbered `V18-nn` as placeholders —
> they become RAL numbers when created in Linear. Every item marked 🔴 is blocked by a decision
> listed in §9 and must not be ticketed until that decision lands.

---

## 0. Headline

**Phase 1 is entirely unblocked and is roughly a third of the version.** Identity, configuration,
landing pages and password reset depend on none of the AIP decisions in §9. Phase 1 is well under
way — see §3.6 for what has shipped.

**✅ Update 2026-08-25 — the PPDC meeting answered every decision that blocked Phases 2–5.** A, C,
D, 5, 9 and 10 are all answered, and 7 is soft-answered; §12 records what changed, including two
things the plan did not contain at all: a **+30% uplift on MOOE and CO** (new DECISION G) and
**ceilings on every fund source**.

**✅ Update 2026-08-26 — both of those are now settled, and one of them was withdrawn.** DECISION G
is answered (tracker G1–G6): the uplift is **presentation-only** — the base is stored, and neither
the ceiling check nor the AIP→WFP limit ever sees the uplifted figure (§12.2). The all-fund ceiling
was **reverted the day after it was made**: ceilings are **General Fund only** again, exactly as
settled on 2026-08-14 (§12.3). **No decision blocks any phase.** Read §12 before ticketing anything
below Phase 1.

**v1.8.0 remains the largest version in this project's history** — larger than v1.4 (WFP rework)
and v1.7 (inventory) combined. §10 proposed splitting it; that was decided against on 2026-08-25 —
one milestone, with patch releases as needed.

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
| E. AIP data model — ownership FK, expenditure lines, storage units | 2 | ✅ A, E, F all answered |
| F. AIP entry flow — programs, ref codes, two-stage entry, ceilings | 3 | ✅ unblocked (5, C, D, G answered; H withdrawn) |
| G. Review / consolidation workflow — submit, lock, comments, PPDO reviewer | 4 | ✅ 10 answered (§12.6) |
| H. Outputs — official AIP form, project profile, office data files | 5 | ✅ unblocked (9, G answered) |
| I. Offline entry | 6 | ✅ 7 soft-answered |
| J. Hardening — DB tier, concurrency, approval snapshot | 7 | No |

---

## 2. Phase map

```
Phase 0  ✅ shipped — office isolation, perf, PWA shell, importer fix
   │
Phase 1  ── Identity, Configuration & Landing ──────────── unblocked, start now
   │        permissions · config pages · landing resolution · password reset
   │
Phase 2  ── AIP Foundation ────────────────────────────── ✅ A, E, F answered
   │        ownership FK · expenditure lines · pesos · FY partition
   │
Phase 3  ── AIP Entry ─────────────────────────────────── ✅            (needs 1 + 2)
   │        programs · ref codes · two-stage entry · ceiling service · +30% uplift
   │
Phase 4  ── Review & Consolidation ────────────────────── ✅ 10 answered (needs 1 + 3)
   │        states · locking · comments · PPDO consolidation · history
   │
Phase 5  ── Outputs ───────────────────────────────────── ✅            (needs 2 + 3)
   │        official AIP form · project profile · canonical dataset · office files
   │
Phase 6  ── Offline Entry ─────────────────────────────── ✅ 7 soft-answered (needs 3)
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
| **V18-03** | New per-user flag **`OverrideCanReviewBudgetPlanning`** (reviewer) | Resolution: SuperAdmin → true, else `Override ?? false`. Written generically for LDIP/WFP reuse, not AIP-only. ⚠️ **2026-08-25: exactly one reviewer per office** (tracker B4-c) — the flag alone permits several, so the constraint has to be built deliberately (V18-79) | M |
| **V18-04** | **`DenyReviewerWriteAsync` guard** + apply to every budget-planning write endpoint | ⚠️ The codebase's **first subtractive permission**. Every existing flag only grants; `ConfigHttp.AuthorizeAsync(req, _jwt, CanX, ct)` cannot express "deny if caller has X". Needs its own helper and its own tests. ⚠️ **Re-scoped 2026-08-26 (tracker B11), and this is the ticket's main change:** the denial does not apply to "reviewers" as one group. There are **two reviewer kinds** — the **department-head reviewer may edit values** during review ("to update any minor details they found"), while the **PPDO reviewer may not** ("just comment"). So the guard must key on which reviewer role the caller holds, not on a single reviewer flag. Design it that way before writing the helper — RAL-256 | M |
| **V18-05** | New flag — **cross-office reviewer**. ⚠️ **Renamed 2026-08-25: NOT `OverrideCanReviewLfc`** | **The LFC no longer reviews in the system** (tracker B5/B6): the cross-office reviewers are designated **PPDO users**, so the flag is `OverrideCanReviewAipConsolidated` (name to settle) and its holders are PPDO staff, not an external committee. Still the first permission that deliberately **bypasses** `OfficeScope` rather than combining with it, and still must not be built as "reviewer + all offices" — that would also inherit reviewer's write-denial. See §12.4 | M |
| **V18-06** | Permission matrix doc + `PermissionService` test sweep | One table covering role × division flag × override × office/division scope for all 11 flags. The model is now large enough that "read the code" is no longer a reasonable answer | S |
| **V18-07** | Audit-log coverage for permission, role, office and division changes | Confirm every write on `users`/`divisions` lands in the audit log; add what is missing. A precondition for accepting self-service password reset (§3.4) | S |
| **V18-08** | User Management form restructure | The form now carries role, office, division, ~11 permission flags and a landing page. Group into sections (Access · Budget Planning · Inventory · Admin) rather than one flat list | M |

**✅ DECIDED 2026-08-25 (DECISION F), and IMPLEMENTED — PPDO is a real office link plus a
host-office flag.** Built in commit `f35c47c` (62 files, migration
`20260825025506_AddOfficeIsHostOffice`) on `feature/v1.8.0-ral-252-258-259-landing-cluster`.
⚠️ **Not merged yet — PR #257 is open against `release/1.8.0`.** The steps below record what was
built and why; treat them as done-pending-review, not as released.
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
| **V18-11** | `ProgramDivision` string keys → real FKs | Keys on `OfficeRefCode` + `ProgramRefCode` **strings** today. Program→division assignment becomes load-bearing for PPDO visibility in Phase 3, so this stops being untidiness and becomes a correctness risk. ✅ **Confirmed 2026-08-26 (tracker B12-b): this is now the *sole* carrier of PPDO's division visibility** — the prediction landed. ℹ️ The original reason for string keys also **lapses**: they exist so assignments survive supplemental `.xlsm` re-uploads, and FY2028+ has no upload (§12.6) | M |
| **V18-12** | Office config: extend the entity + page — **plus the DECISION F host-office change** | **✅ Built in `f35c47c` (2026-08-25); PR #257 open, not merged.** `IsHostOffice` flag, PPDO users on a real `office_id`, `OfficeId == null` retired. Landing-page field (V18-16) also wired on the office form. Was sized S → M for the migration + scope-resolver work; detail below the table | M |
| **V18-13** | Division config: extend the entity + page | Adds the default landing page (V18-16). Confirm the flag set is still right now that reviewer/LFC/PBO are per-user, not per-division | S |
| **V18-14** | Config dashboard tiles for the new pages | `config/page.tsx` — one tile per new config area, counts served by count endpoints, not full lists (the RAL-232 lesson) | S |
| **V18-15** | Division-as-scope for non-PPDO offices — document and enforce | Divisions are office-scoped, but the AIP requirement says office users are scoped by **office only, division explicitly not a factor**. Write that into the scope resolver and its tests now, before Phase 3 reads it | S |

**V18-12 detail — the DECISION F host-office change. ✅ All steps built in `f35c47c`** (PR #257,
open). Kept as the record of what changed and why, since the reasoning is not recoverable from the
diff. ⚠️ RAL-258's own step list is the more current one — it was corrected during implementation
(step g turned out to be **~20 frontend sites, not the 3** estimated here, and RAL-271 added a
fourth `LandingPageResolver` null-reader). Read the ticket before relying on this table's detail.

| Step | What | ✅ | Notes |
|---|---|---|---|
| a | `offices.is_host_office` **BIT NOT NULL DEFAULT 0**, `Office.IsHostOffice` | ✅ | snake_case column per `docs/NAMING_CONVENTIONS.md`; exactly one row may be true — enforce with a filtered unique index, not application code |
| b | Backfill migration | ✅ | `UPDATE users SET office_id = @ppdoId WHERE office_id IS NULL`, then set the flag on the `PPDO` row. Trivial **only because no production office accounts exist yet** — this step gets expensive the moment they do |
| c | `OfficeScope.Resolve` reads the flag, not the null | ✅ | The single backend chokepoint (RAL-228). Delete the null-means-everything branch and its 15-line warning comment — the inversion it was defending against no longer exists |
| d | `.Include(u => u.Office)` on `UserRepository`'s by-id / by-username paths | ✅ | Lines 22/35/46 include `Division` but not `Office`. Depth 1, allowed by CLAUDE.md. ⚠️ This is a per-request join on **every authenticated call** — the one genuine ongoing cost of the change |
| e | Direct `OfficeId is null` readers | ✅ | `LdipFunctions.cs:48`, `LandingPageResolver.cs:72` and `:91`. Small list precisely because `OfficeScope` exists |
| f | Retire the three hardcoded `"PPDO"` lookups | ✅ | `BudgetPlanningDashboardService` (which throws `"Office 'PPDO' is not seeded"` today), `PurchaseRequestService:67`, and `config.ts:127`'s `PPDO_OFFICE_CODE` → resolve via the flag instead |
| g | Frontend `user?.officeId == null` → an `isHostOffice` boolean off `/auth/me` | ✅ | `layout.tsx:211`, `budget-planning/page.tsx:531`, `budget-planning/report/page.tsx:458` |
| h | Rewrite the RAL-228 tests that pin the null rule | ✅ | Including `Resolve_AdminOrAboveWithOfficeIdSet_IsStillScopedToThatOffice` and the asymmetry doc block. Keep the *office-wins-over-role* rule — only the discriminator changes |

**Naming — settled as `IsHostOffice`** (the tracker originally proposed `IsPpdo`). The flag governs
cross-office authority rather than which office it happens to be, so it survives the office being
renamed or restructured. Settled by implementation in `f35c47c`; the migration is written, so this
is no longer cheap to change.

⚠️ **Open sub-question raised during implementation, not yet answered — what does a null
`users.office_id` mean now?** It used to mean *PPDO, see everything*. With the flag as the
discriminator, null becomes genuinely "unassigned", and the safe reading flips to *see nothing*.
Either make the column NOT NULL (schema-level certainty; the user form must then assign the host
office explicitly rather than leaving it blank), or keep it nullable and give `OfficeScope` the
`SeeNothing` state its doc comment says it deliberately lacks — that asymmetry with `DivisionScope`
existed *only because* null meant PPDO, so it is no longer justified. Tracked on RAL-258.

**Verified in the shipped code:** a filtered unique index `UX_offices_is_host_office` enforces
one-host-office-only in the database rather than in application code (step a as specified); the
backfill runs as `UPDATE Users SET OfficeId = @hostOfficeId WHERE OfficeId IS NULL`; and
`OfficeScope` now carries a doc line telling callers to use `IsHostOfficeUser` rather than reading
`OfficeId is null` or comparing office codes to `"PPDO"` — all three hardcoded `"PPDO"` lookups are
gone. The remaining `OfficeId is null` matches in `AipService`/`LdipService` are `LdipRecord`/
`AipRecord` ownership, a genuinely different concept (multi-office bulk uploads), and correctly
untouched.

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

Settled 2026-08-14. **The sub-decisions are the table below — there is no separate document.**
(RAL-253/265/266/267 all used to cite `AIP_Requirements_Review.md` §2.6, which does not exist; the
tickets were corrected 2026-08-26.) Independent of everything else in the version.

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
| **V18-30** | Budget Planning Dashboard as a landing target — and the office view | Office users currently get the readiness hub only (RAL-230, deliberately — an office dashboard "belongs to the redesign"). **✅ Answered 2026-08-25 (tracker D6): Phase 1 ships a real office dashboard**, not the hub. RAL-255 moves out of Backlog | M |
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

All are **Todo**. **RAL-255** (V18-30) was in Backlog pending Ralph's call; that call came on
2026-08-25 (a real office dashboard — tracker D6), so it can move to Todo with the rest.
Suggested starting order: **RAL-254** (the live shared-password finding) → **RAL-251** (landing
schema + resolver, which five other tickets sit on) → **RAL-244**/**RAL-256** (reviewer flag and its
guard, the longest pole in the permission cluster).

Phases 2–7 are **not** ticketed — every one of them has at least one open decision in §9.

---

## 4. Phase 2 — AIP Foundation ✅ unblocked

**All three blocking decisions are answered.** ~~DECISION F (PPDO office identity)~~ ✅ 2026-08-25,
absorbed into V18-12, so V18-32 builds on a settled office identity. ~~DECISION E (storage units)~~
✅ 2026-08-25 — pesos everywhere, migrated not partitioned (V18-35). ~~DECISION A (one pot or two)~~
✅ 2026-08-25 — **one pot drawn down in sequence**, with AIP getting its own tables rather than
WFP's generalised (§12.1). **Phase 2 can be ticketed.**

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
| **V18-40** | New AIP record shape — office-owned, LDIP-like | ✅ **Unblocked 2026-08-26 (tracker B12-b).** PPDO gets an **ordinary office record** — no per-division records, no division column on `AipOffice`, and divisions never print. Division of work is carried on the **program**, via the existing `ProgramDivision` map, exactly as WFP does. A sub-unit that *does* print is an `AipOffice` row sharing the office ref code, distinguished by `(Sector, Name)` — already built, and how the province really encodes (§12.6) |


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

⚠️ **Required second half — the one display that really is denominated in thousands.** The six sites
above all render or compare **pesos** already; deleting their `×1000` changes no visible output. The
AIP detail page is the exception and must ship with V18-35:

```tsx
// aip/detail/page.tsx:2015 — header
<TH colSpan={4} align="center">Amount (in ₱000)</TH>
// aip/detail/page.tsx:369 — cell, raw value, no division
<AmtTD value={act.total} />
```

Today `250` under that header reads correctly as ₱250,000. After the migration, untouched, it shows
`250000` under a header still promising ₱000 — **₱250 million, silently 1000× high**. That is the
same failure mode the migration exists to remove, relocated from a ceiling check to a page people
read numbers off. Fix it one of two ways: drop the `(in ₱000)` headers and show full pesos, or
divide by 1000 at render to keep the province's convention. Not optional polish — skipped, the page
lies.

The principle after V18-35: **convert at the edge for presentation, never in storage.** One storage
unit (pesos), and one conversion, at the point of drawing a table the province wants in thousands.

⚠️ **Phase 5's printable AIP form inherits this.** There is no `AipReportExcelService` yet — it is
unbuilt. Build it peso-storage-aware from the start; do not let it acquire a `×1000` by copying
`WfpReportExcelService`'s assumptions.

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

## 5. Phase 3 — AIP Entry ✅ unblocked

~~#5 (programs outside the LDIP)~~ ✅ closed list, LDIP only. ~~C (block or warn)~~ ✅ block at
submit. ~~D (allocations vs ceiling)~~ ✅ allocations may total less than the ceiling. ~~**DECISION
G** — the +30% uplift on MOOE/CO~~ ✅ answered 2026-08-26: **presentation-only**, so V18-46 never
sees it (§12.2). ~~the two ceiling changes in §12.3~~ ↩️ withdrawn 2026-08-26 — **General Fund
only**, as before.

⚠️ **One item was added here by the 2026-08-26 answers, not removed** — tracker W13 puts
expenditure *procurement lines drawn from the Price Index* inside AIP entry, which no work item
below covers. See **V18-80** (§12.7).

| # | Work item |
|---|---|
| **V18-41** | Programs sourced from a valid LDIP (reuses `seedAipProgramsFromLdip`, RAL-181). ✅ **#5 answered 2026-08-25 — the LDIP is a closed list**; an office cannot add a program outside it, so there is no "propose a new program" path and no approval flow for one |
| **V18-42** | Two-stage entry UI — create Project and Activity first, then enter expenditures against them. **2026-08-25: this is the encoder's own tab**, shaped like the WFP entry page: add/update projects and activities, then submit the whole office's work for department review in one action (§12.5). ✅ **2026-08-26 — THREE stages, not two: encoders must also create the SUB-OFFICE GROUP** (§12.6a). It is entered *with* the program, not separately, and **`LdipForm.tsx` already implements the exact interaction** (RAL-61) — lift it rather than redesign it |
| **V18-43** | Multi-fund toggle, default single (whiteboard W8); one fund source per line (decision 4, settled) |
| **V18-44** | Server-side, concurrency-safe ref-code generation — scoped per office/program, computed in SQL. ✅ **The format is now pinned to a primary source (§12.5):** DBM Budget Operations Manual for LGUs, 2023 Ed., Figure 4 + Annexes C/D. Generation reduces to allocating a sibling-unique `seq` per node — segments 1–5 are office identity and are not generated at all. ⚠️ Do not repeat `GeneratePRNoAsync`'s full-table-scan-per-create bug; and offline clients **cannot** mint ref codes safely (a hard constraint on Phase 6) |
| **V18-45** | AIP draw-down ledger. ✅ **DECISION A answered 2026-08-25 — build `AipDivisionAllocationLedger` mirroring the WFP one; do NOT generalise the existing ledger.** Ralph: "create new tables and fields for AIP, and not reuse the fields used by WFP … in the future, WFP itself will be updated". ✅ **The double-count is answered too (2026-08-26):** the AIP row is a **reservation** that the WFP **relieves per activity** as it commits — the two ledgers must net, not add. Allocation consumed = `WFP committed + AIP reserved not yet converted`. ⚠️ Relief per *fund* instead of per *activity* strands reservations whenever the fund mix changes — §12.1 |
| **V18-46** | AIP ceiling service — validate at **submit** (✅ DECISION C), upsert the ledger, expose remaining. The check sums `mooe + co` of the **General Fund only** (↩️ reverted 2026-08-26, §12.3); PS is exempt as an expense *class* on top of that. It compares the **rounded** figures the document prints (tracker A2-4), and the **base** figures — the +30% uplift is **not** part of the comparison (✅ DECISION G, tracker G3). ⚠️ Non-GF funds must be **explicitly excluded** from the check, not simply left without ceiling rows: `GetDivisionAllocationAsync` resolves a missing allocation to `0m`, so a blank row means *zero*, not *unlimited* (§12.3). ✅ **2026-08-26 — this service does not replace the WFP one:** `WfpCeilingService`'s allocation check **stays live** for FY2028+, and a WFP expenditure is bound by **the lesser** of its AIP activity amount and the fund's currently remaining allocation (§12.1) |
| **V18-47** | Office-level ceiling checks — non-PPDO offices have ceilings but no divisions |
| **V18-48** | Allocation page: office picker + PBO ceiling management (the endpoints already take `officeId`). ✅ **A5-b answered 2026-08-26 — a ceiling cut is non-destructive:** encoded work **stands** and fails at V18-49's submit gate. PBO needs no confirmation dialog beyond the ordinary one, and no cascade. ⚠️ **Do not clamp `remaining` at zero.** After a cut, an office's remaining allocation is legitimately **negative**, and that is precisely the state that must block submit — a `Math.Max(0, …)` anywhere in the ledger, the DTO or the UI hides the only signal the office has. Show the negative figure |
| **V18-49** | Completeness checklist before submit — ≥1 expenditure per activity, totals > 0, CC/eSRE present, ceiling respected. ✅ **DECISION C makes this the enforcement point**: over-ceiling entry is allowed while encoding and blocked at submit, so this checklist *is* the ceiling gate, not a courtesy |

## 6. Phase 4 — Review & Consolidation ✅ unblocked

~~#10 (reject/return, comment level)~~ and ~~the five §6.4 questions~~ ✅ all answered 2026-08-25 —
the workflow is written out in §12.6 and the reviewer is now a **PPDO user, not the LFC** (§12.4).
**Phase 4 can be ticketed**, subject to the two shaping questions in §12.8 (comment threading, and
how PPDO's own divisions submit).

| # | Work item |
|---|---|
| **V18-50** | Extend `PlanningStatus` (Draft/Final/Archived today) to the multi-stage flow |
| **V18-51** | ⚠️ **Corrected 2026-08-25 — there are TWO submits, not one.** The **encoder** submits the office's whole work for department review; the **department-head reviewer** is the sole submitter onward to PPDO. The original "encoders cannot submit" holds only for the second hop |
| **V18-52** | Locking on submit. ✅ **Answered 2026-08-25 (tracker B3):** during department-head review the work stays **editable by both encoder and reviewer**; it locks **only when submitted to PPDO**, and unlocks again when a PPDO reviewer returns it |
| **V18-53** | Review comments. ✅ **Narrowed 2026-08-26 (tracker B10), superseding DECISION 10's "both levels": inline comments only** — there is no whole-submission comment. Each carries a **"Mark as resolved" checkbox**, set when the work is returned; on re-submit the app **counts unresolved comments, warns, and lets the user proceed or cancel** — a soft gate, not a hard one. The department head may also edit values directly; the PPDO reviewer may only comment (V18-04). ✅ **Both remaining gaps closed 2026-08-26 (§12.5):** comments anchor to **the row** (GitHub-style gutter control, collapsed by default with a row marker); **only the authoring side resolves** — an office may not resolve a PPDO reviewer's comment, and an encoder may not resolve their department head's, or the soft gate becomes self-marking; **no replies/threading**, resolve is the only action; the unresolved count is **split by the two authoring roles** (department-head reviewer vs PPDO reviewer — the encoder never comments) and rendered as filter buttons *and* in the re-submit warning. Resolved comments are marked, never deleted — they feed V18-77's "Show History" |
| **V18-54** | Return / send-back path with a resubmit flow |
| **V18-55** | PPDO internal consolidation (divisions → PPDO reviewer). ✅ **Answered (tracker B4/B4-a/B4-b):** "consolidated" is the **existing multi-office record, filled in office by office as they submit** — not a newly assembled record; **only reviewers can see it** (PPDO division users cannot), and PPDO reviewers may view it **partially**, before every office has submitted |
| **V18-56** | ⚠️ **Rewritten 2026-08-25 — PPDO review across all offices, not LFC.** Designated PPDO users review the consolidated document, comment, and **send a whole office's work back** for update and re-submission. One office at a time is the confirmed granularity (tracker B5) |
| **V18-57** | ⚠️ **Narrowed 2026-08-25 (tracker B7): no enforced deadline.** The deadline is communicated to offices outside the system, so build **no date gate** — build the **history**: who submitted and when, who returned it, when it was re-submitted, plus the readiness view of who has and has not submitted. ✅ **The readiness view has a shape as of 2026-08-26: an office kanban — see V18-82 (§12.5a).** The history half is V18-77, surfaced via "Show History" on the review page |
| **V18-58** | In-app notifications — sidebar pending count + review queue page. No email infrastructure exists; push is PWA Phase 3, and the in-app queue is its prerequisite either way |

## 7. Phase 5 — Outputs ✅ unblocked

~~#9 and the A2 ①–⑤ rounding details~~ ✅ all answered 2026-08-25: **round each row first, then sum
the rounded rows** — every subtotal, the row Total (sum of the rounded PS/MOOE/CO), the ceiling
comparison and the Excel cells all use the rounded figure; sub-₱1,000 amounts print as `1`; there
are no negative amounts. **Still blocked by DECISION G** — no report can be drawn until it is known
whether the printed MOOE/CO carry the +30% (§12.2).

| # | Work item |
|---|---|
| **V18-59** | Shared rounding/thousands formatter — `formatThousands()` beside `formatMoney()` in `lib/money.ts`, plus a backend twin so the UI and the Excel output cannot disagree. Store exact; round only at the boundary. ✅ **The rule is settled (2026-08-25):** round each value UP to the next thousand, then **sum the rounded values** for every total and subtotal — including the row's own Total, which is the sum of the rounded PS/MOOE/CO, not a separately-rounded grand total. Excel cells hold the **rounded thousands as real numbers**, so re-summing a column in Excel reproduces the printed total. Amounts under ₱1,000 print as `1`; negatives do not occur |
| **V18-60** | **Official AIP form export** — the document the province actually submits, and the single largest missing deliverable in the draft. ✅ **Specified 2026-08-26 in `docs/v1.8/AIP_Form_Spec.md`**, from DBM Annex B plus the province's real FY2027 file: sheet set, preamble, the A–R column map with DBM numbers, the B/C/D/E level rule, row types, what changes for FY2028+, and the FY2027 defects not to reproduce. Build programmatically from a style catalogue (the v1.4.4 / v1.5 lesson), against RAL-238's description-column rule. ⚠️ **One open question blocks it — §6.1 of that spec.** ⚠️ **Stakes raised 2026-08-26 (§12.6):** this is the document **presented to the PDC and then to the Sangguniang Panlalawigan**, under a **June 7** statutory deadline — not an internal report. It is also where the +30% uplift first meets readers who did not encode it, including the deliberate ceiling-exceedance of DECISION G, so the form or its covering note must say so |
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
| **V18-71** | Concurrent-edit guard within an office — soft lock or "changed by someone else" warning. ⚠️ **Promoted 2026-08-25:** tracker D5 confirms **two or more encoders per office**, so this is a correctness requirement, not hardening — today the last save silently wins | 7 |
| **V18-72** | Approval snapshot — preserve what was approved when a record is returned and edited | 7 |
| **V18-73** | Amendment readiness — don't make approval terminal (RAL-78 already exists). ⚠️ **Doubly restated 2026-08-26:** the LFC is out of the system (§12.4), and the terminal authority now appears to be the **Sangguniang Panlalawigan resolution**, not any state this system owns. Supplementals reportedly go to the SP *first* — so this is a workflow question before it is a schema one (§12.6, tracker B14/B15) | 7 |

---

## 9. Decisions still blocking work

Originally from `AIP_Requirements_Review.md` §10 — **that document does not exist; this table is the
record.** **✅ Updated 2026-08-25 from the PPDC meeting** — the answers
themselves live in `v1.8.0_Open_Items_Tracker.xlsx` (columns H–K); §12 records what each one changes
in this plan. Nothing below Phase 1 was ticketed before this table was updated.

| # | Decision | Blocks | State |
|---|---|---|---|
| **A** | One pot or two — do AIP and WFP draw on the same division allocation? | V18-45, V18-46 | ✅ answered 2026-08-25 — **one pot, drawn down in sequence**: the allocation is repurposed to constrain the **AIP**, and WFP is limited by its AIP activity. AIP gets **its own tables**, not WFP's generalised. ✅ The transitional double-count is resolved too (2026-08-26): the WFP allocation check **stays live**, an expenditure is bound by **the lesser** of its AIP activity and the fund's current remaining allocation, and the AIP reservation is **relieved per activity** as the WFP commits — §12.1 |
| **C** | Ceiling: hard block or warning; at save or at submit? | Phase 3, Phase 6 | ✅ answered 2026-08-25 — **block at submit**, not at save ("this should be discussed or adjusted"). V18-49 becomes the gate |
| **D** | Must division allocations fit inside the office ceiling? | V18-47, V18-48 | ✅ answered 2026-08-25 — allocations **may total less** than the ceiling (they may never exceed it, which the code already enforces). ✅ **The second half is answered too (2026-08-26, tracker A5-b):** when PBO cuts a ceiling *after* offices have encoded against it, **the encoded work stands and fails at submit** — nothing is deleted, adjusted or flagged at cut time. Consistent with DECISION C, and it avoids destroying work done in good faith |
| **E** | Storage units — migrate 2027 to pesos, or partition by FY? | V18-35, V18-37 | ✅ answered 2026-08-25 — **migrate, all years to pesos**; partition is shape-only. LDIP stays in ₱000 (§4) |
| **5** | Can offices add programs outside the LDIP? | V18-41 | ✅ answered 2026-08-25 — **no. The LDIP is a closed list.** Also removes the LDIP↔AIP unit seam §4 warned about |
| **7** | Offline: personal/shared device, or office-issued? | V18-69 | ✅ soft-answered 2026-08-25 — **"probably 1 user per machine"**, so a persistent device session is acceptable. Build the "Sign out & clear local work" escape hatch anyway; "probably" is not "guaranteed" |
| **9** | Must printed rows add up to the printed total? | every report in Phase 5 | ✅ answered 2026-08-25 — **yes: round each row up first, then sum the rounded rows.** Applies to the row Total (= rounded PS + rounded MOOE + rounded CO), every subtotal, the ceiling comparison and the Excel cells. The one-directional drift (~₱25k per 50 rows) is accepted |
| **10** | Reviewer: can they reject/return, and comments at what level? | all of Phase 4 | ✅ answered 2026-08-25 — **return: yes**, a PPDO reviewer sends a whole office's work back. **Comments: both levels** — whole submission, or one program / project / activity. The department head may also edit values outright (§12.6) |
| **11** | 2027 AIP + `.xlsm` upload — migrate, keep, or retire? | V18-37, V18-38 | ✅ answered (§4a clean break; §4b no re-import) |
| **F** | Make PPDO an explicit office (host-office flag) instead of `OfficeId == null`? | V18-32 and every scope check | ✅ answered 2026-08-25 — yes, full change; lands in Phase 1 under V18-12 (§3.1) |
| **G** 🆕 | **The +30% uplift on MOOE and CO** — derived or stored, at what rate, and do the ceiling and the WFP limit see the uplifted figure? | V18-46, V18-74, every Phase 5 report | ✅ answered 2026-08-26 (tracker G1–G6) — **the uplift is presentation-only.** The base is stored, the rate is a fixed 30%, and **neither the ceiling check nor the AIP→WFP limit sees the uplifted figure**. FY2028+ only; printed Total = `PS + 1.3 × (MOOE + CO)`. ⚠️ G3 deliberately overrides the A2-4 principle — §12.2 |
| **H** 🆕 | ~~Ceilings apply to every fund source, not General Fund only~~ | V18-46, V18-48, ~~V18-78~~ | ↩️ **withdrawn 2026-08-26 — the 2026-08-25 reversal was itself reversed** (tracker A1-b, A6-4). Ceilings are **General Fund only**, as settled 2026-08-14. **V18-78 is dropped.** The one thing that survives is a code caveat, not a requirement: non-GF funds must be *explicitly excluded* from the check rather than left blank — §12.3 |
| B, 4, 6, 8b, 12 | Ceiling rule · multi-fund granularity · W1 · round-up · password reset | — | ✅ settled |

**Every decision that blocked Phases 2–5 is answered, including the one that replaced them.**
DECISION **G** closed on 2026-08-26 and DECISION **H** was withdrawn the same day. Nothing in this
table is 🔴.

⚠️ **G's answer is the one to read twice, because it accepts the failure mode rather than removing
it.** The ceiling compares the base figure while the document prints the uplifted one, so an office
sitting exactly on a ₱10,000,000 ceiling prints an AIP reading ₱13,000,000 — over the ceiling on
paper, passing in the system, no error anywhere. That is the same shape as A's double-count and E's
1000× permissiveness, and it directly contradicts the principle tracker A2-4 settled ("the system
must never disagree with the paper"). It is nonetheless **the decision** (tracker G3), so it is
recorded here as deliberate: the printed AIP total may legitimately exceed the printed ceiling by up
to 30%, and this **is stated in `AIP_Form_Spec.md` §6.2** — without it someone reads it as a defect and
"fix" it back.

**✅ A's transitional failure mode is closed (2026-08-26).** It had moved into the transition: WFP's
own draw-down against `DivisionAllocation` is still live in code, so an FY2028 division that plans
₱6M in the AIP and details ₱6M in the WFP would consume ₱12M of a ₱10M allocation. The plan's
recommendation was to retire the WFP check; **Ralph rejected that**, because the allocation may
have changed — in amount *or* in fund mix — by the time the WFP is written. The check therefore
**stays**, an expenditure is bound by **the lesser** of its AIP activity and the fund's currently
remaining allocation, and the AIP reservation is **relieved per activity** as the WFP commits
against it, rather than sitting alongside it. §12.1 has the netting rule written out — read it
before ticketing V18-45/46, because relieving per *fund* instead of per *activity* reintroduces the
bug in a quieter form.

---

## 10. Sequencing — ✅ DECIDED 2026-08-25 (tracker D7)

**One milestone. v1.8.0 keeps all seven phases; patch releases (v1.8.1, v1.8.2, …) handle bugs,
additions and moved requirements as they come up.** Ralph's call, overriding this section's original
recommendation to split into v1.8.0 / v1.9.0 / v1.10.0. That recommendation is recorded below only
so nobody re-derives and re-proposes it.

**What this means in practice:**

- All ~73 work items stay under the existing milestone *"Office Users, AIP Redesign & Reviewer
  Flow"* and the RAL-241 epic. No new milestones to create, no tickets to re-parent.
- Phase 1 shipping is a **release** event, not a milestone boundary — the version ships when it
  ships, and anything that has to move afterwards moves as a patch.
- The phases stay dependency-ordered regardless. Phase 2 still cannot be ticketed until DECISION A
  lands; that constraint was never about how the work was versioned.

**Original recommendation, not taken:** split into v1.8.0 (Phase 1 + Phase 7's cheap items, ~31
items, zero open decisions), v1.9.0 (Phases 2–5, the AIP redesign), v1.10.0 (Phase 6, offline). The
argument was that ~73 items with open decisions inside them make for a milestone that does not ship
for months, and that splitting would stop the office-user path sitting behind the redesign's open
questions. Superseded — but if the milestone does start to feel unreviewable in practice, this is
the shape it would split into.

⚠️ **The operational caution survives the decision, and matters more under one milestone.** Office
users are not safely usable in production until AIP has ownership (Phase 2) — an office account
today still has destructive access to PPDO's AIP via `DELETE /aip/{id}` (V18-26 / RAL-254 territory,
see `Office_User_Path_Findings.md` §3.1). **Do not create production office accounts when Phase 1
ships.** Under the rejected split this was implied by the version boundary; with one milestone there
is no boundary to imply it, so it has to be remembered deliberately.

---

## 11. Questions for Ralph

1. ~~**Did Monday's meeting produce answers to A, C, D, 5, 9 and 10?**~~ ✅ **Answered 2026-08-25**
   — all six, plus 7, plus two items the plan did not contain (the +30% uplift and ceilings on every
   fund). Recorded in §9 and §12. **Phase 2 and Phase 4 can be ticketed; Phase 3 and Phase 5 wait on
   DECISION G.** The live open list is now §12.8, not this section.
2. ~~**DECISION F** (§3.1) — is PPDO becoming a real office row with `IsPpdo`, or does
   `OfficeId == null` stay?~~ ✅ **Answered 2026-08-25: the full change**, folded into V18-12 and
   sized S → M. Remaining sub-question: the flag's name (`IsHostOffice` vs `IsPpdo`) — see §3.2.
3. ~~**V18-30** — does Phase 1 ship a real Budget Planning dashboard for office users, or keep the
   readiness hub until the redesign?~~ ✅ **Answered 2026-08-25: yes, a real dashboard.**
   **RAL-255 can leave Backlog** — it was the only Phase 1 ticket held there pending this call.
4. ~~**V18-21** — delete the `/profile` stub and redirect to `/account`, or build `/profile` out?~~
   ✅ **Answered 2026-08-20: `/account`.** The stub goes; `/profile` redirects.
5. ~~**§10** — split into v1.8.0 / v1.9.0 / v1.10.0, or keep one milestone?~~ ✅ **Answered
   2026-08-25: one milestone**, with v1.8.1/v1.8.2 patch releases as needed. §10 rewritten.

**Open after the 2026-08-26 answers** — full text in **§12.8**, which now records what each of the
ten items resolved to. Only three fragments are still genuinely open: the **ref-code segment
layout** (Q4 — the format string is known, the segment *meanings* and reset points are not), the
**PPDO AIP record shape** (Q8 — one record per PPDO division, or one with division-tagged rows), and
**two answers phrased as a bare "yes" to a question that was not yes/no** (tracker G5 and A5-b),
which are read in §12.8 but are not treated as settled. **A1-b closed 2026-08-26** — it was the last
blocker, and its answer overturned this plan's own recommendation (§12.1).

---

## 12. The 2026-08-25 PPDC meeting — answers, reversals and new scope
### (with the 2026-08-26 follow-up answers folded in)

**Sources:** Ralph's relay of the discussion, two photographed pages of meeting notes, and the
answer columns of `v1.8.0_Open_Items_Tracker.xlsx` (filled the same day). The tracker holds the
answers verbatim; this section holds what they *change*. Where the two differ, the tracker is the
record of what was said and this is the reading of it — flag any misreading rather than working
around it.

Headline: **six blocking decisions answered, one previously settled decision reversed, and two
requirements that were not in the plan at all.**

**✅ Follow-up 2026-08-26.** The eighteen rows this meeting opened in the tracker (G1–G6, A1-b,
A5-b, A6-4, B9–B13, W10–W13) have all been answered. Two outcomes dominate: **DECISION G resolved
to a presentation-only uplift** (§12.2), and **the all-fund ceiling of §12.3 was withdrawn** —
ceilings are General Fund only again. Each subsection below carries its own 2026-08-26 note.

### 12.1 DECISION A — one pot, drawn down in sequence

> "By process. The AIP is created first then WFP. And based on our discussion, we will repurpose the
> allocation to be used for AIP preparation. I think we could create new tables and fields for AIP.
> And not reuse the fields used by WFP. In the future, WFP itself will be updated because of our
> changes in AIP. So let's focus now in AIP." — tracker A1

Three separate things, and they are worth keeping apart:

| The answer | What it means for the build |
|---|---|
| **One pot, consumed in sequence** | `DivisionAllocation` constrains the **AIP**; the WFP is then constrained by **its AIP activity**, which is already how `WfpCeilingService` step 1 works. No double-count *in the intended end state* |
| **New tables for AIP, not WFP's reused** | V18-45 builds `AipDivisionAllocationLedger` mirroring `WfpDivisionAllocationLedger` — the generalise-to-`(sourceType, sourceId)` option is rejected. This closes an open design fork in the plan |
| **WFP changes later** | Explicitly out of scope for v1.8.0 |

⚠️ **The one thing to carry into Phase 2 ticketing.** `WfpCeilingService.ValidateExpenditureSaveAsync`
step 2 draws WFP expenditures down against `DivisionAllocation` *directly*, on top of the AIP-activity
check in step 1. Once the AIP also draws down that same allocation, the same peso is consumed twice:

> Division allocation ₱10,000,000 (General Fund). The AIP plans ₱6,000,000 → the AIP ledger records
> ₱6,000,000 used. The WFP then details that same ₱6,000,000 → the WFP ledger *also* records
> ₱6,000,000. The division now reads as having ₱12,000,000 of a ₱10,000,000 allocation used, or
> conversely can commit ₱10,000,000 in the WFP on top of the ₱6,000,000 already planned. No error is
> raised either way.

That is DECISION A's original failure mode, relocated from the design into the transition period.

#### ✅ Answered 2026-08-26 — and the answer rules out the option this section recommended

Three ways out were offered. Ralph's answer picks the second, and **explicitly rejects the first**:

> "Mostly correct — **but it may be possible that fund allocation will be different during WFP
> creation.**" Clarified the same day: **both** the allocation *amount* and the *fund mix* may
> differ by then, and the WFP is limited by **the lesser** of the AIP activity and the current
> allocation.

That single caveat is decisive. Option 1 — retiring the WFP allocation check for FY2028+ — assumed
the allocation the AIP was validated against is still the allocation in force when the WFP is
written. **It is not.** A check that has been deleted cannot notice that the allocation moved
underneath it, so option 1 would have shipped a silent hole rather than closing one. It is
withdrawn.

**The decision:**

| | Rule |
|---|---|
| **The WFP allocation check stays live** | It is **not** retired for FY2028+. It validates per fund against the allocation **as it stands at WFP time**, not the one the AIP was approved against |
| **Two bounds, both enforced** | An expenditure must satisfy **both** `≤ its AIP activity amount` **and** `≤ the fund's currently remaining allocation`. Whichever bites first governs — that is the "lesser of the two" |
| **A raised allocation gives no extra room** | The AIP stays a cap. Extra headroom requires amending the AIP (V18-73 / RAL-78), not merely a bigger allocation |
| **A cut allocation blocks the difference** | The approved AIP does not entitle the WFP to money that is no longer there |
| **No double-count** | The AIP reservation is **relieved** as the WFP commits against it — it does not sit alongside the WFP's own draw-down |

⚠️ **The netting rule is the part that is easy to get subtly wrong, so it is written out here.**
This is an encumbrance-then-obligation pattern: the AIP *reserves*, the WFP *commits*, and for any
`(division, FY, fund)` the allocation consumed is

> `WFP committed  +  AIP reserved that has not yet been converted into WFP commitment`

**Relief must be per ACTIVITY, not per fund.** When an activity is detailed in the WFP, its whole
AIP reservation is released and replaced by the WFP's actual per-fund commitments. Relieving fund
by fund breaks the moment the fund mix changes: an activity reserved as ₱6,000,000 of General Fund
and then detailed as ₱4,000,000 GF + ₱2,000,000 GAD would leave ₱2,000,000 of stale GF reservation
blocking other work forever.

⚠️ **The dangerous direction of a fund-mix change, now that ceilings are General Fund only
(§12.3).** GF is the only fund that is checked. So an activity **planned under an unchecked fund
and detailed under General Fund** consumes GF allocation with **no AIP reservation standing behind
it** — the one case where the chain `allocation → AIP → WFP` genuinely has a gap. The per-fund
check at WFP time is what catches it, which is the second reason option 1 could not be taken.

FY≤2027 is unaffected either way: it keeps the v1.6 shape and today's behaviour.

#### ⚠️ Amended later on 2026-08-26 — the FY2028 WFP may not be built in this system at all

Ralph, after the above was written:

> "It is possible that the AIP created by our application will be sent to **GSO for their system**,
> which is what was used to create the FY2027 WFP by other offices — although I am not sure yet how
> we would send this to them or what data will be needed."

That reopens the question one level up: **not *how* the WFP check should behave, but whether an
FY2028 WFP exists here to check.** It does not, however, change the decision above — it changes
what gets *built* around it. Checked against the code before deciding:

- The allocation check runs in **four** methods (`GetStatusAsync`, `ValidateExpenditureSaveAsync`,
  `UpsertLedgerForActivityAsync`, `ValidateRecordForFinalizeAsync`), each already parameterised by
  fiscal year. **"Retire for FY2028+" would ADD four conditionals and their tests, not delete code.**
  This section's original "smallest change" framing was wrong about the codebase.
- **If no FY2028 WFP record is created here, none of those four is ever called with
  `fiscalYear >= 2028`.** The check is already inert in that world; retiring it buys nothing.
- If FY2028 WFPs *are* built here, retiring it removes **the only fund-scoped check in the system**.
  Per this service's own header comment, the AIP-budget check (step 1) is *aggregate across all
  funding sources*; only the allocation check (step 2) is fund-scoped. It is precisely the check the
  fund-mix caveat requires.

**So: retiring is all cost and no benefit in both worlds, and the amendment falls on the new work
instead.**

| | Decision |
|---|---|
| `WfpCeilingService` | **Untouched. Zero diff.** Not retired, not made conditional |
| **V18-45** | Build the AIP ledger as an **AIP-only reservation ledger**. **Do NOT build the relief/netting mechanism** — it only earns its keep if an AIP *and* a WFP for the same FY both live here, which is exactly what is unknown. The netting rule above stays written down, unbuilt, for whenever that is settled |
| **V18-81** 🆕 | **Block FY2028+ WFP creation in this system as "not supported yet."** This is the move that resolves the ambiguity: the double-count becomes *impossible* rather than *silently wrong*, and the netting rule need not be decided until we know. Cheap to add, cheap to remove |

⚠️ **This raises the GSO question from a Phase 5 output to something the FY2028 path depends on.**
If GSO's system builds the FY2028 WFP from our AIP, tracker **C2** decides whether our AIP is the
end of the chain or a feed into someone else's.

✅ **Sequencing decided 2026-08-26 (Ralph):** *"I will leave C1 and C2 as-is for now since we
haven't had a discussion yet with GSO — and I would prefer if we already have the structure of our
new AIP that can be provided to them."* So **the AIP structure is built first, and GSO is engaged
with something concrete in hand.** C1/C2 stay **High** rather than being escalated: a Blocker nobody
intends to unblock is noise, and the deferral is deliberate.

⚠️ **That sequencing is only safe because of V18-81.** With FY2028 WFP creation blocked in this
system, deferring the GSO conversation cannot produce a wrong number — it can only produce a missing
feature. Without V18-81 the same deferral would leave the double-count question open while Phase 2
and 3 are being built on top of it.

Two things make the eventual conversation shorter than it sounds:

- `docs/External_AIP_API_Contract.md` (July draft) already anticipated it. Its open item **#4** asks
  GSO literally: *"Anything missing for **WFP building** (e.g. account-level breakdown beyond
  PS/MOOE/CO totals)?"*
- The old format's limitation does **not** carry over. `WfpCeilingService`'s "§2 D3 — AIP data
  carries no per-fund breakdown" is a **FY≤2027** constraint. The new format puts **one fund source
  per expense line**, so the FY2028 export *can* carry the per-fund detail GSO would need to do
  allocation checking on their side. Worth confirming explicitly — if GSO cannot see funds either,
  the fund-mix problem does not disappear, it relocates to a system with **less** information than
  this one.

### 12.2 🆕 DECISION G — the +30% uplift on MOOE and CO

> "Each activities' MOOE and CO of all fund source except for PS will have additional 30% value.
> Values under PS will be displayed as it is."

This corroborates the `X / 30%` note on the meeting page, and it is **new** — no version of the
requirements review, redesign notes or this plan contains it. The rule itself is clear. The
mechanics are not, and every one of them changes a number the province signs:

**✅ Answered 2026-08-26 (tracker G1–G6). The uplift is presentation-only.** Every mechanic below
resolves the same way: the system reasons in base pesos throughout, and the 30% appears only when a
report is rendered. That is a smaller build than any of the six recommendations assumed, and it is
internally consistent — but two of the answers override a recommendation, and both are recorded as
deliberate rather than quietly absorbed.

| # | Question | ✅ Answer | vs. the recommendation |
|---|---|---|---|
| 1 | **Stored or derived?** | **Derived.** "Store the value as it is in the database. The +30% will be in the report" (G1) | as recommended |
| 2 | **Fixed 30%, or configurable?** | **Fixed** (G2) | overrides "configurable per FY, snapshotted". Accepted; noted once and not re-raised. A future rate change becomes a migration, and reprints of historical AIPs will silently move to the new rate |
| 3 | **Does the ceiling compare the uplifted figure or the base?** | **The base** — "the +30% is not included in the ceiling check" (G3) | ⚠️ **overrides the recommendation and the A2-4 principle.** See the warning below |
| 4 | **Does the AIP total that limits the WFP include the uplift?** | **No** — "values or ceiling will be adjusted there" (G4) | answered explicitly, as asked. Consistent with 3: the uplift never enters a limit calculation |
| 5 | **Uplift then round, or round then uplift?** | **Uplift, then round** (G5, read from a bare "Yes" to an either/or question — §12.8 Q1) | as recommended, but the answer does not distinguish the two options; treat as provisional |
| 6 | **Per expenditure line, or per activity?** | *not asked in the tracker* | recommendation stands, and it is self-resolving: ×1.3 distributes over addition, and DECISION 9 rounds once at the activity |

Two scope points, both **✅ confirmed by G6** ("yes to both"): the uplift belongs to the **new
FY2028+ format only** (FY≤2027 rows are historical and are not recomputed), and the printed AIP
form's **Total column is `PS + 1.3 × (MOOE + CO)`** — stated explicitly in `AIP_Form_Spec.md` so
nobody re-derives it as `1.3 × everything`. ✅ **Answered 2026-08-26 (tracker G7):** the **MOOE and CO
columns themselves print uplifted**, not only the Total — PPDC's own call, relayed by Ralph, *"as
long as the entered value (in round up) follows the ceiling."* So `O = SUM(L:N)` still holds and
equals `PS + 1.3 × (MOOE + CO)` at the same time. The qualifier also corroborates **G3** and
**A2-4** together: the ceiling is compared against the entered figures **rounded up** — base,
rounded. ⚠️ The printed and checked figures are **not** exactly 1.3× each other once rounded, and
by how much depends on **G5**, still open. See `AIP_Form_Spec.md` §6.1.

⚠️ **What G3 and G4 together mean, stated plainly because it will look like a bug.** The ceiling
check, the AIP→WFP limit and the stored figures are all in base pesos; only the printed document
carries the uplift. So an office that encodes exactly to its ₱10,000,000 ceiling **passes every
check** and then **prints an AIP reading ₱13,000,000** — 30% over the ceiling on paper, with no
error raised anywhere. Tracker A2-4 settled the opposite principle ("the system must never disagree
with the paper") and G3 knowingly departs from it. **It is recorded in `AIP_Form_Spec.md` §6.2 and
belongs in V18-74's ticket**, or the first person to notice it will "fix" it back and silently
start failing valid submissions.

### 12.3 ↩️ Ceilings on every fund source — made 2026-08-25, **withdrawn 2026-08-26**

**This subsection recorded a reversal that has itself been reversed. Ceilings are General Fund
only — the position originally settled on 2026-08-14.** It is kept rather than deleted so that the
next reader does not re-derive the all-fund version from the meeting notes.

**What was said, in order:**

> ① "No, we will now put ceiling to the other fund source. Same with WFP allocation before."
> — tracker A6-1, 2026-08-25

> ② "In a later discussion, we will go back to old requirement where only GF will have ceiling
> check. Sorry." — tracker A1-b and A6-4, 2026-08-26, **confirmed by Ralph the same day**

So: the 2026-08-14 decision stands, the whiteboard exemption list "except GAD / 20% DF / PS /
LDRRF / Trust Fund" is **not** void, and **PS remains exempt** on top of that as an expense *class*
(tracker A6-2, unaffected by either turn).

**Consequences of the withdrawal:**

- **DECISION H is withdrawn** (§9) and **V18-78 is dropped** — there are no non-GF ceiling figures
  to collect, so §12.8 Q3 disappears with it.
- **V18-46 sums `mooe + co` of the General Fund only.**
- Tracker row **A6-1** keeps its answer but is annotated **superseded**; **A6-4** is closed as
  **N/A**; and the Settled sheet's two 2026-08-14 rows are restored to their original wording.

⚠️ **One code caveat survives the withdrawal, and it is the reason this section is not simply
deleted.** `GetDivisionAllocationAsync` resolves a missing allocation row to **`0m`** — a fund with
no ceiling row is constrained to **zero**, not left unconstrained. "General Fund only" is therefore
*not* something the code does by default; **non-GF funds have to be explicitly excluded from the
check.** Leaving their ceiling rows blank fails every non-GF line at ₱0 remaining. This is a
required behaviour of V18-46, not a nicety.

Two findings from the 2026-08-25 code check remain true and are worth keeping: `BudgetCeiling` and
`DivisionAllocation` have carried `FundingSourceId` since **v1.4.3 / RAL-154** (so a per-fund
ceiling is already the schema, should the province turn again), and `WfpCeilingService` already
checks each expenditure against **its own fund's** allocation rather than against General Fund.

### 12.4 The reviewer is a PPDO user — the LFC is out of the system

> "LFC will no longer [be] reviewing. Certain PPDO users will be the reviewer, where they can comment
> and send back the whole work of an office for update and re-submit." — tracker B5/B6

On the meeting page, the step-4 reviewer is drawn as the LFC with `→ PPDO` written over it. What
changes:

- **V18-05 is no longer `OverrideCanReviewLfc`.** The cross-office reviewer flag is held by PPDO
  staff. Proposed name `OverrideCanReviewAipConsolidated`; the mechanics (a permission that
  deliberately bypasses `OfficeScope`) are unchanged, which is the part that mattered.
- **Tracker B6 — "who are the LFC users" — is void.** The cross-office visibility question survives
  in a smaller form: these PPDO reviewers see every office's budget figures, which is still the first
  permission in the system that does.
- **Two reviewer flags, not one**, and Ralph's "we can add permission flag to the AIP reviewers"
  covers both: **V18-03** = the office's own department-head reviewer (one per office, §12.8 Q9),
  **V18-05** = the PPDO consolidated reviewer.

### 12.5 Entry and review are two pages, and review is query-first

**The encoder's entry tab** (V18-42) — add and update projects and activities, shaped like the WFP
entry page, then **submit the whole office's work** for department review in one action. That second
half corrects V18-51: there are **two submits** (encoder → department head, department head → PPDO),
not one.

**The review tab** is shared by all three roles — encoder, department-head reviewer, PPDO reviewer —
and its defining property is that **it lists nothing by default.** Work is found by query:

| Query by | Note |
|---|---|
| AIP ref code — whole, or **any segment** | Sector, office, program, project, activity — each independently searchable |
| Title / name of the program, project or activity | Free-text match |
| Office code | The common case for a PPDO reviewer |
| A "show everything applicable to me" tag | The escape hatch from an empty page; scoped by the user's own permissions |

✅ **How the filters combine — answered 2026-08-26. OR within a field, AND across fields, and no
query language.** Ralph: *"reviewers may want to see all PPAs across all sectors of an office. Or
certain Programs … I am not sure if AND is needed."*

| | Rule | Why |
|---|---|---|
| **Within one field** | **OR** — each field is multi-value (pick two sectors, three programs) | This is the real ask. "Certain programs" is a *subset*, which a single-value field cannot express |
| **Across fields** | **AND** — office **AND** sector **AND** title | Not a feature to build; it is simply what a filter panel does. It is needed, it is just free |
| **Boolean expressions** | ❌ **Do not build** | No `A OR (B AND C)` syntax, no parser, no query box. Reviewers will not type query syntax, and it would make the SQL unindexable |

⚠️ Worth separating the two examples, because only one of them needs OR: *"all PPAs across all
sectors of an office"* is achieved by setting **office** and leaving **sector blank** — blank
already means "all". It is *"certain programs"* that genuinely needs multi-value.

**There is already a working precedent in this codebase**, so this is a pattern to copy rather than
invent: the **PR List** status filter (`inventory/pr-register`) is multi-select toggle chips with
per-value counts, and its own helper text reads *"Select multiple to combine (e.g. Open + Partially
Delivered = all pending)"* — OR within the field — while its other filter rows AND together. Reuse
that interaction, including the counts on each chip.

This also stays consistent with **V18-76**: `office_id = @x AND sector IN (…)` is index-friendly on
the segment columns that decision creates, whereas a free-text boolean expression over a formatted
ref-code string is exactly the `LIKE '%…%'` shape V18-76 exists to avoid.

⚠️ **A ref-code *prefix* cannot express "one office, all sectors" — and this is the common case, not
an edge case.** The natural instinct is to query by a prefix of the ref code, since the code already
carries both sector and office (*"i imagined i use certain prefix of AIP ref code since it have the
sector and office codes there"*). But the **segment order defeats it for exactly this query**:
**sector is segment 1, office is segment 5**. Pinning the office while letting the sector vary means
wildcarding the *front* of the string and matching the *middle* — not a prefix.

**Verified against the real local AIP data, not reasoned about in the abstract:** the same office
genuinely does appear under several sectors —

```
1000-000-1-01-001   GENERAL   OFFICE OF THE PROVINCIAL GOVERNOR
3000-000-1-01-001   SOCIAL    OFFICE OF THE GOVERNOR - WARDEN
```

— and **11 of the offices present span more than one sector** (office `001` and `017` span three;
PPDO's own `010` spans two). So "all PPAs of office 010 across all sectors" is `1000-…-010` **OR**
`3000-…-010`: two different prefixes, which no single prefix match can cover, and which as a string
search degrades to `LIKE '%-1-01-010%'` — the exact non-SARGable pattern V18-76 exists to prevent.

**So the two inputs divide by what each is actually good at, and both are kept:**

| Input | Use it for | Not for |
|---|---|---|
| **Ref-code prefix** | "All offices in a sector" (`3000-`), and **subtree drill-down** — `…-010-001-` is everything under program 001. Genuinely valuable, and indexable | "One office, all sectors" |
| **Segment filters** (V18-76 columns) | **"One office, all sectors"** — set office, leave sector blank. Also any other mix-and-match | — |

The office-across-sectors case therefore routes through the **segment-column filter**, never the
prefix box. Multi-value OR would also express it by naming both prefixes, but that asks the reviewer
to know the sector list up front; leaving the sector filter blank does not.

⚠️ **This is a Phase 2 schema consequence of a Phase 4 UI requirement, which is why it is recorded
now.** For segments to be queryable they must be **columns, not substrings**: a `LIKE '%…%'` over a
formatted ref-code string cannot use an index and cannot express "segment 3 = 004". Store each
segment as its own indexed column alongside the composite display string, and have V18-44's
generator populate them (new item **V18-76**). Retrofitting this after the ref-code format ships
means a migration over every AIP row.

**✅ 2026-08-26 — fully answered, from the primary source.** The layout is **not** LGU-invented: it
is prescribed by the DBM **Budget Operations Manual for LGUs, 2023 Edition (2024 reprint)**,
Figure 4 "AIP Reference Code Guide" (printed p.25), with the code lists in **Annex C** (sectors) and
**Annex D** (LGU level / office type / office). Local copy:
`D:\RalphFiles\PPDO\PPDO\AIP\BOM-for-LGUs-2023-Edition-(2024-Reprinted)-For-Posting-in-DBM-Website.pdf`.
The meeting-page sketch is superseded; so is the guess recorded here before the manual was read.

| # | Segment | Digits | Meaning | Source |
|---|---|---|---|---|
| 1 | **Sector** | 4 | `1000` General Public Services · `3000` Social Services · `8000` Economic Services · `9000` Other Services | Figure 4, Annex C |
| 2 | **Sub-Sector** | 3 | "if any". Annex C sub-groups — in practice mainly Social Services (Education & Manpower Development, Health, Housing & Community Development) | Figure 4, Annex C |
| 3 | **LGU Level** | 1 | `1` Province · `2` City · `3` Municipality | Figure 4, Annex D |
| 4 | **Office Type** | 2 | `01` Mandatory · `02` Optional · `03` Others | Figure 4, Annex D |
| 5 | **Office** | 3 | Annex D enumerates `001`–`022` per level and type. `1 03 Others` is deliberately left **unenumerated** — the LGU assigns its own | Annex D |
| 6+ | **PPA path** | 3 each | Program → Project → Activity, **one segment per level of the tree** | Figure 4 |

**Verified against all three real codes**, which now make sense rather than looking inconsistent:

- `8000-000-1-01-010-001` (our LDIP seed) — Economic Services / no sub-sector / Province / Mandatory
  / **`010` = Office of the Provincial Planning and Development Coordinator**, i.e. PPDO itself /
  Program `001`.
- `8000-000-1-03-009-001-001-001` (Ralph's example) — … / **Others** / office `009`, an LGU-assigned
  code, which is exactly why it is not in Annex D / Program · Project · Activity.
- `8000-000-1-01-016-004-001-003-001` (the `.xlsm` parser test) — … /
  **`016` = Office of the Provincial Agriculturist** / a **four**-segment PPA path.

✅ **This answers "where the sequences reset":** the manual requires that "all activities and
projects be subsumed under a specific program" (printed p.26), so the PPA path is a tree — program
numbered within its office, project within its program, activity within its project. The one thing
left to confirm is whether the **program sequence restarts each fiscal year** or runs continuously
(the v1.3 LDIP work numbered programs continuously across sub-office groups).

⚠️ **This changes V18-76, and dissolves the blocker this section raised.** Segments **1–5 are fixed**
— five indexed columns, as originally proposed, and they are pure office identity. Segments **6+ are
a variable-length path**, and storing *those* as fixed columns was the wrong shape: it is what forced
the "we need a defined maximum depth" problem. There is no maximum. But there does not need to be
one — the AIP **already is** a Program/Project/Activity tree, so give each node a `seq` that is
unique among its siblings and **render** the code from the root-to-node path. "Segment 6 = 004" then
becomes a query on `program.seq`, indexed naturally, with no cap and no migration when the tree
deepens. The composite string stays a display value.

**Who may do what on that page — ✅ confirmed and narrowed 2026-08-26 (tracker B10, B11).** There
are **two kinds of reviewer, with different powers**, and the distinction is now explicit rather
than inferred:

| Role | Edit values | Comment |
|---|---|---|
| Encoder | ✅ | — |
| **Department-head reviewer** | ✅ — "for them to update any minor details they found during review" (B11) | ✅ |
| **PPDO reviewer** | ❌ — "they won't be able to apply any update, just comment" (B11) | ✅ |

The reading of tracker B3 was correct: the PPDO reviewer comments and returns but does not edit.

⚠️ **Comments are narrower than DECISION 10 recorded.** B10 replaces "whole submission *or* one
program / project / activity" with **inline comments only** — there is no whole-submission comment.
The lifecycle is specified: each comment carries a **"Mark as resolved" checkbox**, set when the
work is returned to the office; on re-submit the app **counts unresolved comments and warns**, and
the user may **proceed anyway or cancel**. It is a soft gate, not a hard one.

✅ **2026-08-26 — both of B10's gaps are now closed, and the interaction is specified.** The two
things B10 left open (what "inline" anchors to, and whose comments enter the resolve flow) were
answered together with the comment UI, modelled on GitHub's inline review comments:

| Question | Answer |
|---|---|
| **What does "inline" anchor to?** | **The row.** Not a field, not an expense line — a gutter control on the left of each row, the way GitHub anchors a comment to a line. Field-level anchoring was rejected: the AIP grid is wide, and it would multiply anchor points for no reviewer benefit |
| **Who may resolve?** | **Only the authoring side.** A PPDO reviewer's comment is resolved by PPDO; a department-head reviewer's comment is resolved by the department head. The recipient never clears the comment addressed to them — an office cannot resolve a PPDO comment, and an **encoder cannot resolve their department head's** (confirmed 2026-08-26). Without this the soft gate is self-marking: the recipient could clear everything and re-submit with nothing actually addressed |
| **Threading / replies?** | **None.** Resolve is the only action on a comment. (This also answers §12.8 Q6's "comment threading") |
| **Default visibility** | **Collapsed.** Rows carrying comments are marked (row highlight + a show-comments control in the gutter); the comment body is opened deliberately, so the grid stays readable |

**The unresolved counter is per-source, and it is a filter, not just a number.** Because comments
are collapsed by default, a user could otherwise re-submit having never opened one. So the count
appears **on screen as filter buttons split by author**, and the **same split appears in the
re-submit warning**. There are exactly **two** sources to split by, not three — per the role table
above the **encoder never comments at all**, so every comment is authored by either the
**department-head reviewer** or the **PPDO reviewer**. Two reasons for the split: the reader cannot
resolve either set themselves (above), so one merged number would imply an action that does not
exist for them; and "3 unresolved from PPDO" is the sentence that actually changes whether someone
re-submits.

**History is a first-class part of this page, not just an audit table.** Ralph: *"As long as the
history is preserved and can be displayed in the review page … then they can backtrack the comments
and changes made."* So V18-77's submission history gets a **"Show History"** control on the review
page itself — comments (including resolved ones) and the submit / return / re-submit chain, readable
in place. Resolved comments are therefore **never deleted**, only marked; the resolve action is a
state change, not a removal.

### 12.5a 🆕 The office kanban — a shape for V18-57's readiness view

**Raised 2026-08-26 by Ralph, as his own initiative.** It is recorded here rather than treated as
new scope, because it is a **concrete shape for two work items that already exist**: V18-57's
"readiness view of who has and has not submitted" and V18-58's "review queue page". Tracked as
**V18-82**.

**Why it earns its place.** §12.5 makes the review page deliberately **query-first — it lists
nothing by default**, and the only escape hatch on record is a "show everything applicable to me"
tag. That is a poor landing for a PPDO reviewer whose first question is *"who is done and who is
not"*. A board of office cards answers that at a glance **and** doubles as the query entry point:
clicking a card filters the review list to that office.

**Columns** — four map onto §12.6's workflow states, plus one for the implicit pre-Draft state that
§12.6's table never names:

| Column | §12.6 state | Note |
|---|---|---|
| **Not Started** | *(none — pre-Draft)* | Office exists and has LDIP-seeded programs, but **zero activities created**. §12.6's table starts at Draft; a readiness view needs this |
| **In Progress** | Draft | **One or more activities created**, not yet submitted for department review |
| **Office Review** | Department review | |
| **PPDO Review** | Submitted to PPDO | Locked (V18-52) |
| **Done** | Consolidated | |

✅ **The Not Started / In Progress boundary is activity count, confirmed 2026-08-26.** Seeded
programs alone do **not** count as started — they arrive from LDIP without anyone in the office
touching the record, so an office that has never opened the page would otherwise show as working.
The card query is therefore: **zero activities → Not Started; one or more → In Progress**, with the
submission states taking precedence over both once the work moves on. Stated explicitly so the
query is not guessed at per-implementation.

⚠️ **"Returned by PPDO" is deliberately NOT a sixth column.** It is a real §12.6 state, but returned
work re-enters exactly the Department-review condition — editable by encoder *and* reviewer, moving
on when the department head re-submits — so it belongs **in the Office Review column carrying a
"Returned" badge**. This keeps what is arguably the reviewer's most actionable signal (*"I sent this
back — has it come back yet?"*) without collapsing it into an undifferentiated "In Progress", and
without column sprawl. Agreed 2026-08-26.

**Card contents and the denominator.** Each card is one office and shows its **assigned program
count** and **created activity count**. The denominator question ("what makes a program *assigned*
to an office?") is answered — and it is **not new logic**:

> Programs reach an office through **LDIP**. `AipService.SeedProgramsFromLdipAsync` seeds AIP
> programs from LDIP, and `AipRecord.LdipId` carries the link. An office's programs are then the
> `AipProgram` rows under the `AipOffice` whose `RefCode` **suffix-matches** that office's
> configured `Office.OfficeRefCode` — precisely the resolution
> `AllocationService.GetProgramAssignmentsAsync` already implements for the Allocation page's
> **PPA → Division** tab. The kanban should reuse that path, not re-derive it.

⚠️ **No "% complete", and this is deliberate.** Ralph: *"we don't know how many activities will be
created per-office … this is up to them."* There is a denominator for **programs** but none for
**activities**, so a percentage would be fiction. This is also the stated reason the view is a
**kanban rather than a chart**: column position is the real signal, and the counts are context
only. Do not let this drift into a progress bar.

**Access.** PPDO reviewers only (V18-05's consolidated-reviewer flag). Office users must **not** get
this board — they would see exactly one card, which is noise; their own status belongs as a chip on
their own AIP page. Scoping it this way is also what keeps the board small enough not to overwhelm:
**19 active offices** across five columns.

**Nav consequence — AIP splits into Entry and Review.** §12.5 already says entry and review are two
pages (though its body then calls them "tabs" — settled here in favour of **two pages**). With the
kanban living on Review, Review becomes the PPDO reviewer's home and deserves to be a first-class
destination rather than a tab inside an entry page; it also lets the two be permission-gated
separately (Entry → encoders, Review → reviewers).

⚠️ **Implement as two siblings, not a third nesting level.** `Sidebar.tsx` is hand-written JSX with
exactly **one** collapsible level (Budget Planning → flat children); it has no nesting primitive.
There is already precedent for a flat item pointing at a sub-page — **WFP** renders as one item
while linking to `/budget-planning/wfp/entry`. So add **"AIP Entry"** and **"AIP Review"** as two
sibling children of Budget Planning. Same outcome, no new sidebar machinery, independently gated.

### 12.6 The workflow, as now answered

| State | Who may edit | Who may see it | Moves on by |
|---|---|---|---|
| **Draft** | Encoder(s) — two or more per office (tracker D5) | The office | Encoder submits for department review |
| **Department review** | **Encoder and reviewer both** (tracker B3) — the reviewer may edit values directly, not only comment | The office | Department-head reviewer submits to PPDO |
| **Submitted to PPDO** | **Nobody — locked** (tracker B3) | The office (read-only) + PPDO reviewers | PPDO reviewer returns it, or it stands |
| **Returned by PPDO** | Encoder and reviewer again | The office | Re-submitted by the department head |
| **Consolidated** | — | **PPDO reviewers only.** PPDO's *division* users cannot see the consolidated document (tracker B4) | — |

**✅ 2026-08-26 — PPDO's own offices run the same ladder (tracker B12).** PPDO's divisions submit
to a **PPDO department-head reviewer** first, mirroring any other office, and that person is
**distinct from the PPDO consolidated reviewer**. PPDO encoders "will only be able to see their
division's work", so division scoping applies inside PPDO exactly as office scoping applies outside
it.

✅ **The record shape is answered — 2026-08-26, tracker B12-b — and it is neither of the two options
this plan had been posing.** Ralph: *"For PPDO, the programs will be assigned to the divisions (same
as in WFP) for the division of work. And like in WFP they will only see their own division's work.
This special filter will not be applied to other offices."*

| | |
|---|---|
| **PPDO's AIP record** | An **ordinary office record**. No per-division records, and no division tag on `AipOffice`. PPDO's divisions **never appear on the printed form** — PPDO prints as office rows exactly like any other office |
| **Where the division lives** | On the **program**, via the existing **`ProgramDivision`** map (v1.2 / RAL-99) — the same mechanism WFP already uses. A program may be assigned to **more than one** division; `AllocationService` already handles a set |
| **The visibility rule** | PPDO users see only programs assigned to their own division. ⚠️ **Host office only** — it must *not* apply to ordinary offices |

⚠️ **Two consequences for Phase 2, and neither is free.**

1. **V18-11 is confirmed load-bearing, exactly as it predicted.** `ProgramDivision` is keyed on
   `(OfficeRefCode, ProgramRefCode)` **strings**. It is now the sole carrier of PPDO's visibility
   scoping, so string keys stop being untidiness and become a correctness risk — which is what
   V18-11 already says. Convert to real FKs **before** Phase 3 builds on it.
   ℹ️ And the reason those keys are strings **lapses in the redesign**: the entity's own comment says
   they are ref-code-keyed "so that assignments survive supplemental AIP re-uploads, which recreate
   `aip_programs` rows with new surrogate IDs". FY2028+ has **no `.xlsm` upload** (§4a/§4b) — so the
   justification for the string keys does not carry into the new format.
2. **"Host office only" is not enforced anywhere today.** `WfpService.GetFilteredAsync` takes
   `divisionId` as a **caller-supplied parameter**, and `OfficeScope.IsHostOfficeUser` is consulted
   in `PermissionService` and `LandingPageResolver` but **not** in `WfpService`. So the conditional
   — *apply the division filter for the host office, and only for the host office* — is new work,
   not a pattern to copy. Building it as an ordinary always-on filter would silently scope every
   office's AIP by division.

⚠️ **2026-08-26 — the ladder does not stop at Consolidated, and the plan had no idea.** Ralph
described a Sangguniang Panlalawigan stage, hedged as his own understanding. **The DBM Budget
Operations Manual confirms it, and adds a stage he did not mention** (printed pp.23–24):

> **Preparation and/or Approval of AIP by the LDC** — the Local Development Council; SLPBC 2016
> puts AIP preparation **within the month of May**.
> **Submission of LDC-approved AIP to the Sanggunian** — reasonable time prior to **June 7**.
> **Approval of the AIP by the Sanggunian** — on or before **June 7** of every year
> (DILG-NEDA-DBM-DOF JMC No. 1, s. 2016).

So there are **two** external bodies downstream of PPDO's work, not one: the **LDC**, then the
**Sanggunian**. The statutory shape is therefore:

| Stage | What happens | When | In this system? |
|---|---|---|---|
| **Consolidated & approved by PPDO reviewers** | Where §12.6's table above ends | — | ✅ yes |
| **🆕 Local Development Council** — for a province, the **PDC (Provincial Development Council)** | Prepares and/or **approves** the AIP. Ralph did not name this stage initially; the BOM does, and he confirmed it as the PDC | within **May** | ◻️ **generation in scope; outcome tracking open — tracker B14** |
| **🆕 Sangguniang Panlalawigan** | The PDC-approved AIP is submitted to the SP — elected board members and the Vice Governor — which **approves it by resolution** | submit before **June 7**, approve on or before **June 7** | ◻️ **generation in scope; outcome tracking open — tracker B14** |
| **🆕 Supplemental changes** | Amendments reportedly go to the SP **first**, before taking effect | — | ❓ **unknown — tracker B15** |
| **🆕 FY2028 budget planning — the WFP** | Built in **GSO's** system, but the WFP document is what **PBO** needs | — | ❓ **unknown — tracker C2** (§12.1) |

Four consequences, none of them yet decided:

- **"Approved" is not the terminal state the plan assumed — and it is two stages away, not one.**
  V18-73's note still reads "don't make **LFC** approval terminal", which is now triply stale: the
  LFC is out (§12.4), it was never the terminal authority, and the real chain is
  **PPDO → LDC → Sanggunian resolution**.
- **The amendment path (V18-73 / RAL-78) has a named external gate.** If supplementals route through
  the SP, amendment readiness is not "allow edits after approval" — it is a second trip through an
  external body: a workflow question before it is a schema one.
- **The LFC's statutory role is upstream, which corroborates B5/B6.** In the BOM the LFC identifies
  *investible funds* at the LDIP/investment-programming stage (printed p.23) — not AIP review. So
  "the LFC no longer reviews" is consistent with the manual, not a local deviation.
- **PBO is the WFP's consumer, GSO only its builder.** That matters for tracker C1: the office that
  *needs* the document and the office that *makes the tool* are different, and they may not give the
  same answer.

✅ **Confirmed 2026-08-26 (Ralph):** *"For our case the LDC is the **PDC** (Provincial Development
Council). The AIP generated by our portal/application will be presented there, then in the
Sanggunian."* Two things follow, and they narrow B14 considerably:

- **Both bodies are offline as far as this system is concerned — what they consume is a document we
  produce.** So the *generation* half is settled and in scope. What stays open in B14 is only
  whether the system **records the outcome** (a state beyond Consolidated, a resolution reference,
  a return-for-revision path), or whether someone simply reports back.
- ⚠️ **This raises the bar on V18-60, the official AIP form export.** It is not an internal report:
  it is the document **presented to the PDC and then to elected officials**, under a statutory
  deadline. Fidelity to the official layout stops being a nicety — and it is where the +30% uplift
  becomes visible to people who did not encode it, including the ceiling-exceedance that DECISION G
  deliberately permits (§12.2). That warning belongs on the form itself or in its covering note,
  not only in this plan.

*(The PDC is already a familiar entity in the portal — "PDC Files" is one of the seeded Records
Management resource links.)*

ℹ️ **No conflict with tracker B7** ("no deadline enforced in the system"). B7 is about *offices
submitting to PPDO*, which is internal and stays undated. **June 7 is statutory and applies to the
Sanggunian step**, downstream of everything this system currently models — recorded here so nobody
later finds the date and concludes B7 was answered wrongly.

Two structural answers inside that table:

- **"Consolidated" is not a new record.** It is the **existing multi-office record, filled in office
  by office as they submit** (tracker B4-a) — which is a considerable simplification of V18-55, and
  it fits the FY2028 shape only if that record survives the redesign as a container. Confirm when
  V18-40 is designed.
- **Partial consolidation is expected**, not an edge case: PPDO reviewers review "the consolidated
  work so far" (tracker B4-b), before every office has submitted.

**No deadline gate** (tracker B7): the deadline is communicated to offices outside the system.
What is wanted instead is **history** — "let's document the history so we can track" — so V18-57
becomes a submission audit trail plus the who-has-and-hasn't view, with no date enforcement.

### 12.6a 🆕 Sub-office groups are user-created — and are NOT the division

Raised by Ralph 2026-08-26, after B12-b: *"I would like to bring up again the sub-office. We also
need for users to add this. This is mostly entered on top of a program — I think in the current AIP
page that we will retire or change, this is considered."*

**What it is.** An office may hold several named groups under **one** office ref code, each heading
its own block of programs on the printed form. The province's FY2027 file is full of them — three
`3000-000-1-01-001` office rows on the SOCIAL sheet reading `OFFICE OF THE GOVERNOR - WARDEN`,
`- AKAP-HUB` and `- HOUSING`, each with its own programs and its own shaded subtotal row. Group
identity is the **`(Sector, Name)`** pair, which is what `AipOffice` and `LdipOffice` already store.

**✅ It is already built, on the LDIP side.** `LdipForm.tsx` (RAL-61) implements precisely the
interaction Ralph describes — the sub-office name is a field **inside the "add a program"
mini-form**, not a separate step:

> Pick a Sector → see the office-level ref-code preview → set the office/sub-office group name,
> choosing an existing name from the suggestions to keep adding under that group **or typing a new
> one to start another** → name the program → enter its budget → Add.

Names are uppercased to match the source files and normalised server-side; **program numbering runs
continuously across groups that share a ref code**, and removals renumber without gaps. **V18-42
should lift this, not redesign it.**

⚠️ **Do not conflate the sub-office group with the division.** Both attach at program level and
they are orthogonal:

| | Sub-office group | Division |
|---|---|---|
| **Stored on** | `AipOffice` — `(Sector, Name)` | `ProgramDivision` — `(OfficeRefCode, ProgramRefCode)` → `DivisionId` |
| **Printed?** | **Yes** — it is an office row on the form | **No** — never appears on the AIP |
| **Applies to** | every office | **the host office only** (B12-b) |
| **Cardinality** | a program sits in exactly one group | a program may be assigned to several divisions |
| **Purpose** | how the document is *structured* | how the work is *divided* |

ℹ️ The two do not collide: because program numbering is continuous across groups sharing a ref
code, `ProgramRefCode` stays unique within an office, so `ProgramDivision`'s key still resolves to
exactly one program even when several sub-office groups share the office code.

### 12.7 New and changed work items

| # | New work item | Phase |
|---|---|---|
| **V18-74** | **The +30% uplift** — ✅ **scope settled and reduced 2026-08-26**: derived at render time from a stored base, **fixed** 30% (no per-FY rate, no snapshot), FY2028+ only, and applied **in reports only** — *not* in the ceiling check and *not* at the AIP→WFP boundary. The ticket must state that the printed total may legitimately exceed the printed ceiling (§12.2) | 2–3 |
| **V18-75** | **Query-first review page** — ref-code (whole or per segment), title, office code, and the "everything applicable to me" tag; empty by default. ✅ **Combination rule settled 2026-08-26 (§12.5): OR within a field (multi-value), AND across fields, and no boolean query syntax.** Copy the PR List status-chip interaction (`inventory/pr-register`), counts included — it already implements exactly this. ⚠️ **The ref-code prefix box and the segment filters are not interchangeable:** sector is segment 1 and office is segment 5, so a prefix cannot express "one office, all sectors" — that must route through the segment columns. Verified against real data, where **11 offices span multiple sectors** | 4 |
| **V18-76** | **Ref-code segments as indexed columns.** ✅ **Reshaped 2026-08-26 once the DBM manual was read (§12.5):** two halves, not one. Segments **1–5** (sector · sub-sector · LGU level · office type · office) are **fixed** — five indexed columns, and they are pure office identity, so they belong with V18-32's ownership FK rather than with the PPA tree. Segments **6+** are a **variable-length PPA path** and must **not** be columns: give each Program/Project/Activity node a sibling-unique `seq` and render the code from the root-to-node path. This removes the "needs a defined maximum depth" blocker — there is no maximum, and none is needed | 2 |
| **V18-77** | **Submission history / audit trail** — submitted, returned, re-submitted, by whom and when (replaces V18-57's deadline gate) | 4 |
| ~~**V18-78**~~ | ~~Non-GF ceiling and allocation data~~ ↩️ **dropped 2026-08-26** — the all-fund ceiling was withdrawn (§12.3). What survives is folded into V18-46: non-GF funds must be **explicitly excluded** from the check, because a missing allocation row resolves to `0m`, not to *unlimited* | — |
| **V18-79** | **One-reviewer-per-office constraint** — ✅ **shape decided 2026-08-26 (tracker B13): a validation error, not a database constraint.** "Enforce by convention, just add an error if an app admin accidentally assigns another reviewer to the same office … so that if they want many reviewers per office in the future, we won't have many issues when changing." So: an application-layer check in the user-assignment path, **no filtered unique index**, and no "reviewer on leave" override to design — the constraint is soft by intent | 1 |
| **V18-81** 🆕 | **Block FY2028+ WFP creation in this system as "not supported yet"** — it is unknown whether the FY2028 WFP is built here or in GSO's system (§12.1). Blocking it makes the AIP/WFP double-count *impossible* rather than *silently wrong*, and defers the netting rule until the answer is known. Cheap to add, cheap to remove; pairs with V18-45's reduced scope | 2–3 |
| **V18-80** 🆕 | **AIP expenditure procurement lines from the Price Index** — tracker W13: activities carry expenditures with accounts, and some carry **procurement items sourced from the Price Index config, the same as WFP**, in both the entry tab and the review tab. Nothing in Phase 3 covers this today; V18-42 says only "enter expenditures", and V18-65 lists the price index purely as offline cached reference data | 3 |
| **V18-82** 🆕 | **Office kanban — the shape of V18-57's readiness view** (§12.5a). Five columns (Not Started · In Progress · Office Review · PPDO Review · Done), with **"Returned by PPDO" as a badge inside Office Review, not a sixth column**. Not Started vs In Progress is decided by **activity count, not seeded programs** (zero → Not Started), since LDIP seeding happens without the office touching anything. One card per office showing assigned-program and created-activity counts; clicking a card filters the review list to that office. Denominator reuses `AllocationService.GetProgramAssignmentsAsync`'s LDIP→AIP ref-code suffix match — do not re-derive it. **No "% complete"** — there is no activity denominator, which is why this is a kanban and not a chart. PPDO reviewers only; office users get a status chip on their own page instead. Also serves as the non-empty landing for V18-75's query-first page | 4 |
| **V18-83** 🆕 | **Split the AIP sidebar item into "AIP Entry" and "AIP Review"** (§12.5a) — **two siblings under Budget Planning, not a third nesting level**: `Sidebar.tsx` has one collapsible level and no nesting primitive, and WFP already sets the flat-item-to-sub-page precedent. Gated separately (Entry → encoders, Review → reviewers). Settles §12.5's internal "two pages" vs "two tabs" wording in favour of **two pages** | 4 |

**Changed in place:** V18-03 (one reviewer per office) · V18-04 (premise questioned) · V18-05 (LFC →
PPDO) · V18-41 (closed LDIP list) · V18-42 (encoder tab + submit) · V18-45 (own tables) · V18-46
(all funds, submit-time, uplift) · V18-49 (the ceiling gate) · V18-51 (two submits) · V18-52
(locking rule) · V18-53 (both comment levels) · V18-55 (consolidation shape) · V18-56 (PPDO not
LFC) · V18-57 (history, no deadline) · V18-59 (rounding settled) · V18-71 (promoted to required).

**Changed again 2026-08-26:** V18-45 (**AIP-only reservation ledger — the relief/netting mechanism
is deferred**, §12.1) · V18-73 (terminal authority is the SP resolution, not the LFC) · V18-46
(**General Fund only**, and the base figure — not the uplifted one) · V18-53 (**inline comments only**, with a resolve checkbox and a soft unresolved-count warning
on re-submit — no whole-submission comment; **later the same day, row-level anchoring, no threading,
and only the authoring side resolves — neither an office nor an encoder clears a comment addressed to them**) · V18-57 (**the readiness view is an
office kanban** — V18-82) · V18-74 (reduced to a report-side concern) · V18-79
(validation error, not a unique index) · **V18-04 — see below.**

⚠️ **Phase 1 gains real work from the 2026-08-26 answers, which it did not have before.** Tracker
B11 answers §12.8 Q7 by **splitting the reviewer into two roles with different powers** — the
department-head reviewer edits and comments, the PPDO reviewer only comments. V18-04 / **RAL-256**
is currently scoped as a single blanket "reviewers cannot write" denial, and that premise is now
wrong: it must distinguish the two reviewer kinds. RAL-256 is a Phase 1 ticket, so this is the one
item here that affects work which could start immediately. V18-03 and V18-79 move with it.

### 12.8 The ten items raised here — ✅ all answered 2026-08-26

Each was a row in `v1.8.0_Open_Items_Tracker.xlsx`; all ten now carry an answer. **Nothing below
blocks a phase.** What remains are three fragments and two readings, marked ⚠️.

| Q | Item | Outcome |
|---|---|---|
| 1 | **DECISION G's mechanics** (§12.2) | ✅ **Presentation-only uplift** (G1–G6). Base stored, fixed 30%, reports only — the ceiling and the WFP limit both see the base. ⚠️ G5 answered "Yes" to an either/or; read as *uplift then round* |
| 2 | **The AIP/WFP transitional double-count** (§12.1) | ✅ **Answered 2026-08-26, against the recommendation.** The WFP allocation check **stays live** — the allocation may differ in amount *and* fund mix by WFP time, so retiring it would hide the change. An expenditure is bound by **the lesser** of its AIP activity and the fund's current remaining allocation; the AIP reservation is **relieved per activity** as the WFP commits. V18-45/46 are ticketable |
| 3 | ~~The non-GF ceiling figures~~ | ↩️ **Moot** — the all-fund ceiling was withdrawn (§12.3). No figures to collect; V18-78 dropped |
| 4 | **The ref-code segment layout** | ⚠️ **Half-answered** (B9). The format is `8000-000-1-03-009-001-001-001` and the meeting sketch is to be ignored — but the **segment meanings and reset points are still missing**, and the count varies with depth. V18-76 cannot create indexed columns without a defined maximum depth (§12.5) |
| 5 | **A ceiling cut after encoding** | ✅ **Confirmed 2026-08-26** — the encoded work **stands and fails at submit**; nothing is destroyed or flagged when the ceiling is cut. Consistent with DECISION C. ⚠️ Implementation trap in V18-48 — see §5 |
| 6 | **Comment threading and lifecycle** | ✅ **Fully answered.** (B10) narrowed it to inline comments only, a "Mark as resolved" checkbox, and a soft unresolved-count warning on re-submit that the user may override — **narrowing DECISION 10**, which allowed a whole-submission comment. ✅ **2026-08-26 closes the two remaining gaps (§12.5): row-level anchoring, no threading, and only the authoring side resolves (an office cannot clear a PPDO reviewer's comment, nor an encoder their department head's)**; the unresolved count is split by the two authoring roles |
| 7 | **The reviewer write-denial contradiction** | ✅ **Answered** (B11) — the denial does not go away, it **splits**: department-head reviewer edits and comments; PPDO reviewer comments only. RAL-256 / V18-04 must be re-scoped. The one Phase 1 consequence (§12.7) |
| 8 | **PPDO's own internal path** | ✅ **Yes to both** (B12) — PPDO divisions submit to a PPDO department-head reviewer, distinct from the consolidated reviewer; PPDO encoders see only their own division. ⚠️ `AIP_Redesign_Notes.md` §4 Q2 (record shape) is still open underneath (§12.6) |
| 9 | **One reviewer per office** | ✅ **Convention, enforced softly** (B13) — an application-layer error on double assignment, deliberately not a database constraint, so future multi-reviewer support is a removal rather than a migration. No "on leave" rule needed |
| 10 | **Four things read off the meeting photos** | ✅ All four answered — see below |

**Q10, item by item (tracker W10–W13):**

- **"PBO Connect" / "Recall AIP"** — ✅ a **later stage**, outside PPDO for now. Not v1.8.0 scope.
- **`format { Price · Budget · AIP }`** over the fund list — ✅ "just per activity-fund source".
  It is the per-activity fund-source list, as read.
- **"set limit Prog 1 of cost, Proj 2 ½ half cost"** — ✅ **not a rule.** "Just a sample scenario
  during the meeting … something like a note or comment that they want adjustment to a project."
  No per-project limit to build.
- **"delete item / change cost → Price Index"** — ⚠️ **not a maintenance note — it is scope.**
  Activities carry expenditures with accounts, and some carry **procurement items drawn from the
  Price Index config, the same as WFP**, in both the entry tab and the review tab. This is new work:
  **V18-80** (§12.7).

Asking the four was worth it: three closed at no cost, and the fourth was the only one that would
have been wrong to assume.

**Also confirmed in passing, no work implied:** amounts under ₱1,000 print as `1` (A2-2); there are
no negative amounts (A2-3); Excel cells hold rounded thousands (A2-5); pesos are stored and
thousands displayed (A6-3, corroborating DECISION E); and the printable AIP form is the same
official layout the `.xlsm` importer already reads (D3), so V18-60 builds from RAL-238's
description-column rule.

**One future direction, not yet scope:** "the data generated in AIP can be used by other office, but
the details [are] not yet set." That is tracker C1/C2 and V18-62/63 — the canonical dataset and the
`docs/External_AIP_API_Contract.md` API. Nothing to build until those consumers say what they need,
and the standing advice holds: ask for a **filled-in** copy of whatever they use today, not a
description.

---

*Companion to `AIP_Redesign_Notes.md` (the record of Ralph's description and the decisions settled
so far), `AIP_Form_Spec.md` (the printable AIP form) and `v1.8.0_Open_Items_Tracker.xlsx` (the open
questions and their answers). ⚠️ `AIP_Requirements_Review.md` and `Monday_Questions.md` are cited
above but do not exist — see the note at the top of this file.*

*No longer "plan only": §12 records decisions taken on 2026-08-25 and 2026-08-26, and Phase 1 is
partly shipped (§3.6).*
