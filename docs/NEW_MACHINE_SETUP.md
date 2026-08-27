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

Versions in the **"Old machine"** column were read off the current laptop on 2026-08-27 — match or
exceed them.

### Required — the app will not build without these

| # | Tool | Old machine | Why |
|---|---|---|---|
| 1 | **Git for Windows** | 2.39.1 | Version control; also the Bash shell Claude Code uses |
| 2 | **.NET SDK** | **10.0.400** | Backend targets `net9.0`; SDK 10 builds it fine via roll-forward — see the note below |
| 3 | **Visual Studio 2026 Community** | 18.9.2 | Backend development. **Include the "Azure development" workload** |
| 4 | **VS Code** | 1.134.0 | Frontend development |
| 5 | **Node.js** | **22.16.0** | CI pins **20** — see the note below. Includes npm (10.9.2) |
| 6 | **SQL Server 2022** | 16.0.1190 | Local `PPDOPortalDev` database |
| 7 | **SSMS** | 22.9.1 | Database GUI |
| 8 | **Python** | 3.14.3 | Repo scripts and Claude's tooling — see Part 8 |

### Supporting tools — installed and in regular use

| Tool | Old machine | Used for |
|---|---|---|
| **Postman** | 12.18.0 | Hitting the Functions API directly — testing endpoints without the frontend, checking JWT flows and `ApiResponse<T>` shapes |
| **Compact Log Format Viewer** | 1.4.0 | Reading `.clef`/structured logs. **Microsoft Store app** (publisher: Warren Buckley) — not a normal installer |
| **Docker Desktop** | 28.5.1 | Containers / local service dependencies |
| **Obsidian** | 1.12.4 | Notes |
| **Notepad++** | 8.9.6.4 | Quick file edits, large-file viewing |
| **GitHub CLI (`gh`)** | 2.92.0 | Auth and PR work from the terminal |
| **Windows Terminal** | 1.24 | Store app |
| **Claude desktop** | 1.37937.3 | Store app |

> ⚠️ **The solution file is `backend/PPDO.slnx`** — the newer XML solution format. It needs a recent
> Visual Studio (2022 17.13+ or 2026) **or** .NET SDK 9.0.200+ for `dotnet build`. An older VS will
> not open it. If you hit "unsupported solution format," your SDK or VS is too old.

> ⚠️ **Two version mismatches carried over from the old machine — reproduce them knowingly, or fix
> them deliberately. Do not fix them by accident while setting up:**
>
> - **.NET SDK 10.0.400 is the only SDK installed**, while every project targets `net9.0`. This
>   works because the SDK builds older target frameworks, and it is what the code has been built
>   with. Installing the 9.0 SDK as well is harmless; installing *only* 10 matches today's setup.
> - **Node 22.16.0 locally vs `node-version: '20'` in `.github/workflows/ci.yml`.** Local and CI
>   have been on different major versions the whole time. Node 22 is the safer choice for the new
>   machine since that is what the code was actually developed against — but the real fix is to
>   align CI and local, and pick the version on purpose rather than by drift.

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
migration is applied by hand from your machine. The old machine ran **dotnet-ef 10.0.5**.

Verify:

```bash
func --version; swa --version; dotnet ef --version; node --version; dotnet --version; gh --version; docker --version
```

> ⚠️ **The SWA CLI was documented but never actually installed on the old machine** — `swa` was not
> on `PATH` as of 2026-08-27, despite `CLAUDE.md` listing it under local development. So the
> `localhost:4280` proxy workflow has not been in real use; the Terminal 1 + Terminal 2 setup is
> what actually gets run. Install it if you want that workflow, but know it is new ground rather
> than something being restored.

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

- **Python** — `scripts/linear_archive.py` runs on it, and Claude uses Python for CSV/Excel work,
  data conversion, and analysis scripts. The `.xlsx` trackers in `docs/v1.8/` and the Linear export
  conversion were all done this way.

  The old machine had **two installs — 3.11.9 and 3.14.3** — with `python` resolving to **3.14.3**,
  plus the Store's Python Manager. One install is enough; just confirm which one `python` resolves
  to, because that is the one Claude's scripts will use.

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

INSTALL — required
[ ] Git for Windows      [ ] .NET SDK 10       [ ] Visual Studio 2026 (+Azure workload)
[ ] VS Code              [ ] Node 22 LTS       [ ] SQL Server 2022
[ ] SSMS 22              [ ] Python 3.14       [ ] Claude Code
[ ] func core tools v4   [ ] dotnet-ef         [ ] GitHub CLI (gh)
[ ] SWA CLI (optional — was never installed on the old machine)

INSTALL — supporting tools in regular use
[ ] Postman              [ ] Docker Desktop    [ ] Obsidian
[ ] Notepad++            [ ] Windows Terminal (Store)
[ ] Compact Log Format Viewer (Microsoft Store — publisher Warren Buckley)

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
