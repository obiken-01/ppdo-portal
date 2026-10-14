---
status: draft
version: v1.9.0
tickets: PPDO-211, PPDO-212, PPDO-213, PPDO-214, PPDO-215
---

# v1.9.0 — Pre-Deployment Checklist

> **Started 2026-10-14 with PPDO-213; still growing.** Every v1.9.0 ticket that adds a migration, a
> one-time step, or something users must be told adds its lines here, in the same PR. Re-check the
> migration count in §0 before using this page: it lists what was known when it was last edited.
>
> Layout follows [Pre_Deployment_Checklist_v1.8.2.md](../v1.8/Pre_Deployment_Checklist_v1.8.2.md).

## 0. Before you start

- [ ] **v1.8.0 → v1.8.4 are in production first**, with their own checklists done. v1.9.0 is cut from
  `release/1.8.4` and assumes every 1.8.x migration has run.
- [ ] **Migration count.** As of PPDO-216 there are two. Re-check rather than trust this page:

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

### Steps (UAT first, then production)

- [ ] ⚠️ **Run it BEFORE the code deploys.** The new code reads `is_reviewer_edit` on every portal page
  (the notifications read), so code without the column fails every page load. **CI does not run
  migrations.**
- [ ] **Apply.** From `backend/`, with `SqlConnectionString` pointing at the target database:

  ```bash
  dotnet ef database update --project PPDO.Infrastructure --startup-project PPDO.Functions
  ```

- [ ] **Verify (PPDO-216).** Only the General Fund is enforced:

  ```sql
  SELECT code, office_id, enforce_aip_ceiling FROM funding_sources WHERE enforce_aip_ceiling = 1;
  -- expect exactly one row: GF, NULL, 1
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

`dotnet ef database update 20261005235915_AddAipConcurrencyTokens` drops both migrations' columns
and indexes. Run it only **after** rolling the code back, because the code needs the columns.
Acknowledgements and reviewer-edit flags recorded since the deploy are lost. The comments themselves
remain.

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

## 3. After the deploy (UAT, then production)

- [ ] As a PPDO reviewer, open an office in `SubmittedToPpdo`, use **Edit amounts** on an activity,
  change a line with a reason, and save. The office's state does not change.
- [ ] Saving without a reason is refused. Delete is not offered.
- [ ] As that office's encoder: the "Edited by PPDO" comment shows the old → new figure and the reason;
  the AIP Entry banner and the "PPDO edited" sidebar pill show; **Mark as seen** clears both.
- [ ] The office's open-comment count and re-submit warning did **not** go up because of the edit.
- [ ] A user of another office sees none of it.

## Rollback summary

| Layer | How |
|---|---|
| Code | Redeploy the previous `main` |
| Database | `dotnet ef database update 20261005235915_AddAipConcurrencyTokens`, after the code |
