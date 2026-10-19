---
id: PPDO-143
title: 'Demo 2.14 T1 — Backend: default fiscal year setting, host-office gate, resolver'
status: Done
project: PPDO Portal
labels: 
milestone: Demo 2 — change requests & fixes
created: 2026-09-27
completed: 2026-09-28
parent: PPDO-136
blocked_by: (none)
related: (none)
url: https://linear.app/ralphoksiprojects/issue/PPDO-143
uuid: d5dca56d-01eb-4880-8922-801dfae15d4e
---

# PPDO-143 — Demo 2.14 T1 — Backend: default fiscal year setting, host-office gate, resolver

**Spec:** `docs/v1.8/Default_Fiscal_Year_Spec.md` — §2 decisions 3–9, §4, §5, §11. (Currently on branch `claude/adoring-hypatia-lkfdct`; lands on `release/1.8.0` with the spec PR.)

## Scope

1. **Entity + migration ⚠️ MIGRATION** `AddInvestmentPlanningSettings` — new single-row snake_case table `investment_planning_settings` (`id` = 1 with `CHECK (id = 1)`, nullable `default_fiscal_year`, `updated_at`, `updated_by_user_id` FK → users `ON DELETE SET NULL`). Seed row `(1, NULL, NULL, NULL)` via `HasData`. CI does not run migrations — `dotnet ef database update` must be run manually against Azure SQL **before or with** the deploy (without it, `/fiscal-years` breaks, and with it the dashboard, Office Ceilings and Report).
2. **Repository + service** — `IInvestmentPlanningSettingsRepository` (`GetAsync` / `SaveAsync`), `InvestmentPlanningSettingsService` (get / update). Audit via `IAuditService.LogAsync("investment_planning_settings", 1, AuditAction.Update, …)` only when the value changes. Validator `Validators/Config/UpdateDefaultFiscalYearValidator.cs`: null OK, range 2020 … Manila year + 3.
3. **Permission** — `PermissionService.CanManageInvestmentPlanningSettingsAsync`: SuperAdmin → true (any office); otherwise `CanManageConfig` **AND** `OfficeScope.IsHostOfficeUser`. ⚠️ Unlike `CanUploadAipAsync`, Admin does **not** bypass the office check. Add the row to `docs/v1.8/Permission_Matrix.md` + `PermissionMatrixTests`.
4. **Endpoints** — `ConfigInvestmentPlanningFunctions.cs`: `GET` / `PUT /api/config/investment-planning/default-fiscal-year`, `ApiResponse<DefaultFiscalYearDto>` (`defaultFiscalYear`, `updatedAt`, `updatedByName`).
5. **Resolver** — `BudgetPlanningDashboardService.ResolveFiscalYearsAsync`: `requested ?? setting ?? newest AIP year ?? UTC year + 1`. The setting is added to `availableFiscalYears` when it has no AIP record (no duplicate). `FiscalYearsDto` gains `DefaultFiscalYear` (the raw setting, nullable). Sequential awaits — no `Task.WhenAll`.

## TDD (spec §11)

`PermissionMatrixTests` (the office rule per role), `UpdateDefaultFiscalYearValidatorTests`, `InvestmentPlanningSettingsServiceTests`, `BudgetPlanningDashboardServiceTests` (resolver order + union), Functions integration 401/403/200/400.

## Out of scope

Config page UI (T2) and page wiring (T3).

**Not a manual-implementation candidate:** EF migration + permission gate.

Commit: `feat(budget-planning): admin-configurable default fiscal year backend (PPDO-136)`
