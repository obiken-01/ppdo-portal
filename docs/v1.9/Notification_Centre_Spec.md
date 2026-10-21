---
type: spec
version: v1.9.0
status: shipped
tickets: [PPDO-221, PPDO-222, PPDO-219, PPDO-239]
updated: 2026-10-20
status_note: "accepted — 2026-10-14"
tickets_note: "PPDO-221 (T3 backend), PPDO-222 (T4 frontend); after PPDO-219/220"
supersedes_note: "—"
---

# v1.9.0 — Notification bell

Up: [v1.9 hub](README.md)

Ralph, 2026-10-14, while settling `Investment_Proposal_Comments_Spec.md` decision 10: "a button on
the top right beside the name … clickable", Facebook as the model.

## 1. Goal

One place, on every portal page, that tells a user what happened that concerns them, and takes them
there in one click. Today each feature signals on its own (sidebar pills, banners), and nothing says
"PPDO commented on your proposal".

## 2. Decisions

### Settled (Ralph, 2026-10-14)

1. **A bell beside the user's name in the top bar**, with an unread count. Click opens a dropdown of
   entries; each entry is a link.
2. **First events**: proposal comments, AIP returned / sent, PPDO reviewer edits (table in §3.1).
   AIP review comments are not in the first version.
3. **Recipients: everyone who can open the thing**, never the person who did it.
4. **Ships in v1.9.0**, after the proposal comments.

### Proposed defaults

5. **An event feed, stored per user, with read state** (the Facebook model), not a derived to-do
   count. One row per recipient per event. The existing state-based signals (sidebar pills, the
   Returned banner, the reviewer-edit notice) **stay**: they say what is still pending, the bell
   says what happened.
6. **Written in the same save as the event.** If working out recipients fails, the event still
   succeeds and the failure is logged at Error; a missed notification must never undo a return.
7. **Polling, not push**: the unread count is read on page load and every 60 seconds while the tab
   is visible. No SignalR/WebSocket on the Functions consumption plan.
8. **Opening an entry marks it read**; "Mark all as read" clears the rest. No delete, no per-type
   mute. Entries older than 90 days are not shown (no purge job in v1.9.0).
9. **The text is frozen at write time** ("PPDO edited test act 3: CO ₱40,000 → ₱50,000"), so a later
   rename does not rewrite history. The link is what stays live.

## 3. Behaviour

### 3.1 Events

| Event | Recipients | Text | Link |
|---|---|---|---|
| Proposal thread started **by the office** (PPDO-239) | The office's own users who can open the proposal (division axis included), except the author. **Not** PPDO reviewers, host-office readers or SuperAdmin | "{Side} commented on the proposal for {ref} — {project}: "{first 80 chars}"" | The proposal, comments panel |
| Proposal thread started **by PPDO** | Everyone who can open the proposal (the office's readers + every PPDO reviewer), except the author | Same | Same |
| Reply in a proposal thread (2026-10-15) | The thread's starter and everyone who has replied, except the replier | "{Name} replied on the proposal for {ref} — {project}: "{first 80 chars}"" | Same |
| Proposal thread resolved | The thread's starter and everyone who has replied, except the resolver | "{Name} resolved your comment on the proposal for {ref} — {project}" | Same |
| AIP returned by PPDO | Every user of the office who can open its AIP | "PPDO returned your FY {fy} AIP with {n} comments" | AIP Entry, that year |
| AIP returned by the department head | The users of the returned division(s) | "{Name} returned your FY {fy} AIP work" | AIP Entry |
| AIP sent to PPDO | PPDO cross-office reviewers (`CanReviewAllOffices`) | "{Office} sent its FY {fy} AIP to PPDO" | AIP Review, that office |
| Division submitted | The office's department head(s) | "{Division} submitted its FY {fy} AIP work" | AIP Entry |
| PPDO reviewer edit | Every user of the office who can open the activity | "PPDO edited {activity}: {change}" / "PPDO added {account} to {activity}" | The activity |
| AIP activity thread started **by the department head** | The office's own users who can open the activity (division axis included), except the author | "{Name} commented on {activity}: "{first 80 chars}"" | AIP Entry, the activity |
| AIP activity thread started **by PPDO** (PPDO-239) | The office's users who can open the activity (AIP Entry link) **and** every PPDO cross-office reviewer (`CanReviewAllOffices`, review drill-down link), except the author. Each user once: someone in both lists gets the AIP Entry entry only | "PPDO commented on {activity}: "{first 80 chars}"" | AIP Entry / AIP Review drill-down |
| Reply / resolve in an AIP thread | The thread's starter and repliers who can still open it: office users by AIP Entry, PPDO reviewers by the review drill-down | "{Name} replied on {activity}: …" / "{Name} resolved your comment on {activity}" | Same split |

**Who hears about a new comment thread (Ralph, 2026-10-20 — PPDO-239).** A thread the office starts
is the office's own discussion: only the office's users who can open the item hear about it. A
thread a PPDO reviewer starts reaches every PPDO reviewer and the affected office's users. Replies
and resolves are unchanged (starter + repliers who can still open it). This changes notifications
only, never who can read, write or resolve a comment.

"Can open" uses the same scope the target's read endpoint uses (`BudgetPlanningScope`, the
division axis included), resolved once per event for the office's users.

### 3.2 Cases

| Given | When | Then |
|---|---|---|
| 3 unread | Page loads | Bell shows 3 |
| 0 unread | Page loads | Bell, no badge |
| 120 unread | Page loads | Badge "99+" |
| Dropdown open | Click an entry | Marked read, navigates to the link, dropdown closes |
| Unread entries | Mark all as read | All read, badge gone |
| A target since deleted (e.g. proposal) | Click its entry | The target page's own not-found state; the entry is still marked read |
| A user who lost access since | Click | The target's 404; no data leaks from the entry beyond its frozen text |
| Count request fails | Polling | Badge keeps its last value; no error shown; retried next tick |
| List request fails | Open dropdown | "Could not load notifications." + Retry |
| Actor is also in scope | Event fires | The actor gets no entry |

## 4. API contract

All JWT-protected; a user only ever reads and updates their own rows (no id from another user is
accepted: 404).

- `GET /api/notifications/unread-count` → `{ count }`
- `GET /api/notifications?unreadOnly={bool=false}&page={int=1}&pageSize={int=20}` →
  `{ items: [{ id, type, text, link, createdAt, readAt? }], total }`, newest first, last 90 days.
- `POST /api/notifications/{id}/read` → 204 (idempotent).
- `POST /api/notifications/read-all` → 204.

## 5. Data model

New table **`notifications`** (migration `AddNotifications`):

| Column | Type | Null | Notes |
|---|---|---|---|
| `id` | bigint identity | no | PK |
| `user_id` | uniqueidentifier | no | FK users, on delete cascade (`Users.Id` is a GUID; corrected in PPDO-221) |
| `type` | nvarchar(40) | no | `ProposalComment`, `ProposalReply`, `ProposalResolved`, `AipReturnedByPpdo`, `AipReturnedByDepartmentHead`, `AipSentToPpdo`, `AipDivisionSubmitted`, `AipReviewerEdit` (`NotificationTypes`) |
| `text` | nvarchar(400) | no | frozen |
| `link` | nvarchar(400) | no | portal-relative path |
| `created_at` | datetime2 | no | UTC |
| `read_at` | datetime2 | yes | |

Index `(user_id, read_at, created_at desc)` for the count and the list.

## 6. UI states

- Bell (`ti-bell`-style icon) left of the name in the top bar; badge with the count, "99+" cap.
- Dropdown: header "Notifications" + "Mark all as read"; All / Unread chips; entries newest first with
  an unread dot and tinted background; relative time and source ("Investment proposal", "AIP FY 2028").
- Loading: three skeleton rows. Empty: "You're all caught up." Error: message + Retry.
- "See all notifications" opens `/notifications`, a paged full list (same rows).
- Phone width: the dropdown becomes full-width under the top bar.
- Keyboard: the bell is a button; Escape closes; entries are links.

## 7. Non-goals

- No email, SMS or browser push. No real-time push.
- No per-type mute or settings page. No deletion. No AIP review comment events (decision 2).
- No change to the existing sidebar pills and banners.

## 8. Deployment notes

One additive migration (`AddNotifications`), before the code; add to
`Pre_Deployment_Checklist_v1.9.0.md`. Nothing is back-filled: the bell starts empty on deploy.

## 9. Ticket split

| Ticket | Scope | Depends on |
|---|---|---|
| T3 Backend | Table + repo; `INotificationService` (write for recipients, count, list, read, read-all); recipient resolution per event; hooks in the proposal comment, AIP workflow and reviewer-edit services; endpoints; tests | Comments T1 |
| T4 Frontend | Bell + dropdown in the top bar, polling, `/notifications` page | T3 |

## 10. Acceptance checklist

- [ ] PPDO comments on PTO's proposal: every PTO user who can open it sees a bell entry; the PPDO author does not.
- [ ] Clicking the entry opens the proposal and the badge drops by one.
- [ ] PPDO returns PTO's AIP: PTO users get "PPDO returned your FY 2028 AIP…".
- [ ] PTO's department head sends to PPDO: `ppdo.finance` gets "PTO sent its FY 2028 AIP to PPDO".
- [ ] A PPDO reviewer edit on PTO: PTO users get the edit entry linking to the activity.
- [ ] Mark all as read clears the badge. A user of another office never sees PTO's entries.

## 11. Test focus

- Recipient resolution per event, including the division axis and "never the actor".
- A user cannot read or mark another user's notification (404).
- A failure in recipient resolution does not fail the triggering action.

## Related

- [v1.9.0 — PPDO Reviewer Edits to a Submitted AIP](AIP_Reviewer_Edit_Spec.md) — spec, shipped
- [v1.9.0 — AIP activity comments as threads](AIP_Comment_Threads_Spec.md) — spec, shipped
- [v1.9.0 — Comments on Investment Proposals](Investment_Proposal_Comments_Spec.md) — spec, shipped
- [v1.8.0 Phase 4 — AIP Review & Consolidation](../v1.8/AIP_Review_Spec.md) — spec, shipped
