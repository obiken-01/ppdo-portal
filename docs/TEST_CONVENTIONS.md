# Test Conventions

## Ralph's Personal Development Standard (RPDS) — Phase 4: Testing

> **Core principle:** Apply TDD where it adds value. Not every line of code needs a test, but every critical business rule, service method, and integration point should be covered. Tests are a first-class citizen — they live in the repo, run in CI, and block deployments if they fail.

---

## Test Types

Choose the appropriate test types based on project complexity. Not all projects need all types.

|Type|What it tests|When to use|
|---|---|---|
|**Unit**|Individual functions, methods, classes in isolation|Always — for all service and domain logic|
|**Integration**|Multiple components working together (e.g. service + DB)|When DB queries or external services are involved|
|**End-to-End (E2E)**|Full user flows through the UI|For critical user journeys in complex frontends|
|**Contract**|API request/response shape consistency|When multiple teams consume the same API|

---

## TDD Approach

### When to apply TDD

- ✅ Business logic and service methods
- ✅ Validation rules
- ✅ Complex algorithms or calculations
- ✅ Authentication and authorization flows
- ✅ Data transformation and mapping
- ⚠️ Simple CRUD with no logic — write tests after, not before
- ⚠️ UI components — test behavior, not implementation details
- ❌ Configuration files, migrations, boilerplate

### The TDD Cycle

```
1. Write a failing test (Red)
2. Write the minimum code to make it pass (Green)
3. Refactor — clean up without breaking tests (Refactor)
4. Repeat
```

### Pragmatic TDD

If strict TDD slows you down on a deadline, write tests alongside implementation — not strictly before, but not after deployment either. Tests must exist before the PR is merged.

---

## Recommended Frameworks

|Language / Platform|Unit & Integration|E2E|
|---|---|---|
|C# / .NET|xUnit + Moq|Playwright|
|JavaScript / React|Vitest + Testing Library|Playwright / Cypress|
|TypeScript / Node.js|Vitest / Jest|Playwright|
|Python|pytest|Playwright|

---

## Test Naming Convention

### Format

```
MethodName_Scenario_ExpectedResult
```

### Examples

**C# / xUnit:**

```csharp
// Good
LoginAsync_WithValidCredentials_ReturnsTokens()
LoginAsync_WithInvalidPassword_ThrowsUnauthorizedException()
LoginAsync_WithDeactivatedAccount_ThrowsUnauthorizedException()
GetFilteredAsync_WithDateRange_ReturnsLogsWithinRange()
ExportCsvAsync_WithNoLogs_ReturnsEmptyCsv()

// Bad
TestLogin()
Login_Works()
Test1()
```

**JavaScript / Vitest:**

```javascript
// Good
describe('loginAsync', () => {
  it('returns tokens when credentials are valid', ...)
  it('throws UnauthorizedError when password is invalid', ...)
  it('throws UnauthorizedError when account is deactivated', ...)
})

// Bad
it('works', ...)
it('test login', ...)
```

**Python / pytest:**

```python
# Good
def test_login_returns_tokens_when_credentials_are_valid():
def test_login_raises_unauthorized_when_password_is_invalid():
def test_get_filtered_returns_logs_within_date_range():

# Bad
def test_login():
def test1():
```

---

## Test Project Structure

### C# / .NET

```
Solution/
├── ProjectName.Api/
├── ProjectName.Application/
├── ProjectName.Domain/
├── ProjectName.Infrastructure/
└── ProjectName.Tests/              ← single test project
    ├── Unit/
    │   ├── Services/
    │   │   ├── AuthServiceTests.cs
    │   │   └── TimeLogServiceTests.cs
    │   └── Domain/
    │       └── RefreshTokenTests.cs
    └── Integration/
        ├── Repositories/
        │   └── TimeLogRepositoryTests.cs
        └── Controllers/
            └── TimeLogControllerTests.cs
```

### JavaScript / React

```
src/
├── components/
│   └── TopMenu/
│       ├── TopMenu.jsx
│       └── TopMenu.test.jsx        ← co-located with component
├── timekeeping/
│   ├── TimeLogPage.jsx
│   └── TimeLogPage.test.jsx
└── utils/
    ├── helpers.js
    └── helpers.test.js
```

### Python

```
project/
├── src/
│   └── services/
│       └── time_log_service.py
└── tests/
    ├── unit/
    │   └── test_time_log_service.py
    └── integration/
        └── test_time_log_repository.py
```

---

## Test Coverage

### Minimum expectations

- **Service layer:** 80% coverage minimum
- **Domain/business logic:** 90% coverage minimum
- **Controllers/API layer:** Key happy path + error cases covered
- **Repositories:** Integration tests for complex queries
- **UI components:** Critical user interactions covered

### What NOT to obsess over

- 100% coverage is not the goal — meaningful tests are
- Don't test framework code (EF Core internals, MUI components)
- Don't test getters/setters with no logic
- Don't write tests just to hit a coverage number

---

## CI/CD Integration

Tests must run in the CI/CD pipeline on every push. **Build fails if any test fails.**

### GitHub Actions example

```yaml
# .github/workflows/ci.yml

# Backend (.NET)
- name: Run tests
  run: dotnet test --no-build --verbosity normal
  working-directory: ./YourProject

# Frontend (JavaScript)
- name: Run tests
  run: npm run test -- --run
  working-directory: ./YourFrontend
```

### Rules

- Tests must pass before merging to `main`
- Never merge with skipped or ignored tests without documented reason
- Flaky tests must be fixed or removed — not ignored

---

## Test Data & Mocking

### General rules

- Unit tests use mocked dependencies (never hit real DB or external APIs)
- Integration tests may use a test database — never the production DB
- Use realistic test data — avoid `"test"`, `"foo"`, `"123"` as values
- Clean up test data after integration tests run

### C# / Moq example

```csharp
var mockUow = new Mock<IUnitOfWork>();
mockUow.Setup(u => u.TimekeepingUsers.GetByEmailAsync("test@test.com"))
       .ReturnsAsync(new TimekeepingUser { ... });

var service = new TimekeepingAuthService(mockUow.Object, mockTokenService.Object, mockPasswordService.Object);
```

### JavaScript / Vitest example

```javascript
vi.mock('./api/timekeepingApi', () => ({
  default: {
    get: vi.fn().mockResolvedValue({ data: { data: { items: [], totalCount: 0 } } })
  }
}))
```

---

## Running Tests Locally

Document the test commands for each project in `CLAUDE.md` and `PROJECT_DOCUMENTATION.md`:

```bash
# .NET
dotnet test

# JavaScript / Vitest
npm run test

# JavaScript / Vitest (single run, no watch)
npm run test -- --run

# Python
pytest

# Python with coverage
pytest --cov=src --cov-report=term-missing
```

---

_Part of Ralph's Personal Development Standard (RPDS) v1.0 — Phase 4: Testing_