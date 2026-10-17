---
status: accepted — 2026-10-16
version: v1.9.0
tickets: PPDO-223
supersedes: AIP_Review_Spec.md §3.1 "the encoder never comments" (encoders may now reply)
---

# v1.9.0 — AIP activity comments as threads

Ralph, 2026-10-15: make AIP review comments work like the Investment Proposal threads
(`Investment_Proposal_Comments_Spec.md`), so the two comment systems behave the same. Settled
2026-10-16; built over the weekend, before v1.9.0 is cut for UAT (and the November training).

## 1. Goal

A remark on an activity becomes a conversation: the reviewer raises it, the encoder answers inside
it ("Done, amount fixed"), and the reviewer resolves it. Several topics on one activity stay
separate. Today encoders cannot answer at all, and every remark is a single flat comment.

## 2. Decisions

### Settled (Ralph, 2026-10-16)

1. **Threads, several per activity.** A comment is a **head** (starts a thread) or a **reply** (one
   level deep, inside a thread). Anchoring is unchanged: activities only (PPDO-79).
2. **Encoders reply only.** They answer inside a thread but cannot start one. Reviewers (PPDO and
   the office's department head) start threads, as today. ↩️ Supersedes "the encoder never
   comments" (`AIP_Review_Spec.md` §3.1) for replies only.
3. **Only the side that started a thread resolves it**, as today: a PPDO thread by PPDO, a
   department-head thread by the department head. A reply is never resolved on its own; resolving
   the head closes the thread.
4. **Reviewer-edit notes stay as they are** (PPDO-212/213: created already resolved, with "Mark as
   seen"). They take no replies, because resolved threads take none.
5. **The bell rings for AIP threads** (PPDO-221 feed, same rules as proposals): a new thread on an
   office's activity → everyone in that office who can open the activity, except the author; a reply
   → the thread starter and everyone who has replied, except the replier; resolved → the same,
   except the resolver.

### Proposed defaults

6. **Existing comments become heads with no replies.** A nullable `parent_comment_id` is added; no
   data is rewritten.
7. **Replies carry a side for display.** An encoder's reply is stored with a new side **`Encoder`**;
   a reviewer's reply keeps the reviewer's side. `Encoder` never resolves anything and is never
   counted (decision 8). This narrows the enum's "exactly two sides" note to "two sides that
   resolve".
8. **Every open-comment count ignores replies.** The re-submit gate (open PPDO comments), the
   department head's open count, the "N open / N resolved" anchors and the notification counts count
   open **heads** only. ⚠️ Replies are stored with `resolved_at` null, so a query that forgets
   `parent_comment_id IS NULL` would let one encoder reply block a re-submit forever. Today's count
   is `AipReviewCommentRepository` line ~79; find every other one.
9. **Who may reply** = anyone who can read that office's comments today and is a reviewer (PPDO,
   the department head) **or** an encoder of the office whose scope covers the activity (the AIP
   Entry write scope, division axis included). An encoder of a divisioned office replies only on
   activities of their division.
10. **Replies are allowed in any workflow state** while the thread is open, including after a
    division has submitted or the office is at PPDO: a reply is not AIP content and changes nothing
    the encoder submitted. It does not reopen or move the workflow.
11. **No edit, delete or un-resolve**, as today. 1–2,000 characters, as today.

## 3. Behaviour

| # | Given | When | Then |
|---|---|---|---|
| 1 | PPDO opened a thread on an activity | The encoder replies "Fixed" | 201, side Encoder, inside the thread; thread still open |
| 2 | Same | The encoder tries to start a new thread | 403 "Encoders reply inside a reviewer's thread; they can't start one." |
| 3 | Open PPDO thread with an encoder reply | Department head sends to PPDO again | Refused by the existing gate (1 open PPDO comment); the reply is not counted |
| 4 | Same | PPDO resolves the head | Thread closed; re-submit no longer blocked by it |
| 5 | A reply | Anyone resolves the reply | 400 "Only a thread's first comment can be resolved." |
| 6 | A resolved thread (incl. a reviewer-edit note) | Anyone replies | 400 "This thread is resolved. Start a new one." |
| 7 | A reply | Anyone replies to the reply | 400 "Reply to the thread's first comment." |
| 8 | Divisioned office, encoder of division A | Replies on a division B activity | 403 (outside their scope) |
| 9 | Encoder replies | — | Bell: the thread starter and earlier repliers get "{Name} replied on {activity}…" |
| 10 | PPDO opens a thread on PTO's activity | — | Bell: PTO users who can open that activity |
| 11 | Old flat comments | Page loads | Shown as threads with no replies; counts unchanged |

## 4. API contract

- `POST budget-planning/aip/{aipId}/offices/{officeId}/comments` gains optional `parentCommentId`.
  Without it: a head (reviewers only, as today). With it: a reply (reviewers and in-scope encoders).
  New 400s as rows 5–7; 403 as rows 2 and 8.
- `GET …/comments` returns `parentCommentId` and `canReply` per head (plus the existing
  `canResolve`); side may be `Encoder` on replies.
- `POST budget-planning/aip/comments/{id}/resolve`: 400 on a reply.
- Changed counts (decision 8): no shape change, values ignore replies.

## 5. Data model

`aip_review_comments` gains `parent_comment_id int NULL` (FK to itself, no action on delete).
⚠️ Comments are anchored by `node_type` + `node_id`, not a foreign key to the activity, so check what
deleting an activity (or a whole AIP group) does with its comments today, and make replies follow
the same rule. Whatever deletes a head must delete its replies first, or the self-FK refuses it;
verify on real SQL Server. `author_side` gains the value `Encoder` (string column, no schema change). Migration
`AddAipCommentThreads`. Index `(aip_office_id, parent_comment_id, resolved_at)` if the counts need it.

## 6. UI states

Where AIP comments show today (AIP Review drill-down and full view, AIP Entry's comment panel, the
review modal):
- Each head shows its replies indented beneath, oldest first, and a **Reply** box when `canReply`.
- Encoders see Reply on open threads in their scope; they no longer see "Only a reviewer can leave a
  comment" for replies, but still cannot start a thread (no new-comment box for them).
- Resolved threads collapse as today and show no Reply box. Reviewer-edit notes unchanged.
- Model the thread rendering on the proposal Comments panel (PPDO-220).

## 7. Non-goals

- No encoder-started threads. No replies on reviewer-edit notes. No anchoring above activities.
- No edit/delete/un-resolve. No email.

## 8. Deployment notes

Migration `AddAipCommentThreads` (the seventh v1.9.0 migration), before the code; add to
`Pre_Deployment_Checklist_v1.9.0.md`. Existing rows are untouched.

## 9. Ticket split

One ticket, PPDO-223, built by one **Opus** session: backend (column, side, reply rules, every count,
bell events) then frontend (thread rendering in each comment surface). Split into two PRs if the
diff is large.

## 10. Acceptance checklist

- [ ] As PPDO, open a thread on a PTO activity; as `pto.user` (encoder), reply; the thread stays open.
- [ ] As `pto.user`, no way to start a thread; the API refuses with 403.
- [ ] As `pto.head`, send to PPDO: refused by the open PPDO thread; after PPDO resolves it, the send goes through.
- [ ] A reply cannot be resolved; a resolved thread and a reviewer-edit note take no replies.
- [ ] Bell entries for the new thread, the reply and the resolve go to the right people, never the actor.
- [ ] Old comments look the same as before, as threads without replies.

## 11. Test focus

- **Every open count ignores replies** (red-test: drop `parent_comment_id IS NULL` from the re-submit
  gate and watch a test with one encoder reply fail).
- Encoder reply scope incl. the division axis; encoder head refused.
- Head-only resolve; replies to resolved threads and to replies refused.
- Whatever deletes a head (activity delete, group delete, any cleanup) also removes its replies, on real SQL Server.
- Bell recipients per event, never the actor.
