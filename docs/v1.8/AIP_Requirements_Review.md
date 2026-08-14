# v1.8.0 Requirements — Review, Gaps & Suggested Additions

> Reviewed 2026-08-14 against Ralph's draft requirements + the whiteboard photo from the
> discussion. Checked against the codebase as it stands on `release/1.8.0`.
>
> Companion docs: `AIP_Redesign_Notes.md` (incomplete, Ralph's earlier description),
> `Office_User_Path_Findings.md` (office-user isolation + reviewer model),
> `../PWA_Feasibility_Study.md` (offline — §11 in particular).
>
> **Nothing here is a decision.** It is (a) what the whiteboard contains that the written draft
> does not, (b) what already exists in code and therefore is not new work, (c) what is missing, and
> (d) what I would add. Items marked **→ DECISION** block ticket-writing.

---

## 0. Headline

Three things stand out from the review:

1. **The Allocation requirement is ~80% already built.** Per-office ceilings, per-division
   allocation and program allocation all exist with the right shape (v1.4.3). The real work is a
   permission split, not a feature. See §4.
2. **The AIP expenditure model as described cannot be stored in the current schema.** Multiple
   fund sources per activity, and one expenditure split across PS/MOOE/CO, break `AipActivity`'s
   single `FundingSourceId` + three amount columns. This is the largest structural change in
   v1.8.0. See §5.1.
3. **The stated rounding rule and its own example disagree** — "round *up* to the nearest thousand"
   applied to `1,234,567.89` gives `1,235,000`, not the `1,234,000` in the draft. Every peso figure
   in every AIP document depends on which one is meant. See §8.

Plus one observation offered as pushback, not obstruction: **52 users is not a load problem**
(§6.3). Offline entry is well justified by *connectivity*; justifying it by *load* points at the
wrong fix.

---

## 1. What the whiteboard has that the written draft does not

Working left-to-right through the photo. These are the items I could not find anywhere in the
written requirements.

| # | On the board | Why it matters | Status |
|---|---|---|---|
| W1 | **"Limit Dept Head — Except: GAD, 20% DF, PS, LDRRF, Trust Fund"** | Reads as: department heads encode their own office's items, **except** these five, which are prepared centrally. That is a scoping rule affecting who may create what — and it is not in the draft at all. | **→ DECISION** — see §5.6 |
| W2 | **"cost auto generated"** | Activity costs roll up to project and program automatically rather than being typed at each level. Confirms totals are derived, not entered. | Assumed; confirm |
| W3 | **"Review Comments"** (right side, next to the review arrow) | Reviewers leave *comments*. The draft says work is "sent for review" and "once approved" but never mentions comments, rejection, or send-back. | **→ DECISION** — see §5.3 |
| W4 | **"Submitted for Review of LFC → Locked"** | Submitted work locks against editing. The draft doesn't state what happens to editability at each transition. | **→ DECISION** — see §5.3 |
| W5 | **"Output/Report → … → Project Profile"** | A per-project profile document as an output, distinct from the AIP form itself and from the §7 database exports. | Missing from draft — §7 |
| W6 | **"20 Depts"** | ~20 offices against 44 non-PPDO users ≈ 2 users per office (encoder + reviewer). Consistent with the draft; useful for sizing. | Consistent |
| W7 | **Fund × class matrix** (`fund | total | PS | MOOE | CO`, rows for Gen. Fund / LDRRF / 20% DF / DOH) | This is the clearest statement of the new expenditure shape — one activity, several funds, each split three ways. Corroborates §5.1. | Confirms §5.1 |
| W8 | **"1 budget item, multiple fund source — [MFS] default single"** | The multi-fund toggle defaults to **off**. Matches the draft's toggle idea and gives the default. | Consistent |
| W9 | **"₱5M cost" against Prog/Project** | Suggests a cost ceiling or threshold at project level. Unclear — may be an example figure. | **→ CLARIFY** |

Also on the board and already covered by the draft: the importable file → PPDO / PBO / Actg / PTO /
GSO (§7), and the round-up arithmetic (§8).

---

## 2. Password Reset

> Requirement: *"Users can trigger password reset. Suggest the steps — do not make it complicated."*

### 2.1 The constraint that decides this

**There is no email infrastructure in the codebase.** No SMTP client, no SendGrid, no MailKit, no
`IEmailService` — nothing sends mail today. And on `User`:

- `Email` is **optional** (`string?`) and **unverified** — the unique index is filtered on
  `IS NOT NULL`, so accounts may have no email at all;
- login is by **username**, not email.

So the familiar "enter your email, click the link" flow is not a small feature here. It needs an
email provider (Azure Communication Services or SendGrid), DNS records for a government domain, a
token table, and a campaign to collect and verify ~52 addresses. That is the opposite of "do not
make it complicated".

### 2.2 A real security finding, found while looking at this

`UserService.ResetPasswordAsync` sets every reset account to the **same hardcoded default**
(`TamarawUser2026!`), and **nothing forces the user to change it**. There is no
`MustChangePassword` flag anywhere. That password is also written down in `CLAUDE.md`.

So today: any reset account sits indefinitely on a known, documented password, and usernames are
guessable. This is worth fixing regardless of what the reset flow becomes.

### 2.3 Recommended flow — no email required

Deliberately boring, uses what already exists, and closes §2.2 on the way:

**What the user does**

1. On the login page, clicks **"Forgot password?"**.
2. Types their **username** and clicks Request Reset. That is the whole form.
3. Sees: *"Your request has been sent to the administrator. You'll be contacted with a temporary
   password."* — always this message, whether or not the username exists, so the page can't be used
   to discover valid usernames.

**What the admin does**

4. Sees a badge on **User Management → Password Requests** with the pending count.
5. Clicks **Approve**. The system generates a **random one-time password** and shows it once, to be
   relayed the way that office already verifies identity — in person, or by phone to a known number.
6. The account is flagged **must change password at next login**.

**What the user does next**

7. Logs in with the temporary password, is sent straight to a **Change Password** screen, and cannot
   navigate anywhere else until it is changed.

**Why this shape:** steps 4–7 are mostly built. `ResetPasswordAsync`, `ChangePasswordAsync` and the
User Management page all exist; the new parts are one small table (`password_reset_requests`), one
`MustChangePassword` column, a guard in the portal layout, and a link on the login page. It fits an
organisation of ~52 people who share a building and already know each other.

**Later, if email ever arrives:** the same table becomes the token store and step 4–5 become an
emailed one-time link. Nothing designed here is thrown away.

**→ DECISION:** is admin-relay acceptable, or is emailed self-service a hard requirement for
v1.8.0? If the latter, treat the email provider as its own ticket with its own lead time — the DNS
side of a `.gov.ph` domain is not a same-day task.

---

## 3. Per-user Landing Page

> Requirement: settable landing page — Main Dashboard, Inventory Dashboard, Budget Planning
> Dashboard, User Profile. Default: Main Dashboard (PPDO), Budget Planning (non-PPDO).

Straightforward, with three traps worth designing around.

### 3.1 It must be validated against permissions, not just stored

Setting a user's landing page to Inventory Dashboard when they lack `CanAccessInventory` produces
either a 403 on login or a redirect loop. The office-user gate in `(portal)/layout.tsx:208-217`
already bounces office users away from everything outside Budget Planning, so an office user with
"Main Dashboard" saved would ping-pong.

**Suggestion:** store the preference, but resolve it through a fallback chain at login —
*preferred → first permitted from an ordered list → `/account`*. `/account` is the one page every
user can always reach, which is exactly why the existing office gate already falls back to it.
Also validate the choice in the user form so an admin cannot save an impossible combination.

### 3.2 Every redirect site has to agree

The landing target is currently hardcoded in several places, and they will drift apart the way
`APP_VERSION` did:

| Site | Today |
|---|---|
| `login/page.tsx:137` | `me.officeId != null ? "/budget-planning" : "/dashboard"` |
| `(portal)/layout.tsx:215` | office-user gate → `/budget-planning` or `/account` |
| `Sidebar.tsx:167` | logo link → `/dashboard` |
| `manifest.ts` | `start_url: "/dashboard"` |
| `/reconnecting` | `?next=` default `/dashboard` |

**Suggestion:** one `resolveLandingPath(me)` helper, used by all five.

### 3.3 It interacts with the PWA I just shipped

The manifest's `start_url` is a **single fixed value** for everyone — it cannot vary per user. Once
per-user landing pages exist, `start_url` should point at a neutral resolver (e.g. `/` or a small
`/home` page that redirects via `resolveLandingPath`) rather than `/dashboard`. One-line change,
but easy to forget.

---

## 4. Allocation Page — mostly already built

> Requirement: office ceilings set by PBO; general-fund allocation to PPDO divisions; program
> allocation; new PBO permission; rename the finance-officer permission.

**Most of this exists.** From v1.4.3:

| Requirement | Existing | Verdict |
|---|---|---|
| Ceiling per office, per fiscal year, per fund | `BudgetCeiling { OfficeId, FiscalYear, FundingSourceId, Amount }` | ✅ exists |
| Allocation to PPDO divisions | `DivisionAllocation { DivisionId, FiscalYear, FundingSourceId, Amount }` | ✅ exists |
| Program allocation | `ProgramDivision` + `PUT /allocation/programs?officeId=` | ✅ exists |
| Ceilings for offices **other than PPDO** | Endpoints already take `officeId` | ✅ endpoint exists; UI/permission is the gap |
| PBO sets them | Everything is gated on the single `CanManageAllocation` | ❌ **the actual work** |

So the ticket is **a permission split plus the office picker**, not a new feature:

- `CanManageAllocation` → rename to reflect PPDO-only scope (Ralph's own suggestion —
  "Manage PPDO Allocation (PPDO finance officer)"). Keeps divisions + programs for PPDO.
- New `OverrideCanManagePboCeiling` (or similar) → may set **`BudgetCeiling` for any office**.
  Mirrors `CanManageAllocation`'s plumbing exactly: `bool?` column, Admin **not** auto-granted.

⚠️ **Rename cost:** `CanManageAllocation` appears across the backend service, the Functions gates,
the user form, `MeResponse`, and the frontend. A rename is mechanical but wide — worth doing as its
own commit so it doesn't hide behind feature work in review.

### 4.1 What is genuinely new — and under-specified

**→ DECISION 1 — what enforces the ceiling, and against what?**
The draft says the division allocation *"will be used as ceiling of all divisions when creating
activities using general fund. Other fund source and Personal Services will have no ceiling."*

That rule mixes two different axes: **fund source** (General Fund) and **expense class** (PS). With
the new model (§5.1) a single expenditure line can be General Fund *and* split across PS/MOOE/CO —
so the check is *"sum only the non-PS portion of General-Fund lines"*, not "sum General-Fund lines".
Worth stating explicitly, because it is easy to implement the wrong one and the difference is
invisible until the numbers are audited.

**→ DECISION 2 — is the ceiling a block or a warning?** WFP has precedent for over-allocation
tracking; AIP should either hard-block the save, or allow it with a visible over-allocation state
that the reviewer sees. Blocking at *submit* rather than at *save* is usually kinder for a document
built over weeks — and it is essential if entry happens offline (§6), where a hard block can't be
evaluated at typing time anyway.

**→ DECISION 3 — must division allocations sum to ≤ the office ceiling?** And what happens if PBO
lowers an office ceiling *after* divisions have been allocated and activities encoded against them?

---

## 5. New AIP implementation

The bulk of the work. Ordered by how much of the design each point decides.

### 5.1 The expenditure model — the structural change

**Current storage.** `AipActivity` holds **one** `FundingSourceId` plus `Ps`, `Mooe`, `Co` columns
directly on the activity row.

**What the requirement (and whiteboard W7) describes:**

- an activity carries **account-code expenditure lines** (like WFP);
- an expenditure's total may be **split across PS / MOOE / CO** — one line, up to three classes;
- an activity may draw on **multiple fund sources**, with a toggle (default single, per W8).

None of that fits the current columns. The shape needed is a new child table, closely mirroring
`WfpExpenditure` (which already snapshots account number/title and funding source code — reuse that
pattern, including the snapshots, so historical AIPs survive config edits):

```
aip_expenditures
  activity_id, account_id + snapshots, funding_source_id + snapshot,
  ps, mooe, co, total (= ps + mooe + co)
```

`AipActivity.Ps/Mooe/Co/Total` then become **derived sums**, matching whiteboard W2's "cost auto
generated". Keeping them as stored, recomputed columns is probably wise — every report reads them.

**⚠️ Three consequences that need planning, not just coding:**

1. **`wfp_activities.aip_activity_id` is an FK-Restrict onto AIP activities.** WFPs already exist
   built on 2027 AIP activities. Restructuring AIP activities cannot orphan them.
2. **The 2027 AIP is live data** and was used for WFP. Whatever happens to the old shape, it has to
   keep reading correctly. **→ DECISION:** migrate 2027 into the new model, or keep both shapes with
   the new model applying only from 2028?
3. **The `.xlsm` upload path produces the old shape.** `AipXlsmParser` fills exactly the columns
   being replaced. **→ DECISION:** retire the upload for 2028+, or keep it for offices that still
   submit spreadsheets? (This is open Q4 in `AIP_Redesign_Notes.md` and still unanswered.)

**→ DECISION 4 — can a *single expenditure line* have multiple fund sources, or does multi-fund
mean multiple lines each with one fund?** The draft says *"multiple expenditure with different fund
source. but i am not sure if this is also true to 1 expenditure"*. The one-fund-per-line answer is
dramatically simpler (the table above works as written) and can express everything the other can.
I would strongly recommend it unless the PBO's own forms require otherwise.

### 5.2 Ownership, scoping and the FK that doesn't exist

Already documented in `Office_User_Path_Findings.md` §6.1 and unchanged: **`AipOffice` has no FK to
the offices config table** — office identity is ref-code string matching. The per-office
prepare-and-submit flow needs real ownership, so this redesign is where that FK is added.

Same problem in `ProgramDivision`, which keys on `OfficeRefCode` + `ProgramRefCode` **strings**.
Since program-to-division assignment is now load-bearing for PPDO visibility ("only programs related
to their division will be visible"), this string matching becomes a correctness risk, not just
untidiness. Worth converting to FKs in the same pass.

### 5.3 The review workflow is under-specified — the biggest documentation gap

The draft describes *"sent for review"* and *"once approved"*. The whiteboard adds **"Review
Comments"** (W3) and **"Locked"** (W4). Between them, the following is undefined:

| Question | Why it matters |
|---|---|
| **Can a reviewer reject / send back?** | W3's comments imply yes. If so: a Returned state, a comment thread, and a resubmit path. If no, "review" is just a submit gate and is far simpler. |
| **Is submitted work locked?** | W4 says locked. Locked against the encoder only, or the reviewer too? Can a reviewer edit, or only comment? §6.2 of the findings doc says reviewers are **read-only on content** — confirm that still holds. |
| **Comments at what level?** | Whole submission, per program/project/activity, or per expenditure line? Per-node is far more useful and materially more work. |
| **Can LFC return one office's work**, or only approve/return the consolidated whole? | Decides whether consolidation is a snapshot or a live view. |
| **Does PPDO consolidate before LFC?** | The draft says a PPDO reviewer approves PPDO's own divisions, and separately LFC reviews everything. So PPDO divisions → PPDO reviewer → consolidated → LFC. Confirm PPDO's internal step is a real gate, not just a view. |
| **Deadline / cutoff?** | Nothing in the draft. See §9.2. |

**Suggested state model** (extends `PlanningStatus`, currently just Draft/Final/Archived):

```
Draft ──submit──▶ SubmittedToOfficeReviewer ──approve──▶ OfficeApproved ──▶ (consolidated)
  ▲                          │                                                    │
  └──────── return ──────────┘                          LFCApproved ◀──approve── LFCReview
                                                             │
  ◀──────────────────── return to office ─────────────────────┘
```

`CalendarEvent` (RAL-82) is the in-repo precedent for reviewer columns — it stores `ReviewedById`
and `ReviewedAt`. A three-level flow needs those per transition, not just once.

### 5.4 Users and permissions

Draft lists five user kinds. Mapping to what exists:

| Draft role | Mechanism | Exists? |
|---|---|---|
| non-PPDO encoder | `CanAccessBudgetPlanning` + `OfficeScope` | ✅ (OfficeScope shipped in RAL-228) |
| non-PPDO reviewer | `OverrideCanReviewBudgetPlanning` | ❌ designed in findings §6.2, not built |
| PPDO user with division | `CanAccessBudgetPlanning` + `DivisionScope` | ✅ |
| PPDO reviewer (PPDC) | Same reviewer flag, PPDO office | ❌ |
| **LFC** | **New flag — spans PPDO and non-PPDO users, sees all offices** | ❌ new |

LFC is the interesting one: it is the **first permission that is explicitly cross-office**. Every
other flag narrows to the caller's own office. Its resolution must therefore bypass `OfficeScope`
rather than combine with it — worth calling out so it isn't accidentally built as
"reviewer + all offices", which would also grant reviewer's write-denial semantics.

⚠️ The reviewer flag carries a caveat already flagged in findings §6.2 and worth repeating: it is
the codebase's **first subtractive permission**. Every existing flag only ever *grants*. A reviewer
must be *denied* write on content while being the *only* one allowed to submit. That cannot be
expressed with the existing `ConfigHttp.AuthorizeAsync(req, _jwt, CanX, ct)` idiom and needs a
companion guard applied to every write endpoint.

### 5.5 Entry flow, programs and ref codes

**Programs come from a valid LDIP.** Reuses `seedAipProgramsFromLdip` (RAL-181) — good, that exists.

**→ DECISION 5 — what if a program isn't in the LDIP?** Can an office add an ad-hoc program, or is
the LDIP the closed universe? This decides whether program creation exists in the AIP UI at all.
(Related: the draft says the PPDO finance officer assigns programs to divisions — for PPDO. Who
assigns for non-PPDO offices, or is it all-programs-to-all-office-users?)

**Ref codes.** The draft correctly flags that correct AIP ref code generation is important. Two
things the draft doesn't cover:

- **Concurrency.** ~20 offices generating project/activity sequence numbers in the same window. The
  existing PR-number generator had exactly this class of bug (`GeneratePRNoAsync`, full-table scan
  per create — `Mobile_And_Inventory_Findings.md` §3.1). Don't repeat it: scope the sequence per
  office/program and compute it in SQL.
- **Offline.** A client working offline **cannot safely reserve a ref code** — two offline users
  would mint the same one. **Ref codes must be assigned server-side at upload**, and the offline UI
  should show a placeholder until then. This is a hard constraint on §6, and it is much cheaper to
  design in than to retrofit.

**Config-driven code lists.** eSRE (`SS`/`ES`/`ID`/`EN`) is currently a free string on
`AipActivity`; climate-change typology is a free string too (`CcTypologyCode`), with
`CcAdaptation`/`CcMitigation` amounts already present. The draft anticipates a config page for the
CC codes — **suggest doing both**: one small `climate_change_typologies` config table and one for
eSRE, following the existing config-page pattern. Free strings on a document that gets audited will
drift.

### 5.6 W1 — "Limit Dept Head, except GAD / 20% DF / PS / LDRRF / Trust Fund"

**→ DECISION 6.** My reading: department heads prepare their own office's AIP, **except** for those
five, which are prepared centrally (PPDO or PBO) because they are province-wide funds or
centrally-computed. If that is right it is a significant scoping rule — certain programs or fund
sources are simply not editable by an office user, even within their own office.

This is not in the written draft at all, and it changes the permission model (a per-fund-source or
per-program editability rule, on top of office/division scope). Worth confirming early: it is
cheap to design in and expensive to add later.

---

## 6. Offline data entry

Analysed at length in `../PWA_Feasibility_Study.md` §11; PWA Phase 1 (installable app, offline
shell) shipped 2026-08-14. What follows is only what the new requirements add or change.

### 6.1 Storage — recommend IndexedDB, not localStorage

The draft suggests localStorage. For this data I'd advise against it: an office's AIP subtree
(programs → projects → activities → expenditure lines) is deep, and localStorage is a **synchronous,
~5 MB, string-only** store — large writes block the UI thread mid-typing. **IndexedDB** is the right
tool: asynchronous, structured, far larger quota. The existing WFP draft persistence
(`wfp/page.tsx:819`) is the right *idea* at a much smaller scale.

### 6.2 "Save the session so they don't need to login" — the part to be careful with

This is the study's §5 auth wall, and the requirement resolves it in the most permissive direction.
Worth being explicit about the trade:

- Today the access token is **in-memory only** and deliberately never persisted, so a stolen or
  shared laptop yields nothing. Persisting a session inverts that: **anyone who opens the browser is
  that user**, offline, with their office's budget data.
- **Suggested middle path:** persist enough to open the app into the user's **own local drafts**
  offline (a local profile marker, not a bearer token), but require a real login for anything that
  touches the server — including upload. Combine with an explicit **"Sign out & clear local work"**
  that wipes IndexedDB, and an automatic wipe after N days of no use.
- **→ DECISION 7:** is the offline device assumed to be a personal/shared laptop, or an
  office-issued machine? A "shared laptop" answer justifies the middle path; "office-issued only"
  makes full session persistence defensible. Either is fine — but it should be a decision, not a
  default.

### 6.3 On the stated justification — load

The draft gives the reason as high traffic: 8 PPDO + 44 non-PPDO users. Offered as a check, not an
objection: **52 users is a very small load.** Even if all 52 worked simultaneously, that is far
below what a single Azure Function instance handles.

If slowness is the real worry, the two actual bottlenecks are already documented and neither is
fixed by offline entry:

- **Azure SQL Basic tier — 5 DTU** (`CLAUDE.md`, switched 2026-08-12). Fine at today's near-idle
  load; it is the first thing to saturate under 52 concurrent users. Scaling it up for AIP season is
  a slider, not a project.
- **Functions Consumption cold start** (~10 min to zero, 5–20 s wake).

And note that offline entry moves work *later*, not away: 20 offices uploading full subtrees near a
deadline is a **more** concentrated load than the same typing spread over weeks, and consolidation
and reports stay entirely server-side.

**None of this argues against offline** — it is well justified by provincial connectivity, by
letting people work through a cold start, and by the PPDC asking for it. It argues only that the
DB tier should be reviewed for AIP season regardless, and that offline shouldn't be *scoped* as a
performance fix.

### 6.4 Offline items the draft doesn't cover

| # | Issue | Note |
|---|---|---|
| 1 | **Ref codes can't be minted offline** | §5.5 — assign at upload, show a placeholder before |
| 2 | **Validation is server-side today** | Study §11.2 ⑨: the client validates only "Name is required". Offline, a user could work for days and have the whole thing rejected at upload. Serve the rules as cached data instead of hand-copying them into TypeScript |
| 3 | **Two encoders, one office, both offline** | Merge policy. The one-encoder-per-office shape makes it rare, not impossible |
| 4 | **Ceiling checks need cached allocations** | §4.1 — a ceiling can't be evaluated offline without the numbers cached |
| 5 | **Reference data must be cached** | Accounts, price index, funding sources, offices, divisions, LDIP programs, eSRE + CC codes |
| 6 | **Upload rejected after days of work** | Must never lose the local draft. Prefer per-node errors over one failed request |
| 7 | **How long does local work live?** | Weeks (a budget season) means storage-eviction behaviour matters, especially on iOS |

---

## 7. Reports and inter-office data files

> Requirement: exports for PPDO, PBO (budget), PACCO (accounting), PTO (treasurer), GSO
> (procurement) — form to be discussed with each.

Three suggestions:

1. **Build one canonical dataset, then filter it.** One row per expenditure line (office, division,
   program, project, activity, account, fund source, PS/MOOE/CO, CC fields, eSRE, ref code) answers
   most of what any of the five will ask for. Five bespoke reports built from five conversations
   will drift; one dataset with five column selections will not.
2. **GSO may already be answered.** `docs/External_AIP_API_Contract.md` is a read-only partner API
   contract for GSO, sitting in the backlog. Before designing a GSO file export, check whether that
   contract supersedes it — a live API beats a file that goes stale the moment it's generated.
3. **Ask each office for a filled example of what they use today**, not a description. The WFP
   export learned this the hard way — the province's `WFP-NEW.xlsx` turned out to be a filled sample
   rather than a blank template (`v1.5` milestone notes).

**Missing from the draft (W5): the AIP document itself.** The requirements cover *data files for
other offices* but not the **official AIP form output** — the printable/exportable document the
province actually submits. WFP got an Excel export (v1.4.4) and PPMP got one (v1.5); AIP has none.
Plus the board's **"Project Profile"** output, which appears to be a separate per-project document.

I would rate the official AIP form as the **single largest missing deliverable** in the draft — it
is the reason the data is being captured at all.

---

## 8. "In Thousand Pesos" display and rounding

> Requirement: users enter `1,234,567.89`, it is stored as entered, and displays as thousands.
> Quoted rule: *"round up to the nearest thousand … it will be 1,234,000.00"*.

**The rule and the example contradict each other.** `1,234,567.89`:

| Interpretation | Result (pesos) | Displayed |
|---|---|---|
| Round **up** (ceiling) — as the rule says | 1,235,000 | `1,235` |
| Round **down** (floor/truncate) — as the example says | 1,234,000 | `1,234` |
| Round **half-up** (normal rounding) | 1,235,000 | `1,235` |

The whiteboard shows both `1,235` and `1,234 0000`, so the ambiguity is real and predates the
document. **→ DECISION 8** — and it should come from the PBO, since their forms are what the output
is checked against. (For what it's worth, "in thousand pesos" on Philippine budget forms is
conventionally normal rounding, i.e. `1,235` — but this is precisely the kind of thing to confirm
rather than assume.)

### 8.1 The harder question the draft doesn't reach

**Do rounded rows have to add up to the rounded total?** They cannot always do both:

| Approach | Consequence |
|---|---|
| Sum exact values, round only the total | The total is accurate, but a reader adding up the printed column gets a different number |
| Round each row, then sum the rounded | The printed column adds up, but the total differs from the true sum |

For a document reviewed line-by-line against PBO figures, "the column adds up" often matters more
than "the total is exact" — but that is a finance call, not a developer one. **→ DECISION 9.**

### 8.2 Implementation notes

- **Store exact, always.** Round only at the display/report boundary — never persist a rounded
  value, or the original is gone.
- **One shared formatter.** Add `formatThousands()` alongside the existing `formatMoney()` /
  `parseMoney()` in `frontend/src/lib/money.ts`, and a matching helper on the backend for exports so
  the UI and the Excel output can't disagree.
- **Label every rounded surface** with "(In Thousand Pesos)" — an unlabelled `1,235` next to an
  entry field showing `1,234,567.89` reads as a bug.
- **Never round the entry field.** Users type and verify the exact figure; rounding is a *view*.

---

## 9. Missing entirely — suggested additions

Neither the draft nor the whiteboard covers these. Ordered by how much I'd argue for them.

### 9.1 The official AIP form output — **strongly recommended**
See §7. The document the province submits. Precedents exist (v1.4.4 WFP, v1.5 PPMP), including the
hard-won rule: build the sheet programmatically from a documented style catalogue, don't clone rows
out of a reference workbook.

### 9.2 Submission deadline per fiscal year — **recommended**
AIP preparation runs to a calendar. Without a cutoff, "has everyone submitted?" is a manual chase.
A per-FY deadline plus a readiness view (which offices have submitted, which haven't) is small and
makes the reviewer's job possible. It also gives the ceiling check a natural hard-block point (§4.1).

### 9.3 Notifications — **recommended**
A reviewer has no way to learn that work is waiting; an encoder has no way to learn their work was
returned. With no email infrastructure (§2.1), the realistic v1.8.0 answer is **in-app**: a pending
count on the sidebar and a review queue page. Push notifications are PWA Phase 3 and need a backend
push service — out of scope now, but the in-app queue is a prerequisite for it either way.

### 9.4 Amendment / supplemental AIP — **flag now, build later**
AIPs change after approval (supplemental budgets, realignments). `RAL-78` already exists for
amendment/copy mechanics. Not needed for first entry, but the data model should not make it
impossible — specifically, don't treat LFC approval as terminal.

### 9.5 Carry-forward from the prior year — **worth confirming**
`RAL-180` (carry-forward) and `copyAipOfficeFromPriorYear` already exist. For 2028, starting from
2027's structure would save every office significant typing. Does the new flow keep this?

### 9.6 Completeness rules before submit — **recommended**
Define what "ready to submit" means: every activity has ≥1 expenditure, totals > 0, required CC/eSRE
fields present, ceiling respected. Surface it as a checklist on the office's AIP page. This is also
the natural home for offline validation (§6.4 #2).

### 9.7 Approval snapshot — **worth a decision**
When LFC approves, is the approved version preserved? If a returned-and-edited record overwrites in
place, "what was approved" becomes unanswerable. The audit log records changes but does not
reconstruct a document version.

### 9.8 Concurrent editing within an office — **small but real**
Two encoders in the same office, both online, editing the same activity. Nothing today prevents
last-write-wins. A per-record soft lock or a "changed by someone else" warning is inexpensive.

---

## 10. Decisions blocking ticket-writing

| # | Decision | §  | Blocks |
|---|---|---|---|
| 1 | Ceiling rule — General Fund minus PS, computed how? | 4.1 | Allocation + AIP validation |
| 2 | Ceiling: hard block or warning, at save or at submit? | 4.1 | Both, and offline |
| 3 | Must division allocations fit inside the office ceiling? | 4.1 | Allocation |
| 4 | Multi-fund: per expenditure line, or per activity? | 5.1 | **The schema** |
| 5 | Can offices add programs outside the LDIP? | 5.5 | Entry UI |
| 6 | W1 — who prepares GAD / 20% DF / PS / LDRRF / Trust Fund? | 5.6 | Permission model |
| 7 | Offline: personal/shared device, or office-issued? | 6.2 | Session persistence |
| 8 | Round up, down, or half-up to the nearest thousand? | 8 | Every report |
| 9 | Must printed rows add up to the printed total? | 8.1 | Every report |
| 10 | Reviewer: can they reject/return, and with comments at what level? | 5.3 | Workflow |
| 11 | 2027 AIP + `.xlsm` upload — migrate, keep, or retire? | 5.1 | Migration |
| 12 | Password reset — admin relay, or is email mandatory? | 2.3 | Reset flow |

Decisions **4, 6, 10 and 11** are the ones that change the data model rather than the UI. If only a
few can be settled before work starts, settle those.

---

*Review only — no implementation. Companion to `AIP_Redesign_Notes.md`, which remains the record of
Ralph's original description.*
