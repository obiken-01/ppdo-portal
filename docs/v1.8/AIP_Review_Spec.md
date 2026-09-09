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
| Happy path — accept | Office at `SubmittedToPpdo` | PPDO reviewer accepts it | → `Consolidated`. Appears in the kanban's Done column and counts toward the consolidated totals |
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
| Office user opens the kanban | An office encoder | Navigates to the board | **Not available to them** (§6.3). Their own status is a chip on their own AIP page |
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
| `POST /api/budget-planning/aip/comments` | Any role that may comment (§3.1) | ⚠️ Must **not** route through `ReviewerWriteGuard` |
| `POST /api/budget-planning/aip/comments/{id}/resolve` | Authoring side only (decision 8) | 403 for the recipient |
| `GET /api/budget-planning/aip/review/search` | `CanAccessBudgetPlanning`, scoped by `ResolveForReview` | Query-first (§4.1). **Slim DTO, paginated** |
| `GET /api/budget-planning/aip/{aipId}/readiness-board` | `CanReviewAllOffices` | The kanban: one row per office with state, program count, activity count |
| `GET /api/budget-planning/aip/{aipId}/consolidated` | `CanReviewAllOffices` | Partial by design (decision 14). Slim, server-aggregated |
| `GET /api/budget-planning/aip/{aipId}/offices/{officeId}/history` | Same as the review read | Transitions + comments, newest first (§5.2) |
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

⚠️ **Per decision 21 an office has several `AipOffice` rows**, so a single transition writes several
audit rows. The history read must **group by transition** (same actor, same action, same second) or
it will show one submit five times.

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
| **Empty (initial)** | ⚠️ **The default, and deliberate.** Filter panel plus copy explaining how to search, and the kanban below it as the non-empty landing. Never a blank panel |
| **Loading** | Skeleton rows matching the result table — same header, same row height. Never a centered spinner (CLS) |
| **Empty (no matches)** | Distinct from the initial state: "No AIP rows match these filters", with a clear-filters action |
| **Success** | Result rows linking into 6.2. Chips show per-value counts, as PR List does |
| **Error** | Inline error with retry; filters preserved |
| **Forbidden** | The page is **hidden from the sidebar** for users without a reviewer flag, not disabled |

### 6.2 AIP Review — one office (PPDO-74)

| State | Content |
|---|---|
| **Success** | The office's tree, read-only, with a comment gutter per row. State chip at the top naming where the work sits and who holds it |
| **Comments collapsed** | ⚠️ **Default.** Rows carrying comments get a marker and a show-comments control; bodies open deliberately, so the grid stays readable (§12.5) |
| **Unresolved filters** | Two buttons with counts — "N from PPDO", "N from department head" — filtering the tree to commented rows (decision 11) |
| **Read-only / forbidden** | An office viewing its own work at `SubmittedToPpdo` sees inputs **replaced by text**, not disabled inputs, plus a banner naming the state. A PPDO reviewer sees the same, since they never edit |
| **Actions** | Return and Accept for PPDO reviewers only; both confirm via `ConfirmDialog` naming the office. Submit-to-PPDO for the department head |
| **Validation** | Comment body required, 2000 char cap shown as a counter |

### 6.3 Readiness kanban (PPDO-78)

Five columns: **Not Started · In Progress · Office Review · PPDO Review · Done**.

- ⚠️ **"Returned by PPDO" is NOT a sixth column.** Returned work re-enters the department-review
  condition, so it sits **in Office Review with a "Returned" badge** — keeping the reviewer's most
  actionable signal ("I sent this back; has it come back?") without column sprawl.
- ✅ **Not Started vs In Progress is decided by activity count**, not seeded programs: zero → Not
  Started. Programs arrive from LDIP without anyone in the office touching the record, so counting
  them would show an office that never opened the page as working. Submission states win over both.
- Each card: office name, **assigned program count**, **created activity count**. The assigned
  denominator reuses `AllocationService.GetProgramAssignmentsAsync`'s LDIP→AIP resolution —
  **do not re-derive it**.
- ⚠️ **No "% complete".** There is a denominator for programs but **none for activities** — "we don't
  know how many activities will be created per-office". A percentage would be fiction. This is also
  why it is a kanban and not a chart. Do not let it drift into a progress bar.
- Clicking a card filters 6.1 to that office.
- **PPDO reviewers only.** An office user would see one card, which is noise.

### 6.4 Sidebar (PPDO-79)

Budget Planning already has `AIP` and `AIP Entry` as two flat children. Add **`AIP Review`** as a
third sibling — ⚠️ **not** a nesting level: `Sidebar.tsx` has exactly one collapsible level and no
nesting primitive, and WFP already sets the flat-item-to-sub-page precedent. Hidden, not disabled,
for users without the grant.

⚠️ Three items reading AIP / AIP Entry / AIP Review is one more than most readers will parse. Rename
the existing list item to **`AIP Records`** as part of this ticket.

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
| **PPDO-77** — history | §5.2 | PPDO-68, PPDO-72 |
| **PPDO-78** — kanban | §6.3 | PPDO-68, PPDO-76 |
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
- [ ] Accepting an office moves it to the kanban's Done column
- [ ] Two browser windows: reviewer A returns the office, reviewer B then accepts and gets a 409
      naming the current state
- [ ] AIP Review opens with no results and copy explaining how to search — not a blank panel
- [ ] Filtering by office 010 with sector blank returns rows from BOTH of PPDO's sectors
- [ ] Typing "1000-000-1-01-010 OR 3000-000-1-01-010" in the ref-code box returns both
- [ ] A title containing the word "or" is matched literally, not split
- [ ] An office with LDIP-seeded programs and zero activities sits in Not Started, not In Progress
- [ ] A returned office appears in Office Review with a Returned badge, not in a sixth column
- [ ] No card anywhere shows a percentage
- [ ] An office encoder cannot reach the kanban, and AIP Review is absent from their sidebar
- [ ] Show History on a returned-and-re-submitted office lists submit, return and re-submit with
      names and timestamps, and shows one row per transition — not one per sub-office group
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
| `AipReviewCommentServiceTests` | Authoring-side resolve, both refusal directions (office→PPDO, encoder→department head); comments allowed while locked; unresolved counts split by side; orphaned comment survives node deletion |
| `AipReviewServiceTests` | Return/accept state guards; the 409 on a concurrent second action; accept refused from `ReturnedByPpdo` |
| `AipReviewSearchTests` | OR-within / AND-across; `officeIds` + blank `sectors` returning multiple sectors; ref-code OR-list; title **not** OR-split; scope clamping for a guest-office user |
| `AipReadinessBoardTests` | Zero activities → Not Started; returned → Office Review + badge; program count reusing the allocation resolution |
| `ReviewerWriteGuardTests` | Unchanged behaviour — plus a new test that **commenting is not denied** by the guard |
| `PermissionMatrixTests` | No new flag, so no new row — assert that, so a flag added here fails the build |

⚠️ Functions handlers are covered by integration tests, not unit tests (`CLAUDE.md`) — happy path
plus the 403/404/409 cases named in §4.2.

---

*Companion to `AIP_Foundation_Spec.md` (Phase 2) and `AIP_Entry_Spec.md` (Phase 3). Supersedes
`Phase_Plan.md` §6 for Phase 4; §12.4–§12.8 remain the plan's current text.*
