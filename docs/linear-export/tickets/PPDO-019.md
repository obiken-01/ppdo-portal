---
id: PPDO-19
title: 'CSV import/export for the climate-change typology and eSRE config pages'
status: Done
project: PPDO Portal
labels: 
milestone: v1.8.0 — Office Users, AIP Redesign & Reviewer Flow
created: 2026-09-01
completed: 2026-09-02
parent: PPDO-28
blocked_by: (none)
related: PPDO-6, PPDO-8, PPDO-23, PPDO-25
url: https://linear.app/ralphoksiprojects/issue/PPDO-19
uuid: e4cdcc6f-2f8c-4d61-b101-e39496ef8cd6
---

# PPDO-19 — CSV import/export for the climate-change typology and eSRE config pages

## Problem

[RAL-247](https://linear.app/ralphoksiprojects/issue/RAL-247/config-climate-change-typology-table-management-page) and [RAL-248](https://linear.app/ralphoksiprojects/issue/RAL-248/config-esre-code-table-management-page) shipped both config pages with CRUD only. Every other config page — accounts, offices, funding sources, divisions, price index — carries CSV export and upsert-import, and for funding sources the CSV upload **is** the seeding path. These two are the odd ones out.

**Not a** `V18-` **item.** New scope, filed 2026-09-01 rather than absorbed silently into a shipped ticket.

## Scope

Per page: two service methods (`ExportCsvAsync`, `ImportCsvAsync`), two endpoints (`GET` and `POST` on `/csv`), and `CsvDownloadButton` + `CsvUploadButton` + the `MessageDialog` import summary in the page header.

The plumbing already exists and should be reused, not re-derived:

* `PPDO.Application/Common/Csv.cs` — `Parse`, `Write`, `ParseBool`
* `PPDO.Application/Common/CsvImportResult.cs` — `(New, Updated, Skipped, Errors)`
* `ConfigHttp.CsvFileAsync` for the download response
* `FundingSourceService.ImportCsvAsync` — the template; upsert keyed on `code`

Columns follow each table: `code,name,description,is_active` for eSRE, plus `category` for typologies.

## ⚠️ Three things to get right

### 1. Audit every changed record — this is the reason to take the ticket seriously

[RAL-246](https://linear.app/ralphoksiprojects/issue/RAL-246/audit-log-coverage-for-permission-role-office-and-division-changes) found `DivisionService.ImportCsvAsync` upserting every division's `Can*` flags with **no audit at all** — the widest "who can do what" write in the system, and it was invisible. A re-upload could have granted Budget Planning, Inventory or Config to every division at once with nothing in the log.

Do not reintroduce that shape:

* one audit row per record the import **actually changed**, old → new
* **nothing** for skipped rows — an import that changed nothing must not look like it did, and audit noise is what makes a real change hard to spot
* emit rows **after **`SaveChangesAsync`: a newly created row has no Id before then, and an audit row keyed on `0` is worse than none

### 2. Writes go through `AuthorizeWriteAsync`

Both CSV endpoints are writes, so they need [RAL-256](https://linear.app/ralphoksiprojects/issue/RAL-256/denyreviewerwrite-guard-the-first-subtractive-permission)'s reviewer write-denial guard like every other mutation on these pages. Export is a read but stays on `CanManageConfig` — it must **not** inherit the list endpoint's `Authenticated` gate, which is deliberately broad because the AIP picker needs it.

### 3. eSRE is a closed vocabulary — settle this before coding

The eSRE codes are a fixed four (SS / ES / ID / EN). An import that lets someone add a fifth is a product question, not an obvious yes.

⚠️ Whatever is decided, an import must **not** resurrect `PPDO/PEO` — the implementing-office name typed into the eSRE column of one FY2027 row, deliberately left orphaned by [RAL-248](https://linear.app/ralphoksiprojects/issue/RAL-248/config-esre-code-table-management-page)'s seed so the Phase 2 backfill flags it. Re-importing an export taken from a database that still holds it would do exactly that.

Climate-change typologies have the opposite property — an open-ended list derived from the imported data — so import is uncontroversial there.

## Done when

Both pages export a CSV that re-imports cleanly as a no-op; a changed row is upserted and audited; a skipped row produces no audit entry; and the reviewer write-denial guard covers both new endpoints.
