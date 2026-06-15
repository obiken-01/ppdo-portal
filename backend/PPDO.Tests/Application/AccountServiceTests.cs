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
    private static Account Acct(int id, string number, string title, bool active = true) => new()
    {
        Id = id, AccountNumber = number, AccountTitle = title, IsActive = active,
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
            new UpsertAccountDto("Different Title", "5-01-01-010", null, null));

        Assert.Equal(ServiceErrorCode.Conflict, result.Code);
    }

    [Fact]
    public async Task CreateAsync_NewAccountNumber_ReturnsOk()
    {
        List<Account> seed = [Acct(1, "5-01-01-010", "Salaries")];
        (AccountService sut, _) = Build(seed);

        ServiceResult<AccountDto> result = await sut.CreateAsync(
            new UpsertAccountDto("Travel", "5-02-01-010", "Debit", null));

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
            2, new UpsertAccountDto("B", "5-01-01-010", null, null));

        Assert.Equal(ServiceErrorCode.Conflict, result.Code);
    }

    [Fact]
    public async Task UpdateAsync_MissingId_ReturnsNotFound()
    {
        (AccountService sut, _) = Build([Acct(1, "5-01-01-010", "A")]);
        ServiceResult<AccountDto> result = await sut.UpdateAsync(
            999, new UpsertAccountDto("X", "5-09-09-090", null, null));
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

        await sut.CreateAsync(new UpsertAccountDto("Salaries", "5-01-01-010", null, null));

        audit.Verify(a => a.LogAsync(
            "accounts", It.IsAny<int>(), AuditAction.Create,
            null, It.IsAny<object?>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task UpdateAsync_CallsAuditLog_CapturingOldAndNewValues()
    {
        List<Account> seed = [Acct(1, "5-01-01-010", "Old Title")];
        (AccountService sut, _, Mock<IAuditService> audit) = BuildWithAudit(seed);

        await sut.UpdateAsync(1, new UpsertAccountDto("New Title", "5-01-01-010", null, null));

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
}
