---
status: approved — §2 settled 2026-09-22
version: v1.8.x (Phase 7)
tickets: V18-71 (PPDO-116 … PPDO-121)
supersedes: —
---

# AIP Concurrent Edit — Requirements

Governed by [SPEC_STANDARD.md](../SPEC_STANDARD.md). Implements **V18-71** from
[Phase_Plan.md](Phase_Plan.md) §8.

> ✅ **§2 settled 2026-09-22.** Ralph: *"go with soft warning, optimistic concurrency"* — 🅐
> confirmed explicitly. 🅑, 🅒 and 🅓 were presented alongside it as defaults and adopted with it;
> they remain cheap to change until PPDO-118 starts, and each still records what would change.

> ⚠️ **"Soft warning" does not mean the save goes through.** It is worth pinning, because the
> phrase has a second reading that would leave the bug in place. **Soft** describes the *locking
> model*: nothing is reserved, no lease is held, an abandoned tab blocks nobody, and the user may
> choose to overwrite. **The conflicting save is still rejected** — 409, nothing written. A save
> that completed with a warning attached would still destroy the first encoder's work, which is
> the entire defect this document exists to close.

---

## 1. Goal

Two or more encoders per office is confirmed normal ([Phase_Plan.md](Phase_Plan.md) tracker D5).
Today, when two of them have the same AIP activity open and both save, **the second save silently
overwrites the first**. Nobody is told. The first encoder's work is gone, and the only trace is an
audit row nobody reads.

This is why V18-71 is a **correctness gap, not hardening** — the Phase Plan promoted it out of the
hardening bucket for exactly this reason, and it is the one item in Phase 7 that costs real work
rather than polish.

**In one line:** a save must fail, loudly and recoverably, when the row changed underneath the
editor since they loaded it.

### What exists already, and why neither covers this

| Mechanism | What it does | Why it is not this |
|---|---|---|
| `AipOffice.WorkflowStatus` lock (PPDO-70) | Freezes an office's whole record at `SubmittedToPpdo`, unfreezes on return | Record-level and workflow-driven. It protects the **submitted** state; this is about the **editable window before** submission, which is where encoders actually collide. |
| Audit log (`audit_logs`) | Records who changed what, after the fact | Forensic. It can tell you afterwards that work was lost; it cannot stop it, and nothing surfaces it to the encoder. |

---

## 2. Decisions (settled 2026-09-22)

### 🅐 Optimistic concurrency, not a pessimistic lock — ✅ **confirmed**

A save carries the version the editor loaded. If the stored version has moved, the save is
**rejected** and the editor is told. No lock is taken, nothing is reserved, and an abandoned tab
blocks nobody.

**Why not a soft lock:** a lock needs a lease duration, renewal while typing, expiry, a takeover
path for "Maria went to lunch with the row open", and a story for a browser that crashed mid-lease.
That is a lot of machinery and several new failure modes for a three-person office. Optimistic
concurrency has one failure mode and it is the one we want.

*If answered the other way:* the data model below still stands (a lock needs the same version
column), but §3, §4 and §6 are rewritten around lease acquisition and takeover, and the ticket
split roughly doubles.

### 🅑 The **activity row** is the unit of conflict — ✅ adopted

Not the whole AIP record, not the office.

**Why:** the normal case this exists to support is two encoders working *different* programs in the
same office at the same time. Record-level detection would reject both of them constantly for
edits that never touched the same data — which trains people to click through the warning, and a
warning people click through is worse than no warning. Expenditures are versioned on their own row
for the same reason.

*If answered the other way:* far simpler to build, and near-useless.

### 🅒 The loser keeps their input — ✅ adopted

A rejected save never discards what the user typed. They see their value, the stored value, who
saved it and when, and choose: **overwrite** or **discard mine and reload**.

**Why not auto-merge:** these are money fields. A silent field-level merge produces a row neither
person entered and neither person can explain at PDC.

### 🅓 AIP Entry only, this round — ✅ adopted

Activities and their expenditures. WFP entry has identical exposure and is deliberately excluded —
the encoders are in AIP this season, and the mechanism is built to be liftable.

*If answered the other way:* the backend primitive is unchanged; add WFP's tables and screens to
the ticket split.

### Open follow-ups (not blocking)

- Whether a rejected save should notify the *other* editor (they may still have the stale row open).
  Proposed: **no** — out of scope, and it needs the notification plumbing from PPDO-75.
- Whether program/project **rename** collisions matter. Proposed: **not yet** — renames are rare and
  low-stakes next to amounts.

---

## 3. Behaviour

| Case | Given | When | Then |
|---|---|---|---|
| Happy path | Ana loads activity #12 (v1) and nobody else touches it | she saves | 200. Row is v2, `updated_by` = Ana. |
| **Conflict** | Ana and Ben both load #12 (v1); Ben saves first (now v2) | Ana saves | **409**, body names the current values, Ben, and when. Ana's input is preserved on screen. Nothing is written. |
| Conflict → overwrite | Ana is looking at the conflict panel | she chooses **Overwrite** | Her values are saved against v2. Row becomes v3. An audit row records the overwrite. |
| Conflict → discard | Ana is looking at the conflict panel | she chooses **Discard mine** | The row reloads at v2. Her input is gone, by her own choice. |
| Edge: no change | Ana loads #12 and saves with nothing edited | she saves | 200 and a version bump. We do not diff to detect a no-op save; not worth the complexity. |
| Edge: different rows | Ana edits #12, Ben edits #13, same office, same moment | both save | Both succeed. **This is the case 🅑 exists to protect** — record-level detection would have failed one of them. |
| Edge: expenditure vs parent | Ana edits activity #12's amounts; Ben adds an expenditure under #12 | both save | Both succeed. They are separate rows with separate versions. |
| Edge: row deleted | Ana loads #12; Ben deletes it | Ana saves | **404**, not 409. "This activity was deleted by Ben." Only recovery is to reload. |
| Failure: version absent | A client sends no version | it saves | **400**. Not treated as "no conflict" — see the ⚠️ in §4. |
| Failure: version malformed | Version is not valid base64 | it saves | **400**, same path. |
| Role: office encoder, own office | Ben is an encoder in the same office | conflict occurs | Sees Ben's **name**. Same office, so this is not a disclosure. |
| Role: PPDO reviewer edit | A department-head reviewer edits during review (permitted — [Permission_Matrix.md](Permission_Matrix.md)) | conflict occurs | Identical behaviour. Concurrency is orthogonal to permission. |
| Role: cross-office | A cross-office reviewer holds `CanReviewAllOffices` | conflict occurs | Sees the name. They already read the whole record; nothing new is disclosed. |
| Role: **submitted record** | The office is at `SubmittedToPpdo` | anyone saves | **PPDO-70's existing 403 wins and fires first.** The concurrency check never runs. Order matters — see §4. |

---

## 4. API contract

No new endpoints. Every AIP **write** endpoint gains an optional-then-required version field.

### Coverage — exactly which edits are protected

Verified against the endpoint list 2026-09-22.

| Route | Versioned row | Covers |
|---|---|---|
| `PUT /api/budget-planning/aip/{id}/activities/{activityId}` | activity | inline amount edit — PS / MOOE / CO |
| `PUT /api/budget-planning/aip/activities/{id}/details` | activity | **activity details** — ESRE code, dates, implementing office, expected outputs, CC adaptation/mitigation, typology |
| `PUT /api/budget-planning/aip/activities/{id}/is-creation` | activity | the new-vs-continuing flag |
| `DELETE /api/budget-planning/aip/activities/{id}` | activity | |
| `PUT /api/budget-planning/aip/expenditures/{id}` | expenditure | **expenditure** — account, fund source, PS/MOOE/CO, **and its procurement items** |
| `DELETE /api/budget-planning/aip/expenditures/{id}` | expenditure | |
| `POST /api/budget-planning/aip/activities/{activityId}/expenditures` | — | **create: no version sent.** A row that does not exist yet cannot conflict. |

**So yes: both activity details and expenditures are covered, and so are procurement items.**

> ✅ **Procurement items need no version of their own.** They have **no endpoints** — they are
> nested in the expenditure write DTO (`dto.ProcurementItems`) and persisted by
> `ReplaceProcurementItemsAsync(expenditureId, …)`, a wholesale replace scoped to the parent. So
> the only way to change an item is through the expenditure `PUT`, and versioning that row covers
> them.
>
> ⚠️ **This holds because of one line, and a future change could quietly break it.**
> `AipExpenditureService` sets `line.UpdatedAt = DateTime.UtcNow` **unconditionally** on update,
> before any item handling — so the parent row is always dirtied and SQL Server always bumps its
> `rowversion`, even when *only* the items changed. Remove that unconditional write as an
> "optimisation" (skip the parent when nothing on it changed) and two encoders editing procurement
> items under the same expenditure would both succeed, silently, last-write-wins — the exact bug
> this spec exists to close, reintroduced one level down. **If procurement items ever get their own
> endpoints, they need their own `rowversion`.**

**Not covered, by decision** (§2 open follow-ups): program, project and office **renames**
(`PUT .../programs/{id}`, `.../projects/{id}`, `.../offices/{officeId}`, `.../function-band`).
Rare, and low-stakes next to amounts. Adding them later is the same three columns and the same
service pattern — the mechanism does not change, only the table list.

**Request** — one added field, base64 of the `rowversion`:

```jsonc
{ "ps": 100000, "mooe": 250000, "co": 0, "rowVersion": "AAAAAAAAB9E=" }
```

**409 response** — the standard `{ data, error, message }` envelope, with the detail the UI needs:

```jsonc
{
  "data": {
    "conflict": {
      "changedByName": "Ben Reyes",
      "changedAtUtc": "2026-09-22T01:14:09Z",
      "current": { "ps": 100000, "mooe": 300000, "co": 0, "rowVersion": "AAAAAAAAB9I=" }
    }
  },
  "error": "This activity was changed by Ben Reyes while you were editing it.",
  "message": null
}
```

`current.rowVersion` is what an **Overwrite** resubmits with, so the retry is one round trip.

### Check ordering — get this right

Per endpoint, in this order:

1. JWT → 401
2. Permission + office scope → 403 *(incl. PPDO-70's submitted-record lock)*
3. Row exists → 404
4. **Version match → 409**
5. Validation → 400

> ⚠️ **The concurrency check goes after authorization, never before.** Running it first turns the
> 409 into an oracle: a caller with no rights to the record could probe whether it exists and
> whether it recently changed by watching 409 vs 403. Same reasoning as
> `ConfigHttp.AuthorizeWriteAsync`, which runs the permission predicate before the reviewer guard
> for exactly this reason.

> ⚠️ **A missing `rowVersion` is a 400, not a pass.** The tempting rollout is "treat absent as no
> conflict, so old clients keep working". That makes the entire feature opt-out by omission — any
> client that forgets the field silently gets the old last-write-wins behaviour, which is the bug.
> Staged rollout is handled in §8 instead.

---

## 5. Data model changes

### `aip_activities` (legacy PascalCase table — see [NAMING_CONVENTIONS.md](../NAMING_CONVENTIONS.md))

| Column | Type | Null | Notes |
|---|---|---|---|
| `RowVersion` | `rowversion` | no | SQL Server maintains it. Mapped with `.IsRowVersion()`. |
| `UpdatedAt` | `datetime2` | yes | Null for rows never edited since the migration. |
| `UpdatedById` | `uniqueidentifier` | yes | FK → `Users.Id`. Supplies the name in the 409. |

⚠️ **`AipActivity` currently has no update tracking at all** — no `UpdatedAt`, no `UpdatedBy`,
not even `CreatedAt`. Verified against the entity 2026-09-22. All three columns are new.

### `aip_expenditures` (snake_case)

| Column | Type | Null | Notes |
|---|---|---|---|
| `row_version` | `rowversion` | no | |
| `updated_by_id` | `uniqueidentifier` | yes | FK → `Users.Id` |

`created_at` and `updated_at` **already exist here** — the asymmetry with `aip_activities` is
pre-existing, not introduced by this change. Do not "tidy" it in this migration.

### Why `rowversion` rather than comparing `UpdatedAt`

`rowversion` is maintained by SQL Server on every update, so it **cannot be forgotten**. EF Core's
`.IsRowVersion()` puts it in the `WHERE` clause automatically and raises
`DbUpdateConcurrencyException` when zero rows match — the detection is the database's job, not a
service's.

Comparing `UpdatedAt` instead would put the burden on every write path to remember to set it. This
codebase has ~240 endpoints and a documented history of one call site missing a rule the others
follow (PPDO-92's three-place route gate, PPDO-115's reset path). A mechanism that depends on
discipline across that many sites will be wrong within a release.

`UpdatedAt` / `UpdatedById` are still added — but for the **message**, not the detection.
`rowversion` tells you *that* it changed; only these tell you *who*.

---

## 6. UI states

Applies to AIP Entry and the AIP detail page's inline editors.

| State | Required content |
|---|---|
| **Loading** | Unchanged — existing skeletons. This adds no fetch. |
| **Empty** | N/A. |
| **Error (conflict)** | An inline panel on the row, **not** a toast and **not** a modal. See below. |
| **Error (other)** | Unchanged — existing save-failure handling. |
| **Success** | Unchanged toast. The new version is stored silently for the next save. |
| **Read-only / forbidden** | Unchanged. PPDO-70's lock already renders submitted records read-only; a read-only row cannot conflict. |
| **Validation** | Unchanged. Validation runs *after* the version check, so a conflicted save shows the conflict, not field errors. |

### The conflict panel

Expands **in place on the conflicting row**, pushing content down rather than overlaying:

- `Ben Reyes changed this activity at 9:14 AM, while you were editing it.`
- A two-column compare, **differing fields only**: `Your value` / `Their value`.
- Two buttons: **Overwrite with mine** (danger) · **Discard mine and reload** (secondary).
- The user's input stays in the form, editable, throughout.

> ⚠️ **Not a toast.** Toasts auto-dismiss, and this one carries the only copy of a decision the user
> has to make. Dismissing it by waiting would leave unsaved money edits on screen with no indication
> they were rejected — strictly worse than the bug we are fixing.

> ⚠️ **Not a modal either.** The user needs to see their own values while deciding, and a modal
> covers the row they are comparing against.

**Time renders in Manila (UTC+8)** per [CLAUDE.md](../../CLAUDE.md) — the API returns UTC.
Styling follows [DESIGN_SYSTEM.md](../DESIGN_SYSTEM.md): flat, no rounding, `danger-*` for the
overwrite action.

---

## 7. Non-goals

- **Real-time presence** ("Ben is editing this"). Needs websockets/polling; no infrastructure for it.
- **Field-level auto-merge.** Rejected in 🅒 — these are money fields.
- **Locking, leasing, takeover.** Rejected in 🅐.
- **WFP, LDIP, Inventory.** Same exposure, deliberately out of scope (🅓).
- **Offline conflict resolution.** That is Phase 6's V18-68 and a harder problem; this assumes an
  online client.
- **Notifying the other editor.** Follow-up in §2.
- **Retrofitting `CreatedAt` onto `aip_activities`.** Tempting while in the migration; unrelated.

---

## 8. Deployment notes

⚠️ **Contains a migration. [CI does not run migrations](../../CLAUDE.md)** — `dotnet ef database update`
is manual, and **must run before the code deploys** or every AIP write fails on a missing column.

Migration name: `AddAipConcurrencyTokens`.

**Backfill:** none needed. SQL Server populates `rowversion` for existing rows automatically;
`UpdatedAt` / `UpdatedById` stay null until a row is next edited, and the UI reads a null as
"not edited since the feature shipped".

**Staged rollout** — this is how old clients are handled, *instead* of treating a missing version
as a pass (§4):

1. **Deploy 1** — migration + backend accepting the field as **optional**. A missing version logs a
   warning and proceeds. Nothing breaks; nothing is protected yet.
2. **Deploy 2** — frontend sends it everywhere.
3. **Deploy 3** — backend makes it **required** (400 when absent). Only flip this once the warning
   from step 1 has stopped appearing in Application Insights.

Step 3 is a real step, not a formality. **A deployment that stops at step 2 has the schema, the UI
and none of the protection** — and will look finished.

---

## 9. Ticket split

| # | Ticket | Depends on |
|---|---|---|
| **1** | Migration + entity config: `rowversion` on both tables, `UpdatedAt`/`UpdatedById` on activities, EF `.IsRowVersion()` mapping | — |
| **2** | Service layer: catch `DbUpdateConcurrencyException`, resolve the changer's name, return `ServiceResult.Conflict` with the payload; set `UpdatedAt`/`UpdatedById` on every AIP write | 1 |
| **3** | Endpoints: accept `rowVersion` (optional), map 409, keep the §4 check order. **Deploy 1.** | 2 |
| **4** | Frontend: hold the version, send it, render the conflict panel, wire Overwrite / Discard. **Deploy 2.** | 3 |
| **5** | Flip to required + remove the warning path. **Deploy 3.** | 4 + prod evidence |

Ticket 2 carries the real logic and is where the tests live.

---

## 10. Acceptance checklist

Verifiable against the running app by a person, two browsers, one office:

- [ ] Two encoders open the **same** activity. First saves → succeeds. Second saves → conflict panel naming the first encoder and the time in Manila.
- [ ] The second encoder's typed values are **still on screen**, unchanged, after the rejection.
- [ ] **Overwrite** saves their values; reloading in the first browser shows them.
- [ ] **Discard mine and reload** shows the other encoder's values.
- [ ] Two encoders edit **different** activities in the same office simultaneously → both succeed, no warning. *(The regression that would make this feature hated.)*
- [ ] Editing an activity's amounts while another adds an expenditure under it → both succeed.
- [ ] Two encoders open the same **expenditure** and both edit **only its procurement items** → the second is stopped. *(The nesting case from §4 — it is protected by the parent row's version, not its own, so it is worth verifying rather than assuming.)*
- [ ] Two encoders open the same activity and edit **details** (dates, ESRE, outputs) rather than amounts → the second is stopped. Same row, same guard.
- [ ] Deleting the row in one browser, then saving in the other → "deleted", not a conflict panel.
- [ ] A submitted (`SubmittedToPpdo`) record still returns PPDO-70's 403, not a 409.
- [ ] After Deploy 3, a request with no `rowVersion` gets 400.

---

## 11. Test focus

- `DbUpdateConcurrencyException` → `ServiceResult.Conflict`, with the changer resolved. **Red-test it** ([CLAUDE.md](../../CLAUDE.md)): the happy-path tests all save against an unchanged row, so a guard that never fires would pass vacuously — the same trap PPDO-115's inactive-target test hit.
- Two saves against the same loaded version in one test: first wins, second conflicts.
- **Check order**: a caller lacking permission gets 403 even when the version is *also* stale. Assert the 403, not just "not 200" — this is the oracle guard from §4 and it is the one that fails silently.
- Deleted row → 404, not 409.
- `UpdatedById` is set on every AIP write path, not only the inline editor. Worth a sweep test over the endpoint list rather than one case, in the shape of `ReviewerWriteGuardCoverageTests`.
- Missing / malformed version → 400 (after the Deploy 3 flip).
