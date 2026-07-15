# v1.4.3 Tickets — Other Fund Source Ceiling & Allocation Setup

> Three implementation prompts, following [`../TICKET_PROMPT_STANDARD.md`](../TICKET_PROMPT_STANDARD.md).
> Authoritative spec: [`v1.4.3_Requirements.md`](v1.4.3_Requirements.md). Backend-first — **A must
> merge before B and C**. Replace `RAL-XXX` with the real Linear IDs when the tickets are created.
> Working integration branch: `release/1.4.3`. All PRs target `release/1.4.3` (**NOT `main`**).

Linear (milestone *v1.4.3 — Other Fund Source Ceiling & Allocation*, children of epic RAL-116):
**A = [RAL-154](https://linear.app/ralphoksiprojects/issue/RAL-154), B = [RAL-155](https://linear.app/ralphoksiprojects/issue/RAL-155) (blocked by A), C = [RAL-156](https://linear.app/ralphoksiprojects/issue/RAL-156) (blocked by A + D), D = [RAL-157](https://linear.app/ralphoksiprojects/issue/RAL-157) — funding-source aliases (blocked by A, blocks C).**

---

## Ticket A — Fund-source-scoped ceiling, allocation & WFP check (backend)  · RAL-154

```
Read CLAUDE.md, PROJECT_DOCUMENTATION_NET_AZURE.md, and PPDO_PROJECT_CONTEXT.md.
Read docs/v1.4.3/v1.4.3_Requirements.md FULLY — it is the authoritative spec for this ticket
(especially §2 Decisions, §3 Data model, §4 Backend behaviour). Also read
docs/v1.4.3/Fund_Source_Ceiling_Allocation_Findings.md for the current-state file:line map.

Read these files before writing code:
- backend/PPDO.Domain/Entities/BudgetCeiling.cs (ceiling entity — add funding_source_id)
- backend/PPDO.Domain/Entities/DivisionAllocation.cs (allocation entity — add funding_source_id)
- backend/PPDO.Domain/Entities/WfpDivisionAllocationLedger.cs (ledger — add funding_source_id + widen key)
- backend/PPDO.Domain/Entities/FundingSource.cs (config table — code "GF" is General Fund)
- backend/PPDO.Domain/Entities/WfpExpenditure.cs (FundingSourceId nullable — backfill nulls to GF)
- backend/PPDO.Infrastructure/Data/Configurations/BudgetCeilingConfiguration.cs (EF config + unique index pattern to mirror for allocation + ledger configs)
- backend/PPDO.Application/Services/AllocationService.cs (ceiling/allocation upsert + guards + setup gate — the fund-scoping happens here)
- backend/PPDO.Application/Services/WfpCeilingService.cs (AIP check stays aggregate; division-allocation check + ledger upsert become fund-scoped)
- backend/PPDO.Application/DTOs/BudgetPlanning/AllocationDtos.cs (DTOs gain FundingSourceId)
- backend/PPDO.Functions/Functions/AllocationFunctions.cs (endpoints — add fundingSourceId params; auth gates already correct, keep them)
- backend/PPDO.Application/Services/WfpReportService.cs (DefaultFundSourceName = "GENERAL FUND" — reference for GF fallback naming; do not change report grouping)
- backend/PPDO.Tests/Application/AllocationServiceTests.cs and WfpCeilingServiceTests.cs (extend these)
- The IWfpAllocationLedgerRepository + IWfpExpenditureRepository interfaces (SumUsedAmountAsync / Sum*Async — add fundingSourceId overloads)

Working branch: release/1.4.3. Create feature/v1.4.3-ral-XXX-fund-scoped-ceiling-allocation off
release/1.4.3 and open the PR against release/1.4.3 (NOT main).

DB naming: new columns are snake_case (funding_source_id) — these are v1.2+ snake_case tables.

TDD: extend AllocationServiceTests and WfpCeilingServiceTests with failing tests first (per-fund
Guard 2; GF-only setup gate; GAD expenditure debits GAD not GF; GF+GAD independent for allocation
but summed for AIP; null-fund expenditure treated as GF; ledger posts one row per fund source),
then implement.

1. Migration AddFundingSourceToAllocation (single migration, safe sequence per §3.4):
   a. Add nullable funding_source_id to budget_ceilings, division_allocations,
      wfp_division_allocation_ledger.
   b. Backfill all existing rows in those three tables to the GF funding_sources row
      (resolve @gfId by Code='GF'; fail loudly if missing).
   c. Backfill wfp_expenditures (and legacy wfp_expenditure_lines) rows with null funding_source_id
      to GF: set funding_source_id, funding_source_snapshot='GF', funding_source_name_snapshot='General Fund'.
   d. Alter the three funding_source_id columns to NOT NULL.
   e. Drop old unique indexes; add the widened unique indexes from §3.1–3.3
      (ceiling: office+FY+fund; allocation: division+FY+fund; ledger key adds fund before wfp_record_id).
   f. Add FK funding_source_id → funding_sources.id (OnDelete Restrict) on all three.
2. Domain + EF: add FundingSourceId (+ FundingSource nav) to BudgetCeiling, DivisionAllocation,
   WfpDivisionAllocationLedger; update the three IEntityTypeConfiguration classes (column mapping,
   unique index, FK) mirroring BudgetCeilingConfiguration.
3. AllocationService: add fundingSourceId to GetCeilingAsync/UpsertCeilingAsync/GetAllocationsAsync/
   UpsertAllocationsAsync; add GetCeilingsAsync(officeId, fiscalYear) returning all funds' ceilings;
   make Guard 1 + Guard 2 per fund source; GetSetupStatusAsync + GetSetupOverviewAsync key on GF only (§4.1).
4. AllocationDtos: add FundingSourceId to write DTOs and FundingSourceId + FundingSourceCode/Name to
   read DTOs.
5. WfpCeilingService: keep the AIP check aggregate (do NOT fund-scope it); fund-scope the
   division-allocation check (GetDivisionAllocationAsync by fund; ledger sums by fund); make
   UpsertLedgerForActivityAsync group the record's expenditures by FundingSourceId and upsert one
   ledger row per (division, FY, fund, wfp record); treat null-fund expenditures as GF; extend
   WfpCeilingStatusDto with a per-fund-source list (always include GF) and fill it in GetStatusAsync;
   fund-scope the finalize backstop.
6. Ledger + expenditure repos: add fundingSourceId to SumUsedAmountAsync and the FindAsync/ledger
   lookups; add a per-fund SumTotalByWfpRecord grouping (or return a per-fund breakdown) as needed.
7. Functions: add fundingSourceId query param to ceiling/divisions GET+PUT (AllocationFunctions);
   keep the existing auth gates (CanManageAllocation for PUT, CanAccessBudgetPlanning for GET, and
   the non-finance own-division filter on GetDivisions). ceilings endpoint returns the richer DTO.

Do NOT touch the Purchase Requests module (PurchaseRequest.Fund, the hardcoded "-GF-" in
PurchaseRequestService.GeneratePRNoAsync) — out of scope (§2 D10). Do NOT fund-scope the AIP-budget
check (§2 D3). Do NOT change Tab 2 PPA→Division assignment (not fund-scoped). Do NOT reintroduce the
retired Division enum / PermissionGroup table.

When done, commit with:
feat(budget-planning): fund-source-scoped ceiling, allocation & WFP check (RAL-154)
```

---

## Ticket B — Allocation page: per-fund-source ceiling & allocation UI (frontend) · RAL-155

```
Read CLAUDE.md, PROJECT_DOCUMENTATION_NET_AZURE.md, and PPDO_PROJECT_CONTEXT.md.
Read docs/v1.4.3/v1.4.3_Requirements.md FULLY — especially §5.1 and the Allocation-page mockup.
Blocked by Ticket A (backend endpoints must carry fundingSourceId first).

Read these files before writing code:
- frontend/src/app/(portal)/budget-planning/allocation/page.tsx (the page to update — reuse AllocationBar, DIVISION_COLORS, MoneyInput; Tab 1 becomes per-fund-source; Tab 2 unchanged)
- frontend/src/lib/allocation.ts (getCeiling/getAllocations/upsert* — add fundingSourceId; add getCeilings for all funds)
- frontend/src/lib/config.ts (listFundingSources — active fund sources for the sections)
- frontend/src/types (BudgetCeilingDto, DivisionAllocationDto, Upsert*Request — add fundingSourceId; FundingSource type)
- frontend/src/components/ui/MoneyInput.tsx and Toast (reuse)

Working branch: release/1.4.3. Create feature/v1.4.3-ral-XXX-allocation-page-fund-sources off
release/1.4.3 and open the PR against release/1.4.3 (NOT main).

1. lib/allocation.ts + types: thread fundingSourceId through ceiling/allocation GET+PUT; add
   getCeilings(officeId, fiscalYear) returning all active funds' ceilings.
2. Tab 1 "Ceiling & Division Allocation": load active fund sources + all funds' ceilings/allocations;
   render one ceiling+allocation section per active fund source (General Fund first). General Fund
   expanded by default; other funds as collapsible cards with a one-line summary (ceiling set/not
   set, Σ allocated) until expanded. Each section reuses the existing ceiling input + AllocationBar +
   division table, with saves carrying fundingSourceId.
3. Keep Tab 2 (PPA → Division) exactly as-is.
4. Loading states must not cause layout shift (render the section shells + skeletons); fetch /auth/me
   via the shared context (do not add per-section /auth/me calls).

Do NOT fund-scope Tab 2. Do NOT hardcode the fund-source list — read active funding_sources from
config (§2 D1).

When done, commit with:
feat(budget-planning): per-fund-source ceiling & allocation on Allocation page (RAL-155)
```

---

## Ticket C — WFP entry: fund-source default, hints & multi-fund bars (frontend) · RAL-156

```
Read CLAUDE.md, PROJECT_DOCUMENTATION_NET_AZURE.md, and PPDO_PROJECT_CONTEXT.md.
Read docs/v1.4.3/v1.4.3_Requirements.md FULLY — especially §5.2 (D5 default, D6 bars, D9 hints).
Blocked by Ticket A (WfpCeilingStatusDto per-fund-source breakdown).

Read these files before writing code:
- frontend/src/app/(portal)/budget-planning/wfp/entry/page.tsx (resolveDefaultFundingSourceId ~105-120; useCeilingStatus ~155-210; the expenditure wizard fund-source <Lookup> ~546-559; sticky ceiling header ~1288-1314)
- frontend/src/lib/allocation.ts (getCeilingStatus — now returns per-fund list)
- frontend/src/types (WfpCeilingStatusDto — add the per-fund-source list)

Working branch: release/1.4.3. Create feature/v1.4.3-ral-XXX-wfp-entry-fund-source off
release/1.4.3 and open the PR against release/1.4.3 (NOT main).

1. resolveDefaultFundingSourceId: match the AIP snapshot against each fund source's Code, Name, AND
   the new aliases column (RAL-157) after normalizing (trim, collapse whitespace/newlines,
   case-insensitive, ignore spaces around %). Single resolvable AIP fund source → that source;
   blank/none → General Fund; ambiguous (comma/slash/newline combination) → null (unselected). Put
   "blank → GF" behind a named constant (DEFAULT_FUND_SOURCE_CODE = "GF") and a
   FALLBACK_AMBIGUOUS_TO_DEFAULT = false flag so option-a can be enabled later without a refactor.
2. Hints (display-only, not persisted): in the expenditure popup's Fund Source field, show a note
   when (a) the value was auto-defaulted to GF because the activity had no fund source, or (b) the
   selected fund source differs from the activity's AIP-assigned fund source.
3. Allocation bars: sticky header shows the GF allocation bar always, PLUS the activity's own
   resolved AIP fund-source bar when it is a single non-GF source (e.g. GAD → show GF + GAD), PLUS a
   bar for any other fund source that has ≥1 expenditure on the selected activity — driven by the
   per-fund WfpCeilingStatusDto.
4. useCeilingStatus: compare the pending amount against the SELECTED fund source's allocation
   remaining (matching the server's fund-scoped block-on-save), not a single aggregate allocation.

Do NOT persist the hint text. Do NOT change the AIP-budget banner logic (still aggregate). Keep the
save-button-disable behaviour consistent with the server check.

When done, commit with:
feat(budget-planning): WFP entry fund-source default, hints & per-fund allocation bars (RAL-156)
```

---

## Ticket D — Funding source config: aliases/other-names column + AIP matching · RAL-157

```
Read CLAUDE.md, PROJECT_DOCUMENTATION_NET_AZURE.md, and PPDO_PROJECT_CONTEXT.md.
Read docs/v1.4.3/Funding_Source_Aliases.md FULLY — AIP naming analysis, normalization/matching
rule, and the seed alias data. Also read docs/v1.4.3/v1.4.3_Requirements.md §5.2 (D5).
Blocked by RAL-154 (shares backend line). BLOCKS RAL-156 (its resolver consumes these aliases).

Read these files before writing code:
- backend/PPDO.Domain/Entities/FundingSource.cs (add Aliases, nullable string, pipe-delimited)
- backend/PPDO.Infrastructure/Data/Configurations/FundingSourceConfiguration.cs (map aliases column)
- backend/PPDO.Application/DTOs/Config/FundingSourceDto.cs (add Aliases to read + upsert DTOs)
- backend/PPDO.Application/Services/FundingSourceService.cs (CsvHeaders currently code,name,description,color,is_active — add aliases; Export/Import round-trip; upsert mapping)
- backend/PPDO.Functions/Functions/ConfigFundingSourceFunctions.cs (DTO passthrough only)
- frontend/src/app/(portal)/config/funding-sources/page.tsx (add Aliases input + list column)
- frontend/src/types (FundingSource + upsert request add aliases)
- backend/PPDO.Tests/Application/FundingSourceServiceTests.cs (extend)

Working branch: release/1.4.3. Create feature/v1.4.3-ral-157-funding-source-aliases off
release/1.4.3 and open the PR against release/1.4.3 (NOT main).

TDD: extend FundingSourceServiceTests (CSV export includes aliases; import parses pipe-delimited
aliases; upsert persists/updates aliases), then implement.

1. Migration AddFundingSourceAliases: add nullable aliases column to funding_sources.
2. Domain + EF: FundingSource.Aliases (string?); map column in FundingSourceConfiguration.
3. DTOs: add Aliases to FundingSourceDto + UpsertFundingSourceDto.
4. Service: add aliases to CsvHeaders; round-trip Export/Import; map in create/update.
5. Config UI: Aliases input (pipe-delimited; hint that /-combinations aren't aliased) + list column.
6. Seed aliases from Funding_Source_Aliases.md §3 (align Codes with reconciled config per O2).

Do NOT resolve multi-fund (/-separated) AIP values — they stay unselected. Do NOT alias external
labels (DOH, NGAs, NGA (ER 1-94), TIEZA, TESDA, NDRRMC, DA-BAFE, PHILMEC, Brgy. Aid, Outsource) to
any PPDO fund. Do NOT touch the Purchase Requests module.

When done, commit with:
feat(config): add aliases column to funding sources for AIP fund-source matching (RAL-157)
```
