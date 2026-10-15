---
status: accepted — 2026-10-15
version: v1.9.0
tickets: PPDO-227 (epic) — PPDO-228 (T1 plumbing), PPDO-229 (T2 worker), PPDO-230 (T3 UI)
supersedes: —
---

# v1.9.0 — Bulk export of initial WFPs (background job)

Ralph, 2026-10-15: the initial WFP report stays per office (`Initial_WFP_Report_Spec.md`); add a
**Download all** that produces one ZIP with one Excel file per Consolidated office. Built as a
background job, **host-portable** (nothing Azure-specific in the core, in case the portal leaves
Azure). Claude implements all of it; Ralph reviews the worker's code closely.

## 1. Goal

PPDO gets every office's initial WFP for a fiscal year in one download, without waiting on one long
request, and can leave the page while it builds.

## 2. Decisions

### Settled (Ralph, 2026-10-15)

1. **One ZIP, one `.xlsx` per office**, each exactly what the per-office export produces
   (`IWfpReportService.GetInitialReportAsync` → `IWfpReportExcelService.Export`).
2. **Background job, portable design.**
   - **Queue** = our own job tables, **one table per job kind sharing an abstract base class**
     (Ralph, 2026-10-15, from DHL's DCE; EF Core TPC, `UseTpcMappingStrategy()`). The base
     `BackgroundJob` carries what every job has; each kind adds its own columns in its own table.
     The first kind is `InitialWfpExportJob` → `initial_wfp_export_jobs`. No Azure Storage Queue,
     no Durable Functions, no Hangfire (it needs an always-on server; revisit after a migration).
   - **Worker** = a plain class in `PPDO.Application` (`IExportJobWorker.RunOnceAsync`). It never
     references Azure.
   - **Host** = a thin wrapper that calls the worker: an Azure Functions **TimerTrigger** every
     30 s now, a .NET `BackgroundService` after a move.
   - **Files** = `IFileStore` (Save, Open, Delete): `BlobFileStore` in Azure, `LocalFileStore`
     for local dev and any non-Azure host. Downloads always stream through our API.
3. **The frontend polls** the job every 3 s while the page is open. No SignalR, SSE or WebSockets.
   The notification bell (PPDO-221) says when it is ready.
4. **Who:** PPDO report readers only, the users who can open the initial WFP for every office
   (`canOpenWfpReport`, host office). Office users keep their per-office export.
5. **One active job per fiscal year.** Asking again while one is Queued or Running returns that job
   (200) instead of starting another.
6. **A finished ZIP is kept 7 days**, then deleted; the job shows "Expired — run it again".
7. **An office that fails is skipped and noted.** The ZIP holds every office that built, plus
   `README.txt` listing each skipped office and the reason. The job ends **Done with problems**.
   Only a failure of the job itself (no offices eligible, storage down) ends it **Failed**.

### Proposed defaults

8. **The worker runs as the requester.** The job stores `requested_by_id`; the worker loads that
   user and calls `GetInitialReportAsync` with them, so the same scope applies as if they had
   clicked each export. A requester who has lost access by then fails the job ("You no longer
   have access to this report.").
9. **Offices are the eligible list at run time**, not at request time (whoever is Consolidated when
   the worker starts). The README states the time it was built.
10. **Claiming a job**: `Queued → Running` with `claimed_at`, in one statement that two workers can
    never both win (SQL Server `UPDATE TOP(1) … WITH (UPDLOCK, READPAST)`; PostgreSQL
    `FOR UPDATE SKIP LOCKED`), hidden in one repository method.
11. **Stuck jobs**: Running with no progress for 10 minutes goes back to Queued; after **3
    attempts** it is Failed ("The export stopped unexpectedly. Run it again."). Progress updates
    refresh `heartbeat_at`.
12. **Cleanup** runs in the same timer: expired ZIPs are deleted from the file store and their
    jobs marked Expired. Job rows are kept (they are the history).
13. **Names**: the ZIP is `Initial WFP FY{fy} - {yyyy-MM-dd HHmm}.zip` (Manila time); inside,
    `WFP_{OfficeCode}_{fy}.xlsx`, the per-office export's own name.
14. **Claim, recover-stuck and expire are generic**, written once against `BackgroundJob`
    (`ClaimNextAsync<TJob>()` and friends) and reused by every job table. A later kind (e.g. SAIP)
    adds a class, a table and its own worker step; the existing kinds are untouched.
15. **Logging: one scope per job** (Ralph's DCE habit of correlating by job id, made searchable).
    The worker wraps each job in `_logger.BeginScope(new { JobId, JobKind })`, so every line
    inside, including from services it calls, carries `JobId` (Seq locally, Application Insights in
    Azure: `customDimensions.JobId`). Events: claimed (attempt n) Information; office built
    (OfficeCode, ms) Information; office skipped (OfficeCode, reason) Warning; finished (status,
    done/total, ms) Information; recovered from stuck (attempt) Warning; failed Error with the
    exception. One Debug line per timer pass ("worker pass, n jobs") shows the timer is alive.
16. **Where the code lives**: base class and job entities in `PPDO.Domain`; `IFileStore` and the
    job repository interfaces beside the other contracts; the worker in `PPDO.Application`; the
    job repository (claim SQL), `LocalFileStore` and `BlobFileStore` in `PPDO.Infrastructure`; the
    timer trigger in `PPDO.Functions`. **Nothing is deployed outside the Function App.** After a
    move off Functions, a small `PPDO.Worker` project (.NET Worker Service: Windows service,
    Linux service or container) or a hosted service in an ASP.NET Core API calls the same worker.
17. **Storage**: a ZIP is ~1–3 MB, built in memory and saved once at the end (a failed job leaves
    nothing half-written). Seven-day expiry keeps Blob use small; revisit if it grows (Ralph:
    "as long as we don't fill the Azure storage").

## 3. Behaviour

| # | Given | When | Then |
|---|---|---|---|
| 1 | FY2028, 12 offices Consolidated | PPDO clicks **Download all** | 202 with the job, Queued. The page shows "Queued…" |
| 2 | Job Queued | Timer fires | Claimed → Running; progress "3 of 12" as it goes |
| 3 | Job Running | Same PPDO user (or another) clicks again | 200, the same job (decision 5) |
| 4 | All 12 built | Worker finishes | Done; bell "Your FY 2028 initial WFP export is ready (12 offices)"; **Download ZIP** |
| 5 | One office throws | Worker finishes | Done with problems; ZIP has 11 + README naming the 12th and the reason |
| 6 | No office Consolidated | Click | 409 "No office's FY {fy} AIP is marked as done yet." No job created |
| 7 | Worker crashes mid-job | 10 min later | Back to Queued (attempt 2); after 3 attempts Failed |
| 8 | 8 days later | Open the page | Job shows Expired; the file is gone; **Run again** |
| 9 | Download a Done job | Click | The ZIP streams from our API (`application/zip`) |
| 10 | Office user or a user without the report | POST or GET | 403 |
| 11 | Another PPDO user's job | GET it | Allowed: jobs are per fiscal year, shared by PPDO readers |
| 12 | FY ≤ 2027 | Click | 400 "Bulk export is for FY 2028 and later." (the entered-year rule, not a literal) |

## 4. API contract

All JWT-protected; the PPDO report-reader check on every call.

- `POST /api/budget-planning/wfp/report/bulk-exports` body `{ fiscalYear }` → `202 { job }` new, or
  `200 { job }` existing active; `400` FY ≤ 2027; `409` none eligible; `403`.
- `GET /api/budget-planning/wfp/report/bulk-exports?fiscalYear=` → the latest job for that year, or
  `204` when there is none (the page uses this on load).
- `GET /api/budget-planning/wfp/report/bulk-exports/{id}` → `{ job }`.
- `GET /api/budget-planning/wfp/report/bulk-exports/{id}/download` → the ZIP; `409` when not Done,
  `410` "This export has expired. Run it again." when expired.

`job` = `{ id, fiscalYear, status: "Queued"|"Running"|"Done"|"DoneWithProblems"|"Failed"|"Expired",
done, total, problems: [{ officeCode, reason }], message?, requestedByName, createdAt, finishedAt?,
expiresAt? }`.

## 5. Data model

Migration `AddInitialWfpExportJobs`. TPC: the base class has **no table of its own**; each concrete
job table carries the base columns plus its own.

**Base columns (`BackgroundJob`, in every job table):**

| Column | Type | Null | Notes |
|---|---|---|---|
| `id` | int | no | PK. ⚠️ TPC needs ids unique across job tables: use a shared sequence (`background_job_ids`), EF's TPC default |
| `status` | nvarchar(20) | no | as §4 |
| `requested_by_id` | uniqueidentifier | no | FK users |
| `created_at` | datetime2 | no | UTC |
| `claimed_at` | datetime2 | yes | |
| `heartbeat_at` | datetime2 | yes | progress refresh |
| `attempts` | int | no | default 0 (the retry count) |
| `message` | nvarchar(400) | yes | failure text |
| `finished_at` | datetime2 | yes | |

**`initial_wfp_export_jobs`** (base columns plus):

| Column | Type | Null | Notes |
|---|---|---|---|
| `fiscal_year` | int | no | |
| `done_count` / `total_count` | int | no | default 0 |
| `problems_json` | nvarchar(max) | yes | `[{ officeCode, reason }]` |
| `file_key` | nvarchar(400) | yes | the `IFileStore` key |
| `expires_at` | datetime2 | yes | |

Indexes on `initial_wfp_export_jobs`: `(fiscal_year, status)` for "active job for this year";
`(status, claimed_at)` for the claim and stuck-job queries.

Setting: `FileStore__Provider` = `Blob` | `Local`; `FileStore__BlobContainer` (Azure) or
`FileStore__LocalPath` (local). Local dev defaults to `Local` under a git-ignored folder.

## 6. UI states

On the Report page, initial WFP view, PPDO only:
- **Download all** button beside the office picker; hidden for office users.
- On load, `GET …?fiscalYear=` restores the latest job: Queued → "Queued…"; Running → progress bar
  "Building {done} of {total} offices…"; Done → **Download ZIP** + "Built {time}, expires {date}";
  Done with problems → the same plus "{n} office(s) skipped" with the list; Failed → message +
  **Try again**; Expired → "Expired — run it again" + **Download all**.
- Polls every 3 s only while Queued/Running and the tab is visible; stops otherwise.

## 7. Non-goals

- No push (SignalR/SSE/WebSockets). No Azure Storage Queues, Durable Functions or Hangfire.
- No bulk export for FY ≤ 2027, for office users, or of other report types (yet).
- No cancel button, no email.

## 8. Deployment notes

- Migration `AddInitialWfpExportJobs`, before the code; add to `Pre_Deployment_Checklist_v1.9.0.md`.
- App settings on the Function App: `FileStore__Provider=Blob`, `FileStore__BlobContainer`
  (create the container in `ppdoportalstorage`). Local: `Local` + a path in `local.settings.json`.
- The timer runs every 30 s on the Consumption plan: negligible cost, but it keeps the worker
  warm only while jobs exist (it returns immediately when there is nothing to do).

## 9. Ticket split

| Ticket | Scope | Who / model |
|---|---|---|
| T1 Plumbing | `BackgroundJob` base + `InitialWfpExportJob` (TPC) + migration; generic job repository (claim SQL, stuck-job and expiry queries) ; `IFileStore` + `LocalFileStore` + `BlobFileStore`; settings; POST/GET/download endpoints; TimerTrigger wrapper; interface `IExportJobWorker` with a stub | Claude, Opus (storage, concurrency SQL) |
| T2 Worker | `ExportJobWorker.RunOnceAsync`: cleanup, recover stuck, claim, build each office as the requester, zip + README, progress/heartbeat, Done/DoneWithProblems/Failed, bell notification; tests with fakes | Claude, Opus. ↩️ Ralph decided (2026-10-15) Claude codes it; he reads the worker closely in review |
| T3 UI | Download all, job states, polling | Claude, Sonnet |

T1 → T2 → T3. T2 depends only on T1's interfaces; it can be tested before T3 exists.

## 10. Acceptance checklist

- [ ] Download all as PPDO on FY2028: progress moves, ZIP downloads with one file per Consolidated office.
- [ ] Clicking again while it runs shows the same job.
- [ ] Force one office to fail (test hook or bad data): ZIP has the rest + README; status Done with problems.
- [ ] Kill the host mid-job: after 10 min it resumes; after 3 tries it fails cleanly.
- [ ] Bell entry on completion opens the page with the download.
- [ ] Office user: no button; API 403.
- [ ] After expiry (set `expires_at` in the past locally): Expired, file gone, download 410.

## 11. Test focus

- The claim: two concurrent claims → exactly one wins (real SQL Server test).
- Worker: per-office failure isolation; README content; progress counts; stuck-job recovery and
  the attempt cap; cleanup deletes the file and marks Expired.
- Endpoints: one active job per year; 403/409/410 cases.
