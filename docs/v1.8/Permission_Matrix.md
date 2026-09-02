# Permission Matrix

> **V18-06 / RAL-245.** The single reference for *who can do what* in the PPDO Portal.
>
> Every row here is pinned by a test in `backend/PPDO.Tests/Application/PermissionMatrixTests.cs`.
> The two are a pair: change a rule and the corresponding row fails until both are updated. A flag
> added to `IPermissionService` without a row fails the build (`Matrix_CoversEveryFlagOnThePermissionService`).
>
> **Read this instead of `PermissionService`.** The model now carries 13 flags across three
> mechanisms plus three scope dimensions and one subtractive guard — past the point where "read the
> code" is a reasonable answer.

---

## 1. The three mechanisms

Permissions resolve through exactly one of two chains, never a mix:

| Chain | Flags | Rule |
|---|---|---|
| **Standard** | 7 feature flags | `SuperAdmin/Admin → true`, else `Override ?? Division.<flag> ?? false` |
| **Per-user grant** | 4 budget-planning authorities | `SuperAdmin → true`, else `Override ?? false` — **Admin is NOT auto-granted** |

Plus two flags that follow neither: `CanAccessProfile` (always true) and `CanViewAuditLog`
(feature-flag gated, SuperAdmin-only).

**Why per-user grants exclude Admin.** These four name a *specific person's job* — the PPDO finance
officer, the PBO finance officer, an office's reviewer, the consolidated reviewer. Auto-granting
them to every Admin would make the designation meaningless. SuperAdmin still resolves true so
support access always works, and that exemption is load-bearing — see §5.

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

`CanManagePpdoAllocation` · `CanManagePboCeiling` · `CanReviewBudgetPlanning` · `CanReviewAllOffices`

| Role | Override | Result |
|---|---|---|
| SuperAdmin | — | ✅ |
| Admin | `null` | ❌ **not auto-granted** |
| Admin | `true` | ✅ |
| Staff | `null` | ❌ |
| Staff | `true` | ✅ |
| Staff | `false` | ❌ |

Neither office nor division is read. A guest-office user holding the override resolves ✅.

**None of the four implies any other.** Mutual independence is pinned by test in both directions
for each pair that could plausibly be conflated:

| Flag | Who holds it | Added |
|---|---|---|
| `CanManagePpdoAllocation` | PPDO finance officer — splits PPDO's own ceiling across its divisions | RAL-97, renamed RAL-242 |
| `CanManagePboCeiling` | PBO finance officer — sets the ceiling **for any office** | RAL-243 |
| `CanReviewBudgetPlanning` | An office's reviewer — the department head who checks its work | RAL-244 |
| `CanReviewAllOffices` | Designated PPDO users who review **every** office's submissions | RAL-257 |

> ⚠️ `CanManagePboCeilingAsync` deliberately does **not** fall back to `CanManagePpdoAllocationAsync`.
> OR-ing them would hand every PPDO finance officer authority over other offices' ceilings.

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
> inversion** (RAL-250's own description among them) — they are stale. A user with a null office has
> an incomplete record, not a privileged one.

**Office wins over role.** The SuperAdmin/Admin bypass governs *feature flags*, not *data scope*. An
admin account deliberately tied to a guest office stays scoped to that office.

**Failure direction.** Both resolvers read `User.Office` / `User.Division` navigation properties. A
query that forgets `.Include(...)` degrades to **more** restrictive, never to full access.

### 3.1 `BudgetPlanningScope` — division is a PPDO-only axis (RAL-250)

| Caller | Office axis | Division axis |
|---|---|---|
| Host office (PPDO) | `SeeAll` | resolved normally — PPDO separates AIP/WFP work by division |
| Guest office | their own office | **`All` — division does not narrow** |
| No office | `NoOffice` (matches nothing) | irrelevant |

> ⚠️ **Consume both axes together.** For a guest-office caller the division axis reads "every
> division", which is only safe because the office axis pins them to one office in the same query.

---

## 4. The cross-office exceptions (RAL-257, RAL-243)

Two flags widen data scope past the caller's own office. Every other flag narrows to it.

| Flag | Widens what | Entry point | Added |
|---|---|---|---|
| `CanReviewAllOffices` | every office's submissions, **read only** | `OfficeScope.ResolveForReview` | RAL-257 |
| `CanManagePboCeiling` | every office's allocation setup — the six allocation reads **and** the ceiling write | `OfficeScope.ResolveForCeiling` | RAL-243, scoped by PPDO-18 |

Each is consumed through its **own entry point**, and that separation is the safety property:

```
OfficeScope.ResolveForReview(user, canReviewAllOffices)    review READ paths only
OfficeScope.ResolveForCeiling(user, canManagePboCeiling)   allocation reads + the ceiling PUT
OfficeScope.Resolve(user)                                  everything else, including every other write
```

`Resolve` feeds the write paths through `Clamp`. Teaching it either flag would silently promote a
cross-office *reviewer* into a cross-office **editor** of every office's data, or a PBO ceiling
officer into an editor of every office's internal division split — with no diff at any write site to
notice it. Pinned by `Resolve_IgnoresTheCrossOfficeGrant_SoWritePathsStayScoped` and
`Resolve_IgnoresThePboCeilingGrant_SoAllocationWritesStayScoped`.

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

## 5. The subtractive exception (RAL-256)

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

*Permission Matrix — v1.8.0 — RAL-245 — 2026-08-28*
