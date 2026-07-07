# RAL-XXX — LDIP File Upload + Activity-Level Fields (mirror AIP upload)

_Findings + implementation prompt drafted 2026-07-03. **Linear is currently disconnected in this
session — create the actual ticket from this doc once reconnected**, then delete/replace this file
name with the real ticket number (matches the "extract upfront" convention used for
RAL-61_LDIP_Entry_Form_Penpot_Findings.md)._

**Suggested title:** LDIP file upload + activity-level fields (mirror AIP upload)
**Suggested milestone:** v1.3 — Budget Planning Completion
**Suggested priority:** High (same tier as RAL-61/62 — LDIP/AIP entry completeness)

---

## 0. Why this exists

PPDO confirmed a requirement missed during RAL-61: LDIP, like AIP, needs a **file upload** path, not
just manual program-name+budget entry. Reference file: `LDIP 2027-2029 formatted.xlsx` (Occidental
Mindoro, sheets `General` / `Social` / `Economic` / `Others` / `Program Description Form` / `AIP 2027`).

## 1. Finding — the shipped RAL-61 schema is missing most of the real LDIP's columns

`docs/v1.3/RAL-61_LDIP_Entry_Form_Design.md` §1 explicitly says:

> Dropped from the original ticket scope (deliberately): PS/MOOE/CO split, funding-source FK,
> expected outputs, implementing office, CC adaptation/mitigation/typology, PDP/RDP/SDG/Sendai
> tags — none of these are in the confirmed RAP-01 design.

Today `ldip_programs` only has `ref_code`, `name`, `budget` (one lump ₱000 sum for the whole
document period). The actual source file carries all of that dropped detail **at the activity
level**, one row per activity, exactly like `aip_activities`.

### Column map of `LDIP 2027-2029 formatted.xlsx` (rows 8–9 headers, sheets General/Social/Economic/Others — all four are structurally identical)

| # | Header | Notes |
|---|---|---|
| 1 | AIP Reference Code | Program rows: 6 segments e.g. `1000-000-1-01-001`. Activity rows: 7 segments e.g. `1000-000-1-01-001-001` (program ref + `-NNN`) |
| 2 | Program/Project/Activity Description | Program rows hold the program name; activity rows hold the activity name |
| 3 | Implementing Office/Department | blank on program rows, populated on activity rows |
| 4 | Start Date | activity rows only — **stored as a bare year number (e.g. `2026`), not a month name** (AIP's `AipActivity.StartDate` stores month names like `"January"` — this is a real format difference, not a bug) |
| 5 | Completion Date | same as above (bare year, e.g. `2029`) |
| 6 | Expected Outputs | activity rows only, free text |
| 7 | Funding Source | activity rows only (e.g. "General Fund") |
| 8 | PS (Personal Services) | amount, ₱000 |
| 9 | MOOE | amount, ₱000 |
| 10 | CO (Capital Outlay) | amount, ₱000 |
| 11 | Total (=8+9+10) | program rows show the rollup of their activities; activity rows show their own total |
| 12 | CC Adaptation amount | ₱000 |
| 13 | CC Mitigation amount | ₱000 |
| 14 | CC Typology Code | text |
| 15 | PDP, RDP | text — **no AIP equivalent** |
| 16 | SDGs | text — **no AIP equivalent** |
| 17 | Sendai Framework | text — **no AIP equivalent** |
| 18 | NDRRM Plan | text — **no AIP equivalent** |
| 19 | NSP | text — **no AIP equivalent** |
| 20 | PDPDFP | text — **no AIP equivalent** |

Columns 1–14 map almost 1:1 to `AipActivity` (`backend/PPDO.Domain/Entities/AipActivity.cs`) minus
`EsreCode` (not present in the LDIP file). Columns 15–20 are six new alignment/tagging columns that
don't exist anywhere in the AIP schema — they're LDIP-specific.

### Hierarchy is 3 levels, not 4

LDIP is Office (sector group) → Program → **Activity** directly — there is **no Project level**
(AIP is Office → Program → Project → Activity). This actually simplifies the parser/model versus
AIP: one child table (`ldip_activities` under `ldip_programs`), not two.

### A 6th sheet exists and is explicitly out of scope for this ticket

`Program Description Form` is a separate one-row-per-program narrative form: Department, Program
Title, Program Description, Identified Mandates/Legal Basis, Current Situation/Baseline Scenario,
Sector Classification, Timeframe, Program Impact, Target Beneficiaries/Coverage, Implementing
Division/Section/Units, Partner Agency/Institution, Program Indicators, Estimated Total Cost.
**Do not build this now** — flag it as an open question for PPDO (possibly a separate ticket; it
reads like program-level metadata that could live as nullable text columns on `ldip_programs`
later, additive, same pattern as the RAL-61 "additive columns" note).

## 2. Finding — the AIP upload pattern to mirror (already shipped, RAL-64/76)

Upload → parse-only preview → client stash → confirm → persist. No DB writes happen until Confirm.

- **Parser contract**: `backend/PPDO.Application/Services/IAipXlsmParser.cs` — `Parse(Stream) →
  Dictionary<string sector, List<ParsedAipOffice>>`, POCOs `ParsedAipOffice/Program/Project/Activity`
  (no IDs, in-memory only). Implementation `backend/PPDO.Infrastructure/Services/AipXlsmParser.cs`
  uses ClosedXML, detects sheets named `GENERAL_*`/`SOCIAL_*`/`ECONOMIC_*`/`OTHERS_*`.
  ⚠️ **The LDIP sample file uses plain sheet names** (`General`, `Social`, `Economic`, `Others`, no
  prefix/suffix) — confirm the real upload files PPDO will submit use the same plain names, and
  write the LDIP parser's sheet-matching rule accordingly; don't assume the AIP `*_` convention.
- **Upload endpoint**: `backend/PPDO.Functions/Functions/AipFunctions.cs` — `POST
  /api/budget-planning/aip/upload?fiscalYear=`, raw `application/octet-stream` body, gated by
  `CanUploadAip` (PPDO-only per the access model), buffers `req.Body` into a `MemoryStream` first
  (Kestrel/isolated-worker disallows sync reads that ClosedXML needs), loads active `FundingSource`s
  so the parser can flag unmatched funding-source text, calls `_aip.ParsePreviewAsync(...)` — pure
  parse, **no persistence**.
- **Confirm endpoint**: `POST /api/budget-planning/aip/confirm` — stateless; client echoes back the
  full parsed hierarchy it got from the preview response (`AipImportConfirmDto`); `CanUploadAip`
  gated; persists as a Draft record.
- **Frontend flow**:
  - `frontend/src/app/(portal)/budget-planning/aip/new/page.tsx` — Upload File / Manual Entry tabs
    (Manual Entry is `disabled` with "coming soon" — AIP itself never built manual entry past
    program stub), fiscal-year select, dropzone (drag/drop + browse, `.xlsm` extension + 20 MB
    client-side validation), "How it works" + "File Requirements" help panels. On upload success,
    stashes the preview response + `{originalFilename, ldipId}` meta in `sessionStorage` and routes
    to `import-preview`.
  - `frontend/src/app/(portal)/budget-planning/aip/import-preview/page.tsx` — reads the
    sessionStorage stash (redirects back to `new` if missing/malformed), shows stat tiles
    (offices/programs/projects/activities counts), a per-sector breakdown table, file info, and a
    collapsible warnings panel (e.g. unmatched funding source codes). Confirm button POSTs to
    `/confirm`, clears sessionStorage, toasts, routes to the list page. Cancel just clears + routes
    back.
  - `frontend/src/lib/aip.ts` — `uploadAipFile(file, fiscalYear)`, `confirmAipImport(body)`, plus
    the shared `{data,error,message}` envelope unwrap + `aipErrorMessage` helper.

### The "use Edit to view what was uploaded" requirement

Confirmed feasible with the existing routing: `frontend/src/app/(portal)/budget-planning/ldip/edit/page.tsx`
already loads by `?id=` (query-param route, not a dynamic segment — required because the app builds
with `output: "export"`) and renders the shared `LdipForm` pre-populated, read-only once the record
is Final. The only gap is that `LdipForm`'s "Created Programs" table currently shows **program**
rows only (ref code / name / budget) — it needs an expandable/nested **activity** sub-table under
each program (read-only rows: implementing office, dates, outputs, funding source, PS/MOOE/CO,
CC amounts + typology, and the 6 alignment columns) so an uploaded record is actually inspectable,
not just its program totals.

## 3. Design decisions to confirm with PPDO before building (open items)

1. **Does the manual "+ Add Program" flow also need activity-level fields**, or does upload become
   the only path to activity detail (mirrors AIP, whose own Manual Entry tab is still disabled)?
   Recommendation: **keep manual entry at Program-level only** (current behavior unchanged) and
   make activity detail an upload-only feature — avoids building a huge manual multi-field-per-row
   form for a case PPDO hasn't asked for yet, matches AIP's own precedent.
2. **Permission gate**: reuse the existing `CanUploadAip` flag (PPDO-only) for LDIP upload too, or
   add a parallel `CanUploadLdip`? Recommendation: reuse `CanUploadAip` — same PPDO uploader role,
   avoids a 4th permission flag; rename its meaning informally to "can upload planning documents" in
   comments only, no schema change needed.
3. **`ldip_programs.Budget` today is a manually-entered lump sum.** Once activities exist, should it
   become a computed rollup (`SUM(activities.Total)`) for uploaded records while staying
   manually-entered for Program-only manual records? Recommendation: yes — keep the column, but
   populate it as the activity sum on import; leave manual-entry behavior unchanged.
4. **`Program Description Form` sheet** — separate ticket or additive nullable columns later?
   Recommendation: punt, log as a follow-up question, don't scope into this ticket.

## 4. Implementation prompt (paste into the Linear ticket once created)

```
Read CLAUDE.md, PROJECT_DOCUMENTATION_NET_AZURE.md, and PPDO_PROJECT_CONTEXT.md.
Read docs/v1.3/RAL-61_LDIP_Entry_Form_Design.md FULLY (current LDIP schema/form — this ticket
extends it) and docs/v1.1/AIP_WFP_Import_Findings.md FULLY (the AIP upload pattern this mirrors).
Confirm the open decisions in docs/v1.3/RAL-XXX_LDIP_Upload_Findings.md §3 with PPDO before
building — especially whether manual entry also needs activity fields (recommendation: no).

Read these files before writing code:
- backend/PPDO.Domain/Entities/AipActivity.cs (field set to mirror, minus EsreCode, plus 6 new
  alignment text columns: PdpRdp, Sdgs, SendaiFramework, NdrrmPlan, Nsp, Pdpdfp)
- backend/PPDO.Application/Services/IAipXlsmParser.cs + Infrastructure/Services/AipXlsmParser.cs
  (parser contract + ClosedXML implementation to mirror — note the LDIP sample file's sheets are
  plainly named General/Social/Economic/Others, NOT the AIP GENERAL_*/SOCIAL_*/... prefixed
  convention — verify against a real PPDO-submitted file before hardcoding a sheet-match rule)
- backend/PPDO.Functions/Functions/AipFunctions.cs (Upload/Confirm endpoint shape: raw octet-stream
  body, MemoryStream buffering, CanUploadAip gate, ParsePreviewAsync/ConfirmImportAsync split)
- backend/PPDO.Application/Services/LdipService.cs (BuildHierarchy/ValidateGroups/ref-code
  renumbering to extend one level deeper for activities under each program)
- backend/PPDO.Application/DTOs/BudgetPlanning/LdipDtos.cs and AipDtos.cs (DTO shapes to extend/mirror)
- frontend/src/app/(portal)/budget-planning/aip/new/page.tsx and .../aip/import-preview/page.tsx
  (page structure + sessionStorage stash pattern to mirror for LDIP)
- frontend/src/lib/aip.ts (client helper shape to mirror in lib/ldip.ts)
- frontend/src/app/(portal)/budget-planning/ldip/LdipForm.tsx (add read-only activity sub-rows
  under each program row in "Created Programs" — this is the "view what was uploaded" requirement)
- backend/PPDO.Tests/Infrastructure/AipXlsmParserTests.cs and Application/AipServiceTests.cs
  (test shape to mirror)

Working branch: main (or the current v1.3 release branch if one is open — confirm before starting).
Create feature/v1.3-ral-XXX-ldip-upload off it and open the PR against it (NOT main directly).

TDD: write LdipXlsmParserTests + extend LdipServiceTests with failing tests first, then implement.

1. Migration: add ldip_activities table — id, ldip_program_id FK (Cascade), ref_code NVARCHAR(50)
   UNIQUE per (ldip_program_id, ref_code), name NVARCHAR(1000), implementing_office NVARCHAR(200)?,
   start_date NVARCHAR(50)? (bare year string, NOT month name), end_date NVARCHAR(50)?,
   expected_outputs NVARCHAR(MAX)?, funding_source_id INT? FK, funding_source_snapshot NVARCHAR(20)?,
   ps/mooe/co/total DECIMAL(18,2)?, cc_adaptation/cc_mitigation DECIMAL(18,2)?,
   cc_typology_code NVARCHAR(50)?, pdp_rdp/sdgs/sendai_framework/ndrrm_plan/nsp/pdpdfp NVARCHAR(500)?.
   snake_case (new table, per NAMING_CONVENTIONS.md).
2. Domain: LdipActivity entity + LdipActivityConfiguration; add Activities nav collection to LdipProgram.
3. Application: ILdipXlsmParser + LdipXlsmParser (Infrastructure, ClosedXML) — Parse(Stream) →
   Dictionary<sector, List<ParsedLdipOffice>>, distinguishing Program rows (blank schedule/funding/
   output columns) from Activity rows (all populated) within each sheet. Add ParsedLdipActivity
   record with the 20-column field set above.
4. Application: LdipService.ParsePreviewAsync(stream, fiscalYearStart, fiscalYearEnd, officeId?,
   fundingSources, ct) and ConfirmImportAsync(dto, callerId, ct), mirroring AipService. Extend
   BuildHierarchy to attach activities per program with continuous ref-code numbering one level
   deeper. Populate LdipProgram.Budget as SUM(activities.Total) for uploaded programs.
5. DTOs: LdipActivityDto, extend LdipProgramDto with Activities list; LdipImportPreviewDto
   (counts incl. activities, sectorOffices tree, warnings), LdipImportConfirmDto.
6. Functions: POST /api/budget-planning/ldip/upload?fiscalYearStart=&fiscalYearEnd=&officeId=
   (raw octet-stream, CanUploadAip gate) and POST /api/budget-planning/ldip/confirm (same gate).
7. Frontend: lib/ldip.ts — uploadLdipFile(file, yearStart, yearEnd, officeId?), confirmLdipImport(body).
   ldip/new/page.tsx gains Upload File / Manual Entry tabs (mirror aip/new — Manual Entry stays as
   today's LdipForm flow, not disabled, since LDIP's manual entry already ships). New
   ldip/import-preview/page.tsx mirroring aip/import-preview (stat tiles: offices/programs/
   activities; sector breakdown; warnings). LdipForm.tsx "Created Programs" table: add an
   expand/collapse per program row showing its activities read-only (implementing office/dates/
   outputs/funding/PS-MOOE-CO/CC fields/alignment tags) — this is what makes Edit usable as a
   "view what was uploaded" screen.

Do NOT build the Program Description Form sheet's fields (Department/Description/Mandates/
Baseline/etc.) — separate open question, not this ticket's scope. Do NOT force activity-level
fields into the manual "+ Add Program" flow unless PPDO explicitly asks (recommendation: upload-only
for activity detail). Do NOT change AIP's own schema, parser, or endpoints — only mirror the pattern.

When done, commit with:
feat(budget-planning): LDIP file upload with activity-level detail (RAL-XXX)
```
