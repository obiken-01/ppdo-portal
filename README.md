# PPDO Portal & Inventory System

> Web portal for the Provincial Planning and Development Office (PPDO), Occidental Mindoro, Philippines.
> Inventory monitoring is the first major feature — tracking purchase requests, deliveries, and stock levels.

---

## Status

| Version | Milestone | Status |
|---|---|---|
| v0.1 | Project Setup & Foundation | 🔄 In Progress |
| v1.0 | Core Portal & Inventory Monitoring | 📋 Planned |
| v1.1 | Employee Profiles | 📋 Planned |
| v1.2 | Calendar & Announcements | 📋 Planned |

---

## Tech Stack

| Layer | Technology |
|---|---|
| Backend API | .NET 9 — Azure Functions (Consumption plan) |
| Frontend | Next.js 14 — TypeScript, Tailwind CSS, shadcn/ui |
| Database | Azure SQL Database (SQL Server 2022) — Free tier |
| ORM | Entity Framework Core 9 |
| Auth | ASP.NET Core Identity + JWT |
| Excel | ClosedXML (.xlsx export) |
| Hosting | Azure Static Web Apps + Azure Functions + Azure SQL — **free forever** |
| CI/CD | GitHub Actions |

---

## Project Structure

```
ppdo-portal/
├── backend/
│   ├── PPDO.Domain/          # Entities, Interfaces, Enums
│   ├── PPDO.Infrastructure/  # EF Core, Repositories, ExcelService
│   ├── PPDO.Application/     # Services, DTOs, Validators
│   ├── PPDO.Functions/       # Azure Functions HTTP triggers (API)
│   └── PPDO.Tests/           # xUnit + Moq
├── frontend/
│   └── src/
│       ├── app/              # Next.js App Router pages
│       ├── components/       # React components
│       ├── lib/              # Axios client, auth helpers
│       └── types/            # TypeScript interfaces
├── docs/                     # Standards and conventions
├── CLAUDE.md                 # Claude Code instructions
└── PROJECT_DOCUMENTATION_NET_AZURE.md  # Full technical spec
```

---

## Local Development

### Prerequisites

| Tool | Install |
|---|---|
| .NET 9 SDK | https://dotnet.microsoft.com/download |
| Node.js (LTS) | https://nodejs.org |
| Azure Functions Core Tools v4 | `npm install -g azure-functions-core-tools@4 --unsafe-perm true` |
| Azure SWA CLI | `npm install -g @azure/static-web-apps-cli` |
| SQL Server Express | https://www.microsoft.com/en-us/sql-server/sql-server-downloads |
| SSMS | https://aka.ms/ssms |

### First-Time Setup

**1. Clone the repo**
```bash
git clone https://github.com/[username]/ppdo-portal.git
cd ppdo-portal
```

**2. Create backend config** — copy the example and fill in values
```bash
cp backend/PPDO.Functions/local.settings.json.example backend/PPDO.Functions/local.settings.json
```

**3. Create frontend config** — copy the example
```bash
cp frontend/.env.example frontend/.env.local
```

**4. Apply database migrations**
```bash
cd backend
dotnet ef database update --project PPDO.Infrastructure --startup-project PPDO.Functions
```

**5. Install frontend dependencies**
```bash
cd frontend
npm install
```

### Running Locally

Open three terminals:

```bash
# Terminal 1 — Backend API
cd backend/PPDO.Functions
func start
# → http://localhost:7071/api

# Terminal 2 — Frontend
cd frontend
npm run dev
# → http://localhost:3000

# Terminal 3 — Full app via SWA CLI (optional — needed for auth flows)
swa start http://localhost:3000 --api-location http://localhost:7071
# → http://localhost:4280
```

---

## Architecture

**Serverless Clean Architecture** — four layers, deployed as Azure Functions + Azure Static Web Apps.

```
Domain → Infrastructure → Application → Functions → Frontend
```

| Layer | Responsibility |
|---|---|
| Domain | Entities, interfaces, enums — no dependencies |
| Infrastructure | EF Core, repositories, ExcelService, JwtMiddleware |
| Application | Business logic services, DTOs, FluentValidation |
| Functions | HTTP-triggered Azure Functions — thin API handlers |
| Frontend | Next.js pages, React components, Axios API client |

---

## User Roles

| Role | Who | Access |
|---|---|---|
| SuperAdmin | Developer / MIS | Full access — bypasses all permission checks |
| Admin | All 5 division heads | All features by default |
| Staff | Any PPDO employee | Access via PermissionGroup (per division) + individual overrides |
| Observer | Provincial admin / read-only users | Read-only on granted features |

**Divisions:** Administrative, Planning, Research Monitoring & Evaluation (RM), MIS, Special Program (SPD)

---

## Key Features (v1.0)

- 🏠 **Public landing page** — announcements visible without login
- 🔐 **Login + RBAC** — JWT auth, role-based + permission-flag access control
- 📅 **Main Dashboard** — calendar with office events and PH holidays
- 📦 **Inventory Dashboard** — PR status cards, stock alerts, quick actions
- 📋 **Create Purchase Request** — 18-field form with Items Master autocomplete + Excel import
- 🚚 **Receive Delivery** — delivery logging with split-by-division support
- 🗃️ **Items Master** — supply catalog management
- 📊 **PR Report** — 3-section report with Excel export (ClosedXML)
- 📒 **Item Ledger** — running stock totals per item
- 🔍 **PR Register** — full PR list with status filters
- 👤 **User Management** — add users, reset passwords, manage permissions

---

## Development Standards

| Standard | File |
|---|---|
| Naming conventions | `docs/NAMING_CONVENTIONS.md` |
| Testing conventions | `docs/TEST_CONVENTIONS.md` |
| Git conventions | `docs/GIT_CONVENTIONS.md` |
| Bug reporting | `docs/BUG_REPORT_STANDARD.md` |
| Claude Code instructions | `CLAUDE.md` |
| Full technical spec | `PROJECT_DOCUMENTATION_NET_AZURE.md` |

---

## Deployment

Deployed on Azure free tier — **₱0/month**.

| Service | Platform |
|---|---|
| Frontend | Azure Static Web Apps (Free) |
| Backend API | Azure Functions — Consumption plan |
| Database | Azure SQL Database — Free offer (32 GB) |

Push to `main` → GitHub Actions builds and deploys automatically.

See `PROJECT_DOCUMENTATION_NET_AZURE.md` → Section 12 for the full first-deployment guide.

---

## Project Tracking

Linear: https://linear.app/ralphoksiprojects/project/ppdo-portal-bdecba26e877

---

*PPDO Portal — Provincial Planning and Development Office, Occidental Mindoro, Philippines*
