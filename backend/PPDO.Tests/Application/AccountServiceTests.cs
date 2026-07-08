using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using PPDO.Application.Common;
using PPDO.Application.DTOs.Config;
using PPDO.Application.Services;
using PPDO.Domain.Entities;
using PPDO.Domain.Interfaces;

namespace PPDO.Tests.Application;

/// <summary>
/// Unit tests for <see cref="AccountService"/> (RAL-70 + RAL-77).
/// Covers the accountType prefix filter, CSV upsert counts, key uniqueness, soft delete,
/// and audit log calls on create / update / deactivate.
/// IRepository&lt;Account&gt; and IAuditService are mocked; no database access occurs.
/// </summary>
public sealed class AccountServiceTests
{
    /// <summary>
    /// Mirrors the pre-RAL-117 prefix rule, used only to seed test fixtures with a
    /// plausible ExpenseClass — production data comes from the migration backfill /
    /// CSV import, never from this derivation at read time anymore.
    /// </summary>
    private static string DeriveExpenseClass(string accountNumber) => accountNumber switch
    {
        _ when accountNumber.StartsWith("5-01-", StringComparison.OrdinalIgnoreCase) => "PS",
        _ when accountNumber.StartsWith("5-02-", StringComparison.OrdinalIgnoreCase) => "MOOE",
        _ when accountNumber.StartsWith("5-03-", StringComparison.OrdinalIgnoreCase) => "CO",
        _ => "Other",
    };

    private static Account Acct(int id, string number, string title, bool active = true, string? expenseClass = null) => new()
    {
        Id = id, AccountNumber = number, AccountTitle = title, IsActive = active,
        ExpenseClass = expenseClass ?? DeriveExpenseClass(number),
        CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow,
    };

    private static (AccountService sut, Mock<IRepository<Account>> repo) Build(
        List<Account> seed, IAuditService? audit = null)
    {
        Mock<IRepository<Account>> repo = new();
        repo.Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>())).ReturnsAsync(seed);
        repo.Setup(r => r.AddAsync(It.IsAny<Account>(), It.IsAny<CancellationToken>()))
            .Callback<Account, CancellationToken>((a, _) => seed.Add(a))
            .Returns(Task.CompletedTask);
        repo.Setup(r => r.UpdateAsync(It.IsAny<Account>(), It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        repo.Setup(r => r.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);
        return (new AccountService(repo.Object, NullLogger<AccountService>.Instance,
            audit ?? Mock.Of<IAuditService>()), repo);
    }

    private static (AccountService sut, Mock<IRepository<Account>> repo, Mock<IAuditService> audit)
        BuildWithAudit(List<Account> seed)
    {
        Mock<IAuditService> audit = new();
        audit.Setup(a => a.LogAsync(
            It.IsAny<string>(), It.IsAny<int>(), It.IsAny<string>(),
            It.IsAny<object?>(), It.IsAny<object?>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        (AccountService sut, Mock<IRepository<Account>> repo) = Build(seed, audit.Object);
        return (sut, repo, audit);
    }

    // ── accountType prefix filter ─────────────────────────────────────────────

    [Fact]
    public async Task GetAllAsync_AccountTypeMOOE_ReturnsOnly5_02_Prefix()
    {
        List<Account> seed =
        [
            Acct(1, "5-01-01-010", "Salaries"),       // PS
            Acct(2, "5-02-03-990", "Other MOOE"),     // MOOE
            Acct(3, "5-02-99-990", "More MOOE"),      // MOOE
            Acct(4, "5-03-01-010", "Office Equip"),   // CO
        ];
        (AccountService sut, _) = Build(seed);

        IReadOnlyList<AccountDto> result =
            await sut.GetAllAsync(search: null, accountType: "MOOE", active: ActiveFilter.All);

        Assert.Equal(2, result.Count);
        Assert.All(result, a => Assert.StartsWith("5-02-", a.AccountNumber));
        Assert.All(result, a => Assert.Equal("MOOE", a.AccountType));
    }

    [Fact]
    public async Task GetAllAsync_AccountTypePS_DerivesTypeAndFiltersByPrefix()
    {
        List<Account> seed = [Acct(1, "5-01-01-010", "Salaries"), Acct(2, "5-03-01-010", "Equip")];
        (AccountService sut, _) = Build(seed);

        IReadOnlyList<AccountDto> result =
            await sut.GetAllAsync(search: null, accountType: "ps", active: ActiveFilter.All);

        Assert.Single(result);
        Assert.Equal("5-01-01-010", result[0].AccountNumber);
        Assert.Equal("PS", result[0].AccountType);
    }

    [Fact]
    public async Task GetAllAsync_ActiveFilter_ExcludesInactive()
    {
        List<Account> seed = [Acct(1, "5-01-01-010", "A", active: true), Acct(2, "5-01-01-020", "B", active: false)];
        (AccountService sut, _) = Build(seed);

        IReadOnlyList<AccountDto> result =
            await sut.GetAllAsync(search: null, accountType: null, active: ActiveFilter.Active);

        Assert.Single(result);
        Assert.True(result[0].IsActive);
    }

    // ── key uniqueness ─────────────────────────────────────────────────────────

    [Fact]
    public async Task CreateAsync_DuplicateAccountNumber_ReturnsConflict()
    {
        List<Account> seed = [Acct(1, "5-01-01-010", "Salaries")];
        (AccountService sut, _) = Build(seed);

        ServiceResult<AccountDto> result = await sut.CreateAsync(
            new UpsertAccountDto("Different Title", "5-01-01-010", null, null, ExpenseClass: "PS"));

        Assert.Equal(ServiceErrorCode.Conflict, result.Code);
    }

    [Fact]
    public async Task CreateAsync_NewAccountNumber_ReturnsOk()
    {
        List<Account> seed = [Acct(1, "5-01-01-010", "Salaries")];
        (AccountService sut, _) = Build(seed);

        ServiceResult<AccountDto> result = await sut.CreateAsync(
            new UpsertAccountDto("Travel", "5-02-01-010", "Debit", null, ExpenseClass: "MOOE"));

        Assert.True(result.IsSuccess);
        Assert.Equal("5-02-01-010", result.Value!.AccountNumber);
        Assert.Equal("MOOE", result.Value.AccountType);
    }

    [Fact]
    public async Task UpdateAsync_AccountNumberTakenByAnother_ReturnsConflict()
    {
        List<Account> seed = [Acct(1, "5-01-01-010", "A"), Acct(2, "5-02-01-010", "B")];
        (AccountService sut, _) = Build(seed);

        // Try to rename #2's number to #1's number.
        ServiceResult<AccountDto> result = await sut.UpdateAsync(
            2, new UpsertAccountDto("B", "5-01-01-010", null, null, ExpenseClass: "PS"));

        Assert.Equal(ServiceErrorCode.Conflict, result.Code);
    }

    [Fact]
    public async Task UpdateAsync_MissingId_ReturnsNotFound()
    {
        (AccountService sut, _) = Build([Acct(1, "5-01-01-010", "A")]);
        ServiceResult<AccountDto> result = await sut.UpdateAsync(
            999, new UpsertAccountDto("X", "5-09-09-090", null, null, ExpenseClass: "Other"));
        Assert.Equal(ServiceErrorCode.NotFound, result.Code);
    }

    // ── soft delete ──────────────────────────────────────────────────────────

    [Fact]
    public async Task DeleteAsync_SetsIsActiveFalse_DoesNotRemove()
    {
        Account target = Acct(1, "5-01-01-010", "A", active: true);
        List<Account> seed = [target];
        (AccountService sut, Mock<IRepository<Account>> repo) = Build(seed);

        ServiceResult<AccountDto> result = await sut.DeleteAsync(1);

        Assert.True(result.IsSuccess);
        Assert.False(target.IsActive);
        Assert.False(result.Value!.IsActive);
        repo.Verify(r => r.DeleteAsync(It.IsAny<Account>(), It.IsAny<CancellationToken>()), Times.Never);
        repo.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    // ── CSV upsert counts ──────────────────────────────────────────────────────

    [Fact]
    public async Task ImportCsvAsync_CountsNewUpdatedSkipped()
    {
        // Existing: 5-01-01-010 "Salaries" (will be unchanged → skipped),
        //           5-02-03-990 "Old MOOE" (title changes → updated).
        List<Account> seed =
        [
            Acct(1, "5-01-01-010", "Salaries"),
            Acct(2, "5-02-03-990", "Old MOOE"),
        ];
        (AccountService sut, _) = Build(seed);

        string csv = string.Join("\r\n",
            "account_title,account_number,normal_balance,description,is_active",
            "Salaries,5-01-01-010,,,true",                // exists, unchanged → skipped
            "New MOOE,5-02-03-990,Debit,,true",           // exists, title changed → updated
            "Office Equipment,5-03-01-010,Debit,,true");  // new → inserted

        ServiceResult<CsvImportResult> result = await sut.ImportCsvAsync(csv);

        Assert.True(result.IsSuccess);
        CsvImportResult r = result.Value!;
        Assert.Equal(1, r.New);
        Assert.Equal(1, r.Updated);
        Assert.Equal(1, r.Skipped);
    }

    [Fact]
    public async Task ImportCsvAsync_RowMissingAccountNumber_IsErrorAndSkipped()
    {
        (AccountService sut, _) = Build([]);

        string csv = string.Join("\r\n",
            "account_title,account_number,normal_balance,description,is_active",
            "No Number,,Debit,,true");

        ServiceResult<CsvImportResult> result = await sut.ImportCsvAsync(csv);

        Assert.True(result.IsSuccess);
        Assert.Equal(0, result.Value!.New);
        Assert.Equal(1, result.Value.Skipped);
        Assert.NotEmpty(result.Value.Errors);
    }

    [Fact]
    public async Task ImportCsvAsync_ReactivatesViaIsActiveColumn()
    {
        Account inactive = Acct(1, "5-01-01-010", "Salaries", active: false);
        List<Account> seed = [inactive];
        (AccountService sut, _) = Build(seed);

        string csv = string.Join("\r\n",
            "account_title,account_number,normal_balance,description,is_active",
            "Salaries,5-01-01-010,,,true");   // same title, but is_active flips false→true → updated

        ServiceResult<CsvImportResult> result = await sut.ImportCsvAsync(csv);

        Assert.Equal(1, result.Value!.Updated);
        Assert.True(inactive.IsActive);
    }

    // ── audit logging (RAL-77) ─────────────────────────────────────────────────

    [Fact]
    public async Task CreateAsync_CallsAuditLog_WithCreateAction()
    {
        (AccountService sut, _, Mock<IAuditService> audit) = BuildWithAudit([]);

        await sut.CreateAsync(new UpsertAccountDto("Salaries", "5-01-01-010", null, null, ExpenseClass: "PS"));

        audit.Verify(a => a.LogAsync(
            "accounts", It.IsAny<int>(), AuditAction.Create,
            null, It.IsAny<object?>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task UpdateAsync_CallsAuditLog_CapturingOldAndNewValues()
    {
        List<Account> seed = [Acct(1, "5-01-01-010", "Old Title")];
        (AccountService sut, _, Mock<IAuditService> audit) = BuildWithAudit(seed);

        await sut.UpdateAsync(1, new UpsertAccountDto("New Title", "5-01-01-010", null, null, ExpenseClass: "PS"));

        audit.Verify(a => a.LogAsync(
            "accounts", 1, AuditAction.Update,
            It.IsNotNull<object>(), It.IsNotNull<object>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task DeleteAsync_CallsAuditLog_WithDeleteAction()
    {
        List<Account> seed = [Acct(1, "5-01-01-010", "Salaries", active: true)];
        (AccountService sut, _, Mock<IAuditService> audit) = BuildWithAudit(seed);

        await sut.DeleteAsync(1);

        audit.Verify(a => a.LogAsync(
            "accounts", 1, AuditAction.Delete,
            It.IsNotNull<object>(), null, It.IsAny<CancellationToken>()), Times.Once);
    }

    // ── RAL-117: expense_class stored, not derived ────────────────────────────

    [Fact]
    public async Task GetAllAsync_AccountType_ReadsStoredExpenseClass_NotPrefixDerived()
    {
        // 5-03- would derive to "CO" under the old prefix rule, but the stored
        // ExpenseClass says "PS" (e.g. a CO-numbered asset account reclassified by the
        // v1.4 seed). AccountType must reflect the stored value, never re-derive it.
        List<Account> seed = [Acct(1, "5-03-01-010", "Reclassified Account", expenseClass: "PS")];
        (AccountService sut, _) = Build(seed);

        IReadOnlyList<AccountDto> result =
            await sut.GetAllAsync(search: null, accountType: null, active: ActiveFilter.All);

        Assert.Equal("PS", result[0].AccountType);
        Assert.Equal("PS", result[0].ExpenseClass);
    }

    [Fact]
    public async Task GetAllAsync_AccountTypeFilter_MatchesStoredExpenseClass_NotPrefix()
    {
        List<Account> seed =
        [
            Acct(1, "5-03-01-010", "Reclassified as PS", expenseClass: "PS"),
            Acct(2, "5-01-01-020", "Regular PS", expenseClass: "PS"),
        ];
        (AccountService sut, _) = Build(seed);

        IReadOnlyList<AccountDto> result =
            await sut.GetAllAsync(search: null, accountType: "PS", active: ActiveFilter.All);

        Assert.Equal(2, result.Count);
    }

    [Fact]
    public async Task CreateAsync_MissingExpenseClass_ReturnsBadRequest()
    {
        (AccountService sut, _) = Build([]);

        ServiceResult<AccountDto> result = await sut.CreateAsync(
            new UpsertAccountDto("Travel", "5-02-01-010", null, null, ExpenseClass: ""));

        Assert.Equal(ServiceErrorCode.BadRequest, result.Code);
    }

    [Fact]
    public async Task CreateAsync_ValidExpenseClass_PersistsAndRoundTrips()
    {
        (AccountService sut, _) = Build([]);

        ServiceResult<AccountDto> result = await sut.CreateAsync(
            new UpsertAccountDto("Office Supplies", "5-02-03-010", null, null, ExpenseClass: "MOOE"));

        Assert.True(result.IsSuccess);
        Assert.Equal("MOOE", result.Value!.ExpenseClass);
        Assert.Equal("MOOE", result.Value.AccountType);
    }

    // ── RAL-117: default_nature — nullable, validated enum, default-only ─────

    [Fact]
    public async Task CreateAsync_DefaultNatureNull_IsAccepted()
    {
        (AccountService sut, _) = Build([]);

        ServiceResult<AccountDto> result = await sut.CreateAsync(
            new UpsertAccountDto("Travel", "5-02-01-010", null, null, ExpenseClass: "MOOE", DefaultNature: null));

        Assert.True(result.IsSuccess);
        Assert.Null(result.Value!.DefaultNature);
    }

    [Theory]
    [InlineData("Procurement")]
    [InlineData("Non-Procurement")]
    [InlineData("Combined")]
    public async Task CreateAsync_DefaultNatureAllowedValue_Persists(string nature)
    {
        (AccountService sut, _) = Build([]);

        ServiceResult<AccountDto> result = await sut.CreateAsync(
            new UpsertAccountDto("Supplies", "5-02-03-010", null, null, ExpenseClass: "MOOE", DefaultNature: nature));

        Assert.True(result.IsSuccess);
        Assert.Equal(nature, result.Value!.DefaultNature);
    }

    [Fact]
    public async Task CreateAsync_DefaultNatureInvalidValue_ReturnsBadRequest()
    {
        (AccountService sut, _) = Build([]);

        ServiceResult<AccountDto> result = await sut.CreateAsync(
            new UpsertAccountDto("Supplies", "5-02-03-010", null, null, ExpenseClass: "MOOE", DefaultNature: "Bogus"));

        Assert.Equal(ServiceErrorCode.BadRequest, result.Code);
    }

    [Fact]
    public async Task UpdateAsync_DefaultNatureInvalidValue_ReturnsBadRequest()
    {
        List<Account> seed = [Acct(1, "5-02-03-010", "Supplies", expenseClass: "MOOE")];
        (AccountService sut, _) = Build(seed);

        ServiceResult<AccountDto> result = await sut.UpdateAsync(1,
            new UpsertAccountDto("Supplies", "5-02-03-010", null, null, ExpenseClass: "MOOE", DefaultNature: "Bogus"));

        Assert.Equal(ServiceErrorCode.BadRequest, result.Code);
    }

    // ── RAL-117: default_apply_reserve — plain bool, no side effects, never a gate ──

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task CreateAsync_DefaultApplyReserve_RoundTripsPlainBool(bool applyReserve)
    {
        (AccountService sut, _) = Build([]);

        ServiceResult<AccountDto> result = await sut.CreateAsync(
            new UpsertAccountDto("Supplies", "5-02-03-010", null, null,
                ExpenseClass: "MOOE", DefaultApplyReserve: applyReserve));

        Assert.True(result.IsSuccess);
        Assert.Equal(applyReserve, result.Value!.DefaultApplyReserve);
    }

    [Fact]
    public async Task CreateAsync_DefaultApplyReserveTrue_OnNonMooeAccount_IsAllowed()
    {
        // No eligibility gate: even a PS account may default the reserve toggle on.
        (AccountService sut, _) = Build([]);

        ServiceResult<AccountDto> result = await sut.CreateAsync(
            new UpsertAccountDto("Salaries", "5-01-01-010", null, null,
                ExpenseClass: "PS", DefaultApplyReserve: true));

        Assert.True(result.IsSuccess);
        Assert.True(result.Value!.DefaultApplyReserve);
    }

    // ── RAL-117: CSV export/import round-trip for the 3 new columns ──────────

    [Fact]
    public async Task ExportCsvAsync_IncludesNewColumns()
    {
        Account account = Acct(1, "5-02-03-010", "Supplies", expenseClass: "MOOE");
        account.DefaultNature = "Procurement";
        account.DefaultApplyReserve = true;
        List<Account> seed = [account];
        (AccountService sut, _) = Build(seed);

        string csv = await sut.ExportCsvAsync();

        Assert.Contains("expense_class", csv);
        Assert.Contains("default_nature", csv);
        Assert.Contains("default_apply_reserve", csv);
        Assert.Contains("MOOE,Procurement,true", csv);
    }

    [Fact]
    public async Task ImportCsvAsync_UpsertsNewColumns()
    {
        (AccountService sut, _) = Build([]);

        string csv = string.Join("\r\n",
            "account_title,account_number,normal_balance,description,is_active,expense_class,default_nature,default_apply_reserve",
            "Supplies,5-02-03-010,,,true,MOOE,Procurement,true");

        ServiceResult<CsvImportResult> result = await sut.ImportCsvAsync(csv);
        Assert.True(result.IsSuccess);
        Assert.Equal(1, result.Value!.New);

        IReadOnlyList<AccountDto> all = await sut.GetAllAsync(null, null, ActiveFilter.All);
        Assert.Equal("MOOE", all[0].ExpenseClass);
        Assert.Equal("Procurement", all[0].DefaultNature);
        Assert.True(all[0].DefaultApplyReserve);
    }

    [Fact]
    public async Task ImportCsvAsync_MissingExpenseClassColumn_FallsBackToPrefixDerivation()
    {
        // Backward compatibility: a pre-v1.4 CSV export (no new columns) must still import.
        (AccountService sut, _) = Build([]);

        string csv = string.Join("\r\n",
            "account_title,account_number,normal_balance,description,is_active",
            "Salaries,5-01-01-010,,,true");

        ServiceResult<CsvImportResult> result = await sut.ImportCsvAsync(csv);
        Assert.True(result.IsSuccess);

        IReadOnlyList<AccountDto> all = await sut.GetAllAsync(null, null, ActiveFilter.All);
        Assert.Equal("PS", all[0].ExpenseClass);
    }

    [Fact]
    public async Task ImportCsvAsync_InvalidDefaultNature_IsErrorAndSkipped()
    {
        (AccountService sut, _) = Build([]);

        string csv = string.Join("\r\n",
            "account_title,account_number,normal_balance,description,is_active,expense_class,default_nature,default_apply_reserve",
            "Supplies,5-02-03-010,,,true,MOOE,Bogus,false");

        ServiceResult<CsvImportResult> result = await sut.ImportCsvAsync(csv);

        Assert.True(result.IsSuccess);
        Assert.Equal(0, result.Value!.New);
        Assert.Equal(1, result.Value.Skipped);
        Assert.NotEmpty(result.Value.Errors);
    }
}
