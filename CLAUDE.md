# CLAUDE.md — PPDO Portal & Inventory System

> This file is read by Claude Code at the start of every session.
> Read this file AND `PROJECT_DOCUMENTATION_NET_AZURE.md` before doing any work.
> If anything in this file conflicts with the documentation, this file takes precedence.

---

## Project Summary

**PPDO Portal & Inventory System** — a web portal for the Provincial Planning and Development Office (PPDO), Occidental Mindoro, Philippines. Inventory monitoring is the first major feature.

**Stack:** .NET 9 (Azure Functions) + Next.js 14 (TypeScript) + Azure SQL (SQL Server)  
**Architecture:** Serverless Clean Architecture — Domain → Infrastructure → Application → Functions → Frontend  
**Full spec:** `PROJECT_DOCUMENTATION_NET_AZURE.md`  
**Design reference:** `PPDO_PROJECT_CONTEXT.md` (Penpot frames, design tokens, business logic)

---

## Repository Structure

```
ppdo-portal/
├── backend/          ← .NET 9 solution (Visual Studio 2026)
│   ├── PPDO.Domain/
│   ├── PPDO.Infrastructure/
│   ├── PPDO.Application/
│   ├── PPDO.Functions/
│   └── PPDO.Tests/
├── frontend/         ← Next.js 14 (VS Code)
│   └── src/
├── CLAUDE.md         ← this file
├── PROJECT_DOCUMENTATION_NET_AZURE.md
└── PPDO_PROJECT_CONTEXT.md
```

---

## Local Development Environment

### Developer Machine

| Tool | Version | Purpose |
|---|---|---|
| Visual Studio | 2026 Community (18.6.0) | .NET backend development |
| VS Code | latest | Next.js frontend development |
| SQL Server Express | local | Local database |
| SSMS | 22.6.0 | Database management GUI |
| Node.js | installed | Frontend + Azure Functions Core Tools |
| Azure Functions Core Tools | v4 | Run Functions locally (`func start`) |
| Azure SWA CLI | latest | Emulate SWA /api proxy locally |
| Application Insights SDK | auto | Installed via NuGet in PPDO.Functions — no manual setup |

### Install Azure Functions Core Tools (if not yet installed)
```bash
npm install -g azure-functions-core-tools@4 --unsafe-perm true
```

### Install Azure SWA CLI (if not yet installed)
```bash
npm install -g @azure/static-web-apps-cli
```

---

## Running the Project Locally

Always start in this order — backend first, then frontend.

### Terminal 1 — .NET Functions (backend API)
```bash
cd backend/PPDO.Functions
func start
# API available at: http://localhost:7071/api
```

> In Visual Studio: open `backend/PPDO.sln` → set `PPDO.Functions` as startup project → F5

### Terminal 2 — Next.js (frontend)
```bash
cd frontend
npm install      # first time only
npm run dev
# Frontend available at: http://localhost:3000
```

### Terminal 3 — SWA CLI (optional — emulates /api proxy)
```bash
swa start http://localhost:3000 --api-location http://localhost:7071
# Full emulated app at: http://localhost:4280
```

> Use Terminal 3 when testing auth flows or any feature that calls the API through the `/api` proxy. Use Terminal 1 + 2 separately for isolated frontend/backend work.

---

## Local Configuration Files

### backend/PPDO.Functions/local.settings.json
> ⚠️ This file is git-ignored. Never commit it. Create it manually on each machine.

```json
{
  "IsEncrypted": false,
  "Values": {
    "AzureWebJobsStorage": "UseDevelopmentStorage=true",
    "FUNCTIONS_WORKER_RUNTIME": "dotnet-isolated",
    "SqlConnectionString": "Server=.\\SQLEXPRESS;Database=PPDOPortalDev;Trusted_Connection=True;TrustServerCertificate=True;",
    "Jwt__SecretKey": "dev-secret-key-minimum-32-characters-long-replace-in-prod",
    "Jwt__Issuer": "http://localhost:4280",
    "Jwt__Audience": "ppdo-portal",
    "Jwt__AccessTokenExpiryMinutes": "15",
    "Jwt__RefreshTokenExpiryDays": "7",
    "APPLICATIONINSIGHTS_CONNECTION_STRING": ""
  }
}
```

> Leave `APPLICATIONINSIGHTS_CONNECTION_STRING` blank locally — telemetry won't be sent to Azure but console logging via `ILogger<T>` still works. Add the real value if you want to test monitoring locally.

### frontend/.env.local
> ⚠️ This file is git-ignored. Never commit it. Create it manually on each machine.

```env
NEXT_PUBLIC_API_BASE_URL=http://localhost:7071/api
```

> Change to `/api` when running via SWA CLI (localhost:4280).

### First-time database setup
```bash
# From repo root — creates the PPDOPortalDev database and all tables
cd backend
dotnet ef database update \
  --project PPDO.Infrastructure \
  --startup-project PPDO.Functions
```

> SQL Server Express connection string uses `.\SQLEXPRESS` as the server name and Windows Authentication (`Trusted_Connection=True`). No username/password needed locally.

---

## Code Delivery Order

**Always implement in this order.** Never skip ahead — each layer depends on the previous.

```
1. PPDO.Domain          Entities, Interfaces, Enums — no dependencies
2. PPDO.Infrastructure  AppDbContext, Repositories, ExcelService, JwtMiddleware
3. PPDO.Application     Services, DTOs, Validators
4. PPDO.Functions       HTTP-triggered Function handlers
5. frontend/            Next.js pages and components
```

Within each layer, deliver in this order:
```
Interfaces / contracts first → Implementations → Tests
```

---

## Architecture Rules

### General
- **Never put business logic in Function handlers.** Handlers call Application services only — validate input, call service, return response. That's it.
- **Never expose domain entities directly over HTTP.** Always map to DTOs before returning from a Function.
- **Never reference PPDO.Infrastructure from PPDO.Functions directly.** Always go through PPDO.Application.
- **All services must be registered in `Program.cs` via DI.** No `new ServiceName()` anywhere.

### Functions (Backend API)
- One file per feature group: `AuthFunctions.cs`, `PurchaseRequestFunctions.cs`, etc.
- All HTTP triggers use `AuthorizationLevel.Anonymous` — JWT is validated manually in the handler
- JWT validation pattern — always use this exact pattern for protected endpoints:
  ```csharp
  var user = await _jwt.ValidateAsync(req);
  if (user == null)
      return req.CreateResponse(HttpStatusCode.Unauthorized);
  ```
- Permission check pattern — always use `PermissionService` for feature access:
  ```csharp
  if (!await _permissions.CanAccessInventoryAsync(user))
      return req.CreateResponse(HttpStatusCode.Forbidden);
  ```
- Public endpoints (no JWT needed): `POST /api/auth/login`, `POST /api/auth/refresh`, `GET /api/announcements`
- **Never skip the JWT check on a non-public endpoint.** When in doubt, protect it.

### EF Core
- Use `AppDbContext` only in repositories — never in Application services or Functions
- All queries must be async (`ToListAsync`, `FirstOrDefaultAsync`, etc.)
- Never use `Include` chains deeper than 2 levels — write separate queries instead
- Migrations go in `PPDO.Infrastructure/Data/Migrations/`
- Migration naming convention: `PascalCase` description — e.g. `AddPermissionGroups`, `AddRefreshTokenTable`

### RBAC / Permissions
- Effective permission resolution always goes through `PermissionService` — never inline the logic
- `PermissionService.CanAccessInventoryAsync(user)` logic:
  ```
  SuperAdmin or Admin role → return true (always)
  Staff/Observer → return OverrideCanAccessInventory ?? Group.CanAccessInventory
  ```
- Division scope — always apply for Staff and Observer:
  ```csharp
  // Write actions
  if (user.Role is Staff or Observer && dto.Division != user.Division)
      return Forbidden;
  // Read queries
  if (user.Role is Staff or Observer)
      query = query.Where(x => x.Division == user.Division);
  ```

### Logging
- Always inject `ILogger<T>` in Application services — never in Function handlers or Domain entities
- Use structured logging with named parameters — always use `{PropertyName}` format, never string interpolation:
  ```csharp
  // ✅ Correct — structured, searchable in Application Insights
  _logger.LogInformation("PR submitted. PRNo: {PRNo}, Division: {Division}", pr.PRNo, pr.Division);

  // ❌ Wrong — not structured, harder to query
  _logger.LogInformation($"PR submitted: {pr.PRNo}");
  ```
- Log these business events at `LogInformation` level:
  - PR submitted (PRNo, Division, UserId)
  - Delivery received (DeliveryRef, PRNo, UserId)
  - PR status changed (PRNo, OldStatus, NewStatus)
  - User created (UserId, Role, Division)
  - User login success (UserId)
- Log these at `LogWarning` level:
  - Low stock alert (StockNo, ItemName, RemainingQty)
  - Login failed — wrong password (Email — never log passwords)
  - Permission denied — user attempted unauthorized action (UserId, Feature)
- Log these at `LogError` level:
  - All caught exceptions in Application services (include `ex` as first parameter)
  - EF Core save failures
- **Never log:** passwords, JWT tokens, full request bodies, PII beyond UserId

### Frontend (Next.js)
- All pages under `src/app/(portal)/` require authentication — enforced by `layout.tsx` auth guard
- All pages under `src/app/(public)/` are public — no auth guard
- API calls always go through `src/lib/api.ts` (Axios instance) — never use `fetch` directly
- Token refresh is handled automatically by the Axios interceptor in `api.ts` — never handle 401s manually in page components
- Never hardcode colours — always use Tailwind classes mapped to PPDO design tokens in `tailwind.config.ts`
- Component naming: PascalCase files, matching the component name — e.g. `StatCard.tsx`, `PRStatusTable.tsx`

---

## Naming Conventions

### Backend (.NET)
| Element | Convention | Example |
|---|---|---|
| Classes | PascalCase | `PurchaseRequestService` |
| Interfaces | `I` prefix + PascalCase | `IPurchaseRequestService` |
| Methods | PascalCase | `GetAllAsync` |
| Private fields | `_camelCase` | `_service` |
| DTOs | suffix `Dto` | `CreatePRDto`, `PRResponseDto` |
| Validators | suffix `Validator` | `CreatePRValidator` |
| Functions classes | suffix `Functions` | `PurchaseRequestFunctions` |
| EF migrations | PascalCase description | `AddPermissionGroups` |

### Frontend (TypeScript)
| Element | Convention | Example |
|---|---|---|
| Components | PascalCase | `StatCard.tsx` |
| Pages | `page.tsx` (Next.js convention) | `page.tsx` |
| Hooks | `use` prefix + camelCase | `useInventoryStats` |
| API functions | camelCase | `getPurchaseRequests` |
| Types/interfaces | PascalCase | `PurchaseRequest`, `CreatePRRequest` |
| Constants | UPPER_SNAKE_CASE | `API_BASE_URL` |

---

## Branch & Commit Strategy

```
main                              ← production auto-deploys to Azure
feature/vX.Y-short-description   ← all new work
fix/vX.Y-short-description       ← bug fixes
```

Commit message format (Conventional Commits):
```
feat(scope): description
fix(scope): description
chore(scope): description
test(scope): description
docs(scope): description

# Examples:
feat(auth): add JWT refresh token rotation
feat(inventory): add Create PR endpoint with FluentValidation
fix(permissions): resolve group override null check
chore(deps): upgrade ClosedXML to 0.104.1
```

---

## Testing

- Test project: `PPDO.Tests` (xUnit + Moq)
- Test naming: `MethodName_Scenario_ExpectedResult`
  ```
  CreateAsync_WithValidDto_ReturnsPRResponse
  ValidateAsync_WithExpiredToken_ReturnsNull
  ```
- Always write tests for Application services
- Functions handlers are tested via integration tests — not unit tests
- Run all tests:
  ```bash
  cd backend
  dotnet test
  ```

**Coverage targets (from RPDS `TEST_CONVENTIONS.md`):**
- Application/Service layer: **80% minimum**
- Domain/Business logic: **90% minimum**
- Functions handlers: happy path + key error cases
- Build fails if any test fails — enforced in CI

**When to apply TDD:**
- ✅ Always: business logic, validators, auth flows, permission resolution
- ⚠️ After: simple CRUD with no logic
- ❌ Never: migrations, config, boilerplate

**Linear issue linking:**
- Always reference the Linear issue in PR description: e.g. `Closes RAL-24`
- Branch name should include the issue number: `feature/v0.1-ral-24-scaffold-solution`

---

## Key Business Logic (Do Not Change Without Checking PPDO_PROJECT_CONTEXT.md)

These rules come from the Google Sheets v0.4 prototype and must be preserved exactly:

| Rule | Detail |
|---|---|
| PR No. format | `101-1041-GF-YYYY-MM-DD-XXX` (3-digit zero-padded sequence) |
| Delivery Ref format | `DEL-YYYYMMDD-XXXXX` (5-digit random, Manila timezone = UTC+8) |
| Issue Ref format | `ISS-YYYYMMDD-XXXXX-N` |
| PR status transitions | `Open → PartiallyDelivered → FullyDelivered` — triggered automatically on delivery submit |
| Split delivery | One item can be split across multiple divisions — aggregate QtyIssued per division |
| Items Master auto-flag | New items added via Create PR set `IsNewItem = true` pending admin review |
| StockNo ↔ Description lookup | Bidirectional — entering either auto-fills the other from Items Master |
| Long text fields | Program, Project, Activity — `textarea`, min-height 44px, max-height 88px, resize vertical |
| Timezone | Always use Manila time (UTC+8) for generated refs and timestamps |

---

## PermissionGroup Seed Data

Run this seed on first migration. Do not change group names — they are referenced in user creation logic.

| Name | Division | CanAccessInventory | CanAccessReports | CanManageUsers |
|---|---|---|---|---|
| Admin Division Staff | Admin | true | true | false |
| Planning Staff | Planning | false | true | false |
| RM Staff | RM | false | true | false |
| MIS Staff | MIS | false | true | false |
| SPD Staff | SPD | false | true | false |
| Observer Default | — (null) | false | false | false |

---

## Linear Project

**PPDO Portal on Linear:** https://linear.app/ralphoksiprojects/project/ppdo-portal-bdecba26e877

| Milestone | Issues |
|---|---|
| v0.1 — Project Setup & Foundation | RAL-24, RAL-25, RAL-26, RAL-27, RAL-28, RAL-29, RAL-30, RAL-31 |
| v1.0 — Core Portal & Inventory | TBD |
| v1.1 — Employee Profiles | TBD |
| v1.2 — Calendar & Announcements | TBD |

Always reference the Linear issue when committing: `feat(auth): add JWT login endpoint (RAL-32)`

---

## Common Claude Code Session Starters

Use these prompts to begin a focused session:

```
# Start a new feature
Read CLAUDE.md then implement [feature name] as documented in
PROJECT_DOCUMENTATION_NET_AZURE.md. Start with the Domain layer.

# Continue existing work
Read CLAUDE.md. The last session completed [X]. Continue with [Y].
Follow the delivery order in CLAUDE.md.

# Fix a bug
Read CLAUDE.md. Fix the following issue: [describe bug].
Do not change any logic outside the affected area.

# Add a new API endpoint
Read CLAUDE.md then add the [endpoint] endpoint to [FunctionsFile].
Follow the JWT validation and permission check patterns in CLAUDE.md.
```

---

## What NOT to Do

- ❌ Do not use `var` for non-obvious types in C# — be explicit
- ❌ Do not use `dynamic` anywhere
- ❌ Do not put connection strings or secrets in any committed file
- ❌ Do not use `Thread.Sleep` — use `await Task.Delay` 
- ❌ Do not use `DateTime.Now` — use `DateTime.UtcNow` then convert to UTC+8 where needed
- ❌ Do not use `Console.WriteLine` for logging — use `ILogger<T>`
- ❌ Do not create new DbContext instances manually — always inject `AppDbContext`
- ❌ Do not use `any` type in TypeScript — always type explicitly
- ❌ Do not call APIs from Next.js Server Components — use Client Components with Axios via `api.ts`
- ❌ Do not hardcode division names as strings — always use the `Division` enum
- ❌ Do not modify EF Core migrations manually after they have been applied
- ❌ Do not use `Console.WriteLine` for logging — always use `ILogger<T>`
- ❌ Do not use string interpolation in log messages — always use structured `{PropertyName}` parameters
- ❌ Do not log passwords, tokens, or sensitive PII — UserId is fine, email is acceptable, never passwords

---

*CLAUDE.md — PPDO Portal v0.1 — 2026-05-26 — Ralph Armand Alcaide*