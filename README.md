# PPDO Portal & Inventory System

> Web portal for the Provincial Planning and Development Office (PPDO), Occidental Mindoro, Philippines.
> Covers inventory monitoring, investment planning (LDIP / AIP), budget planning (WFP / PPMP),
> and office operations.

**Current version: v1.9.0** (complete, frozen on `release/1.9.0`, going to UAT) · **Next: v1.10.0** (placeholder — SAIP)

---

## Status

| Version | Milestone | Status |
|---|---|---|
| v0.1 – v1.1 | Foundation, Core Portal & Inventory, Distribution | ✅ Shipped |
| v1.2 | Calendar approval, Announcements, configurable Divisions, allocation | ✅ Shipped |
| v1.3 | LDIP upload + Budget Planning readiness hub | ✅ Shipped |
| v1.4.x | WFP rework, multi-fund ceilings, WFP Excel export | ✅ Shipped |
| v1.5 | PPMP Report — preview + Excel export | ✅ Shipped |
| v1.6 | AIP editing, carry-forward, responsive shell | ✅ Shipped |
| v1.7.x | Inventory — stock balances, server-side Distribution, GSO PR import, audit trail | ✅ Shipped |
| v1.8.0 | Office users, AIP redesign (FY2028+), reviewer flow, external AIP API | ✅ Built — in UAT, not in production |
| v1.8.1 | Optimization & dashboard fixes | ✅ Built — ships with v1.8.0 |
| v1.8.2 | Dashboard redesign, caching, concurrent-edit guard (schema) | ✅ Built — on UAT |
| v1.8.3 – v1.8.4 | Concurrent-edit guard enforced, Demo 3 minor fixes | ✅ Built — on UAT |
| **v1.9.0** | **Larger requests from Demo 3** (see below) | 🧊 **Complete, frozen for UAT** |
| **v1.10.0** | **SAIP** — Supplemental AIP | 📝 **Placeholder — not specced or scheduled** |

> **Production** is at v1.7.4. v1.8.0 → v1.9.0 are **not deployed to production** until the PPDC and
> finance users sign off. They ship as one deploy, with the migrations run by hand first.

See [`CLAUDE.md`](CLAUDE.md) for the full release history, branch strategy and architecture rules.

---

## What's in v1.9.0

Built on `release/1.9.0`; only fixes found in UAT are accepted until it ships.

- ✏️ **PPDO reviewer edits** — PPDO reviewers can correct amounts and fields on an office's submitted
  AIP directly, with the change recorded in history. Adding, removing or renaming programs, projects
  or activities is deliberately *not* included (that is SAIP).
- 💰 **Enforced fund-source ceilings** — the AIP ceiling check now covers every fund source, not just
  the General Fund, and ceilings are configurable on the Funding Sources config page.
- 💬 **Comment threads** — replies, a Reply box and collapsed resolved threads on both Investment
  Proposals and AIP review.
- 🔔 **Notification bell** — a top-right inbox beside the user name, shared across features.
- 📄 **Initial WFP report** — generated from the consolidated AIP once offices are marked done.
- 📦 **Bulk export** — the initial WFP report for many offices as a background job, delivered through
  an Azure Blob container.

⚠️ **Deploying v1.9.0:** six additive migrations, a Blob container and two app settings must be in place
**before** the code. Follow
[`docs/v1.9/Pre_Deployment_Checklist_v1.9.0.md`](docs/v1.9/Pre_Deployment_Checklist_v1.9.0.md).

Specs: [`docs/v1.9/`](docs/v1.9/) — `AIP_Reviewer_Edit_Spec.md`, `Fund_Source_Ceiling_Spec.md`,
`AIP_Comment_Threads_Spec.md`, `Investment_Proposal_Comments_Spec.md`, `Notification_Centre_Spec.md`,
`Initial_WFP_Report_Spec.md`, `Bulk_Initial_WFP_Export_Spec.md`.

## v1.10.0 — SAIP (placeholder)

**SAIP — Supplemental Annual Investment Program** — is reserved as the headline of v1.10.0. It is the
mid-year companion to the AIP: changes that PPDO reviewer edits in v1.9.0 intentionally cannot make,
chiefly adding, removing or renaming programs, projects and activities after the AIP is approved.

- **Status:** placeholder only. No spec, tickets or branch exist yet; scope below is provisional.
- **Likely reuses:** the AIP entry/review flow, comment threads, the notification bell and the
  background-job plumbing from v1.9.0 (the bulk-export spec already anticipates a SAIP job kind).
- **Before any build:** a spec per [`docs/SPEC_STANDARD.md`](docs/SPEC_STANDARD.md) — goal, per-role
  behaviour, failure cases, UI states, non-goals and acceptance checklist.
- `release/1.10.0` is cut from the completed `release/1.9.0` once v1.9.0 is signed off in UAT.

---

## Tech Stack

| Layer | Technology |
|---|---|
| Backend API | .NET 9 — Azure Functions (isolated worker, Consumption plan) |
| Frontend | Next.js 14 — TypeScript, Tailwind CSS |
| Database | Azure SQL Database (Basic tier, Southeast Asia) |
| ORM | Entity Framework Core 9 |
| Auth | JWT access token + httpOnly refresh cookie; permissions resolved by `PermissionService` |
| Excel | ClosedXML (.xlsx export) |
| Storage | Azure Storage (Functions host; Blob container for v1.9.0 bulk exports) |
| Hosting | Azure Static Web Apps + Azure Functions + Azure SQL — all in **Southeast Asia** |
| CI/CD | GitHub Actions (does **not** run EF migrations) |

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
│       ├── lib/              # Axios client, auth helpers, version.ts
│       └── types/            # TypeScript interfaces
├── docs/                     # Specs, standards and release checklists (Obsidian vault)
├── CLAUDE.md                 # Claude Code instructions + implementation status
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
git clone https://github.com/obiken-01/ppdo-portal.git
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

### Tests

```bash
cd backend
dotnet test
```

---

## Architecture

**Serverless Clean Architecture** — layers deployed as Azure Functions + Azure Static Web Apps.

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

## Users, Roles & Offices

Roles: **SuperAdmin**, **Admin**, **Staff**. (The `Observer` role and the `PermissionGroup` table
were retired in v1.2.)

| Role | Who | Access |
|---|---|---|
| SuperAdmin | Developer / MIS | Full access — bypasses all permission checks |
| Admin | Division heads | All features by default |
| Staff | Any PPDO employee | Their division's feature flags plus individual overrides |

- **Divisions are configurable** (v1.2) — a `divisions` table carries both data scope and feature
  flags; a Staff user's effective access resolves from those plus per-user overrides.
- **Office users** (v1.8.0) — users belonging to other provincial offices sign in as guests and see
  only their own office's AIP and Investment Proposal data. A user with no office assigned sees nothing.
- **Permission reference** — [`docs/v1.8/Permission_Matrix.md`](docs/v1.8/Permission_Matrix.md) lists
  every flag × role × scope and is pinned by tests.

---

## Features

### Portal
- 🏠 Public landing page with announcements
- 🔐 Login + RBAC (role, division flags, per-user overrides, office scope)
- 📅 Dashboard — calendar with approval workflow and PH holidays
- 🔔 Notification bell (v1.9.0)
- 👤 User management, self-service profile and change password
- 📜 Audit log (SuperAdmin)

### Investment & Budget Planning
- 📈 **LDIP** — `.xlsm` upload, re-upload and inline editing
- 🧾 **AIP** — redesigned for FY2028+ (entry wizard, expenditures, ceilings, DBM reference codes);
  FY2027 stays on the legacy format
- 🔁 **Review flow** — submit → review → return → re-submit with row-anchored comment threads,
  PPDO reviewer edits (v1.9.0), consolidated view and Annex B report/Excel export
- 💰 **Ceilings & allocation** — per-fund-source ceilings (enforced on every fund source from v1.9.0)
  and per-division allocation
- 🗂️ **WFP** — entry wizard, procurement line items, frequency grids, report preview and Excel export;
  initial WFP report from the consolidated AIP and bulk export (v1.9.0)
- 📋 **PPMP** — item-grained report preview and Excel export
- 🔌 **External AIP API** — partner access by API key; see [`docs/external-api/README.md`](docs/external-api/README.md)

### Inventory
- 📦 Dashboard, Create PR (form, Excel import, GSO PR import), Receive Delivery, Distribution (server-side FIFO)
- 🗃️ Items Master, Stock Overview, Stock Balances (physical-count ledger), PR List, PR Report with Excel export
- 📜 Full audit trail across PRs, deliveries, distributions, items and stock balances

### AIP Reference Code Format

`SSSS-000-L-CC-OOO[-PPP[-AAAA[-XXXX]]]` — follows the DBM BOM segment layout.

| Segments | Level | Example |
|---|---|---|
| 5 | Office | `1000-000-1-01-005` |
| 6 | Program | `1000-000-1-01-005-001` |
| 7 | Project / Sub-program | `1000-000-1-01-005-001-001` |
| 8 | Activity (leaf) | `1000-000-1-01-005-001-001-001` |

---

## Development Standards

| Standard | File |
|---|---|
| Naming conventions | `docs/NAMING_CONVENTIONS.md` |
| Testing conventions | `docs/TEST_CONVENTIONS.md` |
| Git conventions | `docs/GIT_CONVENTIONS.md` |
| Bug reporting | `docs/BUG_REPORT_STANDARD.md` |
| Spec standard | `docs/SPEC_STANDARD.md` |
| Ticket prompt standard | `docs/TICKET_PROMPT_STANDARD.md` |
| Performance & scalability guidelines | `docs/PERFORMANCE_GUIDELINES.md` |
| Design system | `docs/DESIGN_SYSTEM.md` |
| Claude Code instructions | `CLAUDE.md` |
| Full technical spec | `PROJECT_DOCUMENTATION_NET_AZURE.md` |
| Design / business-logic context | `PPDO_PROJECT_CONTEXT.md` |

### Release docs

| Release | Doc |
|---|---|
| v1.8.0 (rewrites existing AIP amounts) | `docs/v1.8/Pre_Deployment_Checklist.md` |
| v1.8.2 | `docs/v1.8/Pre_Deployment_Checklist_v1.8.2.md` |
| v1.9.0 | `docs/v1.9/Pre_Deployment_Checklist_v1.9.0.md` |

Historical design notes: [`docs/v1.1/DB_Model.md`](docs/v1.1/DB_Model.md),
[`docs/v1.4.4/WFP_Excel_Export_Assessment.md`](docs/v1.4.4/WFP_Excel_Export_Assessment.md),
[`docs/v1.5/PPMP_Report_Findings.md`](docs/v1.5/PPMP_Report_Findings.md),
[`docs/v1.7/GSO_PR_Import_Findings.md`](docs/v1.7/GSO_PR_Import_Findings.md).

---

## Branching & Deployment

```
main                  ← production; push auto-deploys via GitHub Actions
release/X.Y.Z         ← one working branch per version
feature/vX.Y.Z-…      ← new work, PR into the release branch
fix/vX.Y.Z-…          ← UAT fixes
uat                   ← deploy target for UAT (never branch off it)
```

| Service | Platform |
|---|---|
| Frontend | Azure Static Web Apps |
| Backend API | Azure Functions — Consumption plan, Southeast Asia |
| Database | Azure SQL Database — Basic tier, Southeast Asia |

⚠️ **CI does not run EF migrations.** A release with a migration needs a manual
`dotnet ef database update` against Azure SQL, run **before** the code deploys. Any new Azure resource
goes in Southeast Asia, next to the database.

See `PROJECT_DOCUMENTATION_NET_AZURE.md` → Section 12 for the first-deployment guide.

---

## Project Tracking

Linear: https://linear.app/ralphoksiprojects/project/ppdo-portal-bdecba26e877
New tickets use the `PPDO-` prefix; older ones keep `RAL-`.

---

*PPDO Portal — Provincial Planning and Development Office, Occidental Mindoro, Philippines*
