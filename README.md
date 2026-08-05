# PPDO Portal & Inventory System

> Web portal for the Provincial Planning and Development Office (PPDO), Occidental Mindoro, Philippines.
> Covers inventory monitoring, budget planning (LDIP / AIP / WFP), and office operations.

---

## Status

| Version | Milestone | Status |
|---|---|---|
| v0.1 | Project Setup & Foundation | ✅ Done |
| v1.0 | Core Portal & Inventory Monitoring | ✅ Done |
| v1.0.1 | Security Hardening | ✅ Done |
| v1.1 | Inventory UI Refinements + Distribution | ✅ Done |
| v1.1.1 | Calendar Approval, Announcements, User Profile | ✅ Done |
| v1.2 | Divisions & Permission Model Rework | ✅ Done |
| v1.3 | LDIP Upload + Dashboard Readiness Hub | ✅ Done |
| v1.4 | Budget Planning — WFP Rework (epic) | ✅ Done |
| v1.4.1 – v1.4.3 | WFP Rework follow-ups — fund-scoped ceiling & allocation | ✅ Done |
| v1.4.4 | WFP Report → Excel export (matches PBO form) | ✅ Done |
| v1.4.5 | Budget Planning Dashboard — PPDO-scoped, per-division view | ✅ Done |
| v1.4.6 – v1.4.9 | Budget Planning perf fixes + Audit Log page | ✅ Done |
| v1.5 | PPMP Report — preview + Excel export | ✅ Done |
| v1.6 | AIP Manual Entry, In-Place Editing, Carry-Forward & LDIP Inline Edit | ✅ Done |
| v1.7 | Inventory Optimization — perf rework, Stock Balances, server-side Distribution, GSO PR import, audit logging | ✅ Done |

See [`CLAUDE.md`](CLAUDE.md) for the full delivery history and current architecture rules.

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

Roles: **SuperAdmin**, **Admin**, **Staff**. (The `Observer` role and the old `PermissionGroup`
table were both retired in v1.2 — see below.)

| Role | Who | Access |
|---|---|---|
| SuperAdmin | Developer / MIS | Full access — bypasses all permission checks |
| Admin | Division heads | All features by default |
| Staff | Any PPDO employee | Access via their Division's feature flags + individual overrides |

**Divisions are configurable** (v1.2, RAL-97) — a `divisions` table (FK `users.division_id`)
replaces the old fixed `Division` enum and the `PermissionGroup` table. Each division carries
both a **data-scoping** dimension (which office's/division's budget-planning and inventory data a
Staff user may see) and **feature-permission flags** (`CanAccessInventory`, `CanAccessBudgetPlanning`,
`CanManageUsers`, etc.) — a Staff user's effective access resolves from their division's flags plus
any per-user overrides.

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
- 📒 **Stock Overview** — running stock totals per item
- 🔍 **PR List** — full PR list with status filters
- 👤 **User Management** — add users, reset passwords, manage permissions

## What's New in v1.1 — Budget Planning (LDIP / AIP / WFP)

v1.1 adds the **Budget Planning** module — a web-based replacement for the Province's existing Excel-based LDIP, AIP, and WFP files (currently managed via `.xlsm` files with VBA macros).

### Module Overview

| Document | Full Name | Scope |
|---|---|---|
| **LDIP** | Local/Provincial Development Investment Program | Multi-year (3–6 yrs), all offices |
| **AIP** | Annual Investment Program | Single fiscal year, annual slice of LDIP |
| **WFP** | Work and Financial Plan | Per-department, quarterly expenditure breakdown |

**Hierarchy:** LDIP → AIP (annual slice) → WFP (department execution plan)  
**Legal basis:** RA 7160, DBM LBC 152 (2023), DILG-NEDA-DBM-DOF JMC No. 1 (2016)

### Key Features

- 📂 **Configuration section** — Accounts (Chart of Accounts), Offices, and Funding Sources; each config page supports CSV upload/download, add/edit via modal, and searchable/sortable table
- 📥 **AIP file upload** — import existing `.xlsm` files (4 sector sheets); post-upload summary page before confirming import
- ✏️ **AIP manual entry** — create AIP records directly through the web UI (Office → Program → Project → Activity hierarchy)
- 📊 **WFP entry** — per-office WFP linked to an AIP record; expenditure lines entered via popup (PS / MOOE / CO sections with quarterly amounts, 10% reserve toggle, and funding source)
- 🌳 **Hierarchical PPA tree** — accordion/tree UI for the 4-level AIP reference code structure (Office → Program → Project → Activity)
- 🔢 **Auto-computed totals** — PS + MOOE + CO = Total; Q1+Q2+Q3+Q4 = quarterly total; rollups at every parent level
- 📜 **Audit log** — change history on all LDIP, AIP, and WFP records (who changed what, when)
- 🔒 **Draft / Final / Archived workflow** — records are editable as Draft; locked when Finalized; amendments create a new Draft copy

### AIP Reference Code Format

`SSSS-000-L-CC-OOO[-PPP[-AAAA[-XXXX]]]`

| Segments | Level | Example |
|---|---|---|
| 5 | Office | `1000-000-1-01-005` |
| 6 | Program | `1000-000-1-01-005-001` |
| 7 | Project / Sub-program | `1000-000-1-01-005-001-001` |
| 8 | Activity (leaf) | `1000-000-1-01-005-001-001-001` |

### New Database Tables

Config: `offices`, `funding_sources`, `accounts`  
LDIP: `ldip_records`  
AIP: `aip_records`, `aip_offices`, `aip_programs`, `aip_projects`, `aip_activities`  
WFP: `wfp_records`, `wfp_activities`, `wfp_expenditure_lines`  
Audit: `audit_log`

See [`docs/v1.1/DB_Model.md`](docs/v1.1/DB_Model.md) for the full schema.

### v1.0.x — Inventory UI Refinements + Security (shipped)

- 📦 **Distribution page** — standalone distribution flow with FIFO batch allocation; Stock Sources read-only view
- 📋 **PR List** — full filter panel (division, quarter, status, requested by, fund, AIP code, account)
- 📒 **Stock Overview** — Received in Quarter filter; renamed from Item Ledger
- 📊 **PR Report** — delivery summary bar; Quarter column replaces Date Created
- 🎨 **UI refinements** — flat design system across all inventory pages
- 🔐 **Security hardening (v1.0.1)** — login rate limiting, httpOnly refresh token cookie, CORS origin whitelist

## What's New in v1.2 – v1.4.5 — Divisions, WFP Rework & PPDO-Scoped Dashboard

- 📅 **v1.1.1 — Calendar approval workflow + Announcements** — calendar events gain an approval
  workflow; public/admin Announcements (CRUD, public landing page); user profile page.
- 🏢 **v1.2 — Configurable Divisions** — replaced the fixed `Division` enum and `PermissionGroup`
  table with a `divisions` table carrying both data-scope and feature-permission flags per
  division; roles simplified to SuperAdmin / Admin / Staff (Observer retired).
- 📈 **v1.3 — LDIP upload + Dashboard readiness hub** — LDIP `.xlsm` upload/re-upload creates one
  record spanning all offices; new 2×2 Allocation/LDIP/AIP/WFP readiness hub on the Budget
  Planning Dashboard.
- 🔁 **v1.4 — WFP Rework (epic)** — Chart of Accounts rework, Price Index config, function-band +
  activity-creation flags on AIP programs/activities, procurement line-item entry with quantity ×
  unit price × days, quarterly frequency grids, a dedicated WFP Report preview page mirroring the
  province's official "WFP FINAL" reference sheet.
- 💰 **v1.4.3 — Multi-fund ceiling & allocation** — budget ceilings and division allocations
  extended from General-Fund-only to every active funding source (GAD, LDRRM, 20% Development
  Fund, etc.), with fund-source aliasing so AIP's inconsistent free-text fund labels resolve to
  the right canonical fund.
- 📤 **v1.4.4 — WFP Report → Excel export** — one-click `.xlsx` export of the WFP Report matching
  the PBO's official "WFP FINAL" form layout (one worksheet per fund source, full function-band →
  program → project → activity → expense-class hierarchy with sub-totals/grand-totals), built with
  ClosedXML.
- 📊 **v1.4.5 — PPDO-scoped Budget Planning Dashboard** — the Dashboard is now permanently scoped
  to PPDO (Budget Planning is effectively PPDO-only in practice) with a per-division WFP status
  view and per-fund ceiling/allocation pie charts; the underlying queries were also reworked from
  several unfiltered full-table scans down to properly scoped SQL queries.
- 🛠️ **v1.4.6 – v1.4.9 — Budget Planning fixes + Audit Log page** — further N+1 query cleanup,
  Manila-timezone and calendar-row-height fixes on the Dashboard, and a new SuperAdmin-only Audit
  Log config page (auto-refreshing) surfacing the `audit_log` table added back in v1.1.

See [`docs/v1.4.4/WFP_Excel_Export_Assessment.md`](docs/v1.4.4/WFP_Excel_Export_Assessment.md) for
the design decisions behind v1.4.4's Excel export.

## What's New in v1.5 — PPMP Report

- 📊 **PPMP Report** — a second report type on Budget Planning › Report, alongside WFP (WFP stays
  the default). Item-grained, matching the Province's own filed PPMP working form rather than the
  national GPPB form — one row per procurement item, nested under the AIP program/project/activity
  hierarchy, with quarterly qty/amount schedules and a Stock Card No. column.
- 📤 **PPMP Excel export** — one-click `.xlsx` export matching the province's official layout,
  built with the same programmatic ClosedXML approach as the v1.4.4 WFP export.
- 🏷️ **Stock Card No. on the Price Index** — items in the Chart-of-Accounts price index now carry
  the GSO Item Code, joined live via `WfpProcurementItem.PriceIndexItemId` (never snapshotted, so
  a GSO correction retroactively fixes every report).

See [`docs/v1.5/PPMP_Report_Findings.md`](docs/v1.5/PPMP_Report_Findings.md) for the full design
spec, and [`docs/v1.5/STOCK_CARD_NO_RUNBOOK.md`](docs/v1.5/STOCK_CARD_NO_RUNBOOK.md) for the
Stock Card No. backfill process.

## What's New in v1.6 — AIP Manual Entry, In-Place Editing & LDIP

- ✏️ **AIP Manual Entry** — build an AIP record directly in the web UI, one node at a time
  (Office → Program → Project → Activity), each addition persisted immediately.
- 🔧 **AIP in-place editing** — edit/save/cancel per activity row on an uploaded or manually-built
  Draft AIP record, no re-upload required.
- 📋 **Carry-forward & seed from LDIP** — copy an office's programs (with full project/activity
  subtrees) from a prior fiscal year's AIP into the current one, or seed a fresh AIP office's
  programs directly from its matching LDIP record.
- 📝 **LDIP inline editing** — edit a single LDIP program's fields without a full file re-upload.
- 📱 **Mobile-responsive portal shell** — off-canvas sidebar drawer and responsive dashboard
  stacking below the `lg` breakpoint.

## What's New in v1.7 — Inventory Optimization

- ⚡ **Performance rework** — PR number generation moved from a full-table scan to a SQL
  aggregate; Inventory's catalog/PR/Delivery/Items-Master queries scoped and server-side paginated
  instead of filtering in memory; layout-preserving loading skeletons replace full-page spinners.
- 🧮 **Stock Balances** — a recurring, PPDO-wide physical-count ledger
  (`/inventory/stock-balances`) that reconciles counted quantities against system on-hand via a
  variance formula, with bulk CSV upsert.
- 📥 **GSO PR import** — prefill Create PR directly from a GSO-system PR export (Excel or signed
  PDF), including account-code matching across punctuation styles.
- 🚚 **Distribution FIFO moved server-side** — batch allocation across delivery batches is now
  computed on the backend instead of the frontend (and fixed a real LIFO-not-FIFO bug in the
  process).
- 📄 **Create PR Excel template redesign** — matches the look of the GSO export.
- 📜 **Full audit trail on Inventory** — PRs, deliveries, distributions, Items Master, and Stock
  Balances are all now logged to the audit trail.
- 🔍 **SEO** — sitemap, robots.txt, and metadata for the public landing page.

See [`docs/v1.7/Mobile_And_Inventory_Findings.md`](docs/v1.7/Mobile_And_Inventory_Findings.md) and
[`docs/v1.7/GSO_PR_Import_Findings.md`](docs/v1.7/GSO_PR_Import_Findings.md) for the audit and
design decisions behind this milestone.

---

## Development Standards

| Standard | File |
|---|---|
| Naming conventions | `docs/NAMING_CONVENTIONS.md` |
| Testing conventions | `docs/TEST_CONVENTIONS.md` |
| Git conventions | `docs/GIT_CONVENTIONS.md` |
| Bug reporting | `docs/BUG_REPORT_STANDARD.md` |
| Ticket prompt standard | `docs/TICKET_PROMPT_STANDARD.md` |
| Performance & scalability guidelines | `docs/PERFORMANCE_GUIDELINES.md` |
| Claude Code instructions | `CLAUDE.md` |
| Full technical spec | `PROJECT_DOCUMENTATION_NET_AZURE.md` |

Full-application performance audit (2026-07-16, findings + prioritization, not yet all actioned):
[`docs/Performance_Audit_2026-07-16.md`](docs/Performance_Audit_2026-07-16.md)

### v1.1 Budget Planning Docs

| Doc | File |
|---|---|
| Requirements & field analysis (LDIP / AIP / WFP) | `docs/v1.1/LDIP_AIP_WFP_Web_Requirements.md` |
| Database model | `docs/v1.1/DB_Model.md` |
| AIP import findings & design decisions | `docs/v1.1/AIP_WFP_Import_Findings.md` |
| Shared UI component standards | `docs/v1.1/UI_Component_Standards.md` |
| User roles, permissions & access model | `docs/v1.1/User_Roles_Permissions.md` |

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
