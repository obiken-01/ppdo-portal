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

2. **Files to read before writing code.** A bulleted list of **exact repo-relative paths** the
   implementer must read first, each with a short note on *why* (what pattern/contract it carries).
   This is the most important section — it front-loads the real code instead of guessing. Include
   the entity, its EF config, the service + interface, the DTO folder, the Functions file, the
   relevant test file, and (frontend) the component/lib/types it must match.

3. **Working branch + PR target.** State the integration branch, the feature-branch name, and the
   PR target explicitly — call out that it is **NOT `main`**, e.g.
   `Working branch: hotfix/1.1.1. Create feature/v1.1.1-ral-XX-… off hotfix/1.1.1 and open the PR against hotfix/1.1.1 (NOT main).`

4. **TDD instruction** (whenever there is Application/service logic):
   `TDD: extend <TestFile> with failing tests first, then implement.`

5. **Numbered implementation steps.** Concrete and ordered — migration → domain → application →
   functions → frontend. Name the methods, routes, DTOs, columns. Keep each step a few lines; the
   exhaustive detail lives in the spec doc (step 1), not here.

6. **Out-of-scope / "Do NOT".** Explicitly list deferred items, things that must NOT change
   (e.g. privilege-escalation guards), and anything a reasonable implementer might over-reach into.

7. **Commit message.** End with the Conventional Commits message to use, e.g.
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
- **PR body:** include a manual test plan checklist (`docs/TEST_CONVENTIONS.md`), note blocked-by /
  blocks relationships, and flag any new migration for the deploy step.

---

## Template

```
Read CLAUDE.md, PROJECT_DOCUMENTATION_NET_AZURE.md, and PPDO_PROJECT_CONTEXT.md.
Read <authoritative spec path + section> FULLY — it is the authoritative spec for this ticket.

Read these files before writing code:
- <path> (<why>)
- <path> (<why>)
- ...

Working branch: <integration branch>.
Create <feature branch> off <integration branch> and open the PR against <integration branch> (NOT main).

TDD: extend <test file> with failing tests first, then implement.

1. <migration / domain step>
2. <application step — name methods, DTOs>
3. <functions step — routes, auth, envelope>
4. <frontend step — page, components, lib, types>

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
