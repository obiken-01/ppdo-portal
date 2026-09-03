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

4. **PPDO gets an ordinary office record** (tracker B12-b, 2026-08-26). No per-division AIP records,
   no division column on `AipOffice`, and divisions never print. Division of work is carried on the
   **program**, through the existing `ProgramDivision` map, exactly as WFP does.

5. **Office identity is a real FK** (DECISION F, shipped in Phase 1). `AipOffice.OfficeId` →
   `offices.id`. `RefCode` stays as the AIP-side re-link key and the backfill audit trail, the same
   division of labour `ProgramDivision` already uses.

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
| An activity with no expenditures | New activity, nothing costed | Read | `Total == 0`, not null. Null means "never computed", which no longer exists as a state |
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
| `POST /api/budget-planning/aip/upload` | Refuses FY≥2028 (V18-38) |

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
recomputed to zero** — the recompute runs only where a child row exists, or a whole fiscal year is
silently wiped.

### 5.5 FY partition (V18-37)

FY≤2027 → v1.6 shape. FY≥2028 → new shape. **No migration between them**, and no record changes
shape. Gate mechanism is **P2-b**.

### 5.6 Migrations — three, and they are ordered

| Order | Migration | Ticket |
|---|---|---|
| 1 | `AddAipOfficeOwnership` — FK + index + backfill | V18-32 |
| 2 | `AddAipExpenditures` — new table | V18-33 |
| 3 | `ConvertAipAmountsToPesos` — data-only | V18-35 |

⚠️ **CI does not run migrations.** Each needs a manual `dotnet ef database update` against Azure
SQL. `release/1.8.0` already carries two pending from Phase 1 (`AddClimateChangeTypologies`,
`AddEsreCodes`), so v1.8.0 reaches production with **five** manual migrations. Write the order down
in the release checklist; #3 is data-only and must run after the schema is settled.

---

## 6. UI states

Phase 2 adds no screen. Two existing surfaces change.

| Surface | Change |
|---|---|
| **AIP detail page** | ✅ P2-a answered — headers keep `(in ₱000)`, cells divide at render, inputs multiply on save. Display and entry moved together, as they had to. Counts corrected while implementing: **10 cells** (the office-total footer row renders AIP money too) and **10 inputs** (the eleventh grep hit was the import line) |
| **AIP upload** | FY≥2028 refused with a reason naming the fiscal year, not a generic validation error. The button is **disabled with a reason**, not hidden — the user has permission, the *state* forbids it (`Budget_Planning_Dashboard_Requirements.md` §6.1) |

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

- **Three migrations, manually applied, in the §5.6 order.** Five total for v1.8.0.
- **Take a database backup before migration 3.** It is the only one that rewrites existing values
  across every fiscal year. Reversible arithmetically, but a restore is faster than a reasoning
  exercise at 9pm.
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
- [ ] Every scoped AIP read filters on aip_offices.office_id; no RefCode.EndsWith remains on a
      scoping path (grep it)
- [ ] The backfill reports unmatched rows rather than dropping them, and the count is recorded
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
- [ ] An activity with no expenditure rows reports Total 0, not null
- [ ] An FY2027 activity keeps its imported totals — the recompute does not zero it
- [ ] Uploading an .xlsm for FY2028 is refused with a message naming the fiscal year
- [ ] Uploading an .xlsm for FY2027 still works unchanged
- [ ] A guest-office user sees only their office's AIP; a query-string officeId is clamped, not 403
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
| `AipExpenditureTests` | Adding, editing and deleting a child row each recompute the parent; three rows sum correctly; deleting the last one leaves `Total == 0`, not null |
| `AipExpenditureTests` | An FY2027 activity with **no** child rows is left untouched by the recompute |
| `OfficeScopeTests` / new `AipScopeTests` | The two-axis rule: PPDO caller narrows by division, guest-office caller does not; a guest office's division id is ignored rather than honoured |
| `AipUploadTests` | FY2028 refused, FY2027 accepted, and the refusal message names the year |
| `PermissionMatrixTests` | No new flag, so no new row. Confirm the build still passes its coverage assertion |

TDD is mandatory for V18-39 (scope resolution — `CLAUDE.md`) and for V18-36, whose whole purpose is
the test. V18-32's backfill is testable only against real data: run it against a restored copy of
production and check the unmatched count before running it for real.
