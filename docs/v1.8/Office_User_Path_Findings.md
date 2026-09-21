# Office-User Path — Codebase Impact Assessment

> Assessment date: 2026-08-13 · Branch: `release/1.7.2`
> Scope: what already exists, and what breaks, when non-PPDO ("office") users are allowed to log in.
> Status: **findings only — nothing implemented.** No ticket numbers assigned yet.

---

## 1. Executive summary

The office-user concept is **already modelled end-to-end** — `User.OfficeId` is a documented
PPDO/non-PPDO discriminator, account creation supports it, permissions resolve for it, and the
sidebar already hides PPDO-internal features from it. This is not greenfield work.

What is missing is **data isolation**. Exactly one budget-planning feature (LDIP, via RAL-61)
actually enforces office ownership. AIP, WFP, Allocation, the Budget Planning dashboard, and the
WFP/PPMP reports do not. Because office users get `CanAccessBudgetPlanning` **ON by default**
([PermissionService.cs:71-72](../../backend/PPDO.Application/Services/PermissionService.cs)), every
one of those endpoints becomes reachable by any office user the moment such an account exists.

**Headline risk:** `DELETE /api/budget-planning/aip/{id}` is gated only on
`CanAccessBudgetPlanning`. An office user could delete PPDO's entire fiscal-year AIP record.

The work is therefore best framed as **"add office isolation to Budget Planning"**, not
"add office users".

---

## 2. Already built — no work required

| Area | Where | State |
|---|---|---|
| PPDO/non-PPDO discriminator | `User.OfficeId` ([User.cs:41-47](../../backend/PPDO.Domain/Entities/User.cs)) | Documented: `null` → PPDO-internal, set → office user |
| Account creation / edit | `UserService.CreateAsync` / `UpdateAsync` (lines 88-153, 243-287) | Validates the office, makes division optional for office users, enforces division-belongs-to-office |
| Permission resolution | [`PermissionService`](../../backend/PPDO.Application/Services/PermissionService.cs) | `CanAccessBudgetPlanning` defaults ON for office users; `CanUploadAip` hard-denied |
| Sidebar navigation | `Sidebar.tsx:115-128` | `isOfficeUser` hides Dashboard, Inventory, Resource Links, Config, User Management, Announcements |
| Budget Planning page shell | `budget-planning/page.tsx:443-513` | `isPpdo` branch, office-locked `effectiveOfficeId`, redirect-loop-safe `/account` fallback |
| **LDIP office isolation** | [`LdipFunctions.cs`](../../backend/PPDO.Functions/Functions/LdipFunctions.cs) (RAL-61) | **Complete.** This is the reference pattern — see §4 |

---

## 3. Gaps, by severity

### 3.1 — AIP has no office scoping at any layer 🔴 Critical

`AipFunctions.cs` contains **zero** `OfficeId` references. Every one of its ~30 endpoints is
guarded only by `ConfigHttp.AuthorizeAsync(req, _jwt, CanAccess, ct)` — no ownership check.

The service interface has no office dimension to scope by even if the Functions layer wanted to:

```csharp
// IAipService.cs:14 — no office parameter, no caller
Task<IReadOnlyList<AipRecordDto>> GetAllAsync(int? fiscalYear, string? status, CancellationToken ct = default);
```

Mutations take bare entity IDs with no ownership resolution:

| Endpoint | Guard | Effect if called by an office user |
|---|---|---|
| `DELETE /aip/{id}` | `CanAccessBudgetPlanning` | Deletes the whole FY AIP record |
| `DELETE /aip/offices/{officeId}` | `CanAccessBudgetPlanning` | Deletes another office's entire subtree |
| `DELETE /aip/programs/{programId}` | `CanAccessBudgetPlanning` | Deletes another office's program |
| `PUT /aip/{id}/activities/{activityId}` | `CanAccessBudgetPlanning` | Edits another office's line item |
| `POST /aip/{aipId}/offices` | `CanAccessBudgetPlanning` | Adds an office to any AIP |
| `POST /aip/copy-office` | `CanAccessBudgetPlanning` | Copies from any office, any prior FY |
| `GET /aip` | `CanAccessBudgetPlanning` | Lists every office's AIP records |

Only Upload/Confirm require `CanUploadAip` (which office users can never hold) — so the
*import* path is safe, and everything RAL-62/179/180/181 added in v1.6 is not.

**⚠️ This is structurally harder than LDIP.** An `AipRecord` is *inherently multi-office*: one
fiscal-year document containing an `AipOffice` per office. LDIP could scope on
`LdipRecord.OfficeId` because an LDIP record usually belongs to one office. AIP needs **sub-tree
filtering inside a shared document**, which raises product questions that have no code answer:

- What does `AipRecord.Status` (Draft/Final/Archived) mean when office A has finished and office
  B has not? Finalize is currently record-wide.
- Can an office user create an `AipRecord`, or only populate their `AipOffice` inside a
  PPDO-created one?
- Does an office user see the record's PPDO-wide totals, or only their own subtree's?

These need answering before AIP office-scoping can be ticketed. **They also overlap heavily with
the AIP-redesign track** — if AIP is being reworked to be LDIP-based and WFP-like anyway, the
ownership model should be designed as part of that redesign rather than retrofitted first.

### 3.2 — Budget Planning dashboard is hardcoded to PPDO, and office users still call it 🔴 Critical

`BudgetPlanningDashboardService` resolves the office internally and permanently:

```csharp
private const string PpdoOfficeCode = "PPDO";                                    // line 24
Office ppdo = await _officeRepo.GetByCodeAsync(PpdoOfficeCode, ct)               // line 76
    ?? throw new InvalidOperationException($"Office '{PpdoOfficeCode}' is not seeded.");
```

The frontend calls it **unconditionally** — the load effect has no `isPpdo` guard:

```tsx
// budget-planning/page.tsx:483-485
useEffect(() => {
  loadDashboard();          // → GET /budget-planning/dashboard  (always PPDO)
}, [loadDashboard]);
```

The JSX gates *rendering* behind `isPpdo` (lines 621, 640), but the response — PPDO's budget
ceilings, per-division allocations, and per-division WFP status — is fetched into the office
user's browser regardless, and `fiscalYear` is seeded from it (line 473). Visible in devtools.

### 3.3 — `GET /budget-planning/dashboard/office` has no caller clamp 🔴 Critical

[BudgetPlanningDashboardFunctions.cs:129](../../backend/PPDO.Functions/Functions/BudgetPlanningDashboardFunctions.cs)
reads `officeId` straight from the query string after only a `CanAccessBudgetPlanning` check:

```csharp
if (!int.TryParse(req.Query["officeId"], out int officeId) || ...)
OfficeDashboardDto result = await _service.GetOfficeDashboardAsync(officeId, fiscalYear, ct);
```

Any budget-planning user can read any office's dashboard by changing one query parameter. A
textbook IDOR — and unlike §3.2 this one leaks in *both* directions between offices.

### 3.4 — WFP trusts the request body's `OfficeId` 🟠 High

`WfpFunctions.cs` has a single `OfficeId` reference and it is a pass-through
(`body.OfficeId` → `EnsureActivityAsync`, line 134). `WfpService.SaveAsync` likewise takes
`dto.OfficeId` (lines 102-104, 207) with no caller comparison. `IWfpService.GetAllAsync` has no
caller parameter. Same shape of exposure as AIP, smaller surface.

### 3.5 — WFP/PPMP reports clamp division but not office 🟠 High

`WfpReportFunctions` already has the RAL-136 division clamp — a good precedent, and worth
copying:

```csharp
// non-finance callers are forced to their own division; a passed divisionId is ignored
divisionId = caller.DivisionId;
```

But `officeId` on those same endpoints comes from the query string unclamped. The clamp reasoning
was applied one dimension short.

### 3.6 — No shared office-scope primitive 🟡 Medium

[`DivisionScope`](../../backend/PPDO.Application/Common/DivisionScope.cs) is the repo's proven
scope-resolution pattern (`All` / `Nothing` / `For(id)`, with an explicit warning that a null
division must mean *nothing*, never *everything*). There is no `OfficeScope` equivalent.

Office users have `DivisionId = null`, so `DivisionScope.Resolve` returns `Nothing` for them —
which is safe-by-accident for Inventory (they see no inventory data), but it means Budget
Planning has no primitive to mirror, and every fix in §3.1-3.5 would otherwise be hand-rolled
per endpoint.

---

## 4. The pattern to copy — LDIP (RAL-61)

`LdipFunctions.cs` already solves this cleanly, three ways:

**Read guard** — deny by ownership before acting:
```csharp
private async Task<HttpResponseData?> DenyForeignOfficeAsync(
    HttpRequestData req, User caller, int id, CancellationToken ct)
{
    if (caller.OfficeId is null) return null;   // PPDO — full access
    ...
    if (existing.IsSuccess && existing.Value!.OfficeId != caller.OfficeId)
        return /* 403 */;
}
```

**Clamp on write** — ignore whatever the body claims:
```csharp
// Office users always create for their own office, whatever the body says.
if (caller!.OfficeId is not null)
    body = body with { OfficeId = caller.OfficeId };
```

**Clamp on list** — the caller's office wins over any filter:
```csharp
int? officeId = caller!.OfficeId ?? /* PPDO may filter freely */;
```

Both clamp forms are strictly better than validate-and-reject: there is no error path to get
wrong, and no way for a client to probe for other offices' IDs.

---

## 5. Recommended shape of the work

1. **`OfficeScope` primitive** in `PPDO.Application/Common/`, mirroring `DivisionScope` —
   `All` (PPDO) / `For(officeId)` (office user). Plus a `ConfigHttp`-level clamp helper so §3.1-3.5
   are not re-implemented per endpoint.
2. **Fix the three 🔴 leaks first** — they are small, self-contained, and independent of any
   redesign: guard the office dashboard (§3.3), stop the unconditional PPDO dashboard fetch
   (§3.2), and decide the office-user dashboard story.
3. **Add the office dimension to the WFP/report clamps** (§3.4, §3.5) — mechanical, follows
   RAL-136 and LDIP directly.
4. **Fold AIP office-scoping, the reviewer flow, and the AIP redesign into one piece of work**
   (§3.1, §6) — confirmed by §6.1: AIP has no office-ownership FK at all, and the reviewer model
   inverts the data flow. Scoping it first would be work done twice.
5. **Do not create any office-user account in production** until at least step 2 lands. Today the
   permission model would grant that account destructive access to PPDO's AIP.

Steps 1-3 are safe to start now and are independent of every product answer still outstanding.
Step 4 is blocked on §6.4.

---

## 6. Office reviewers (confirmed 2026-08-13)

Ralph confirmed: `CanAccessBudgetPlanning` stays ON by default for office users, and each office
will additionally have **reviewer** users, defined as:

- ~~**read-only on content** — they view their office's work, they don't edit it;~~
  ⚠️ **Superseded 2026-08-25:** the department-head reviewer **may edit values** during review, and
  may comment instead of or as well as editing (tracker B1/B3). This matters beyond the AIP — the
  planned `DenyReviewerWriteAsync` guard (PPDO-6) was designed from the read-only reading;
- **the sole submit authority** — only a reviewer may send their office's work.
  ⚠️ **Refined 2026-08-25:** true for the hop **to PPDO**. There is an earlier hop the encoder makes
  themselves — encoder → department-head review — so "reviewer is the sole submitter" is not the
  whole flow (`Phase_Plan.md` §12.5);
- **submitted work feeds a consolidated document** at the PPDO level.

Scope for now is **AIP only**, but the backend permission must be written generically so LDIP and
WFP can reuse it unchanged when their gates are added.

### 6.1 — This inverts the current AIP data flow

Today: PPDO uploads **one file containing every office**, producing a single provincial
`AipRecord` per fiscal year with an `AipOffice` per office hanging off it. Status is record-wide;
Finalize locks the whole thing.

The reviewer model runs the other way: **each office prepares its own AIP → its reviewer submits
→ PPDO consolidates the submitted ones.** That is not a workflow bolted onto the current model, it
is the opposite ownership direction.

**And there is no FK to hang office ownership on.** `AipOffice` has *no* reference to the `offices`
config table — no `OfficeId`, no `OfficeConfigId`:

```csharp
public sealed class AipOffice          // AipOffice.cs
{
    public int    Id          { get; set; }
    public int    AipRecordId { get; set; }   // parent AIP — not the office config
    public string RefCode     { get; set; }   // office identity is a STRING
    public string Name        { get; set; }
    public string Sector      { get; set; }
}
```

Office identity in AIP is **ref-code string matching**, the same fragile mechanism RAL-181's
follow-up (PR #188) had to fall back on. So §3.1's "AIP has no office scoping" is stronger than a
missing `WHERE` clause — there is no column to filter on, and adding ownership means adding the FK.

**Consequence: the reviewer/consolidation flow and the AIP redesign are one piece of work, not
two.** LDIP already has the needed shape (`LdipRecord.OfficeId` — office-owned records), which is
exactly why basing the new AIP on LDIP is the right call structurally, not just visually.

### 6.2 — Permission design

Per Ralph's decision, model it as a **per-user flag**, mirroring `CanManageAllocation`:

| Piece | Shape |
|---|---|
| Column | `User.OverrideCanReviewBudgetPlanning` (`bool?`) |
| Resolution | `PermissionService.CanReviewBudgetPlanningAsync` → SuperAdmin `true`; else `Override ?? false`. **Admin not auto-granted**, exactly like `CanManageAllocation`. |
| Naming | Generic (`...BudgetPlanning`, not `...Aip`) so LDIP/WFP reuse it unchanged |
| Scope | Always combined with the caller's `OfficeScope` — a reviewer reviews **their own office only** |

**⚠️ Caveat on the pattern.** The flag fits the *plumbing* (storage, resolution, user-form
surfacing) but the *semantics* are new to this codebase. Every existing flag is purely
**additive** — `CanManageAllocation` grants a capability and never removes one. Reviewer needs two
guards that are inverses of each other:

1. every Budget Planning **write** endpoint must *deny* a reviewer (read-only on content);
2. the **submit** transition must deny *everyone except* a reviewer of that office.

Guard (1) is a subtraction, which no current flag does. It cannot be expressed by the existing
`ConfigHttp.AuthorizeAsync(req, _jwt, CanX, ct)` idiom — that helper only asks "does the caller
have X?". Expect a companion helper (`DenyReviewerWriteAsync`, alongside the `DenyForeignOfficeAsync`
of §4), and expect to apply it to every write endpoint rather than relying on a single choke point.

### 6.3 — Workflow state

`PlanningStatus` has no room for submission today — it is three string constants
(`Draft` / `Final` / `Archived`) in `PPDO.Application/Common/`, not a DB enum, so adding a value is
cheap. Submission needs at minimum a `Submitted` state plus `SubmittedById` / `SubmittedAt` audit
columns.

`CalendarEvent` (RAL-82) is the in-repo precedent worth copying — `Pending`/`Approved` plus
`ReviewedById` and `ReviewedAt`. Note it stores the *reviewer* and *review time*; a submit flow
needs the same two columns, and consolidation will likely want a third (which consolidated record
absorbed this submission).

### 6.4 — Open questions — ✅ all five answered 2026-08-25

Answered at the PPDC meeting. Full reading in `Phase_Plan.md` §12.6; verbatim answers in
`v1.8.0_Open_Items_Tracker.xlsx` (rows B3, B4, B4-a, B4-b, B4-c, B5).

1. ~~**Can a reviewer reject / send back?**~~ ✅ **Yes** — a PPDO reviewer comments and sends a
   whole office's work back for update and re-submission. One office at a time, not only the
   document as a whole. So `Submitted → Returned` does need its reason/comment trail.
2. ~~**What is "the consolidated work" concretely?**~~ ✅ **The existing multi-office record, with
   offices filled in as they submit** — not a newly assembled provincial record.
3. ~~**Can PPDO consolidate partially?**~~ ✅ **Yes** — PPDO reviewers review the consolidated work
   so far, before every office has submitted. Partial is the normal case, not an edge case.
4. ~~**Can an office edit after submitting?**~~ ✅ **Locked on submission to PPDO**, and unlocked
   again when a PPDO reviewer returns it. Before that hop — during department-head review — both
   the encoder and the reviewer may still edit.
5. ~~**One reviewer per office, or several?**~~ ✅ **One per office.** The per-user flag permits
   several, so the constraint has to be built deliberately (`Phase_Plan.md` V18-79); whether it is
   a database constraint or an administrative convention, and what happens when that person is on
   leave, is tracker row B13.

**Also settled:** the cross-office reviewer is a **PPDO user, not the LFC** — the LFC no longer
reviews in the system (`Phase_Plan.md` §12.4).

---

## 7. Test coverage note

`PermissionServiceTests` and `LdipServiceTests` both cover the office-user path. `AipServiceTests`
references `OfficeId` only in the `AipOffice`-entity sense, not the caller sense — there is
currently no test anywhere asserting that an office user is denied another office's AIP data.
Any fix in §3.1-3.5 should land with that assertion, per `../TEST_CONVENTIONS.md`.
