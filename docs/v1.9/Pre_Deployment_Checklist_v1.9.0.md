---
status: final — 2026-10-16 (v1.9.0 frozen)
version: v1.9.0
tickets: PPDO-211, PPDO-212, PPDO-213, PPDO-214, PPDO-215, PPDO-216, PPDO-219, PPDO-221, PPDO-223, PPDO-224, PPDO-228, PPDO-229
---

# v1.9.0 — Pre-Deployment Checklist

> **Final as of 2026-10-16:** v1.9.0 is complete (last merge #508, PPDO-223) and frozen. A UAT fix that
> adds a migration or a step still adds its lines here, in the same PR.
>
> Layout follows [Pre_Deployment_Checklist_v1.8.2.md](../v1.8/Pre_Deployment_Checklist_v1.8.2.md).

## 0. Before you start

- [ ] **Every 1.8.x migration has run on the target first.** v1.9.0 is cut from `release/1.8.4`.
  **UAT:** already true (v1.8.4 reached `uat` via #486, last 1.8.x migration `AddAipConcurrencyTokens`).
  **Production:** v1.8.0 → v1.8.4 go first, with their own checklists.
- [ ] **Migration count: six**, all additive (none rewrites a value). Re-check:

  ```bash
  git diff --name-only origin/release/1.8.4...origin/release/1.9.0 -- backend/PPDO.Infrastructure/Data/Migrations | grep -v Designer | grep -v Snapshot
  ```

## 1. Database

### What the migration does

`20261014064212_AddReviewerEditAcknowledgement` (PPDO-213), on `aip_review_comments` only:

| Change | Detail |
|---|---|
| `is_reviewer_edit` | `bit NOT NULL DEFAULT 0`. Every existing comment becomes `0`, which is correct: none were written by a reviewer edit |
| `acknowledged_at` | `datetime2 NULL` |
| `acknowledged_by_id` | `uniqueidentifier NULL`, FK `Users.Id`, `ON DELETE RESTRICT` |
| Two indexes | `IX_aip_review_comments_office_reviewer_edit_ack`, `IX_aip_review_comments_acknowledged_by_id` |

Additive only: no existing value changes, so no baseline sums are needed.

`20261014084154_AddFundingSourceEnforceAipCeiling` (PPDO-216), on `funding_sources`:

| Change | Detail |
|---|---|
| `enforce_aip_ceiling` | `bit NOT NULL DEFAULT 0`, then set to `1` on the shared `GF` row only |

Additive. Every other fund stays unenforced until finance turns it on, so no office's checks change
at deploy time.

`20261015011949_AddInvestmentProposalComments` (PPDO-219): a **new table**, `investment_proposal_comments`
(`id`, `proposal_id`, `body nvarchar(2000)`, `author_side`, `author_id`, `created_at`, `resolved_at`,
`resolved_by_id`, `parent_comment_id`). Threaded: a null `parent_comment_id` is a head comment, and only
open heads block Finalize.

| Change | Detail |
|---|---|
| FK `proposal_id` → `investment_proposals.id` | `ON DELETE CASCADE`: deleting a Draft proposal deletes its comments |
| FKs `author_id`, `resolved_by_id` → `Users.Id` | `ON DELETE RESTRICT` |
| FK `parent_comment_id` → the same table | `NO ACTION`; the proposal cascade still removes a head and its replies in one delete |
| Index | `IX_investment_proposal_comments_proposal_resolved` on `(proposal_id, resolved_at)`, plus the two user-FK indexes |

Additive only and empty at creation: no existing row changes, so no baseline sums are needed.

`20261015025922_AddNotifications` (PPDO-221): a **new table**, `notifications` (`id bigint`, `user_id`,
`type nvarchar(40)`, `text nvarchar(400)`, `link nvarchar(400)`, `created_at`, `read_at`), one row per
recipient per event, behind the top-bar bell.

| Change | Detail |
|---|---|
| FK `user_id` → `Users.Id` | `ON DELETE CASCADE`. `user_id` is a `uniqueidentifier` (`Users.Id` is a GUID; the spec's "int" was a slip) |
| Index | `IX_notifications_user_read_created` on `(user_id, read_at, created_at DESC)` for the unread count and the newest-first list |

Additive and empty at creation. **Nothing is back-filled**: the bell starts empty on deploy (spec §8).

`20261015084843_AddInitialWfpExportJobs` (PPDO-228): the bulk initial WFP export's job queue
([Bulk_Initial_WFP_Export_Spec.md](Bulk_Initial_WFP_Export_Spec.md) §5). A **sequence** and a **new table**:

| Change | Detail |
|---|---|
| Sequence `background_job_ids` | Ids for every job table (EF Core TPC: one table per job kind, ids unique across all of them) |
| Table `initial_wfp_export_jobs` | `id` (`DEFAULT NEXT VALUE FOR background_job_ids`), `status`, `requested_by_id`, `created_at`, `claimed_at`, `heartbeat_at`, `attempts`, `message`, `finished_at`, `fiscal_year`, `done_count`, `total_count`, `problems_json`, `file_key`, `expires_at` |
| FK `requested_by_id` → `Users.Id` | `ON DELETE RESTRICT` |
| Indexes | `IX_initial_wfp_export_jobs_fiscal_year_status`; `IX_initial_wfp_export_jobs_status_created_at` (the claim); **filtered unique** `IX_initial_wfp_export_jobs_fiscal_year_active` on `fiscal_year` where `status IN ('Queued','Running')`: one active export per year |

Additive and empty at creation.

⚠️ The filtered index needs `QUOTED_IDENTIFIER ON` for any write to the table. EF and the app always
connect that way; a hand-run `sqlcmd` does not unless given `-I`.

`20261016134525_AddAipCommentThreads` (PPDO-223, [AIP_Comment_Threads_Spec.md](AIP_Comment_Threads_Spec.md)),
on `aip_review_comments`:

| Change | Detail |
|---|---|
| `parent_comment_id` | `int NULL`. Null is a thread's first comment (a head); every existing comment becomes a head with no replies |
| FK `parent_comment_id` → the same table | `NO ACTION`. A reply carries its head's office row and node, so deleting an activity, a program or an office group still removes a head and its replies in one statement |
| Index | `IX_aip_review_comments_parent_comment_id` |

Additive only: no existing value changes. `author_side` may now also read `Encoder` (an encoder's
reply); it is a string column, so no schema change.

### Steps (UAT first, then production)

- [ ] ⚠️ **Run it BEFORE the code deploys.** The new code reads `is_reviewer_edit` on every portal page
  (the notifications read), so code without the column fails every page load. PPDO-219 is the same
  kind of dependency: the proposals list and Finalize read `investment_proposal_comments`, so code
  without the table fails both. So does PPDO-221: commenting on a proposal, every AIP hand-off and every
  PPDO reviewer edit write `notifications` in the same save, so code without the table fails all of
  them. PPDO-228's endpoints read `initial_wfp_export_jobs`, and its 30-second timer starts with the
  code. PPDO-223's comments read selects `parent_comment_id`, so code without the column fails every
  AIP comment list, on AIP Entry and the review screen. **CI does not run migrations.**
- [ ] **Apply.** From `backend/`, with `SqlConnectionString` pointing at the target database:

  ```bash
  dotnet ef database update --project PPDO.Infrastructure --startup-project PPDO.Functions
  ```

- [ ] **Verify (PPDO-216).** Only the General Fund is enforced:

  ```sql
  SELECT code, office_id, enforce_aip_ceiling FROM funding_sources WHERE enforce_aip_ceiling = 1;
  -- expect exactly one row: GF, NULL, 1
  ```

- [ ] **Verify (PPDO-219).** The table exists, is empty, and the proposal FK cascades:

  ```sql
  SELECT COUNT(*) AS comments FROM investment_proposal_comments;   -- expect 0
  SELECT name, delete_referential_action_desc FROM sys.foreign_keys
  WHERE parent_object_id = OBJECT_ID('investment_proposal_comments');
  -- expect proposal_id -> CASCADE; author_id, resolved_by_id and parent_comment_id -> NO_ACTION
  ```

- [ ] **Verify (PPDO-221).** The table exists, is empty, and its index is there:

  ```sql
  SELECT COUNT(*) AS notifications FROM notifications;   -- expect 0
  SELECT name FROM sys.indexes WHERE object_id = OBJECT_ID('notifications');
  -- expect PK_notifications and IX_notifications_user_read_created
  ```

- [ ] **Verify (PPDO-228).** The sequence and table exist, the table is empty, the active-job index is filtered:

  ```sql
  SELECT name, CAST(current_value AS int) AS current_value FROM sys.sequences WHERE name = 'background_job_ids';
  SELECT COUNT(*) AS jobs FROM initial_wfp_export_jobs;   -- expect 0
  SELECT name, is_unique, filter_definition FROM sys.indexes WHERE object_id = OBJECT_ID('initial_wfp_export_jobs');
  -- expect IX_..._fiscal_year_active: is_unique 1, filter ([status]=N'Queued' OR [status]=N'Running')
  ```

- [ ] **Verify (PPDO-223).** The column exists, every comment is a head, the FK is NO ACTION:

  ```sql
  SELECT COUNT(*) AS total, SUM(CASE WHEN parent_comment_id IS NULL THEN 1 ELSE 0 END) AS heads
  FROM aip_review_comments;   -- expect total = heads = the pre-deploy count
  SELECT name, delete_referential_action_desc FROM sys.foreign_keys
  WHERE name = 'FK_aip_review_comments_aip_review_comments_parent_comment_id';   -- expect NO_ACTION
  ```

- [ ] **Verify (PPDO-213).** The columns exist and nothing is flagged yet:

  ```sql
  SELECT COUNT(*) AS total,
         SUM(CASE WHEN is_reviewer_edit = 1 THEN 1 ELSE 0 END) AS reviewer_edits,
         SUM(CASE WHEN acknowledged_at IS NOT NULL THEN 1 ELSE 0 END) AS acknowledged
  FROM aip_review_comments;
  -- expect: total = the pre-deploy count, reviewer_edits = 0, acknowledged = 0
  ```

### Rollback

`dotnet ef database update 20261005235915_AddAipConcurrencyTokens` drops all six migrations' columns,
indexes, the `investment_proposal_comments`, `notifications` and `initial_wfp_export_jobs` tables and the
`background_job_ids` sequence. Run it only **after** rolling the code back,
because the code needs them. Acknowledgements and reviewer-edit flags recorded since the deploy are
lost, and so is **every AIP comment reply** (PPDO-223: dropping the column would leave replies as
loose heads, so delete them first with `DELETE FROM aip_review_comments WHERE parent_comment_id IS NOT NULL`),
**every Investment Proposal comment** (PPDO-219), **every notification** (PPDO-221) and the
**export job history** (PPDO-228; finished ZIPs left in Blob storage then belong to no job: delete the
`initial-wfp-exports/` folder in the container). The AIP comments themselves remain.

### File storage (PPDO-228)

Finished bulk-export ZIPs are kept in Blob storage for 7 days. Set this up **before** the code deploys.
⚠️ With no `FileStore__Provider` the app falls back to `Local`, a folder in the instance's temp
directory: it starts fine, but each instance has its own folder and loses it on restart, so downloads
would randomly answer "expired". `Blob` without a container name, or an unknown provider, stops the app
at startup instead.

- [ ] **Create the container** in `ppdoportalstorage` (the `AzureWebJobsStorage` account), private
  access: e.g. `wfp-exports` (UAT: `wfp-exports-uat` if UAT shares the account).
- [ ] **App settings** on `ppdo-portal-api-sea` (and the UAT Function App):
  `FileStore__Provider` = `Blob`, `FileStore__BlobContainer` = the container's name. Leave
  `FileStore__LocalPath` unset in Azure.
- [ ] **No other new setting.** The Blob store uses the existing `AzureWebJobsStorage` connection.

PPDO-229 (the worker) and PPDO-230 (the Download all UI) are in v1.9.0, so a requested export runs
end to end on deploy and writes its ZIP to the container.

## 2. Tell people first

- [ ] **The GSO (external API consumer):** consolidated AIP figures can now change after
  consolidation. A PPDO reviewer may raise an activity's amounts during the LFC adjustment (PPDO-212),
  and the next `GET external/v1/aip` returns the new figures. Nothing in the API contract changes.
- [ ] **Office encoders and department heads:** PPDO may edit their AIP after it is submitted. Each
  change appears as an "Edited by PPDO" comment with the reason, and AIP Entry (plus a sidebar pill)
  shows a notice until someone in the office clicks **Mark as seen**.

- [ ] **PPDO finance (PPDO-216):** once they tick "Enforce AIP ceiling" on a fund (TF-PhilHealth
  first), every office with lines on that fund and **no ceiling set** is blocked from sending to PPDO.
  Set those offices' ceilings on Office Ceilings straight after turning the flag on.

- [ ] **Office encoders, department heads and PPDO reviewers (PPDO-219):** a proposal now has a
  comments section made of threads. Either side may reply to an open thread. An **open thread** blocks
  **Finalize**, and only the side that started it (PPDO or the office) can resolve it, so an open PPDO
  thread holds the office's Finalize until a PPDO reviewer resolves it.

- [ ] **PPDO finance and office department heads (PPDO-224):** from FY 2028 the WFP report shows an
  **initial WFP read off the AIP**, for an office only once PPDO has marked **every** one of its AIP groups
  as done (Consolidated). Offices can now open their own (never another office's). Lines without items are
  spread over the activity's months; the 10% reserve is on MOOE and CO only. No migration; it needs the
  PPDO-225 frontend to show it.

- [ ] **Office encoders, department heads and PPDO reviewers (PPDO-223):** AIP activity comments are now
  threads, like the proposal comments. Encoders can **reply** inside a PPDO or department-head thread on
  an activity they can edit (their own division's), but still cannot start one. Replies do not count as
  open comments; only the side that started a thread resolves it. The bell rings for a new thread, a
  reply and a resolve.

## 3. After the deploy (UAT, then production)

- [ ] As a PPDO reviewer, open an office in `SubmittedToPpdo`, use **Edit amounts** on an activity,
  change a line with a reason, and save. The office's state does not change.
- [ ] Saving without a reason is refused. Delete is not offered.
- [ ] As that office's encoder: the "Edited by PPDO" comment shows the old → new figure and the reason;
  the AIP Entry banner and the "PPDO edited" sidebar pill show; **Mark as seen** clears both.
- [ ] The office's open-comment count and re-submit warning did **not** go up because of the edit.
- [ ] A user of another office sees none of it.
- [ ] **Proposal comments (PPDO-219).** As an office encoder, `POST` a comment on a Draft proposal: it
  is side `Office`, open. As `ppdo.finance`, comment on another office's proposal: side `Ppdo`.
  Reply to it from the other side (allowed); a reply to a reply, or to a resolved thread, is a 400, and
  so is resolving a reply. Finalize as the office is refused with "Resolve the 1 open comment before
  finalizing." (409, nothing changes; replies are not counted). The office cannot resolve the PPDO comment (403); PPDO resolves it, the office resolves its
  own, and Finalize then succeeds. A user of another office gets 404 on the thread.
- [ ] **Notifications (PPDO-221).** After the proposal comment above, `GET /api/notifications/unread-count`
  as one of that office's users is 1 or more, and as the commenter it is unchanged. Reply to the thread:
  only the thread's starter and earlier repliers get a `ProposalReply` row. Return an office's AIP as a
  PPDO reviewer: its users get "PPDO returned your FY … AIP …". `POST /api/notifications/{id}/read` with
  another user's id is 404; with your own it is 204, twice. Spot-check who received an event:

  ```sql
  SELECT u.Username, n.type, n.created_at FROM notifications n JOIN Users u ON u.Id = n.user_id
  ORDER BY n.id DESC;
  ```

- [ ] **Initial WFP (PPDO-224).** FY 2027's WFP report is unchanged for PPDO. For FY 2028, an office
  with a group not yet Consolidated is not in the office list, and its preview answers 409 "… is not marked
  as done yet." Once it is Consolidated: each fund block's net equals the office's AIP total for that fund
  (base figures), PS lines carry no reserve, and the export's title row reads "INITIAL WFP (FROM AIP)". As a
  user of that office the report opens for their own office; another office's id answers 404.

- [ ] **Bulk export plumbing (PPDO-228).** As `ppdo.finance`: `POST /api/budget-planning/wfp/report/bulk-exports`
  `{ "fiscalYear": 2027 }` is 400; for a year with no office Consolidated it is 409 and no row is created;
  for a year with one it is 202 and a Queued job, and a second POST is 200 with the **same** id. As an office
  user, every bulk-exports call is 403. `GET …/bulk-exports?fiscalYear=` for a year with no job is 204. In
  Application Insights, `Functions.ExportJobTimer` runs every 30 s and succeeds.

- [ ] **AIP comment threads (PPDO-223).** As a PPDO reviewer, comment on an activity of an office in
  `SubmittedToPpdo`. As that office's encoder, reply: 201, side `Encoder`, the thread stays open; the
  dashboard's open count and the re-submit warning are unchanged by the reply. Starting a thread as the
  encoder is a 403; a reply to a reply, to a resolved thread or to a reviewer-edit note is a 400. After
  PPDO resolves the thread, the department head's send to PPDO shows no unresolved-comment warning. The
  thread's starter got a reply bell entry and the encoder got the resolve one, never the actor.

## Rollback summary

| Layer | How |
|---|---|
| Code | Redeploy the previous `main` |
| Database | `dotnet ef database update 20261005235915_AddAipConcurrencyTokens`, after the code |
