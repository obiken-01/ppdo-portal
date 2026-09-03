# v1.8.0 — Pre-Deployment to Production Checklist

> Work through this **before** merging `release/1.8.0` → `main`. Pushing to `main` auto-deploys
> both frontend and backend; **CI does not run EF migrations**, so the database steps below are
> manual and must happen in the order given.
>
> Companion to the Post-Deployment Checklist in `PROJECT_DOCUMENTATION_NET_AZURE.md`. That one
> confirms the environment stood up; this one is about not losing data on the way in.

---

## 1. Database — the one irreversible part

v1.8.0 carries migrations that production has never seen — **13 as of the end of Phase 2**, and the
count grows as later phases land. Recheck it at release time with:

```bash
git diff --name-only main release/1.8.0 -- backend/PPDO.Infrastructure/Data/Migrations | grep -v Designer
```

All of them so far are additive (new tables, columns, permission flags) except one:

| Migration | Kind |
|---|---|
| `20260824030231_AddLandingPage` … `20260902131412_AddAipExpenditures` (12) | Schema, additive |
| **`20260903004121_MigrateAipAmountsToPesos`** | ⚠️ **Rewrites existing values in place** |

`MigrateAipAmountsToPesos` multiplies six money columns on every `aip_activities` row, every
fiscal year, by 1000 (V18-35 / PPDO-34). It is the only migration in the release that can destroy
data rather than add to it.

> 📖 **`docs/v1.8/Units_Migration_Runbook.md` is the step-by-step**: the before/after SQL, the
> manual test cases, how to read a failure, and when `Down` is and is not a valid rollback. The
> summary below is the checklist; the runbook is what you actually work from on the day.

- [ ] **Rehearse against a copy of production first** (runbook §1a). A local restore of a prod
      `.bacpac` costs nothing and exercises the real rows through the real UI.

- [ ] **Capture the baseline sums first.** Non-negotiable — run this against production and keep
      the output. Applied without a baseline, the question "is this number right?" has no answer
      afterwards, because the correct value and a 1000×-wrong value look equally plausible.

      ```sql
      SELECT r.fiscal_year, COUNT(a.id) AS activities, SUM(a.total) AS sum_total
      FROM   aip_activities a
             JOIN aip_projects p ON p.id = a.project_id
             JOIN aip_programs g ON g.id = p.program_id
             JOIN aip_offices  o ON o.id = g.office_id
             JOIN aip_records  r ON r.id = o.aip_record_id
      GROUP  BY r.fiscal_year
      ORDER  BY r.fiscal_year;
      ```

- [ ] **Confirm a restore path exists before running it.** Azure SQL Basic keeps automatic
      point-in-time restore (7 days by default — confirm the retention in the portal rather than
      assuming it). If PITR is not confirmed, take an export/copy first. Do not rely on the
      migration's own `Down`: see the note below.

- [ ] **Apply the migrations** — `dotnet ef database update` against Azure SQL, with
      `SqlConnectionString` pointing at `ppdo-portal-db`. They apply in timestamp order, so the
      units migration runs last, after `AddAipExpenditures` has created its table.

- [ ] **Re-run the baseline query and check the ratio.** Every fiscal year's `SUM(total)` must be
      **exactly** its before-value × 1000. Not approximately — exactly. A year that is off by any
      other factor means the migration hit rows it should not have, or ran twice.

- [ ] **Spot-check that NULLs survived.** `SUM(CASE WHEN total IS NULL THEN 1 ELSE 0 END)` per
      year must be unchanged. An uncosted activity has no amount; it must not have become 0, which
      would read as "costed at nothing" in the dashboard's costed counts.

- [ ] **Record the AIP ownership backfill's unmatched count** (V18-32 / PPDO-33). That migration is
      additive — it adds `aip_offices.office_id` and fills it from the ref-code suffix — so it
      cannot destroy anything. What it can do is leave rows unmatched, and **an unmatched row is
      invisible to every scoped read**: the office simply sees no AIP, with no error anywhere. Run
      after applying, and resolve anything it returns before announcing the release:

      ```sql
      SELECT r.fiscal_year,
             COUNT(*)                                                  AS aip_offices,
             SUM(CASE WHEN a.office_id IS NULL THEN 1 ELSE 0 END)      AS unmatched
      FROM   aip_offices a JOIN aip_records r ON r.id = a.aip_record_id
      GROUP  BY r.fiscal_year ORDER BY r.fiscal_year;

      SELECT a.ref_code, a.name FROM aip_offices a WHERE a.office_id IS NULL;
      ```

      Local rehearsal: 56 rows, 55 matched, 1 unmatched (`3000-000-1-01-004` — no configured office
      carries `01-004`). Production will differ; a non-zero count is expected and fine, but it must
      be *seen*.

> ⚠️ **`Down` is not a general rollback.** It divides by 1000, which exactly reverses the multiply
> — but only while nothing has been written since. The moment a user saves an AIP activity through
> the migrated UI, that row holds a genuine peso amount, and rolling back divides *that* by 1000
> too. `Down` covers "I applied it and the sums came out wrong"; it does not cover "we found a
> problem on Thursday." That is what the restore path is for.

---

## 2. Application

- [ ] `APP_VERSION` reads `v1.8.0` in all three places — `components/layout/Sidebar.tsx`,
      `components/landing/Footer.tsx`, `app/(public)/login/page.tsx`. They have drifted apart
      before.
- [ ] CLAUDE.md's **Implementation Status** section and its footer date stamp updated for v1.8.0.
- [ ] `dotnet test` green on the release branch — the suite is the safety net that makes a change
      this size tractable (`docs/v1.8/RETROSPECTIVE.md`).
- [ ] Azure Functions **CORS** on `ppdo-portal-api-sea` still lists the SWA origin. Configured in
      the portal, not `host.json`.

---

## 3. After the deploy

- [ ] Open an FY2027 AIP office and confirm figures under `Amount (in ₱000)` read the same as they
      did before the release. Storage moved to pesos; the page converts at its edge (P2-a), so a
      **visible** change here means the display half did not ship with the data half.
- [ ] Save one AIP activity edit and reload it. The value must come back unchanged — this is the
      check that catches a one-directional conversion, which silently divides the record by a
      thousand on every subsequent edit.
- [ ] Upload an FY2027 `.xlsm` into a scratch record and confirm the amounts land as pesos. The
      province's workbook is denominated in ₱000 and `AipXlsmParser` converts on import.

---

*Created 2026-09-03 alongside PPDO-34 (V18-35), the release's only data-rewriting migration.*
