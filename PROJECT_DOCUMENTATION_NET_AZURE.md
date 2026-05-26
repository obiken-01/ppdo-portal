# PPDO Portal Website and Inventory Monitoring System

## Project Documentation — .NET 9 + Azure Stack

> **Stack:** ASP.NET Core on Azure Functions · Next.js 14 on Azure Static Web Apps · Azure SQL Database · Azure Application Insights  
> **Deployment:** Free forever — Azure Static Web Apps (Free) + Azure Functions (Consumption) + Azure SQL (Free offer) + Application Insights (Free 5GB/mo)  
> **Last Updated:** 2026-05-26  
> **Version:** v0.1

---

## Table of Contents

1. [Project Overview](#1-project-overview)
2. [Tech Stack](#2-tech-stack)
3. [Architecture](#3-architecture)
4. [Project Structure](#4-project-structure)
5. [Domain Models](#5-domain-models)
6. [API Endpoints](#6-api-endpoints)
7. [Authentication & Security](#7-authentication--security)
8. [Infrastructure & DevOps](#8-infrastructure--devops)
9. [Environment Configuration](#9-environment-configuration)
10. [Development Approach](#10-development-approach)
11. [Roadmap](#11-roadmap)

---

## 1. Project Overview

**PPDO Portal Website and Inventory Monitoring System** is a web portal built for employees of the Provincial Planning and Development Office (PPDO), Occidental Mindoro, Philippines. Inventory monitoring is the first major feature, replacing a Google Sheets prototype (v0.4). The portal will expand to include a main dashboard with calendar, user management, and employee profiles in later versions.

| Field | Detail |
|---|---|
| **Frontend** | https://[yourapp].azurestaticapps.net *(to be updated after deploy)* |
| **API** | https://[yourapp-api].azurewebsites.net/api *(to be updated after deploy)* |
| **GitHub** | https://github.com/[username]/ppdo-portal |
| **Design (Penpot)** | https://design.penpot.app — PPDO Portal v0.5 (8 screens complete) |
| **Current Version** | v0.1 |

---

## 2. Tech Stack

### Backend

| Technology | Version | Purpose |
|---|---|---|
| Azure Functions (.NET isolated) | .NET 9 | Serverless API — HTTP-triggered functions replace ASP.NET controllers |
| C# | 13 | Primary backend language |
| Azure SQL Database | SQL Server 2022 (serverless) | Primary relational database — 32 GB free forever per subscription |
| Entity Framework Core | 9 | ORM — code-first migrations, LINQ queries |
| ASP.NET Core Identity (adapted) | 9 | User management, password hashing, role assignment |
| JWT Bearer Auth | built-in (.NET 9) | Access token + refresh token authentication flow |
| ClosedXML | 0.104+ | Excel `.xlsx` — export PR Report, generate PR import template, parse uploaded PR files — MIT license, no commercial fee |
| FluentValidation | 11+ | Request body validation (Create PR, Receive Delivery, user forms) |
| Application Insights | latest | Monitoring, logging, request tracking, exception capture — Azure native |
| Microsoft.Extensions.Logging | built-in (.NET 9) | `ILogger<T>` — standard logging abstraction used throughout all layers |

> **ClosedXML vs EPPlus:** ClosedXML is MIT-licensed with no commercial restrictions. EPPlus requires a paid commercial license for production use. ClosedXML handles merged cells, borders, and column widths needed for the PR Report export.

### Frontend

| Technology | Version | Purpose |
|---|---|---|
| Next.js (App Router) | 14 | UI framework — pages, SSR, routing |
| TypeScript | 5+ | Type safety across all components and API calls |
| Tailwind CSS | 3+ | Utility-first styling — mapped to PPDO design tokens (see Section 4) |
| shadcn/ui | latest | Component library — tables, forms, dialogs, dropdowns, badges |
| TanStack Table | 8+ | Data grids — Inventory, PR Register, Items Master (sortable, filterable) |
| React Hook Form + Zod | latest | Form handling and validation — Create PR (18 fields), Receive Delivery |
| FullCalendar | 6+ | Main Dashboard calendar — office events, personal events, PH holidays |
| Axios | 1+ | HTTP client for Azure Functions API calls |

### DevOps & Services

| Technology | Purpose | Cost |
|---|---|---|
| Azure Static Web Apps (Free) | Frontend hosting — Next.js, 100 GB bandwidth/mo | ₱0 |
| Azure Functions (Consumption plan) | Backend API hosting — 1M executions/mo free | ₱0 |
| Azure SQL Database (Free offer) | Managed SQL Server — 32 GB, 100K vCore-sec/mo free | ₱0 |
| Azure Application Insights | Monitoring, logging, request tracking — 5 GB/mo free | ₱0 |
| GitHub | Source control, PR reviews | Free |
| GitHub Actions | CI/CD — build, test, deploy on push to `main` | Free |

---

## 3. Architecture

### Pattern

**Serverless Clean Architecture** — Domain, Infrastructure, Application, and Functions layers, deployed as Azure Functions. Chosen because it keeps the familiar Clean Architecture separation that .NET developers know well, while fitting into Azure's serverless free tier. The Excel logic (export + import) lives in the Infrastructure layer (not in Function handlers), keeping handlers thin and testable.

> **Alternatives considered:** ASP.NET Core Web API on Railway (~₱307/mo) was considered but ruled out in favour of Azure's free-forever stack. A full monolith on Azure App Service was considered but requires a paid plan for persistent hosting.

### Layer Breakdown

```
Domain Layer
   ↓  Entities (User, PurchaseRequest, Item, Delivery, Distribution, ItemMaster)
   ↓  Interfaces (IRepository<T>, IExcelService, ICurrentUserService)
   ↓  Enums (PRStatus, DeliveryStatus, UserRole, Division)

Infrastructure Layer
   ↓  AppDbContext (EF Core — Azure SQL)
   ↓  Repositories (generic + feature-specific)
   ↓  ExcelService (ClosedXML — export PR Report, generate PR template, parse uploaded PR Excel files)
   ↓  CurrentUserService (reads JWT claims)

Application Layer
   ↓  Services (PurchaseRequestService, DeliveryService, ItemService, UserService, PermissionService)
   ↓  DTOs (request/response models per feature)
   ↓  Validators (FluentValidation — one per request DTO)

Functions Layer  (replaces ASP.NET Controllers)
      HTTP-triggered Azure Functions — one file per feature group
      e.g. AuthFunctions.cs, PurchaseRequestFunctions.cs, DeliveryFunctions.cs
      Azure SWA automatically proxies /api/* to linked Functions — no CORS config needed
```

### Dependency / Delivery Order

> Always implement in this order to avoid forward-reference errors:

```
Domain → Infrastructure → Application → Functions → Frontend (Next.js)
```

New injectable services and constructors must be delivered before the methods that call them.

### Design Patterns Used

- **Repository Pattern** — abstracts EF Core data access; each entity has a typed repository
- **Dependency Injection** — all services registered in `Program.cs` via `IServiceCollection`; Functions use constructor injection
- **DTO Pattern** — request and response models are separate from domain entities; no entity is exposed directly over HTTP
- **Service Layer** — business logic lives in Application services, not in Function handlers
- **Options Pattern** — configuration (JWT settings, connection strings) bound via `IOptions<T>`
- **Structured Logging** — `ILogger<T>` injected into all Application services; Application Insights hooks in automatically via `APPLICATIONINSIGHTS_CONNECTION_STRING`; key business events logged manually (PR submit, delivery receive, low stock alerts, auth failures)

---

## 4. Project Structure

```
ppdo-portal/
│
├── backend/                            ← .NET solution root
│   ├── PPDO.Domain/                    ← Domain layer (no dependencies)
│   │   ├── Entities/
│   │   │   ├── User.cs
│   │   │   ├── PermissionGroup.cs
│   │   │   ├── PurchaseRequest.cs
│   │   │   ├── PRItem.cs
│   │   │   ├── Delivery.cs
│   │   │   ├── Distribution.cs
│   │   │   └── ItemMaster.cs
│   │   ├── Interfaces/
│   │   │   ├── IRepository.cs
│   │   │   ├── IExcelService.cs
│   │   │   └── ICurrentUserService.cs
│   │   └── Enums/
│   │       ├── PRStatus.cs             ← Open, PartiallyDelivered, FullyDelivered, Completed
│   │       ├── UserRole.cs             ← SuperAdmin, Admin, Staff, Observer
│   │       └── Division.cs             ← Admin, Planning, RM, MIS, SPD
│   │
│   ├── PPDO.Infrastructure/            ← Infrastructure layer
│   │   ├── Data/
│   │   │   ├── AppDbContext.cs
│   │   │   └── Migrations/
│   │   ├── Repositories/
│   │   │   ├── Repository.cs           ← Generic base
│   │   │   ├── PurchaseRequestRepository.cs
│   │   │   └── ItemMasterRepository.cs
│   │   └── Services/
│   │       ├── ExcelService.cs             ← ClosedXML: export PR Report, generate PR template, parse PR import upload
│   │       └── CurrentUserService.cs
│   │       └── (logging handled via ILogger<T> injection — no separate service needed)
│   │
│   ├── PPDO.Application/               ← Application layer
│   │   ├── DTOs/
│   │   │   ├── Auth/
│   │   │   ├── PurchaseRequest/
│   │   │   ├── Delivery/
│   │   │   ├── Item/
│   │   │   └── User/
│   │   ├── Services/
│   │   │   ├── AuthService.cs
│   │   │   ├── PurchaseRequestService.cs
│   │   │   ├── DeliveryService.cs
│   │   │   ├── ItemService.cs
│   │   │   ├── UserService.cs
│   │   │   └── PermissionService.cs        ← resolves effective permissions (group + override logic)
│   │   └── Validators/                 ← FluentValidation — one per request DTO
│   │
│   ├── PPDO.Functions/                 ← Azure Functions layer (API entry point)
│   │   ├── AuthFunctions.cs
│   │   ├── PurchaseRequestFunctions.cs
│   │   ├── DeliveryFunctions.cs
│   │   ├── ItemFunctions.cs
│   │   ├── UserFunctions.cs
│   │   ├── PermissionGroupFunctions.cs
│   │   ├── ReportFunctions.cs
│   │   ├── Program.cs                  ← DI registration, EF Core, JWT config
│   │   └── host.json
│   │
│   └── PPDO.Tests/                     ← xUnit test project
│       ├── Application/
│       └── Functions/
│
├── frontend/                           ← Next.js project
│   ├── src/
│   │   ├── app/                        ← Next.js App Router pages
│   │   │   ├── (public)/
│   │   │   │   ├── page.tsx            ← Landing page
│   │   │   │   └── login/page.tsx
│   │   │   └── (portal)/              ← Authenticated layout
│   │   │       ├── layout.tsx          ← Sidebar + auth guard
│   │   │       ├── dashboard/page.tsx
│   │   │       ├── inventory/
│   │   │       │   ├── page.tsx        ← Inventory Dashboard
│   │   │       │   ├── create-pr/page.tsx
│   │   │       │   ├── receive-delivery/page.tsx
│   │   │       │   ├── items-master/page.tsx
│   │   │       │   ├── item-ledger/page.tsx
│   │   │       │   ├── pr-register/page.tsx
│   │   │       │   └── pr-report/[prNo]/page.tsx
│   │   │       └── admin/
│   │   │           └── users/page.tsx
│   │   ├── components/
│   │   │   ├── ui/                     ← shadcn/ui base components
│   │   │   ├── layout/                 ← Sidebar, Topbar, PageHeader
│   │   │   ├── inventory/              ← Feature-specific components
│   │   │   └── shared/                 ← DataTable, StatusBadge, etc.
│   │   ├── lib/
│   │   │   ├── api.ts                  ← Axios instance + interceptors
│   │   │   ├── auth.ts                 ← Token storage + refresh logic
│   │   │   └── utils.ts
│   │   └── types/                      ← TypeScript interfaces matching backend DTOs
│   ├── tailwind.config.ts              ← PPDO design tokens (see below)
│   └── staticwebapp.config.json        ← Azure SWA routing config
│
├── .github/
│   └── workflows/
│       ├── ci.yml                      ← Build + test on PR
│       └── deploy.yml                  ← Deploy to Azure on push to main
│
└── swa-cli.config.json                 ← Azure SWA CLI local dev config
```

### Tailwind Design Tokens (tailwind.config.ts)

Map these from the Penpot design system — add to `theme.extend.colors`:

```ts
colors: {
  green: {
    950: '#071F12', 900: '#0F4526', 800: '#13512D',
    700: '#196638', 600: '#1F7A45', 500: '#2E9958',
    400: '#3BAD6A', 300: '#6DC492', 200: '#A8DABC',
    100: '#D4EDDE', 50: '#F0FAF4', 25: '#F7FCF9',
  },
  slate: {
    800: '#343A40', 600: '#6C757D', 400: '#ADB5BD',
    200: '#E9ECEF', 100: '#F1F3F5', 50: '#F8F9FA',
  },
  amber:  { 500: '#EF9F27', 100: '#FEF3CD' },
  danger: { 500: '#E24B4A', 100: '#FDECEA' },
  info:   { 500: '#378ADD', 100: '#E3F2FD' },
}
```

---

## 5. Domain Models

### User

> A PPDO staff member who can log in to the portal.

```
Id                              Guid        PK
FullName                        string      required, max 100
Email                           string      required, unique — used as login username
PasswordHash                    string      BCrypt hash — managed by ASP.NET Core Identity
Role                            UserRole    enum: SuperAdmin | Admin | Staff | Observer
Division                        Division    enum: Admin | Planning | RM | MIS | SPD
GroupId                         Guid?       FK → PermissionGroup — null for SuperAdmin/Admin
Position                        string?     optional, max 100
ContactNo                       string?     optional
IsActive                        bool        default true
CreatedAt                       DateTime
UpdatedAt                       DateTime

// Individual permission overrides — null = inherit from group, true/false = explicit override
// SuperAdmin and Admin ignore all flags — always have full access
OverrideCanAccessInventory      bool?       null = use group value
OverrideCanAccessReports        bool?       null = use group value
OverrideCanManageUsers          bool?       null = use group value
// CanAccessProfile is always true for all roles — no override needed
```

> **Effective permission logic:**
> ```
> bool CanAccessInventory =
>     Role is SuperAdmin or Admin  → true (always, flags ignored)
>     else → OverrideCanAccessInventory ?? Group.CanAccessInventory
> ```

### PurchaseRequest

> A formal request for office supplies, submitted by a staff member for a division.

```
Id              Guid        PK
PRNo            string      unique — format: 101-1041-GF-YYYY-MM-DD-XXX
PRDate          DateOnly    date on the PR form
DateCreated     DateTime    auto — date record was created in system
Department      string      default "PPDO"
Division        Division    FK scope — division requesting the items
Fund            string      e.g. "General Fund"
RequestedBy     string      name of requesting staff
Position        string      position of requesting staff
ApprovedBy      string?     name of approving officer
ApprovingPosition string?
AIPCode         string?
AccountNo       string?
AccountTitle    string?
Program         string?     long field — up to 120 chars
Project         string?     long field — up to 120 chars
Activity        string?     long field — up to 120 chars
SAINo           string?
ALOBSNo         string?
TotalAmount     decimal     computed from PRItems
Status          PRStatus    enum: Open | PartiallyDelivered | FullyDelivered | Completed
CreatedById     Guid        FK → User
CreatedAt       DateTime
UpdatedAt       DateTime
→ Items         PRItem[]    line items on this PR
→ Deliveries    Delivery[]  delivery records against this PR
```

### PRItem

> A single line item on a Purchase Request.

```
Id              Guid        PK
PRId            Guid        FK → PurchaseRequest
ItemNo          int         sequential within PR
StockNo         string?     from Items Master
Description     string      required
Unit            string      e.g. "ream", "box", "piece"
Quantity        decimal     qty requested
UnitCost        decimal     from Items Master
TotalCost       decimal     computed — Quantity × UnitCost
ItemType        string?     from Items Master
```

### ItemMaster

> The office supply catalog — source of truth for stock numbers, descriptions, unit costs.

```
Id              Guid        PK
StockNo         string      unique
Description     string      required
Category        string?     assigned by admin; blank = "★ NEW - review"
Unit            string
UnitCost        decimal
ItemType        string?
ReorderQty      int         threshold for low-stock alert
Remarks         string?
IsNewItem       bool        true = flagged for review
CreatedAt       DateTime
UpdatedAt       DateTime
```

### Delivery

> A delivery event — records items received against a PR on a given date.

```
Id              Guid        PK
DeliveryRef     string      unique — format: DEL-YYYYMMDD-XXXXX
PRId            Guid        FK → PurchaseRequest
DeliveryDate    DateOnly
ReceivedBy      string
Supplier        string?
Remarks         string?
CreatedAt       DateTime
→ Items         DeliveryItem[]
```

### DeliveryItem

> A single item line within a delivery, with optional split-by-division.

```
Id              Guid        PK
DeliveryId      Guid        FK → Delivery
PRItemId        Guid        FK → PRItem
QtyDelivered    decimal     total delivered this event
→ Distributions Distribution[]  per-division breakdown
```

### Distribution

> Tracks which division received how many units from a delivery item.

```
Id              Guid        PK
IssueRef        string      unique — format: ISS-YYYYMMDD-XXXXX-N
DeliveryItemId  Guid        FK → DeliveryItem
Division        Division    enum — receiving division
QtyIssued       decimal
DateIssued      DateOnly
IssuedBy        string
Remarks         string?
```

### PermissionGroup

> A named set of feature permissions assigned to a division. Users inherit these flags; individual overrides can be set per user.

```
Id                      Guid        PK
Name                    string      required, unique — e.g. "Admin Division Staff"
Division                Division    the division this group is the default for
Description             string?     optional notes

// Feature permission flags — what members of this group can do by default
CanAccessInventory      bool        default false
CanAccessReports        bool        default false
CanManageUsers          bool        default false
// CanAccessProfile is always true — not stored here

CreatedAt               DateTime
UpdatedAt               DateTime
→ Users                 User[]      members of this group
```

> **Default groups seeded on first deploy:**
>
> | Group | Division | CanAccessInventory | CanAccessReports | CanManageUsers |
> |---|---|---|---|---|
> | Admin Division Staff | Admin | true | true | false |
> | Planning Staff | Planning | false | true | false |
> | RM Staff | RM | false | true | false |
> | MIS Staff | MIS | false | true | false |
> | SPD Staff | SPD | false | true | false |
> | Observer (default) | — | false | false | false |

---

### Enums

```
PRStatus:    Open (0), PartiallyDelivered (1), FullyDelivered (2), Completed (3)
UserRole:    SuperAdmin (0), Admin (1), Staff (2), Observer (3)
Division:    Admin (0), Planning (1), RM (2), MIS (3), SPD (4)
```

---

## 6. API Endpoints

> **Auth legend:** ❌ = public  ✅ = requires JWT  
> All authenticated endpoints enforce **division scope** for Staff and Observer roles. Permission checks use effective permissions (group flags + individual overrides).

### Auth — /api/auth

| Method | Endpoint | Auth | Description |
|---|---|---|---|
| POST | /api/auth/login | ❌ | Email + password → access token + refresh token |
| POST | /api/auth/refresh | ❌ | Refresh token → new access token |
| POST | /api/auth/logout | ✅ | Revoke refresh token |
| GET | /api/auth/me | ✅ | Current user info (id, name, role, division, effective permissions) |
| POST | /api/auth/change-password | ✅ | Authenticated user changes own password |

### Users — /api/users *(Super Admin / Admin / Staff with CanManageUsers)*

| Method | Endpoint | Auth | Description |
|---|---|---|---|
| GET | /api/users | ✅ | List all users |
| GET | /api/users/{id} | ✅ | Get user by ID |
| POST | /api/users | ✅ | Create user — assigns group by division, Admin sets default password |
| PUT | /api/users/{id} | ✅ | Update user details |
| PUT | /api/users/{id}/reset-password | ✅ | Reset user password to default |
| PUT | /api/users/{id}/permissions | ✅ | Set individual permission overrides for a user (SuperAdmin only) |
| DELETE | /api/users/{id} | ✅ | Soft delete — sets IsActive = false |

### Permission Groups — /api/permission-groups *(Super Admin only)*

| Method | Endpoint | Auth | Description |
|---|---|---|---|
| GET | /api/permission-groups | ✅ | List all groups with their flags |
| GET | /api/permission-groups/{id} | ✅ | Get group with member list |
| PUT | /api/permission-groups/{id} | ✅ | Update group flags — propagates to all members at runtime |

### Purchase Requests — /api/purchase-requests

| Method | Endpoint | Auth | Description |
|---|---|---|---|
| GET | /api/purchase-requests | ✅ | List PRs (division-scoped for Staff/Viewer) |
| GET | /api/purchase-requests/{id} | ✅ | Get PR with items |
| POST | /api/purchase-requests | ✅ | Submit new PR |
| PUT | /api/purchase-requests/{id} | ✅ | Update PR (Admin only, if status = Open) |
| GET | /api/purchase-requests/{id}/report | ✅ | PR Report data (Sections 1, 2, 3) |
| GET | /api/purchase-requests/{id}/export | ✅ | Download PR Report as .xlsx (ClosedXML) |
| GET | /api/purchase-requests/template | ✅ | Download blank PR import template as .xlsx |
| POST | /api/purchase-requests/import | ✅ | Upload populated PR template — creates one or multiple PRs from Excel file |

### Deliveries — /api/deliveries

| Method | Endpoint | Auth | Description |
|---|---|---|---|
| GET | /api/deliveries | ✅ | List deliveries (division-scoped) |
| GET | /api/deliveries/{id} | ✅ | Get delivery with items + distributions |
| POST | /api/deliveries | ✅ | Submit delivery — updates PR status automatically |

### Items — /api/items

| Method | Endpoint | Auth | Description |
|---|---|---|---|
| GET | /api/items | ✅ | Inventory summary — all items with stock levels |
| GET | /api/items/master | ✅ | Items Master Data catalog |
| GET | /api/items/master/{id} | ✅ | Single item master record |
| POST | /api/items/master | ✅ | Add new item to master (Admin only) |
| PUT | /api/items/master/{id} | ✅ | Update item master record (Admin only) |
| GET | /api/items/ledger | ✅ | Item Ledger — running stock totals per item across all PRs |
| GET | /api/items/lookup | ✅ | Lookup by stockNo or description — used by Create PR autocomplete |

### Dashboard — /api/dashboard

| Method | Endpoint | Auth | Description |
|---|---|---|---|
| GET | /api/dashboard/stats | ✅ | Grouped stat card counts (PR counts, stock alerts) |
| GET | /api/dashboard/pr-status | ✅ | PR status table (Open, Partially Delivered, etc.) |

---

## 7. Authentication & Security

### Strategy

JWT with Refresh Token Rotation — short-lived access tokens (15 min) and longer-lived refresh tokens (7 days), stored server-side for revocation.

### Flow

```
1. User submits email + password to POST /api/auth/login
2. Server validates credentials via ASP.NET Core Identity (BCrypt hash compare)
3. Server returns: Access Token (JWT, 15 min) + Refresh Token (opaque string, 7 days)
4. Refresh token is stored in DB (User table or RefreshTokens table) and can be revoked
5. Client stores Access Token in memory; Refresh Token in httpOnly cookie
6. Client attaches Access Token to every request: Authorization: Bearer <token>
7. On 401 response, client calls POST /api/auth/refresh with the cookie
8. Server validates refresh token, issues new Access Token + rotates Refresh Token
9. If refresh fails (expired, revoked), clear tokens and redirect to /login
```

### Roles & Permissions

#### Roles

| Role | Who | Behavior |
|---|---|---|
| **SuperAdmin** | Developer / MIS staff | Bypasses all permission checks — full access to everything |
| **Admin** | All 5 division heads | Gets all feature permissions by default — flags ignored |
| **Staff** | Any PPDO employee | Access determined by PermissionGroup flags + individual overrides |
| **Observer** | Provincial administrator or internal read-only users (TBD) | Read-only on granted features — no create/edit/delete ever |

#### Feature Permission Flags

| Flag | Covers | SuperAdmin | Admin | Staff | Observer |
|---|---|---|---|---|---|
| `CanAccessInventory` | Full Inventory Management — Create PR, Receive Delivery, Items Master, Item Ledger, PR Register, Excel import | ✅ always | ✅ always | group + override | group + override (read-only) |
| `CanAccessReports` | PR Report — view + export | ✅ always | ✅ always | group + override | group + override (read-only) |
| `CanManageUsers` | User Management — add, reset password, deactivate | ✅ always | ✅ always | override only | ❌ never |
| `CanAccessProfile` | Own profile — view + edit | ✅ always | ✅ always | ✅ always | ✅ always |

#### Division Scope Rule

| | Reads | Writes |
|---|---|---|
| SuperAdmin + Admin | All divisions | All divisions |
| Staff + Observer | All divisions | Own division only |

#### User Management Rules

| Role | Can manage |
|---|---|
| SuperAdmin | Everyone — including other Admins and SuperAdmins |
| Admin | Staff + Observer only — cannot touch Admin or SuperAdmin accounts |
| Staff (CanManageUsers override = true) | Staff + Observer only — same scope as Admin |
| Observer | No user management — ever |

#### Public Landing Page

| Content | Auth required |
|---|---|
| Announcements / news posts | ❌ Public — no login needed |
| All other portal features | ✅ Login required |

#### Adding a New Feature

```
1. Add bool flag to PermissionGroup entity    e.g. CanAccessCalendar
2. Add nullable override to User entity       e.g. OverrideCanAccessCalendar bool?
3. Set defaults in group seed data            per division as appropriate
4. Check effective permission in Function     Role is Admin/SuperAdmin OR override ?? group flag
5. Done — existing users get defaults automatically via their group
```

### Security Notes

- Passwords hashed with BCrypt via ASP.NET Core Identity — never stored in plain text
- Refresh tokens stored in DB — can be individually revoked (e.g. on logout, password change)
- JWT secret key minimum 32 characters — stored in Azure Functions Application Settings (not in code)
- HTTPS enforced — Azure SWA and Functions both enforce TLS
- RA 10173 (Data Privacy Act 2012) compliance — user PII fields are logged minimally; no sensitive data in URL parameters
- Rate limiting applied to `/api/auth/login` and `/api/auth/refresh` to prevent brute force

---

## 8. Infrastructure & DevOps

### Local Development

| Service | URL |
|---|---|
| Functions API (with Swagger via Swashbuckle) | http://localhost:7071/api |
| Frontend (Next.js dev server) | http://localhost:3000 |
| Azure SWA CLI (emulates full SWA locally) | http://localhost:4280 |
| Azure SQL (local) | SQL Server LocalDB or Azure SQL Dev container |

**Start local environment:**

```bash
# Terminal 1 — start backend Functions
cd backend/PPDO.Functions
func start

# Terminal 2 — start frontend
cd frontend
npm run dev

# Terminal 3 — optional: SWA CLI (emulates /api proxy locally)
swa start http://localhost:3000 --api-location http://localhost:7071
```

> **Note:** Azure SWA CLI (`npm install -g @azure/static-web-apps-cli`) proxies `/api/*` to the Functions port locally, matching the production behaviour exactly.

### Production

| Service | Platform | URL |
|---|---|---|
| Frontend (Next.js) | Azure Static Web Apps (Free) | https://[yourapp].azurestaticapps.net |
| Backend (.NET Functions) | Azure Functions (Consumption) | Linked to SWA — accessed via `/api/*` proxy |
| Database | Azure SQL Database (Free offer) | Managed — connection string in App Settings |

**Deploy trigger:** Push to `main` → GitHub Actions runs CI → deploys to Azure automatically.

Azure SWA auto-proxies all `/api/*` requests to the linked Functions app — no CORS configuration needed in production.

### CI/CD Pipeline

**Files:** `.github/workflows/ci.yml` and `.github/workflows/deploy.yml`

**On every PR to `main` (CI):**
1. Backend: `dotnet restore` → `dotnet build` → `dotnet test`
2. Frontend: `npm ci` → `npm run build` → `npm run lint`

**On push to `main` (Deploy):**
1. Backend: build + publish Functions project
2. Frontend: `npm run build` → static export
3. Deploy both to Azure via `azure/static-web-apps-deploy` GitHub Action

### Database Migrations

```bash
# From solution root — add a new migration
dotnet ef migrations add MigrationName \
  --project backend/PPDO.Infrastructure \
  --startup-project backend/PPDO.Functions

# Apply migration to Azure SQL
dotnet ef database update \
  --project backend/PPDO.Infrastructure \
  --startup-project backend/PPDO.Functions \
  --connection "Server=tcp:[server].database.windows.net;..."
```

> **Azure SQL Free tier note:** The database auto-pauses when the 100K vCore-second monthly limit is reached and resumes at the start of the next calendar month. Set `--auto-pause-delay` behavior to pause (not bill) to guarantee ₱0. For ~10 users and typical PPDO workloads this limit is very unlikely to be hit.

---

## 9. Environment Configuration

> Variable names and descriptions only — never actual values. Maintain `.env.example` and `local.settings.json.example` in the repo.

### Backend (Azure Functions — `local.settings.json` locally, App Settings in Azure)

```json
{
  "Values": {
    "AzureWebJobsStorage": "UseDevelopmentStorage=true for local; Azure Storage connection string in prod",
    "FUNCTIONS_WORKER_RUNTIME": "dotnet-isolated",
    "SqlConnectionString": "Azure SQL connection string — Server, Database, User, Password",
    "Jwt__SecretKey": "JWT signing secret — minimum 32 characters, strong random string",
    "Jwt__Issuer": "Issuer claim — e.g. https://yourapp.azurestaticapps.net",
    "Jwt__Audience": "Audience claim — e.g. ppdo-portal",
    "Jwt__AccessTokenExpiryMinutes": "15",
    "Jwt__RefreshTokenExpiryDays": "7",
    "APPLICATIONINSIGHTS_CONNECTION_STRING": "Azure Application Insights connection string — from Azure Portal → Application Insights → Connection String"
  }
}
```

> **Application Insights local dev note:** The connection string is optional for local development. When omitted, telemetry is not sent to Azure — console logging still works via `ILogger<T>`. Add the connection string to `local.settings.json` only if you want to test monitoring locally.

### Frontend (Next.js — `.env.local` locally, Azure SWA App Settings in prod)

```env
NEXT_PUBLIC_API_BASE_URL=URL of the Azure Functions API (e.g. /api for SWA proxy, or full URL for local)
```

### Security Rules

| File | Contains Secrets | Git Status |
|---|---|---|
| `local.settings.json` | Yes | ❌ Never commit — already in `.gitignore` by default |
| `local.settings.json.example` | No (placeholders) | ✅ Safe to commit |
| `.env.local` | Yes | ❌ Never commit |
| `.env.example` | No (placeholders) | ✅ Safe to commit |
| `appsettings.json` | No (empty values only) | ✅ Safe to commit |

### Known Configuration Notes

- .NET Functions use double-underscore for nested config: `Jwt__SecretKey` maps to `Jwt:SecretKey` in code
- Application Insights uses `APPLICATIONINSIGHTS_CONNECTION_STRING` — Azure Functions auto-detects this key and hooks into `ILogger<T>` automatically
- In Azure Portal, Application Insights is created separately and then linked to the Function App
- Add `http://localhost:3000` and `http://localhost:4280` to CORS allowed origins in `Program.cs` for local dev; production CORS is handled by Azure SWA automatically
- `NEXT_PUBLIC_API_BASE_URL` should be `/api` in production (SWA proxy) and `http://localhost:7071/api` in local dev without SWA CLI

---

## 10. Development Approach

### Methodology

Spec-driven development with AI assistance — full documentation written before coding begins. Requirements are locked per version before implementation starts. Google Sheets v0.4 remains the reference for business logic (field names, PR number formats, delivery aggregation rules).

### Code Delivery Order

Always follow the dependency order to avoid forward-reference errors:

```
Domain → Infrastructure → Application → Functions → Frontend
```

New injectable services and constructors delivered before the methods that use them.

### Test-Driven Development

- Tests written alongside implementation for Application services and Function handlers
- Tests integrated into CI pipeline — build fails if tests fail
- Test project: `PPDO.Tests`
- Test framework: xUnit + Moq
- Run tests: `dotnet test`

### Branch Strategy

```
main                              ← production — auto-deploys to Azure
feature/vX.Y-short-description   ← feature branches — PR required to merge
fix/vX.Y-short-description       ← bug fix branches
refactor/description              ← refactoring (no feature change)
chore/description                 ← config, deps, tooling updates
```

### Commit Message Format

Follows [Conventional Commits](https://www.conventionalcommits.org/):

```
feat(scope): description
fix(scope): description
refactor(scope): description
test(scope): description
chore(scope): description
docs(scope): description
```

Examples:
```
feat(inventory): add PR submit endpoint with FluentValidation
fix(auth): refresh token not rotating on concurrent requests
chore(deps): upgrade ClosedXML to 0.104.1
```

### AI Assistance

- `PROJECT_DOCUMENTATION_NET_AZURE.md` — primary context for Claude Code sessions (this file)
- `PPDO_PROJECT_CONTEXT.md` — design system, Penpot frames, business logic reference
- `CLAUDE.md` *(to be created)* — Claude Code conventions, commands, and shortcuts

### ExcelService — Design Notes

`ExcelService` handles all Excel interactions in both directions. It lives in `PPDO.Infrastructure/Services/ExcelService.cs` and implements `IExcelService`.

**Three responsibilities:**

| Method | Direction | Description |
|---|---|---|
| `GeneratePRTemplate()` | Export | Returns a styled `.xlsx` file with Section 1 header fields and a Section 2 items grid. Yellow cells = user fills in. Locked/gray cells = do not edit. Instructions sheet included. |
| `ExportPRReport(prId)` | Export | Returns a styled `.xlsx` PR Report — Section 1 (header), Section 2 (line items), Section 3 (distribution). Matches the on-screen PR Report layout. |
| `ParsePRImport(stream)` | Import | Reads an uploaded `.xlsx` file (one or multiple PR sheets), validates each row, returns a list of `CreatePRDto` ready for `PurchaseRequestService`. |

**PR Template design rules:**
- One worksheet per PR — users can duplicate the sheet tab to create multiple PRs in one upload
- Sheet tab name = PR reference (e.g. `PR-001`, `PR-002`) — used to distinguish PRs during import
- Yellow cells (`#FFFDE7`) = user fills in — matches the web app UX convention
- Gray cells (`#F1F3F5`) = locked, do not edit (item descriptions auto-filled from StockNo on import if found in Items Master)
- A separate `Instructions` sheet is included in every template download explaining the fill rules
- Stock No. column: if a user enters a known StockNo, the import parser auto-fills Description, Unit, and UnitCost from Items Master — same behaviour as the web form autocomplete

**Import validation rules (ParsePRImport):**
- Required Section 1 fields: Division, Requested By, PR Date
- At least one item row must have Qty > 0
- Unknown StockNo values are flagged as `IsNewItem = true` (same as web form behaviour)
- Rows with blank Description AND blank StockNo are skipped silently
- Parse errors (missing required fields, invalid dates, non-numeric Qty) are collected and returned as a validation error list — the whole file is rejected if any PR sheet has errors
- Duplicate PR Nos. within the same upload are rejected

### Key Business Logic Notes (from Google Sheets v0.4)

These rules must be preserved exactly in the web app:

- **PR No. format:** `101-1041-GF-YYYY-MM-DD-XXX` (3-digit sequence)
- **Delivery Ref format:** `DEL-YYYYMMDD-XXXXX` (5-digit random, Manila timezone)
- **Issue Ref format:** `ISS-YYYYMMDD-XXXXX-N`
- **PR status transitions:** `Open → PartiallyDelivered → FullyDelivered` (triggered by `SubmitDelivery`)
- **Split delivery:** one item can be split across multiple divisions — aggregate `QtyIssued` per division
- **Items Master auto-flag:** new items added via Create PR are flagged `IsNewItem = true` pending admin review
- **Program / Project / Activity fields:** long text — `<textarea>` in frontend with `min-height: 44px`, `max-height: 88px`, `resize: vertical`
- **Bidirectional lookup:** in Create PR, entering StockNo auto-fills Description (and vice versa) from Items Master

---

## 11. Roadmap

| Version | Scope | Status |
|---|---|---|
| v0.1 | Project setup + foundation | 📋 Planned |
| v1.0 | Core portal + inventory monitoring | 📋 Planned |
| v1.1 | Employee profiles + user self-service | 📋 Planned |
| v1.2 | Calendar appointments + announcements | 📋 Planned |

---

### v0.1 — Project Setup (Planned)

- [ ] GitHub repository created (`ppdo-portal`)
- [ ] .NET solution scaffolded (Domain, Infrastructure, Application, Functions, Tests)
- [ ] Next.js project scaffolded with Tailwind + shadcn/ui
- [ ] Azure resources provisioned (SWA, Functions, SQL Database)
- [ ] Local development environment working (Functions + Next.js + SWA CLI)
- [ ] EF Core initial migration — all domain entities
- [ ] GitHub Actions CI/CD pipeline (build + test + deploy)
- [ ] `.env.example` and `local.settings.json.example` committed
- [ ] Azure Application Insights resource created and linked to Function App
- [ ] `APPLICATIONINSIGHTS_CONNECTION_STRING` added to Azure App Settings

### v1.0 — Core Portal + Inventory Monitoring (Planned)

**Auth & Users**
- [ ] Login page (matches Penpot `02 Login` frame)
- [ ] JWT auth flow (login, refresh, logout)
- [ ] User management page — Admin creates users, resets passwords
- [ ] RBAC middleware — role + division scope enforced on all endpoints

**Main Dashboard**
- [ ] Main Dashboard page with FullCalendar (matches Penpot `03 Main Dashboard` frame)
- [ ] Public PH holidays loaded into calendar
- [ ] Office events — any logged-in user can create; visible to all

**Inventory — Core Workflow**
- [ ] Inventory Dashboard — grouped stat cards + PR status table (Penpot `04 Inventory Dashboard`)
- [ ] Items Master Data — catalog view + edit (Penpot `06 Items Master`)
- [ ] Create PR — Section 1 (18 fields) + Section 2 (items grid with autocomplete) (Penpot `04b Create PR`)
- [ ] Receive Delivery — delivery form + PR selector + split delivery (Penpot `05 Receive Delivery`)
- [ ] PR Report — 3-section view + Excel export via ClosedXML (Penpot `07 PR Report`)
- [ ] Excel PR Import — download blank PR template (.xlsx), upload populated file to create one or multiple PRs in bulk
- [ ] `ExcelService` — 3 methods: `ExportPRReport()`, `GeneratePRTemplate()`, `ParsePRImport()`
- [ ] Item Ledger — running stock totals per item across all PRs
- [ ] PR Register — full PR list with status filters
- [ ] Inventory alerts — low/out-of-stock flags on dashboard

**Landing Page**
- [ ] Public landing page (matches Penpot `01 Landing` frame)

### v1.1 — Employee Profiles (Planned)

- [ ] Employee profile page — view and edit own contact info, position
- [ ] Admin can view all employee profiles
- [ ] Profile photo upload (Azure Blob Storage)

### v1.2 — Calendar Appointments + Announcements (Planned)

- [ ] Create calendar appointment — user creates event, visible to all
- [ ] Announcement post — Admin posts announcements shown on dashboard
- [ ] Notification bell — in-system alerts for low stock, PR status changes

---

## 12. First Deployment Guide (Azure — Step by Step)

> **Context:** This guide is written for a developer coming from a local IIS + SQL Server + installer background. It covers the one-time Azure setup for v0.1. After this is done, all future deployments are automatic on push to `main`.
>
> **Time estimate:** 45–90 minutes for the first full setup.
>
> **Prerequisites:** GitHub account, Azure free account (portal.azure.com — no credit card required for free tier resources used here).

---

### Step 1 — Create Your Azure Free Account

1. Go to [portal.azure.com](https://portal.azure.com)
2. Click **Start free** → sign in with a Microsoft account (or create one)
3. No credit card required for the free tier services used in this project

> **Free services used:** Azure Static Web Apps (Free plan), Azure Functions (Consumption plan), Azure SQL Database (Free offer). None of these require a credit card to create.

---

### Step 2 — Create the Azure SQL Database (Free Tier)

This is your cloud SQL Server. Think of it as setting up SQL Server on a remote machine that Microsoft manages.

1. In the Azure Portal, click **Create a resource** → search **Azure SQL** → select **SQL Database**
2. Fill in:
   - **Subscription:** your free subscription
   - **Resource group:** create new → name it `ppdo-portal-rg` *(groups all PPDO resources together)*
   - **Database name:** `ppdo-portal-db`
   - **Server:** click *Create new*
     - Server name: `ppdo-portal-sql` *(becomes `ppdo-portal-sql.database.windows.net`)*
     - Location: `Southeast Asia` *(closest to PH)*
     - Authentication: SQL authentication
     - Server admin login: `ppdo_admin` *(save this — you'll need it)*
     - Password: strong password *(save this — you'll need it)*
   - **Want to use SQL elastic pool?** No
   - **Workload environment:** Development
   - **Compute + storage:** click *Configure* → select **Free offer (Preview)** *(32 GB, ₱0)*
3. Click **Review + create** → **Create**
4. Wait ~3–5 minutes for deployment

**Connect via SSMS (same as local SQL Server):**
```
Server name:  ppdo-portal-sql.database.windows.net
Authentication: SQL Server Authentication
Login:        ppdo_admin
Password:     [your password]
```

> **Firewall note:** First time you connect from SSMS, Azure will prompt you to add your IP address to the firewall. Click yes — this allows your machine to connect. GitHub Actions gets its own firewall rule added later.

---

### Step 3 — Create the Azure Functions App

This is your backend API host — replaces IIS for the .NET API.

1. In Azure Portal → **Create a resource** → search **Function App** → **Create**
2. Fill in:
   - **Resource group:** `ppdo-portal-rg` *(same group as the database)*
   - **Function App name:** `ppdo-portal-api` *(becomes `ppdo-portal-api.azurewebsites.net`)*
   - **Runtime stack:** .NET
   - **Version:** 9 (isolated)
   - **Region:** Southeast Asia
   - **Hosting:** Consumption (Serverless) ← **important — this is the free tier**
   - **Operating System:** Windows
3. Click **Review + create** → **Create**

---

### Step 3b — Create Azure Application Insights

This is your monitoring and logging dashboard. Azure Functions sends all `ILogger<T>` output here automatically once the connection string is set.

1. In Azure Portal → **Create a resource** → search **Application Insights** → **Create**
2. Fill in:
   - **Resource group:** `ppdo-portal-rg`
   - **Name:** `ppdo-portal-insights`
   - **Region:** Southeast Asia
   - **Resource Mode:** Workspace-based
3. Click **Review + create** → **Create**
4. After creation, go to the resource → **Overview** → copy the **Connection String** (starts with `InstrumentationKey=...`)
   - You'll need this in Step 4

> **Cost:** Application Insights free tier includes **5 GB/month** data ingestion. For ~10 PPDO users this will never be exceeded. ₱0/month.

---

### Step 4 — Set Environment Variables (App Settings)

This replaces `web.config` / `appsettings.json` for sensitive values. Never put secrets in your code files.

1. Go to your Function App (`ppdo-portal-api`) → **Settings** → **Environment variables**
2. Add the following keys one by one (click **+ Add**):

| Name | Value |
|---|---|
| `SqlConnectionString` | `Server=tcp:ppdo-portal-sql.database.windows.net,1433;Database=ppdo-portal-db;User ID=ppdo_admin;Password=[yourpassword];Encrypt=True;` |
| `Jwt__SecretKey` | a long random string — 40+ characters, mix of letters/numbers/symbols |
| `Jwt__Issuer` | `https://[yourapp].azurestaticapps.net` *(fill in after Step 5)* |
| `Jwt__Audience` | `ppdo-portal` |
| `Jwt__AccessTokenExpiryMinutes` | `15` |
| `Jwt__RefreshTokenExpiryDays` | `7` |
| `APPLICATIONINSIGHTS_CONNECTION_STRING` | Connection string from Step 3b — Azure Portal → Application Insights → Overview → Connection String |

3. Click **Apply** → **Confirm**

> **Note:** The double underscore `Jwt__SecretKey` is how .NET reads nested config (`Jwt:SecretKey`) from environment variables. This is a .NET convention — not a typo.

---

### Step 5 — Create Azure Static Web Apps (Frontend + API Proxy)

This hosts the Next.js frontend AND automatically proxies `/api/*` requests to your Functions app. It also generates the GitHub Actions deploy file for you automatically.

1. In Azure Portal → **Create a resource** → search **Static Web App** → **Create**
2. Fill in:
   - **Resource group:** `ppdo-portal-rg`
   - **Name:** `ppdo-portal`
   - **Plan type:** Free ← **important**
   - **Region:** East Asia *(closest free-tier region to PH for SWA)*
   - **Source:** GitHub
   - Click **Sign in with GitHub** → authorize Azure
   - **Organization:** your GitHub username
   - **Repository:** `ppdo-portal`
   - **Branch:** `main`
   - **Build presets:** Next.js
   - **App location:** `/frontend`
   - **API location:** *(leave blank — we'll link Functions separately)*
   - **Output location:** `.next`
3. Click **Review + create** → **Create**

**What happens automatically:**
Azure creates a file in your repo:
`.github/workflows/azure-static-web-apps-[random].yml`

This is your auto-generated CI/CD pipeline. Every push to `main` triggers it — builds the Next.js app and deploys to Azure. **You don't need to write this file yourself.**

4. After creation, go to the SWA resource → **Settings** → **APIs** → **Link** → select your `ppdo-portal-api` Functions app

This links the Functions app so `/api/*` calls from the frontend automatically route to it — no CORS setup needed.

---

### Step 6 — Allow GitHub Actions to Access Azure SQL

GitHub Actions runs in Microsoft's cloud — its IP needs firewall access to your database.

1. Go to your SQL Server resource (`ppdo-portal-sql`) → **Security** → **Networking**
2. Under **Exceptions**, check **Allow Azure services and resources to access this server**
3. Click **Save**

> This setting allows any Azure service (including GitHub Actions) to connect. It does not expose the database to the public internet — a username and password are still required.

---

### Step 7 — Run EF Core Migrations Against Azure SQL

This creates all your tables in the Azure SQL database — same as running `Update-Database` locally, just pointing at Azure.

```bash
# From your solution root — run once to set up the cloud database
dotnet ef database update \
  --project backend/PPDO.Infrastructure \
  --startup-project backend/PPDO.Functions \
  --connection "Server=tcp:ppdo-portal-sql.database.windows.net,1433;Database=ppdo-portal-db;User ID=ppdo_admin;Password=[yourpassword];Encrypt=True;"
```

Verify in SSMS — connect to `ppdo-portal-sql.database.windows.net` and you should see all your tables created.

---

### Step 8 — Push to Main and Verify Deployment

```bash
git add .
git commit -m "chore: initial deployment setup"
git push origin main
```

1. Go to your GitHub repo → **Actions** tab
2. You'll see the workflow running — watch the build and deploy steps
3. Green checkmark = deployed successfully
4. Visit `https://[yourapp].azurestaticapps.net` — your app is live

---

### Post-Deployment Checklist

- [ ] Azure SQL Database created — free tier confirmed
- [ ] Azure Functions App created — Consumption plan confirmed
- [ ] App Settings added (connection string, JWT keys)
- [ ] Azure Static Web Apps created — linked to GitHub repo
- [ ] Functions app linked to SWA (`/api` proxy working)
- [ ] Azure Application Insights resource created
- [ ] `APPLICATIONINSIGHTS_CONNECTION_STRING` added to Function App Settings
- [ ] Azure SQL firewall — Azure services allowed
- [ ] EF Core migrations applied to Azure SQL
- [ ] GitHub Actions pipeline passing (green)
- [ ] Frontend loads at `https://[yourapp].azurestaticapps.net`
- [ ] Login endpoint responding at `https://[yourapp].azurestaticapps.net/api/auth/login`
- [ ] Update `Jwt__Issuer` in App Settings with the actual SWA URL
- [ ] Update `PROJECT_DOCUMENTATION_NET_AZURE.md` with actual URLs

---

### Troubleshooting Common First-Deploy Issues

| Problem | Likely Cause | Fix |
|---|---|---|
| GitHub Actions fails on build | Missing `local.settings.json` values in CI | Add secrets to GitHub repo → Settings → Secrets and variables → Actions |
| 500 error on API calls | Missing App Settings in Functions | Double-check all env vars in Azure Portal → Function App → Environment variables |
| Cannot connect from SSMS | Your IP not in SQL firewall | Azure Portal → SQL Server → Networking → add your current IP |
| `/api` calls return 404 | Functions not linked to SWA | Azure Portal → SWA → APIs → Link → select Functions app |
| EF migration fails | Wrong connection string or firewall | Check connection string format; ensure "Allow Azure services" is checked |
| App loads but login fails | `Jwt__Issuer` mismatch | Update `Jwt__Issuer` in App Settings to match your actual SWA URL |

---

### After First Deployment — Your Daily Workflow

Once everything above is done, your workflow going forward is simply:

```
1. Write code locally
2. Test locally (Functions on localhost:7071 + Next.js on localhost:3000)
3. git push origin main
4. GitHub Actions automatically builds + deploys to Azure
5. Done — no manual deployment steps ever again
```

No installer to build. No IIS to configure. No SQL scripts to bundle. Just push and it's live.

---

*Document version: v0.1 — 2026-05-26 — Ralph Armand Alcaide — PPDO Occidental Mindoro*  
*Stack: ASP.NET Core (.NET 9) on Azure Functions + Next.js 14 on Azure Static Web Apps + Azure SQL Database (Free)*