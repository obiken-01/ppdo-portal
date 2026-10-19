---
id: PPDO-154
title: 'Demo 2.15 T1 — Data model: investment proposal tables + signatory defaults (⚠️ migration)'
status: Done
project: PPDO Portal
labels: 
milestone: Demo 2 — change requests & fixes
created: 2026-09-29
completed: 2026-10-01
parent: PPDO-153
blocked_by: (none)
related: PPDO-173
url: https://linear.app/ralphoksiprojects/issue/PPDO-154
uuid: ba778a3f-1426-48ef-af17-cdc1e31659d9
---

# PPDO-154 — Demo 2.15 T1 — Data model: investment proposal tables + signatory defaults (⚠️ migration)

**Spec:** `docs/v1.8/Investment_Proposal_Spec.md` §5 · **Parent:** [PPDO-153](https://linear.app/ralphoksiprojects/issue/PPDO-153/demo-215-pgom-investment-proposal-generate-the-word-proposal-from-the) · **Prompts file:** `docs/v1.8/Investment_Proposal_Ticket_Prompts.md`

⚠️ **MIGRATION** `AddInvestmentProposals` — CI does not run migrations; run `dotnet ef database update` manually against Azure SQL **before** the code deploys. Added to `docs/v1.8/Pre_Deployment_Checklist.md` by this ticket.

**Not a manual-implementation candidate:** EF migration.

## Implementation prompt

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
