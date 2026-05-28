# GitHub Copilot Instructions — PPDO Portal

## Project Summary

PPDO Portal & Inventory System — a web portal for the Provincial Planning and Development Office (PPDO), Occidental Mindoro, Philippines.

**Stack:** .NET 9 (Azure Functions isolated worker) + Next.js 14 (TypeScript, App Router) + Azure SQL (Entity Framework Core 9)  
**Architecture:** Serverless Clean Architecture — Domain → Infrastructure → Application → Functions → Frontend

## Commit Message Format

Always use Conventional Commits format:

```
<type>(<scope>): <description>
```

Types:
- `feat` — new feature
- `fix` — bug fix
- `chore` — tooling, config, dependencies, devops (no production logic)
- `test` — adding or updating tests
- `docs` — documentation only
- `refactor` — code change that neither fixes a bug nor adds a feature

Scopes (use the most specific one that applies):
- `auth` — JWT, login, refresh token
- `inventory` — purchase requests, deliveries, items
- `permissions` — RBAC, permission groups, roles
- `users` — user management
- `db` — migrations, seeding, EF Core config
- `functions` — Azure Functions host, Program.cs, DI
- `frontend` — Next.js pages, components
- `deploy` — CI/CD, GitHub Actions, Azure config
- `deps` — NuGet or npm dependency updates
- `devops` — infra, monitoring, Application Insights

Examples:
```
feat(auth): add JWT refresh token rotation
feat(inventory): add Create PR endpoint with FluentValidation
fix(permissions): resolve group override null check
chore(deps): upgrade ClosedXML to 0.104.1
chore(devops): add Application Insights to Functions host (RAL-32)
test(auth): add unit tests for JWT validation service
```

Always reference the Linear issue in the description when applicable: e.g. `(RAL-42)`.

## Branch Naming

```
feature/vX.Y-ral-<issue>-<short-description>
fix/vX.Y-ral-<issue>-<short-description>
```

## Key Directories

- `backend/PPDO.Domain/` — entities, interfaces, enums (no dependencies)
- `backend/PPDO.Infrastructure/` — AppDbContext, repositories, migrations, Excel service
- `backend/PPDO.Application/` — services, DTOs, validators, settings
- `backend/PPDO.Functions/` — HTTP-triggered Azure Function handlers, Program.cs
- `backend/PPDO.Tests/` — xUnit + Moq unit tests
- `frontend/src/app/` — Next.js 14 App Router pages
- `frontend/src/components/` — shared React components
- `frontend/src/lib/` — Axios API client, utilities
- `.github/workflows/` — CI (`ci.yml`) and deploy (`deploy.yml`) pipelines
