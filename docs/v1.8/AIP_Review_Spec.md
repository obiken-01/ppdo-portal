# v1.8.0 Phase 4 — AIP Review & Consolidation

> **Status:** written 2026-09-08, after Phase 3 (PPDO-48) closed and merged to `release/1.8.0`.
> **Ticket:** PPDO-68 · **Epic:** PPDO-67 · **Standard:** `docs/SPEC_STANDARD.md`
>
> ⚠️ **This supersedes `Phase_Plan.md` §6 for Phase 4.** §6's table is the original work-item list and
> several of its rows are now stale — it names two "shaping questions" that §12.8 answered on
> 2026-08-26, and V18-50 / V18-57 rows that Phase 3 has since overtaken. The plan sections that
> remain current are **§12.4, §12.5, §12.5a, §12.6, §12.7 and §12.8**; this document collects them.
>
> Two questions were open when this spec was started and were **answered by Ralph on 2026-09-08**:
> decision 4 (who may edit during department review) and decision 6 (how an office reaches
> Consolidated). Both are marked 🆕.

---

## 1. Goal

Phase 3 made FY2028 enterable: an office builds its AIP and submits it for department review. **That
submit currently leads nowhere** — `DepartmentReview` is a terminal state in shipped code, and the
three states beyond it exist in the column with nothing to move them.

Phase 4 is the rest of the ladder. A department head reviews their office's work and sends it on to
PPDO; designated PPDO users read every office's submission, comment on the rows they want changed,
and send a whole office back or accept it into the consolidated AIP. Offices see where their work
sits and what was asked of them. PPDO sees who is done and who is not.

---

## 2. Decisions (settled)

Each decision is recorded with its reasoning so a ticket need not reconstruct it, and so the ones
that have already moved are not moved back.

1. **The reviewer is a PPDO user; the LFC is out of the system** (§12.4, tracker B5/B6). The meeting
   whiteboard drew step 4 as the Local Finance Committee. Withdrawn: *"LFC will no longer [be]
   reviewing. Certain PPDO users will be the reviewer."* The DBM Budget Operations Manual
   corroborates it — the LFC's statutory role is **upstream**, identifying investible funds at the
   LDIP stage (printed p.23), not AIP review. What survives unchanged is the mechanic that mattered:
   a permission that deliberately bypasses `OfficeScope`.

2. **There are two reviewer roles and they differ on exactly one point — whether they may edit.**
   Both flags shipped in Phase 1 and both resolve independently, so one person may hold either or
   both. Tabulated in §3.1 rather than described, because "the reviewer" is ambiguous in every
   sentence that does not say which.

3. **There are two submits, not one** (corrected 2026-08-25). The encoder submits for department
   review — **shipped in Phase 3**. The department head submits onward to PPDO — **this phase**. §6's
   original "encoders cannot submit" holds only for the second hop.

4. 🆕 **During `DepartmentReview` the work stays editable by the encoder AND the department-head
   reviewer** (§12.6, tracker B3; **confirmed 2026-09-08**). The lock falls at `SubmittedToPpdo`.

   ⚠️ **This reverses shipped Phase 3 behaviour and requires a correction, not just new code.**
   `AipWorkflowStatus.IsEncoderEditable` returns true for `Draft` only, and its own remarks assert
   *"once submitted the office is read-only to the encoder"*. That was a reasonable reading while
   Phase 4 did not exist, but it is not what the workflow says. The reason §12.6 wins: the department
   head's remit is *"to update any minor details they found during review"* (tracker B11) — if the
   encoder is frozen out, every correction however small must be typed by the department head
   personally, which is not what a review of someone else's work looks like in practice.

5. **`SubmittedToPpdo` locks content for everyone** (tracker B3), and returning unlocks it. Nobody
   edits an office's numbers while PPDO is reading them — not the encoder, not the department head,
   and not the PPDO reviewer, who never edits in any state.

6. 🆕 **An office reaches `Consolidated` by an explicit per-office Accept** (**decided 2026-09-08**).
   §12.6's table said only *"PPDO reviewer returns it, or it stands"*, which never named who marks it
   done. The PPDO reviewer presses Accept on one office: `SubmittedToPpdo → Consolidated`.

   Chosen over closing the whole year at once for three reasons: it mirrors the return path's
   already-confirmed **one-office-at-a-time** granularity (tracker B5); it gives the kanban's Done
   column a meaning before the very end of the season; and it produces an auditable who/when per
   office, which a bulk close does not.

7. **Comments are inline only, anchored to the row** (tracker B10, closed 2026-08-26). ⚠️ Narrower
   than DECISION 10 recorded: **there is no whole-submission comment.** Field-level anchoring was
   rejected — the AIP grid is wide, and it would multiply anchor points for no reviewer benefit.

   ↩️ **Narrowed to activities only** (PPDO-79, decided 2026-09-13). A program or project prints no
   figures of its own on the form, so a remark about one is really about some activity under it —
   and anchoring it higher hides which row has to change. The server refuses a Program or Project
   comment at create with a **400**; the UI offers no composer on those rows. ⚠️ **The `node_type`
   enum and column keep all three values**: a comment written before the rule stays readable and
   resolvable, and still renders on its row — hiding it would leave it counted in decision 10's
   warning with nowhere on screen to clear it.

8. **Only the authoring side resolves a comment** (§12.5). An office may not resolve a PPDO
   reviewer's comment; an encoder may not resolve their department head's. ⚠️ **The single most
   load-bearing rule in the phase.** Without it the soft gate at decision 10 is self-marking: the
   person being asked to change something clears the ask and re-submits having addressed nothing,
   and the failure is silent — everything looks resolved.

9. **No threading, no replies.** Resolve is the only action on a comment (§12.8 Q6). Resolved
   comments are **marked, never deleted** — they are what decision 12's history reads back.

10. **The unresolved-comment gate on re-submit is soft.** The app counts, warns, and lets the user
    **proceed or cancel** (tracker B10). ⚠️ Deliberately not a hard block: a comment may have been
    answered in a phone call or overtaken by a change elsewhere. The gate exists to stop *accidental*
    re-submission by someone who never expanded a collapsed comment.

11. **The unresolved count is split by authoring role, and it is a filter as well as a number.**
    Exactly **two** sources — department-head reviewer and PPDO reviewer — because per §3.1 **the
    encoder never comments**. Two reasons for the split: the reader cannot resolve either set
    themselves (decision 8), so one merged number implies an action that does not exist for them; and
    *"3 unresolved from PPDO"* is the sentence that actually changes whether someone re-submits.

12. **History reuses `AuditLog`; no new history table.** The mechanism already exists, AIP already
    uses it (`AipExpenditureService` logs every expenditure write), and a second parallel history
    table is the outcome to avoid. What this phase adds is three `AuditAction` constants and a
    **read** shaped for display. Reasoning and the one caveat are in §5.2.

13. **"Consolidated" is not a new record** (tracker B4-a). It is the **existing multi-office record,
    filled in office by office as they submit**. Nothing is assembled, copied or snapshotted —
    PPDO-61 settled the container from the other direction, one base AIP record per fiscal year
    holding every office. V18-55 is therefore a view and an access rule, not a data-building
    exercise.

14. **Partial consolidation is the normal state, not an edge case** (tracker B4-b). PPDO reviewers
    review *"the consolidated work so far"*, before every office has submitted. There is no
    "wait until complete" gate.

15. **Review is a separate page from entry, and it is query-first** (§12.5, §12.5a). It **lists
    nothing by default**; work is found by querying for it. Settles §12.5's own internal "two pages"
    vs "two tabs" wording in favour of two pages, which also lets the two be permission-gated
    separately.

16. **Filters: OR within a field, AND across fields, and no boolean expression language** (§12.5).
    Copy the PR List status-chip interaction (`inventory/pr-register`), counts included — it already
    implements exactly this.

17. ⚠️ **V18-76 (ref-code segments as indexed columns) is NOT needed, and was never ticketed.**
    §12.5 raised it as a Phase 2 schema consequence of this phase's UI. Reading the shipped schema
    dissolves it — see §5.3. Recorded as a decision rather than omitted, because the plan still
    describes it as a prerequisite and an implementer who believes that will build a migration
    nothing needs.

18. **The readiness board is an office kanban, five columns** (§12.5a, V18-82). Raised by Ralph
    2026-08-26 as a concrete shape for V18-57's readiness view and V18-58's queue. Details in §6.3.

    ↩️ **Re-placed 2026-09-13 (PPDO-78).** It lives on the **Budget Planning dashboard's Offices band**
    as a **Board / Table view switch** — not below the AIP Review search filters, as first written.
    Two reasons. **One office read instead of two:** the dashboard's `dashboard/offices` read already
    carries every figure a card shows except the workflow column and program count, so it is
    extended rather than a separate `readiness-board` endpoint built beside it — and the dashboard
    requirements already record two office reads drifting apart (140 vs 139 activities for one
    office). **It is where reviewers land.** A switch rather than a replacement, because the table
    is the budget officer's ceiling workspace; they keep the table alone.

19. **No deadline gate anywhere** (tracker B7). The office-to-PPDO deadline is communicated outside
    the system. ⚠️ June 7 is a real statutory deadline but it applies to the **Sanggunian** step, two
    stages downstream of anything this system models — recorded so nobody finds that date later and
    concludes B7 was answered wrongly.

20. **Notifications are in-app only.** No email infrastructure exists in this project and standing
    one up is not v1.8.0 scope; push is PWA Phase 3. The in-app queue is a prerequisite for either,
    so it is the right thing to build regardless of which arrives.

21. ⚠️ **One office can hold several `AipOffice` rows — one per sub-office group — and they move
    through the workflow together.** Already true in shipped code (`AipReadinessDto.WorkflowStatus`
    documents it as "their shared state"). Every transition in this phase therefore operates on
    **the set of rows for one office**, never on a single row. A transition that moves one row leaves
    an office half-submitted, which no screen in this phase can render honestly.

22. 🆕 **The AIP Review search is both reviewers' page** (PPDO-79, decided 2026-09-13). The
    department head finds their own office's rows there and opens an activity in the activity
    modal (§6.1a), rather than working only from the entry tree. The one-office review screen (§6.2)
    stays cross-office only — Return and Accept live there. Opening the search widened what it is
    reachable from, which exposed a scope rule that had only ever been theoretical; it is now stated
    per caller in §3.4, and in one sentence: **cross-office reviewer → every office; department head
    or guest-office user → their own office; host-office user with no reviewer flag → nothing.**

### Open follow-ups (not blocking)

- **Recording the PDC / Sangguniang Panlalawigan outcome** (tracker B14). Both bodies are offline as
  far as this system is concerned; what they consume is a document we produce, so *generation* is
  settled and in scope for Phase 5. What stays open is whether the system records the **outcome** —
  a state beyond `Consolidated`, a resolution reference, a return-for-revision path.
- **Supplemental amendments** (tracker B15, V18-73). Reportedly route through the SP first, which
  makes amendment readiness a workflow question before it is a schema one.
- **Whether a department head may return work *down* to the encoder.** §12.6 defines only the
  PPDO→office return. Decision 4 makes this much less pressing — both parties can edit during
  department review, so there is nothing to unlock. **Default: not built.** Revisit if offices ask.
- **Whether an accepted (`Consolidated`) office can be re-opened.** Not specified. **Default:** only
  by a PPDO reviewer, through the existing return path. If that proves wrong it becomes a ticket.

---

## 3. Behaviour

### 3.1 The two reviewers — the table this phase exists to keep straight

"The reviewer" is ambiguous in every sentence that does not say which one. Both flags shipped in
Phase 1 and **resolve independently** (`PermissionService`), so one person may hold either or both.

| | Department-head reviewer | PPDO consolidated reviewer |
|---|---|---|
| **Flag** | `CanReviewBudgetPlanning` (RAL-244) | `CanReviewAllOffices` (RAL-257) |
| **Scope** | their **own** office | **every** office — the first flag in the system that bypasses `OfficeScope` |
| **Edit values** | ✅ **Yes** — "to update any minor details they found during review" (B11) | ❌ **Never** — "they won't be able to apply any update, just comment" (B11) |
| **Comment** | ✅ | ✅ |
| **Submit onward** | ✅ — the **sole** authority for `DepartmentReview → SubmittedToPpdo` | — |
| **Return an office** | — | ✅ |
| **Accept an office** | — | ✅ (decision 6) |
| **Enforced by** | `ReviewerWriteGuard` **permits** this role | `ReviewerWriteGuard` **denies** content writes |
| **Convention** | one per office, enforced as a validation error not a DB constraint (V18-79, tracker B13) | several is fine |

⚠️ **The encoder never comments.** They read comments, act on them, and re-submit. This is why the
unresolved count in decision 11 splits two ways and not three.

⚠️ **PPDO runs the same ladder as everyone else** (tracker B12). PPDO's divisions submit to a **PPDO
department-head reviewer**, who is a *different person* from the PPDO consolidated reviewer. Do not
shortcut PPDO straight to `SubmittedToPpdo`.

### 3.2 The workflow

| State | Who may edit content | Who may see it | Moves on by |
|---|---|---|---|
| `Draft` | Encoder(s) — two or more per office (tracker D5) | The office | Encoder submits for department review **(shipped, Phase 3)** |
| `DepartmentReview` | **Encoder and department head both** (decision 4) | The office | Department head submits to PPDO |
| `SubmittedToPpdo` | **Nobody** (decision 5) | The office (read-only) + PPDO reviewers | PPDO reviewer **returns** or **accepts** |
| `ReturnedByPpdo` | Encoder and department head again | The office | Department head **re-submits** |
| `Consolidated` | Nobody | **PPDO reviewers only** — PPDO's *division* users cannot see the consolidated document (tracker B4) | — (see open follow-up) |

### 3.3 Core

| Case | Given | When | Then |
|---|---|---|---|
| Happy path — second submit | Office in `DepartmentReview`, all activities complete, GF within ceiling | Department head submits to PPDO | Every `AipOffice` row for that office moves to `SubmittedToPpdo` **together** (decision 21). Content locks |
| Happy path — return | Office at `SubmittedToPpdo` with comments | PPDO reviewer returns it | → `ReturnedByPpdo`; content unlocks for encoder and department head. Comments stay, unresolved |
| Happy path — accept | Office at `SubmittedToPpdo` | PPDO reviewer accepts it | → `Consolidated`. Appears in the readiness board's Done column (§6.3) and counts toward the consolidated totals |
| Happy path — re-submit | Office at `ReturnedByPpdo`, edits made | Department head re-submits | → `SubmittedToPpdo`. Same transition as the second submit, plus the unresolved warning |
| ⚠️ Gate re-runs on the second hop | Department head edited values during review | Submit to PPDO | Completeness **and** ceiling re-run. The figures that passed the first gate are not necessarily the figures being sent on |
| Encoder edits during department review | Office in `DepartmentReview` | Encoder edits an activity | ✅ **Allowed** (decision 4). ⚠️ This is the behaviour Phase 3 currently refuses |
| Encoder edits after PPDO submit | Office at `SubmittedToPpdo` | Encoder edits anything | **400**, naming the state in words: "…while it is in review by PPDO" (`AipWriteGuard.Describe`) |
| PPDO reviewer edits | Any state | PPDO reviewer attempts a content write | **403** via `ReviewerWriteGuard`. Unchanged from shipped behaviour |
| PPDO reviewer comments on a locked office | Office at `SubmittedToPpdo` | Reviewer leaves a comment | ✅ **Allowed.** A comment is not a content write — `SubmittedToPpdo` is precisely the state in which commenting must work |
| Encoder tries to submit onward | Office in `DepartmentReview` | Encoder presses submit-to-PPDO | **403.** The second hop is the department head's alone (decision 3) |
| Department head submits an empty office | No activities | Submit to PPDO | Refused with the same completeness issue list the first gate produces |
| Re-submit with unresolved comments | 3 unresolved from PPDO, 1 from the department head | Department head re-submits | **Warning naming both counts separately**, with Proceed and Cancel. Proceeding succeeds (decision 10) |
| Re-submit with none unresolved | All comments resolved | Re-submit | No warning. Straight through |
| Office resolves a PPDO comment | Comment authored by a PPDO reviewer | Encoder or department head presses resolve | **403.** Only the authoring side resolves (decision 8) |
| Encoder resolves the department head's comment | Comment authored by their own department head | Encoder presses resolve | **403.** Same rule, inside one office |
| PPDO reviewer resolves their own comment | Their comment | Resolve | ✅. Marked resolved, retained, visible in history |
| Comment on a deleted row | A commented activity is deleted while in `Draft` | Reload | Comment is retained and shown as orphaned in history, never silently dropped — see §5.1 |
| Two reviewers act at once | Two PPDO reviewers open one office | One returns, the other accepts | Second action is refused **409**, naming the state it is now in. Last-write-wins would silently undo a return |
| Return an office not at PPDO | Office in `Draft` | Reviewer returns it | **400** — nothing to return |
| Accept a returned office | Office at `ReturnedByPpdo` | Reviewer accepts | **409**, naming the current state. ↩️ *Was 400 until PPDO-74.* This row and the concurrency row two above it answered the same state two different ways. 409 wins: §10's checklist states that exact race concretely, only another reviewer can have put the office in `ReturnedByPpdo`, and it is the split `AipReviewService.Refuse` already draws (reviewer-produced state → 409, pre-PPDO state → 400). A 400 tells the reviewer who lost the race that they made the mistake |
| Accept an office that never reached PPDO | Office in `Draft` or `DepartmentReview` | Reviewer accepts | **400** — nothing was sent up, so there is nothing to accept. This is the row that was meant by "accept applies only from `SubmittedToPpdo`" |
| Consolidated view, most offices unsubmitted | 3 of 19 offices submitted | PPDO reviewer opens it | Renders the 3, and shows the other 16 as **not yet submitted** — ⚠️ **not as ₱0** (decision 14) |
| Review page first open | Any reviewer | Opens AIP Review | **Empty by design** (decision 15), with copy explaining how to search — not a blank panel, not a spinner |
| Query returns nothing | A ref code that matches no row | Search | Empty-results state distinct from the initial empty state — "no matches" reads differently from "start here" |
| Office user and the readiness board | An office encoder | Opens the Budget Planning dashboard | **No Offices band, so no board** (§6.3). Their own status is a chip on their own AIP page |
| Budget officer and the readiness board | `CanManagePboCeiling` only | Opens the Budget Planning dashboard | **The Offices table, with no Board / Table switch** (§6.3) |
| FY2027 legacy record | An FY2027 record | Opened in review | No workflow, no comments, no submit. Phase 4 is FY2028+ only, exactly as Phase 3 |

### 3.4 Permission and scope

Every row is pinned by `docs/v1.8/Permission_Matrix.md`. **Phase 4 adds no new flag** — it adds call
sites that must use the existing resolvers.

| Account | Given | When | Then |
|---|---|---|---|
| Guest-office encoder | `CanAccessBudgetPlanning`, own office | Opens AIP Review | Their own office only. Division is not a factor for them |
| Guest-office encoder supplies another `officeId` | Query string | Read | **Clamped** to their own office — their data back, not a 403 that confirms the other office exists |
| Guest-office encoder targets another office's node | A node id they do not own | Write or comment | **`NotFound`**, not `Forbidden` (PPDO-46). A write names one node; clamping would write to the wrong row |
| Department-head reviewer | `CanReviewBudgetPlanning`, own office | Any state their office is in | Read always; edit per §3.2; submit onward from `DepartmentReview` only |
| Department-head reviewer of office A | — | Opens office B | **Clamped / NotFound** as above. The flag is office-scoped, not cross-office |
| Department-head reviewer | `CanReviewBudgetPlanning`, own office | Uses the AIP Review search (PPDO-79) | **Their own office's rows**, clamped server-side — never a 403. Opens an activity in the modal (§6.1a); edits it only when the server's `canEdit` is true |
| PPDO (host) department head | Host office, no `CanReviewAllOffices` | Searches, or opens another office's activity by id | **Clamped to PPDO / 404.** ⚠️ "Own office" is matched on `caller.OfficeId`, **never** `OfficeScope.Resolve` — which gives every host-office user the whole province. Pinned by `Search_HostDepartmentHead_IsClampedToTheirOwnOfficeNotTheProvince` and `GetActivityForReview_ByPpdosDepartmentHeadOnAnotherOffice_IsNotFound` |
| PPDO (host) user, no reviewer flag | Host office | Calls the search, with or without `mine` | **Empty page; the repository is never queried** (PPDO-79). `Resolve` would give them the province, and the search cannot apply the division axis that narrows them everywhere else. They hold no review work |
| Holder of **both** reviewer flags | Own office | Opens an own-office activity in the modal | Reads and comments **as department head** (own office wins, as for comment sides) — but **not editable**: `ReviewerWriteGuard` denies content writes to the cross-office grant, so `canEdit` is false |
| PPDO consolidated reviewer | `CanReviewAllOffices` | Any office's record | Read via `OfficeScope.ResolveForReview` — ⚠️ **never** `OfficeScope.Resolve`. Comment and return/accept; content writes denied |
| PPDO (host) encoder | Division D | Reads their office's AIP | Only programs assigned to **division D** via `ProgramDivision`. ⚠️ **Host office only** — must not apply to guest offices |
| PPDO division user (no reviewer flag) | Host office | Opens the consolidated view | **Forbidden.** ⚠️ The gate is the reviewer flag, **not** `IsHostOffice` — the tempting wrong axis (tracker B4) |
| Any user, `office_id` null | Unassigned | Any read | `OfficeScope.NoOffice` → sees nothing. Empty states, not an error (DECISION F) |
| SuperAdmin | Resolves every flag true | Any write | **Exempt from the subtractive reviewer guard** — a naive guard locks SuperAdmin out of every budget-planning write. Pinned by `ReviewerWriteGuardTests` |

---

## 4. API contract

All routes are **JWT-protected**; none appears on `CLAUDE.md`'s public list. Envelope is
`ApiResponse<T>` (`{ data, error, message }`); services return `ServiceResult<T>`.

Routes follow the shipped `budget-planning/aip/...` family (`AipSubmitFunctions` already serves
`{aipId}/readiness` and `{aipId}/submit`).

| Endpoint | Gate | Notes |
|---|---|---|
| `POST /api/budget-planning/aip/{aipId}/offices/{officeId}/submit-to-ppdo` | `CanReviewBudgetPlanning` + office match | `DepartmentReview` \| `ReturnedByPpdo` → `SubmittedToPpdo`. Re-runs completeness + ceiling. Moves **every** `AipOffice` row for the office (decision 21) |
| `POST /api/budget-planning/aip/{aipId}/offices/{officeId}/return` | `CanReviewAllOffices` | → `ReturnedByPpdo`. Body optional; the comments are the mechanism, not a covering note |
| `POST /api/budget-planning/aip/{aipId}/offices/{officeId}/accept` | `CanReviewAllOffices` | → `Consolidated` (decision 6). Only from `SubmittedToPpdo` |
| `GET /api/budget-planning/aip/{aipId}/offices/{officeId}/comments` | `CanAccessBudgetPlanning` (scoped) or `CanReviewAllOffices` | All comments for one office, resolved included |
| `POST /api/budget-planning/aip/comments` | Any role that may comment (§3.1) | ⚠️ Must **not** route through `ReviewerWriteGuard`. **Activity nodes only** — Program / Project → 400 (decision 7, PPDO-79) |
| `POST /api/budget-planning/aip/comments/{id}/resolve` | Authoring side only (decision 8) | 403 for the recipient |
| `GET /api/budget-planning/aip/review/search` | `CanAccessBudgetPlanning`, **clamped per caller** (§3.4, decision 22) | Query-first (§4.1). **Slim DTO, paginated** |
| `GET /api/budget-planning/aip/activities/{activityId}/review` | `CanReviewAllOffices` **or** `CanReviewBudgetPlanning` | 🆕 PPDO-79 — the activity modal (§6.1a). Path + activity + expenditure lines + a server-computed `canEdit`. A department head may open only their own office's activity; every refusal is a **404** worded like a missing activity (PPDO-46). ⚠️ Returns the lines itself rather than leaning on `activities/{id}/expenditures`, which scopes with `OfficeScope.Resolve` and 404s a cross-office reviewer who does not sit in PPDO |
| `GET /api/budget-planning/dashboard/offices?fiscalYear=` | Unchanged — `CanAccessBudgetPlanning` **and** a cross-office grant (`Budget_Planning_Dashboard_Requirements.md` §4.2) | ↩️ **Replaces the planned `readiness-board` endpoint** (decision 18). PPDO-78 adds `ReadinessColumn`, `IsReturned` and `AssignedProgramCount` to `OfficeSummaryDto`, and derives `SubmissionStatus` from workflow state instead of the constant `Todo`. One office read feeds both views, so they cannot drift |
| `GET /api/budget-planning/aip/{aipId}/consolidated` | `CanReviewAllOffices` | Partial by design (decision 14). Slim, server-aggregated |
| `GET /api/budget-planning/aip/{aipId}/offices/{officeId}/history` | `CanAccessBudgetPlanning` (scoped) or `CanReviewAllOffices` — ↩️ **the same gate and scope as the comments read** (PPDO-77) | Was "same as the review read", which would have hidden an office's own history from the office. Readable by its encoders and department head on AIP Entry and by any cross-office reviewer. Hand-offs newest first, each carrying the comments written while the office sat in the state it opened (§5.2). Every refusal is a **404** worded like a missing office (PPDO-46) |
| `GET /api/budget-planning/aip/review/pending-count` | Any reviewer | One integer, resolved per person. ⚠️ `CountAsync` at the database |

### 4.1 The search contract

| Field | Multi-value | Matching |
|---|---|---|
| `officeIds` | ✅ OR | `AipOffice.OfficeId IN (…)` — indexed FK |
| `sectors` | ✅ OR | `AipOffice.Sector IN (…)` |
| `refCode` | ✅ OR, typed list or comma | **Prefix** match `LIKE 'x%'` per value — SARGable. ⚠️ Never `LIKE '%x%'` |
| `title` | ❌ single | Free text over program / project / activity name. ⚠️ **Do not OR-split** — a title may legitimately contain the word "or" |
| `workflowStatuses` | ✅ OR | Chip filter, mirrors the kanban columns |
| `mine` | — | The "everything applicable to me" tag, scoped by the caller's own permissions |

Fields AND together. **No boolean expressions** (decision 16).

⚠️ **A ref-code prefix cannot express "one office, all sectors"** — sector is segment 1 and office is
segment 5, so pinning the office while letting the sector vary means matching the *middle* of the
string. Verified against real data: **11 offices span more than one sector** (`001` and `017` span
three; PPDO's own `010` spans two). That query is `officeIds` + blank `sectors`, which is also the
form that stays correct as the data grows — a typed prefix list captures only today's sectors.

### 4.2 Error shapes

| Case | Status | Shape |
|---|---|---|
| Office belongs to someone else | **404** | Byte-identical to a node that does not exist (PPDO-46) |
| Transition from the wrong state | 400 | Names the state in words — `AipWriteGuard.Describe` |
| Another reviewer moved it first | **409** | `"This office was already returned by someone else. Reload to see the current state."` |
| Encoder attempts the second hop | 403 | Names the department-head reviewer as the required authority |
| Recipient resolves a comment | 403 | `"Only the reviewer who wrote this comment can resolve it."` |
| Submit-to-PPDO fails completeness | 400 | **A list**, one entry per failing activity — reuses `AipReadinessIssueDto` |
| Submit-to-PPDO fails ceiling | 400 | Names fund, ceiling, encoded total, overage |
| Comment on a locked office | — | ✅ **Not an error.** Explicit row because it looks like one |

⚠️ Unresolved comments are **never** an error — decision 10 makes that a client-side warning with
Proceed and Cancel. If the API refuses a re-submit over unresolved comments, the gate has become
hard and the decision has been reversed by accident.

---

## 5. Data model changes

### 5.1 `aip_review_comments` — new table (V18-53)

snake_case per `docs/NAMING_CONVENTIONS.md` (new table). Migration: **`AddAipReviewComments`**.

| Column | Type | Null | Note |
|---|---|---|---|
| `id` | int identity | no | |
| `aip_office_id` | int FK → `aip_offices.id` | no | The office whose review this belongs to. Cascade |
| `node_type` | nvarchar(16) | no | `Program` \| `Project` \| `Activity` |
| `node_id` | int | no | ⚠️ **Deliberately not an FK** — see below |
| `author_id` | uniqueidentifier FK → `Users` | no | |
| `author_side` | nvarchar(16) | no | `DepartmentHead` \| `Ppdo`. ⚠️ **Stored, not derived** — see below |
| `body` | nvarchar(2000) | no | |
| `created_at` | datetime2 | no | UTC, rendered UTC+8 |
| `resolved_at` | datetime2 | yes | Null = unresolved |
| `resolved_by_id` | uniqueidentifier FK → `Users` | yes | |

Index: `IX_aip_review_comments_office_resolved (aip_office_id, resolved_at)` — the unresolved count
in decision 11 runs on every review page load and every re-submit.

⚠️ **`author_side` is stored, not resolved from the author's flags at read time.** Flags change:
a person can gain or lose `CanReviewAllOffices` months later, and if the side were derived, an old
comment would silently change who is allowed to resolve it. Decision 8 is only enforceable if the
side is a fact about the comment, not about the author today.

⚠️ **`node_id` is a plain int with `node_type`, not a foreign key.** A polymorphic anchor across
three tables cannot be a single FK. The consequence is real and must be handled rather than
discovered: **deleting a commented node leaves the comment dangling.** Per §3.3 it is retained and
shown as orphaned in history rather than cascaded away — decision 9 says resolved comments are never
deleted, and the same reasoning applies to a comment whose row was removed: it is part of why the
work changed. The read joins and tolerates a missing node.

### 5.2 History — `AuditLog`, three new actions (V18-77)

**No new table** (decision 12). `AuditAction` today is `CREATE` / `UPDATE` / `DELETE`; add:

```
SUBMIT_DH · SUBMIT_PPD · RETURN_PPD · ACCEPT_PPD
```

with `table_name = "aip_offices"`, `record_id` = the `AipOffice` row, and old/new values carrying
the workflow status. The Phase 3 encoder submit gains `SUBMIT_DH` so the history reads as one chain
rather than starting mid-ladder.

⚠️ **`audit_log.action` is `nvarchar(10)`** — set in `AuditLogConfiguration`, and this spec's first
draft proposed `SubmitToPpdo` / `ReturnByPpdo` / `AcceptByPpdo`, all of which are 12 characters and
would have failed on the first real write to SQL Server. Corrected in PPDO-69. **Count any further
action name against ten before adding it**; nothing in the build catches an over-long one, because
the in-memory provider used by the unit tests does not enforce the width.

⚠️ **This is the one place the reuse is not free.** A transition written as a generic `UPDATE` would
be indistinguishable from any other column change without parsing the JSON. That is exactly why the
actions are named constants rather than `UPDATE` — the history read is then
`WHERE table_name = 'aip_offices' AND record_id IN (…) AND action IN (…)`, which needs no JSON
parsing and no new index beyond what `AuditLog` already has.

↩️ **Corrected in PPDO-77 — there is nothing to group.** This paragraph said that, because an office
has several `AipOffice` rows (decision 21), one transition writes several audit rows and the read
must group them back. What PPDO-69 and PPDO-72 actually shipped writes **one** row per transition,
keyed on the office's first group, with every group id in `new_values.GroupIds`. The read filters
`record_id IN (every group id of the office)` — so it finds the row whichever group was first — and
renders each row once. Grouping built to the old text would have grouped rows that do not exist.

**How comments join the chain** (PPDO-77, agreed on the wireframe):

- The chain is **hand-offs only**, newest first. A resolve shows on its comment, never as a line.
- Each comment sits under the **most recent hand-off at or before its `created_at`** — the state the
  office was in when it was written. Within a hand-off, comments read oldest first, as a conversation.
- Comments written before any hand-off form a separate **before first submission** group.
- `from` is read from `old_values.WorkflowStatus` **for display only** — the filter never touches
  JSON. It is what makes a `SUBMIT_PPD` from `ReturnedByPpdo` read as a re-submit. An unreadable
  payload leaves `from` empty rather than failing the read.

### 5.3 ⚠️ V18-76 is not needed — do not build it

`Phase_Plan.md` §12.5/§12.7 records V18-76 as a Phase 2 prerequisite for this phase's segment
filters: store ref-code segments 1–5 as five indexed columns, because `LIKE '%…%'` over a formatted
string cannot use an index. **It was never ticketed, and reading the shipped schema shows it is not
required.**

| Query §12.5 asks for | What V18-76 proposed | What already exists |
|---|---|---|
| One office, all sectors | `office` segment column | **`AipOffice.OfficeId`** — a real FK since PPDO-33 |
| All offices in a sector | `sector` segment column | **`AipOffice.Sector`** — already a column |
| Subtree drill-down (`…-010-001-`) | segment columns 6+ | **Prefix** `LIKE 'x%'` on `RefCode`, which is SARGable, plus the parent FKs |
| "Segment 3 = 004" | a column per segment | Not a query any screen in this spec asks for |

The remaining half of §12.5's reshaping — *"give each node a sibling-unique `seq` and render the code
from the path"* — is what the tree already is. **Confirm `IX` coverage on `AipOffice.OfficeId` and
`Sector` when PPDO-76 is built**; adding an index is a smaller change than the five-column migration,
and it is the only part of V18-76 that may still be warranted.

↩️ **Confirmed in PPDO-76: no index, and no migration.** `AipOfficeConfiguration` already declares
`(AipRecordId, RefCode)` unique, `(AipRecordId)`, `(RefCode)`, `(AipRecordId, OfficeId)` and
`(OfficeId, WorkflowStatus)` — so the office axis is covered twice over. **`Sector` was deliberately
left unindexed:** every search is scoped to one AIP record first, which is ~25–40 office rows, and
an index cannot beat a scan of forty rows that the `(AipRecordId, …)` index has already isolated.
The office-level filters are applied to that set *before* the joins, so the sector predicate never
sees the node tables at all. Adding one would be cargo-cult indexing — a migration, a write cost and
a thing to maintain, bought for nothing. Revisit only if an AIP record ever holds office rows in the
thousands, which the one-record-per-fiscal-year model (PPDO-61) rules out.

### 5.4 No change to `aip_offices`

`workflow_status` already holds all five states, introduced in one migration by Phase 3
(`AddAipOfficeWorkflowStatus`) precisely so this phase needs no second migration on that column.

---

## 6. UI states

Flat design, PPDO tokens only, per `docs/DESIGN_SYSTEM.md`. Reuse `components/ui/` — `Modal`,
`DataTable`, `ConfirmDialog`, `useToast` — and the AIP components PPDO-64 extracted.

⚠️ **Do not build the review surface on `aip/detail/page.tsx`.** PPDO-64 extracted it into components
precisely so review could reuse the pieces rather than fork a 2,000-line page.

### 6.1 AIP Review — search (PPDO-76, new page)

| State | Content |
|---|---|
| **Empty (initial)** | ⚠️ **The default, and deliberate.** Filter panel plus copy explaining how to search. Never a blank panel. ↩️ *No readiness board below it* — the board moved to the dashboard (decision 18, §6.3) |
| **Loading** | Skeleton rows matching the result table — same header, same row height. Never a centered spinner (CLS) |
| **Empty (no matches)** | Distinct from the initial state: "No AIP rows match these filters", with a clear-filters action |
| **Success** | Chips show per-value counts, as PR List does. ↩️ *PPDO-79:* an **activity** name opens the activity modal (§6.1a); a **program or project** name opens the reader's own whole-office surface — §6.2 for the PPDO reviewer, AIP Entry for a department head |
| **Filter panel** (PPDO-79) | Opens expanded. **A Search press that returns rows collapses it** to one line: a funnel-marked label, one removable chip per applied filter, the match count, *Edit filters* and *Clear*. An error or an empty result leaves it open. *Edit filters* re-opens it showing what produced the rows. **A chip's × re-runs the search at once** — unlike the open panel, which stays draft-until-Search; removing the **last** chip returns to the initial state rather than searching everything (decision 15) |
| **Office filter** (PPDO-79) | `OfficeSelect`, **one office at a time** — the API still takes a list. Hidden for a department head, replaced by a line naming their office: the server clamps them regardless, and a picker that silently does nothing reads as broken. "Only what's waiting on me" is hidden from them for the same reason |
| **Error** | Inline error with retry; filters preserved |
| **Forbidden** | The page is **hidden from the sidebar** for users without a reviewer flag, not disabled. ↩️ *Settled in PPDO-76:* the **page** is gated on `CanReviewAllOffices` and redirects anyone else, while the **endpoint** keeps §4's `CanAccessBudgetPlanning` gate and *clamps* a guest office to its own rows. §3.4's "guest-office encoder opens AIP Review" row described the endpoint's behaviour, not a UI that exists — every result links into §6.2's reviewer-only screen, so a non-reviewer searching would hit a dead end on every row. The endpoint stays permissive so a 403 cannot be used to discover which offices exist (PPDO-46), and so an office-facing surface can be added later without reopening the scope rule ↩️ **Widened in PPDO-79 (decision 22):** the page now opens for **either** reviewer flag (`canOpenAipReview`), and the one-office screen keeps the cross-office gate on its own rule (`canOpenAipOfficeReview`). A department head's result rows link to the modal and to AIP Entry, so the dead end described above no longer applies to them |

### 6.1a AIP Review — activity modal (PPDO-79, new)

Layout agreed on the wireframe (`docs/v1.8/wireframes/aip-review/`, settled 2026-09-13) before it was
built — the process gap the PPDO-79 review feedback exposed.

| State | Content |
|---|---|
| **Opened from** | An activity name in §6.1's results. Program and project rows do not open it (decision 7) |
| **Loading** | Skeleton shaped like the loaded modal — banner, three path rows, the two columns. Never a spinner |
| **Success** | A banner naming who holds the work, **in the reader's own voice**; a path strip (office → program → project), because a search row is flat; then **two columns** — left: activity header, amounts, details, expenditure lines; right: the activity's comment thread, **always open** |
| **Two readers** | **PPDO reviewer** — read and comment; footer links to §6.2. **Department head, own office** — the same modal, editable through the entry page's own `AipActivityFields` and `AipExpenditureTable` when `canEdit` is true; footer links to AIP Entry |
| **`canEdit`** | ⚠️ **Server-computed and never re-derived**: own-office department head **and** a Draft record **and** `IsOfficeEditable` **and** not denied by `ReviewerWriteGuard`. The last is invisible from `/auth/me` — a holder of both flags is read and comment only |
| **Height** | Expenditure lines stay in (decided). The body scrolls inside `Modal`, under a fixed header and footer |
| **Narrow window** | The two columns stack below `lg` |
| **Error** | Inline error with *Try again*. A refresh that fails after a save keeps the last good read on screen and says so |
| **After a save** | The modal re-reads the activity in place — no skeleton, scroll kept. Closing it re-runs the search, so a renamed row shows its new name |
| **Comments** | Composer offered only where the reader may comment (decision 8's sides); 2000-character counter; the empty rail says so, and a failed comment fetch is said out loud rather than shown as an empty thread |

### 6.2 AIP Review — one office (PPDO-74)

| State | Content |
|---|---|
| **Success** | The office's tree, read-only, with a comment gutter per row. State chip at the top naming where the work sits and who holds it |
| **Comments collapsed** | ⚠️ **Default.** Rows carrying comments get a marker and a show-comments control; bodies open deliberately, so the grid stays readable (§12.5) |
| **Unresolved filters** | Two buttons with counts — "N from PPDO", "N from department head" — filtering the tree to commented rows (decision 11) |
| **Read-only / forbidden** | An office viewing its own work at `SubmittedToPpdo` sees inputs **replaced by text**, not disabled inputs, plus a banner naming the state. A PPDO reviewer sees the same, since they never edit |
| **Actions** | Return and Accept for PPDO reviewers only; both confirm via `ConfirmDialog` naming the office. Submit-to-PPDO for the department head |
| **Validation** | Comment body required, 2000 char cap shown as a counter |
| **History** (PPDO-77) | A **History** button in the header between the state chip and Send back / Accept, shown at **every** status — an accepted or returned office is exactly the one a reviewer wants to trace. Opens §6.2a |

### 6.2a History modal (PPDO-77, new)

Layout agreed on the wireframe (`docs/v1.8/wireframes/submission-history/`, settled 2026-09-14).

| State | Content |
|---|---|
| **Opened from** | The review screen header (§6.2), and **AIP Entry's submit panel header** for the office's own people — encoders and department head alike. It sits beside the submit button and stays when the panel reads "With PPDO" |
| **Loading** | Skeleton shaped like the hand-off entries. Never a spinner |
| **Success** | A timeline, newest first. Each hand-off names what happened ("Submitted for department review", "Sent to PPDO", "Re-submitted to PPDO", "Sent back by PPDO", "Accepted by PPDO"), who, when (Manila time) and from → to; the newest is marked as where the work is now. Beneath each, the comments written while the office held that state, **collapsed to a count** ("3 comments while with PPDO · 1 still unresolved") and opened on demand |
| **Comments** | Author, side, time, the row's ref code (or that the row was removed), body, and who resolved it when. **Read-only** — resolving stays on the tree and the activity modal |
| **Empty** | "No hand-offs yet", naming the draft state. Comments written before a first submit still render, under "Before first submission" |
| **Error** | Inline error with *Try again* |
| **Size** | `Modal` size `lg`; the body scrolls under a fixed header |

### 6.3 Readiness board (PPDO-78)

Wireframe: `docs/v1.8/wireframes/readiness-board/` — every question on it settled with Ralph on
2026-09-13, before any of it is built.

↩️ **Where it lives changed on 2026-09-13 (decision 18).** Not below the AIP Review search filters:
the board is the **Offices band on the Budget Planning dashboard**, shown as a **Board / Table view
switch**. The table is unchanged apart from the switch and its Submission column (below).

**Who sees what**

| Caller | Offices band |
|---|---|
| Cross-office reviewer (`CanReviewAllOffices`), or SuperAdmin | Board / Table switch. **Board by default**; the last choice is **remembered** on that device |
| Budget officer (`CanManagePboCeiling`) only | **Table only, no switch.** The board adds where each office sits in review, which they do not act on — ceilings, costed and the risk pills are already in the table, and the table is where they publish ceilings |
| Holds both grants | The switch, board by default |
| Anyone else — office encoders, host-office users with no cross-office grant | No Offices band at all — unchanged; the endpoint answers 403 |

⚠️ **The remembered view is per person, per device** — `localStorage`, keyed by user id, so two
people sharing a PC keep their own choice. A stored `board` is ignored for a caller who cannot see
the board, and a missing or unreadable value falls back to the caller's default. It is a
convenience, never a gate.

**Columns — five:** Not Started · In Progress · Office Review · PPDO Review · Done.

| Column | Rule |
|---|---|
| **Not Started** | `Draft` (or no AIP office row yet) **and zero activities** |
| **In Progress** | `Draft` and one or more activities |
| **Office Review** | `DepartmentReview` — **or `ReturnedByPpdo`, carrying a *Returned* badge** |
| **PPDO Review** | `SubmittedToPpdo` |
| **Done** | `Consolidated` |

- ⚠️ **"Returned by PPDO" is NOT a sixth column.** Returned work re-enters the department-review
  condition, so it sits in Office Review with a *Returned* badge — the reviewer's most actionable
  signal ("I sent this back; has it come back?") without column sprawl.
- ✅ **Not Started vs In Progress is decided by activity count**, not seeded programs. Programs
  arrive from LDIP without anyone in the office touching the record, so counting them would show an
  office that never opened the page as working. Submission states win over both.
- ⚠️ **The column is computed by the server** (`ReadinessColumn`, §4). It is workflow logic, so it is
  tested in C# — and the board and the table must never be able to disagree about it.

**Not Started is a compact list, not cards.** Each row: office code, assigned program count, and a
red dot when nobody in the office can submit, with a one-line legend under the list. Early in the
season 17 of 19 offices sit here; as full cards they would turn the board into one tall column.

**A card** — every other column — in this order:

1. Office code, the **Host** badge, the **Returned** badge
2. Office name, truncated
3. `N programs · N activities`
4. Costed against ceiling, **abbreviated** — `₱1.12M of ₱10M`. "No ceiling published" when there is
   none; danger-coloured when over. The table keeps exact pesos
5. Risk pills — *Over ceiling*, *Cannot submit* — only when they apply
6. Reviewer name, or "No reviewer — assign"

- ⚠️ **No "% complete" anywhere.** There is a denominator for programs but **none for activities** —
  "we don't know how many activities will be created per-office". A percentage would be fiction,
  which is also why this is a board and not a chart. Do not let it drift into a progress bar.
- The assigned program count uses the same office resolution as
  `AllocationService.GetProgramAssignmentsAsync` — group rows matched on `aip_offices.office_id` —
  but is counted inside the band's existing `GetOfficeRollupsAsync` query. ↩️ *Calling that method
  per office*, as this line first said, is five round trips an office: ~95 for nineteen offices on
  every dashboard load (`PERFORMANCE_GUIDELINES.md`). ⚠️ Count programs with their own subquery, not
  over the rollup's activity join, or a program with three activities counts three times.

**Clicking an office — a card or a Not Started row — opens the AIP Review search (§6.1) filtered to
that office.** Not Started offices are clickable too: their LDIP programs are already in the AIP, so
the search has rows to show, and every office on the board opens the same way.

↩️ **The table's Submission column becomes real.** `SubmissionStatus` was the constant `Todo`
"until Phase 4". Left that way, the two views would contradict each other on one screen — PPDO would
read *Returned* on the board and *Todo* in the table — so PPDO-78 derives it from the same workflow
state: `Draft` → Todo · `DepartmentReview` and `ReturnedByPpdo` → In progress · `SubmittedToPpdo` →
Review · `Consolidated` → Done.

| State | Content |
|---|---|
| **Loading** | The band's skeleton, shaped like the active view — five column headers over grey cards for the board; the existing table skeleton for the table |
| **Empty column** | "No offices" inside the column. The column stays, so the board keeps its shape |
| **Empty band** | Unchanged — "No offices have a FY \<year\> ceiling yet." |
| **Error** | Unchanged — per band, with Retry; one failing band never blanks the page |

### 6.4 Sidebar (PPDO-79)

Budget Planning already has `AIP` and `AIP Entry` as two flat children. Add **`AIP Review`** as a
third sibling — ⚠️ **not** a nesting level: `Sidebar.tsx` has exactly one collapsible level and no
nesting primitive, and WFP already sets the flat-item-to-sub-page precedent. Hidden, not disabled,
for users without the grant.

⚠️ Three items reading AIP / AIP Entry / AIP Review is one more than most readers will parse. Rename
the existing list item to **`AIP Records`** as part of this ticket.

↩️ **Widened in the PPDO-79 follow-up (decision 22):** `AIP Review` is shown for **either** reviewer
flag, and points at the search. It stays hidden for everyone else, including a PPDO division encoder.

### 6.5 Notifications (PPDO-75)

Sidebar pending count beside AIP Review, resolved per person. ⚠️ Read it from **shared context**
mounted in the portal layout — not fetched per component (the WFP page once fired `/auth/me` four
times a load). An encoder gets no queue, but does see when their own office has been returned.

---

## 7. Non-goals

- **The printable AIP form.** Phase 5 (V18-60), and the stakes are higher there — it is presented to
  the PDC and then the Sanggunian.
- **Recording PDC / SP outcomes.** Generation is in scope for Phase 5; outcome tracking is open
  (tracker B14).
- **Offline review.** Phase 6.
- **Amendment after approval** (V18-73) and **"PBO Connect" / "Recall AIP"** (tracker W10 — a later
  stage, outside PPDO).
- **Returning part of an office.** One office at a time is the confirmed granularity (tracker B5).
  Returning a single program or activity is not built; the comments carry that precision.
- **Comment threading, replies, mentions, attachments.** Decision 9. Resolve is the only action.
- **A deadline or overdue state.** Decision 19.
- **Email or push notification.** Decision 20.
- **Editing by the PPDO reviewer.** Decision 2 — permanently, not "not yet".

---

## 8. Deployment notes

⚠️ **One new migration: `AddAipReviewComments`** (§5.1). Additive — one new table, no data rewrite.

**CI does not run EF migrations.** It must be applied manually against Azure SQL:

```bash
cd backend && dotnet ef database update --project PPDO.Infrastructure --startup-project PPDO.Functions
```

- No new NuGet or NPM dependencies.
- No new environment variables, and no CORS change — all routes are on the existing Function App.
- ⚠️ **`release/1.8.0` still has PPDO-54's `AddAipProcurementItems` unapplied** at the time of
  writing. Both migrations must be applied before the release deploys; `AddAipReviewComments` does
  not depend on it, but a single `database update` run applies both.
- Ordering: no step must precede the deploy other than the migration.

---

## 9. Ticket split

Already created under epic **PPDO-67**. This spec is PPDO-68 and blocks the rest.

| Ticket | Section | Blocked by |
|---|---|---|
| **PPDO-68** — this spec | — | — |
| **PPDO-69** — second submit | §3.2, §4 | PPDO-68 |
| **PPDO-70** — locking | §3.2, decision 4/5 | PPDO-68, PPDO-69 |
| **PPDO-71** — comments | §5.1, §6.2 | PPDO-68 |
| **PPDO-72** — return + re-submit | §3.3, §4.2 | PPDO-68, PPDO-70, PPDO-71 |
| **PPDO-73** — consolidated view | §3.3, decision 13/14 | PPDO-68 |
| **PPDO-74** — PPDO review screen | §6.2 | PPDO-68, PPDO-71, PPDO-72 |
| **PPDO-75** — notifications | §6.5 | PPDO-68, PPDO-69 |
| **PPDO-76** — query-first page | §4.1, §5.3, §6.1 | PPDO-68 |
| **PPDO-77** — history | §4, §5.2, §6.2, §6.2a | PPDO-68, PPDO-72 |
| **PPDO-78** — readiness board | §6.3, §4 | PPDO-68 |
| **PPDO-79** — sidebar | §6.4 | PPDO-68, PPDO-76 |

⚠️ **PPDO-70 also carries the Phase 3 correction** required by decision 4 — `IsEncoderEditable` must
accept `Draft`, `DepartmentReview` and `ReturnedByPpdo`, and its remarks must be rewritten. Its
"accept the shipped behaviour" branch is now closed.

⚠️ **PPDO-74 gains the Accept action** from decision 6, which it did not have when it was written.

---

## 10. Acceptance checklist

```
- [ ] An encoder edits an activity while their office is in Department review and the save succeeds
- [ ] The same encoder editing after the department head submits to PPDO sees
      "Cannot edit this office's AIP while it is in review by PPDO"
- [ ] An encoder pressing submit-to-PPDO is refused; the department head pressing it succeeds
- [ ] A department head who changes an amount during review, pushing the office over its GF
      ceiling, is refused at submit-to-PPDO naming the fund, ceiling, total and overage
- [ ] Submitting to PPDO moves every sub-office group of that office, not just the first
- [ ] A PPDO reviewer leaves a comment on an activity row while the office is locked, and it saves
- [ ] A PPDO reviewer attempting to edit any value gets 403
- [ ] The office cannot resolve the PPDO reviewer's comment (the control is absent, and the API
      returns 403 if called directly)
- [ ] An encoder cannot resolve their own department head's comment
- [ ] Re-submitting with 2 unresolved PPDO comments and 1 unresolved department-head comment shows
      a warning naming both counts separately, and Proceed completes the re-submit
- [ ] Re-submitting with everything resolved shows no warning
- [ ] Returning an office unlocks editing for both the encoder and the department head
- [ ] Accepting an office moves it to the readiness board's Done column
- [ ] Two browser windows: reviewer A returns the office, reviewer B then accepts and gets a 409
      naming the current state
- [ ] AIP Review opens with no results and copy explaining how to search — not a blank panel
- [ ] Filtering by office 010 with sector blank returns rows from BOTH of PPDO's sectors
- [ ] Typing "1000-000-1-01-010 OR 3000-000-1-01-010" in the ref-code box returns both
- [ ] A title containing the word "or" is matched literally, not split
- [ ] An office with LDIP-seeded programs and zero activities sits in Not Started, not In Progress
- [ ] A returned office appears in Office Review with a Returned badge, not in a sixth column
- [ ] No card anywhere shows a percentage
- [ ] An office encoder sees no Offices band and no board on the dashboard, and AIP Review is absent
      from their sidebar
- [ ] A cross-office reviewer's Offices band opens on Board; switching to Table and reloading keeps
      Table
- [ ] A budget officer sees the Offices table with no Board / Table switch
- [ ] Clicking a card, and clicking a Not Started row, each open AIP Review search filtered to that
      office
- [ ] For every office, the board's column and the table's Submission column agree — the table no
      longer shows a constant Todo
- [ ] With 17 offices at zero activities, Not Started renders as a compact list, not a tall column
      of cards
- [ ] Show History on a returned-and-re-submitted office lists submit, return and re-submit with
      names and timestamps, and shows one row per transition — not one per sub-office group
- [ ] An encoder opens History from AIP Entry on their own office and sees the same chain; the API
      answers 404 for another office's history
- [ ] A comment written while the office was with PPDO sits under that "Sent to PPDO" entry, not
      under the later return
- [ ] History opens on an accepted office, and on a Draft office with the empty state — not only on
      an office at PPDO
- [ ] The consolidated view with 3 of 19 offices submitted shows the other 16 as not yet submitted,
      never as ₱0
- [ ] A PPDO division user without a reviewer flag cannot open the consolidated view
- [ ] An FY2027 record shows no workflow controls, no comment gutters and no submit
```

---

## 11. Test focus

TDD is mandatory here — this is permission resolution and workflow logic, both on `CLAUDE.md`'s
always-TDD list.

| Class | Cover |
|---|---|
| `AipSubmitGateTests` | The second hop from `DepartmentReview` **and** `ReturnedByPpdo`; refusal for an encoder; the gate re-running on the second hop; **every group moving together** |
| `AipWorkflowGateTests` | The full (state × role) matrix of §3.2 — including the three cells decision 4 changes. ⚠️ Add the failing test for encoder-edits-in-`DepartmentReview` **first**; it should fail against current `main` |
| `AipReviewCommentServiceTests` | Authoring-side resolve, both refusal directions (office→PPDO, encoder→department head); comments allowed while locked; unresolved counts split by side; orphaned comment survives node deletion. **History (PPDO-77):** one entry per audit row found across every group id; comments bucketed under the hand-off open when written; the before-first-submission bucket; a re-submit read from the old status; an encoder reads their own office and gets 404 for another |
| `AipReviewServiceTests` | Return/accept state guards; the 409 on a concurrent second action; accept refused from `ReturnedByPpdo` |
| `AipReviewSearchTests` | OR-within / AND-across; `officeIds` + blank `sectors` returning multiple sectors; ref-code OR-list; title **not** OR-split; scope clamping for a guest-office user |
| `BudgetPlanningDashboardServiceTests` (`GetOfficesAsync`) | `ReadinessColumn` for every state — zero activities → NotStarted, one or more in `Draft` → InProgress, `DepartmentReview` → OfficeReview, `ReturnedByPpdo` → OfficeReview with `IsReturned`, `SubmittedToPpdo` → PpdoReview, `Consolidated` → Done; a submission state beating activity count; `AssignedProgramCount` summed across an office's groups; groups that disagree report the least advanced; an office with no group row reads Not Started / Todo; `SubmissionStatus` derived, never the constant |
| `AipOfficeRollupRepositoryTests` (SQLite) | `ProgramCount` counted apart from the activity join; an untouched office still counts its seeded programs; each group's `WorkflowStatus` carried |
| `ReviewerWriteGuardTests` | Unchanged behaviour — plus a new test that **commenting is not denied** by the guard |
| `PermissionMatrixTests` | No new flag, so no new row — assert that, so a flag added here fails the build |

⚠️ Functions handlers are covered by integration tests, not unit tests (`CLAUDE.md`) — happy path
plus the 403/404/409 cases named in §4.2.

---

*Companion to `AIP_Foundation_Spec.md` (Phase 2) and `AIP_Entry_Spec.md` (Phase 3). Supersedes
`Phase_Plan.md` §6 for Phase 4; §12.4–§12.8 remain the plan's current text.*
