# Ticket Implementation Prompt Standard

How to write the **Claude Code implementation prompt** that gets pasted into a Linear ticket (as a
comment, or the bottom of the description) to kick off that ticket. A good prompt is *self-contained*:
a fresh Claude Code session with no prior context should be able to act on it correctly.

`RAL-81` is the canonical reference example — match its shape.

---

## Required structure (in order)

1. **Context docs to read.** Always start with:
   `Read CLAUDE.md, PROJECT_DOCUMENTATION_NET_AZURE.md, and PPDO_PROJECT_CONTEXT.md.`
   Then point at the **authoritative spec** for this ticket and say to read it FULLY, e.g.
   `Read docs/v1.1.1/v1.1.1_Requirements.md §2C FULLY — it is the authoritative spec for this ticket.`
   The spec must satisfy `docs/SPEC_STANDARD.md`. If no spec exists and the ticket adds a table, an
   endpoint, a permission, or a screen, **write the spec first** — the ticket prompt is not the
   place to invent requirements.

2. **Files to read before writing code.** A bulleted list of **exact repo-relative paths** the
   implementer must read first, each with a short note on *why* (what pattern/contract it carries).
   This is the most important section — it front-loads the real code instead of guessing. Include
   the entity, its EF config, the service + interface, the DTO folder, the Functions file, the
   relevant test file, and (frontend) the component/lib/types it must match.

3. **Current behaviour → target behaviour.** One line each. State what the code does *today* and
   what it must do *after*. This is cheap and it catches the expensive failure: an implementer who
   misread the existing behaviour and "fixes" something that was never broken. Where the ticket
   changes nothing today (net-new feature), say `Current: does not exist.`

   > Ask for the current-behaviour line as *output*, not just as reading: an implementer who can
   > state today's behaviour in one sentence has actually read the file.

4. **Working branch + PR target.** State the integration branch, the feature-branch name, and the
   PR target explicitly — call out that it is **NOT `main`**, e.g.
   `Working branch: hotfix/1.1.1. Create feature/v1.1.1-ral-XX-… off hotfix/1.1.1 and open the PR against hotfix/1.1.1 (NOT main).`
   Branch names follow the **active development version**, not the ticket's own label.

5. **TDD instruction** (whenever there is Application/service logic):
   `TDD: extend <TestFile> with failing tests first, then implement.`

6. **Numbered implementation steps.** Concrete and ordered — migration → domain → application →
   functions → frontend. Name the methods, routes, DTOs, columns. Keep each step a few lines; the
   exhaustive detail lives in the spec doc (step 1), not here.

   Each step carries two things beyond the change itself:

   - **How to verify it.** The specific check — `dotnet test PPDO.Tests --filter X`, the exact
     screen and action, the curl against the route. **A step with no verification method is not a
     step, it is a hope.** This is the project's most consistent failure mode: defects found after
     merge rather than before.
   - **A flag if it needs a migration or a new dependency.** Mark the step
     `⚠️ MIGRATION` or `⚠️ NEW DEPENDENCY`. **CI does not run EF migrations** — a flagged step means
     a manual `dotnet ef database update` against Azure SQL at deploy time, and that must reach the
     PR body.

   Sequence so the **solution compiles and the test suite passes after every step**. Keep the layer
   order from `CLAUDE.md` — do *not* reorder to put risky unknowns first; each layer genuinely
   depends on the one before it. Surface risk through step 7 instead.

7. **Risks, rollback, and sign-off triggers.** For each material risk: what could break, and the
   **exact rollback** — the revert, the down-migration, the feature flag, the config value to
   restore. "Revert the commit" is only a real answer when no migration has run.

   Flag for **explicit sign-off before proceeding** anything that touches:

   - **Auth, JWT validation, or `PermissionService`** — a missing check is not caught by the compiler
   - **Division or office scope** — the guard against one office reading another's data
   - **EF migrations, especially destructive ones** (column drops, type changes, backfills)
   - **Production data** — anything run against Azure SQL by hand
   - **The shared design system or `components/ui/`** — blast radius is every page

   Adapt the list to the ticket; drop what does not apply rather than listing all five every time.

8. **Out-of-scope / "Do NOT".** Explicitly list deferred items, things that must NOT change
   (e.g. privilege-escalation guards), and anything a reasonable implementer might over-reach into.

9. **Commit message.** End with the Conventional Commits message to use, e.g.
   `When done, commit with:` then `feat(calendar): calendar event approval workflow (RAL-84)`.

---

## Conventions the prompt should reinforce

- **DB naming:** new tables/columns are snake_case; legacy PascalCase tables stay PascalCase
  (`docs/NAMING_CONVENTIONS.md`). Say which applies for this ticket.
- **Public vs JWT endpoints:** all triggers are `AuthorizationLevel.Anonymous`; JWT is enforced
  manually via `_jwt.ValidateAsync(...)`. Public routes are listed in `CLAUDE.md`. State whether the
  new endpoint is public or protected, and the role gate.
- **Response envelope:** `ApiResponse<T>` (`{ data, error, message }`) for protected/config-style
  endpoints; `ServiceResult` for service returns.
- **Frontend reuse:** reuse `components/ui/` (`Modal`, `DataTable`, `ConfirmDialog`, `useToast`);
  flat design (no rounded corners); the sidebar uses **emoji icons**, not an icon library.
- **Performance (`docs/PERFORMANCE_GUIDELINES.md`):** query at the DB, not in memory (no
  `GetAllAsync()` + in-memory filter/count/uniqueness — use scoped repo methods, `CountAsync`,
  `AnyAsync`); no `Task.WhenAll` over one `DbContext`; slim DTOs for list/grid endpoints; fetch
  shared state (`/auth/me`) once via context; loading states must not cause layout shift. Call out
  the relevant rule when the ticket adds a query, endpoint, or list view.
- **PR body:** the manual test plan checklist is **copied from the spec's Acceptance checklist**
  (`docs/SPEC_STANDARD.md` §2.10) — do not invent it at PR time. Note blocked-by / blocks
  relationships, and carry every `⚠️ MIGRATION` flag from the steps into the deploy note.

---

## Template

```
Read CLAUDE.md, PROJECT_DOCUMENTATION_NET_AZURE.md, and PPDO_PROJECT_CONTEXT.md.
Read <authoritative spec path + section> FULLY — it is the authoritative spec for this ticket.

Read these files before writing code:
- <path> (<why>)
- <path> (<why>)
- ...

Before changing anything, state in one line each:
- Current behaviour: <what it does today>
- Target behaviour: <what it must do after>

Working branch: <integration branch>.
Create <feature branch> off <integration branch> and open the PR against <integration branch> (NOT main).

TDD: extend <test file> with failing tests first, then implement.

1. <migration / domain step>            ⚠️ MIGRATION
   Verify: <exact check>
2. <application step — name methods, DTOs>
   Verify: dotnet test backend/PPDO.slnx --filter <TestClass>
3. <functions step — routes, auth, envelope>
   Verify: <route + expected status/shape>
4. <frontend step — page, components, lib, types>
   Verify: <screen + action + expected result>

Risks and rollback:
- <risk> → rollback: <exact action>

Stop and get my sign-off before: <auth/permissions | division-office scope |
destructive migration | prod data | shared components> — whichever apply.

Do NOT <deferred items / things that must not change>.

When done, commit with:
<type(scope): summary (RAL-XX)>
```

---

## Reference example — RAL-81

```
Read CLAUDE.md, PROJECT_DOCUMENTATION_NET_AZURE.md, and PPDO_PROJECT_CONTEXT.md.
Read docs/v1.1/User_Roles_Permissions.md FULLY — it is the authoritative access model.
Read these files before writing code:
- backend/PPDO.Domain/Entities/PermissionGroup.cs
- backend/PPDO.Domain/Entities/User.cs
- backend/PPDO.Application/Services/PermissionService.cs
- backend/PPDO.Application/Services/UserService.cs (GroupIdFor, create/update)
- backend/PPDO.Application/Services/InventoryService.cs and DistributionService.cs
  (null-scope semantics — see bug guards below)
- backend/PPDO.Tests/Application/PermissionServiceTests.cs

Working branch: release/1.1.0.
Create feature/v1.1-ral-81-budget-planning-permissions off release/1.1.0 and open the PR
against release/1.1.0 (NOT main).

TDD: extend PermissionServiceTests with failing tests first, then implement.

1. Migration: users.division → nullable; add users.office_id ...
2. PermissionService: CanAccessBudgetPlanningAsync, CanUploadAipAsync ...
3. NULLABLE-DIVISION BUG GUARDS (critical, compiler will not catch): ...
   ...

Do NOT implement the deferred items (forced password change, off JWT claim, etc.) —
they are documented in User_Roles_Permissions.md §9 for later.

When done, commit with:
feat(auth): add budget planning permissions, nullable division, and office users (RAL-81)
```

(See the full RAL-81 prompt on the Linear ticket for the complete numbered steps.)

> **Note:** RAL-81 predates the 2026-08-27 revision, so it lacks the current→target behaviour lines
> (§3), per-step verification and `⚠️ MIGRATION` flags (§6), and the risks/rollback/sign-off section
> (§7) — even though it is exactly the kind of ticket that needed all three: it changed
> `PermissionService`, made `users.division` nullable, and added `users.office_id`. Match its
> strengths, add the missing sections.

---

*Ticket Prompt Standard — PPDO Portal. Revised 2026-08-27: added current→target behaviour,
per-step verification, migration/dependency flags, and risks/rollback/sign-off triggers.
Companion to `docs/SPEC_STANDARD.md`.*
