---
type: spec
version: v1.10.0
status: draft
tickets: [PPDO-241]
updated: 2026-10-21
status_note: "draft — decisions settled with Ralph 2026-10-21, awaiting review"
tickets_note: "PPDO-241 (epic); tickets cut from §9 once accepted"
supersedes_note: "— (the v1.6 record-copy amendment flow stays for FY ≤ 2027 only)"
---

# v1.10.0 — SAIP (Supplemental AIP)

Up: [v1.10 hub](README.md)

Ralph's Demo 3 notes (2026-10-13), and the decisions settled with him on 2026-10-21.

## 1. Goal

Once an office's AIP is **Consolidated** ("marked as done"), nobody can change it. Offices still
need to change it during the year: add a project or activity, or change amounts and accounts. An
**SAIP** is that path. The office prepares the changes, the department head sends them to PPDO
with a **signed request letter**, and a PPDO reviewer approves or returns them. The official
figures change only on approval.

## 2. Decisions (settled)

1. **An SAIP belongs to one office and one fiscal year**, and starts only when that office is
   **Consolidated** in an **entered year** (FY2028+) whose AIP record is not Archived. FY ≤ 2027
   keeps the old v1.6 amendment flow.
2. **Several per year, one open at a time.** They are numbered per office and year: SAIP 1, SAIP 2…
   A new one can start only after the last one is Approved or Cancelled.
3. **Only the department head starts it** (Admin of the office, or `CanReviewBudgetPlanning`).
   After that, encoders edit as they do in AIP Entry.
4. **Working copy.** Starting an SAIP copies the office's approved AIP tree (programs, projects,
   activities, expenditures, procurement items) into a **draft copy**. All SAIP edits go to the
   copy. **The approved AIP is untouched until approval**, so every report, the review screens and
   the GSO feed keep showing the approved figures with no change to how they read.
5. **Allowed changes:** add a project (with its Investment Proposal), add an activity, change an
   activity, add or change accounts (expenditures) and their amounts. **Nothing approved can be
   deleted.** The office lowers it to ₱0 instead. Rows added in this SAIP can be deleted while it
   is open.
6. **Same office steps as the AIP:** encoders submit their division's part to the department head
   (the existing division submit), and the department head sends it to PPDO.
7. **The letter is required, one per SAIP.** PDF, JPG or PNG, up to 10 MB. It can be replaced until
   the SAIP is sent to PPDO, then it is locked. It is stored with `IFileStore` (Blob in Azure,
   local disk in dev). The office's AIP readers and PPDO reviewers can view and download it.
8. **A new project's Investment Proposal must be Finalized** before the head can send the SAIP to
   PPDO, the same gate as the AIP.
9. **A PPDO reviewer approves or returns it**, on the same review screen as the AIP. A return
   reopens the copy for the office, like a returned AIP. **Reviewer edits apply** with the same
   rules as PPDO-211: amounts only, a reason required, the office is notified.
10. **The same checks as the AIP:** ceiling warnings, enforced fund ceilings (PPDO-216) and the
    division allocation warning (PPDO-237), all measured on the **amended totals**.
11. **On approval** the copy's changes are applied onto the approved AIP **keeping its row IDs**, so
    comments, proposals, links and external IDs stay valid. New rows are inserted. The office is
    Consolidated again. A **before → after list** of every change is saved with the SAIP.
12. **Cancel:** the department head can cancel an SAIP any time before approval. The draft copy is
    deleted, and the SAIP stays in the history as Cancelled. Its letter is kept for the record.
13. **Reports after approval** show the amended figures in the consolidated version (Annex B,
    initial WFP, GSO feed), as they do for any Consolidated office today.
14. **SAIP history:** per office and year, a list of SAIPs (number, status, dates, who, the letter)
    and, for each approved one, its before → after changes.
15. **SAIP report:** the Annex B form and its Excel export, titled "SAIP {n} — FY {fy} — {Office}",
    listing **only the PPAs that SAIP added or changed**, with their **amended** amounts.
16. **Notifications** (the v1.9.0 bell): SAIP sent to PPDO → PPDO reviewers; returned or approved →
    the office's AIP readers; a division submitted → the department head (existing event). Comment
    threads on an SAIP follow PPDO-239's rule.

### Open follow-ups (not blocking)

- A. Whether the PPDC or finance must also sign off on an SAIP (today: a PPDO reviewer only).
- B. Whether an SAIP may move an activity to another fund source or division (default: yes, as in
  AIP Entry, re-checked against ceilings).
- C. Whether the SAIP report needs the before → after figures too (default: amended only, decision 15).

## 3. Behaviour

| Given | When | Then |
|---|---|---|
| PTO is Consolidated for FY2028, no open SAIP | PTO's head clicks **Start SAIP** | SAIP 1 is created as Draft; the draft copy is made; AIP Entry opens the copy with an "SAIP 1 — draft" banner |
| PTO has SAIP 1 open | The head clicks **Start SAIP** again | Refused: "SAIP 1 is still open." (409) |
| PTO is in Department review (not Consolidated) | The head tries to start an SAIP | Refused: "An SAIP starts once your AIP is marked as done." (409) |
| FY2027 (not an entered year) | Anyone tries to start an SAIP | Refused (400); FY ≤ 2027 uses the old amendment flow |
| SAIP 1 open | A PTO encoder opens AIP Entry for FY2028 | They see and edit the draft copy, within their division as today |
| SAIP 1 open | An encoder deletes an approved activity | Refused: "Approved items can't be deleted in an SAIP. Set the amount to ₱0." (400) |
| SAIP 1 open | An encoder deletes an activity added in SAIP 1 | Deleted |
| SAIP 1 open | Anyone opens Annex B, the initial WFP or the GSO feed | They show the **approved** figures, unchanged |
| SAIP 1, all divisions submitted, no letter | The head clicks **Send to PPDO** | Blocked: "Attach the signed request letter first." |
| SAIP 1 adds a project whose proposal is Draft | The head clicks **Send to PPDO** | Blocked: lists the projects whose proposal is not Finalized |
| SAIP 1 pushes PTO over an enforced fund ceiling | The head clicks **Send to PPDO** | Blocked, as for the AIP (PPDO-216) |
| SAIP 1 sent to PPDO | A PTO user tries to edit | Refused, as for a submitted AIP (locked) |
| SAIP 1 sent | A PPDO reviewer edits an amount with a reason | Saved on the copy; office notified (PPDO-211 rules) |
| SAIP 1 sent | A reviewer **returns** it | Back to the office for changes; office notified |
| SAIP 1 sent | A reviewer **approves** it | Changes applied to the approved AIP (IDs kept), before → after saved, PTO Consolidated, copy removed, office notified |
| SAIP 1 Draft or Returned | The head clicks **Cancel SAIP** and confirms | Copy deleted; SAIP 1 shown as Cancelled; AIP unchanged |
| SAIP 1 sent to PPDO | The head clicks **Cancel SAIP** | Allowed (decision 12); reviewers are notified it was withdrawn |
| SAIP 1 approved | Anyone opens the SAIP report for SAIP 1 | Annex B rows for the added or changed PPAs only, amended amounts |
| Two encoders edit the same activity in the copy | Both save | The concurrency guard (PPDO-121) applies to the copy as to the AIP |

**Per role**

| Role | Start / Cancel | Edit copy | Send to PPDO | Approve / Return | View history, letter, SAIP report |
|---|---|---|---|---|---|
| Office department head | ✅ | ✅ | ✅ | ❌ | ✅ own office |
| Office encoder | ❌ | ✅ own division | ❌ (division submit only) | ❌ | ✅ own office (report: if they can open Report, PPDO-240) |
| PPDO reviewer (`CanReviewAllOffices`) | ❌ | amounts only once sent (decision 9) | ❌ | ✅ | ✅ every office |
| Another office's user | ❌ | ❌ | ❌ | ❌ | ❌ (404, same as the AIP) |
| SuperAdmin | as head | ✅ | ✅ | ✅ | ✅ |

## 4. API contract (all JWT; same office and division scope as AIP Entry and AIP Review)

| Route | Who | Errors |
|---|---|---|
| `POST /api/budget-planning/aip/{fy}/offices/{officeId}/saips` | Office head | 409 not Consolidated / one already open; 400 not an entered year; 403 not the head; 404 other office |
| `GET  …/offices/{officeId}/saips` | Office readers, reviewers | 404 other office |
| `GET  …/saips/{saipId}` | same | 404 |
| `PUT  …/saips/{saipId}/letter` (multipart) | Office head, while Draft/Returned | 400 type/size; 409 locked |
| `GET  …/saips/{saipId}/letter` | Office readers, reviewers | 404 none |
| `POST …/saips/{saipId}/submit` | Office head | 409 not all divisions submitted / no letter / proposal not Finalized / enforced ceiling |
| `POST …/saips/{saipId}/return` | Reviewer | 409 not sent |
| `POST …/saips/{saipId}/approve` | Reviewer | 409 not sent; 409 the approved AIP changed since the copy (should not happen; guard) |
| `POST …/saips/{saipId}/cancel` | Office head | 409 already Approved/Cancelled |
| `GET  …/saips/{saipId}/changes` | Office readers, reviewers | 404 |
| `GET  …/saips/{saipId}/report` and `/report/export` | Report readers (PPDO-240 rule) | 404 not approved |

The existing AIP Entry, division submit, comment and reviewer-edit endpoints work on the copy when
an SAIP is open: they take the office's **open SAIP's copy** as their tree. Error bodies use the
existing `ApiResponse` envelope.

## 5. Data model changes (snake_case, additive)

### `aip_saips`
`id`, `aip_office_id` (the approved office row), `number`, `status` (Draft, DepartmentReview,
SubmittedToPpdo, ReturnedByPpdo, Approved, Cancelled), `draft_aip_office_id` (null once Approved or
Cancelled), `letter_file_key`, `letter_file_name`, `letter_content_type`, `letter_size_bytes`,
`started_by_id`/`started_at`, `submitted_by_id`/`submitted_at`, `decided_by_id`/`decided_at`,
`cancelled_by_id`/`cancelled_at`, `row_version`. Unique (`aip_office_id`, `number`), and at most
one open SAIP per office (filtered unique index on status not in Approved/Cancelled).

### The draft copy
A **second `aip_offices` row** in the same record, with `saip_id` set, and its own programs,
projects, activities, expenditures and items. Every copied row carries `source_id` → the approved
row it came from (null for rows added in the SAIP).
⚠️ **Every existing read of `aip_offices` must skip draft copies.** Add an EF global query filter
(`saip_id IS NULL`) on `AipOffice` and use `IgnoreQueryFilters()` only in SAIP code paths, so a
report, review list or the GSO feed can never pick up unapproved figures by accident.

### `aip_saip_changes`
Written at approval: `saip_id`, `node_type` (project, activity, expenditure, item), `node_id`
(approved row id after apply), `change` (added, changed, reduced to zero), `field`, `before`,
`after`. Drives the history and the SAIP report.

### Division submissions
The copy has its own `aip_division_submissions` rows, so the division step (decision 6) works
unchanged.

## 6. UI states

**AIP Entry (office)**
- Consolidated, no SAIP: read-only, as today, plus **Start SAIP** for the head (with a short
  "what an SAIP is" line). Encoders see "Ask your department head to start an SAIP".
- SAIP open: amber banner "SAIP {n} — {status}". Approved rows show an "Approved" tag and no delete.
  Rows added in the SAIP show "New in SAIP {n}".
- Letter: a "Signed request letter" panel with upload, file name, replace, view. Empty state says
  it is required to send.
- Send to PPDO: the existing submit checklist, plus "Letter attached" and "New projects' proposals
  finalized".
- Cancel: a confirm dialog that says the changes will be discarded.
- Loading keeps the page shell (no layout shift); errors use the existing toast.

**AIP Review (PPDO)**
- The review list shows SAIPs with an "SAIP {n}" pill next to the office.
- The review screen shows the copy, with the changed rows marked and the letter link at the top.
  Approve and Return as for the AIP.

**SAIP history** (a tab on the office's AIP, for office readers and reviewers): list of SAIPs, and
for each approved one the before → after table and the letter. Empty: "No SAIPs yet."

**Report page:** an "SAIP" report type for those who can open Report (PPDO-240), with Fiscal year,
Office and SAIP number pickers. Empty when the office has no approved SAIP.

## 7. Non-goals

- SAIPs for FY ≤ 2027 (the old amendment flow stays).
- A second approval by the PPDC or finance (follow-up A).
- Deleting approved items, or renumbering ref codes.
- Several letters or extra attachments per SAIP.
- Editing an SAIP after approval; a new change needs the next SAIP.
- Before → after columns on the consolidated Annex B.

## 8. Deployment notes

- ⚠️ **Migrations:** `aip_saips`, `aip_saip_changes`, `saip_id` and `source_id` columns on the AIP
  tree tables. All additive. Run **before** the code (CI does not run migrations).
- **Blob:** letters go in the existing export container under a `saip-letters/` prefix, or a new
  container (decide in T1). No new app settings if the existing container is used.
- **No backfill:** no SAIP exists before this release.

## 9. Ticket split

| Ticket | Scope |
|---|---|
| T1 Backend: model + start/cancel | Migration, query filter, copy on start, cancel, list/get, permission matrix rows |
| T2 Backend: edit + submit | AIP Entry/division submit/comments on the copy, delete rule, letter upload, submit gates |
| T3 Backend: review | Return, approve (apply with IDs kept, save changes), reviewer edits on the copy, notifications |
| T4 Frontend: office | Start, banner, tags, letter panel, send, cancel, history tab |
| T5 Frontend: review + reports | Review list pill and screen, SAIP report type + Excel export |
| T6 Docs | Permission matrix, AIP Review spec, pre-deployment checklist |

## 10. Acceptance checklist

- [ ] PTO head starts SAIP 1 on a Consolidated FY2028 AIP; encoders see the draft copy.
- [ ] Annex B, the initial WFP and the GSO feed still show the approved figures while SAIP 1 is open.
- [ ] An approved activity can't be deleted in the SAIP; it can be set to ₱0.
- [ ] Send to PPDO is blocked without the letter, with a draft proposal, and over an enforced ceiling.
- [ ] A PPDO reviewer returns, the office fixes, the head resends; the reviewer approves.
- [ ] After approval, Annex B shows the amended figures; comments and proposal links on changed
      activities still open the right rows.
- [ ] The SAIP history shows SAIP 1's before → after and the letter.
- [ ] The SAIP report lists only SAIP 1's added or changed PPAs, with amended amounts; Excel matches.
- [ ] Cancel discards the copy; the AIP is unchanged; SAIP 1 is shown as Cancelled.
- [ ] A second SAIP can't start while one is open; SAIP 2 can start after SAIP 1 is approved.
- [ ] A GSO user can't see PTO's SAIPs, letter or report (404).
- [ ] Notifications: sent → PPDO reviewers; returned/approved → PTO users.

## 11. Test focus

- The global query filter: every existing AIP read returns the same results with a draft copy in
  the table (a test per report, review list and the external feed).
- Apply on approval keeps IDs and touches only changed rows; red-test it.
- The delete rule (approved vs new rows), the one-open-SAIP rule, and every submit gate.
- Cancel removes the whole copy and nothing else.
- Office and division scope on every new endpoint, including the letter download.
