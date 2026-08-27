# Learning Ticket Bank

Tickets scoped for **hand-coding rather than delegation** — small blast radius, an existing sibling
in the repo to pattern-match against, testable, reversible.

Written at **guidance level 1**: what to build and where the working example lives, *not* code to
paste. Escalate any entry on request — (2) signature + file location, (3) step outline, (4) the
code, explained.

Candidates are added here as a by-product of ticket planning (`CLAUDE.md` § "Flag
manual-implementation candidates when planning"). **Nothing here is scheduled** — v1.8.0 comes
first, and handing a ticket back is a normal, cost-free outcome.

| Status | Meaning |
|---|---|
| 🟢 Ready | Verified against the codebase, spec below is current |
| 🔵 Taken | Ralph is on it |
| ✅ Done | Merged |

---

## W1 🟢 Replace the one `DateTime.Now` in the codebase

**Warm-up. ~15 minutes. Backend, single line.**

`CLAUDE.md` forbids `DateTime.Now` — use `DateTime.UtcNow` and convert to UTC+8 where a human will
read it. There is **exactly one violation in ~72,000 lines**, and it is in a report footer, so it
is visibly wrong to a user in Manila for eight hours of every day.

- **Target:** `backend/PPDO.Infrastructure/Services/ExcelService.cs:1361` — a `Generated: …` footer
  string on the WFP report.
- **Sibling to copy the shape from:** `backend/PPDO.Application/Services/DeliveryService.cs` —
  see the static `ManilaZone` field, its `LoadManilaZone()` fallback (lines ~31–40), and the
  conversion at line ~232. `DistributionService.cs` does the same thing, which tells you something.

**What you'll learn:** the project's timezone rule and why `TimeZoneInfo.FindSystemTimeZoneById`
needs a fallback (the ID differs between Windows and the Linux Functions host).

**Worth noticing:** two services already declare their own private `ManilaZone`, and this would be
a third site. Whether that should become a shared helper — and where it would live, given
`ExcelService` is in Infrastructure while the other two are in Application — is a real design
question. Form your own view; it's a good first one to argue about.

**Verify:** `dotnet test backend/PPDO.slnx --filter WfpReportExcelServiceTests`, then export a WFP
report and read the footer.

---

## B1 🟢 Write the project's first FluentValidation validator

**The flagship starter. Backend, Application layer, TDD.**

**FluentValidation 11.11.0 is referenced in `PPDO.Application.csproj` and has zero usages in the
entire codebase.** RAL-235 built the `Validators/` folder structure mirroring `DTOs/` — all 14
folders contain nothing but a `.gitkeep`. Validation today lives as inline guards inside service
methods.

This ticket writes the first real one and establishes the pattern everything else follows.

- **Suggested target:** `CreateItemMasterDto` → `Validators/Items/CreateItemValidator.cs`
- **The spec already exists as code** — `ItemService.CreateAsync`
  (`backend/PPDO.Application/Services/ItemService.cs`, ~lines 113–123) currently guards:
  `StockNo` required · `Description` required · `Description` ≤ 300 chars (aligned to Price Index,
  RAL-226). The StockNo-already-exists check is a **database** concern and stays in the service —
  a validator does not get to query. Recognizing that split is most of the point of this ticket.
- **Test file:** `backend/PPDO.Tests/Application/ItemServiceTests.cs` already covers these
  behaviours. **They must still pass unchanged** — same messages, same `ServiceResult` codes.
  That existing suite is your safety net; write the validator's own tests first, then refactor.
- **DI:** services are registered in `backend/PPDO.Functions/Program.cs`. `CLAUDE.md` is strict —
  no `new ServiceName()` anywhere. `FluentValidation.DependencyInjectionExtensions` is already a
  package reference, which is a hint about the intended registration style.

**What you'll learn:** FluentValidation's rule syntax, DI registration, how validation failures map
onto `ServiceResult.BadRequest`, and TDD against a real safety net.

**The judgment call to make yourself:** whether the validator is invoked *inside* the service or at
the Functions boundary. `CLAUDE.md` says handlers validate input, call the service, return — but it
also says never put business logic in handlers. Both readings are defensible. **Decide, and be able
to say why** — whatever you pick becomes the precedent for every validator after it.

**Verify:** `dotnet test backend/PPDO.slnx --filter ItemService` — the existing tests must stay
green.

---

## B2 🟢 Delete the duplicated empty DTO and Validator folders

**Trivial. ~10 minutes. Good for a low-energy day.**

`DTOs/Item` and `DTOs/User` are **empty**; the real DTOs live in `DTOs/Items` and `DTOs/Users`. The
singular/plural mistake was then mirrored into `Validators/Item` and `Validators/User`, so there
are four folders that should not exist.

- **Check before deleting:** confirm nothing references the singular paths, and that no `.csproj`
  glob or namespace depends on them.
- **What you'll learn:** git does not track empty directories — which is why `.gitkeep` exists, and
  why deleting these behaves differently from deleting a folder with files in it.

---

## F1 🟢 Finish the `formatMoney` consolidation

**Frontend, contained, visible result.**

`frontend/src/lib/money.ts` is the documented single source of truth for peso display and is
imported by **12** files. There are still **18 raw `toLocaleString` / `Intl.NumberFormat` call
sites** across the portal pages.

- **First task is an audit, not an edit:** not all 18 are money — some format dates or counts.
  Work out which are peso amounts. **Report the split before changing anything.**
- **Sibling:** the 12 files already importing `formatMoney` show the intended shape.
- **Watch for:** any site whose options differ from `money.ts` (a different fraction-digit setting,
  a currency symbol). Those are not drive-by conversions — they are either bugs to fix or genuine
  exceptions to leave alone. Say which you think each is.

**What you'll learn:** how to scope a refactor honestly, and the discipline of separating "same
behaviour, less duplication" from "behaviour change."

**Verify:** `npm run build` and `npm run lint` in `frontend/`, then read the money columns on
Inventory and Budget Planning pages.

---

## C1 🟢 Add a post-deploy smoke test to `deploy.yml`

**CI/tooling. One workflow step.** *(Retrospective action item 2.)*

`GET /api/health` already exists, returns `{ status, api, database, utc }`, needs no auth, and
returns 503 when the database is unreachable — **and nothing calls it after a deploy.** A broken
deploy is currently discovered by a user.

- **Target:** `.github/workflows/deploy.yml`, after the Functions deploy step.
- **Behaviour:** poll the health endpoint until 200, with a timeout, and fail the job on 503 or
  timeout. **Allow for cold start** — Consumption plan scales to zero after ~10 minutes and takes
  5–20s to wake, so a single immediate request will produce false failures. That constraint is the
  interesting part of this ticket.
- **Host:** `ppdo-portal-api-sea…southeastasia-01.azurewebsites.net` (already in the workflow).

**What you'll learn:** GitHub Actions step syntax, conditions and exit codes, and how a deploy gate
differs from a test.

---

## C2 🟢 Add a frontend file-size ceiling to lint

**Tooling.** *(Retrospective action item 4.)*

Six page components exceed 1,000 lines, topping out at 2,057 — and that concentration lines up with
where the fix commits landed. A lint rule converts "keep files small" from an intention into a
machine-checked rule.

- **Target:** the ESLint config in `frontend/`.
- **Shape:** warn at ~600 lines, error at ~900, with **existing offenders grandfathered in an
  explicit allowlist that may only ever shrink.** Without the allowlist the rule cannot be adopted;
  with it, the list itself becomes the refactor backlog.
- **Rule to look up:** ESLint's `max-lines`, and how per-file overrides work.

**What you'll learn:** ESLint configuration and override precedence, plus the general technique of
introducing a rule into a codebase that already violates it.

---

## Not candidates (and why)

Excluded for feedback-loop and blast-radius reasons, **not difficulty**:

| Area | Why not |
|---|---|
| `PermissionService`, auth, JWT | A missing check is not caught by the compiler and not obviously wrong on screen. Wrong place to be learning. |
| EF migrations | Not reversible in the usual sense, and CI does not run them — a mistake reaches production by hand. |
| `aip/detail/page.tsx`, `wfp/entry/page.tsx` | 2,057 and 1,790 lines. You cannot hold them in your head, and neither can I. |
| `ExcelService.cs` | 1,828 lines, and its output is validated by eye against a government form. |

These open up later — the exclusion is about sequencing.

---

*Learning Ticket Bank — PPDO Portal — started 2026-08-27. Verified against the codebase at
commit `56db087`.*
