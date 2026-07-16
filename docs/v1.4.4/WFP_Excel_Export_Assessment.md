# v1.4.4 — WFP Report → Excel Export (PBO form): Initial Assessment

> **Status: initial assessment — work continues.** Written 2026-07-15 from an inspection of the
> PBO-provided form `WFP-NEW.xlsx` (kept outside the repo at `D:\RalphFiles\PPDO\WFP-NEW.xlsx` —
> **not committed**; it is a working reference, not source). Goal for v1.4.4: export the WFP report
> as an `.xlsx` whose **structure and design match the PBO WFP form** (or an updated version of it).
>
> Decision already reached (see `docs/v1.4.3/…` discussion): **populate the PBO template**, not
> generate from scratch — it is an official fixed-layout form, and our current exports
> (`ExcelService.ExportPRReport`) build cell-by-cell in C#, which would be far more brittle for a
> government form. This doc records the form's actual structure so implementation can start clean.
>
> **v1.4.4 acceptance bar (confirmed 2026-07-16):** hierarchy, activities, and expenditure values
> populate accurately. The 6 descriptive columns F–K ship **blank** — Ralph is separately
> clarifying that requirement; see §4. Not a blocker for this feature.

---

## 1. Key finding — the report was already modelled on this form

`WfpReportDto` and its children (`backend/PPDO.Application/DTOs/BudgetPlanning/WfpReportDtos.cs`)
were **built to mirror this exact form** — their XML-doc comments already reference "the WFP FINAL
sheet" and an earlier `WFP-Copy_NEW.xlsx`. So the on-screen report page (`/budget-planning/report`)
and this Excel form are the *same logical structure*; the export is largely a **rendering** of the
existing `WfpReportDto`, not new business logic. That de-risks the feature substantially.

---

## 2. The form file (`WFP-NEW.xlsx`)

Three sheets:

| Sheet | Purpose | Notes |
|---|---|---|
| **`WFP FINAL`** | The form itself | `A2:AY256`, **landscape**, freeze panes at `A9`, font **Arial Narrow 13**. Columns `B`–`W` visible; `X`–`AZ` hidden helper columns. |
| `VALIDATION` | Account-code → title lookup + data-validation dropdowns | `N` column uses `=VLOOKUP(M, VALIDATION!$D$3:$E$403, 2, 0)`. Our export writes account titles directly (we have them), so this sheet is **not needed** in the output. |
| `Accounts with Reserve` | Reference list of reserve-eligible accounts | Informational; our reserve amounts come from the DTO. |

### 2.1 Header block (rows 2–8)

| Cell | Content | Maps to |
|---|---|---|
| `B2` | `WORK AND FINANCIAL PLAN FY {year}` (merged `B2:U2`) | `WfpReportDto.FiscalYear` |
| `B3` | `FY {year}` | " |
| `B4` / `E4` | `DEPARTMENT/OFFICE:` / office name | `WfpReportDto.OfficeName` |
| `B5` / `E5` | `SOURCE OF FUND:` / **`GENERAL FUND`** | **`WfpReportFundSourceDto.FundSourceName`** — see §3 |
| `P6` | `Equiv. to 10% of Operational Expenses` (reserve note) | `WfpReportDto.ReserveRate` |
| rows 7–8 | Column headers, green fill `#A8D08D`, wrapped, merged vertically | static |

### 2.2 Column layout (the 22 visible columns)

| Col | Header | Source (per row) |
|---|---|---|
| `B` | AIP REF CODE | ref code of the band/program/project/activity on that row |
| `C`/`D`/`E` | PROGRAMS, PROJECTS AND ACTIVITIES (merged `C7:E8`) | program title in `C`, project in `D`, activity in `E` (indent by column) |
| `F` | RESOURCES NEEDED | ⚠️ **not in DTO** — see §4 |
| `G` | RESPONSIBLE PERSON/UNIT | ⚠️ **not in DTO** |
| `H` | SUCCESS INDICATOR | ⚠️ **not in DTO** |
| `I` | MEANS OF VERIFICATION | ⚠️ **not in DTO** |
| `J` | OUTCOME INDICATOR | ⚠️ **not in DTO** (entry wizard captures `outcomeIndicator`) |
| `K` | TARGET BENEFICIARIES | ⚠️ **not in DTO** (entry wizard captures `targetBeneficiaries`) |
| `L` | NATURE OF IMPLEMENTATION | `WfpReportRowDto.Nature` (Procurement / Non-procurement) |
| `M` | ACCOUNT CODE | `WfpReportRowDto.AccountNumber`; also holds the PS/MOOE/CO sub-headers and `SUB-TOTAL` |
| `N` | OBJECT OF EXPENDITURE | `WfpReportRowDto.AccountTitle` (form uses VLOOKUP; we write directly) |
| `O` | TOTAL APPROPRIATION | `Amounts.TotalAppropriation` (form: `=P+Q`) |
| `P` | RESERVED | `Amounts.Reserved` |
| `Q` | NET APPROPRIATION | `Amounts.NetAppropriation` (form: `=SUM(R:U)`) |
| `R`/`S`/`T`/`U` | TIME FRAME 1st–4th Quarter | `Amounts.Q1..Q4` |
| `V` | AMOUNT TO BE RELEASED | `Amounts.AmountToBeReleased` |
| `W` | (hidden check column) | ignore |

Accounting number format throughout: `_(* #,##0.00_);_(* \(#,##0.00\);_(* "-"??_);_(@_)`.

### 2.3 Body hierarchy & the repeating block (maps 1:1 to our DTO tree)

```
CORE FUNCTIONS                      ← WfpReportFunctionBandSectionDto.FunctionBandLabel   (col B, italic)
  {program ref}  PROGRAM TITLE      ← WfpReportProgramDto (ref in B, title in C)
    {project ref}  PROJECT TITLE    ← WfpReportProjectDto (ref in B, title in D)
      {activity ref}  ACTIVITY      ← WfpReportActivityDto (ref in B, title in E; F–K descriptive)
        PERSONAL SERVICES           ← WfpReportExpenseClassGroupDto (M header)
          <expenditure rows>        ← WfpReportRowDto      (L,M,N,O,P,Q,R–U,V)
          SUB-TOTAL                 ← ExpenseClassGroupDto.SubTotal   (green fill #92D050)
        MAINTENANCE AND OTHER…      ← next ExpenseClassGroupDto
          … SUB-TOTAL
        CAPITAL OUTLAY              ← next ExpenseClassGroupDto
          … SUB-TOTAL
        ACTIVITY GRAND TOTAL        ← WfpReportActivityDto.GrandTotal   (yellow fill #FFFF00)
    PROGRAM GRAND TOTAL             ← WfpReportProgramDto.GrandTotal    (orange fill #FFC000)
STRATEGIC FUNCTIONS …
SUPPORT FUNCTIONS …
  (closing breakdown — see §2.4)
```

### 2.4 Closing breakdown block (rows 77–82) → `WfpReportBreakdownDto` exactly

| Form label (col M) | DTO field | Fill |
|---|---|---|
| `TOTAL - PERSONAL SERVICES` | `PersonalServices` | `#C5E0B3` |
| `TOTAL - MOOE (Excluding Creation)` | `MooeExcludingCreation` | " |
| `TOTAL - CAPITAL OUTLAY` | `CapitalOutlay` | " |
| `TOTAL - PERSONAL SERVICES CREATION` | `PersonalServicesCreation` | " |
| `TOTAL - MOOE - CREATION` | `MooeCreation` | " |
| `GRAND-TOTAL` | `GrandTotal` | `#FFFF00` |

### 2.5 Per-fund repetition

Row 88 restarts `WORK AND FINANCIAL PLAN FY 2027` with a fresh header block — i.e. **the whole
form repeats once per fund source**, matching `WfpReportDto.FundSourceReports` (one
`WfpReportFundSourceDto` block each). `SOURCE OF FUND:` (row 5) is the per-block fund name.

---

## 3. Resolved / confirmed design points

- **One fund per form block.** `SOURCE OF FUND` is single-valued and the block repeats per fund →
  export produces one form block per `WfpReportFundSourceDto`. **Open sub-decision:** one **sheet
  per fund** vs. **stacked blocks on one sheet** vs. **one file per fund**. Leaning: one sheet per
  fund source (clean page breaks, mirrors the "block repeats" intent). Confirm with PBO.
- **Formulas → write computed values.** The form is formula-driven (VLOOKUP + SUM). For a generated
  artifact we **write the computed values directly** from `WfpReportDto` (which already holds every
  subtotal/total/quarter). Avoids the fragile "insert rows and hope SUM ranges extend" problem and
  removes the dependency on the `VALIDATION` sheet. **Confirmed 2026-07-16 (Ralph):** this applies to
  every formula cell in the form, not just totals — overwrite outright with the computed value, no
  attempt to preserve or repair the formula.
- **Template population, not scratch.** Confirmed prior decision.
- **Freeze panes dropped; everything else visual is preserved exactly.** **Confirmed 2026-07-16
  (Ralph):** freeze panes (`A9` in the source form) do not need to survive the export — fine to drop.
  Column widths, cell fill colors (band/subtotal/grand-total/breakdown), border layout, and the
  merged-cell structure are **not** optional and must match the source form exactly — these carry the
  form's visual identity and are the main verification criteria for RAL-160.

## 4. Decided: F–K columns ship blank in v1.4.4

**Decision (2026-07-16, Ralph):** the 6 descriptive columns (Resources Needed, Responsible
Person/Unit, Success Indicator, Means of Verification, Outcome Indicator, Target Beneficiaries)
are **project-level** by design (per Ralph's own discussion/intent for the entry wizard — this is
the authoritative call, overriding my raw reading of the form's row-12 placement below). They stay
**out of scope for v1.4.4's Excel export**: the generated file ships with F–K **blank**, and PBO
staff fill them in manually after export, same as they do today. Ralph is separately clarifying the
requirement; the export's job for now is to get the **hierarchy, activities, and expenditure values
accurately populated** — that's the acceptance bar, not F–K.

Do NOT block implementation on this. Do NOT add backend persistence for these fields as part of
this feature. Revisit only if/when Ralph's clarification lands and he asks for it explicitly.

<details>
<summary>Reference: raw form/UI findings (kept for whenever F–K clarification lands)</summary>

The form's Resources Needed/Responsible Person/Success Indicator/Means of Verification/Outcome
Indicator/Target Beneficiaries sit on row 12, the same row as `E12 = ACTIVITY` — i.e. the raw
template places one set of 6 values per activity row, not per project. The entry wizard's existing
UI for these fields ("Project details (optional)" accordion, `PROJECT_FIELDS` const ~line 183 in
`frontend/.../wfp/entry/page.tsx`) is (a) **localStorage-only** — `saveProjectField()` (~line 1150)
writes to `localStorage.setItem(wfp_entry_project_fields_${selectedProjectId}, …)`, never sent to
any API (the accordion's own on-screen note confirms this) — and (b) keyed per **project**
(`selectedProjectId`). If backend persistence is ever added, resolve the project-vs-activity
granularity question against the real requirement first; check `AipActivity.expected_outputs` for
overlap before designing new columns.

</details>

## 5. Open items to resolve before/early in implementation

1. **Template packaging.** Bundle a cleaned copy of the PBO form (data rows stripped, one styled
   "template row" per row-type kept) as an embedded resource / content file under
   `PPDO.Infrastructure`. It must contain only the static chrome + a styled exemplar of each row
   type (band header, program, project, activity, expense-class header, expenditure row, sub-total,
   activity/program grand total, breakdown rows).
3. **Dynamic-row strategy (ClosedXML).** Clone the matching styled template row per data row (copy
   style, set values), then delete the placeholder template rows. Watch merged cells (`C7:E8`, the
   many `B{n}:B{m}` vertical merges) and row heights. Prototype early — this is the main technical
   risk.
4. **Multi-page / print setup.** Landscape, freeze at row 9, repeat header rows on each printed page
   (`print_titles`), set print area per fund block. Carry over from the template where possible.
5. **Where the button lives.** Report page already has print/PDF (RAL-149). Add an "Export to Excel"
   action that calls a new `GET …/budget-planning/wfp/report/export` returning the `.xlsx` bytes
   (mirror `ExportPRReport`'s `WfpReportPreview` sibling pattern).
6. **Scope of "or update it".** The user noted the generated report may *update* the form design
   rather than match it verbatim — clarify with PBO which columns/labels are fixed vs. adjustable.
   F–K is now confirmed non-blocking (§4) regardless of this answer.

## 5. Rough ticket shape (to firm up tomorrow)

Acceptance bar for v1.4.4 (confirmed): hierarchy, activities, and expenditure values populate
**accurately**; F–K columns ship blank (§4) — not a blocker.

- **A (backend, export):** `IWfpExcelService.ExportWfpReport(WfpReportDto)` + embedded template +
  row-cloning engine; new `WfpReportExport` function endpoint.
- **B (frontend):** "Export to Excel" button on the Report page calling the new endpoint.
- **C (verification):** open the generated file, diff structure/labels against `WFP-NEW.xlsx`;
  confirm per-fund blocks, subtotals, breakdown, and print layout.

---

## 6. Reference

- Inspection script (throwaway): reproduced with `openpyxl` — sheets, merged ranges, values,
  column widths, fills, fonts, number formats.
- Existing export precedent to mirror: `backend/PPDO.Infrastructure/Services/ExcelService.cs`
  (`ExportPRReport`, `IWfpExcelService`).
- Report data source: `WfpReportService.GetReportAsync` → `WfpReportDto`.
- Fills seen: header `#A8D08D`, sub-total `#92D050`, activity total `#FFFF00`, program total
  `#FFC000`, breakdown `#C5E0B3`.

*Continue tomorrow: start with the §4.1 data-availability audit (F–K fields) and a ClosedXML
row-cloning spike against a stripped template.*
