# Fund Source, Ceiling & Allocation — Current State Findings

> Research pass prepared ahead of writing requirements for "ceiling and allocation setup
> for other fund sources." Branch: `release/1.4.3`. Scope: backend
> (`PPDO.Domain`/`Infrastructure`/`Application`/`Functions`) + frontend (`frontend/src`) + `docs/`.

---

## 1. Ceiling and Allocation

### 1.1 Data model — office/division scoped only, no fund-source dimension

| Table | Entity | Key columns | Uniqueness |
|---|---|---|---|
| `budget_ceilings` | [`BudgetCeiling.cs:9-17`](../../backend/PPDO.Domain/Entities/BudgetCeiling.cs) | `office_id`, `fiscal_year`, `amount` | Unique `(office_id, fiscal_year)` — [`BudgetCeilingConfiguration.cs:29-31`](../../backend/PPDO.Infrastructure/Data/Configurations/BudgetCeilingConfiguration.cs), migration `20260629014205_AddAllocationTables.cs:76-80` |
| `division_allocations` | [`DivisionAllocation.cs:9-17`](../../backend/PPDO.Domain/Entities/DivisionAllocation.cs) | `division_id`, `fiscal_year`, `amount` | Unique `(division_id, fiscal_year)` — same migration, lines 82-86 |
| `program_divisions` | (no dedicated entity file; managed via `IAllocationRepository`) | `office_ref_code`, `program_ref_code`, `division_id` | Unique `(office_ref_code, program_ref_code, division_id)` |
| `wfp_division_allocation_ledger` | [`WfpDivisionAllocationLedger.cs:17-49`](../../backend/PPDO.Domain/Entities/WfpDivisionAllocationLedger.cs) | `division_id, fiscal_year, wfp_record_id, allocated_amount_snapshot, used_amount, updated_at` | one row per `(division_id, fiscal_year, wfp_record_id)` |

**Key gap:** None of these four tables has a `funding_source_id` column. The DB-level unique
constraints (`office_id+fiscal_year` on ceilings, `division_id+fiscal_year` on allocations)
structurally allow only **one ceiling per office/FY** and **one allocation per division/FY** —
adding a fund-source dimension requires a schema change (widen the unique key to include
`funding_source_id`, or add a new table), not just new logic.

### 1.2 Relationship between Ceiling and Allocation

- **Ceiling** = the PBO's total budget cap for one `(office, fiscal_year)`
  (`BudgetCeiling.cs:4-7`: "maximum total that can be allocated across all divisions for that
  office+FY").
- **Allocation** = how that ceiling is split across the office's divisions — one
  `DivisionAllocation` row per `(division, fiscal_year)`.
- Enforced in `AllocationService.UpsertAllocationsAsync`
  ([`AllocationService.cs:107-171`](../../backend/PPDO.Application/Services/AllocationService.cs)):
  Guard 1 requires a ceiling to exist first (line 112-116); Guard 2 rejects if
  `Σ(division amounts) > ceiling.Amount` (line 118-122).
- Both amounts are always in **pesos** (explicit repeated doc comments — never the AIP ×1000
  convention).

### 1.3 How allocation is set

- Set via `/budget-planning/allocation` page
  (`frontend/src/app/(portal)/budget-planning/allocation/page.tsx`) — finance-officer-only
  (`CanManageAllocation`), scoped by **Office + Fiscal Year**, then split by **Division**
  (`AllocationService.cs:88-171`, DTOs in
  `backend/PPDO.Application/DTOs/BudgetPlanning/AllocationDtos.cs:5-16`).
- A separate concept, **PPA → Division assignment** (`program_divisions`), maps AIP Programs to
  Divisions (`AllocationService.cs:175-280`), not to fund sources.
- Endpoints: `backend/PPDO.Functions/Functions/AllocationFunctions.cs` — `GET/PUT ceiling`,
  `GET/PUT divisions`, `GET/PUT programs`, `GET status`.
- Design intent doc: `docs/v1.2/Allocation_Requirements.md:1-90` (v1.2, RAL-99/101) — explicitly
  designed as Office→Ceiling→Division only; no mention of fund source anywhere in that doc
  (confirmed via grep, zero matches).

### 1.4 Setup gate

`AllocationService.GetSetupStatusAsync` (`AllocationService.cs:283-321`) requires 3 flags before
WFP entry is allowed: `HasCeiling`, `HasAllocation`, `HasProgramAssignment` — all purely
office/division-scoped, no fund-source flag.

**Gap:** There is currently no way to set a separate ceiling or allocation per fund source (e.g. a
GAD-specific ceiling distinct from the General Fund ceiling). A new feature would need either
(a) a new `funding_source_id` dimension added to `budget_ceilings`/`division_allocations`/the
ledger with new unique-key/migration work, or (b) a parallel table structure.

---

## 2. WFP value checking against allocation/ceiling

### 2.1 Core validator: `WfpCeilingService`

- Interface: `backend/PPDO.Application/Services/IWfpCeilingService.cs` — doc comment explicitly
  frames this as "v1.4 WFP Rework — §8, RAL-122."
- Implementation: `backend/PPDO.Application/Services/WfpCeilingService.cs`.
- **Two independent checks**, both server-computed and both **block-on-save** (not just a
  warning):
  1. **AIP budget check** (`ValidateExpenditureSaveAsync`, lines 66-85): Σ of WFP expenditure
     totals for one AIP activity, across **all divisions of the office**, vs.
     `AipActivity.Total × 1000` (the one place the AIP-thousands→pesos conversion happens, line
     52/77).
  2. **Division allocation check** (lines 87-103): reads `wfp_division_allocation_ledger` (never a
     live SUM over `wfp_expenditures`) — `Remaining = DivisionAllocation.Amount −
     Σ(used_amount across that division+FY's ledger rows)`.
- Both checks are **fund-source-agnostic** — they sum ALL expenditures for the activity/division
  regardless of which `FundingSourceId` each expenditure carries. A GAD-fund expenditure and a
  General-Fund expenditure on the same activity/division are summed together against the same
  single ceiling/allocation.
- Called from `WfpExpenditureService.SaveExpenditureAsync` **before every write**
  (`backend/PPDO.Application/Services/WfpExpenditureService.cs:158-162`) — returns `BadRequest`
  with a message naming which ceiling and by how much (e.g. "exceeding its AIP budget of ₱X by
  ₱Y", lines 82-84, 100-102 of `WfpCeilingService.cs`).
- `WfpService.FinalizeAsync` runs `ValidateRecordForFinalizeAsync` as an independent backstop
  (`WfpCeilingService.cs:150-181`), described as "should be unreachable in practice once every
  save is blocked."
- Ledger upkeep: `UpsertLedgerForActivityAsync` (`WfpCeilingService.cs:110-146`) recomputes
  `used_amount` after every successful save.

### 2.2 Frontend display

`frontend/src/app/(portal)/budget-planning/wfp/entry/page.tsx`:

- `useCeilingStatus` hook (lines 155-210) — debounced (400ms) live client-side check calling
  `getCeilingStatus` (`frontend/src/lib/allocation.ts:143-153`,
  `GET /api/budget-planning/wfp/ceilings`).
- Sticky context header (§4.2 in the design doc) shows both ceilings — division allocation
  remaining (line ~1288-1301) and AIP budget vs. used (line ~1306-1314) with progress bars.
- Inline warning inside the expenditure wizard when a pending amount "would exceed" the division
  allocation (lines 463-476: "Exceeds division allocation by …").
- Save button is disabled before a would-be-rejected save round-trips to the server (design doc
  §8).

No PPMP module exists in this codebase (a "PPMP" string match was just a seeded external resource
link title, not a module) — WFP is the only module with this ceiling-checking logic; Purchase
Requests (PR) have no such ceiling/allocation validation at all.

### 2.3 Design doc

`docs/v1.4/WFP_Rework_Requirements_Draft.md` §8 "Ceiling monitoring & validation (Division
Allocation Fund)" (lines 343-386) is the authoritative spec: confirms block-on-every-save (not
warn-then-block-on-finalize, which was struck through as superseded), the ledger design
rationale, and explicitly frames the ledger as "WFP-scoped by design (not a generic polymorphic
ledger)... named/shaped so a future consumer of the same allocation could post its own rows
later... but that generalization is explicitly out of scope for this ticket" (also echoed in
`WfpDivisionAllocationLedger.cs:12-15`). This is a direct pointer that fund-source-scoped
ceilings were anticipated but deliberately deferred.

**Gap:** No fund-source-aware validation exists anywhere in this pipeline. A GF-only division
allocation gets debited by GAD or SEF expenditures too, since the ledger and ceiling checks don't
filter/group by `FundingSourceId`.

---

## 3. Fund Source usage

### 3.1 The `FundingSource` config entity (proper, multi-source-capable)

- Entity: `backend/PPDO.Domain/Entities/FundingSource.cs` — `Id, Code, Name, Description, Color,
  IsActive` (config table, RAL-73, CSV-seeded). Doc comment: "e.g. GF, GAD, LDRRMF."
- Table: `funding_sources`. CRUD service `FundingSourceService`/`IFundingSourceService`, endpoints
  `backend/PPDO.Functions/Functions/ConfigFundingSourceFunctions.cs` (list/get/create/update/delete/
  CSV import-export).
- Frontend config page: `frontend/src/app/(portal)/config/funding-sources/`.
- Per `docs/v1.4/WFP_New_Form_Findings.md:93`, 6 sources are seeded and match the WFP workbook's
  dropdown: **General Fund, Special Education Fund, Trust Fund, Calamity Fund, 20% Development
  Fund, Gender & Development Fund**.

### 3.2 Where `FundingSourceId` is actually referenced (all *optional*, nullable FKs)

| Entity | FK | Snapshot columns | File |
|---|---|---|---|
| `AipActivity` | `FundingSourceId` (nullable) | `FundingSourceSnapshot` (code) | `backend/PPDO.Domain/Entities/AipActivity.cs:40-44` — "Null when unmatched at import" |
| `WfpExpenditure` | `FundingSourceId` (nullable) | `FundingSourceSnapshot`, `FundingSourceNameSnapshot` | `backend/PPDO.Domain/Entities/WfpExpenditure.cs:38-45` — "Null when not selected" |
| `WfpExpenditureLine` (legacy pre-rework) | `FundingSourceId` (nullable) | same snapshot pair | `backend/PPDO.Domain/Entities/WfpExpenditureLine.cs:69-76` |
| `LdipProgram`, `PriceIndexItem` | also carry a `FundingSource` nav property (not read in detail) | — | `backend/PPDO.Domain/Entities/LdipProgram.cs`, `PriceIndexItem.cs` |

**None of these are required.** `WfpExpenditureService.ValidateDto`
(`backend/PPDO.Application/Services/WfpExpenditureService.cs:318-357`) never checks that
`FundingSourceId` is set — an expenditure can be saved with `FundingSourceId = null`.

### 3.3 Frontend fund-source selection & default logic

In `frontend/src/app/(portal)/budget-planning/wfp/entry/page.tsx`:

- `resolveDefaultFundingSourceId` (lines 105-120): pre-fills the wizard's fund-source dropdown by
  matching the **AIP activity's `fundingSourceSnapshot`** text against known
  `FundingSource.code`/`.name`/description-aliases. Returns **`null`** (no default) if:
  - the snapshot is blank, OR
  - the snapshot contains a comma or slash (line 110: `if (/[,/]/.test(snapshot)) return null;` —
    i.e., an AIP activity funded by multiple sources is deliberately left unresolved rather than
    guessed), OR
  - no match is found in the funding-source list.
- The dropdown itself (lines 546-559, `<Lookup>` component) starts with whatever
  `defaultFundingSourceId` resolved to (possibly `null`/unselected) and the user can freely
  override it — there is **no hardcoded fallback to "General Fund" or "GF"** anywhere in this
  WFP flow.
- Design doc confirms this is intentional: `docs/v1.4/WFP_Rework_Requirements_Draft.md:56,79`:
  "Fund source | Per expenditure entry, **defaulted from the AIP activity's funding source**,
  overridable."

**Gap #1 (confirmed unhandled case):** When an AIP activity has no fund source, an ambiguous
(multi-fund) fund source, or an unmatched fund-source string, the WFP expenditure entry's Fund
Source field is left **blank/unselected**, and nothing in the backend enforces selecting one
before save. There is no explicit "please choose a fund source" required-field validation, and no
default-to-General-Fund behavior in this path. **This is the case the new feature's "note in the
fund source field" idea is targeting.**

### 3.4 Reporting layer already supports multiple fund sources

`backend/PPDO.Application/Services/WfpReportService.cs`:

- `DefaultFundSourceName = "GENERAL FUND"` (line 28) is used **only** as the display bucket for
  expenditures with no fund source snapshot at all (`FundSourceNameFor`, lines 364-365:
  `string.IsNullOrWhiteSpace(e.FundingSourceNameSnapshot) ? DefaultFundSourceName :
  e.FundingSourceNameSnapshot`), matching the WFP-NEW.xlsx layout's "General Fund" as the
  first/default block.
- The report **dynamically builds one block per distinct fund source name present** in the data
  (lines 153-171) — i.e., the reporting layer already fully supports "other fund sources" and
  requires no changes for a multi-fund report. This is the one place "General Fund" is a real
  default/fallback string, and it's presentational grouping only — it does not affect
  ceiling/allocation math.

### 3.5 Purchase Request (PR) module — separate, disconnected free-text field

- `PurchaseRequest.Fund` is a **plain string**, not an FK to `FundingSource`
  (`backend/PPDO.Domain/Entities/PurchaseRequest.cs:36-37`: `"Funding source. e.g. \"General
  Fund\"."`).
- Frontend PR creation form: `frontend/src/app/(portal)/inventory/create-pr/page.tsx:791-797` —
  free-text input, required (`f.fund.trim()` validated, line 324), placeholder `"e.g. General
  Fund"`, no dropdown tied to the `funding_sources` config table.
- Excel-import mapping defaults blank `Fund` to `"General Fund"`:
  `backend/PPDO.Application/Services/PurchaseRequestService.cs:559` — `Fund = row.Fund ??
  "General Fund"`. This **is** a hardcoded "General Fund" default, but it's scoped to the PR
  Excel-import path only, and it's independent of the `FundingSource` config table (just a
  literal string).
- `IExcelService`/`ExcelService` and `CreatePRDto` also carry a `Fund` string field
  (`backend/PPDO.Domain/Interfaces/IExcelService.cs`,
  `backend/PPDO.Application/DTOs/PurchaseRequest/CreatePRDto.cs`).

### 3.6 PR Number format — "GF" hardcoded, confirmed

- `CLAUDE.md`: `| PR No. format | \`101-1041-GF-YYYY-MM-DD-XXX\` (3-digit zero-padded sequence) |`.
- Entity doc comment: `backend/PPDO.Domain/Entities/PurchaseRequest.cs:9,19` — same format string.
- **Generation logic**: `backend/PPDO.Application/Services/PurchaseRequestService.cs`:
  - `GeneratePRNoAsync` (lines 437-465) — the format string is built at **line 464**:
    ```csharp
    return $"101-1041-GF-{dateSegment}-{nextSeq:D3}";
    ```
    `"GF"` is a **hard-coded literal**, not derived from `PurchaseRequest.Fund` or any
    `FundingSource` record. Every PR gets a `-GF-` segment regardless of what fund source is
    actually selected on the form.
  - `ParseSequence` (lines 467-475) — parses the 3-digit sequence back out of the fixed 7-part
    format; also implicitly assumes the `GF` segment position never varies.
- **Gap #2 (confirmed hardcode):** The PR number generator always embeds `"GF"` literally. If a
  PR is created against a different fund source (e.g. GAD-funded supplies), the generated PR No.
  still shows `101-1041-GF-...`, which is inconsistent with the `Fund` field the user actually
  typed. There is no logic mapping `dto.Fund`/a `FundingSourceId` to a fund-source code segment
  in the PR number.

---

## Summary of gaps relevant to "ceiling and allocation for other fund sources"

1. **Schema gap (biggest lift):** `budget_ceilings` and `division_allocations` have no
   `funding_source_id` column; their unique keys (`office+FY`, `division+FY`) structurally allow
   only one ceiling/allocation per office or division per year, with no per-fund breakdown.
   `wfp_division_allocation_ledger` is likewise fund-agnostic. The
   `WfpDivisionAllocationLedger.cs` doc comment explicitly says this generalization was deferred,
   not designed for.
2. **Validation gap:** `WfpCeilingService`'s AIP-budget and division-allocation checks sum
   expenditures **across all fund sources** against a single ceiling/allocation — they don't
   segment by `FundingSourceId` at all, so a multi-fund office can't have independent per-fund
   budget tracking today.
3. **Fund-source selection UX gap:** In WFP expenditure entry, when the AIP activity has no fund
   source, an ambiguous one, or an unmatched one, the Fund Source dropdown is left unselected
   with **no enforced requirement** to pick one before saving, and **no default-to-General-Fund
   fallback**. This is an explicit, currently-unhandled case
   (`resolveDefaultFundingSourceId` returns `null`) — and the specific gap the proposed
   "default to General Fund + inline note" idea would close.
4. **PR module inconsistency:** `PurchaseRequest.Fund` is free text, disconnected from the
   `FundingSource` config table used elsewhere; its Excel import defaults blank to `"General
   Fund"` (a literal string default, distinct from anything in WFP/AIP), and the PR number
   generator hardcodes `"GF"` regardless of the actual selected fund.
5. **Reporting layer is already fund-source-ready** — `WfpReportService` dynamically builds one
   report block per distinct fund source with no schema changes needed; this can serve as a
   model/precedent for how "other fund sources" should flow through once ceiling/allocation is
   made fund-source-aware.
6. **Documented but unresolved open question:** `docs/v1.4/WFP_New_Form_Findings.md` §8 Q7 ("Fund
   sources per office... is each office×fund an independently draftable/finalizable document?")
   was raised during the WFP rework design phase and left open; the rework requirements draft
   resolved fund source as "per expenditure entry, defaulted, overridable" but never revisited
   ceiling/allocation to be fund-source-scoped. No ticket or doc in `docs/v1.4/` currently
   specifies the target design for fund-source-scoped ceilings/allocations — this appears to be
   genuinely new-requirements territory, not an already-planned-but-unbuilt feature.

---

## Open question to resolve before writing requirements

Should "ceiling and allocation setup for other fund sources" include segmenting the
ceiling/allocation/ledger by fund source (gaps #1–2, a schema + validation change), or is the
immediate scope just the WFP entry-form default + note (gap #3)? The two are independent: a
default+note fix does not, by itself, give per-fund-source budget tracking — expenditures under
the defaulted fund source would still be checked against the single fund-agnostic
office/division ceiling that exists today.
