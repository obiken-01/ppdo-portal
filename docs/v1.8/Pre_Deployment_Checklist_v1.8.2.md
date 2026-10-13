---
status: current
version: v1.8.2
tickets: PPDO-111, PPDO-112, PPDO-113, PPDO-192, PPDO-117…120, PPDO-141, PPDO-142, PPDO-178…181, PPDO-187…191, PPDO-193
---

# v1.8.2 — Pre-Deployment Checklist

> Work through this **before** `release/1.8.2` goes to UAT, and again before it goes to `main`.
> Pushing to `uat` or `main` auto-deploys both frontend and backend. **CI does not run EF
> migrations**, so the database step is manual and comes first.
>
> v1.8.2 is much smaller than v1.8.0 on the database side: **one additive migration, no data
> rewritten.** What it does have is two things users notice: **everyone is signed out once**
> (PPDO-141), and the concurrent-edit guard starts protecting saves (V18-71, warn-and-proceed stage).

---

## 0. Before you start

- [ ] **v1.8.0 + v1.8.1 are in production**, deployed through
      [Pre_Deployment_Checklist.md](Pre_Deployment_Checklist.md). v1.8.2 was cut from the frozen
      `release/1.8.1` and assumes all 30 of that release's migrations have run. Do not take
      `release/1.8.2` to `uat` while v1.8.1 is still being tested there.
- [ ] **Every UAT fix on `release/1.8.1` is merged forward into `release/1.8.2`.** Expect no output:

      ```bash
      git fetch origin
      git log --oneline origin/release/1.8.2..origin/release/1.8.1
      ```

      Checked 2026-10-07: no output (no fixes since the cut at `88bb84c5`). Re-run it on the day.

- [ ] **Migration count is still one.** Re-check rather than trust this page:

      ```bash
      git diff --name-only origin/release/1.8.1...origin/release/1.8.2 \
        -- backend/PPDO.Infrastructure/Data/Migrations | grep -v Designer | grep -v ModelSnapshot
      ```

      Expect exactly `20261005235915_AddAipConcurrencyTokens.cs`.

---

## 1. Database

### What the migration does

| Migration | Kind |
|---|---|
| `20261005235915_AddAipConcurrencyTokens` (V18-71 / PPDO-117) | Schema, additive. Adds `row_version` (`rowversion`, not null) to `aip_activities` and `aip_expenditures`; `updated_at` (`datetime2`, null) to `aip_activities`; `updated_by_id` (`uniqueidentifier`, null) to both, each with an index and an FK to `Users.Id` (`Restrict`). **No backfill, no `Sql()`, no existing value changed.** SQL Server fills `row_version` for existing rows itself; `updated_at` / `updated_by_id` stay null until a row is next saved, which the conflict message reads as "not edited since this shipped". |

⚠️ **Run it before the code deploys.** EF maps `row_version` on every activity and expenditure read
from v1.8.2 on, so code that reaches an un-migrated database fails **every AIP Entry, AIP detail
and WFP read** with `Invalid column name 'row_version'`.

### Steps (UAT first, then production)

- [ ] **Confirm a restore path.** Azure SQL Basic keeps point-in-time restore; confirm the retention
      in the portal. The migration is additive, so this is precaution, not expectation.

- [ ] **Check the migration has not run under its old ID.** It was first generated on 2026-09-22
      as `20260922022444_AddAipConcurrencyTokens` and regenerated with a new timestamp when it was
      brought into v1.8.2. Only local dev databases should carry the old ID; check anyway:

      ```sql
      SELECT MigrationId FROM __EFMigrationsHistory WHERE MigrationId LIKE '%AddAipConcurrencyTokens';
      ```

      - **No rows** → normal; apply below.
      - **`20261005235915_…`** → already applied; skip the apply step.
      - **`20260922022444_…`** → the columns already exist. Do **not** let EF run it again (it would
        fail on duplicate columns). Rename the history row instead, then apply:

        ```sql
        UPDATE __EFMigrationsHistory
           SET MigrationId = '20261005235915_AddAipConcurrencyTokens'
         WHERE MigrationId = '20260922022444_AddAipConcurrencyTokens';
        ```

- [ ] **Apply.** From `backend/`, with `SqlConnectionString` pointing at the target database:

      ```bash
      dotnet ef database update --project PPDO.Infrastructure --startup-project PPDO.Functions
      ```

- [ ] **Verify.** Row counts unchanged, every row has a version, nobody is stamped yet:

      ```sql
      SELECT COUNT(*) AS activities,
             COUNT(DISTINCT row_version) AS distinct_versions,
             SUM(CASE WHEN updated_at IS NULL THEN 1 ELSE 0 END) AS never_stamped
      FROM aip_activities;

      SELECT COUNT(*) AS lines, COUNT(DISTINCT row_version) AS distinct_versions
      FROM aip_expenditures;
      ```

      Expect `distinct_versions` = the row count, and `never_stamped` = the row count. Capture the
      `activities` and `lines` counts **before** applying as well; they must match.

### Rollback

`Down` drops the five columns, their indexes and FKs. Safe at any time **as far as data goes**: the
only thing lost is the `updated_at` / `updated_by_id` stamps, which feed the "changed by … at …"
wording and nothing else. ⚠️ **Roll the code back first**: v1.8.2 code against a database without
the columns fails every AIP read, which is the failure the "migration first" rule exists to avoid.

---

## 2. Application

- [x] ✅ `APP_VERSION` reads `v1.8.2` (`frontend/src/lib/version.ts`).
- [x] ✅ **Backend tests green** on the release branch: 3,140 passed, 0 failed (2026-10-07, at the
      PPDO-193 merge, #457). Re-run on the day with `cd backend && dotnet test`.
- [x] ✅ **Frontend tests green:** Vitest 72 passed (2026-10-07, at #460). `cd frontend && npm test`.
- [ ] **CLAUDE.md's Implementation Status** gets a v1.8.2 row and the footer date stamp, in the merge
      that takes v1.8.2 to `main` (the release ritual in CLAUDE.md).
- [ ] ⚠️ **Watch the first deploy run end to end** (PPDO-190, #443). The GitHub Actions in
      `ci.yml` and `deploy.yml` moved to their Node 24 majors; `deploy.yml` has **never run** with
      them, because `release/1.8.2` has not been to `uat` yet. Watch the Functions publish, the SWA
      deploy and the health smoke test in the `uat` run before trusting the `main` run.
- [ ] Azure Functions **CORS** on `ppdo-portal-api-sea` still lists the SWA origin (portal only).

---

## 3. Tell people first

- [ ] ⚠️ **Everyone is signed out once** (PPDO-141, #449). Refresh tokens are now stored as SHA-256
      hashes, so the plaintext tokens in `Users.RefreshToken` stop matching. Each user's next silent
      refresh fails and they land on the login page (`?reason=token_superseded`), once. No data is
      touched. Tell the UAT testers before the UAT deploy and the offices before production; deploy
      outside office hours if possible.

      *Optional, your sign-off (production data):* clear the stale plaintext values, which no longer
      match anything:

      ```sql
      -- after the deploy; capture the count first
      SELECT COUNT(*) FROM Users WHERE RefreshToken IS NOT NULL AND LEN(RefreshToken) <> 64;
      UPDATE Users SET RefreshToken = NULL, RefreshTokenExpiry = NULL
       WHERE RefreshToken IS NOT NULL AND LEN(RefreshToken) <> 64;
      ```

      Verified against the code 2026-10-07: `Users.RefreshToken` / `RefreshTokenExpiry`
      (`UserConfiguration`), hashes are 64 lower-case hex characters (`RefreshTokenHasher` →
      `ApiKeyGenerator.Hash`), and the old plaintext tokens are 88 characters, so `LEN <> 64` picks
      out exactly the stale ones.

- [ ] **Ask users to reload any AIP tab left open from before the deploy.** Three reasons, all from
      the same old tab:
      1. Its saves send no `rowVersion`, so they still save **unguarded** (and each logs the warning
         in §5, which muddies PPDO-121's evidence).
      2. If it has a browser cache open (none exists yet in UAT or production; it arrives with
         v1.8.2), an old connection would block the upgrade to version 3 (PPDO-112, PPDO-192). New
         tabs fall back to live fetches after ~3 s, so nothing breaks, it is just
         slower until the old tab closes.
      3. It still shows the pre-redesign dashboard.

---

## 4. After the deploy (UAT, then production)

Smoke checks, one per change that a user can see. Use an office encoder account, a department head,
and a PPDO reviewer.

**Sign-in and health**
- [ ] Sign in, reload the page, wait past 15 minutes, act again: you stay signed in (hashed refresh
      tokens round-trip, #449).
- [ ] `GET /api/health` returns `{ status, api, database, utc }` and, when the DB is reachable,
      no `error` text (#450).

**Dashboard** (#447, #448, #451, #452)
- [ ] One status band at the top, no separate action card or pipeline rail; correct for an encoder,
      a department head and a reviewer.
- [ ] Investment proposals band shows; a reviewer sees the all-offices card.
- [ ] Recent activity reads as sentences, not table names.
- [ ] Calendar and the announcements editor still open (they now load lazily, #445).

**AIP Entry**
- [ ] Pickers load on the first visit, and instantly on the second (reference cache, #453).
- [ ] **Concurrent edit** (#454, #455, #457): two browsers, two users of one office, same activity.
      Save details in A, then in B → conflict panel in B naming A. Repeat for an expenditure line, and
      for **deleting** the activity from the stale browser (panel, activity kept). Then the
      regression check: two **different** activities saved at once → no panel for either.
- [ ] **Draft recovery** (#456): type in an activity's details, close the tab with **Leave**, reopen
      → "A local draft from … was found" banner; Restore brings the text back.
- [ ] **Expenditure-line draft** (#460): + Add Account, pick an account, type an amount, close the tab
      with **Leave**, reopen → "A local draft of a new expenditure line…" banner above Expenditures;
      Restore opens the add row filled in. Save it, reopen: no banner and no duplicate line.
- [ ] Saving an activity's details refreshes the submit checklist; the ceiling strip has **no** amber
      "as of" caption (#458 — it only appears when that refresh fails).

**Inventory**
- [ ] A small stock-balance import commits and the balances read right (set-based import, #446).

**Removed**
- [ ] `GET /api/dashboard/stats` is gone (404, #444). Nothing in the frontend called it; only worth
      knowing if an outside script did.

---

## 5. PPDO-121's evidence: UAT first, production confirms

The concurrent-edit guard ships in its **warn-and-proceed** state: a save with no `rowVersion` is
still accepted, and logged. PPDO-121 (milestone **v1.8.3**) turns that into a 400 once the warning
is shown not to appear.

⚠️ **Changed 2026-10-07 (Ralph): the evidence comes from the UAT test round, not a production window.**
Production goes straight from v1.7.4 to v1.8.0–v1.8.3 in one deploy, so no v1.8.x screen is in use
there before PPDO-121. The question "does every save path send `rowVersion`?" is answered by
testers saving in UAT. `ppdo-portal-api-uat` has Application Insights connected (checked 2026-10-07).

**In UAT, after the testers' first round:**

- [ ] **Run the query** (Application Insights → Logs):

      ```kusto
      traces
      | where cloud_RoleName =~ "ppdo-portal-api-uat"
      | where message startswith "AIP write without rowVersion"
      | summarize count() by tostring(customDimensions.Operation), tostring(customDimensions.Entity), bin(timestamp, 1d)
      ```

- [ ] **Expected: zero rows**, provided the testers did each of these: an inline activity edit, an
      AIP Entry details save, an expenditure edit and delete, an activity delete, and the WFP
      is-creation toggle. An empty result without those saves proves nothing.
- [ ] **Any rows:** `Operation` and `Entity` name the path, and `customDimensions.UserId` names who.
      Fix it before PPDO-121 starts.

**In production, after the deploy:**

- [ ] **Run the same query a day after,** with `cloud_RoleName =~ "ppdo-portal-api-sea"`. Expect a
      short tail from tabs left open across the deploy, and nothing after the first day. A count
      that stays up means something still saves without a version.

---

## Rollback summary

| Change | How to undo | Notes |
|---|---|---|
| Code | Revert the merge on `main` (or redeploy the previous commit) | Do this **before** the migration's `Down` |
| `AddAipConcurrencyTokens` | `dotnet ef database update 20261005015736_PriceIndexUniqueByStockCardNo` | Loses only the new edit stamps |
| Hashed refresh tokens | Reverting the code signs everyone out **again** (hashes stop matching) | Prefer fixing forward |
| Browser caches (IndexedDB) | Nothing to undo server-side | ⚠️ One-way: v1.8.2 creates `ppdo-aip-cache` at **version 3**. Code that opens it at a lower version (a revert to before PPDO-192) gets `VersionError`, caught as "no database": pickers fetch live and drafts stop until site data is cleared. Degraded, not broken |

---

*Created 2026-10-07, when the last v1.8.2 ticket (PPDO-113, #458) merged. Companion to
[Pre_Deployment_Checklist.md](Pre_Deployment_Checklist.md) (v1.8.0 + v1.8.1).*
