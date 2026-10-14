---
status: accepted — 2026-10-14
version: v1.9.0
tickets: PPDO-219 (T1 backend), PPDO-220 (T2 frontend)
supersedes: —
---

# v1.9.0 — Comments on Investment Proposals

Demo 3 (2026-10-14), item "IP comments". Settled with Ralph the same day: both PPDO and the office
comment; one thread per proposal; comments are resolved, and open ones block Finalize.

## 1. Goal

PPDO reviewers and the office can leave remarks on an Investment Proposal and track them to done,
so a proposal is not finalized while a remark on it is still open. Today the only channel is
outside the portal.

## 2. Decisions

### Settled (Ralph, 2026-10-14)

1. **Who comments: both.** Anyone who can open the proposal can comment on it: the office's own
   users (encoders, department head) and PPDO cross-office reviewers (`CanReviewAllOffices`, who
   are otherwise read-only on proposals).
2. **One thread per proposal.** Not anchored to a section, field or activity.
3. **Comments are resolved, and an open comment blocks Finalize.**

### Follow-ups — settled (Ralph, 2026-10-14; 4, 7 and 10 confirmed, the rest are the proposed defaults)

4. **Each comment has a side, PPDO or Office, and only that side resolves it.** Copied from the AIP
   comments (PPDO-72). Side = **Office** when the caller's `users.office_id` is the proposal's
   office; otherwise **PPDO**. (A PPDO host-office user commenting on PPDO's own proposal is Office.)
   Reason: if the office could clear PPDO's remark, the Finalize gate would be self-marking.
   ⚠️ Consequence: an open PPDO comment blocks the office's Finalize until a PPDO reviewer resolves
   it. That is the AIP re-submit gate's behaviour too.
5. **Any user on the author's side may resolve**, not only the author (a colleague can close it).
6. **No edit, no delete, no un-resolve.** A comment is a record. A wrong one is resolved with a
   follow-up comment. Matches the AIP comments.
7. **Comments can be added to a Final proposal** (they do not reopen it). They block the next
   Finalize after a Reopen.
8. **Deleting a Draft proposal deletes its comments** (cascade). A proposal with open comments can
   still be deleted.
9. **Plain text, 1–2,000 characters**, same limit as AIP comments.
10. **Notification: the list pill now, a notification bell next.** The proposals list shows an
    "N open" pill per project (this spec). Ralph also wants a Facebook-style bell beside the user's
    name in the top bar, with clickable entries: that is its own feature, shared by every
    notification in the portal, and is specced separately (`Notification_Centre_Spec.md`). These
    comments are one of its first event types.

## 3. Behaviour

| # | Given | When | Then |
|---|---|---|---|
| 1 | A Draft proposal in the caller's scope | An office encoder posts "Fix the objective" | 201. Shown at the bottom of the thread, side Office, open |
| 2 | Same | A PPDO reviewer posts | 201, side PPDO. Allowed although the reviewer cannot edit the proposal |
| 3 | One open comment | The office clicks **Finalize** | 409 "Resolve the 1 open comment before finalizing." Nothing changes |
| 4 | An open PPDO comment | An office user clicks **Resolve** on it | 403 "Only PPDO can resolve a PPDO comment." |
| 5 | An open Office comment | Another office user resolves it | 200. Resolved, with who and when |
| 6 | All comments resolved | Finalize | 200, as today |
| 7 | A Final proposal | A PPDO reviewer comments | 201. Stays Final. After a Reopen, Finalize is blocked until it is resolved |
| 8 | Empty or 2,001-character body | Post | 400 with the message. Nothing written |
| 9 | Already resolved | Resolve again | 400 "This comment is already resolved." |
| 10 | A proposal outside the caller's scope, or no such id | Read, post or resolve | 404 "Proposal not found." (existence never confirmed) |
| 11 | A Draft with comments | Delete the proposal | 204. Comments gone with it |

### Per role

| Caller | Read thread | Post | Resolve |
|---|---|---|---|
| Office encoder / Staff in scope | ✅ | ✅ Office side | Office comments |
| Department head, own office | ✅ | ✅ Office side | Office comments |
| PPDO cross-office reviewer | ✅ every office | ✅ PPDO side | PPDO comments |
| Host Admin / SuperAdmin on another office | ✅ | ✅ PPDO side | PPDO comments |
| Staff in a divisioned office with no division | ✅ (as proposal read) | ✅ (a comment is not proposal content) | Office comments |
| Out of scope / null office | 404 | 404 | 404 |

⚠️ Posting is a **read-scope** action (`OfficeScope.ResolveForReview`), not a write. It must not go
through `ReviewerWriteGuard`, or the PPDO reviewer could not comment. The same exemption the AIP
comments have; the reflective `ReviewerWriteGuardCoverageTests` needs the endpoint listed as exempt.

## 4. API contract

All under the existing proposal scope rules (§3.2 of `../v1.8/Investment_Proposal_Spec.md`).

- `GET /api/budget-planning/proposals/{id}/comments` → `200 [{ id, body, side: "Ppdo"|"Office",
  authorName, createdAt, resolvedAt?, resolvedByName? }]`, oldest first.
- `POST /api/budget-planning/proposals/{id}/comments` body `{ body }` → `201` the comment.
  `400` empty / too long; `404` out of scope.
- `POST /api/budget-planning/proposals/{id}/comments/{commentId}/resolve` → `200` the comment.
  `403` wrong side; `400` already resolved; `404`.
- Changed: `POST /proposals/{id}/finalize` → new `409` "Resolve the {n} open comment(s) before
  finalizing." checked before the row-version check.
- Changed: the list rows (`GET /proposals`) gain `openCommentCount`.

## 5. Data model

New table **`investment_proposal_comments`** (snake_case, additive migration
`AddInvestmentProposalComments`):

| Column | Type | Null | Notes |
|---|---|---|---|
| `id` | int identity | no | PK |
| `proposal_id` | int | no | FK `investment_proposals.id`, **on delete cascade**; indexed |
| `body` | nvarchar(2000) | no | |
| `author_side` | nvarchar(10) | no | `Ppdo` / `Office` |
| `author_id` | int | no | FK users |
| `created_at` | datetime2 | no | UTC |
| `resolved_at` | datetime2 | yes | |
| `resolved_by_id` | int | yes | FK users |

## 6. UI states

On the proposal editor, a **Comments** panel (right side on wide screens, below on narrow):
- **Loading**: skeleton rows. **Error**: "Could not load comments." + Retry.
- **Empty**: "No comments yet."
- Each comment: side tag (PPDO / Office), author, time, body; **Resolve** only for the caller's side;
  resolved ones collapse under "N resolved".
- Composer at the bottom with a 0/2000 counter. Shown for every caller who can read, including on a
  Final proposal and to PPDO reviewers.
- **Finalize** with open comments: the button stays enabled; clicking shows the 409 message and
  scrolls to the panel. A note under the button: "N open comments."
- Proposals list: an "N open" pill on rows with open comments.

## 7. Non-goals

- No anchoring to a section, field or activity. No threaded replies, mentions, edits or deletes.
- No email. The bell is a separate spec (decision 10).
- No comments on FY ≤ 2027 projects (no proposals there).
- The Word export does not print comments.

## 8. Deployment notes

One additive migration (`AddInvestmentProposalComments`), run before the code. Add it to
`Pre_Deployment_Checklist_v1.9.0.md`.

## 9. Ticket split

| Ticket | Scope | Depends on |
|---|---|---|
| T1 Backend | Entity, migration, repo; service (create, list, resolve, side rule); finalize gate; list count; endpoints (read-scope, guard-exempt); tests incl. red-tested side rule and gate | — |
| T2 Frontend | Comments panel on the editor; Finalize message; list pill | T1 |

## 10. Acceptance checklist

- [ ] As an office encoder, comment on a Draft proposal; it shows as Office, open.
- [ ] As a PPDO reviewer (`ppdo.finance`), comment on another office's proposal; it shows as PPDO.
- [ ] As the office, Finalize is refused naming the open count.
- [ ] As the office, Resolve is not offered on the PPDO comment; the API refuses it with 403.
- [ ] As PPDO, resolve the PPDO comment; as the office, resolve the Office comment; Finalize succeeds.
- [ ] Comment on the Final proposal as PPDO; Reopen; Finalize is blocked again.
- [ ] The proposals list shows the "N open" pill and it clears when all are resolved.
- [ ] A user of another office gets 404 on the thread.

## 11. Test focus

- The side rule both ways (red-test: let the other side resolve, watch the test fail).
- The Finalize gate (red-test: remove it).
- Read-scope posting by a reviewer, and `ReviewerWriteGuardCoverageTests` listing the endpoint.
- Cascade on proposal delete.
