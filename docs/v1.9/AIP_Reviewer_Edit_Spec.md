---
status: draft
version: v1.9.0
tickets: PPDO-211 (epic), PPDO-212 T1, PPDO-213 T2, PPDO-214 T3, PPDO-215 T4
supersedes: AIP_Review_Spec.md decision 5 (in part), Permission_Matrix.md §5 (in part)
---

# v1.9.0 — PPDO Reviewer Edits to a Submitted AIP

> Source: Demo 3 / meeting, 2026-10-13 ("Half AIP edit by LFC"). Settled with Ralph 2026-10-14.
> **Must be ready for the November 2026 user training.** Revised 2026-10-14: edit comments are
> created resolved and the office acknowledges them separately (see decisions 6, 7).

## 1. Goal

Some activities are scheduled for only part of the year (e.g. a 6-month scope), so their account
amounts cover only those months. When the Local Finance Committee (LFC) adjusts the AIP, the **PPDO
reviewer** brings such an activity's account amounts up to cover the whole year, directly on the
office's submitted AIP, instead of returning it to the office. The office sees what changed, who
changed it, and why.

## 2. Decisions (settled)

1. **Who:** a holder of `CanReviewAllOffices`, the existing PPDO cross-office reviewer. No new flag
   (Ralph, 2026-10-14). SuperAdmin can already write and is unaffected.
2. **When:** only while the office's AIP is in **`SubmittedToPpdo` or `Consolidated`**, FY2028+
   (entered) records only. An edit does **not** change the workflow state.
3. **What:** on an existing activity, the reviewer may **edit an expenditure line's amounts** and
   **add a new expenditure line (account)**. Nothing else: no programs, projects or activities added,
   removed or renamed, and no activity details (title, schedule, outputs, CC tagging).
4. **No ceiling check.** This is already how saves work: the ceiling is enforced only at submit
   (DECISION C, `IAipCeilingService`), never during entry. The ceiling ledger still updates after
   the write, so every screen shows the office's real position, including over-ceiling.
5. **A reason is required** on every reviewer edit (1–500 characters).
6. **The office is told through the review comments that already exist.** Each reviewer edit posts
   one PPDO-side comment, anchored to the activity, whose text is written by the system:
   `PPDO edited <account no.> <account title>: <PS|MOOE|CO> ₱<old> → ₱<new>. Reason: <reason>`
   (for an added line: `PPDO added <account …>: <column> ₱<amount>. Reason: …`). It shows up in the
   AIP Review screen, the activity's comment thread and the History modal (PPDO-77) with no new UI
   there. The existing audit row on `aip_expenditures` keeps the structured before/after.
   ⚠️ **The comment is created already resolved** (by the reviewer, at creation). Only the authoring
   side may resolve a comment, and unresolved PPDO comments feed the soft re-submit gate and the
   dashboard's open-comment counts (`AipReviewCommentService.Tally`, `CountUnresolvedBySideAsync`).
   An edit is a record, not a request, so it must not show up as an open item the office cannot
   clear.
7. **Notice for the office** (the PPDO-75 pattern): "PPDO made N changes to your AIP"
   while any reviewer-edit comment is **unacknowledged**. Acknowledging is a separate action from
   resolving: any user who can read that office's AIP (encoder or department head) clicks
   **Mark as seen**, once, for all of the office's pending edit comments. It never touches the
   resolve rule.
   The general notification inbox (shared with SAIP) is **not** part of this; it stays v1.9.0 later.
8. **`ReviewerWriteGuard` is not loosened globally.** It stays on all budget-planning write
   endpoints. Only the two expenditure endpoints (`POST …/activities/{id}/expenditures`,
   `PUT …/expenditures/{id}`) let a cross-office reviewer through, and the service then allows only
   the reviewer path in decision 2's states. `DELETE` stays denied.
9. **The concurrent-edit guard applies as today** (`rowVersion`). Offices cannot edit in these
   states, so a clash can only be two reviewers on one line.

### Open follow-ups (A settled; B and C stand on their defaults — Ralph started T1 on them, 2026-10-14)

- ✅ **A. Procurement-itemised lines — settled (Ralph, 2026-10-14).** A procurement line's amount
  *is* its items' total, so editing "the amount" means editing items. The reviewer may change item
  quantities, add items (same Price Index picker), **and add new account lines** (decision 3); the
  comment lists the line's old → new total.
- **B. Activity schedule.** If the edit annualises the amounts, should Start/End month also become
  Jan–Dec? *Default:* **no**, schedule stays the office's (decision 3); the reason text records it.
- **C. Undoing a mistaken added line.** Delete stays denied. *Default:* the reviewer edits it to the
  correct figure; if the line should not exist, Return the office (existing flow) or ask SuperAdmin.
- ~~**D. Who resolves a reviewer-edit comment.**~~ Settled by checking the code (2026-10-14): only
  the authoring side may resolve, by design. See decisions 6 and 7: the comment is created resolved,
  and the office acknowledges it instead.

## 3. Behaviour

| # | Given | When | Then |
|---|---|---|---|
| 1 | Office X in `SubmittedToPpdo`; PPDO reviewer | Edits MOOE line ₱60,000 → ₱120,000 with a reason | 200; line, activity totals, ceiling ledger updated; workflow state unchanged; one PPDO comment on the activity with the generated text; audit Update row |
| 2 | Office X in `Consolidated` | Reviewer adds a new account line with a reason | 201; line added; comment `PPDO added …`; office X stays `Consolidated` |
| 3 | Edit takes office X over its ceiling | Reviewer saves | Saves (no ceiling check). Readiness/consolidated screens show the overage as they already do |
| 4 | No reason / blank / > 500 chars | Reviewer saves | 400 `"A reason is required for a PPDO edit (up to 500 characters)."`; nothing saved |
| 5 | Office X in `Draft`, `DepartmentReview` or `ReturnedByPpdo` | Reviewer saves | 400 `"PPDO can edit this office's AIP only after it is submitted to PPDO."` (the office's own turn) |
| 6 | FY2027 (legacy) record | Reviewer saves | 400, same message as the record-format refusal today |
| 7 | Reviewer | `DELETE` an expenditure, or any program/project/activity write | 403 as today (`ReviewerWriteGuard`) |
| 8 | Two reviewers edit the same line | Second saves with an old `rowVersion` | 409 conflict panel, as today |
| 9 | Department-head reviewer or encoder of office X | Tries to edit in `SubmittedToPpdo`/`Consolidated` | 400 as today; nothing about their rules changes |
| 10 | Office X user | Opens AIP Entry after a reviewer edit | Notice "PPDO made N changes to your AIP" linking to their AIP; **Mark as seen** stamps every pending edit comment of office X and the notice clears |
| 10a | Reviewer edit posted | Office X is later returned and re-submits | Edit comments do not count as unresolved; the soft re-submit gate and the dashboard's open-comment counts are unchanged by edits |
| 11 | Office Y user | Anything | Never sees office X's notice or comments (office scope unchanged) |
| 12 | Encoder sends `reviewerReason` | Normal save in `Draft` | Field ignored; no comment posted |
| 13 | Reviewer edits an office released to the external API | — | The GSO's next `GET external/v1/aip` returns the new figures (no extra step) |

## 4. API contract

No new routes. Two existing endpoints accept one new optional body field:

### `POST /api/budget-planning/aip/activities/{activityId}/expenditures`
### `PUT  /api/budget-planning/aip/expenditures/{id}`

JWT + `CanAccessBudgetPlanning`. Authorization changes from `AuthorizeWriteAsync` to a variant that
lets a cross-office reviewer through to the service, which enforces decision 2.

```json
{ "...existing fields...": "", "reviewerReason": "Annualised: activity runs Jan–Jun only" }
```

| Status | When |
|---|---|
| 200 / 201 | Saved (response shape unchanged) |
| 400 | Missing/too-long reason (reviewer path); wrong state; legacy FY; existing validations |
| 403 | No Budget Planning access; reviewer on any endpoint other than these two |
| 404 | Activity/line not found or outside scope (reviewer scope = all offices) |
| 409 | Stale `rowVersion` |

`GET` notifications (`IAipNotificationService`) gains `reviewerEditCount` (unacknowledged edit
comments of the caller's own office).

### `POST /api/budget-planning/aip/{aipId}/offices/{officeId}/comments/acknowledge-edits` — JWT + Budget Planning access + own-office read scope

Stamps `acknowledged_at`/`acknowledged_by_id` on every unacknowledged reviewer-edit comment of that
office. 200 `{ acknowledged: n }` (0 is fine, idempotent). 404 outside scope. A PPDO reviewer acting
for PPDO (not their own office) gets 403: the acknowledgement belongs to the office.

## 5. Data model changes

### `aip_review_comments` (snake_case) — one additive column

| Column | Type | Null | Notes |
|---|---|---|---|
| `is_reviewer_edit` | `bit` | no, default `0` | Marks system-written edit comments, so the notice can count them and the UI can style them |
| `acknowledged_at` | `datetime2` | yes | Office's "Mark as seen"; only ever set on reviewer-edit rows |
| `acknowledged_by_id` | `uniqueidentifier` | yes | FK `Users.Id`, no cascade |

⚠️ **MIGRATION** — additive, default `0`, safe to run before the code. CI does not run it.

## 6. UI states

### AIP Review — one office (PPDO-74), reviewer only, office in `SubmittedToPpdo`/`Consolidated`
- Each activity gets **Edit amounts**. Hidden in other states and for non-reviewers.
- Opens the AIP Entry expenditure editor for that activity in a panel, with the banner
  **"Editing <Office>'s AIP as PPDO reviewer. The office will see each change and your reason."**
- **Add account** visible; **delete** hidden. Activity details read-only.
- **Reason** textarea above Save, required; Save disabled until it has text; counter at 500.
- Loading: same skeleton as AIP Entry. Error: inline banner, entered values kept.
  Conflict: the existing conflict panel. Validation: under the field, as AIP Entry does.
- After save: the row's totals update in place and the new comment appears in the thread.

### Office side
- Reviewer-edit comments render with an "Edited by PPDO" tag (reads `is_reviewer_edit`) and "Seen by …" /
  "Not yet marked as seen by the office" instead of "resolved"; a row's comment button counts them as
  "N PPDO edits", never as resolved.
- ↩️ **Where the notice lives (as built, PPDO-214):** the PPDO-75 "Returned" pattern rather than a
  dashboard card. A **"PPDO edited" pill** beside AIP Entry in the sidebar (every page; Returned takes
  the slot when both apply) and a **banner with Mark as seen** at the top of AIP Entry for that year.
  "Changes", not "lines": one line edited twice is two changes.
- Notice per decision 7 with **Mark as seen**; hidden when nothing is unacknowledged.
  Mark as seen: button shows a spinner and disables while saving; on error the notice stays with an
  inline "Couldn't mark as seen. Try again."

## 7. Non-goals

- No state change from an edit; no office sign-off on the change; no undo button (history is the record).
- No adding/removing/renaming programs, projects or activities by PPDO (that is SAIP).
- No ceiling check on reviewer edits.
- No general notification inbox, email or push (later v1.9.0, shared with SAIP).
- No new permission flag.

## 8. Deployment notes

- One additive migration (§5), run **before** the code in UAT and production.
- `Permission_Matrix.md` §5 and `AIP_Review_Spec.md` §3.1/§3.2 (decision 5, "Nobody edits in
  `SubmittedToPpdo`") get a superseded note pointing here; `PermissionMatrixTests` and
  `ReviewerWriteGuardCoverageTests` gain the two-endpoint exception.
- Tell the GSO that consolidated figures can now change after consolidation (row 13).

## 9. Ticket split

| Ticket | Scope | Depends on |
|---|---|---|
| T1 Backend | Reviewer path in `AipExpenditureService` (add + update), the two-endpoint guard exception, reason validation, generated comment (created resolved), matrix + guard tests (red-tested) | — |
| T2 Migration + notice | the three columns, notification count, acknowledge endpoint, comment DTO flag | T1 |
| T3 Frontend | Edit amounts panel from the review screen, reason field, "Edited by PPDO" tag, dashboard notice | T1, T2 |
| T4 Docs | Permission matrix §5, AIP Review Spec superseded notes | T1 |

## 10. Acceptance checklist

- [ ] As a PPDO reviewer, on an office in `SubmittedToPpdo`, change a MOOE line and save with a reason: totals update, office stays `SubmittedToPpdo`.
- [ ] Same on a `Consolidated` office, adding a new account line.
- [ ] Saving without a reason is refused with the message in §3 row 4.
- [ ] An edit that takes the office over its ceiling still saves.
- [ ] On a `Draft` office the Edit amounts button is absent, and a direct API call returns 400.
- [ ] Delete is not offered; a direct `DELETE` returns 403.
- [ ] Office user sees the "Edited by PPDO" comment with old → new and the reason, in the thread and the History modal.
- [ ] Office user sees the dashboard notice; **Mark as seen** clears it.
- [ ] After an edit, the office's open-comment count and the re-submit warning are unchanged.
- [ ] A user of another office sees none of it.
- [ ] Printed AIP / Annex B export show the new figures.

## 11. Test focus

- State matrix: reviewer × each workflow state (only two allowed), red-tested.
- Guard: the exception covers exactly two endpoints; every other write still 403s for the reviewer.
- Encoder/department-head behaviour unchanged in every state (regression).
- Generated comment text for update, add, and procurement-itemised lines.
- Notification count is office-scoped and counts only unacknowledged reviewer-edit comments.
- Edit comments never appear in `Tally` / `CountUnresolvedBySideAsync` (created resolved), red-tested.
- Acknowledge: own office only, idempotent, 403 for a PPDO reviewer on another office.
