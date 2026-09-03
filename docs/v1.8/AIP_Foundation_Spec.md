# v1.8.0 Phase 2 — AIP Foundation

> **Authoritative spec.** Governed by `docs/SPEC_STANDARD.md`. Written 2026-09-02.
>
> Read first: `docs/v1.8/Phase_Plan.md` §4 (the work items this expands),
> `docs/v1.8/AIP_Redesign_Notes.md` §4a (the clean break), `docs/v1.8/Permission_Matrix.md`,
> `docs/PERFORMANCE_GUIDELINES.md`, `docs/NAMING_CONVENTIONS.md`.
>
> Phase 1 is complete and merged to `release/1.8.0`. Nothing here waits on it.

---

## 1. Goal

The AIP is the subject of v1.8.0, and today it cannot carry the redesign. Three structural facts
block Phase 3 (AIP Entry) and Phase 4 (Review):

1. **An AIP office has no owner.** `AipOffice` identifies its office by `RefCode` *string suffix
   matching* — there is no column to scope on. Every ownership question in the codebase is
   currently answered by `RefCode.EndsWith(office.OfficeRefCode)`. A review workflow cannot be
   built on a string comparison.
2. **Money lives on the leaf with no breakdown.** `AipActivity.Ps/Mooe/Co/Total` are four loose
   decimals. There is nowhere to record *what* an amount is for, which is what AIP entry (Phase 3)
   and the printable form (Phase 5) both need.
3. **AIP stores thousands while everything it touches stores pesos.** Six sites multiply by 1000
   to cross that boundary. It is the one live numeric seam in Budget Planning and it is guarded by
   nothing.

This phase fixes the three, and partitions the record shape at FY2028 so the redesign can proceed
without migrating historical data.

**It ships no user-facing feature.** That is deliberate — see §7.

---

## 2. Decisions (settled)

1. **One pot, drawn down in sequence** (DECISION A, 2026-08-25). AIP gets its **own** expenditure
   table rather than generalising `WfpExpenditure`. The two documents answer different questions and
   a shared table would have to serve both, badly.

2. **Pesos everywhere, migrated — not partitioned** (DECISION E, 2026-08-25). `total = total * 1000`
   for **every** fiscal year, then the six ×1000 sites are **deleted**, not made FY-conditional.
   Reasoning in §5.3; it is the most important paragraph in this document.

3. **Shape partitions, units do not.** FY≤2027 keeps the v1.6 record shape; FY≥2028 uses the new
   one. But `AipActivity.Total` means **pesos on every row regardless of fiscal year**. Anyone who
   reads "clean break" as covering units will reintroduce exactly the bug §5.3 exists to remove.

4. ✅ **Shipped 2026-09-03 (V18-40).** `AipRecord.OfficeId` → `offices.id`, nullable, Restrict,
   indexed on `(office_id, fiscal_year)`. Two shapes now live in one table: **office-owned**
   (owner set — the FY≥2028 shape) and **legacy multi-office** (owner null).

   ⚠️ **That null is permanent and correct, not a backfill that has yet to run** — the opposite of
   `AipOffice.OfficeId`, whose nulls are unmatched rows to be resolved. A pre-FY2028 record spans
   every office in the province, so there is no owner to fill in. Hence no backfill in the
   migration, and no conversion between shapes (V18-37).

   ⚠️ **The create guard had to become shape-aware.** "Is there an AIP for FY 2028" was the right
   question while one record spanned every office; under the office-owned shape it reports the
   *first* office's record as a conflict for every other office in the province. Office-owned
   creates ask `GetByOfficeAndFiscalYearAsync` instead. The index is deliberately **not unique** —
   the rule counts only non-Archived records, which an index cannot express, so the service owns it
   (the same call `LdipRecord` made).

   **PPDO gets an ordinary office record** (tracker B12-b, 2026-08-26). No per-division AIP records,
   no division column on `AipOffice`, and divisions never print. Division of work is carried on the
   **program**, through the existing `ProgramDivision` map, exactly as WFP does.

5. **Office identity is a real FK.** `AipOffice.OfficeId` → `offices.id`. `RefCode` stays as the
   AIP-side re-link key and the backfill audit trail, the same division of labour `ProgramDivision`
   already uses.

   ⚠️ **Corrected 2026-09-03: this was wrongly recorded as "shipped in Phase 1 (DECISION F)".** It
   was not. DECISION F shipped `users.office_id` and `offices.is_host_office`, a different column on
   a different table. `AipOffice` had no ownership column at all until **V18-32 (PPDO-33)** added it,
   and nine `RefCode.EndsWith` scoping sites were still live across five services when that ticket
   started. Anyone reading the old wording would have skipped the ticket as already done.

6. **LDIP is not moving to pesos, and that is not an inconsistency.** The rule is not *one unit
   everywhere*; it is **units may differ, but only where the value never crosses a boundary, and
   every boundary is named**. AIP had to move because AIP↔WFP is crossed six times. LDIP crosses
   zero — `SeedProgramsFromLdipAsync` copies ref code, name and function band and explicitly no
   amounts (`AipService.cs:785`), and both dashboard summaries carry counts only.

### Open — must be answered before the ticket that needs it

| # | Question | Blocks | Default if unanswered |
|---|---|---|---|
| ~~**P2-a**~~ ✅ **answered 2026-09-03 — divide at render** | After the migration, does the AIP detail page **drop** the `(in ₱000)` headers and show full pesos, or **divide by 1000 at render** to keep the province's convention? | V18-35 | Shipped as the default: the headers stay, `toDisplayUnits`/`toStorageUnits` convert at the page edge, and nothing an encoder sees or types changed when storage did. Phase 5's printable form has to render thousands to match the province's form regardless, so showing raw pesos would only have relocated the conversion |
| **P2-b** | Does the FY partition gate on a **literal `fiscalYear` check inside shared endpoints**, or **new endpoints beside untouched old ones**? | V18-37 | New endpoints. A literal check inside a shared handler is a branch that must stay correct forever in a file nobody re-reads |
| **P2-c** | Does `aip_expenditures` reuse the existing `accounts` config table, or does AIP need its own expense vocabulary? | V18-33 | Reuse `accounts` — it is the same chart of accounts, and WFP already snapshots from it |

P2-a needed a person outside the dev team (the finance officers) and is now answered. P2-b and P2-c
are engineering calls that can be made when the ticket starts.

---

## 3. Behaviour

Phase 2 is mostly structural, so most rows below are invariants rather than interactions.

### 3.1 Core

| Case | Given | When | Then |
|---|---|---|---|
| Ownership resolves without string matching | An `AipOffice` with `OfficeId` set | Any scoped AIP read | Rows filter on `OfficeId`, not `RefCode.EndsWith(...)` |
| Backfill leaves nothing orphaned | FY2027 AIP rows whose `RefCode` suffix matches a configured office | Migration runs | Every matched row gets `OfficeId`; **unmatched rows keep null and are reported, never dropped** |
| Units migrate uniformly | Any `aip_activities` row, any fiscal year | Migration runs | `sum(total) after == sum(total) before × 1000`, exactly. Same for `ps`, `mooe`, `co`, `cc_adaptation`, `cc_mitigation` |
| The AIP↔WFP seam still agrees | A WFP expenditure against an AIP activity of ₱250,000 | Ceiling check runs | Same accept/reject verdict as before the migration — the ×1000 is gone from **both** sides at once |
| Activity totals are derived | An activity with three `aip_expenditures` rows | An expenditure is added, edited or deleted | `Ps`/`Mooe`/`Co`/`Total` on the activity are recomputed and **stored**; every report reads the stored value |
| An activity whose lines were all deleted | It had lines; the last is removed | Recompute after the delete | `Total == 0`, not null. Null meant "never computed", which no longer exists as a state once an activity has had lines |
| An activity that never had lines | FY≤2027, imported | Any recompute | **Untouched.** Same `LineCount` 0 as the row above, opposite outcome — see §5.4 |
| Old fiscal years keep working | FY2027 record | Opened | Renders in the v1.6 shape, unchanged, with peso values |
| New fiscal years use the new shape | FY2028 record | Opened | New shape. **No migration path between the two** — a record does not change shape when its year changes |
| Upload is historical-only | A user uploads an `.xlsm` for FY2028 | Submit | Refused with a message naming the reason. FY≤2027 upload is unchanged |

### 3.2 Permission and scope

Every row is already pinned by `docs/v1.8/Permission_Matrix.md`; Phase 2 changes the *mechanism*,
not the rules.

| Account | Given | When | Then |
|---|---|---|---|
| Host-office (PPDO) user | `OfficeScope` → `SeeAll` | AIP read | Every office's AIP. **Division narrows further** — the two-axis case |
| Guest-office user | Own office | AIP read | Own office only. **Division is not a factor** (`Permission_Matrix.md` §3.1, PPDO-4) |
| Guest-office user supplies another `officeId` | Query string | AIP read | Clamped to their own office. Their data back — not a 403 that confirms the other office exists |
| Any user, `office_id` null | Unassigned record | AIP read | `NoOffice` (id 0) → sees nothing. Empty states, not an error (DECISION F) |
| Cross-office reviewer | `CanReviewAllOffices` | AIP read | Every office, **read-only**, via `OfficeScope.ResolveForReview` — never `Resolve` |

✅ **Shipped 2026-09-03 as `AipReadScope`** (`PPDO.Application/Common/AipReadScope.cs`) — the first
consumer `BudgetPlanningScope` has ever had. Two findings worth recording:

1. **AIP reads were not scoped at all.** `GetByIdAsync` returned *every* office's full hierarchy to
   any caller with Budget Planning access, and the handlers bound `caller` without using it. Only
   the absence of production guest-office accounts kept that from being a live cross-office leak.
   The list endpoint's office count was unscoped for the same reason.
2. **The two axes attach to different levels.** Office filters `AipOffice` on its ownership FK;
   division filters `AipProgram` through `ProgramDivision`, because division of work is carried on
   the program (§2 decision 4). And the division filter applies **only to the host office's own AIP
   offices** — a division belongs to an office, so PPDO's internal division of labour says nothing
   about GSO's programs, and a division-scoped PPDO caller still sees every guest office in full.
   This matches what `BudgetPlanningDashboardService` already does over `hostAipOfficeIds`.

✅ **The AIP *write* paths are now scoped too** (PPDO-46, 2026-09-03). They checked existence and
Draft status but not ownership, so any caller with Budget Planning access could edit or delete
another office's AIP node by id. `CheckDraftAsync` became `CheckWritableAsync` — ownership first,
then draft state — covering the 11 write paths that already funnelled through it, with five
outliers guarded individually.

⚠️ **The refusal is `NotFound`, not `Forbidden`, and each call site passes its own message** so a
node the caller may not touch is byte-identical to one that does not exist. A 403 would confirm the
node exists and belongs to another office — the same existence check the reads clamp to avoid.
Clamping itself is not available on a write: a write names one node, and redirecting it to another
would silently write to the wrong row.

⚠️ **`OfficeScope` × `DivisionScope` is genuinely two-axis and is new** (V18-39). WFP applies both
always; LDIP applies office only. AIP applies office always and division **only when the caller is
PPDO**. Neither existing resolver expresses that, and `BudgetPlanningScope` — which exists to pair
the two axes and still has no consumer — is the natural home for it.

---

## 4. API contract

**No new endpoints in Phase 2.** Existing AIP endpoints keep their routes and envelopes; what
changes is what they filter on and what the numbers mean.

| Endpoint | Change |
|---|---|
| Every scoped AIP read | Filters on `AipOffice.OfficeId` instead of a `RefCode` suffix match |
| Every AIP response carrying money | Values are **pesos**. No shape change — the same fields, a different magnitude |
| `POST /api/budget-planning/aip/upload` | ✅ Refuses FY≥2028 (V18-38, shipped 2026-09-03). So does `…/aip/confirm` — the preview gate spares the user a parsed 20 MB workbook, the confirm gate is the one that guards |

⚠️ **Every money field on every AIP DTO changes meaning without changing type.** A `decimal Total`
of `250` becomes `250000`. Nothing in the type system catches a missed call site — which is why
§11's boundary test exists and why the frontend sweep in §6 is exhaustive rather than sampled.

`aip_expenditures` gets no endpoint here. Phase 3 owns AIP entry; this phase only creates the table
and the recompute so Phase 3 has something to write into.

---

## 5. Data model changes

### 5.1 `aip_offices` — ownership FK (V18-32)

```
ALTER TABLE aip_offices ADD office_id INT NULL
    CONSTRAINT FK_aip_offices_offices REFERENCES offices(id)
CREATE INDEX IX_aip_offices_office_id ON aip_offices(office_id)
```

**Nullable, deliberately.** The backfill matches `RefCode` suffix against `offices.office_ref_code`;
an office that was never configured has no match. A `NOT NULL` column forces the migration to invent
an owner or fail — both worse than a reported null, and the same choice `ProgramDivision.OfficeId`
made for the same reason.

`RefCode` is **not** removed. It stays as the AIP-side re-link key and the record of what the
backfill matched on.

### 5.2 `aip_expenditures` — new table (V18-33)

snake_case per `docs/NAMING_CONVENTIONS.md`, mapped from PascalCase via `IEntityTypeConfiguration`.
Mirrors `WfpExpenditure`'s **snapshot** pattern so a historical AIP survives config edits.

| Column | Type | Notes |
|---|---|---|
| `id` | INT IDENTITY | |
| `activity_id` | INT NOT NULL | FK → `aip_activities(id)`, cascade delete |
| `account_id` | INT NULL | FK → `accounts(id)` — see P2-c |
| `account_number_snapshot` | NVARCHAR NULL | Survives config edits |
| `account_title_snapshot` | NVARCHAR NULL | |
| `funding_source_id` | INT NULL | FK → `funding_sources(id)` |
| `funding_source_snapshot` | NVARCHAR(20) NULL | |
| `ps` / `mooe` / `co` | DECIMAL NOT NULL | **Pesos.** Default 0 |
| `total` | DECIMAL NOT NULL | `ps + mooe + co`, computed on write — never read from an input field |
| `created_at` / `updated_at` | DATETIME2 | |

Index on `activity_id` — every read is "this activity's expenditures", and the recompute in §5.4
runs on every write.

### 5.3 Units: thousands → pesos (V18-35) — **the dangerous one**

```sql
UPDATE aip_activities SET
  ps = ps * 1000, mooe = mooe * 1000, co = co * 1000, total = total * 1000,
  cc_adaptation = cc_adaptation * 1000, cc_mitigation = cc_mitigation * 1000
```

**Why migrate rather than partition.** Under a partition `AipActivity.Total` stops being readable
without knowing the fiscal year — permanently, not for a migration window — and six conversion sites
become six FY-conditional branches that must stay correct forever. Getting one wrong in the
*permissive* direction is **silent**: the ceiling simply never trips again, and the first symptom is
a WFP over its AIP activity, found by someone adding up a printed report by hand. Migrating deletes
the failure mode; partitioning schedules it.

**All six ×1000 sites — verified 2026-09-02, delete every one:**

| Layer | Site |
|---|---|
| Backend | `WfpCeilingService.cs:60` (status), `:106` (save validation), `:220` (finalize) |
| Frontend | `wfp/page.tsx:274`, `:377`, `:1409` |

⚠️ `WfpCeilingService`'s class doc claims the conversion happens **only** there. True of backend
services, **false of the codebase** — the frontend triple is undocumented today. Fix the comment or
delete it with the code.

⚠️ **The display half is mandatory and larger than Phase_Plan §4 states.** Verified 2026-09-02, all
in `aip/detail/page.tsx` (2,057 lines — the largest file in the repo):

| What | Where | Count |
|---|---|---|
| Thousands headers | `:2015` `Amount (in ₱000)`, `:2016` `CC Expenditure (₱000)` | 2 |
| Raw display cells | `:369–374` — `ps`, `mooe`, `co`, `total`, `ccAdaptation`, `ccMitigation` via `AmtTD` | 6 |
| **Money inputs** | `MoneyInput` in the inline add/edit rows | **11** |

Phase_Plan named one header and one cell. **Entry is in thousands too** — so a value typed as `250`
today must still mean ₱250,000 after the migration, or every edit silently divides the record by a
thousand. Untouched, `250` renders as `250000` under a header still promising ₱000: **₱250 million,
1000× high**, on a page people read numbers off. That is the same failure mode the migration exists
to remove, relocated from a ceiling check to a report.

⚠️ **`types/budget-planning.ts:908`** documents LDIP's budget as "in thousands (₱000), **like AIP
totals**". That cross-reference becomes false. Update it in the same commit or it will mislead the
next reader exactly as intended by its author.

**Verification is arithmetic, so do it:** capture `SUM(total)` per fiscal year before and after; the
ratio must be exactly 1000 for every year. Reversible by dividing.

### 5.4 Derived activity totals (V18-34)

`Ps`/`Mooe`/`Co`/`Total` on `aip_activities` stay **stored**, recomputed from `aip_expenditures` on
every write to a child row. Every report reads the stored value — recomputing on read would put a
`GROUP BY` under the printable form and the external API.

FY≤2027 activities have no expenditure rows. They keep their imported values and **must not be
recomputed to zero**, or a whole fiscal year is silently wiped.

⚠️ **Corrected during implementation (2026-09-03): "runs only where a child row exists" cannot be
the whole rule, because it contradicts the next requirement.** Two activities with **zero lines**
mean opposite things:

| Activity with no lines | Correct outcome |
|---|---|
| FY≤2027, imported from the workbook, never had children | **Leave its figures alone** |
| Had lines, the last one was just deleted | **Total → 0** |

Both present identically — `LineCount` 0, all amounts 0, since a `SUM` over no rows and a `SUM` of
zeroes are the same number. **The data cannot settle it, so the caller does.**
`IAipRepository.ApplyActivityTotalsAsync` takes a `zeroWhenNoLines` flag that **defaults to the safe
reading**, and the Application seam exposes it as two named methods rather than a boolean at the
call site:

| Called after | Method | No-lines behaviour |
|---|---|---|
| adding or editing a line, or a bulk/defensive pass | `RecalculateAsync` | leave alone |
| **deleting** a line | `RecalculateAfterLineDeleteAsync` | write 0 |

Only a caller that has just deleted a line knows the activity was expenditure-derived a moment ago.
Defaulting the other way would mean the cost of forgetting the flag is a stale total rather than an
erased fiscal year.

Note that an activity with lines that all sum to zero **is** written — it is costed, at zero.
`LineCount` is the only thing separating that from the imported case, which is why
`SumByActivityIdAsync` returns it.

### 5.5 FY partition (V18-37)

FY≤2027 → v1.6 shape (legacy multi-office, `AipRecord.OfficeId` null). FY≥2028 → new shape
(office-owned, `OfficeId` set). **No migration between them**, and no record changes shape.

#### P2-b — settled 2026-09-03: one named policy, not new endpoints

**Decision: a single `AipShape` policy in `PPDO.Application/Common/`, consulted by every path that
creates an AIP record.** No endpoint is duplicated and no endpoint is removed.

The spec's original default was *new endpoints beside untouched old ones*, on the reasoning that
the new path would then carry no legacy branch. Implementation showed that reasoning does not hold
here, for two concrete reasons:

1. **Duplicating the endpoint does not remove the fiscal-year check.** A new office-owned create
   endpoint still has to refuse FY2027, and the legacy one still has to refuse FY2028 — otherwise
   the gate is bypassed by posting the wrong year to the wrong route. Both routes end up carrying
   the check, so the split adds surface *on top of* it rather than instead of it.
2. **Two of the four create paths are not creates by name.** `CopyOfficeFromPriorYearAsync` and
   `SeedProgramsFromLdipAsync` *find-or-create* their target record. They would have had to be
   duplicated too, and neither reads as a record-creation endpoint from the outside — which is
   exactly how one of them gets missed.

The objection the original default was protecting against is real and stands: *a literal
`fiscalYear >= 2028` branch in a file nobody re-reads*. The answer to it is to make the branch a
**named, tested, greppable thing that exists once**, not to fork the routes. `AipShape` sits beside
`AipReadScope`, `OfficeScope`, `BudgetPlanningScope`, `AipOfficeOwnership` and `ReviewerWriteGuard`,
which are the same move for the same reason.

**The boundary year is a single constant**, `AipShape.FirstOfficeOwnedFiscalYear`. A test asserts
no other file hardcodes it, so moving the break — should the province ever slip FY2028 — is one
edit rather than a hunt.

#### The four gated paths

| Path | Was | Now |
|---|---|---|
| `ConfirmImportAsync` (`.xlsm` import) | always legacy | legacy only; FY≥2028 refused |
| `CreateManualRecordAsync` | shape chosen by DTO (V18-40) | shape must match the fiscal year |
| `CopyOfficeFromPriorYearAsync` | always legacy | legacy only; FY≥2028 refused |
| `SeedProgramsFromLdipAsync` | always legacy | legacy only; FY≥2028 refused |

⚠️ **The last two were a live shape leak, not a hypothetical one.** Both find-or-create with
`GetLatestByFiscalYearAsync` and construct a record with `OfficeId` unset. Pointed at FY2028 before
this ticket, they silently produced a legacy-shape record in a year that must not have one — with
no error, and nothing downstream to notice. Carry-forward and LDIP seeding into FY2028+ are Phase 3
work; until then the gate refuses them rather than letting them write the wrong shape.

#### The other door into a shape change

A record's shape is also reachable **one node at a time**. `AddOfficeAsync` on an office-owned
record could add an `AipOffice` belonging to a *different* office, and after two such calls the
record spans several offices — the legacy shape, arrived at without any record ever being
"converted". So the gate covers that too: **on an office-owned record, only the owning office may
have an `AipOffice` child.** The check is not a scope check and does not replace one; `OfficeScope`
already stops a guest office reaching another's record, but it says nothing about a PPDO admin, who
legitimately sees every office and would otherwise be able to do this.

The refusal is a `BadRequest` naming both offices, not the `NotFound` used by the ownership guard
(PPDO-46). Those hide *existence*; this one hides nothing — the caller may see the record, they are
being told the operation is wrong for its shape.

#### What is deliberately not gated

**Reads, and every write to an existing node.** A record's shape is a property of the row, already
settled at creation; `GetByIdAsync` and the program/project/activity writes behave the same either
way and gating them would add a branch with nothing behind it. And **nothing converts**: there is no
endpoint, service method or migration that changes `OfficeId` on an existing record, and there
should not be one. If a shape is wrong, the record is archived and recreated.

### 5.6 Migrations — four, and they apply in timestamp order

✅ **Corrected 2026-09-03, at the end of the phase.** This section planned three migrations under
names that were never used, and estimated the release's total at five. Both numbers were wrong; the
list below is what actually shipped.

| Order in the release | Migration (real name) | Ticket |
|---|---|---|
| 12 | `AddAipExpenditures` — new table | V18-33 |
| 13 | `MigrateAipAmountsToPesos` — data-only ⚠️ | V18-35 |
| 14 | `AddAipOfficeOwnershipFk` — FK + index + backfill | V18-32 |
| 15 | `AddAipRecordOwningOffice` — FK + index | V18-40 |

The **fourth** was not planned here because V18-40 (the office-owned record shape) was scoped after
this section was written. It is the reason "three" became four.

⚠️ **CI does not run migrations.** One manual `dotnet ef database update` against Azure SQL
applies them all. **v1.8.0 reaches production with 15 pending migrations, not five** — this section
counted only Phase 1's two AIP-adjacent ones (`AddClimateChangeTypologies`, `AddEsreCodes`) and
missed Phase 1's identity, password-reset and permission migrations entirely.

⚠️ **The units migration is #13, not last.** It does not need to be: #14 and #15 add columns to
`aip_offices` and `aip_records` and never touch `aip_activities` money. The ordering that does
matter is #12 before #13.

⚠️ **`docs/v1.8/Pre_Deployment_Checklist.md` §1 is the authority on the count and the order**,
not this section — it is rechecked at release time against `git diff`, and this one is a plan
written before the code. If they disagree again, believe the checklist.

---

## 6. UI states

Phase 2 adds no screen. Two existing surfaces change.

| Surface | Change |
|---|---|
| **AIP detail page** | ✅ P2-a answered — headers keep `(in ₱000)`, cells divide at render, inputs multiply on save. Display and entry moved together, as they had to. Counts corrected while implementing: **10 cells** (the office-total footer row renders AIP money too) and **10 inputs** (the eleventh grep hit was the import line) |
| **AIP upload** | ✅ Shipped 2026-09-03 (V18-38). FY≥2028 refused with a reason naming the fiscal year and the alternative, not a generic validation error. The dropzone and the Upload button are **disabled with the reason shown**, not hidden — the user has CanUploadAip, the *state* forbids it (`Budget_Planning_Dashboard_Requirements.md` §6.1). The detail page's **Re-upload** button is disabled the same way rather than linking to a page that refuses on arrival |

Flat design, PPDO tokens, `slate-800` headings / `slate-600` body, never `text-slate-700`.

> **`aip/detail/page.tsx` is 2,057 lines and `docs/v1.8/RETROSPECTIVE.md` says to extract before
> redesigning.** V18-35 touches 19 sites in it. That is a reasonable moment to extract the activity
> row and its edit form — but **extraction and a unit migration in one commit is not reviewable**.
> Do the migration first, extract second, or the diff hides which change caused a wrong number.

---

## 7. Non-goals

- **AIP entry.** Phase 3. This phase creates the table and the recompute; it writes no UI to fill it.
- **Review, submission, locking, comments.** Phase 4.
- **The printable AIP form.** Phase 5. ⚠️ It inherits pesos — build `AipReportExcelService`
  peso-aware from the start; do not let it acquire a ×1000 by copying `WfpReportExcelService`.
- **Moving LDIP to pesos.** Decision 6.
- **Removing `AipOffice.RefCode`.** It stays as the re-link key.
- **Removing `ProgramDivision`'s ref-code keying.** Deliberate and documented (RAL-249).
- **Enforcing 1 program : 1 division.** PPDO-31, deferred to the WFP rework.
- **Any new endpoint.**

---

## 8. Deployment notes

- **Four migrations from this phase, manually applied** (§5.6). **15 pending for v1.8.0 as a whole** — counted at the end of Phase 2 and rechecked at release time against `git diff`; `Pre_Deployment_Checklist.md` §1 is the authority.
- ⚠️ **Check for pre-existing FY≥2028 owner-less records before the release, and archive any that
  are still active.** V18-37 governs new writes; it cannot reach rows already in the table. The
  local dev database was found (2026-09-03, live check) to hold eleven FY2028 records with a null
  `office_id` — the exact combination the partition forbids — left over from earlier testing. All
  were Archived, so they are inert: the create guard counts only non-Archived records, and an
  archived row cannot be written to. An **active** one would be worse than untidy, because it would
  block that year's real office-owned records through the one-per-year conflict guard.

  ```sql
  SELECT id, fiscal_year, office_id, entry_source, status
  FROM   aip_records
  WHERE  fiscal_year >= 2028 AND office_id IS NULL AND status <> 'Archived';
  ```

  Expect zero rows in production — FY2028 planning has not started there. Archive anything returned
  rather than deleting it or backfilling an owner: there is no correct owner to fill in (§5.5).
- **Take a database backup before migration 3.** It is the only one that rewrites existing values
  across every fiscal year. Reversible arithmetically, but a restore is faster than a reasoning
  exercise at 9pm.
- ⚠️ **Release note, not a defect: FY2027's stored AIP is permanently not a faithful copy of the
  province's workbook.** Those rows were imported by the pre-RAL-238 parser, which read hierarchy
  level from the wrong column (`Phase_Plan.md` §4b). Freezing the importer (V18-38) makes that
  permanent — there is no longer a supported path that would re-import them correctly, and
  re-importing FY2027 faithfully is explicitly out of scope. Say so in the v1.8.0 notes rather than
  leaving it for whoever next diffs a portal FY2027 total against the province's file.
- No new dependency, environment variable, or CORS origin.
- No new Azure resource. If one is ever added: **Southeast Asia** (RAL-237).

---

## 9. Ticket split

| Ticket | Scope | Size | Blocked by |
|---|---|---|---|
| **V18-32** | `AipOffice.OfficeId` FK + backfill + switch every scoped read off string matching | M | — |
| **V18-35** | Units → pesos, all FYs; delete the six ×1000 sites; the 19-site display/entry half | L | — |
| **V18-36** | Pin the AIP↔WFP numeric boundary with tests | S | V18-35 |
| **V18-33** | `aip_expenditures` table + entity + configuration + repository | M | — |
| **V18-34** | Derived, recomputed activity totals | M | V18-33 |
| **V18-39** | AIP scope resolver — `OfficeScope` × `DivisionScope` | M | V18-32 |
| **V18-40** | New AIP record shape — office-owned, LDIP-like | L | V18-32 |
| **V18-37** | FY partition, FY≤2027 vs FY≥2028 | M | V18-40 |
| **V18-38** | Freeze `.xlsm` upload to historical years | S | V18-37 |

**Order:** `32` + `35` in parallel → `36`, `33`, `39` → `34`, `40` → `37` → `38`.

**V18-36 is a good manual-implementation candidate** — small blast radius, `dotnet test` feedback
with no app running, reversible, and `WfpCeilingServiceTests` is a sibling to pattern-match.
**V18-32, V18-33 and V18-35 are not** (migrations). **V18-39 is explicitly not** — scope resolution
is the class where a wrong choice compiles cleanly and leaks data.

---

## 10. Acceptance checklist

```
- [x] An FY2028 record belongs to exactly one office (V18-40)
- [x] PPDO's record is structurally identical to a guest office's — no branch anywhere, pinned by
      a test that asserts the two creates produce the same shape
- [x] No division column exists on aip_offices OR aip_records — asserted against the EF model, so
      adding one fails the build rather than passing review
- [x] Two offices can each hold a record for the same fiscal year; one office cannot hold two
- [x] Every scoped AIP read filters on aip_offices.office_id; no RefCode.EndsWith remains on a
      scoping path (grep it). ONE deliberate use survives, in AipOfficeOwnership.ResolveOfficeId —
      it ESTABLISHES the FK for a newly uploaded office and is not a read path
- [x] The backfill reports unmatched rows rather than dropping them, and the count is recorded.
      Local rehearsal 2026-09-03: 56 AIP offices, 55 matched, 1 unmatched (`3000-000-1-01-004`, a
      nameless row whose `01-004` has no configured office). Production count is captured by the
      runbook step before the release
- [x] SUM(total) per fiscal year after migration = SUM(total) before x 1000, exactly, every year
      (verified locally, 3 fiscal years; production runs the same check per the pre-deployment
      checklist, and the baseline must be captured BEFORE applying or it cannot be checked at all)
- [x] All six x1000 sites are deleted — not made FY-conditional (grep "* 1000")
- [x] The AIP detail page’s 2 headers, 10 cells and 10 inputs agree with each other and with P2-a (divide at render)
- [ ] Typing 250 into an AIP amount and reloading shows the same number it showed before Phase 2
      (⚠️ still unrun — needs a portal login)
- [x] types/budget-planning.ts:908's "like AIP totals" cross-reference is corrected — and it turned
      out to be SIX files, not one: LdipDtos, LdipService, LdipProgramConfiguration,
      AllocationService, types/budget-planning.ts, LdipForm.tsx
- [x] AipXlsmParser converts the workbook's ₱000 into pesos at the import edge — NOT in the ticket,
      and without it every upload writes thousands into a peso column
- [x] A WFP expenditure against a known AIP activity gets the same accept/reject verdict as before
- [x] Deleting an activity's LAST expenditure line takes its Total to 0, not null
- [x] An activity whose lines all sum to zero is written (costed at zero) — distinguished from one
      that never had lines by LineCount alone
- [x] An FY2027 activity keeps its imported totals — the recompute does not zero it, and repeated
      runs stay safe
- [x] Uploading an .xlsm for FY2028 is refused with a message naming the fiscal year — the
      SERVER half (V18-37), now at BOTH import gates rather than confirm alone (V18-38)
- [x] The refusal names the year and what to do instead, and is not Mismatch's "choose the office"
      — advice an importer cannot take, since the workbook decides its offices (V18-38)
- [x] `ParsePreviewAsync` refuses before `_parser.Parse` is reached — asserted by `Times.Never`,
      not by the status code, so a gate placed after the parse would fail this (V18-38)
- [x] The upload control is disabled with its reason visible, not hidden — the user holds
      CanUploadAip and it is the fiscal year that forbids it
      (`Budget_Planning_Dashboard_Requirements.md` §6.1) (V18-38)
- [x] The detail page's Re-upload button is disabled with the same reason rather than linking to a
      page that refuses on arrival (V18-38)
- [x] The break year appears once on the frontend too — `frontend/src` is now scanned by the same
      test, mutation-checked by planting a stray literal and watching it fail (V18-38)
- [x] Uploading an .xlsm for FY2027 still works unchanged, first upload AND re-upload
- [x] An office-owned record cannot be created in a historical year, and an owner-less one cannot
      be created from FY2028 on — both refused with the year named (V18-37)
- [x] Carry-forward and LDIP-seeding into FY2028 are refused BEFORE any row is written — verified
      by asserting AddAsync/SaveChangesAsync are never reached, not by inspecting the result
- [x] Re-uploading cannot carry a record across the boundary: the shape check sits above the
      re-upload branch, which assigns rec.FiscalYear outright.
      ⚠️ **V18-40's test for this passed for the wrong reason and was corrected in V18-38.** It
      seeded a `Manual` record, which replace-import refuses on entry source before it reaches the
      shape guard — so the test stayed green with that guard deleted. Found by mutation, not by
      review. Now seeded as `Upload`, and it asserts the year appears in the message
- [x] An office-owned record refuses a second office's AipOffice child — the shape change reachable
      one node at a time, which no create-path gate can see
- [x] The break year appears on exactly one production code path — asserted by a test that scans
      PPDO.Domain/Application/Infrastructure/Functions and fails the build on a second copy
- [x] Disabling the guards turns exactly the partition tests red and nothing else — re-run per
      guard after V18-38 (confirm alone: 3 red, including the corrected re-upload test)
- [ ] Creating an FY2028 record from the portal UI — **not reachable in Phase 2 and not a defect.**
      The office picker is Phase 3 (AIP entry, §7). Selecting FY2028 in the manual-create form gets
      the server refusal naming the year, which is the honest state of a clean break part-built
- [x] A guest-office user sees only their office's AIP
- [n/a] A query-string officeId is clamped, not 403 — **no AIP read endpoint accepts one.** The
      read surface is `/aip`, `/aip/{id}` and `/aip/{id}/summary`; scope comes from the JWT, so
      there is nothing for a caller to supply and nothing to clamp. Recorded rather than invented
- [ ] A PPDO user's AIP view narrows by division; a guest office's does not
- [ ] A user with office_id null sees empty states, not an error and not everything
- [ ] 1644+ tests green; PermissionMatrixTests still passes Matrix_CoversEveryFlagOnThePermissionService
```

---

## 11. Test focus

| Class | Behaviour |
|---|---|
| `AipServiceTests` | Ownership resolves by `OfficeId`; an `AipOffice` with a null `OfficeId` is visible to nobody but the host office; a `RefCode` that would match by suffix but whose `OfficeId` is a different office resolves to **`OfficeId`** — the FK wins, or the migration achieved nothing |
| **`AipWfpBoundaryTests`** (new — V18-36) | The one place the two documents meet numerically. An explicit accept **and** reject case against a known AIP activity total, asserting the peso figure literally. ⚠️ Assert the number, not the relationship: a test written as `aipBudget == activity.Total * factor` passes whatever `factor` is |
| `AipActivityTotalsRecomputeTests` (Sqlite) | Adding, editing and deleting a child row each recompute the parent; three rows sum per component; deleting the last one leaves `Total == 0`, not null; an activity with no lines is untouched across repeated runs |
| `AipActivityTotalsServiceTests` (Moq) | The two service methods differ only in the `zeroWhenNoLines` flag and pass the right one; no save when the parent was left alone |
| `AipExpenditureTests` | An FY2027 activity with **no** child rows is left untouched by the recompute |
| `OfficeScopeTests` / new `AipScopeTests` | The two-axis rule: PPDO caller narrows by division, guest-office caller does not; a guest office's division id is ignored rather than honoured |
| `AipUploadTests` | FY2028 refused, FY2027 accepted, and the refusal message names the year |
| `PermissionMatrixTests` | No new flag, so no new row. Confirm the build still passes its coverage assertion |

TDD is mandatory for V18-39 (scope resolution — `CLAUDE.md`) and for V18-36, whose whole purpose is
the test. V18-32's backfill is testable only against real data: run it against a restored copy of
production and check the unmatched count before running it for real.
