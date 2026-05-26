# Git Conventions

## Ralph's Personal Development Standard (RPDS)

---

## Branch Naming

All branches follow this pattern:

```
type/vX.Y-short-description
```

|Type|When to use|
|---|---|
|`feature/`|New feature or functionality|
|`fix/`|Bug fix|
|`refactor/`|Code restructuring with no behavior change|
|`chore/`|Maintenance tasks (dependencies, config, CI)|
|`docs/`|Documentation only changes|
|`test/`|Adding or updating tests|

### Examples

```
feature/v1.3-timekeeping
fix/v1.3-loggedat-utc
refactor/v1.4-clean-auth
chore/update-dependencies
docs/update-api-endpoints
test/timekeeping-service-unit-tests
```

### Rules

- Use **kebab-case** — lowercase, words separated by hyphens
- Keep descriptions **short and clear** — 2-4 words max
- Include version number for feature and fix branches — omit for chore/docs/test
- Never commit directly to `main` — always use a branch + pull request

---

## Branch Strategy

```
main              ← production branch — auto-deploys on push
  └── feature/    ← all development work happens here
  └── fix/
  └── refactor/
  └── chore/
```

- `main` is always deployable
- Feature branches are created from `main` and merged back via pull request
- Delete branches after merging

---

## Commit Messages

Follows [Conventional Commits](https://www.conventionalcommits.org/) specification.

### Format

```
type(scope): short description
```

### Types

|Type|When to use|
|---|---|
|`feat`|New feature|
|`fix`|Bug fix|
|`refactor`|Code change that neither fixes a bug nor adds a feature|
|`test`|Adding or updating tests|
|`chore`|Build process, dependencies, config changes|
|`docs`|Documentation only|
|`style`|Formatting, missing semicolons — no logic change|
|`perf`|Performance improvement|

### Scope

The scope is the area of the codebase affected — keep it short:

```
feat(timekeeping): add time log CRUD endpoints
fix(auth): remove RefreshToken FK to Users table
refactor(db): convert DateOnly filters to UTC
test(timekeeping): add unit tests for TimeLogService
chore(ci): add dotnet test step to pipeline
docs(readme): update environment variable list
style(frontend): fix inconsistent button spacing
```

### Rules

- Use **present tense** — "add feature" not "added feature"
- Use **lowercase** for the description
- Keep the description **under 72 characters**
- No period at the end
- If more detail is needed, add a blank line then a longer body

### Examples

```
feat(timekeeping): add CSV export for filtered time logs
fix(filters): correct UTC offset on DateOnly query params
refactor(auth): replace Include navigation with direct lookup
chore(deps): update EF Core to 9.0.5
docs(api): add timekeeping endpoints to PROJECT_DOCUMENTATION
test(auth): add refresh token rotation unit tests
```

---

## Pull Request Guidelines

- PR title should match the branch description
- Link the related Linear issue in the PR description
- Squash commits when merging if there are many small WIP commits
- At minimum, verify CI passes before merging to `main`

---

## Tags & Releases

Tag each version release on `main`:

```bash
git tag -a v1.3 -m "v1.3 — Timekeeping Feature"
git push origin v1.3
```

Tag format: `vX.Y` matching the roadmap version.

---

_Part of Ralph's Personal Development Standard (RPDS) v1.0_