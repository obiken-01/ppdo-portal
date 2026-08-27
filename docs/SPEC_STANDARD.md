# Spec Standard

`docs/TICKET_PROMPT_STANDARD.md` step 1 tells every ticket prompt to *"read the authoritative spec
FULLY."* **This document defines what an authoritative spec has to contain to deserve that
sentence.**

It is not a new format. It codifies the structure the good specs in this repo already use
(`docs/v1.4.3/v1.4.3_Requirements.md`, `docs/v1.1.1/v1.1.1_Requirements.md`) and closes four gaps
that have been left to the implementer's judgment: **failure cases, UI states, error shapes, and a
verifiable acceptance checklist.**

---

## 1. Document taxonomy

Four kinds of doc live under `docs/vX.Y/`. Only the first is authoritative.

| Kind | Filename | Role |
|---|---|---|
| **Requirements** | `<Version>_Requirements.md` or `<Feature>_Requirements.md` | **Authoritative.** The source of truth a ticket implements against. This standard governs it. |
| **Findings** | `<Topic>_Findings.md` | Investigation output — what we learned about a real file, an external form, an API. Feeds a Requirements doc; never implemented directly. |
| **Design Spec** | `<Area>_Design_Spec.md` | UI layout and interaction detail too long to inline. Referenced *from* Requirements, not instead of it. |
| **Tickets** | `Tickets.md` / `Ticket_Prompts.md` | The ticket split and the pasteable prompts. Derived from Requirements. |

> A `_Findings.md` is **not** a spec. It records what is true; a spec records what we will build.
> Promoting findings into requirements is a deliberate step, not an assumption.

---

## 2. Required sections

In order. Sections marked **(gap)** are the ones this standard adds to existing practice.

### 2.1 Goal

Two to five sentences. What changes for the user, and why now. If this section is hard to write,
the feature is not understood yet — stop here rather than proceeding.

### 2.2 Decisions (settled)

Numbered, each stating the decision **and the reasoning behind it**. This is the section that
prevents re-litigating the same question three tickets later. Anything still open goes under a
separate **Open follow-ups (not blocking)** heading so a reader can tell a settled decision from a
deferred one at a glance.

### 2.3 Behaviour — given / when / then **(gap)**

Every case gets a row, not just the happy path:

| Case | Given | When | Then |
|---|---|---|---|
| Happy path | … | … | … |
| Edge: … | … | … | … |
| Failure: … | … | … | … |

Cover at minimum:

- **The happy path.**
- **Edge cases:** empty collections, first record, boundary values, zero/negative amounts,
  duplicate submissions, concurrent edits.
- **Failure states:** validation rejection, not found, forbidden by role, forbidden by
  division/office scope, upstream save failure.
- **Permission cases explicitly.** For every role that can reach the feature — SuperAdmin, Admin,
  Staff — and for a user whose `division_id` or `office_id` does **not** match the record. Scope
  leaks are not theoretical here; state the expected result per role.

### 2.4 API contract

For each endpoint: **method, route, auth requirement, request shape, success shape, error shapes,
and status codes.**

- State whether the route is **public or JWT-protected**, and which permission gate applies
  (`PermissionService.CanAccess…Async`). Public routes are enumerated in `CLAUDE.md` — if this one
  is not on that list, it is protected.
- State the envelope: `ApiResponse<T>` (`{ data, error, message }`) for protected/config-style
  endpoints; services return `ServiceResult`.
- **Error shapes are part of the contract, not an afterthought** — give the status code and the
  message shape for each failure case named in §2.3. Do not log or return anything on the
  never-log list in `CLAUDE.md`.
- List/grid endpoints: state the **slim DTO's exact fields** and the pagination parameters
  (`docs/PERFORMANCE_GUIDELINES.md`).

### 2.5 Data model changes

Tables and columns with types and nullability, plus the migration name.

- **New tables/columns are snake_case**, mapped from PascalCase C# via `IEntityTypeConfiguration`.
  **Legacy pre-v1.1 PascalCase tables keep PascalCase** — say which applies here
  (`docs/NAMING_CONVENTIONS.md`).
- Call out any **data backfill** as an explicit step of the same migration.
- Note indexes needed by the queries in §2.4.
- **If this section is non-empty, §2.8 must name the manual `dotnet ef database update` step** —
  CI does not run migrations.

### 2.6 UI states **(gap)**

For every screen the feature touches, all four states — plus the ones that get forgotten:

| State | Required content |
|---|---|
| **Loading** | Skeleton matching the loaded structure — same header, same row height. Never a centered spinner that a full table then replaces (`docs/PERFORMANCE_GUIDELINES.md`, CLS). |
| **Empty** | The exact empty-state copy and any call to action. |
| **Error** | What the user sees on a failed fetch or a rejected save, and how they recover. |
| **Success** | The loaded/saved view, including the toast or confirmation. |
| **Read-only / forbidden** | What a user without write permission sees — hidden control vs disabled control. Say which. |
| **Validation** | Per-field messages and where they render. |

Reuse from `components/ui/` (`Modal`, `DataTable`, `ConfirmDialog`, `useToast`) and follow
`docs/DESIGN_SYSTEM.md` — flat design, PPDO tokens only, emoji sidebar icons. **Name the components
this feature will use, and any new one it introduces.**

> This section exists because UI defects were the single largest fix category in the project's
> first 86 days. Enumerating states costs minutes in a spec and catches bugs that unit tests
> structurally cannot reach.

### 2.7 Non-goals **(gap)**

What this spec **deliberately does not do** — stated so an implementer does not helpfully build it.
Distinct from *Open follow-ups*: a non-goal is out of scope by choice; a follow-up is deferred work
we intend to return to. Where a non-goal is likely to tempt someone, say why it is excluded.

### 2.8 Deployment notes

- **New migrations** — name each, and state that `dotnet ef database update` must be run manually
  against Azure SQL. **CI does not run migrations.**
- New NuGet or NPM dependencies.
- New environment variables or Azure configuration (including CORS origins, which are set in the
  Azure Portal, not `host.json`).
- Anything that must happen in a specific order relative to the deploy.

### 2.9 Ticket split

The proposed tickets with their blocked-by / blocks relationships. Each should be independently
mergeable and map to a section of this spec.

### 2.10 Acceptance checklist **(gap)**

Checkbox lines, each one **verifiable by a person against the running app** — not a restatement of
intent.

```
- [ ] A Staff user in Division A opening the list sees only Division A records
- [ ] Submitting with an empty Program field shows "Program is required" under the field
- [ ] The table shows a skeleton with 5 rows on first load, not a spinner
```

Not `- [ ] Permissions work correctly.` If a line cannot be checked by doing something and looking
at the result, rewrite it until it can.

This checklist is the source of the PR's manual test plan (`docs/TEST_CONVENTIONS.md`) — write it
once, here, and copy it into the PR rather than inventing it at PR time.

### 2.11 Test focus

Which service test classes get new tests, and the specific behaviours to cover. Per `CLAUDE.md`,
TDD is mandatory for business logic, validators, auth flows, and permission resolution.

---

## 3. The deviation protocol

**The spec is the source of truth, not the code.**

When implementation reveals the spec is wrong or incomplete:

1. **Stop.** Do not silently implement something different because it is easier or obviously better.
2. **Say what the spec assumed and what reality turned out to be.**
3. **Get agreement on the change.**
4. **Update the spec, in the same PR as the code that deviates.**
5. Then continue.

A spec that quietly diverges from the code is worse than no spec, because the next ticket will be
written against the fiction. The cost of step 4 is a few minutes; the cost of skipping it compounds
silently across every later ticket that trusts the document.

---

## 4. Template

```markdown
# <Version / Feature> — <Short Title>

## 1. Goal
<What changes for the user, and why now.>

## 2. Decisions (settled)
1. <Decision> — <why>
### Open follow-ups (not blocking)
- <question> — <who/what unblocks it>

## 3. Behaviour
| Case | Given | When | Then |
|---|---|---|---|
| Happy path | | | |
| Edge: | | | |
| Failure: | | | |
| Role: Staff, other division | | | |

## 4. API contract
### `<METHOD> /api/<route>` — <public | JWT + PermissionService.CanX>
- Request: <shape>
- 200: `ApiResponse<T>` where T = <shape>
- 4xx: <status> — <error shape / message>

## 5. Data model changes
Migration: `<PascalCaseName>`
### `<table_name>` (snake_case | legacy PascalCase)
| Column | Type | Null | Notes |
Backfill: <steps, or none>

## 6. UI states
### <Screen>
| State | Content |
| Loading | |
| Empty | |
| Error | |
| Success | |
| Read-only / forbidden | |
| Validation | |
Components: <reused from components/ui/ | new>

## 7. Non-goals
- <thing we are deliberately not building> — <why>

## 8. Deployment notes
- Migrations: <name> — run `dotnet ef database update` manually (CI does not)
- Dependencies / config: <or none>

## 9. Ticket split
| Ticket | Scope | Blocked by |

## 10. Acceptance checklist
- [ ] <verifiable by a person against the running app>

## 11. Test focus
- `<ServiceTests.cs>` — <behaviours>
```

---

## 5. Reference examples

| Doc | Good for |
|---|---|
| `docs/v1.4.3/v1.4.3_Requirements.md` | Decisions-with-reasoning, data model changes, ticket split |
| `docs/v1.1.1/v1.1.1_Requirements.md` | Per-ticket sectioning (A/B/C/D), deployment notes |

Both predate this standard and so lack §2.3, §2.6, §2.7, and §2.10. **Match their strengths, add
the missing sections** — do not treat their omissions as precedent.

---

## 6. When a spec is not required

Not everything needs one. Skip straight to a ticket prompt for:

- A single-file bug fix with no behaviour change beyond "it now does what it already claimed"
- Copy, label, or styling changes
- Dependency bumps, config, CI changes

**Anything that adds a table, an endpoint, a permission, or a screen needs a spec.** If it touches
permissions, division/office scope, or money, it needs one regardless of size.

---

*Spec Standard — PPDO Portal — added 2026-08-27. Companion to `docs/TICKET_PROMPT_STANDARD.md`.*
