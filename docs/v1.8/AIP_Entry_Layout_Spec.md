---
status: shipped
version: v1.8.0
tickets: E1 = PPDO-88 (delete backend, done), E2 = PPDO-89 (drill-down layout, done), E3 = PPDO-91 (delete controls, done)
supersedes: AIP_Entry_Spec.md §6.1 layout (tree) — behaviour, gates and API in that spec are unchanged unless named here
---

# v1.8.0 — AIP Entry: drill-down layout + delete Project/Activity

## 1. Goal

AIP Entry today renders the office's **whole tree** — every program, project and activity — on one
page, and an activity's fields and expenditure lines open inline in the middle of it. At the
2026-09-15 PDC demo this read as overwhelming: while entering one activity the encoder is looking at
every other PPA. The WFP entry page never had that problem because it picks **Program → Project →
Activity** first and shows only the selected one. This spec moves AIP Entry to that shape, and adds
the **delete Project / delete Activity** capability decided at the same meeting.

## 2. Decisions (settled)

1. **Mirror the WFP entry picker.** Three `Lookup`s in a row — Program, Project, Activity — each
   disabled until its parent is picked, each clearing its children when changed
   (`wfp/entry/page.tsx` "Context picker"). One node is worked on at a time. — *This is the shape the
   PDC asked for by name, and the users already know it from WFP.*
2. **No separate sub-office group picker.** The Program lookup lists the programs of every group the
   user sees; when the office has more than one group, each option shows the group name as a subtitle.
   — *Most offices have one group; a fourth picker would add a step for everyone to serve a few.*
3. **The panel below the picker shows the deepest selected level only:**
   - Program selected → **Program panel**: header (ref code, name, figure strip, comment anchor) and
     a compact list of its projects (ref segment · name · total · unresolved-comment count), each row
     selecting that project, plus **+ Add project**.
   - Project selected → **Project panel**: header (name, figure strip, comment anchor, **Delete
     project**), a reserved **Project details** section (empty until the new Project fields are
     specified — see §7), and a compact list of its activities with **+ Add activity**.
   - Activity selected → **Activity panel**: header (name, fund pill, figure strip, comment anchor,
     **Delete activity**), then the existing `AipActivityFields` and `AipExpenditureTable`
     **unchanged**, and a footer — **Change activity · Change project · Done** — as on WFP.
   — *The child lists are the one concession to overview: names and totals only, never fields. They
   are how an encoder finds the activity they want without a search.*
4. **Office-level context stays at the top, always visible:** `AipSubmitChecklist`, the comment filter
   bar, and a sticky header (FY · office · office figure strip, per group when several) that replaces
   each `GroupBlock`'s header. — *Submit is office-level; hiding it inside a drill-down would hide the
   gate.*
   ↩️ **The per-group strips are behind a disclosure; the always-visible figure is the office total**
   (PPDO-89, live-testing). The Office of the Provincial Governor has **eight** sub-office groups, and
   eight strips pinned to the top left barely a panel's worth of viewport under them — a context header
   that eats the work area is not a context header. The office total is the figure that belongs on the
   permanent line anyway: the ceiling the checklist gates on is office-wide. A "N sub-office groups ▸"
   toggle opens the per-group split, which the form still needs.
5. **Selection lives in the URL** — `?fiscalYear=&programId=&projectId=&activityId=`. — *Reload keeps
   the place, the returned-work banner and the review search can deep-link to a node, and "Change
   activity" is just a param change. A stale id (deleted, or not the user's) falls back to the deepest
   valid ancestor with an inline notice.*
6. **Everything that names a node selects it.** A checklist issue, an unresolved comment in the filter
   bar, and a child-list row all set the URL selection instead of scrolling a tree. Lookup options and
   child rows carry an **unresolved-comment count badge**. — *A returned office must be able to find
   every flagged node without the tree that used to show them.*
7. **Add selects what it created.** + Add project / + Add activity open the existing inline name input;
   on success the created node is spliced into state (no reload, as today) **and becomes the
   selection**.
8. **Delete is hard delete** (Ralph, 2026-09-15). Recoverability comes from the audit log, whose
   delete row is enriched to a **full snapshot**: the node, its descendant activities, their
   expenditure lines and amounts, and the comments removed with them. — *These rows are pre-PPDO draft
   work; soft delete would need a filter on every total, ceiling check, ledger, export and API read,
   and one missed filter counts deleted money against the ceiling.*
9. **Who and when:** encoder and department head, in **Draft, DepartmentReview and ReturnedByPpdo** —
   i.e. exactly where `AipWorkflowStatus.IsOfficeEditable` already allows writes. No new permission:
   the existing gates (`CanAccessBudgetPlanning` + `ReviewerWriteGuard` + `AipWriteGuard`) apply
   unchanged, so the comment-only PPDO reviewer stays refused.
10. **Deleting a project deletes its activities; deleting either deletes the comments anchored to
    everything removed** (Ralph, 2026-09-15). ↩️ This **reverses** the `AipReviewComment.NodeId`
    design note ("deleting a commented node leaves this comment dangling … accepted on purpose"). The
    history that note protected is kept by decision 8's snapshot instead. The confirm dialog states the
    unresolved count so nobody clears a reviewer's ask by accident.
11. **Ref codes renumber to close the gap** (Ralph, 2026-09-15): deleting `…-002` of `-001 -002 -003`
    turns `-003` into `-002`. A renumbered **project rewrites the prefix of every activity under it**.
    Programs are untouched — their codes are the LDIP's (`aip-program-refcodes-match-ldip`).
12. **Renumbering applies to FY2028+ records only.** FY≤2027 AIPs were uploaded from the province's
    file, their codes are the file's, and some are unparseable (`RefCodeAllocator` skips those); they
    keep today's gap-leaving delete. — *Rewriting imported codes would make last year's document
    disagree with the file it came from.*
12a. **Delete clears the allocation ledger first.** `aip_division_allocation_ledger.aip_activity_id` is
    `NoAction` and its configuration says the delete path removes those rows — but
    `IAipAllocationLedgerRepository.DeleteByActivityAsync` **has no caller**, so deleting an activity
    that holds a reservation (or its project or program) FK-fails today. The delete work fixes this
    for program, project and activity delete.

### Open follow-ups (not blocking)

- ~~**Does AIP Review's one-office screen change to the same layout?**~~ **Answered — yes, with the
  overview kept** (Ralph, 2026-09-16). It gets the same picker and panels as the default view, and
  today's whole tree stays behind a **Full office** switch, because Accept and Send back are
  whole-office decisions and a reviewer has to be able to satisfy themselves they have seen all of
  it. Specified in [AIP_Review_Layout_Spec.md](AIP_Review_Layout_Spec.md); nothing in *this* spec
  changes, and the review screen was untouched by PPDO-89.
- **The new Project fields** — field list and the report that uses them are pending. Decision 3
  reserves the section; it is a separate spec.

## 3. Behaviour

| Case | Given | When | Then |
|---|---|---|---|
| Happy path — drill down | Office with programs, one editable | Encoder picks a program, a project, an activity | Each lookup enables in turn; the panel shows only the selected level; the URL carries all three ids |
| Happy path — add | Program selected, office editable | + Add project, name, Add | Project appears in the lookup and child list, **is selected**, no skeleton flash, readiness refreshes |
| Happy path — change | Activity selected | "Change activity" | Activity lookup clears and focuses; project stays selected |
| Reload | URL has program/project/activity ids | Page reloads | Same selection restored |
| Edge: stale id | URL names an activity since deleted | Page loads | Selection falls back to its project; notice "That activity no longer exists." |
| Edge: first record | Program with no projects | Program selected | Child list empty state "No projects yet" + Add project (if editable) |
| Edge: division filter | Host-office user with a division | Opens the page | Program lookup lists only programs assigned to their division; the existing division notice stays |
| Edge: several groups | Office with two sub-office groups | Opens Program lookup | Options show group name as subtitle; sticky header shows a figure strip per group |
| Edge: jump from comment | ReturnedByPpdo, unresolved comment on an activity | Clicks it in the filter bar | That activity becomes the selection |
| Delete activity (middle) | Project with `-001 -002 -003`, Draft | Deletes `-002`, confirms | `-002` gone; old `-003` is now `-002`; selection moves to the project; totals and readiness refresh; audit row holds the snapshot |
| Delete project | Project `-002` of three, 4 activities, 2 unresolved comments | Deletes, confirm dialog says "4 activities, 2 unresolved comments" | Project, activities, their lines, ledger rows and all their comments removed; old project `-003` → `-002` **and its activities' codes follow** |
| Delete last sibling | Delete `-003` of three | Confirms | No renumbering; next add reuses `-003` (today's allocator behaviour) |
| Delete with reservation | Activity holds an `aip_division_allocation_ledger` row | Deletes | Succeeds (today: FK failure → 500); division's consumed allocation drops by the reservation |
| Dept head in review | DepartmentReview, dept head | Deletes an activity | Allowed |
| Returned by PPDO | ReturnedByPpdo, encoder | Deletes an activity PPDO commented on | Allowed; dialog names the unresolved PPDO comment count; the comment is deleted and captured in the audit snapshot |
| Failure: locked | SubmittedToPpdo or Consolidated | Delete control | Disabled with the existing locked reason; direct call → 400 (`AipWriteGuard`) |
| Failure: concurrent | Encoder A adds an activity while B deletes a sibling | Both save | One succeeds; the other gets **409** "This list changed while you were saving — reload and try again."; nothing half-renumbered |
| Failure: save error | Network/500 on delete | — | Error banner on the panel; node still shown; selection unchanged |
| Role: PPDO cross-office reviewer | `CanReviewAllOffices` (non-SuperAdmin) | Calls DELETE | 403 (`ReviewerWriteGuard`) |
| Role: other office | Guest-office encoder | Calls DELETE on another office's node | 404, indistinguishable from a missing node (`AipWriteGuard`, V18-39) |
| Role: SuperAdmin | — | Deletes | Allowed (support access) |
| FY≤2027 | Uploaded AIP on `aip/detail` | Deletes a middle activity | Deleted, **no renumbering** (decision 12), ledger/comments cleared as for FY2028+ |

## 4. API contract

No new endpoints. The three DELETE routes change their **success shape** and gain a 409.

### `DELETE /api/budget-planning/aip/projects/{projectId:int}` — JWT + `CanAccessBudgetPlanning` + `ReviewerWriteGuard`
### `DELETE /api/budget-planning/aip/activities/{activityId:int}` — same gate
### `DELETE /api/budget-planning/aip/programs/{programId:int}` — same gate (ledger/comment fix only; no renumber, no UI here)

- Request: none.
- 200: `ApiResponse<AipDeleteResultDto>`
  ```
  AipDeleteResultDto {
    deletedNodeType: "Program" | "Project" | "Activity",
    deletedId: number,
    removedActivityCount: number,
    removedCommentCount: number,
    renumbered: { nodeType: "Project" | "Activity", id: number, refCode: string }[]  // new codes, empty when none moved
  }
  ```
  ↩️ Was `ApiResponse<boolean>`. The frontend patches codes from `renumbered` instead of reloading
  (the page's no-reload rule). `lib/aip.ts` `deleteAipProject` / `deleteAipActivity` return it; the old
  detail page may ignore it.
- 400: `{ error: "Cannot delete from this office's AIP while it is in review by PPDO. …" }` — existing
  `AipWriteGuard` message.
- 403: empty — `ReviewerWriteGuard`.
- 404: `{ error: "AIP activity {id} not found." }` — missing **or** not the caller's.
- 409: `{ error: "This list changed while you were saving — reload and try again." }` — renumber lost a
  race on a sibling unique index.

### Service rules (`AipService.DeleteProjectAsync` / `DeleteActivityAsync` / `DeleteProgramAsync`)

One explicit transaction, run through the EF execution strategy (`CreateExecutionStrategy`, as the
bulk stock-balance import does), in this order:

1. Existing guards (`AipWriteGuard`).
2. Collect the activity ids being removed (the one, or all under the project/program).
3. `DeleteByActivityIdsAsync` for the collected ids → ledger rows gone.
4. Delete `aip_review_comments` where `(NodeType, NodeId)` is any removed activity, the removed
   project(s), or the removed program. Count unresolved for the snapshot.
5. The enriched audit snapshot (decision 8) is **read before** the transaction, while the data still
   exists, and the audit row is **written inside** it, after the renumber — so a rolled-back delete
   leaves no audit row. ↩️ Built that way in PPDO-88 (this step originally said "write before the delete").
6. Delete the node (DB cascade removes descendants, expenditures, procurement items).
7. **FY2028+ only:** renumber later siblings (decision 11) via a pure helper
   `RefCodeAllocator.Renumber(parentRefCode, siblings)` → new code per sibling. Apply **two-phase** —
   first set every moving row to a temporary code unique by id, save, then the final codes, save — so
   the `(parent_id, ref_code)` unique index never sees two rows with one code mid-shift. A renumbered
   project rewrites each activity's prefix in the same two phases.
8. Commit. A unique-index rejection (`UniqueConstraintViolationException`, which the repository
   raises for it) maps to the 409.

↩️ **Office delete gets the ledger fix too (PPDO-88).** `DeleteOfficeAsync` hit the same `NoAction` FK,
so it now clears its activities' ledger rows in the same transaction. It keeps its `bool` result and
does not snapshot or renumber.

Log `LogInformation` "AIP node deleted. NodeType: {NodeType}, Id: {Id}, RefCode: {RefCode},
RemovedActivities: {RemovedActivities}, RemovedComments: {RemovedComments}, Renumbered: {Renumbered},
UserId: {UserId}".

## 5. Data model changes

**None.** No migration. (Comment rows are deleted, not re-shaped; the audit snapshot is JSON in the
existing `AuditLog` payload.)

## 6. UI states

### AIP Entry (`/budget-planning/aip/entry`)

| State | Content |
|---|---|
| **Loading** | Header + FY picker render immediately; skeleton of the sticky header, the checklist box, three lookup-shaped bars, and one panel — same heights as loaded. No spinner |
| **Empty — FY not opened** | Unchanged: "FY \<year\> has not been opened yet" (no create action) |
| **Empty — no programs** | Unchanged copy; `AipAddProgramsPanel` shown instead of the picker when editable |
| **Empty — nothing picked** | Below the picker: "Pick a program to start." |
| **Empty child list** | "No projects yet" / "No activities yet" + the add control when editable |
| **Error — load** | Existing error banner; picker hidden |
| **Error — save/delete** | Inline banner at the top of the panel it came from; input and selection kept |
| **Conflict (409)** | Inline banner on the panel with a **Reload** action — not a toast |
| **Success** | Add → node selected, no reload. Delete → toast "Activity deleted" / "Project deleted"; selection moves to the parent; renumbered codes update in place |
| **Read-only** | Past the office-editable states: add and delete controls **disabled with the reason naming who holds it** (existing `describeAipHolder`), picker and panels still browsable |
| **Validation** | Unchanged — per field under the field in `AipActivityFields` / `AipExpenditureTable`; add input requires a non-blank name |
| **Delete confirm** | `ConfirmDialog`, danger variant. Title "Delete project \<ref\>?" Body lists what goes: "N activities, M expenditure lines, K unresolved comments. Later projects in this program will be renumbered. This cannot be undone." Omit zero counts; omit the renumber sentence when nothing follows it |

↩️ **As built (PPDO-91), the dialog drops the M expenditure-line count.** The loaded tree carries
activity totals, not line counts — `AipActivityDetail` has no such field, and fetching lines per
activity just to count them, on every button that opens a confirm dialog, is a round trip nobody
reads before deciding. The unresolved-comment count is a `useAipUnresolvedCount()` read against data
the office is already looking at (PPDO-89's provider); the activity count is already in the tree.
Both are free. The acceptance checklist (§10) only ever asked for these two.

**Components.** Reused: `Lookup`, `ConfirmDialog`, `RowActions`, `useToast`, `AipSubmitChecklist`,
`AipCommentsProvider` / `AipCommentFilterBar` / `AipCommentAnchor`, `AipActivityFields`,
`AipExpenditureTable`, `AipFigureStrip`, `AipFundPill`, `AipLevelChip`, `AipRefCode`. New, under
`components/aip/entry/`: `AipEntryPicker.tsx`, `AipProgramPanel.tsx`, `AipProjectPanel.tsx`,
`AipActivityPanel.tsx`. ⚠️ The page is 784 lines today — the new layout must **shrink** it by moving the
panels out, not grow it (`RETROSPECTIVE.md`).

↩️ **As built (PPDO-89), four more files came out of the page** so it could actually shrink — it is
**610 lines**: `AipEntrySelection.ts` (the pure URL-id → node resolution and its ancestor fallback,
per §11), `AipSelectedPanel.tsx` (which level renders), `AipEntryPanelParts.tsx` (the office header,
child list, inline add, panel frame) and `AipEntryTree.ts` (the immutable splices, moved as-is).
Three existing components gained one optional prop each rather than being forked: `Lookup`'s
`inputRef` (focus the activity box on "Change activity"), `AipCommentFilterBar`'s `onSelectNode` and
`AipSubmitChecklist`'s `onSelectActivity` (decision 6 — the review page passes neither, so it is
unchanged).

↩️ **"+ Add programs" is OFF on a populated AIP** (Ralph, 2026-09-16) — `SHOW_ADD_PROGRAMS_WHEN_POPULATED`
in `aip/entry/page.tsx`, one constant to flip it back. The old page rendered `AipAddProgramsPanel`
twice — in the empty state, and again below the tree whenever the office could edit — and the second
placement was carried over unchanged at first, then moved into the office header band beside the
sub-office-group disclosure (the panel opening **below** the sticky header in normal flow, since open
it is a sector picker over a scrolling checkbox list). It is now hidden there entirely:

- Year-open already populates every office from its LDIP, so on a populated AIP this is only the
  recovery path for a program the LDIP gained afterwards.
- ⚠️ **The picker lists the office's whole LDIP group, including programs already in the AIP** —
  `AipService.GetAddableProgramsAsync` does not subtract what has been added. `AddProgramsWithGroupAsync`
  *does* refuse them ("These programs are already in this group: …"), so no duplicate row can be
  created, but the encoder is offered a choice that cannot succeed. **Filtering the read path is the
  prerequisite for turning this back on** — see the techdebt ticket.

The **empty state keeps it**: an office with no programs has nothing to re-add and no other way to
begin. `AipAddProgramsPanel` gained an optional controlled mode (`open` / `onOpenChange`) for the
split trigger; omitting both props is the old self-contained behaviour the empty state uses.

↩️ **PPDO-88's delete controls were carried across, not dropped.** They shipped into the old tree as an
interim and moving the tree out would have removed a capability the office already has, so
`AipDeleteNodeButton` now sits in the Project and Activity panel headers. **PPDO-91 shipped the final
copy**: the unresolved-comment count in the dialog, the conditional renumber sentence, the success
toast, the 409 banner with Reload, and disabled-with-reason in place of the button being absent.
⚠️ **Not yet exercised against a running app** — `tsc`/lint are clean, but the three cases worth a
human pass are a middle-sibling delete renumbering live, a last-sibling delete omitting the renumber
sentence, and a project delete taking its activities' costing in one confirm.

## 7. Non-goals

- **AIP Review one-office screen** — unchanged pending Ralph's question (§2 follow-ups).
- **The new Project fields** — the Project panel reserves a section; no columns, no form.
- **Program delete in the UI** — programs are the LDIP's closed list; the endpoint's ledger/comment fix
  ships, but nothing on this page offers it. Deleting a program still leaves a numbering gap, by design.
- **Soft delete / undo / restore** — decision 8.
- **Renumbering FY≤2027 codes** — decision 12.
- **Changing `aip/detail`** beyond consuming the new delete result.
- **Keyboard shortcuts, drag-to-reorder, moving an activity between projects.**

## 8. Deployment notes

- Migrations: **none.**
- Dependencies / config: none.
- The delete fix is independently valuable (today's deletes can 500). If the layout slips, ship E1 alone.

## 9. Ticket split

| Ticket | Scope | Blocked by |
|---|---|---|
| **E1** — Delete: ledger + comments + snapshot + renumber | §4 service rules, `AipDeleteResultDto`, `RefCodeAllocator.Renumber`, 409 mapping. TDD | — |
| **E2** — Drill-down layout | §2 decisions 1–7, §6 minus delete; URL selection; comment/checklist node selection | — |
| **E3** — Delete controls | Delete buttons + `ConfirmDialog` on Project/Activity panels; patch codes from `renumbered` | E1, E2 |

Manual-coding candidates: **E1's `RefCodeAllocator.Renumber` pure helper** (sibling `NextRefCode` in the
same file, fully testable with `dotnet test`) — the transaction around it is not. E2/E3 are not
(`aip/entry` is a large, stateful page).

## 10. Acceptance checklist

- [ ] Opening AIP Entry shows the checklist and three lookups — no program, project or activity content until a program is picked
- [ ] Picking a program lists only its projects (names + totals); picking a project lists only its activities; picking an activity shows its fields and expenditure lines and nothing of any other activity
- [ ] Changing the Program lookup clears Project and Activity
- [ ] + Add activity creates the activity and selects it without the skeleton flashing
- [ ] Reloading the page with an activity selected returns to the same activity
- [ ] Clicking an unresolved comment in the filter bar selects the node it belongs to
- [ ] Each lookup option with unresolved comments shows a count badge
- [ ] In an office with two sub-office groups, program options show which group they belong to
- [ ] Deleting activity `-002` of three renames the old `-003` to `-002` in the lookup, the child list and the consolidated grid
- [ ] Deleting a project removes its activities, and the next project's activities show the new project segment in their codes
- [ ] Deleting an activity that has a division allocation reservation succeeds, and the Allocation page's consumed figure drops by that amount
- [ ] The delete dialog states the number of activities and unresolved comments that will be removed
- [ ] After deleting a commented activity, the comment is gone from the filter bar and the audit log's delete row lists it
- [ ] A department head can delete while the office is in DepartmentReview; an encoder can delete while ReturnedByPpdo
- [ ] After Submit to PPDO the delete buttons are disabled with a reason naming PPDO
- [ ] A PPDO cross-office reviewer calling the DELETE endpoint gets 403
- [ ] Deleting a middle activity on an FY2027 AIP in `aip/detail` leaves the gap (no renumbering)

## 11. Test focus

- `RefCodeAllocatorTests` — `Renumber`: middle delete shifts later siblings down by one; last delete moves
  nothing; gaps already present collapse; unparseable codes are left as they are; three-digit padding kept.
- `AipServiceTests` — delete activity/project/program: ledger rows removed before the node; comments on
  every removed node removed, count returned; snapshot contains activities, lines, comments; FY2028
  renumbers and returns `renumbered`, FY2027 does not; project renumber rewrites descendant activity
  prefixes; unique-index `DbUpdateException` → Conflict; locked status → BadRequest; foreign office →
  NotFound.
- `ReviewerWriteGuardCoverageTests` — the three DELETE routes stay covered (no exemption).
- Frontend: selection-from-URL fallback to ancestor is pure — extract and unit-check if a frontend test
  harness exists; otherwise covered by the acceptance lines above.
