# Permission Matrix

> **V18-06 / PPDO-7.** The single reference for *who can do what* in the PPDO Portal.
>
> Every row here is pinned by a test in `backend/PPDO.Tests/Application/PermissionMatrixTests.cs`.
> The two are a pair: change a rule and the corresponding row fails until both are updated. A flag
> added to `IPermissionService` without a row fails the build (`Matrix_CoversEveryFlagOnThePermissionService`).
>
> **Read this instead of `PermissionService`.** The model now carries 14 flags across three
> mechanisms plus three scope dimensions and one subtractive guard — past the point where "read the
> code" is a reasonable answer.

---

## 1. The three mechanisms

Permissions resolve through exactly one of two chains, never a mix:

| Chain | Flags | Rule |
|---|---|---|
| **Standard** | 7 feature flags | `SuperAdmin/Admin → true`, else `Override ?? Division.<flag> ?? false` |
| **Per-user grant** | 5 budget-planning + API-access authorities | `SuperAdmin → true`, else `Override ?? false` — **Admin is NOT auto-granted** |

Plus two flags that follow neither: `CanAccessProfile` (always true) and `CanViewAuditLog`
(feature-flag gated, SuperAdmin-only).

**Why per-user grants exclude Admin.** These five name a *specific person's job* — the PPDO finance
officer, the PBO finance officer, an office's reviewer, the consolidated reviewer, the person who
issues partner API keys. Auto-granting them to every Admin would make the designation meaningless.
SuperAdmin still resolves true so support access always works, and that exemption is load-bearing
— see §5.

**Division flags exist for the standard chain only.** `Division` carries seven `Can*` booleans and
deliberately carries none of the per-user grants.

---

## 2. The matrix

`—` means the input is not read at all for that flag.

### 2.1 Standard flags

`CanAccessInventory` · `CanAccessReports` · `CanManageUsers` · `CanManageResourceLinks` · `CanManageConfig`

| Role | Override | Division flag | Result |
|---|---|---|---|
| SuperAdmin | — | — | ✅ |
| Admin | — | — | ✅ |
| Staff | `null` | `false` | ❌ |
| Staff | `null` | `true` | ✅ |
| Staff | `true` | `false` | ✅ |
| Staff | `false` | `true` | ❌ |

Office is not read by any of these five.

> **`CanManageResourceLinks` carries an extra rule the flag cannot express.** Staff who hold it may
> **add only** — edit and delete always require Admin/SuperAdmin regardless of the flag. That is
> enforced at the endpoint, not in `PermissionService`.

### 2.2 `CanAccessBudgetPlanning` — the one flag whose default flips by office

| Role | Office | Override | Division flag | Result |
|---|---|---|---|---|
| SuperAdmin | any | — | — | ✅ |
| Admin | any | — | — | ✅ |
| Staff | host | `null` | `false` | ❌ |
| Staff | host | `null` | `true` | ✅ |
| Staff | host | `true` | `false` | ✅ |
| Staff | host | `false` | `true` | ❌ |
| Staff | **guest** | `null` | — | ✅ **defaults ON** |
| Staff | **guest** | `true` | — | ✅ |
| Staff | **guest** | `false` | — | ❌ |

A guest-office user has no division to inherit from and Budget Planning is their only feature, so a
blank override means granted. An explicit `false` still turns it off.

### 2.3 `CanUploadAip` — host-office only, never grantable to a guest

| Role | Office | Override | Division flag | Result |
|---|---|---|---|---|
| SuperAdmin | any | — | — | ✅ |
| Admin | any | — | — | ✅ |
| Staff | host | `null` | `false` | ❌ |
| Staff | host | `null` | `true` | ✅ |
| Staff | host | `true` | `false` | ✅ |
| Staff | host | `false` | `true` | ❌ |
| Staff | **guest** | `true` | `true` | ❌ **never, however set** |

The uploaded file contains *every* office's records, so upload is host-office-only by construction.

### 2.4 Per-user grants

`CanManagePpdoAllocation` · `CanManageOfficeCeilings` · `CanReviewBudgetPlanning` · `CanReviewAllOffices` ·
`CanManageApiKeys`

| Role | Override | Result |
|---|---|---|
| SuperAdmin | — | ✅ |
| Admin | `null` | ❌ **not auto-granted** |
| Admin | `true` | ✅ |
| Staff | `null` | ❌ |
| Staff | `true` | ✅ |
| Staff | `false` | ❌ |

Neither office nor division is read. A guest-office user holding the override resolves ✅.

**None of the six implies any other.** Mutual independence is pinned by test in both directions
for each pair that could plausibly be conflated:

| Flag | Who holds it | Added |
|---|---|---|
| `CanManagePpdoAllocation` | PPDO finance officer — splits PPDO's own ceiling across its divisions | RAL-97, renamed PPDO-9 |
| `CanManageOfficeCeilings` | PPDO finance user — sets the ceiling **for any office** (PBO until 2026-09-15; renamed from `CanManagePboCeiling`, PPDO-87 — column still `OverrideCanManagePboCeiling`) | PPDO-2 |
| `CanReviewBudgetPlanning` | An office's reviewer — the department head who checks its work | PPDO-3 |
| `CanReviewAllOffices` | Designated PPDO users who review **every** office's submissions | PPDO-5 |
| `CanManageApiKeys` | Named person who issues/revokes partner API keys under Configuration → API Access | PPDO-15 |
| `CanManageOfficeSetup` | A department head who sets up **their own office**: its division split, programme → division assignment, divisions, fund sources, and (PPDO-135) which division each of their own Staff belongs to | PPDO-107, PPDO-135 |

> ⚠️ `CanManageOfficeCeilingsAsync` deliberately does **not** fall back to `CanManagePpdoAllocationAsync`.
> OR-ing them would hand every PPDO finance officer authority over other offices' ceilings.
>
> ⚠️ `CanManageOfficeSetupAsync` is **not** a narrowed `CanManagePpdoAllocation`, and must never be
> implemented as one. That flag is host-office-exclusive (§4a below) precisely because a guest-office
> account once held it; this one exists so an office can configure **itself** without that rule being
> relaxed. It also does not follow from either reviewer flag — reviewing an office's work and
> configuring that office are different jobs, and one person holding both is a grant, not an inference.
>
> ⚠️ `CanManageApiKeysAsync` is independent of `CanManageConfigAsync`, `CanManageUsersAsync` and
> `CanReviewAllOfficesAsync` in both directions — none of them implies it, and it implies none of
> them. A key reads AIP data across every office it is scoped to, from outside the portal entirely,
> so it stays scoped to a named person rather than inheriting any existing role default.

### 2.5 The two that follow neither chain

| Flag | Rule |
|---|---|
| `CanAccessProfile` | Always `true`, every role, every office. |
| `CanViewAuditLog` | `FeatureFlags.AuditLogPageEnabled && Role == SuperAdmin`. No override, no division flag. |

---

## 3. Scope dimensions

Permissions answer *"may this caller use this feature?"*. Scope answers *"over which rows?"*. They
are independent — a caller can hold a flag and still see nothing.

| Resolver | Axis | `null` means | Widest state |
|---|---|---|---|
| `OfficeScope` | office | **unassigned → sees nothing** (`NoOffice`, id 0) | host-office user → `SeeAll` |
| `DivisionScope` | division | **unassigned → sees nothing** (`Nothing`) | SuperAdmin/Admin → `All` |
| `BudgetPlanningScope` | both | — composes the two | — |

> ⚠️ **A correction worth knowing about.** Until DECISION F (RAL-258) a null `users.office_id`
> positively meant *"PPDO-internal, sees everything"* — the **inverse** of the division rule. Two
> mechanisms described PPDO and nothing kept them in agreement. Cross-office authority now comes
> from `offices.is_host_office`, which frees null to mean what it means on the other axis:
> unassigned, and therefore scoped to nothing. **Several tickets and comments still assert the old
> inversion** (PPDO-4's own description among them) — they are stale. A user with a null office has
> an incomplete record, not a privileged one.

**Office wins over role.** The SuperAdmin/Admin bypass governs *feature flags*, not *data scope*. An
admin account deliberately tied to a guest office stays scoped to that office.

**Failure direction.** Both resolvers read `User.Office` / `User.Division` navigation properties. A
query that forgets `.Include(...)` degrades to **more** restrictive, never to full access.

### 3.1 `BudgetPlanningScope` — division is a PPDO-only axis (PPDO-4)

| Caller | Office axis | Division axis |
|---|---|---|
| Host office (PPDO) | `SeeAll` | resolved normally — PPDO separates AIP/WFP work by division |
| Guest office | their own office | **`All` — division does not narrow** |
| No office | `NoOffice` (matches nothing) | irrelevant |

> ⚠️ **Consume both axes together.** For a guest-office caller the division axis reads "every
> division", which is only safe because the office axis pins them to one office in the same query.

---

## 4. The cross-office exceptions (PPDO-5, PPDO-2)

Two flags widen data scope past the caller's own office. Every other flag narrows to it —
`CanManageOfficeSetup` (PPDO-107) narrows hardest of all: it grants **only** the caller's own
office, compared at the endpoint against the office the request targets.

| Flag | Widens what | Entry point | Added |
|---|---|---|---|
| `CanReviewAllOffices` | every office's submissions, **read only** | `OfficeScope.ResolveForReview` | PPDO-5 |
| `CanManageOfficeCeilings` | every office's allocation setup — the six allocation reads **and** the ceiling write | `OfficeScope.ResolveForCeiling` | PPDO-2, scoped by PPDO-18 |

Each is consumed through its **own entry point**, and that separation is the safety property:

```
OfficeScope.ResolveForReview(user, canReviewAllOffices)    review READ paths only
OfficeScope.ResolveForCeiling(user, canManageOfficeCeilings)   allocation reads + the ceiling PUT
OfficeScope.Resolve(user)                                  everything else, including every other write
```

### 4a. The Annex B report read — scoped without `OfficeScope` at all (PPDO-90)

`GET …/aip/consolidated` and `…/consolidated/export` admit **either** reviewer flag, and the service
resolves the scope:

| Caller | Offices | Workflow states | `officeId` param |
|---|---|---|---|
| `CanReviewAllOffices` | every office (consolidated), or the one named | with PPDO only (`SubmittedToPpdo`, `Consolidated`) | honoured |
| `CanReviewBudgetPlanning` only (department head) | **their own, pinned to `users.office_id`** | **any state, including Draft** | **ignored** (clamped, not refused) |
| neither | — | — | 403 |

⚠️ **The department head is pinned to `users.office_id` and NOT resolved through
`OfficeScope.Resolve`.** A department head who sits in the **host office (PPDO)** resolves to
`SeeAll` there, which would hand them every office in the province through a read that is meant to be
their own office only. This is the same trap as tracker B4, and it is why this row exists in the
matrix rather than the scoping being left implicit. Pinned by
`GetSheetAsync_HostOfficeDeptHead_IsPinnedToTheirOwnOfficeNotEveryOffice`.

Holding **both** flags gives cross-office behaviour, so a PPDO reviewer who also heads a division
keeps the consolidated view rather than being narrowed to one office.

No new flag, and no migration — this widens an existing read.

`Resolve` feeds the write paths through `Clamp`. Teaching it either flag would silently promote a
cross-office *reviewer* into a cross-office **editor** of every office's data, or a PBO ceiling
officer into an editor of every office's internal division split — with no diff at any write site to
notice it. Pinned by `Resolve_IgnoresTheCrossOfficeGrant_SoWritePathsStayScoped` and
`Resolve_IgnoresTheOfficeCeilingsGrant_SoAllocationWritesStayScoped`.

**The two do not substitute for each other.** Reusing `ResolveForReview` for the ceiling grant would
hand a comment-only reviewer a write; reusing `ResolveForCeiling` for review would hand a ceiling
officer the review scope. Pinned by `TheTwoBypasses_DoNotLeakIntoEachOther`.

A holder's own office is **ignored, not combined** — a reviewer sitting in GSO reviews every office,
not GSO's rows plus everyone else's.

> **Where the ceiling grant stops.** It is authority over an office's ceiling, not over what that
> office does with it. `PUT /allocation/divisions` (the division split) and
> `PUT /allocation/programs` (PPA assignment) stay on `Resolve` and are **host-office only** — see
> the note below. `PUT /allocation/ceiling` is the one write the grant covers, and it carries no
> office guard at all: the gate *is* the grant.
>
> ↩️ **Since PPDO-107 those two writes admit a second caller**, and only that one: a holder of
> `CanManageOfficeSetup` whose own office id equals the office the request targets. The PPDO path
> is unchanged and still demands host office, so the note below stands exactly as written — the
> department head arrives through a different door, not through a widened one.
>
> The programme write carries an AIP office **ref code** rather than an office id, so the endpoint
> resolves the code to its owning office before comparing (`ResolveOfficeIdForAipRefCodeAsync`);
> an unresolvable code is a 403, not a NotFound, so the denial never confirms which codes exist.
>
> ⚠️ **One state gate, on the division split only** (`Office_Setup_Spec.md` D10): a department head
> may write while their office holds at least one AIP group in an office-editable state, else
> **409**. PPDO is never state-gated — it sets offices up across the whole cycle. The programme
> write has no fiscal year at all (assignments are permanent across years), so no state can gate it.

### `CanManageOfficeSetup`'s third door — assigning divisions to a Staff member (PPDO-135)

`GET /api/office/users` and `PUT /api/office/users/{id}/division` are gated on
`CanManageOfficeSetup` **alone**, never OR'd with `CanManageUsers`. That is a deliberate,
narrower door than User Management's own — see the ticket's own warning:

> Granting the existing flag would be a privilege escalation, not a shortcut. `GET /api/users`
> has no office axis and the write guard (`CanRequesterManageTarget`) checks **role only**, so a
> department head holding `CanManageUsers` could list and edit Staff in **every** office
> province-wide, including PPDO's.

So this is a separate, deliberately smaller pair of endpoints rather than a widened
`CanManageUsers`:

| What it can do | What it cannot do |
|---|---|
| List the Staff in the caller's OWN office (slim `OfficeUserDto` — no email, no override flags) | See or touch a user in any other office |
| Set or clear one Staff member's division | Create a user, reset a password, change a role, or touch any permission override |

Both rules live in `UserService.SetOfficeUserDivisionAsync`, not in the Function handler or the
UI: the target's `OfficeId` must equal the requester's own (an OFFICE comparison — deliberately
**not** `CanRequesterManageTarget`'s role-only check, which would let this leak exactly the way
the ticket warned about), and a non-null division id must belong to that same office
(`ValidateDivisionAsync`'s existing `requireOfficeId` guard, reused rather than re-derived). A
target who is SuperAdmin/Admin is refused — those roles carry no division. Pinned by
`UserServiceTests.SetOfficeUserDivisionAsync_TargetInAnotherOffice_ReturnsForbidden` and its
siblings, red-tested against the office comparison specifically (this project has shipped three
cross-office leaks already: RAL-229, PPDO-18, PPDO-30).

Refuses rather than clamps, same reasoning as `AllocationFunctions`: silently reassigning a
user's division to keep the request "working" is a worse failure than a 403.

### `CanManagePpdoAllocation` is exclusive to host-office users

Settled 2026-09-02, after a live account — `pto.user`, Provincial Treasurer's Office — was found
holding the flag by mistake. The flag's name and this table always said "PPDO", but nothing
enforced it, and the Allocation page duly offered that account a division-allocation tab for its
own office.

Both endpoints on the flag now refuse a guest-office caller **outright**, for their own office as
well as a foreign one. Enforcing "PPDO only" rather than merely "not someone else's office" means
the endpoint stops depending on the grant being administered correctly, which is the thing that
actually went wrong. A host-office caller still writes any office — that is how PPDO sets other
offices up.

> ⚠️ The flag is *not* office-scoped-per-caller. If a future office genuinely needs to split its
> own ceiling across its own divisions, that is a **new** grant, not a widening of this one —
> widening it would silently re-open what this note closed. Pinned by
> `AllocationFunctionsTests.UpsertDivisions_AsOfficeUser_TargetingOwnOffice_IsAlsoForbidden`.

---

## 5. The subtractive exception (PPDO-6)

Every flag above is **additive** — it only ever grants. `ReviewerWriteGuard` is the one rule that
takes a write away, and it is applied to all 40 budget-planning write endpoints via
`ConfigHttp.AuthorizeWriteAsync`.

> ⚠️ **The rule is not "reviewers cannot write."** There are two reviewer kinds and they differ on
> exactly this point (tracker B11):

| Reviewer | Flag | May edit content? |
|---|---|---|
| Department head (office) | `CanReviewBudgetPlanning` | **Yes** — updates minor details found while checking |
| PPDO consolidated | `CanReviewAllOffices` | **No** — comment only. **This is what the guard keys on.** |

Denying the department head would freeze them out of the edits the review exists to make.

### SuperAdmin is exempt, deliberately

`CanReviewAllOfficesAsync` resolves **true** for SuperAdmin — as every flag does, so support access
always works. A guard that simply asked *"is this a cross-office reviewer?"* would **lock SuperAdmin
out of every write in budget planning.** The blanket bypass exists to *grant* access, never to
impose a restriction. Pinned by `DeniesWriteAsync_SuperAdmin_IsNeverDenied`.

### What the guard must never cover

**Submit, return, and comment are the reviewer's own actions.** When Phase 4 adds them they must not
be routed through this guard — a comment-only reviewer who cannot comment is not a reviewer.

**The office ceiling save (`PUT /allocation/ceiling`) is exempt too** (PPDO-87, 2026-09-15). Ceiling
authority moved from PBO to PPDO finance users, and those same users are the cross-office reviewer, so
the guard would refuse the very people the grant is for. It is the one exempt write that changes a
figure — it qualifies because a ceiling is PPDO's own top-down number, not an office's plan content,
which is all the guard protects. It stays gated on `CanManageOfficeCeilings`; the reviewer flag alone
never reaches it. Pinned by `ReviewerWriteGuardCoverageTests.UpsertCeiling_LetsACrossOfficeReviewerThrough`
and `AllocationFunctionsTests.UpsertCeiling_AsCrossOfficeReviewerWithoutTheCeilingGrant_ReturnsForbidden`.

> Finance users who also encode for their own division use a **separate encoder account** — the guard
> is not narrowed to "own office" (recommended 2026-09-15, to confirm with finance).

---

## 6. Where each rule lives

| Concern | File |
|---|---|
| Flag resolution | `PPDO.Application/Services/PermissionService.cs` |
| Flag contracts + per-flag notes | `PPDO.Domain/Interfaces/IPermissionService.cs` |
| Per-user override storage | `PPDO.Domain/Entities/User.cs` |
| Division defaults | `PPDO.Domain/Entities/Division.cs` |
| Office scope + cross-office read bypass | `PPDO.Application/Common/OfficeScope.cs` |
| Division scope | `PPDO.Application/Common/DivisionScope.cs` |
| Combined budget-planning scope | `PPDO.Application/Common/BudgetPlanningScope.cs` |
| Reviewer write denial | `PPDO.Application/Common/ReviewerWriteGuard.cs` |
| Endpoint wiring | `PPDO.Functions/Functions/ConfigHttp.cs` |
| **This matrix, as tests** | `PPDO.Tests/Application/PermissionMatrixTests.cs` |
| Endpoint coverage of the guard | `PPDO.Tests/Functions/ReviewerWriteGuardCoverageTests.cs` |

**Never inline permission resolution.** Always call `PermissionService`.

---

*Permission Matrix — v1.8.0 — PPDO-7 — 2026-08-28*
