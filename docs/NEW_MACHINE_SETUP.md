# New Machine Setup

Everything needed to get the PPDO Portal building, running, and deployable on a fresh Windows
machine — including the tooling Claude Code uses, not just what the app needs.

Versions below are taken from the repo itself (`.csproj` target frameworks, `package.json`,
`.github/workflows/ci.yml`), not from memory. `README.md` §Local Development covers day-to-day
running; **this doc covers going from a blank machine to that point.**

---

## Part 0 — Before you retire the old laptop ⚠️

**Do this first. These things exist on that machine and nowhere else.**

### 0.1 Push everything

As of 2026-08-27 the old machine held **4 branches with unpushed commits** and **3 stashes**:

| Branch | Unpushed |
|---|---|
| `feature/v1.8-retro` | 4 commits |
| `docs/v1.8.0-open-questions-refresh` | 1 commit |
| `feature/v1.1-default-password` | 1 commit |
| `feature/v1.1.1-ral-87-announcements-public-page` | 1 commit |

Check the current state, then push anything real:

```bash
git for-each-ref --format='%(refname:short)' refs/heads | ForEach-Object { $n = git rev-list --count --no-merges $_ --not --remotes; if ($n -gt 0) { "$_ — $n unpushed" } }
```

**Stashes are not pushed by `git push` and are lost with the machine.** List them, and either apply
and commit or explicitly discard:

```bash
git stash list
```

### 0.2 Copy the git-ignored config files

These are deliberately never committed and must be carried over or recreated:

- `backend/PPDO.Functions/local.settings.json`
- `frontend/.env.local`
- `frontend/.env.production.local`
- `.claude/settings.local.json` (Claude Code permissions — convenience, not critical)

Copy them to a USB drive or a private note. Templates are in Part 5 if you'd rather retype them.

### 0.3 Other machine-local things

- **Any local database data** you care about. `PPDOPortalDev` on SQL Express is local only —
  export a `.bacpac` if there is test data worth keeping. Usually there isn't; the seed rebuilds.
- **Browser-saved credentials** for Azure Portal, Linear, GitHub.
- **The other repos** in `D:\RalphFiles\` — this checklist only covers `ppdo-portal`.

---

## Part 1 — Core toolchain

| # | Tool | Version | Why |
|---|---|---|---|
| 1 | **Git for Windows** | latest | Version control; also provides the Bash shell Claude Code uses |
| 2 | **.NET SDK** | **9.0** (`net9.0`) | Backend targets .NET 9. Get the SDK, not just the runtime |
| 3 | **Visual Studio 2026 Community** | 18.6+ | Backend development. **Include the "Azure development" workload** |
| 4 | **VS Code** | latest | Frontend development |
| 5 | **Node.js** | **20 LTS** | Pinned in CI (`node-version: '20'`). Includes npm |
| 6 | **SQL Server Express** | 2022 | Local `PPDOPortalDev` database |
| 7 | **SSMS** | 22.x | Database GUI |
| 8 | **Python** | 3.11+ | Repo scripts (`scripts/linear_archive.py`) and Claude's tooling — see Part 8 |

> ⚠️ **The solution file is `backend/PPDO.slnx`** — the newer XML solution format. It needs a recent
> Visual Studio (2022 17.13+ or 2026) **or** .NET SDK 9.0.200+ for `dotnet build`. An older VS will
> not open it. If you hit "unsupported solution format," your SDK or VS is too old.

---

## Part 2 — Global CLI tools

Install after Node and the .NET SDK:

```bash
npm install -g azure-functions-core-tools@4 --unsafe-perm true
```

```bash
npm install -g @azure/static-web-apps-cli
```

```bash
dotnet tool install --global dotnet-ef
```

`dotnet-ef` is required for migrations — **CI does not run them**, so every release containing a
migration is applied by hand from your machine.

Verify all three:

```bash
func --version; swa --version; dotnet ef --version; node --version; dotnet --version
```

---

## Part 3 — Claude Code and connectors

1. **Install Claude Code** and sign in.
2. **Reconnect the MCP connectors** — these are account-level, but confirm they work on the new
   machine. In use as of 2026-08-27: **Linear**, **Google Drive**, **Google Calendar**, **Gmail**,
   **Figma**.
3. **`.claude/settings.local.json`** is git-ignored — permissions will prompt again from scratch
   until it's rebuilt or copied. Not critical; it just means more approval prompts early on.
4. **Memory carries over automatically** — it lives in your Claude account, not the machine.

> The **Linear API key** used by `scripts/linear_archive.py` is read from the `LINEAR_API_KEY`
> environment variable and is deliberately not stored anywhere in the repo. If the archive job is
> ever needed again, mint a fresh key then — do not carry the old one over.

---

## Part 4 — Git configuration

```bash
git config --global user.name "obiken01"
```

```bash
git config --global user.email "ralpharmand.alcaide@gmail.com"
```

**Line endings — set this before cloning.** The old machine used `core.autocrlf=true`, which is
correct for Windows and keeps LF in the repo while checking out CRLF locally:

```bash
git config --global core.autocrlf true
```

Getting this wrong after cloning shows every file as modified in a diff.

Then authenticate to GitHub — either the `gh` CLI (`gh auth login`) or an SSH key. If you generate
a new SSH key, add it at GitHub → Settings → SSH and GPG keys.

---

## Part 5 — Clone and local config

```bash
git clone https://github.com/obiken-01/ppdo-portal.git
```

### 5.1 `backend/PPDO.Functions/local.settings.json`

Never committed. Create it by hand:

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

### 5.2 `frontend/.env.local`

```env
NEXT_PUBLIC_API_BASE_URL=http://localhost:7071/api
```

Change to `/api` when running through the SWA CLI on `localhost:4280`.

### 5.3 Install dependencies

```bash
npm install --prefix frontend
```

```bash
dotnet restore backend/PPDO.slnx
```

---

## Part 6 — Database

Create the local database and apply all **37 migrations**:

```bash
dotnet ef database update --project backend/PPDO.Infrastructure --startup-project backend/PPDO.Functions
```

Uses Windows Authentication against `.\SQLEXPRESS` — no username or password needed locally.

Seeded SuperAdmin: `superadmin@ppdo.gov.ph` / `PPDOAdmin2026!`
Default password for new users: `TamarawUser2026!`

---

## Part 7 — Verify the machine actually works

Do these in order. Each one proves a different part of the stack.

**1. Backend builds and the full suite passes** — this is the strongest single signal, and it needs
no running app:

```bash
dotnet test backend/PPDO.slnx
```

Expect **1,061 tests passing** across 40 test classes. Anything less means the toolchain is off.

**2. Frontend builds and lints:**

```bash
npm run build --prefix frontend
```

```bash
npm run lint --prefix frontend
```

**3. Functions host starts:**

```bash
func start --script-root backend/PPDO.Functions
```

Then hit `http://localhost:7071/api/health` — it should return `{ status, api, database, utc }` with
200, which proves the database connection too. A 503 means the API is up but SQL is not reachable.

**4. Frontend runs** (separate terminal):

```bash
npm run dev --prefix frontend
```

Log in at `http://localhost:3000` and confirm the sidebar shows the expected version string.

---

## Part 8 — What Claude uses (not the app)

Worth installing even though the application does not need it:

- **Python 3.11+** — `scripts/linear_archive.py` runs on it, and Claude uses Python for CSV/Excel
  work, data conversion, and analysis scripts. The `.xlsx` trackers in `docs/v1.8/` are handled this
  way.
- **Git Bash** — comes with Git for Windows; Claude uses it for POSIX shell work alongside
  PowerShell.
- Make sure `python` resolves on `PATH` (`python --version`). On Windows the Microsoft Store shim
  can shadow a real install — if `python` opens the Store, fix `PATH` or install from python.org.

---

## Part 9 — Accounts and access

Not software, but needed before you can ship:

- **Azure Portal** — resource group `ppdo-portal-rg`. Needed for CORS settings, Function App
  config, and running migrations against Azure SQL.
- **GitHub** — push access to `obiken-01/ppdo-portal`; Actions deploys on merge to `main`.
- **Linear** — `RalphOksiProjects` workspace.
- **Azure SQL firewall** — ⚠️ **a new machine has a new public IP.** Running
  `dotnet ef database update` against Azure SQL will fail until that IP is added to the
  `ppdo-portal-server` firewall rules. This one is easy to forget and looks like a connection-string
  bug.

---

## Quick checklist

```
BEFORE WIPING THE OLD MACHINE
[ ] Push all 4 branches with unpushed commits
[ ] Resolve or discard the 3 stashes
[ ] Copy local.settings.json, .env.local, .env.production.local
[ ] Check other repos under D:\RalphFiles\

INSTALL
[ ] Git for Windows      [ ] .NET 9 SDK        [ ] Visual Studio 2026 (+Azure workload)
[ ] VS Code              [ ] Node 20 LTS       [ ] SQL Server Express 2022
[ ] SSMS                 [ ] Python 3.11+      [ ] Claude Code
[ ] func core tools v4   [ ] SWA CLI           [ ] dotnet-ef

CONFIGURE
[ ] git user.name / user.email / core.autocrlf true
[ ] GitHub auth (gh or SSH)
[ ] Clone repo
[ ] local.settings.json + .env.local
[ ] npm install, dotnet restore
[ ] dotnet ef database update
[ ] Reconnect MCP connectors
[ ] Add new machine IP to Azure SQL firewall

VERIFY
[ ] dotnet test -> 1,061 passing
[ ] npm run build + lint
[ ] func start -> /api/health returns 200
[ ] npm run dev -> log in successfully
```

---

*New Machine Setup — PPDO Portal — written 2026-08-27, current as of v1.7.4.
Versions verified against the repo, not recalled. Update when the toolchain moves.*
