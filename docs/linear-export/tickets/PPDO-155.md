---
id: PPDO-155
title: 'Demo 2.15 T2 — Investment proposal service + API: lifecycle, scope, concurrency'
status: Done
project: PPDO Portal
labels: 
milestone: Demo 2 — change requests & fixes
created: 2026-09-29
completed: 2026-10-01
parent: PPDO-153
blocked_by: PPDO-154, PPDO-173
related: PPDO-174, PPDO-195
url: https://linear.app/ralphoksiprojects/issue/PPDO-155
uuid: b246c7b0-58d2-475e-bd8b-47e9cc30418c
---

# PPDO-155 — Demo 2.15 T2 — Investment proposal service + API: lifecycle, scope, concurrency

**Spec:** `docs/v1.8/Investment_Proposal_Spec.md` §2–4 · **Parent:** [PPDO-153](https://linear.app/ralphoksiprojects/issue/PPDO-153/demo-215-pgom-investment-proposal-generate-the-word-proposal-from-the) · **Prompts file:** `docs/v1.8/Investment_Proposal_Ticket_Prompts.md`

**Not a manual-implementation candidate:** `PermissionService` + office/division scope — a missing check isn't caught by the compiler.

## Implementation prompt

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
