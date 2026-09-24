# v1.8.0 — Pre-Deployment to Production Checklist

> Work through this **before** merging `release/1.8.0` → `main`. Pushing to `main` auto-deploys
> both frontend and backend; **CI does not run EF migrations**, so the database steps below are
> manual and must happen in the order given.
>
> Companion to the Post-Deployment Checklist in `PROJECT_DOCUMENTATION_NET_AZURE.md`. That one
> confirms the environment stood up; this one is about not losing data on the way in.

---

## 1. Database — the one irreversible part

v1.8.0 carries migrations that production has never seen — **24 as of 2026-09-24** (PPDO-137 added
one after the release was called complete). The count has drifted three times now (13 → 15 → 23 → 24), so **recheck it rather than trusting any
number written here**:

```bash
git diff --name-only main release/1.8.0 -- backend/PPDO.Infrastructure/Data/Migrations \
  | grep -v Designer | grep -v ModelSnapshot
```

All of them are additive (new tables, columns, permission flags) except one:

| Order | Migration | Kind |
|---|---|---|
| 1–12 | `20260824030231_AddLandingPage` … `20260902131412_AddAipExpenditures` | Schema, additive |
| **13** | **`20260903004121_MigrateAipAmountsToPesos`** | ⚠️ **Rewrites existing values in place** |
| 14 | `20260903023255_AddAipOfficeOwnershipFk` (V18-32 / PPDO-33) | Schema, additive — **plus a backfill** |
| 15–23 | `20260907020223_AddAipDivisionAllocationLedger` … `20260920234022_AddFundingSourceOfficeId` | Schema, additive |
| 24 | `20260924064806_WidenClimateChangeTypologyName` (PPDO-137) | Schema — widens `climate_change_typologies.name` 200 → 500. Up is lossless; **Down fails** once any name exceeds 200 |

↩️ **`20260903045149_AddAipRecordOwningOffice` was listed here as #15 and no longer exists.** PPDO-61
reversed the office-owned record shape and **dropped** the migration rather than reversing it,
because it had never run against production. Nothing to do at release — noted because a reader
comparing this table against a database will otherwise go looking for it. (A local dev database that
predates PPDO-61 *does* carry the row and an orphaned `aip_records.office_id` column; production
does not, and that asymmetry is correct.)

⚠️ **#14 writes data, and that is still additive.** It adds `aip_offices.office_id` and fills it
from the ref-code suffix, so every row it touches is a column it created in the same migration.
It cannot alter a pre-existing value. What it *can* do is leave rows unmatched — see the backfill
step below, which is the one that matters.

`MigrateAipAmountsToPesos` multiplies six money columns on every `aip_activities` row, every
fiscal year, by 1000 (V18-35 / PPDO-34). It is the only migration in the release that can destroy
data rather than add to it.

> 📖 **`docs/v1.8/Units_Migration_Runbook.md` is the step-by-step**: the before/after SQL, the
> manual test cases, how to read a failure, and when `Down` is and is not a valid rollback. The
> summary below is the checklist; the runbook is what you actually work from on the day.

- [x] **Rehearse against a copy of production first** (runbook §1a). A local restore of a prod
      `.bacpac` costs nothing and exercises the real rows through the real UI.

      ✅ **Done 2026-09-21.** Ralph restored production into a local `ppdo-portal-db-prod` and ran
      all 23 migrations against it successfully. That database is now the **post-migration** state of
      real production data, which makes the ratio check below a straight comparison rather than a
      leap of faith — see the recorded numbers under each step.

      ⚠️ The older `PPDOPortalDev` is NOT the rehearsal database. It predates PPDO-61 and carries
      the dropped `AddAipRecordOwningOffice` row plus an orphaned `aip_records.office_id`. Read the
      numbers from `ppdo-portal-db-prod`.

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

      **Rehearsal reference, measured 2026-09-21** against `ppdo-portal-db-prod` *after* the
      migrations ran. Production, still unmigrated, must therefore read **1/1000 of this** when the
      baseline is captured — if it does not, stop and work out why before applying anything:

      | FY | activities | `SUM(total)` AFTER | so prod BEFORE must read |
      |---|---|---|---|
      | 2027 | 2,304 | `32,562,217,860.00` | `32,562,217.86` |

      One fiscal year only — FY2028 has no production rows yet.

- [ ] **Confirm a restore path exists before running it.** Azure SQL Basic keeps automatic
      point-in-time restore (7 days by default — confirm the retention in the portal rather than
      assuming it). If PITR is not confirmed, take an export/copy first. Do not rely on the
      migration's own `Down`: see the note below.

- [ ] **Apply the migrations** — `dotnet ef database update` against Azure SQL, with
      `SqlConnectionString` pointing at `ppdo-portal-db`. One command applies all 15, in timestamp
      order.

      ⚠️ **The units migration is #13 of 24, not last** — an earlier draft of this checklist said
      it ran last, and it does not. **Eleven** schema migrations sort after it (the draft said two;
      #24, PPDO-137, re-checked 2026-09-24 — it alters only `climate_change_typologies`).
      That is **safe, and worth understanding rather than working around**: none of the ten touches
      `aip_activities` at all. Verified 2026-09-21 by grepping every one of them for
      `table: "aip_activities"` (zero hits) and for raw `Sql()` calls — there is exactly one, in #14,
      and it writes `aip_offices.office_id`, a column that same migration creates. So the ratio check
      below is still valid run at the end.

      **Do not reorder them by hand** — renaming migrations to force the units one last would break
      the applied-migrations history for no gain. What does matter is the order already guaranteed:
      `AddAipExpenditures` (#12) creates its table before #13 runs.

      ↩️ **Re-verify this if more migrations land before release.** The claim is "nothing after #13
      writes `aip_activities`", and it is only as current as the last time someone checked:

      ```bash
      cd backend/PPDO.Infrastructure/Data/Migrations
      grep -l 'table: "aip_activities"' 2026090[4-9]* 202609[1-9]* 2026[1-9]*  # expect: no output
      grep -c 'Sql(' 2026090[4-9]* 202609[1-9]* 2026[1-9]*                    # expect: all zero
      ```

- [ ] **Re-run the baseline query and check the ratio.** Every fiscal year's `SUM(total)` must be
      **exactly** its before-value × 1000. Not approximately — exactly. A year that is off by any
      other factor means the migration hit rows it should not have, or ran twice.

- [ ] **Spot-check that NULLs survived.** `SUM(CASE WHEN total IS NULL THEN 1 ELSE 0 END)` per
      year must be unchanged. An uncosted activity has no amount; it must not have become 0, which
      would read as "costed at nothing" in the dashboard's costed counts.

      **Rehearsal reference (post-migration, 2026-09-21):** FY2027 — 2,304 activities, **21 NULL**
      totals, **4 genuine zeros**. Capture the same two counts from production *before* applying;
      21 and 4 must come back unchanged afterwards. The zeros matter as much as the NULLs here:
      they are what a NULL would have turned into, so a run that produced 25 zeros and 0 NULLs would
      still pass a naive "is anything NULL" check.

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

      ✅ **Measured against the production copy, 2026-09-21: 37 `aip_offices` rows, 36 matched,
      1 unmatched — the same `3000-000-1-01-004`.** So this is production's row, not a local
      artefact. Detail:

      | | |
      |---|---|
      | ref code | `3000-000-1-01-004` (SOCIAL sector, blank name) |
      | holds | 1 program, 1 project, **0 activities, ₱0** |
      | why unmatched | no configured office carries the suffix `01-004` — the `offices` table jumps `01-003` (SPO) → `01-005` (PTO); `01-002` is absent too |

      **Impact today is nil and it is still worth resolving.** The row carries no money and no
      activities, so nothing is hidden from anyone right now — but it is permanently invisible to
      every scoped read, so if someone later encodes into it, that work silently disappears.
      Two clean options, Ralph's call:

      1. **Create/assign** the office that `01-004` is meant to be, and set `office_id` on the row.
      2. **Delete the empty shell** — it has one program and one project, both empty, and no
         activities. Cheapest, and reversible from the same backup the units migration needs anyway.

      ⚠️ Do this *after* the migrations and *before* announcing the release, per the step above.

> ⚠️ **`Down` is not a general rollback.** It divides by 1000, which exactly reverses the multiply
> — but only while nothing has been written since. The moment a user saves an AIP activity through
> the migrated UI, that row holds a genuine peso amount, and rolling back divides *that* by 1000
> too. `Down` covers "I applied it and the sums came out wrong"; it does not cover "we found a
> problem on Thursday." That is what the restore path is for.

---

## 2. Application

- [x] ✅ **`APP_VERSION` reads `v1.8.0`.** ↩️ **There is no longer anything to keep in sync**: the
      constant moved to a single `frontend/src/lib/version.ts`, which `Sidebar.tsx`, `Footer.tsx`
      and `login/page.tsx` all import. Verified 2026-09-21 — one definition, three importers, no
      hardcoded version strings left outside comments. The old "check all three places" instruction
      is kept here only so a reader knows the drift it guarded against was fixed, not forgotten.

- [x] ✅ **CLAUDE.md's Implementation Status section and its footer date stamp updated for v1.8.0**
      (2026-09-21). Moved off v1.7.4 / 2026-08-27: a v1.8.0 row in the release-history table, the
      milestone marked complete-awaiting-merge, and the "Next: v1.8.0 — AIP Redesign (in planning)"
      section replaced with what actually shipped — including a section on what was deliberately
      **not** built, so Phases 6 and 7 are not mistaken for oversights.

- [x] ✅ **`dotnet test` green on the release branch** — **2,381 passed, 0 failed**, run 2026-09-21
      on `release/1.8.0` at `279bd64` (the PPDO-109 merge). The suite is the safety net that makes a
      change this size tractable (`docs/v1.8/RETROSPECTIVE.md`); it has grown from the 1,061 tests
      that retrospective was written against.

- [ ] Azure Functions **CORS** on `ppdo-portal-api-sea` still lists the SWA origin. Configured in
      the portal, not `host.json`. (Portal-only — cannot be checked from the repo.)

- [ ] **Confirm the release's own migration applies cleanly to Azure SQL**, specifically
      `20260920234022_AddFundingSourceOfficeId` (PPDO-109, the last one in). It is additive with no
      backfill, so it changes no behaviour on its own — but the new code reads
      `funding_sources.office_id` unconditionally, so **every funding-source read fails if the code
      deploys before the migration runs**. Migration first, then merge to `main`.

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
