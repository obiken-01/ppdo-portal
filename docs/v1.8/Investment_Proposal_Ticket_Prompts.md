---
status: ready — 2026-09-29
version: v1.8.0 (Demo 2.15)
tickets: PPDO-153 (parent), PPDO-154 … PPDO-161
supersedes: —
---

# Demo 2.15 — Investment Proposal: ticket prompts

Written to [TICKET_PROMPT_STANDARD.md](../TICKET_PROMPT_STANDARD.md). Each prompt below is pasted
into its Linear ticket's description. The authoritative spec is
[Investment_Proposal_Spec.md](Investment_Proposal_Spec.md).

| Ticket | Scope | Blocked by |
|---|---|---|
| **PPDO-154** T1 | Data model + migration | — |
| **PPDO-155** T2 | Service + API | PPDO-154 |
| **PPDO-156** T3 | HGDG calculator (hand-code candidate) | PPDO-155 |
| **PPDO-157** T4 | Document model builder | PPDO-155 |
| **PPDO-158** T5 | Word export | PPDO-157 |
| **PPDO-159** T6 | List page + sidebar | PPDO-155, PPDO-145 (`useDefaultFiscalYear`) |
| **PPDO-160** T7 | Editor page | PPDO-155, PPDO-157 |
| **PPDO-161** T8 | Signatory defaults card | PPDO-155, PPDO-144 (settings page) |

> **Why T3 follows T2 (a change from spec §9's first draft).** T3 is the hand-coding candidate, so it
> must not sit on the critical path. T2 ships `attributedGadBudget` as `null`; T3 adds the
> calculator and the one-line wiring.

**Branch convention (all tickets):** feature branch off `release/1.8.0`, PR into `release/1.8.0`
(**NOT `main`**), as for every Demo 2 item.

---

## PPDO-154 — T1 Data model: investment proposal tables + signatory defaults ⚠️ MIGRATION

```
Read CLAUDE.md, PROJECT_DOCUMENTATION_NET_AZURE.md, and PPDO_PROJECT_CONTEXT.md.
Read docs/v1.8/Investment_Proposal_Spec.md FULLY — it is the authoritative spec for this ticket.
This ticket implements §5 (data model) and decisions 23, 25 and 26's schema parts.

Read these files before writing code:
- docs/NAMING_CONVENTIONS.md (new tables are snake_case, mapped from PascalCase C#)
- backend/PPDO.Domain/Entities/InvestmentPlanningSettings.cs and
  backend/PPDO.Infrastructure/Data/Configurations/InvestmentPlanningSettingsConfiguration.cs
  (the table this ticket extends, and the most recent snake_case config to copy)
- backend/PPDO.Infrastructure/Data/Configurations/AccountConfiguration.cs (.ToTable/.HasColumnName pattern)
- backend/PPDO.Domain/Entities/AipProject.cs and AipActivity.cs (the two FKs; AipActivity is a
  legacy PascalCase table — do not touch its columns)
- backend/PPDO.Infrastructure/Data/AppDbContext.cs (DbSet registration)
- docs/v1.8/AIP_Concurrent_Edit_Spec.md §5 (why rowversion, and .IsRowVersion())
- docs/v1.8/Pre_Deployment_Checklist.md (this migration joins its list)

Before changing anything, state in one line each:
- Current behaviour: no investment proposal storage exists; investment_planning_settings holds
  only default_fiscal_year.
- Target behaviour: ten new snake_case tables per spec §5, four nullable signatory-default
  columns on investment_planning_settings, one migration.

Working branch: release/1.8.0.
Create feature/v1.8.0-ppdo-154-investment-proposal-data-model off release/1.8.0 and open the PR
against release/1.8.0 (NOT main).

TDD: n/a for the migration itself (CLAUDE.md: never TDD migrations). Step 4 adds a repository
round-trip test.

1. Domain: entities InvestmentProposal, InvestmentProposalBeneficiary, …Benefit, …Logframe,
   …Group, …WorkPlanRow, …TeamMember, …CapacityTraining, …Monitoring, …Risk, with the columns in
   spec §5. InvestmentProposal.RowVersion is byte[]. Add PpdcName, PpdcPosition, LceName,
   LcePosition (string?) to InvestmentPlanningSettings.
   Verify: dotnet build backend/PPDO.slnx
2. Infrastructure: one IEntityTypeConfiguration per entity (.ToTable/.HasColumnName snake_case).
   Enforce: unique aip_project_id; investment_proposals.aip_project_id ON DELETE NO ACTION;
   children cascade from investment_proposals; work_plan_rows.aip_activity_id ON DELETE CASCADE;
   work_plan_rows.group_id ON DELETE NO ACTION (multiple-cascade-path rule, spec §5 note);
   filtered unique (proposal_id, aip_activity_id) WHERE aip_activity_id IS NOT NULL;
   check constraint aip_activity_id IS NOT NULL OR name IS NOT NULL; unique (proposal_id, sector)
   on benefits and (proposal_id, level) on logframe; .IsRowVersion() on row_version;
   direct_same_as_summary default 1. Register DbSets.
   Verify: dotnet build
3. Migration AddInvestmentProposals.                                       ⚠️ MIGRATION
   dotnet ef migrations add AddInvestmentProposals --project PPDO.Infrastructure
     --startup-project PPDO.Functions
   Read the generated Up() line by line. It must create only the new tables/indexes and add the
   four settings columns — no drop, no rename, no change to any AIP table.
   Verify: dotnet ef database update against a LOCAL database with the current release/1.8.0
   schema (SQL Server reports multiple cascade paths only at update time, not at migrations add).
   Then dotnet ef database update <previous migration> to prove Down() runs.
4. Repository: IInvestmentProposalRepository + InvestmentProposalRepository with the methods
   named in spec §5 "Repository" (scoped queries only — no GetAllAsync-then-filter; AnyAsync for
   existence; CountAsync for the list total; Include depth ≤ 1). Register in Program.cs.
   Verify: a repository test that saves a proposal with one row in every child table, reloads
   it, and deletes it (children gone) — dotnet test --filter InvestmentProposalRepository
5. Add AddInvestmentProposals to docs/v1.8/Pre_Deployment_Checklist.md, in migration order,
   after AddInvestmentPlanningSettings.
   Verify: the checklist lists it with "run before the code deploys".

Risks and rollback:
- Cascade-path error only surfaces at database update → caught by step 3's local update; fix the
  FK, re-generate the migration (it is unapplied, so re-generating is allowed).
- Migration applied to Azure SQL and must be undone → dotnet ef database update
  <AddInvestmentPlanningSettings migration id>; the new tables are empty on first deploy, so Down()
  loses nothing. After real proposals exist, Down() drops them — take a backup first.

Stop and get my sign-off before: running this migration against any shared or Azure database.

Do NOT: add services, endpoints or UI (T2+); touch AipActivity/AipProject columns; rename any
existing table; seed data.

When done, commit with:
feat(investment-proposal): data model and AddInvestmentProposals migration (PPDO-154)
```

---

## PPDO-155 — T2 Service + API: lifecycle, scope, concurrency

```
Read CLAUDE.md, PROJECT_DOCUMENTATION_NET_AZURE.md, and PPDO_PROJECT_CONTEXT.md.
Read docs/v1.8/Investment_Proposal_Spec.md FULLY — it is the authoritative spec for this ticket.
This ticket implements §2 (decisions 1–26 except the Word export), §3 (every behaviour and role
row) and §4 (every endpoint except /export).
Read docs/v1.8/Permission_Matrix.md FULLY, especially §3 (scope) and §4a (the host-office
department-head trap), and docs/PERFORMANCE_GUIDELINES.md.

Read these files before writing code:
- backend/PPDO.Application/Common/BudgetPlanningScope.cs, OfficeScope.cs, DivisionScope.cs
  (the read scope; ResolveForReview is the cross-office reviewer's read path)
- backend/PPDO.Application/Common/ReviewerWriteGuard.cs and
  backend/PPDO.Functions/Functions/ConfigHttp.cs (AuthorizeWriteAsync — every write goes through it)
- backend/PPDO.Application/Services/PermissionService.cs, backend/PPDO.Domain/Interfaces/IPermissionService.cs
- backend/PPDO.Tests/Application/PermissionMatrixTests.cs (a new method needs a row, or the build fails)
- docs/v1.8/Division_Submit_Spec.md decisions 5–6 and backend/PPDO.Tests/Application/AipExpenditureReadScopeTests.cs
  (which programs a division encoder sees — the proposal uses the same read scope)
- backend/PPDO.Application/Services/AipService.cs (DeleteProjectAsync — gains a 409) and
  backend/PPDO.Infrastructure/Repositories/AipRepository.cs (activities/expenditures/items by project)
- backend/PPDO.Application/Common/AipFiscalYears.cs (IsEntered — the FY2028 gate)
- backend/PPDO.Application/Services/AnnouncementService.cs (HtmlSanitizer usage to copy and restrict)
- backend/PPDO.Application/Services/InvestmentPlanningSettingsService.cs and
  backend/PPDO.Functions/Functions/ConfigInvestmentPlanningFunctions.cs (settings read/write + audit pattern)
- backend/PPDO.Functions/Functions/AipConsolidatedFunctions.cs (a recent budget-planning Functions
  file: JWT → permission → scope → service → ApiResponse)
- backend/PPDO.Application/Common/ServiceResult.cs
- backend/PPDO.Tests/Functions/ReviewerWriteGuardCoverageTests.cs

Before changing anything, state in one line each:
- Current behaviour: no proposal endpoints; deleting an AIP project never checks for a proposal.
- Target behaviour: the §4 endpoints with §3.2's per-role scope, row-version 409s, Final lock,
  and a 409 on deleting an AIP project that has a proposal.

Working branch: release/1.8.0.
Create feature/v1.8.0-ppdo-155-investment-proposal-service-api off release/1.8.0 and open the PR
against release/1.8.0 (NOT main).

TDD: write failing tests first in InvestmentProposalServiceTests (new), PermissionMatrixTests,
AipServiceTests and ReviewerWriteGuardCoverageTests, then implement.

1. DTOs in PPDO.Application/DTOs/InvestmentProposal/: ProposalListItemDto (exact §4 fields),
   ProposalDto (header, warnings, content, aipRows, rowVersion as base64), UpdateProposalDto,
   RowVersionDto, SignatoryDefaultsDto. Validators in Validators/InvestmentProposal/ (RAL-235
   folder rule) with the §4 validation table's rules and messages verbatim.
   Verify: dotnet test --filter UpdateProposalValidatorTests
2. Permission: PermissionService.CanReopenInvestmentProposalAsync(user, officeId) — SuperAdmin →
   true; Admin → true only when host office; CanReviewBudgetPlanning → true only when
   users.office_id == officeId (NOT OfficeScope.Resolve — §4a trap). Matrix row in
   Permission_Matrix.md + PermissionMatrixTests, including
   HostOfficeDeptHead_CannotReopenAnotherOffice.
   Verify: dotnet test --filter PermissionMatrixTests
3. InvestmentProposalService (+ interface, registered in Program.cs; ILogger<T>, structured logs):
   ListAsync, CreateAsync (pre-fill decisions 11–12 + 19 once), GetAsync (live merge while Draft —
   append unstored activities ungrouped; snapshot while Final; aipChangedSinceFinal), UpdateAsync
   (replace-all children, sanitize rich text to the decision-20 allow-list, row version),
   FinalizeAsync (write snapshot_json), ReopenAsync (clear it), DeleteAsync (Draft only).
   Header per decision 6 (schedule min/max in CALENDAR month order, not alphabetical); amounts in
   full pesos (decision 7, no rounding, no +30%); attributedGadBudget = null (T3 wires it).
   Out-of-scope and missing both return NotFound("Proposal not found.").
   Stale RowVersion → DbUpdateConcurrencyException → Conflict with { currentRowVersion,
   updatedByName, updatedAt }. Log "Investment proposal finalized. ProposalId: {ProposalId},
   AipProjectId: {AipProjectId}, UserId: {UserId}" at Information; never log content.
   Sequential awaits only — no Task.WhenAll on the shared DbContext.
   Verify: dotnet test --filter InvestmentProposalServiceTests (spec §11 list, every bullet)
4. AipService.DeleteProjectAsync → Conflict("This project has an investment proposal. Delete the
   proposal first.") when IInvestmentProposalRepository.ExistsForProjectAsync is true.
   Verify: dotnet test --filter AipServiceTests
5. Signatory defaults: extend InvestmentPlanningSettingsService with get/update of the four
   columns (audited only on change, as the fiscal-year setting is).
   Verify: dotnet test --filter InvestmentPlanningSettingsServiceTests
6. Functions: InvestmentProposalFunctions.cs with the §4 routes (all AuthorizationLevel.Anonymous
   + _jwt.ValidateAsync + CanAccessBudgetPlanningAsync; every write via
   ConfigHttp.AuthorizeWriteAsync; reopen also CanReopenInvestmentProposalAsync). Add the
   signatory-defaults GET/PUT to ConfigInvestmentPlanningFunctions.cs (PUT gated on
   CanManageInvestmentPlanningSettingsAsync; GET also allowed with CanAccessBudgetPlanning).
   Camel-case JSON, ApiResponse<T> envelope. Add the five write routes to
   ReviewerWriteGuardCoverageTests.
   Verify: func start, then with a guest-office Staff token: POST a create for another office's
   project → 404; GET list → only own office; with a cross-office reviewer token: GET 200,
   PUT 403.

Risks and rollback:
- Scope leak (a guest office reads another office's proposal) → the red-tests in step 3 must fail
  first against a deliberately unscoped query, then pass. Rollback: revert the PR (no migration in
  this ticket).
- Reviewer write slips past the guard → ReviewerWriteGuardCoverageTests covers every route.
- Snapshot shape drift between Final proposals → snapshot_json is versioned with
  "schemaVersion": 1; readers must accept version 1 forever.

Stop and get my sign-off before: merging the PermissionService change and the scope logic
(auth/permissions + office scope).

Do NOT: build the Word export (T5) or the HGDG calculation (T3); let division submit
(PPDO-130) lock proposals (spec §3.2 says it doesn't); allow editing AIP data from a proposal.

When done, commit with:
feat(investment-proposal): proposal service, scope and API (PPDO-155)
```

---

## PPDO-156 — T3 HGDG score → attributed GAD budget calculator (hand-code candidate)

> **Manual-implementation candidate for Ralph** (CLAUDE.md "Flag manual-implementation
> candidates"): pure function, tiny blast radius, feedback entirely via `dotnet test`, and
> `WfpExpenditureCalculator` is the sibling to copy the shape from. **Guidance ladder level 1:**
> this spec + that sibling. Escalate only on request. Handing it back is a normal outcome.
>
> ⚠️ **Merge gate:** confirm the score bands and percentages (spec decision 9) against the PCW
> HGDG manual first. Starting before that is fine.

```
Read CLAUDE.md.
Read docs/v1.8/Investment_Proposal_Spec.md decision 9 and §11 (HgdgAttributionTests) FULLY.

Read these files before writing code:
- backend/PPDO.Application/Common/WfpExpenditureCalculator.cs (the sibling: a static, pure
  calculator in Application/Common)
- backend/PPDO.Tests/Application/WfpExpenditureCalculatorTests.cs (its test shape)
- the InvestmentProposalService from PPDO-155 (where attributedGadBudget is set to null today)

Before changing anything, state in one line each:
- Current behaviour: attributedGadBudget is always null.
- Target behaviour: score present → Project Cost × band %; score null → null.

Working branch: release/1.8.0.
Create feature/v1.8.0-ppdo-156-hgdg-attribution off release/1.8.0 and open the PR against
release/1.8.0 (NOT main).

TDD: write HgdgAttributionTests first — every boundary in decision 9 — then implement.

1. HgdgAttribution (static, Application/Common): Percent(decimal? score) → int? and
   AttributedBudget(decimal? score, decimal projectCost) → decimal? (2 decimals,
   MidpointRounding.AwayFromZero).
   Verify: dotnet test --filter HgdgAttributionTests
2. Wire it into InvestmentProposalService where the header is built; the HGDG checklist code list
   (HgdgChecklists constant) lives next to it.
   Verify: dotnet test --filter InvestmentProposalServiceTests still passes, plus one new test:
   score 8, cost 200000 → 100000.00

Risks and rollback: wrong bands → fix the constants; no migration, revert is safe.

Do NOT: compute the score from checklist answers (spec §7 non-goal).

When done, commit with:
feat(investment-proposal): HGDG attributed GAD budget calculator (PPDO-156)
```

---

## PPDO-157 — T4 Proposal document model builder

```
Read CLAUDE.md, PROJECT_DOCUMENTATION_NET_AZURE.md, and PPDO_PROJECT_CONTEXT.md.
Read docs/v1.8/Investment_Proposal_Spec.md FULLY — authoritative. This ticket implements
decisions 6, 13–18 and 21 as a pure, testable model (no Open XML).
Read docs/v1.8/Investment_Proposal_Findings.md §8.2 (how real proposals lay out H-1).

Read these files before writing code:
- backend/PPDO.Application/Common/AipFormRowBuilder.cs (the Annex B equivalent: a pure builder the
  Excel service renders — copy its separation, not its rounding)
- backend/PPDO.Application/Services/AipConsolidatedService.cs (how the builder is fed)
- backend/PPDO.Domain/Entities/AipExpenditure.cs and AipProcurementItem.cs (snapshot columns only)
- the ProposalDto from PPDO-155 (the builder's input)

Before changing anything, state in one line each:
- Current behaviour: no document model exists.
- Target behaviour: InvestmentProposalDocumentBuilder.Build(ProposalDto) → an ordered,
  render-ready model of every section, with G and H-1 sharing one grouping.

Working branch: release/1.8.0.
Create feature/v1.8.0-ppdo-157-proposal-document-builder off release/1.8.0 and open the PR against
release/1.8.0 (NOT main).

TDD: InvestmentProposalDocumentBuilderTests first — every builder bullet in spec §11.

1. Model types (Application/DTOs/InvestmentProposal/Document/): ProposalDocument, SectionA rows,
   WorkPlanRow (group header | row), AnnexH1Row (group header | activity | expenditure heading |
   computation line | total), RichTextBlock (paragraph/list items with bold/italic runs parsed
   from the allow-listed HTML).
2. Builder: grouping + order shared by G and H-1; ungrouped after grouped; empty groups skipped;
   proposal-only rows blank in H-1; computation line
   "{Name}  {UnitPrice:N2} x {Qty} {Unit}[ x {NumberOfDays} days] = {LineTotal:N2}" (days part only
   when NumberOfDays ≠ 1); expenditure with no items → heading + amount; activity with no lines →
   one totals row; amounts in the PS/MOOE/CO column; fund = FundingSourceSnapshot code; grand
   total; Section M "N/A" when empty; F-Direct = A's rows while directSameAsSummary.
   Verify: dotnet test --filter InvestmentProposalDocumentBuilderTests
3. Invariant test: for a seeded proposal, H-1 grand total == header.projectCost (to the centavo).
   Verify: same filter.

Risks and rollback: pure code, no migration — revert is safe.

Do NOT: reference DocumentFormat.OpenXml here (Application stays renderer-agnostic); round
amounts or apply +30% (decision 7).

When done, commit with:
feat(investment-proposal): document model builder for G and Annex H-1 (PPDO-157)
```

---

## PPDO-158 — T5 Word export: template + Open XML renderer + endpoint

> **Not a manual-implementation candidate:** layout-heavy Open XML with a slow feedback loop, the
> same reasons `ExcelService.cs` is excluded.

```
Read CLAUDE.md, PROJECT_DOCUMENTATION_NET_AZURE.md, and PPDO_PROJECT_CONTEXT.md.
Read docs/v1.8/Investment_Proposal_Spec.md decision 27, §4 (export) and §8 FULLY, and
docs/v1.8/Investment_Proposal_Findings.md §8.1 (measured page setup, fonts, letterhead).

Read these files before writing code:
- backend/PPDO.Application/Services/IAipFormExcelService.cs and
  backend/PPDO.Infrastructure/Services/AipFormExcelService.cs (interface in Application,
  implementation in Infrastructure — mirror exactly)
- backend/PPDO.Functions/Functions/AipConsolidatedFunctions.cs (the /export route: headers,
  Content-Disposition, file response)
- backend/PPDO.Infrastructure/PPDO.Infrastructure.csproj (package references)
- the ProposalDocument model from PPDO-157

Before changing anything, state in one line each:
- Current behaviour: proposals cannot be exported.
- Target behaviour: GET /api/budget-planning/proposals/{id}/export returns a valid .docx built
  from the template and the PPDO-157 model.

Working branch: release/1.8.0.
Create feature/v1.8.0-ppdo-158-proposal-word-export off release/1.8.0 and open the PR against
release/1.8.0 (NOT main).

TDD: InvestmentProposalWordServiceTests first (opens with WordprocessingDocument.Open, zero
OpenXmlValidator errors, contains the title and the expected H-1 rows).

1. Dependency: explicit PackageReference DocumentFormat.OpenXml in PPDO.Infrastructure, pinned to
   the version ClosedXML 0.104.2 already resolves (read obj/project.assets.json).
                                                                    ⚠️ NEW DEPENDENCY (explicit pin only)
   Verify: dotnet build; dotnet list package --include-transitive shows one OpenXml version.
2. Template: PPDO.Infrastructure/Templates/InvestmentProposalTemplate.docx (embedded resource) —
   A4, 1009-twip margins, Verdana 10 pt, header = seal + four letterhead lines + Bagong Pilipinas
   logo, footer "Page {PAGE} of {NUMPAGES}", empty body. Compress the seal image to < 200 KB first.
   Verify: open the template in Word; the file is < 400 KB.
3. InvestmentProposalWordService: render the model into the body — title, sections A–M in template
   order, tables with the template's column headings, H-1 with PS | MOOE | CO | Total | Source of
   Fund, rich-text runs as paragraphs/lists, signature block (3 slots). No guidance text.
   Verify: dotnet test --filter InvestmentProposalWordServiceTests
4. Endpoint: GET /api/budget-planning/proposals/{id}/export — same JWT/permission/scope/404 as
   GET; read-only (no write guard). Filename per spec §3.1, sanitized, ≤ 120 chars, RFC 5987
   filename*.
   Verify: func start; export a seeded FY2028 proposal; open in Word — no repair prompt; compare
   against spec §10 "Export" lines one by one.

Risks and rollback:
- Word "repair" prompt from malformed XML → OpenXmlValidator in the tests; do not merge with
  validator errors.
- Large embedded images bloat every export → step 2 size check.
- Revert is safe (no migration).

Stop and get my sign-off before: adding the package reference (new dependency in the csproj).

Do NOT: generate PDF; render guidance text; round amounts.

When done, commit with:
feat(investment-proposal): Word export of the investment proposal (PPDO-158)
```

---

## PPDO-159 — T6 Investment Proposals list page + sidebar

```
Read CLAUDE.md, PROJECT_DOCUMENTATION_NET_AZURE.md, and PPDO_PROJECT_CONTEXT.md.
Read docs/v1.8/Investment_Proposal_Spec.md §6.1 and §10 "Roles and scope" / "UI states" FULLY.
Read docs/DESIGN_SYSTEM.md and docs/PERFORMANCE_GUIDELINES.md (frontend rules).

Read these files before writing code:
- frontend/src/lib/budget-planning-access.ts and frontend/src/components/layout/Sidebar.tsx
  (the nav rule and the route guard share one function — add the entry in both through it)
- frontend/src/components/ui/DataTable.tsx, TableSkeleton.tsx, RowActions.tsx, StatusPill.tsx,
  OfficeSelect.tsx
- the useDefaultFiscalYear hook from PPDO-145 (fiscal-year default)
- frontend/src/app/(portal)/budget-planning/aip/entry/page.tsx (query-string routing under
  output: "export", and how a budget-planning page reads the current user from context)
- frontend/src/lib/api.ts (all calls go through it)

Before changing anything, state in one line each:
- Current behaviour: no Investment Proposals page or sidebar entry.
- Target behaviour: /budget-planning/proposals lists the year's AIP projects in scope with their
  proposal status, and Create/Open/Export actions per spec §6.1.

Working branch: release/1.8.0.
Create feature/v1.8.0-ppdo-159-investment-proposals-list off release/1.8.0 and open the PR against
release/1.8.0 (NOT main).

1. Types + API functions (lib/investment-proposals.ts): getProposalList, createProposal,
   exportProposal (blob download).
   Verify: npm run lint && npx tsc --noEmit
2. Page /budget-planning/proposals/page.tsx: filters (fiscal year from useDefaultFiscalYear,
   OfficeSelect for host-office users, debounced search), server-side pagination, every §6.1
   state (8-row skeleton, both empty states, FY2027 message, error + Retry, read-only for
   cross-office reviewers — Create hidden). Create → navigate to the editor; 409 → navigate to
   the existing proposal.
   Verify: npm run dev; walk each §6.1 state (throttle the network for the skeleton).
3. Sidebar entry "Investment Proposals" after AIP (emoji icon, per DESIGN_SYSTEM.md).
   Verify: a Staff user without Budget Planning access doesn't see it.

Risks and rollback: frontend only; revert is safe.

Do NOT: fetch /auth/me in the page (read the shared user context); use text-slate-700 or
rounded-lg; build the editor (T7).

When done, commit with:
feat(investment-proposal): Investment Proposals list page (PPDO-159)
```

---

## PPDO-160 — T7 Investment proposal editor page

> **Not a manual-implementation candidate:** a large page component.

```
Read CLAUDE.md, PROJECT_DOCUMENTATION_NET_AZURE.md, and PPDO_PROJECT_CONTEXT.md.
Read docs/v1.8/Investment_Proposal_Spec.md FULLY — §6.2 is the screen, §2 the rules it displays,
§10 the acceptance list you will be tested against.
Read docs/DESIGN_SYSTEM.md and docs/PERFORMANCE_GUIDELINES.md.

Read these files before writing code:
- frontend/src/components/announcements/RichTextToolbar.tsx and AnnouncementEditorModal.tsx
  (the TipTap setup to extract)
- frontend/src/components/ui/Modal.tsx, ConfirmDialog.tsx, MessageDialog.tsx, Toast.tsx,
  Lookup.tsx, RowActions.tsx, StatusPill.tsx, MoneyInput.tsx
- the ProposalDto / UpdateProposalDto from PPDO-155 and the document model from PPDO-157
  (the H-1 preview must match the export's grouping and lines)
- frontend/src/app/(portal)/budget-planning/aip/entry/ (an Investment Planning page with
  read-only states and the shared user context)

Before changing anything, state in one line each:
- Current behaviour: no proposal editor.
- Target behaviour: /budget-planning/proposals/edit?id= edits every section A–M per §6.2, saves
  with the row version, and shows Final/read-only/conflict states.

Working branch: release/1.8.0.
Create feature/v1.8.0-ppdo-160-investment-proposal-editor off release/1.8.0 and open the PR
against release/1.8.0 (NOT main).

1. Extract components/ui/RichTextEditor.tsx from the Announcements toolbar, restricted to bold,
   italic, bullet and ordered lists; switch Announcements to it without changing its toolbar.
   Verify: the Announcements editor still works exactly as before (create + edit an announcement).
2. Editor shell: sticky header (Save, Export Word, Finalize/Reopen, Delete), A–M rail with
   unsaved/error dots, section-shaped skeletons, beforeunload + in-app leave confirm, Ctrl+S.
   Verify: load with network throttled — no layout shift between skeleton and content.
3. Sections A–F, I–M, signatories (spec §6.2 "Section specifics"): computed totals, live GAD
   budget, "Same as Section A" toggle with confirm, D's cost column label.
   Verify: spec §10 "Content rules" lines, one by one.
4. G work-plan editor (ProposalWorkPlanEditor): groups, ↑/↓ ordering, proposal-only steps with
   "Proposal only" tag, AIP rows tagged "From AIP" with read-only name/timeline/OPR.
   H-1 preview (AnnexH1Preview) rendered from the same grouping.
   Verify: spec §10 "Work plan and cost annex" lines.
5. States: 409 stale → MessageDialog with Reload / Keep editing (input kept); Final and
   cross-office-reviewer read-only rendering (text, not disabled inputs; actions hidden);
   activities-without-lines banner; "AIP has changed since finalized" notice.
   Verify: two browser tabs, save in both — the second shows the conflict dialog and keeps text.

Risks and rollback:
- The RichTextEditor extraction touches components/ui → blast radius includes Announcements;
  step 1's check covers it. Frontend only; revert is safe.

Stop and get my sign-off before: merging the components/ui/RichTextEditor extraction (shared
component).

Do NOT: autosave; allow tables/links/colours in rich text; edit AIP-sourced fields; call APIs
from Server Components.

When done, commit with:
feat(investment-proposal): investment proposal editor (PPDO-160)
```

---

## PPDO-161 — T8 Signatory defaults card

```
Read CLAUDE.md.
Read docs/v1.8/Investment_Proposal_Spec.md decisions 19 and 25, §4 (signatory-defaults) and §6.3.

Read these files before writing code:
- the Investment Planning Settings page from PPDO-144 (/config/investment-planning) and its
  default-fiscal-year card — this card sits beside it and copies its states
- the signatory-defaults endpoints from PPDO-155

Before changing anything, state in one line each:
- Current behaviour: PPDC/LCE names can only be typed per proposal.
- Target behaviour: a PPDO config manager sets them once; new proposals default slots 2–3 from them.

Working branch: release/1.8.0.
Create feature/v1.8.0-ppdo-161-signatory-defaults off release/1.8.0 and open the PR against
release/1.8.0 (NOT main).

1. Card with four inputs (PPDC name/position, LCE name/position), Save, the same
   loading/error/success states as the fiscal-year card; hidden without
   CanManageInvestmentPlanningSettings.
   Verify: as a PPDO config manager, save names → create a new proposal → slots 2–3 are filled;
   as a guest-office Admin, the card is not shown.

Risks and rollback: frontend only; revert is safe.

Do NOT: back-fill existing proposals' signatories (defaults apply at creation only).

When done, commit with:
feat(investment-proposal): signatory defaults on Investment Planning Settings (PPDO-161)
```

---

*Investment Proposal ticket prompts — Demo 2.15 — 2026-09-29.*
