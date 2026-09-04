# v1.8.0 Phase 3 — AIP Entry

> **Authoritative spec.** Governed by `docs/SPEC_STANDARD.md`. Written 2026-09-03.
>
> Read first: `docs/v1.8/Phase_Plan.md` §5 (the work items this expands), §12.1 (the AIP↔WFP
> seam), §12.3 (ceilings), §12.5a (the kanban), §12.6 + §12.6a (the workflow, and the sub-office
> group), §12.7 · `docs/v1.8/AIP_Foundation_Spec.md` (Phase 2, which this builds on) ·
> `docs/v1.8/Permission_Matrix.md` · `docs/PERFORMANCE_GUIDELINES.md` · `docs/DESIGN_SYSTEM.md` ·
> `docs/NAMING_CONVENTIONS.md`.
>
> Phase 2 is complete and merged to `release/1.8.0`. Nothing here waits on it.
>
> ℹ️ **Filename note.** `SPEC_STANDARD.md` §1 names authoritative docs `<Feature>_Requirements.md`.
> This folder's two existing authoritative specs are `AIP_Foundation_Spec.md` and
> `AIP_Form_Spec.md`, so this one follows the local convention instead. The standard governs the
> *contents*; the deviation is the filename only, and is deliberate.

---

## 1. Goal

**FY2028 is currently impossible to create.** V18-38 froze the `.xlsm` importer at FY2027, and the
office-owned record shape it froze in favour of has no screen behind it. Phase 2 built the
structure and shipped no feature, deliberately; this is the phase where an office can actually
build its AIP.

An encoder picks programs from their office's LDIP, groups them under a sub-office heading, adds
projects and activities, and composes each activity's cost out of expenditure lines. When the
office is finished, one action submits the whole office's work for department review — and that
submit is the only place the ceiling is enforced.

---

## 2. Decisions (settled)

Every decision below is already recorded in `Phase_Plan.md` or the open-items tracker. It is
repeated here with its reasoning so a ticket does not have to reconstruct it, and so the two that
have already flipped are not flipped back.

1. **Programs come from the LDIP, which is a closed list** (open question #5, 2026-08-25). An
   office cannot add a program the LDIP does not contain. There is therefore **no "propose a new
   program" path and no approval flow for one** — a branch the original plan anticipated and that
   does not exist.

2. **Entry is three stages, not two** (2026-08-26). The plan said project/activity then
   expenditures; encoders must also create the **sub-office group**, and it is entered *with* the
   program rather than as a separate step. `LdipForm.tsx` (RAL-61) already implements exactly this
   interaction and is to be **lifted, not redesigned** (§12.6a).

3. **The sub-office group is not the division.** Both attach at program level and they are
   orthogonal. This is the most confusable pair in the phase, so it is tabulated in §3.1 rather
   than described. Getting it wrong produces a document that is wrong in a way no unit test
   reaches, because the group **prints** and the division never does.

4. **One funding source per expenditure line**, with multi-fund expressed as several lines
   (Phase 2 decision 4). The UI defaults to single (whiteboard W8) — the toggle exists so the
   multi-fund case is *possible*, not so every encoder meets it.

5. **The ceiling is validated at submit, never during entry** (DECISION C). Over-ceiling encoding
   is allowed and expected. V18-49's checklist *is* the ceiling gate; built as a dismissible
   summary, there is no ceiling enforcement anywhere in the system.

6. **The ceiling check sums `mooe + co`, General Fund only, PS exempt, on rounded base figures.**
   ↩️ DECISION H (all-fund ceilings) was adopted 2026-08-25 and **withdrawn 2026-08-26**. This has
   now flipped twice — §12.3 is the authority, and it must not be re-derived from meeting notes.
   The +30% uplift of DECISION G is **presentation-only** and is not part of the comparison
   (tracker G3).

7. **A ceiling cut is non-destructive** (A5-b, 2026-08-26). Encoded work stands, nothing is flagged
   or deleted, and the office learns at submit. No cascade, and no confirmation dialog beyond the
   ordinary one.

8. **AIP gets its own reservation ledger, and no netting mechanism** (DECISION A, reduced
   2026-08-26). `AipDivisionAllocationLedger` mirrors the WFP ledger rather than generalising it.
   The relief rule stays written down and unbuilt — §2.1 is what makes that safe.

9. **`WfpCeilingService` gets a zero diff.** The plan once proposed retiring its allocation check
   for FY2028+; reversed 2026-08-26. The check lives in four already-FY-parameterised methods, so
   "retire for FY2028+" would **add four conditionals rather than delete code**, and it is the only
   fund-scoped check in the system (its own header: step 1 is aggregate across funding sources,
   only step 2 is per-fund).

10. **Workflow status lives on `AipRecord`.** Submit is "the whole office's work", and under the
    office-owned shape one record *is* one office — so the record is the natural carrier. A direct
    consequence of V18-40, and not available before it.

11. **Ref codes are allocated server-side, scoped to the parent, and retried on conflict.**
    Segments 1–5 are office identity and are **not generated at all**; the job is allocating a
    sibling-unique `seq`. Format pinned to DBM Budget Operations Manual for LGUs, 2023 Ed.,
    Figure 4 + Annexes C/D.

    ↩️ **Revised 2026-09-04 during V18-44, per `SPEC_STANDARD.md` §3.** This originally read
    *"generated server-side, **in SQL**"*, on the assumption that the database had to serialise the
    allocation. Reading the code showed that assumption was already satisfied by something else:
    **unique indexes on `(ParentId, RefCode)` exist at all three levels**, so a duplicate was
    never writable. Generation also already existed (`AipService.NextRefCode`), and the sibling
    queries were already parent-scoped in SQL — so the plan's `GeneratePRNoAsync` full-table-scan
    warning did not apply either.

    What was actually broken was the **gap** between generation and the index: load siblings →
    compute → insert, with nothing in between, so a losing racer was rejected by the index and
    surfaced as an **unhandled exception and a 500**. Moving the computation into SQL would have
    duplicated a guarantee the index already gives, cost the readable C# generator and its direct
    testability, and still not have decided what the loser should see. The fix is
    `RefCodeAllocator`: re-read the siblings and re-attempt, bounded at 3, returning a 409 on
    exhaustion. The index stays the authority; it simply stops being a crash.

12. **No new permission flags.** Encoder is `CanAccessBudgetPlanning`; department-head reviewer is
    `CanReviewBudgetPlanning` (PPDO-3); the cross-office bypass is `CanReviewAllOffices` (PPDO-5).
    All three shipped in Phase 1. Phase 3 consumes them and adds none.

### 2.1 Why deferring the netting rule is safe — read before "simplifying" it

The AIP row is a **reservation** the WFP **relieves** as it commits. The two ledgers must net, not
add:

```
allocation consumed = WFP committed + AIP reserved not yet converted
```

⚠️ **Relief must be per ACTIVITY, not per fund.** Per-fund relief strands reservations whenever the
fund mix changes: ₱6M reserved as GF, later detailed as ₱4M GF + ₱2M GAD, leaves ₱2M of stale GF
reserved forever.

**None of that is built in Phase 3, and that is correct**, because V18-81 blocks FY2028+ WFP
creation in this system. With no FY2028 WFP here, there is nothing to net against. **V18-81 must
land before V18-45** — without it the reduced ledger leaves an open correctness question rather
than a closed one. The rule is recorded here and in the ledger's own class remarks so that whoever
builds it later does not re-derive the per-fund version.

⚠️ **One gap this leaves, given General-Fund-only ceilings:** an activity **planned under an
unchecked fund and detailed under GF** consumes GF allocation with no AIP reservation behind it.
Known, accepted, and worth re-reading when netting is built.

### Open — must be answered before the ticket that needs it

| # | Question | Blocks | Default if unanswered |
|---|---|---|---|
| **P3-a** | **How far do AIP procurement lines go?** Full WFP treatment (presets, duplicate warnings, quantity × unit price) or item selection plus a cost? Tracker **W13-b** | V18-80 | Item selection + cost. The AIP is a *plan*; the arithmetic and presets exist in WFP because it is a *schedule*. Building the full treatment speculatively is the larger and less reversible mistake |
| **P3-b** | **Does Phase 3 close the `ProgramDivision` program-half FK?** PPDO-1 shipped the office side only; a **program** ref-code change still silently detaches its division assignment — and Phase 3 makes that assignment load-bearing for what a PPDO user can see | V18-42 (PPDO visibility) | **Yes, and this is the moment.** See §5.4 — the reason the program side stayed a string *lapses* for FY2028+ |
| **P3-c** | **Two encoders in one office editing at once** (tracker D5 confirms two or more per office). Optimistic concurrency per node, or last-write-wins? | V18-42 | Optimistic concurrency **per node**, surfaced as "this activity was changed by someone else — reload". Last-write-wins on a shared document loses an encoder's work with no signal, which is the failure nobody reports because nobody notices it happened |
| **P3-d** | **Ref-code segment meanings and reset points.** The format is confirmed; the meanings are not, and segment count varies with depth. Tracker **B9-b** | **Not Phase 3** — V18-76 | V18-44 needs neither: allocating a sibling-unique `seq` is independent of what the segments mean. Recorded here because this is where someone will first want the answer |

**P3-a and P3-b are the two worth asking about now.** P3-c is an engineering call with an obvious
answer; P3-d blocks nothing in this phase.

---

## 3. Behaviour

### 3.1 The two program-level axes — the table this phase exists to keep straight

| | Sub-office group | Division |
|---|---|---|
| **Stored on** | `AipOffice` — the `(Sector, Name)` pair | `ProgramDivision` → `DivisionId` |
| **Printed on the AIP form?** | **Yes** — it is an office row, with its own shaded subtotal | **No** — never appears |
| **Applies to** | every office | **the host office (PPDO) only** |
| **Cardinality** | a program sits in exactly one group | a program may be assigned to several divisions |
| **Purpose** | how the document is *structured* | how the work is *divided* |
| **Who creates it** | the encoder, while adding a program | PPDO, on the Allocation page's PPA→Division tab |

Real example from the province's FY2027 file: three `3000-000-1-01-001` office rows on the SOCIAL
sheet — `OFFICE OF THE GOVERNOR - WARDEN`, `- AKAP-HUB`, `- HOUSING` — each heading its own block
of programs.

### 3.2 Core

| Case | Given | When | Then |
|---|---|---|---|
| Happy path — build | An FY2028 office-owned record, Draft, with LDIP-seeded programs | Encoder adds a sub-office group, program, project, activity and one expenditure line | Each node gets a server-generated sibling-unique ref code; the activity's `Ps/Mooe/Co/Total` are recomputed and **stored** from its lines |
| Happy path — submit | Every activity has ≥1 line, totals > 0, CC and eSRE present, GF `mooe + co` within ceiling | Encoder submits | Record moves Draft → Department review. The whole office moves in one action |
| Programs are a closed list | An office whose LDIP has 4 programs | Encoder opens the program picker | Exactly those 4, and no free-text name field anywhere on the page |
| Empty LDIP | An office with no LDIP for the sector | Encoder opens the program picker | Empty state naming the LDIP as the prerequisite — **not** a blank picker or a spinner |
| First record | An office with no FY2028 AIP | Encoder opens AIP Entry | Offered creation of the office-owned record for their own office; no office picker for a guest-office user |
| New sub-office group | Encoder types a name not in the suggestions | Program is added | A new group starts under the same office ref code; program numbering **continues across groups**, it does not restart |
| Group removal renumbers | Three programs across two groups; the middle one is removed | Removal saves | Numbering closes the gap — no holes |
| Concurrent edit | Two encoders in one office, same activity | Both save | Second save is refused with "changed by someone else"; nothing is silently overwritten (P3-c default) |
| Concurrent **create** | Two encoders adding an activity under one project at the same moment | Both save | ✅ **Both succeed**, with distinct sibling-unique codes (V18-44). The loser re-reads and takes the next code — it is not asked to retry by hand |
| Create loses repeatedly | Sustained contention under one parent | Third attempt also loses | **409**, naming the node type. Not a 500, and not an unbounded retry holding the request open |
| Activity with no lines | An activity created but never costed | Any recompute | **Untouched.** `LineCount` 0 and `Total` null — never costed |
| Activity whose lines were all deleted | It had lines; the last is removed | Recompute after delete | `Total == 0`, not null — costed at zero. **Same `LineCount`, opposite meaning** from the row above |
| Multi-fund off by default | A new expenditure line | Encoder opens the form | One fund field. The toggle reveals per-line fund selection; each line still carries exactly one fund |
| Over-ceiling encoding | Office ₱2M over its GF ceiling | Encoder keeps entering | **Allowed.** No block, no dialog that stops work — the gate is submit |
| Ceiling cut mid-encoding | PBO cuts the ceiling below what is encoded | Encoder reloads | Work stands, nothing flagged or deleted; `remaining` shows a **negative** figure |
| Submit blocked by ceiling | GF `mooe + co` exceeds the ceiling | Encoder submits | Refused, naming the fund, the ceiling, the encoded total and the overage. Record stays Draft |
| Submit blocked by completeness | One activity has no expenditure lines | Encoder submits | Refused, naming the activity and what is missing. Record stays Draft |
| PS does not count | An office at its GF ceiling on `mooe + co`, with large PS | Encoder submits | **Passes.** PS is exempt as an expense class |
| Uplift is not in the check | An office exactly at its ceiling | Encoder submits | Passes — and the printed form will read 30% over. ⚠️ **Intended** (DECISION G); Phase 5's form spec must say so |
| Guest office, no divisions | A guest office with a ceiling and no division rows | Submit runs the ceiling check | Checked at **office** level. No synthetic division row is created |
| Blank ceiling ≠ unlimited | A fund with no allocation row | Submit runs the check | Non-GF funds are excluded **by an explicit rule**. A blank GF row means **zero**, not unlimited (`GetDivisionAllocationAsync` → `0m`) |
| FY2027 unchanged | An FY2027 legacy record | Opened | Renders in the v1.6 shape. **No entry flow, no submit, no ledger** — Phase 3 is FY2028+ only |
| FY2028 WFP refused | Any office | WFP creation for FY2028 | Refused as "not supported yet", naming the year (V18-81) |

### 3.3 Permission and scope

Every row is already pinned by `docs/v1.8/Permission_Matrix.md`. Phase 3 adds no flag; it adds
**call sites** that must use the existing resolvers.

| Account | Given | When | Then |
|---|---|---|---|
| Guest-office encoder | `CanAccessBudgetPlanning`, own office | Opens AIP Entry | Their own office's record only. **Division is not a factor** for them |
| Guest-office encoder supplies another `officeId` | Query string | Read | **Clamped** to their own office — their data back, not a 403 that confirms the other office exists |
| Guest-office encoder targets another office's node | A node id they do not own | Write | **`NotFound`**, not `Forbidden` (PPDO-46). Clamping is not available on a write: a write names one node, and redirecting it would write to the wrong row |
| PPDO (host) encoder | Division D | Reads their office's AIP | Only programs assigned to **division D** via `ProgramDivision`. ⚠️ **Host office only** — this filter must not apply to guest offices |
| PPDO encoder | Division D | Reads a **guest** office's AIP | Every program in it. PPDO's internal division of labour says nothing about GSO's programs |
| Department-head reviewer | `CanReviewBudgetPlanning`, own office | Record in Department review | **May edit values directly**, not only comment (tracker B3). The reviewer write-denial does not apply to this role |
| PPDO consolidated reviewer | `CanReviewAllOffices` | Any office's record | Read-only, via `OfficeScope.ResolveForReview` — **never** `Resolve`. Comment-only; `ReviewerWriteGuard` denies writes |
| Any user, `office_id` null | Unassigned | Any read | `OfficeScope.NoOffice` (id 0) → sees nothing. Empty states, not an error (DECISION F) |
| SuperAdmin | Resolves every flag true | Any write | **Exempt from the subtractive reviewer guard** — a naive guard locks SuperAdmin out of every budget-planning write |

⚠️ **PPDO runs the same ladder as everyone else** (tracker B12). PPDO's divisions submit to a **PPDO
department-head reviewer**, distinct from the PPDO consolidated reviewer. PPDO's record is an
**ordinary office record** — no per-division records, no division column on `AipOffice`, and
divisions never print.

---

## 4. API contract

All routes are **JWT-protected**; none appears on `CLAUDE.md`'s public list. Envelope is
`ApiResponse<T>` (`{ data, error, message }`); services return `ServiceResult<T>`.

| Endpoint | Gate | Notes |
|---|---|---|
| `POST /api/budget-planning/aip/{aipId}/programs` | `CanAccessBudgetPlanning` | Adds a program from the LDIP **plus its sub-office group**, in one call — they are one interaction (decision 2) |
| `POST /api/budget-planning/aip/projects` | `CanAccessBudgetPlanning` | Ref code generated server-side |
| `POST /api/budget-planning/aip/activities` | `CanAccessBudgetPlanning` | Ref code generated server-side |
| `POST /api/budget-planning/aip/activities/{activityId}/expenditures` | `CanAccessBudgetPlanning` | Triggers the V18-34 recompute |
| `PUT /api/budget-planning/aip/expenditures/{id}` | `CanAccessBudgetPlanning` | Recompute |
| `DELETE /api/budget-planning/aip/expenditures/{id}` | `CanAccessBudgetPlanning` | Recompute — to `0`, not null, if it was the last line |
| `GET /api/budget-planning/aip/{aipId}/readiness` | `CanAccessBudgetPlanning` | The submit checklist's state, so the UI can show it **before** the user presses submit |
| `POST /api/budget-planning/aip/{aipId}/submit` | `CanAccessBudgetPlanning` | Draft → Department review. Runs V18-49's checks |
| `GET /api/budget-planning/aip/{aipId}/ceiling` | `CanAccessBudgetPlanning` | Exposes `remaining`, which **may be negative** |

### Error shapes

| Case | Status | Shape |
|---|---|---|
| Node belongs to another office | **404** | `"AIP activity {id} not found."` — byte-identical to a node that does not exist (PPDO-46) |
| Record not Draft | 400 | Names the current state and who holds it |
| Fiscal year is legacy | 400 | `AipShape.Mismatch` — names the year and the shape that year takes |
| Submit fails completeness | 400 | **A list**, one entry per failing activity, each naming the node and what is missing — not a single sentence |
| Submit fails ceiling | 400 | Names the fund, ceiling, encoded total and overage. **Not** "over ceiling" |
| Concurrent edit lost the race | 409 | `"This activity was changed by someone else. Reload to see the current version."` |
| Ref-code allocation lost 3 races | 409 | `"Another activity was added to this project at the same moment. Please try again."` — per node type (V18-44) |

⚠️ **List endpoints return slim DTOs.** The AIP detail response once produced a **1.2 MB** payload.
The entry page's tree must not ship free-text fields a grid never renders, and any list that grows
with the record paginates server-side (`docs/PERFORMANCE_GUIDELINES.md`).

---

## 5. Data model changes

### 5.1 `aip_records` — workflow status (V18-49)

```
ALTER TABLE aip_records ADD workflow_status NVARCHAR(30) NOT NULL DEFAULT 'Draft'
CREATE INDEX IX_aip_records_workflow_status ON aip_records(office_id, fiscal_year, workflow_status)
```

**Distinct from the existing `status` column**, which is `PlanningStatus` (`Draft` / `Final` /
`Archived`) and is shared with LDIP and WFP. The workflow has five states that vocabulary cannot
express, and overloading it would change LDIP's and WFP's meaning too.

⚠️ **Introduce all five states in one migration, implement only the first transition.** The states
are settled (§12.6) — `Draft`, `DepartmentReview`, `SubmittedToPpdo`, `ReturnedByPpdo`,
`Consolidated`. Adding them piecemeal means a second migration on the same column and an interval
where the column's domain does not match the documented workflow. Phase 3 writes only
`Draft → DepartmentReview`; Phase 4 uses the rest.

Migration: `AddAipWorkflowStatus`. snake_case, per `NAMING_CONVENTIONS.md` — `aip_records` is a
v1.6 table and already snake_case.

### 5.2 `aip_division_allocation_ledger` — new table (V18-45)

Mirrors `wfp_division_allocation_ledger` in shape. Reservation rows are keyed **per activity** —
which is what makes the deferred relief rule implementable later without a second migration (§2.1).

⚠️ **`remaining` is computed, never stored clamped.** No `Math.Max(0, …)` in the table, the query,
the DTO or the UI.

Migration: `AddAipDivisionAllocationLedger`.

### 5.3 Sub-office group — no new column expected

The group is the `(Sector, Name)` pair on the existing `AipOffice`, which **already stores both**.
Confirm this before adding anything — the LDIP side stores it the same way, and a new column here
would be a second source of truth for a value that prints.

### 5.4 ⚠️ `ProgramDivision` — the half-finished FK (P3-b)

**PPDO-1 shipped the office side only.** The program side is still keyed on `ProgramRefCode`
(a string), so a program ref-code change silently detaches its division assignment — and Phase 3
makes that assignment load-bearing for what a PPDO user can see. The failure looks like missing
data, not an error.

**The reason it stayed a string lapses here.** PPDO-1's reasoning was that `aip_programs` rows have
no identity surviving a re-upload, because `ReplaceImportAsync` deletes and recreates the subtree
with fresh surrogate IDs. **FY2028+ has no upload** — V18-38 froze it. So for the fiscal years this
phase serves, a durable program identity exists for the first time.

That does not make it free: FY≤2027 stays re-uploadable, so any FK must tolerate the legacy path.
**This is P3-b, and it is the one open question that could change V18-42's shape.**

### 5.5 Migration order

Both are additive and independent; apply in ticket order. **CI does not run migrations** — §8.

---

## 6. UI states

Two new surfaces, one existing one changed. Flat design, PPDO tokens, `slate-800` headings /
`slate-600` body, **never `text-slate-700`**.

### 6.1 AIP Entry (new page, V18-42)

⚠️ **V18-83 splits the sidebar into "AIP Entry" and "AIP Review" as two siblings** under Budget
Planning — not a third nesting level (`Sidebar.tsx` has one collapsible level and no nesting
primitive; WFP already sets the flat-item-to-sub-page precedent). Build against that split.

| State | Content |
|---|---|
| **Loading** | Skeleton matching the loaded tree — same header, same row heights. **Not** a centered spinner replaced by a full-height table (CLS) |
| **Empty — no record** | "No FY2028 AIP for this office yet" + create action |
| **Empty — no LDIP** | Names the LDIP as the prerequisite and links to it. A **different** empty state from the one above; do not collapse them |
| **Empty — programs seeded, nothing built** | The Not Started case. Prompts adding the first project |
| **Error** | Failed fetch shows a retry; a rejected save **keeps the user's input** — never clears the form |
| **Success** | Toast via `useToast`; the tree updates in place |
| **Read-only** | Record past Draft: controls **disabled with a reason naming the state and who holds it**, not hidden. The user has permission; the state forbids it (`Budget_Planning_Dashboard_Requirements.md` §6.1) |
| **Validation** | Per-field, under the field. Amounts via `MoneyInput` |
| **Conflict** | The 409 renders as an inline banner on the affected node with a reload action — not a toast that scrolls away |

Reuse `MoneyInput`, `ConfirmDialog`, `RowActions`, `useToast`, `OfficeSelect`, `InfoTip`.

### 6.2 Submit checklist (V18-49)

Shown **before** submit via the readiness endpoint, so the office can fix things without guessing.
Each failing item names the node and links to it. The ceiling row shows ceiling, encoded total and
**the signed difference** — negative when over.

⚠️ **This is a gate, not a summary.** There is no "submit anyway".

### 6.3 Allocation page (V18-48 — picker already shipped)

PPDO-17 shipped the office picker, the cross-office flag, and the role-based re-labelling
(`budget-planning-labels.ts` is the single source for sidebar, breadcrumb, header and tab). What
remains is the ceiling **management** experience.

⚠️ **Show the negative remaining.** After a cut, a clamped zero hides the only signal the office
has that it must revise.

⚠️ PPDO-17's work was `tsc`/lint clean but **never browser-verified**. A manual pass as a PBO-only
caller belongs in V18-48's test plan.

### 6.4 ⚠️ Extract before building

`aip/detail/page.tsx` is **2,057 lines — the largest file in the repo**, and
`docs/v1.8/RETROSPECTIVE.md` says to extract before redesigning. **Extraction and new entry UI in
one commit is not reviewable** — the same warning V18-35 carried, for the same reason. Either
extract first in its own PR, or build Entry as a genuinely separate page and leave detail alone.

---

## 7. Non-goals

- **Review, return and consolidation** — Phase 4. Phase 3 stops at the *first* submit; there are
  **two** (encoder → department head → PPDO).
- **The printable AIP form** — Phase 5. The +30% uplift renders there, not here.
- **The AIP↔WFP netting mechanism** — deferred, safely, by V18-81 (§2.1).
- **Offline entry** — Phase 6. ⚠️ Offline clients **cannot mint ref codes safely**; that is a hard
  constraint V18-44 should record where the generator lives.
- **Amendment after approval** — V18-73, and the terminal authority is the **SP resolution**, not
  the LFC.
- **Retiring or changing `WfpCeilingService`** — decision 9. Zero diff.
- **Re-importing FY2027 faithfully** — out of scope permanently; V18-38 froze the importer, so
  FY2027's pre-RAL-238 import is now permanent.
- **Indexed ref-code columns** — V18-76, blocked on B9-b.

---

## 8. Deployment notes

- **Two migrations** (§5.1, §5.2), plus a third if P3-b is answered yes. ⚠️ **CI does not run EF
  migrations** — each needs a manual `dotnet ef database update` against Azure SQL.
- **These add to the release's pending count.** `docs/v1.8/Pre_Deployment_Checklist.md` §1 was 15
  at the end of Phase 2 and **is the authority** — it is rechecked against `git diff` at release
  time. Do not quote a count from a planning doc; that has already drifted twice.
- **v1.8.0 deploys when most of the release is implemented, not per phase** (decided 2026-09-03).
  `release/1.8.0` accumulates; production stays on v1.7.4 meanwhile.
- Both migrations are **additive**. Neither rewrites existing values — unlike Phase 2's
  `MigrateAipAmountsToPesos`, which remains the release's only destructive one.
- No new dependency, environment variable, or CORS origin. No new Azure resource; if one is ever
  added, **Southeast Asia** (RAL-237).

---

## 9. Ticket split

Epic **PPDO-48**. Blocking relations are wired in Linear, not only described here.

| Ticket | # | Size | Blocked by |
|---|---|---|---|
| PPDO-49 | V18-81 — block FY2028+ WFP creation | S | — |
| PPDO-50 | V18-44 — ref-code generation | M | — |
| PPDO-51 | V18-41 — programs from a valid LDIP | S | — |
| PPDO-52 | V18-42 — three-stage entry UI | **L** | PPDO-50, PPDO-51 |
| PPDO-53 | V18-43 — multi-fund toggle | S | PPDO-52 |
| PPDO-54 | V18-80 — procurement lines from the Price Index | M | PPDO-52 · **P3-a** |
| PPDO-55 | V18-45 — reservation ledger | M | PPDO-49 |
| PPDO-56 | V18-46 — ceiling service | M | PPDO-55 |
| PPDO-57 | V18-47 — office-level ceiling checks | S | PPDO-56 |
| PPDO-58 | V18-48 — PBO ceiling management UI | S | — |
| PPDO-59 | V18-49 — completeness checklist + submit gate | M | PPDO-52, PPDO-56 |

**Order:** `49 + 50 + 51` in parallel → `52` → `53`, `54` · `55` → `56` → `57` · then `59` · `58`
anytime.

**Manual-implementation candidates** (per CLAUDE.md): **PPDO-49** and **PPDO-57** — small blast
radius, `dotnet test` feedback with no app running, reversible, and each has a sibling to
pattern-match (`AipShape.RefuseUpload` for 49; `AipReadScope`'s host-office branch for 57).
**PPDO-56 is explicitly not** — a ceiling check with four named traps is exactly where a wrong
choice compiles cleanly and produces a wrong number. Nor is **PPDO-52**, a 1,000+ line page
component.

---

## 10. Acceptance checklist

- [ ] A guest-office encoder opening AIP Entry sees only their own office, with no office picker
- [ ] A guest-office encoder passing another office's `officeId` gets **their own** data back, not a 403
- [ ] A guest-office encoder targeting another office's activity id gets **404**, worded identically to a nonexistent id
- [ ] A PPDO encoder in division D sees only programs assigned to division D
- [ ] That same PPDO encoder, opening a **guest** office's AIP, sees every program in it
- [ ] The program picker lists exactly the office's LDIP programs, and there is no free-text program-name field on the page
- [ ] An office with no LDIP sees an empty state naming the LDIP — not a blank picker
- [ ] Typing a new sub-office group name starts a new group; program numbering continues across groups rather than restarting
- [ ] Removing a middle program renumbers without leaving a gap
- [ ] Two browsers editing the same activity: the second save shows "changed by someone else", and the first encoder's value survives
- [ ] Deleting an activity's last expenditure line leaves `Total` **0**; an activity that never had lines still shows **no** total
- [ ] Entering ₱2M over the GF ceiling is **allowed** while encoding, with no blocking dialog
- [ ] Submitting over the GF ceiling is refused, and the message names the fund, ceiling, total and overage
- [ ] An office at its ceiling on `mooe + co` with large PS **submits successfully**
- [ ] Submitting with one uncosted activity is refused, and the message names that activity
- [ ] After PBO cuts a ceiling below encoded work, the encoder sees a **negative** remaining, and nothing has been deleted or flagged
- [ ] A guest office with no divisions passes the ceiling check at office level, and no division row was created for it
- [ ] A successful submit moves the record to Department review and the encoder's controls become disabled **with a reason naming the state**
- [ ] Creating a WFP for FY2028 is refused with a message naming the year; FY2027 WFP creation is unchanged
- [ ] Opening an FY2027 AIP still renders the v1.6 shape with no entry flow
- [ ] First load of AIP Entry shows a skeleton matching the tree, not a spinner replaced by a table
- [ ] `dotnet test` passes; `tsc` and `eslint` clean

---

## 11. Test focus

| Class | Cover |
|---|---|
| `AipEntryServiceTests` (new) | The three-stage create path; sibling-unique ref codes under concurrent creates; program numbering across groups and after removal |
| `AipCeilingServiceTests` (new) | **One test per trap, each named**: GF-only, PS exempt, rounded figures, base-not-uplifted. Plus blank-row-means-zero, and the office-level (division-less) shape |
| `AipSubmitGateTests` (new) | Each failing condition **individually**, not just the happy path; never-costed vs costed-at-zero; the over-ceiling refusal's wording |
| `AipReadScopeTests` (extend) | The new entry call sites — guest clamp, host-office division filter, and that the division filter does **not** reach guest offices |
| `AipShapeTests` (extend) | V18-81's WFP refusal reads the one break-year constant; the scan still passes with no new `2028` literal |
| `AipActivityTotalsServiceTests` (extend) | Recompute fires on expenditure add/edit/delete from the new entry endpoints |
| `PermissionMatrixTests` | Unchanged — Phase 3 adds no flag. If it fails, a flag was added and needs a matrix row |

⚠️ **Verify the guards by mutation, not by assertion count.** Phase 2 found a V18-37 test that
passed with its guard deleted — it seeded a record that was refused earlier for an unrelated
reason, so it asserted the right status code for the wrong cause. Disable each new guard and
confirm exactly the intended tests go red.

---

*`docs/v1.8/AIP_Entry_Spec.md` — v1.8.0 Phase 3 — written 2026-09-03 — Ralph Armand Alcaide*
