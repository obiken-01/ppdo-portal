# v1.8.0 — Division heads submit to the department head (PPDO-130)

> **Status: accepted** — 2026-09-28. Parent: PPDO-122 (Demo 2 change requests), item 2.8.
> Written to `docs/SPEC_STANDARD.md`. All decisions are Ralph's (session 2026-09-28): 1–8 and 12
> chosen directly; 9–11 proposed and accepted, with 11 revised to the simpler "PPDO return reopens
> every division"; 13 (ceiling) added on review and extended to offices without divisions.
>
> ⚠️ **This reverses part of `AIP_Review_Spec.md` decision 4** (PPDO-70, 2026-09-08: "the lock falls
> at `SubmittedToPpdo`, not at the first submit"). For an office **with divisions**, a division's
> work now locks for that division's encoders when they submit it. Offices without divisions keep
> decision 4 exactly as it is.
>
> ⚠️ **It also relaxes `AIP_Review_Spec.md` §6.2 ("there is no submit anyway") for the ceiling, in
> every office** (decision 13). Being over the ceiling becomes a warning at the submit to the
> department head, and stays a hard block at the send to PPDO. Completeness issues still block every
> submit.

---

## 1. Goal

Today an office's AIP moves as one unit: any encoder submits the whole office to department review,
and nothing locks until the department head sends it to PPDO. Offices are organised into divisions,
and each division head builds their own division's share of the AIP. This change lets **each division
head submit their own division's activities to the department head**. Once they submit, **they can no
longer edit that work**. The department head can **return one division's work** without disturbing
the others. The office goes to PPDO only once every division has submitted. Requested at Demo 2
(item 2.8) because the department head needs to know which divisions are finished, and a division
needs to know its submitted work cannot be changed under it.

---

## 2. Decisions (settled)

1. **An activity belongs to a division via a new tag on the activity** (Ralph, 2026-09-28) —
   `aip_activities.division_id`. Not inferred from `ProgramDivision`: a program can be assigned to
   several divisions (`IX_program_divisions_office_program` is deliberately non-unique), and the
   question "which division's work is this activity" has no answer at program grain. The tag answers
   it at the only grain that is unambiguous.
2. **A submitted division is locked for its encoders; the department head can still edit it** (Ralph,
   2026-09-28). This keeps `AIP_Review_Spec.md` decision 4's reasoning — the department head fixes
   minor details during review rather than returning work for every typo — while taking the ability to
   edit away from the division that submitted it.
3. **Return is per division; the office goes to PPDO only when every division has submitted** (Ralph,
   2026-09-28). Returning Division A reopens only Division A. `SubmitToPpdo` is refused while any
   division with activities is still in Draft.
4. **Offices with no divisions keep today's single-hop flow unchanged** (Ralph, 2026-09-28). An office
   with zero active divisions submits, returns and locks exactly as it does now, **except the
   ceiling rule in decision 13, which applies to every office**. An office with divisions
   uses the division flow, and **every activity must be tagged before any division can submit** — the
   readiness panel names the untagged ones.
5. **A new activity is tagged with its creator's division; only the department head can re-tag**
   (Ralph, 2026-09-28). For an encoder, the server sets `division_id = caller.DivisionId` and ignores
   any value the client sends. The department head (and Admin/SuperAdmin) chooses the division for
   activities they create, and can move any activity to any active division of the office — this is
   how misfiled work is corrected.
6. **Division heads see the whole office tree; they edit only their own division's activities**
   (Ralph, 2026-09-28). Other divisions' activities render read-only. Office totals and the ceiling
   bar stay office-wide, as today — hiding rows would leave numbers nobody can trace.
7. **Ceiling / allocation attribution does NOT change here** (Ralph, 2026-09-28).
   `AipCeilingService.ResolveDivisionIdAsync` keeps attributing through `ProgramDivision` (lowest
   division id). Moving the host-office ledger to the activity's tag is a **separate follow-up
   ticket** (§2 Open follow-ups): it changes allocation figures and deserves its own review.
8. **Target: v1.8.0, landed on `release/1.8.0` before go-live** (Ralph, 2026-09-28). The
   pre-deployment checklist grows by the migration in §5.

### Workflow details

9. **The office's `workflow_status` is kept and derived from its divisions.** In an office
   with divisions it stays `Draft` while any division is still working, and **moves to
   `DepartmentReview` automatically when the last division with activities submits**. Why: the kanban,
   the readiness board, notification counts, the review search and `SubmitToPpdoAsync` all key on
   `DepartmentReview` today. Keeping the existing state means none of them change. The per-division
   state lives in a new table (§5) instead of new office states, so `AipWorkflowStatus.All` and the
   "closed is the safe default" rule are untouched.
10. **Which divisions must submit: those with ≥ 1 activity in this office for this AIP
    record.** A division with nothing tagged has nothing to submit and does not block the office.
    Division membership of the activity decides this — not division membership of users.
11. **A PPDO return reopens every division** (Ralph, 2026-09-28 — chosen as the less complicated
    option). The office moves to `ReturnedByPpdo`, as today, and every division row goes back to
    `Draft`. Divisions then resubmit exactly as in the first round. When the last one does, the office
    moves to `DepartmentReview` (decision 9), and the department head sends it to PPDO again. Why:
    this re-uses the first-round path with no special cases. The alternative (divisions stay locked,
    and the department head returns them selectively) needed extra rules for how the office state
    moves when a division is returned out of `ReturnedByPpdo`. The cost is accepted: every division
    must press submit again, including ones PPDO had no comment on.
12. **FY2028+ only.** The division flow applies to AIP records with
    `FiscalYear >= AipFiscalYears.FirstEnteredFiscalYear`. FY ≤ 2027 records have no entry flow to
    change — clean fiscal-year break (CLAUDE.md, v1.8.0).
13. **The ceiling is checked at every submit, and only blocks the send to PPDO — in every office**
    (Ralph, 2026-09-28). Both a division submit and, in an office without divisions, the encoder's
    whole-office submit run the office-wide ceiling check. When the office is over its ceiling, the
    submit **still succeeds, with a warning** that names the overage. `SubmitToPpdo`
    keeps refusing while the office is over its ceiling, as it does today. Why: division heads find
    out about the overage while there is time to fix it, and the department head, who owns the
    whole-office total, is the one it finally gates. One rule for every office, so an encoder does
    not meet a different ceiling behaviour depending on whether their office has divisions.
    **Readiness:** the ceiling issue moves out of the blocking list for the department-head submit.
    `AipReadinessDto` gains `CeilingWarning` (string or null); `CanSubmit` no longer turns false on
    the ceiling alone. A separate `CanSubmitToPpdo` (or the existing re-run in `SubmitToPpdoAsync`)
    keeps it blocking at the PPDO step.

Further rules implied by the above (not separate choices):

- **Program and project nodes (containers).** In a divisioned office, a division encoder may rename,
  add to or edit a program/project while the office is office-editable. They **may not delete** a
  container that holds another division's activity, or one of their own submitted activities. The
  department head may. Reason: deleting a container cascades to activities the encoder has no right
  to touch.
- **Expenditure lines follow their activity.** `AipExpenditureService` writes (3 call sites of
  `AipWriteGuard.CheckAsync`) take the activity's division lock.
- **Encoders with no division in a divisioned office are read-only.** They cannot be tagged
  (decision 5), so they cannot create or edit activities. The refusal message tells them to ask their
  department head to assign a division (PPDO-135's Staff → division assignment).
- **Bulk-created activities** (import, re-upload, seed from LDIP, carry-forward): tagged with the
  program's single `ProgramDivision` division when there is exactly one, otherwise left untagged —
  the same rule as the backfill (§5). Untagged activities then block division submits (decision 4)
  until the department head tags them.
- **Comments are unchanged.** Row-anchored comments stay office-scoped and work in every state; a
  comment is not a content write.

### Open follow-ups (not blocking)

- **Ceiling ledger by activity division** — replace `ProgramDivision` attribution in
  `AipCeilingService` with `aip_activities.division_id` for FY2028+. File as its own Demo 2 ticket
  when this spec is accepted (decision 7).
- **Should `ProgramDivision` pre-fill tags at all** once tags exist, or be retired for FY2028+? Not
  decided. This spec only reads it for the backfill and bulk creation.
- **Division with zero activities.** Decision 10 lets the office proceed without it. If PPDO wants
  "Division C confirms it has nothing" as an explicit act, that is a later addition.
- **Notify the department head when a division submits.** Counts on the existing notifications
  endpoint are in scope (§6.3); email/push is not.

---

## 3. Behaviour

Roles used below: **Division encoder** = Staff with `CanAccessBudgetPlanning` and a `division_id` in the
office. **Department head** = the office's `CanReviewBudgetPlanning` holder (pinned to their own
office — `Permission_Matrix.md`). **PPDO reviewer** = `CanReviewAllOffices`. Admin/SuperAdmin act as
department head within the scope `OfficeScope` already gives them.

### 3.1 Tagging

| Case | Given | When | Then |
|---|---|---|---|
| Happy path | Encoder in Division A, office editable, A in Draft | Adds an activity | Saved with `division_id = A`; any `divisionId` in the body is ignored |
| Dept head creates | Department head | Adds an activity with `divisionId = B` | Saved tagged B |
| Dept head creates, no division | Department head, divisioned office | Adds an activity without `divisionId` | 400 "Choose the division this activity belongs to." |
| Re-tag | Department head | Moves an activity from A to B | Saved; audit row old A → new B. Allowed even if A or B is Submitted (the department head edits locked work — decision 2) |
| Failure: encoder re-tags | Encoder in A | Calls the re-tag endpoint | 403 |
| Failure: wrong office | Department head of office X | Re-tags to a division of office Y | 400 "That division is not part of this office." |
| Failure: inactive division | — | Re-tags to an inactive division | 400 "That division is inactive." |
| Edge: encoder with no division | Staff, divisioned office, `division_id` null | Adds an activity | 400 "You are not assigned to a division in this office. Ask your department head to assign you one." |
| Edge: no-division office | Office with zero active divisions | Anyone adds an activity | Saved untagged (`division_id` null); today's behaviour |
| Bulk | Import / re-upload / LDIP seed / carry-forward | Activities created | Tagged from the program's single `ProgramDivision` division, else null |

### 3.2 Division submit

| Case | Given | When | Then |
|---|---|---|---|
| Happy path | Encoder in A; A in Draft; all A's activities complete; no untagged activities in the office | Submits Division A | A → `Submitted`; audit `SubmitDivision`; `LogInformation` (AipRecordId, OfficeId, DivisionId, UserId); A's activities read-only to A's encoders |
| Last division | B and C already Submitted | A submits | A → Submitted **and** the office's groups → `DepartmentReview` (decision 9); one audit row for each transition |
| Dept head submits on behalf | Division head away | Department head submits Division A | Allowed; audit names the department head |
| Edge: double submit | A already Submitted | Submits A again | 400 "Division A has already been submitted." |
| Edge: empty division | A has no activities | Submits A | 400 "Division A has no activities to submit." |
| Failure: untagged | Office has 3 untagged activities | A submits | 400 "3 activities in this office have no division. Your department head must assign them before any division can submit." |
| Failure: incomplete | One of A's activities has no expenditure lines | A submits | 400 with the existing readiness refusal text, scoped to A's activities |
| Failure: other division | Encoder in A | Submits Division B | 403 |
| Failure: office not editable | Office in `SubmittedToPpdo` or `Consolidated` | Submits A | 400 naming the office state (`AipWriteGuard.Describe`) |
| Failure: record not Draft | Record Final/Archived | Submits A | 400 "The FY {fy} AIP is '{status}' and cannot be submitted." (existing text) |
| Failure: no-division office | Office with zero divisions | Calls division submit | 400 "This office has no divisions — submit the whole office instead." |
| Failure: office-level submit in a divisioned office | Office with divisions | Calls the existing `POST …/submit` | 400 "This office submits by division. Each division head submits their own division." |
| Ceiling | Office is over its ceiling | A submits | **Allowed, with a warning** (decision 13): the response carries `ceilingWarning` "This office is ₱{overage} over its ceiling. Your department head cannot send it to PPDO until it is within the ceiling." The confirm dialog shows it before submitting, and the toast repeats it after. Sending to PPDO stays refused until the overage is fixed |
| Concurrency | Dept head re-tags an activity into A while A's encoder submits | Both land | Transitions check state inside the save; a re-tag into a Submitted division is legal (decision 2). No lost update on the status row — unique key (§5) |

### 3.3 Editing under the lock

| Case | Given | When | Then |
|---|---|---|---|
| Locked for encoder | A Submitted | A's encoder edits an A activity or its expenditures | 400 "Division A's work has been submitted to the department head and can no longer be edited here." |
| Other division open | A Submitted, B Draft | B's encoder edits a B activity | Saved |
| Cross-division | — | A's encoder edits a B activity | 400 "This activity belongs to Division B." (read-only in the UI; the server refuses regardless) |
| Dept head edits locked | A Submitted | Department head edits an A activity | Saved (decision 2) |
| Container delete | Program holds A and B activities | A's encoder deletes the program | 400 "This program contains another division's activities." |
| Office lock unchanged | Office `SubmittedToPpdo` | Anyone edits | 400 — existing `AipWriteGuard` text; the division rule never *grants* a write the office state refuses |
| PPDO reviewer | Any state | Edits | Refused by `ReviewerWriteGuard` — unchanged |

### 3.4 Return and onward to PPDO

| Case | Given | When | Then |
|---|---|---|---|
| Return one division | A and B Submitted, office in `DepartmentReview` | Department head returns A | A → Draft; office → `Draft` (not every division is submitted any more); B stays Submitted; audit `ReturnDivision`; A's encoders can edit |
| Return before all submitted | A Submitted, B Draft, office Draft | Department head returns A | A → Draft; office stays Draft |
| Return all | Office with divisions in `DepartmentReview` | Existing `return-to-encoder` endpoint | Every Submitted division → Draft; office → Draft. One audit row per division plus the office row |
| Failure: return a Draft division | A Draft | Returns A | 400 "Division A is still with its encoders." |
| Failure: encoder returns | Encoder in A | Calls return | 403 |
| Failure: other office | Department head of X | Returns a division of Y | 404, same text as a missing office (PPDO-46) |
| To PPDO | Every division with activities Submitted | Department head submits to PPDO | Unchanged: full readiness incl. ceiling re-run, office → `SubmittedToPpdo` |
| Failure: to PPDO early | B still Draft | Submits to PPDO | 400 "Division B has not submitted yet." (lists every one) |
| PPDO returns | Office `SubmittedToPpdo`, all divisions Submitted | PPDO reviewer returns it | Office → `ReturnedByPpdo`; **every division → Draft** (decision 11); each division's encoders can edit again; audit one `ReturnDivision` row per division, attributed to the PPDO reviewer |
| Resubmit after PPDO return | Office `ReturnedByPpdo`, divisions Draft | Divisions resubmit one by one | The office stays `ReturnedByPpdo` (so the "returned by PPDO" banner stays up) until the last division submits, then moves to `DepartmentReview`; the department head sends it to PPDO again |

### 3.5 Per-role summary

| Role | See office tree | Edit own division (Draft) | Edit own division (Submitted) | Edit other division | Submit division | Return division | Re-tag |
|---|---|---|---|---|---|---|---|
| Division encoder (Staff, division A) | Yes | Yes | **No** | No | Own only | No | No |
| Staff in office, no division | Yes | — (read-only) | — | No | No | No | No |
| Department head (own office) | Yes | Yes | **Yes** | Yes | Any, on behalf | Yes | Yes |
| Admin / SuperAdmin | Per `OfficeScope` | Yes | Yes | Yes | Yes | Yes | Yes |
| PPDO reviewer (other office) | Read-only, as today | No | No | No | No | No | No |
| Staff of another office | 404, as today | — | — | — | — | — | — |

---

## 4. API contract

All routes JWT-protected; envelope `ApiResponse<T>`; services return `ServiceResult<T>`. Office
ownership failures are **404 with the same text as a missing office** (PPDO-46).

### `GET /api/budget-planning/aip/{aipId}/offices/{officeId}/divisions` — JWT + `CanAccessBudgetPlanningAsync` + office scope

200 `ApiResponse<AipDivisionStatusListDto>`:

```jsonc
{ "officeId": 12,
  "hasDivisions": true,
  "untaggedActivityCount": 0,
  "divisions": [
    { "divisionId": 4, "code": "PLAN", "name": "Planning Division",
      "status": "Submitted",            // "Draft" | "Submitted"
      "activityCount": 18,
      "submittedAt": "2026-10-02T03:10:00Z", "submittedByName": "Ana Reyes",
      "canSubmit": false,               // for the caller
      "canReturn": true,                // for the caller
      "blockers": [] } ] }              // readiness refusal lines, scoped to this division
```

Divisions with zero activities are listed (with `activityCount: 0`) so the department head can see
them; they never block (decision 10).

### `POST /api/budget-planning/aip/{aipId}/divisions/{divisionId}/submit` — JWT + `CanAccessBudgetPlanningAsync`

Caller must be a member of the division, or the office's department head, or Admin/SuperAdmin in scope.
No body. 200 `ApiResponse<AipDivisionSubmitResultDto>`
`{ divisionId, status, officeWorkflowStatus, ceilingWarning }`. `ceilingWarning` is a string or null (decision 13).
400 / 403 / 404 per §3.2.

### `POST /api/budget-planning/aip/{aipId}/divisions/{divisionId}/return` — JWT + `CanReviewBudgetPlanningAsync` (own office) or Admin/SuperAdmin

No body — the reason goes in a comment, as with office returns today. 200 as above. 400 / 403 / 404
per §3.4.

### `PUT /api/budget-planning/aip/activities/{activityId}/division` — JWT + `CanReviewBudgetPlanningAsync` (own office) or Admin/SuperAdmin

Body `{ "divisionId": 4 }` (`[JsonRequired]`; not nullable in a divisioned office). 200
`ApiResponse<AipActivityDto>`. 400 per §3.1 (wrong office, inactive division, office not editable).
403 for encoders.

### Changed existing endpoints

- **`POST …/aip/{aipId}/submit`** — 400 in a divisioned office (§3.2). In an office without
  divisions, over the ceiling now **succeeds with a warning** (decision 13). `AipSubmitResultDto`
  gains `ceilingWarning` (string or null). Completeness failures still return 400.
- **`GET …/aip/{aipId}/readiness`** — `AipReadinessDto` gains `ceilingWarning`. The ceiling no longer
  sets `canSubmit` to false on its own.
- **`POST …/offices/{officeId}/return-to-encoder`** — in a divisioned office, returns every Submitted
  division (§3.4).
- **`POST …/offices/{officeId}/submit-to-ppdo`** — adds the "every division with activities is
  Submitted" check before the existing checklist, which still includes the blocking ceiling check.
- **PPDO return (`POST …/offices/{officeId}/return`)** — in a divisioned office, also resets every
  division row to Draft (decision 11).
- **Activity create** — body may carry `divisionId`; honoured only for department head/Admin (§3.1).
- **`AipActivityDto`** gains `divisionId: int | null`, `divisionName: string | null`,
  `canEdit: bool` (computed for the caller, so the UI does not re-derive the lock rules).

---

## 5. Data model changes

Migration: **`AddAipDivisionSubmit`** — ⚠️ MIGRATION (would be #26 on the pre-deployment checklist;
recheck the count).

### `aip_activities` (existing, snake_case) — new column

| Column | Type | Null | Notes |
|---|---|---|---|
| `division_id` | `int` | yes | FK → `divisions.id`, `ON DELETE NO ACTION` (divisions soft-delete; a hard delete must not orphan silently). Index `IX_aip_activities_division_id` |

### `aip_division_submissions` (new, snake_case)

| Column | Type | Null | Notes |
|---|---|---|---|
| `id` | `int` identity | no | PK |
| `aip_record_id` | `int` | no | FK → `aip_records`, cascade |
| `office_id` | `int` | no | FK → `offices` (config office), restrict |
| `division_id` | `int` | no | FK → `divisions`, restrict |
| `status` | `nvarchar(20)` | no | `Draft` / `Submitted` (a `CHECK` on the domain) |
| `submitted_at` | `datetime2` | yes | UTC |
| `submitted_by_user_id` | `uniqueidentifier` | yes | FK → `Users`, set null |
| `returned_at` | `datetime2` | yes | UTC |
| `returned_by_user_id` | `uniqueidentifier` | yes | FK → `Users`, set null |

Unique index `(aip_record_id, division_id)`. **No row means Draft** — rows are created on first submit,
so opening a year writes nothing here. Entity `AipDivisionSubmission` + configuration; repository
methods scoped to `(aipRecordId, officeId)` — one query per office, never per division.

**Backfill (same migration):** for AIP records with `fiscal_year >= 2028`, set
`aip_activities.division_id` from `program_divisions` where the activity's program has **exactly one**
assignment for its office. Everything else stays null. FY ≤ 2027 rows are not touched.

**Office-state backfill:** none. Any divisioned office already in `DepartmentReview` on UAT keeps that
state, with no division rows. Its divisions read as Draft, so the department head either returns it
(everything reopens) or sends it on to PPDO — and send-on is refused until the divisions submit
(§3.4). Note this in the UAT reset step (§8).

`AuditAction` gains `SubmitDivision`, `ReturnDivision`, `RetagActivityDivision`.

---

## 6. UI states

Follow `docs/DESIGN_SYSTEM.md`: flat, PPDO tokens only, no `text-slate-700`. Reuse `ConfirmDialog`,
`useToast` and the existing readiness panel components under `components/aip/entry/` and
`components/aip/review/`.

### 6.1 AIP Entry (`aip/entry/page.tsx`, `components/aip/*`) — division encoder

- A **division status strip** above the tree: "Planning Division — Draft · 18 activities", with a
  **Submit Planning Division** button.
- Each activity row shows a small division pill (`rounded-full`) when the office has divisions.

| State | Content |
|---|---|
| Loading | Strip renders as a skeleton bar of the loaded height alongside the existing tree skeleton; no spinner |
| Empty | Division has no activities: strip reads "No activities yet in Planning Division." Submit disabled |
| Error | Status fetch failed: strip shows "Could not load division status." with **Retry**. The tree still renders but is **read-only** until status loads (never guess editable) |
| Success | After submit: toast "Planning Division submitted to your department head."; strip turns to "Submitted {date} by {name}"; own rows go read-only |
| Read-only | Other divisions' rows: inputs **disabled** (not hidden), with a lock icon and the tooltip "Belongs to {Division}". Own rows after submit: disabled, with the banner "Submitted — your department head can return it if changes are needed." |
| Validation | Submit refused: the refusal lines render in the existing readiness panel, including the untagged count |
| No division | Staff with no division: banner "You are not assigned to a division. Ask your department head to assign you one." Tree read-only |

The submit button opens `ConfirmDialog`: *"Submit Planning Division? You won't be able to edit these
activities unless your department head returns them."* When the office is over its ceiling, the
dialog adds the warning from §3.2 in the warning style (an amber panel, as in the existing readiness
panel). The submit still goes ahead.

The strip also shows the office's ceiling position (remaining / over) at every stage, read from the
existing readiness `Ceiling` field.

### 6.2 AIP Entry / Review — department head

- A **divisions panel** listing every division: status, activity count, submitted-by/at, and a
  **Return** action per Submitted division (`ConfirmDialog`: *"Return Planning Division? Its encoders
  can edit again, and the office leaves department review."*).
- **Untagged activities** callout with a count and a filter that shows them.
- Activity row: a division `<select>` (re-tag) on each row.
- **Submit to PPDO** disabled with the reason "Waiting on: Engineering Division" until all have
  submitted.

States as in §6.1. Forbidden: the re-tag select and Return are **hidden** for non-department-heads.

### 6.3 Readiness board / notifications

- Office card on the kanban shows "2 of 3 divisions submitted" while the office is in Draft.
- The department head's notification count includes divisions submitted since their last visit,
  using the existing `AipNotificationService` pattern — no email.

---

## 7. Non-goals

- **No change to ceiling or allocation attribution** (decision 7) — follow-up ticket.
- **No new office-level workflow states** (decision 9).
- **Divisions are not exposed on the external partner API** (PPDO-12/13/15). The contract is
  unchanged.
- **No division-level comments or comment filtering.** Comments stay office-scoped.
- **No change to FY ≤ 2027 records** (decision 12).
- **No per-division ceiling.** The ceiling check is office-wide at every stage (decision 13), and division allocations are not checked here.
- **Not a per-user lock or a concurrent-edit guard** — V18-71 is separate.

---

## 8. Deployment notes

- ⚠️ **Migration `AddAipDivisionSubmit`** — run `dotnet ef database update` manually against Azure SQL
  **before** the code deploys. **CI does not run migrations.** The code reads `aip_activities.division_id`
  on every AIP tree load. Add it to `docs/v1.8/Pre_Deployment_Checklist.md` with its backfill.
- **Before first use in an office with divisions:** the department head should check the untagged
  count and tag the remainder. Nothing can be submitted by division until it is zero (decision 4).
- **UAT:** divisioned offices already in `DepartmentReview` need a return before division submits make
  sense (§5 office-state note).
- No new NuGet/NPM dependencies, environment variables or CORS changes.

---

## 9. Ticket split

| Ticket | Scope | Blocked by |
|---|---|---|
| **T1 — data model** | Column + table + entity/config + migration with backfill (⚠️), repository, `AuditAction` values, checklist entry | spec accepted |
| **T2 — tagging + write guard** | Tag on create (§3.1), re-tag endpoint, bulk-create tagging, division lock added to `AipWriteGuard` for `AipService` + `AipExpenditureService`, container-delete rule, `AipActivityDto.canEdit`/division fields, `Permission_Matrix.md` rows | T1 |
| **T0 — ceiling warns at the department-head submit** | Decision 13 for offices without divisions: `AipSubmitService.SubmitAsync` + readiness DTO change, `ceilingWarning` on the result, readiness panel shows it as a warning, and the confirm dialog repeats it. `SubmitToPpdoAsync` unchanged | — (independent; can ship first) |
| **T3 — division submit/return** | `AipDivisionSubmitService` (or methods on `AipSubmitService`), the three new endpoints, office-state derivation (decision 9), changes to office submit / return-to-encoder / submit-to-PPDO | T2 |
| **T4 — entry UI (encoder)** | §6.1 | T3 |
| **T5 — department-head UI + board** | §6.2, §6.3 | T3 |
| *(follow-up, separate ticket)* | Ceiling ledger by activity division (decision 7) | T1 |

**Manual-implementation candidates:** **T0** is a fair one. It's small, it's in one service with
`AipSubmitServiceTests` as a ready feedback loop (`dotnet test`), and the sibling to copy is how
`SubmitToPpdoAsync` already re-runs the checklist. It relaxes a gate rather than adding a permission.
None of T1–T3. T1 is a migration with a backfill; T2 and T3 are
the write guard and permission paths, where a missed check is not caught by the compiler. **T5's
kanban "n of m divisions submitted" chip** is a reasonable small candidate once T3 exists — the
sibling is the existing office card's status chip.

---

## 10. Acceptance checklist

- [ ] In an office with no divisions, an encoder submits the whole office and it goes to department review exactly as before
- [ ] In an office with no divisions that is over its ceiling, the encoder's submit shows the overage warning and still goes to department review; the department head's **Submit to PPDO** is refused with the ceiling message
- [ ] In an office with divisions, an encoder in Planning adds an activity and it shows a "Planning" pill
- [ ] That encoder sees Engineering's activities with inputs disabled and the tooltip "Belongs to Engineering Division"
- [ ] With one untagged activity in the office, **Submit Planning Division** shows "1 activity in this office has no division…" and nothing changes
- [ ] The department head tags that activity from the row's division select; the untagged callout disappears
- [ ] Planning's encoder submits; the toast reads "Planning Division submitted to your department head." and their rows go read-only
- [ ] Planning's encoder tries to edit an expenditure line on a Planning activity and cannot
- [ ] The department head can still edit that Planning activity and save it
- [ ] **Submit to PPDO** is disabled with "Waiting on: Engineering Division"
- [ ] Engineering submits; the office card on the readiness board moves to Department Review
- [ ] The department head returns Planning; Planning's encoders can edit again, Engineering stays locked, and the office card leaves Department Review
- [ ] Planning resubmits; the department head submits to PPDO and the office locks for everyone
- [ ] PPDO returns the office; every division shows Draft and its encoders can edit; once all divisions resubmit, the office is back in Department Review and can go to PPDO again
- [ ] With the office over its ceiling, a division submit shows the overage warning in the confirm dialog and still submits; **Submit to PPDO** stays refused with the ceiling message
- [ ] A Staff user in the office with no division sees the "not assigned to a division" banner and cannot edit
- [ ] An encoder in office X cannot open office Y's divisions (404)
- [ ] The audit log shows division submit, return and re-tag rows with the acting user
- [ ] An FY 2027 record shows no division pills or panels

---

## 11. Test focus

TDD — this is workflow state and permission resolution.

- **`AipDivisionSubmitServiceTests`** (new): submit happy path; last division moves the office to
  `DepartmentReview`; double submit; empty division; untagged blocks; incomplete activity blocks (only
  the division's own activities are counted); cross-division 403; department head submits on behalf;
  no-division office refused; return one reopens only that one and moves the office back to Draft;
  return a Draft division refused; return-all.
- **`AipSubmitServiceTests`**: office submit refused in a divisioned office; in a no-division office,
  over-ceiling submit succeeds with `ceilingWarning` set, an incomplete activity still blocks, and
  submit-to-PPDO over the ceiling is refused; submit-to-PPDO refused while a division is Draft, with every waiting division named; PPDO
  return resets every division row to Draft and the office stays `ReturnedByPpdo` until the last one
  resubmits; over-ceiling division submit succeeds with `ceilingWarning` set, and submit-to-PPDO over
  the ceiling is still refused.
- **`AipWriteGuard` / `AipServiceTests` / `AipExpenditureServiceTests`**: encoder locked on own Submitted
  division; encoder refused on another division's activity; department head allowed on locked;
  container delete with foreign activities refused; the division rule never allows a write the office
  state refuses (pin it); no-division office unchanged.
- **Tagging**: encoder's client `divisionId` ignored; department head must supply one; re-tag across
  offices refused; inactive division refused; bulk creation applies the single-`ProgramDivision` rule.
- **`PermissionMatrixTests`**: rows for division submit, return and re-tag per role, including
  department head of another office and a no-division Staff user.
- **Migration backfill**: covered by a repository-level test on a seeded context. Program with one
  assignment → tagged; two assignments → null; FY2027 → untouched.
