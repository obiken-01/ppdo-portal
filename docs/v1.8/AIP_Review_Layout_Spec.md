---
status: draft
version: v1.8.0
tickets: R1 = the drill-down + Full office toggle on the review screen (blocked by PPDO-89)
supersedes: AIP_Review_Spec.md §6.2 "Success" and "Unresolved filters" rows (layout only) — the gates, actions, comment rules and History modal in that spec are unchanged
---

# v1.8.0 — AIP Review: the same drill-down as AIP Entry, with a Full office view

## 1. Goal

PPDO-89 replaced AIP Entry's whole-tree page with a **Program → Project → Activity** picker showing
one node at a time. The one-office review screen (`/budget-planning/aip/review`) still renders the
office's whole tree, and reaching a particular row means scrolling it.

A reviewer usually arrives **targeting a node** — a search result, a kanban card, an unresolved
comment they wrote last week — and the tree makes that the slowest part of the job. This spec gives
the review screen the same picker and panels.

⚠️ **But review is not entry, and this is the constraint the whole spec is built around.** An
encoder works on one activity at a time; a reviewer's decision — Accept, or Send back — is about the
**whole office**, and they have to be able to satisfy themselves they have seen all of it. So the
tree does not go away: it becomes the second of two views. `AIP_Entry_Layout_Spec.md` §2's open
follow-up asked whether this screen should change at all, and noted that it "shows the whole
structure on purpose (review needs the overview)". That concern is answered by keeping the overview,
not by deciding it was never needed.

## 2. Decisions (settled)

1. **Two views, one screen: Drill-down and Full office.** A segmented control in the office header
   switches them. — *Navigation and overview are two different jobs on this screen and neither one
   subsumes the other. Precedent: PPDO-78's Board/Table switch on the dashboard Offices band.*
2. **Drill-down is the default.** — *It is what the reviewer wants on the overwhelming majority of
   visits: they came for a node. The reviewer who wants to read the office end to end takes one
   click to say so; today everyone pays the scroll.*
3. **Reuse PPDO-89's components unchanged in shape** — `AipEntryPicker`, `AipProgramPanel`,
   `AipProjectPanel`, `AipActivityPanel`, `AipSelectedPanel`, `AipEntrySelection`. — *A reviewer and
   an encoder discussing "the second project" must be looking at the same thing; that is already why
   the review screen's `GroupBlock` was kept structurally identical to the entry page's. Two
   drill-downs that drifted would undo it.*
4. **Full office view is today's tree, moved, not rewritten.** The existing `GroupBlock` /
   `ActivityBlock` in `aip/review/page.tsx` become the Full office view as they stand. — *Nothing
   about it is wrong; it is only the wrong default.*
5. **Read-only is a third state, not `canEdit={false}`.** The panels currently render a **disabled**
   add control with a reason naming who holds the work ("With PPDO — activities cannot be added
   here"). On the review screen that sentence is false: the PPDO reviewer is not waiting for anybody,
   they simply never add. The panels take `lockedReason: string | null`, where **null omits the
   control entirely**. — *A disabled button tells a reviewer they are blocked from something they
   were never supposed to do.*
6. **The office header is sticky here too**, carrying FY · office · state chip · History · Send back
   / Accept / Re-open, plus the view switch. — *Same argument as `AIP_Entry_Layout_Spec.md` decision
   4: the decision this screen exists for is office-level, and a drill-down that scrolls it off the
   top hides the two buttons the reviewer came to press.*
7. **Selection lives in the URL**, alongside what is already there:
   `?officeId=&fiscalYear=&view=&programId=&projectId=&activityId=`. A stale id falls back to the
   deepest valid ancestor with a notice, exactly as on entry (`resolveAipSelection`). `view=full`
   is the only value that switches; anything else, including absent, is the drill-down.
8. **The unresolved-comment filter selects the node**, as on entry — `AipCommentFilterBar`'s
   `onSelectNode`, already built in PPDO-89. ↩️ This **replaces** `AIP_Review_Spec.md` §6.2's
   "filtering the tree to commented rows" in the drill-down view. In the **Full office** view the
   chips keep today's behaviour (the anchors open in place), because there the rows are on screen.
9. **Search and the kanban deep-link the selection.** PPDO-76's result rows link a program or project
   name to this screen; they gain `&programId=` / `&projectId=`, and an activity result may link here
   with all three instead of only opening the modal. — *This is the payoff. A search that lands the
   reviewer on the office and leaves them to find the row is the problem restated.*
10. **Every reader of the screen gets both views** — PPDO cross-office reviewer, department head, and
    an office reading its own submitted work. — *One screen, one shape. A second layout keyed on who
    is looking is two sets of states to keep in sync for no reader's benefit.*
11. **No change to any gate, action, or comment rule.** `canOpenAipOfficeReview` still gates the
    page, `ReviewerWriteGuard` still refuses reviewer writes, Accept / Send back / Re-open behave
    exactly as they do now. — *This is a layout ticket. A screen that changed who may accept an
    office while rearranging itself is how a permission regression ships unnoticed.*

### Open follow-ups (not blocking)

- **Does the view choice want to be remembered per reader?** Not in this ticket — it is in the URL,
  which survives a reload and a shared link. If reviewers ask for a default, `localStorage` is the
  cheap answer and CLAUDE.md permits it for exactly this kind of per-viewer convenience.

## 3. Behaviour

| Case | Given | When | Then |
|---|---|---|---|
| Happy path — arrive | Reviewer opens an office with no node in the URL | Page loads | Drill-down view; picker with the office's programs; "Pick a program to start." |
| Happy path — drill down | An office with programs | Picks program, project, activity | Each lookup enables in turn; the panel shows only the selected level, read-only; the URL carries all three ids |
| Happy path — deep link | Search result for a project | Reviewer clicks it | Lands on the review screen with that project selected and its activity list on screen |
| Switch to Full office | Drill-down, an activity selected | Clicks **Full office** | Today's tree, whole office; `view=full` in the URL; the selection ids stay in the URL |
| Switch back | Full office | Clicks **Drill-down** | Returns to the node that was selected before, not to an empty picker |
| Jump from a comment | Unresolved comment on an activity in another program | Clicks it in the filter bar | That activity becomes the selection (drill-down); in Full office the anchor opens in place as today |
| Reload | URL carries view + three ids | Page reloads | Same view, same selection |
| Edge: stale id | URL names an activity since deleted by the office | Page loads | Falls back to its project; notice "That activity no longer exists." |
| Edge: office not yet encoded | `review.groups` empty | Page loads | Today's "Nothing encoded yet" empty state in **both** views; no picker |
| Edge: program with no projects | Program selected | — | "No projects yet." and **no add control** (decision 5) |
| Edge: several sub-office groups | Office with two groups | Opens the Program lookup | Group name as a subtitle on each option; the header's figure strip is the office total with the per-group split behind its disclosure |
| Role: PPDO cross-office reviewer | At `SubmittedToPpdo` | — | Both views read-only; Send back and Accept in the sticky header |
| Role: PPDO reviewer, office not at PPDO | Draft / department review / returned | — | Unchanged: the existing "This office's AIP is with …" notice, no decision buttons, both views still browsable |
| Role: accepted office | `Consolidated` | — | Unchanged: Re-open and send back in the header; figures final |
| Role: department head / own office reading its own submitted work | — | — | Same two views, read-only, no decision buttons |
| Failure: load error | Network/500 on the office review | — | Today's error banner; neither view rendered |
| Failure: comments fetch failed | — | — | Unchanged: no filter bar, panels still render. ⚠️ Never an empty filter bar, which reads as "nothing outstanding" |

## 4. API contract

**No change.** `GET /api/budget-planning/aip/review/{aipRecordId}/offices/{officeId}` already returns
the whole office (`AipOfficeReview.groups`), which is the shape `resolveAipSelection` consumes; the
comments endpoint is already fetched once for the office by `AipCommentsProvider`.

⚠️ **Do not paginate or narrow the office fetch to the selected node.** The Full office view needs
the whole thing, the picker needs every node's name to be searchable, and the figure strips are
summed client-side from activities already in memory (`AipRowFigures`). One request, as today.

## 5. Data model changes

**None. No migration.**

## 6. UI states

### AIP Review — one office (`/budget-planning/aip/review`)

| State | Content |
|---|---|
| **Loading** | Header and FY picker render immediately, as today. Skeleton of the sticky header, three lookup-shaped bars and one panel — same heights as loaded. No spinner |
| **Drill-down — nothing picked** | Below the picker: "Pick a program to start." |
| **Drill-down — selected** | The deepest level only, read-only: header (ref code, name, fund pill on an activity, figure strip, comment anchor), then the child list, or `AipActivityFields` + `AipExpenditureTable` at the leaf |
| **Full office** | Today's tree, unchanged |
| **View switch** | Segmented control in the sticky header, beside the state chip. Two labels, **Drill-down** and **Full office**; the current one is `aria-pressed` |
| **Empty — office not encoded** | Today's "Nothing encoded yet"; the picker and the switch are **hidden**, not rendered empty |
| **Stale id** | Inline amber notice above the picker, same copy and placement as AIP Entry |
| **Read-only** | ⚠️ Add and delete controls are **absent**, not disabled (decision 5). No reason text — there is no holder to name |
| **Error — load** | Today's banner; picker hidden |

**Components.** Reused as-is: `AipEntryPicker`, `AipSelectedPanel`, `AipProgramPanel`,
`AipProjectPanel`, `AipActivityPanel`, `AipEntrySelection`, `AipOfficeHeader`, `AipCommentFilterBar`
(with `onSelectNode`), `AipCommentAnchor`, `AipActivityFields`, `AipExpenditureTable`,
`AipHistoryButton`, `ConfirmDialog`. Changed: the three panels' `lockedReason` becomes
`string | null` (decision 5). New: a small `AipReviewViewSwitch`, and the existing `GroupBlock` /
`ActivityBlock` extracted out of `aip/review/page.tsx` into
`components/aip/review/AipReviewFullTree.tsx`.

⚠️ `aip/review/page.tsx` is **535 lines**. Like PPDO-89 it must come out **smaller**, not larger:
the tree moves out, the drill-down arrives as composition (`RETROSPECTIVE.md`).

## 7. Non-goals

- **The activity modal (`AIP_Review_Spec.md` §6.1a)** — unchanged; it is the surface for a single
  activity reached from search, and it is not the same thing as this screen.
- **The consolidated grid** (`aip/consolidated`) — untouched.
- **Any change to Accept / Send back / Re-open**, their confirmations, or who may press them.
- **The comments API, the resolve rule, or who may comment.**
- **Remembering the view per reader** — §2 follow-up.
- **Editing anything on this screen.** The department-head reviewer edits on AIP Entry, as today.
- **AIP Entry** — this ticket does not touch it beyond the `lockedReason` type change.

## 8. Deployment notes

- Migrations: **none.** Config: none.
- Ships behind no flag. It is reversible by reverting one PR, and the Full office view means the
  previous behaviour is still reachable from the UI if reviewers dislike the default.

## 9. Ticket split

| Ticket | Scope | Blocked by |
|---|---|---|
| **R1** — Review drill-down + Full office toggle | All of §2. Includes the search/kanban deep-links (decision 9) and the `lockedReason` change | **PPDO-89** |

One ticket: the pieces do not stand up separately — a toggle with one view is nothing, and
deep-links that land on a tree are what this replaces.

Manual-coding candidate: **no.** Same reason as PPDO-89 — a large stateful page with a CLS-sensitive
loading shape. The `lockedReason` widening on its own is too small to be worth carving out.

## 10. Acceptance checklist

- [ ] Opening a review office shows the picker and no program, project or activity content until a program is picked
- [ ] Picking a program lists only its projects; picking a project lists only its activities; picking an activity shows its fields and lines and nothing of any other activity
- [ ] Every panel is read-only: no add control, no delete control, and **no disabled control with a reason**
- [ ] **Full office** shows the whole tree exactly as the screen does today, comment anchors included
- [ ] Switching to Full office and back returns to the node that was selected, not to an empty picker
- [ ] The view and the selection survive a reload and can be shared as a link
- [ ] Clicking an unresolved comment in the filter bar selects its node in the drill-down, and opens it in place in Full office
- [ ] A search result for a project opens the review screen with that project already selected
- [ ] Send back / Accept / Re-open sit in the header and stay visible while scrolling a long office
- [ ] An office still in Draft shows the existing "with the office" notice and no decision buttons, in both views
- [ ] A user without `canOpenAipOfficeReview` is still redirected, and a foreign office still answers "No AIP to review here"
- [ ] `aip/review/page.tsx` is shorter than 535 lines

## 11. Test focus

- `resolveAipSelection` is already shared with AIP Entry — no second copy, and that is the thing to
  check in review: the spec fails if the review screen grows its own fallback.
- `ReviewerWriteGuardCoverageTests` — unchanged and still passing; this ticket must not add or exempt
  a route.
- Frontend, by the acceptance lines above: the read-only panels omitting rather than disabling their
  controls, and the view switch preserving the selection.
- ⚠️ Live-test the **department head reading their own office's submitted work**. It is the reader
  most easily broken by a `canEdit` / read-only mix-up, and it is not the one anybody opens first.
