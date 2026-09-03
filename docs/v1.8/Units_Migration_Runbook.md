# PPDO-34 / V18-35 — AIP Units Migration Runbook

> The runbook for `20260903004121_MigrateAipAmountsToPesos`, the one migration in v1.8.0 that
> **rewrites existing values in place**. Called from `docs/v1.8/Pre_Deployment_Checklist.md` §1.
>
> Nothing here has been run against production. It was written and rehearsed against a local
> `PPDOPortalDev` on 2026-09-03, where all three fiscal years came out at exactly ×1000.

---

## 0. What this migration does, in one line

`aip_activities.ps / mooe / co / total / cc_adaptation / cc_mitigation` × 1000, every row, every
fiscal year. Storage moves from ₱000 to pesos. **Nothing a user sees should change**, because the
AIP detail page now divides at render (P2-a) and the six `×1000` sites that used to convert on the
fly are deleted.

That is the property every check below is testing: **the numbers on screen must not move.**

---

## 1. Why the baseline cannot be skipped

Run afterwards, `SUM(total) = 33,328,087,420` is unfalsifiable — it is equally consistent with a
correct migration and with one that ran twice. The before-value is the only thing that makes the
after-value checkable, and it stops existing the moment the migration runs.

⚠️ **Capture it in the same maintenance window as the migration, not days earlier.** A baseline
taken before someone edits an AIP no longer matches.

---

## 1a. Rehearse against a copy of production first — recommended

The risk in this migration is **correctness on real data**, not duration: ~2,500 rows update
instantly. A local database proves the SQL is valid; it cannot prove the result is right for
production's rows, because it does not have them. A rehearsal against a copy does, and it is where
an overflow, an unexpected NULL pattern or a row shape nobody anticipated shows up while it is
still free to be wrong.

**A rehearsal needs a database, not an environment.** Three options, cheapest first:

| Option | What it covers | Cost |
|---|---|---|
| **Local restore of a prod `.bacpac`** (recommended) | Everything — arithmetic *and* the §6 UI checks, against the app you already run locally | **$0** (42 MB export; egress is negligible) |
| **Azure DB copy** — `CREATE DATABASE … AS COPY OF`, migrate, run Script 3, drop | Arithmetic only. No app, no public surface | **~$0.16/day** at Basic ($4.90/mo prorated) |
| Full UAT environment (SWA + Functions + DB) | Same as local restore, but internet-reachable | ~$5/mo, **plus** the `robots.ts` blocker in RAL-221 |

⚠️ **Production data carries real personnel names and budget figures, and `obiken-01/ppdo-portal`
is a public repository.** RAL-221 refused a prod copy for the UAT instance on exactly this ground —
guide screenshots would have carried it. That objection still binds anything internet-reachable or
screenshotted. It does not bind a throwaway copy used to check sums, which is why the first two
options are preferred over standing up UAT for this.

A rehearsal does **not** replace §2's baseline capture: prod data moves between the rehearsal and
the cutover, so the baseline must still be taken in the real maintenance window.

---

## 2. Script 1 — BEFORE the migration

Self-checking version. It stores the baseline in a scratch table so Script 3 can compare
mechanically rather than by eye, which is where transcription errors live.

```sql
-- ── PPDO-34 Script 1: capture the baseline. Run immediately before the migration. ──
IF OBJECT_ID('dbo._ppdo34_baseline') IS NOT NULL DROP TABLE dbo._ppdo34_baseline;

SELECT r.fiscal_year,
       COUNT(a.id)                                        AS activities,
       SUM(a.total)                                       AS sum_total,
       SUM(a.ps)                                          AS sum_ps,
       SUM(a.mooe)                                        AS sum_mooe,
       SUM(a.co)                                          AS sum_co,
       SUM(a.cc_adaptation)                               AS sum_cc_adaptation,
       SUM(a.cc_mitigation)                               AS sum_cc_mitigation,
       SUM(CASE WHEN a.total IS NULL THEN 1 ELSE 0 END)   AS null_totals,
       SUM(CASE WHEN a.total = 0    THEN 1 ELSE 0 END)    AS zero_totals
INTO   dbo._ppdo34_baseline
FROM   aip_activities a
       JOIN aip_projects p ON p.id = a.project_id
       JOIN aip_programs g ON g.id = p.program_id
       JOIN aip_offices  o ON o.id = g.office_id
       JOIN aip_records  r ON r.id = o.aip_record_id
GROUP  BY r.fiscal_year;

SELECT * FROM dbo._ppdo34_baseline ORDER BY fiscal_year;   -- keep this output as well
```

**Also save the printed output**, not just the table. If the migration has to be rolled back the
scratch table may go with it.

---

## 3. Script 2 — BEFORE: pick the test subjects

These are the rows a human will eyeball in the UI. The script names them **and records what the
page displays today** — which, if the migration is right, is what it must still display after.

```sql
-- ── PPDO-34 Script 2: name the manual test subjects. Run before the migration. ──
-- PRE-MIGRATION: `total` is in THOUSANDS here, which is exactly what the page shows today.

-- A1 — largest, round number
SELECT TOP 2 'A1-largest' AS case_id, r.fiscal_year, a.id, a.ref_code, a.total AS shows_today
FROM aip_activities a
     JOIN aip_projects p ON p.id = a.project_id
     JOIN aip_programs g ON g.id = p.program_id
     JOIN aip_offices  o ON o.id = g.office_id
     JOIN aip_records  r ON r.id = o.aip_record_id
WHERE a.total IS NOT NULL ORDER BY a.total DESC;

-- A2 — ⚠️ THE IMPORTANT ONE: a value with centavos, which must survive the round trip
SELECT TOP 2 'A2-decimals' AS case_id, r.fiscal_year, a.id, a.ref_code, a.total AS shows_today
FROM aip_activities a
     JOIN aip_projects p ON p.id = a.project_id
     JOIN aip_programs g ON g.id = p.program_id
     JOIN aip_offices  o ON o.id = g.office_id
     JOIN aip_records  r ON r.id = o.aip_record_id
WHERE a.total IS NOT NULL AND a.total <> FLOOR(a.total) ORDER BY a.total DESC;

-- A3 — NULL total: must render as an em dash, never 0.00
SELECT TOP 2 'A3-null' AS case_id, r.fiscal_year, a.id, a.ref_code, NULL AS shows_today
FROM aip_activities a
     JOIN aip_projects p ON p.id = a.project_id
     JOIN aip_programs g ON g.id = p.program_id
     JOIN aip_offices  o ON o.id = g.office_id
     JOIN aip_records  r ON r.id = o.aip_record_id
WHERE a.total IS NULL;

-- C — the tightest WFP ceiling, for the live accept/reject pair.
--     PRE-MIGRATION the AIP budget in pesos is total * 1000; AFTER it is just total.
SELECT TOP 5 'C-ceiling' AS case_id, a.id, a.ref_code,
       a.total * 1000                  AS aip_budget_pesos,
       SUM(we.total_appropriation)     AS wfp_used_pesos,
       a.total * 1000 - SUM(we.total_appropriation) AS headroom_pesos
FROM   aip_activities a
       JOIN wfp_activities   wa ON wa.aip_activity_id = a.id
       JOIN wfp_expenditures we ON we.wfp_activity_id = wa.id
WHERE  a.total IS NOT NULL
GROUP  BY a.id, a.ref_code, a.total
HAVING a.total * 1000 - SUM(we.total_appropriation) > 0
ORDER  BY a.total * 1000 - SUM(we.total_appropriation) ASC;
```

Fill the table in §6 from this output before running the migration.

---

## 4. Apply

```bash
cd backend
dotnet ef database update --project PPDO.Infrastructure --startup-project PPDO.Functions \
  --connection "<Azure SQL connection string for ppdo-portal-db>"
```

Applies in timestamp order, so `AddAipExpenditures` creates its table first and
`MigrateAipAmountsToPesos` runs last.

---

## 5. Script 3 — AFTER the migration

```sql
-- ── PPDO-34 Script 3: verify. Every row must read PASS. ──
SELECT b.fiscal_year,
       b.sum_total                          AS before_total,
       n.sum_total                          AS after_total,
       CASE WHEN n.sum_total = b.sum_total * 1000
            THEN 'PASS' ELSE '*** FAIL ***' END AS ratio_total,
       CASE WHEN n.sum_ps            = b.sum_ps            * 1000
             AND n.sum_mooe          = b.sum_mooe          * 1000
             AND n.sum_co            = b.sum_co            * 1000
             AND n.sum_cc_adaptation = b.sum_cc_adaptation * 1000
             AND n.sum_cc_mitigation = b.sum_cc_mitigation * 1000
            THEN 'PASS' ELSE '*** FAIL ***' END AS ratio_components,
       CASE WHEN n.null_totals = b.null_totals AND n.zero_totals = b.zero_totals
            THEN 'PASS' ELSE '*** FAIL ***' END AS nulls_and_zeroes,
       b.activities AS before_rows, n.activities AS after_rows
FROM   dbo._ppdo34_baseline b
       JOIN (
         SELECT r.fiscal_year, COUNT(a.id) AS activities,
                SUM(a.total) AS sum_total, SUM(a.ps) AS sum_ps, SUM(a.mooe) AS sum_mooe,
                SUM(a.co) AS sum_co, SUM(a.cc_adaptation) AS sum_cc_adaptation,
                SUM(a.cc_mitigation) AS sum_cc_mitigation,
                SUM(CASE WHEN a.total IS NULL THEN 1 ELSE 0 END) AS null_totals,
                SUM(CASE WHEN a.total = 0    THEN 1 ELSE 0 END)  AS zero_totals
         FROM   aip_activities a
                JOIN aip_projects p ON p.id = a.project_id
                JOIN aip_programs g ON g.id = p.program_id
                JOIN aip_offices  o ON o.id = g.office_id
                JOIN aip_records  r ON r.id = o.aip_record_id
         GROUP  BY r.fiscal_year
       ) n ON n.fiscal_year = b.fiscal_year
ORDER  BY b.fiscal_year;
```

**A `FAIL` on any row stops the release.** Do not proceed to the UI checks to "see if it looks
fine" — the arithmetic is the authority, and a page that converts at render will look plausible
whatever the stored value is.

Drop the scratch table only once §6 is green too:

```sql
DROP TABLE dbo._ppdo34_baseline;
```

### Reading a failure

| Symptom | Likely cause |
|---|---|
| One year passes, another fails | The `UPDATE` hit a filtered subset — it should have no `WHERE` clause at all |
| Ratio is 1,000,000 | Migration applied twice. Restore; do not "divide it back" — see §8 |
| `sum_total` passes, `ratio_components` fails | A column was missed in the `UPDATE`; `total` no longer equals its own components |
| `nulls_and_zeroes` fails | NULLs were coalesced to 0. Restore — this is not reversible by arithmetic |
| Row counts differ | Something other than this migration changed data during the window |

---

## 6. Manual test cases — after the deploy

Fill the middle column from Script 2's output *before* the migration; the whole point is that it
does not change.

| # | Subject | Showed before | Must show after | Catches |
|---|---|---|---|---|
| A1 | ref code `__________` | `__________` | **identical** | ordinary round value |
| A2 | ref code `__________` | `__________` | **identical** | ⚠️ centavos surviving the divide |
| A3 | ref code `__________` | `—` | **`—`** | NULL must not become `0.00` |

⚠️ **A2 is the first thing to look at.** If it shows a number 1000× larger, the display half did
not deploy with the data half. If 1000× smaller, something divides twice.

### B — the round trip

The check that a display-only fix passes, and then corrupts the record on the next save.

1. Open A2's activity, click **Edit**. Inputs must be pre-filled in **₱000** — the same figures the
   read-only row showed, not peso figures.
2. Change nothing. **Save. Reload.** The value must be unchanged.
3. Type a known value (e.g. `250`) into PS, save, reload → row shows `250.00`, and the database
   holds `250000.00`.

Step 2 is the one that matters. A one-directional conversion passes every check in §6's table and
then divides the record by a thousand the first time anyone opens and saves it.

### C — the WFP ceiling, live

Using the tightest-headroom activity from Script 2, where headroom = `H`:

| # | Add a WFP expenditure of | Expected |
|---|---|---|
| C1 | exactly `H` | **Accepted** — lands on the ceiling |
| C2 | `H + 0.01` | **Rejected**, and the message quotes the AIP budget in full pesos |

The WFP page's "AIP Budget:" line for that activity must read the same figure it read before the
release. This is the live form of `AipWfpBoundaryTests`: a lost factor makes C1 reject, an inflated
one makes C2 accept.

### D — upload

Upload an FY2027 `.xlsm` into a **scratch** record. A source cell reading `250` must land as
`250000.00`. The province's workbook is denominated in ₱000 and `AipXlsmParser` converts on import
— this path was not in the ticket, and untested it writes thousands into a peso column.

### E — the dashboard, which changes on purpose

`BudgetPlanningDashboardService` computes `allocated - aip.Amount` — pesos minus thousands — and
surfaces it as a division's remaining figure. Nothing multiplied on that path, so **it has been
1000× wrong all along and this migration silently corrects it.**

So unlike everywhere else, the dashboard's AIP figures *are* expected to move. Note the
pre-migration values for one division before the deploy so the jump can be recognised as the fix
rather than reported as a regression.

---

## 7. Things that are not bugs

- **A genuine ₱0 renders as `—`**, indistinguishable from uncosted; `fmt` returns the em dash for
  both `null` and `0`. Pre-existing and unchanged here, but it gets more visible at **PPDO-37**,
  which makes `Total` derived and specifies `0, not null` for an activity with no expenditure rows.
  The 0/null distinction that spec draws will not be visible on screen. Decide it there.
- **Sub-₱10 amounts cannot be typed** on the AIP detail page: display is ₱000 at two decimals, so
  the smallest increment is ₱0.01 thousand = ₱10. Equally true before the migration. Storage can
  now hold ₱1, so a row written directly through the API at ₱5 would render `0.01` — not reachable
  through the UI or the parser.

---

## 8. Rollback

⚠️ **The migration's `Down` is not a general rollback.** It divides by 1000, exactly reversing the
multiply — but only while nothing has been written since. Once a user saves an AIP activity through
the migrated UI that row holds a genuine peso amount, and `Down` divides *that* by 1000 too.

| Situation | Action |
|---|---|
| Script 3 fails, no user traffic yet | `Down` is safe. Migration window only |
| Anything else — including "we found it the next day" | **Restore.** Azure SQL Basic keeps automatic point-in-time restore (7 days by default — confirm the retention in the portal before relying on it) |
| Applied twice (ratio 1,000,000) | Restore. Do **not** divide back: a second `UPDATE` cannot distinguish rows written after the first from rows written before |

---

## Appendix A — The local rehearsal, step by step

✅ **Chosen approach, 2026-09-03.** Restore a copy of production locally and run the whole migration
against it before the real one. Costs nothing and exercises production's actual rows through the
actual UI.

⚠️ **This puts real personnel names and budget figures on a laptop.** Keep the `.bacpac` outside the
repository — `*.bacpac` and `*.bak` are git-ignored as a safety net, not as the plan — and complete
the teardown in step 6. Do not screenshot the rehearsal database into any document that leaves the
machine.

### Who does which half

The export needs the production SQL credential, so it is a manual step. Everything after the
`.bacpac` exists on disk uses Windows auth and can be scripted or handed to Claude.

| Step | Who |
|---|---|
| 1 — export from Azure | **Ralph** (needs the prod credential) |
| 2–5 — import, migrate, verify, UI checks | scriptable / Claude |
| 6 — teardown | either |

### 1. Export production

Easiest with SSMS, which is already installed: connect to `ppdo-portal-server`, right-click
`ppdo-portal-db` → **Tasks → Export Data-tier Application…** → save to e.g.
`D:\RalphFiles\PPDO\_rehearsal\ppdo-prod-YYYYMMDD.bacpac`. **Outside the repo.**

Scriptable alternative — `dotnet tool install -g microsoft.sqlpackage`, then:

```bash
sqlpackage /Action:Export \
  /SourceServerName:"ppdo-portal-server.database.windows.net" \
  /SourceDatabaseName:"ppdo-portal-db" \
  /SourceUser:"<admin>" /SourcePassword:"<password>" \
  /TargetFile:"D:\RalphFiles\PPDO\_rehearsal\ppdo-prod-YYYYMMDD.bacpac"
```

Export takes a transactionally consistent snapshot; it does not lock or modify production. Your
laptop's IP must be on the Azure SQL firewall.

### 2. Import as a SEPARATE local database

⚠️ **Do not import over `PPDOPortalDev`.** That is your working dev database, and it is already
migrated. The rehearsal needs its own:

```bash
sqlpackage /Action:Import \
  /TargetServerName:".\SQLEXPRESS" /TargetDatabaseName:"PPDOPortalUat" \
  /TargetTrustServerCertificate:True \
  /SourceFile:"D:\RalphFiles\PPDO\_rehearsal\ppdo-prod-YYYYMMDD.bacpac"
```

Or SSMS → right-click **Databases → Import Data-tier Application…**, target name `PPDOPortalUat`.

### 3. Capture the baseline and the test subjects

Run **Script 1** (§2) and **Script 2** (§3) against `PPDOPortalUat`. Fill in §6's table from
Script 2's output — those are the values that must not change.

### 4. Apply every pending migration

The restored copy is at production's schema, so this rehearses **all 13** of v1.8.0's migrations in
order, not just this one:

```bash
cd backend
dotnet ef database update --project PPDO.Infrastructure --startup-project PPDO.Functions \
  --connection "Server=.\SQLEXPRESS;Database=PPDOPortalUat;Trusted_Connection=True;TrustServerCertificate=True;"
```

### 5. Verify

Run **Script 3** (§5) — every row must read PASS. Then point the Functions app at the rehearsal
database (`SqlConnectionString` in `backend/PPDO.Functions/local.settings.json`, temporarily) and
work through §6's UI checks against real data.

⚠️ **Put `local.settings.json` back to `PPDOPortalDev` afterwards.** It is git-ignored, so nothing
catches this for you.

### 6. Teardown

```sql
DROP DATABASE PPDOPortalUat;
```

Then delete the `.bacpac` and the `_rehearsal` folder. The data has no reason to outlive the check.

### What a green rehearsal does and does not license

It says the migration is correct for production's data **as of the export**. It does not replace
§2's baseline capture in the real maintenance window, because prod moves in between — and the
baseline is the only thing that makes the post-migration number checkable.

---

*Written 2026-09-03 alongside PPDO-34. Rehearsed on a local development database; the production-copy
rehearsal in Appendix A is agreed and not yet run. Nothing here has been run against production.*
