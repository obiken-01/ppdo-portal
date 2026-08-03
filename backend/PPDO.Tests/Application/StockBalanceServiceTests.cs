using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using PPDO.Application.Common;
using PPDO.Application.DTOs.Inventory;
using PPDO.Application.Services;
using PPDO.Domain.Entities;
using PPDO.Domain.Enums;
using PPDO.Domain.Interfaces;

namespace PPDO.Tests.Application;

/// <summary>
/// Unit tests for <see cref="StockBalanceService"/> (RAL-193).
/// IStockBalanceRepository, IInventoryRepository, IUserRepository, and IExcelService are
/// mocked; IPermissionService uses the real implementation (matches ItemServiceTests).
///
/// Focus: the on-hand formula's variance computation — onHand = SUM(VarianceQty) +
/// QtyDelivered - QtyDistributed, with VarianceQty = CountedQty - SystemOnHandAtEntry
/// computed once at save time.
/// </summary>
public sealed class StockBalanceServiceTests
{
    // ── Fixtures ──────────────────────────────────────────────────────────────

    private static User MakeAdmin() => new()
    {
        Id = Guid.NewGuid(), FullName = "Admin", Email = "admin@ppdo.gov.ph",
        PasswordHash = "hash", Role = UserRole.Admin, DivisionId = null, IsActive = true,
    };

    private static User MakeStaffNoInventory() => new()
    {
        Id = Guid.NewGuid(), FullName = "Staff", Email = "staff@ppdo.gov.ph",
        PasswordHash = "hash", Role = UserRole.Staff, DivisionId = 2,
        Division = new Division { Id = 2, OfficeId = 100, Name = "Planning Division", CanAccessInventory = false },
        IsActive = true,
    };

    private static ItemStockLevel EmptyLevel(string stockNo) => new(stockNo, 0m, 0m, 0m);

    private static Mock<IStockBalanceRepository> RepoThatSaves()
    {
        Mock<IStockBalanceRepository> repo = new();
        repo.Setup(r => r.AddAsync(It.IsAny<StockBalance>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        repo.Setup(r => r.UpdateAsync(It.IsAny<StockBalance>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        repo.Setup(r => r.DeleteAsync(It.IsAny<StockBalance>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        repo.Setup(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);
        repo.Setup(r => r.GetTotalVarianceByStockNosAsync(
                It.IsAny<IReadOnlyCollection<string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<string, decimal>());
        return repo;
    }

    private static Mock<IInventoryRepository> InventoryReturning(ItemStockLevel level)
    {
        Mock<IInventoryRepository> repo = new();
        repo.Setup(r => r.GetItemStockLevelAsync(level.StockNo, It.IsAny<CancellationToken>()))
            .ReturnsAsync(level);
        return repo;
    }

    private static Mock<IUserRepository> UserRepoStub()
    {
        Mock<IUserRepository> repo = new();
        repo.Setup(r => r.GetNamesByIdsAsync(It.IsAny<IReadOnlyList<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<Guid, string>());
        return repo;
    }

    private static StockBalanceService BuildSut(
        Mock<IStockBalanceRepository> stockRepo,
        Mock<IInventoryRepository> invRepo,
        Mock<IUserRepository>? userRepo = null,
        Mock<IExcelService>? excelRepo = null)
        => new(
            stockRepo.Object,
            invRepo.Object,
            (userRepo ?? UserRepoStub()).Object,
            new PermissionService(),
            (excelRepo ?? new Mock<IExcelService>()).Object,
            NullLogger<StockBalanceService>.Instance);

    // ── CreateAsync — permission ──────────────────────────────────────────────

    [Fact]
    public async Task CreateAsync_WithoutCanAccessInventory_ReturnsForbidden()
    {
        Mock<IStockBalanceRepository> stockRepo = RepoThatSaves();
        Mock<IInventoryRepository> invRepo = InventoryReturning(EmptyLevel("A01"));

        ServiceResult<StockBalanceDto> result = await BuildSut(stockRepo, invRepo).CreateAsync(
            MakeStaffNoInventory(),
            new CreateStockBalanceDto("A01", 10m, DateOnly.FromDateTime(DateTime.UtcNow), null));

        Assert.Equal(ServiceErrorCode.Forbidden, result.Code);
        stockRepo.Verify(r => r.AddAsync(It.IsAny<StockBalance>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    // ── CreateAsync — validation ──────────────────────────────────────────────

    [Theory]
    [InlineData("", 10)]
    [InlineData("A01", -1)]
    public async Task CreateAsync_InvalidInput_ReturnsBadRequest(string stockNo, decimal countedQty)
    {
        Mock<IStockBalanceRepository> stockRepo = RepoThatSaves();
        Mock<IInventoryRepository> invRepo = InventoryReturning(EmptyLevel("A01"));

        ServiceResult<StockBalanceDto> result = await BuildSut(stockRepo, invRepo).CreateAsync(
            MakeAdmin(),
            new CreateStockBalanceDto(stockNo, countedQty, DateOnly.FromDateTime(DateTime.UtcNow), null));

        Assert.Equal(ServiceErrorCode.BadRequest, result.Code);
    }

    [Fact]
    public async Task CreateAsync_FutureEffectiveDate_ReturnsBadRequest()
    {
        Mock<IStockBalanceRepository> stockRepo = RepoThatSaves();
        Mock<IInventoryRepository> invRepo = InventoryReturning(EmptyLevel("A01"));

        ServiceResult<StockBalanceDto> result = await BuildSut(stockRepo, invRepo).CreateAsync(
            MakeAdmin(),
            new CreateStockBalanceDto("A01", 10m, DateOnly.FromDateTime(DateTime.UtcNow).AddDays(1), null));

        Assert.Equal(ServiceErrorCode.BadRequest, result.Code);
    }

    // ── CreateAsync — variance computation (the core formula) ────────────────

    [Fact]
    public async Task CreateAsync_NoMovementsNoPriorEntries_VarianceEqualsCountedQty()
    {
        // SystemOnHandAtEntry = 0 (no deliveries/distributions, no prior variance) →
        // VarianceQty = CountedQty - 0 = CountedQty.
        Mock<IStockBalanceRepository> stockRepo = RepoThatSaves();
        Mock<IInventoryRepository> invRepo = InventoryReturning(EmptyLevel("A01"));

        ServiceResult<StockBalanceDto> result = await BuildSut(stockRepo, invRepo).CreateAsync(
            MakeAdmin(),
            new CreateStockBalanceDto("A01", 50m, DateOnly.FromDateTime(DateTime.UtcNow), "Initial count"));

        Assert.True(result.IsSuccess);
        Assert.Equal(0m, result.Value!.SystemOnHandAtEntry);
        Assert.Equal(50m, result.Value.VarianceQty);
        Assert.Equal(50m, result.Value.CountedQty);
    }

    [Fact]
    public async Task CreateAsync_WithExistingMovements_SystemOnHandIsDeliveredMinusDistributed()
    {
        // QtyDelivered=20, QtyDistributed=5 → SystemOnHandAtEntry=15.
        // Physically counted 12 → VarianceQty = 12 - 15 = -3 (3 units short).
        Mock<IStockBalanceRepository> stockRepo = RepoThatSaves();
        Mock<IInventoryRepository> invRepo = InventoryReturning(new ItemStockLevel("B01", 20m, 20m, 5m));

        ServiceResult<StockBalanceDto> result = await BuildSut(stockRepo, invRepo).CreateAsync(
            MakeAdmin(),
            new CreateStockBalanceDto("B01", 12m, DateOnly.FromDateTime(DateTime.UtcNow), null));

        Assert.Equal(15m, result.Value!.SystemOnHandAtEntry);
        Assert.Equal(-3m, result.Value.VarianceQty);
    }

    [Fact]
    public async Task CreateAsync_WithPriorVarianceEntries_IncludesThemInSystemOnHand()
    {
        // A prior entry already contributed +5 variance. New movement-only on-hand is 10.
        // SystemOnHandAtEntry = 10 (movements) + 5 (prior variance) = 15.
        Mock<IStockBalanceRepository> stockRepo = RepoThatSaves();
        stockRepo.Setup(r => r.GetTotalVarianceByStockNosAsync(
                It.Is<IReadOnlyCollection<string>>(s => s.Contains("C01")), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<string, decimal> { ["C01"] = 5m });

        Mock<IInventoryRepository> invRepo = InventoryReturning(new ItemStockLevel("C01", 10m, 10m, 0m));

        ServiceResult<StockBalanceDto> result = await BuildSut(stockRepo, invRepo).CreateAsync(
            MakeAdmin(),
            new CreateStockBalanceDto("C01", 15m, DateOnly.FromDateTime(DateTime.UtcNow), null));

        Assert.Equal(15m, result.Value!.SystemOnHandAtEntry);
        Assert.Equal(0m, result.Value.VarianceQty); // counted matches system exactly
    }

    [Fact]
    public async Task CreateAsync_ValidEntry_PersistsAndLogsRecordedByRequester()
    {
        Mock<IStockBalanceRepository> stockRepo = RepoThatSaves();
        Mock<IInventoryRepository> invRepo = InventoryReturning(EmptyLevel("D01"));
        User admin = MakeAdmin();

        ServiceResult<StockBalanceDto> result = await BuildSut(stockRepo, invRepo).CreateAsync(
            admin, new CreateStockBalanceDto("D01", 8m, DateOnly.FromDateTime(DateTime.UtcNow), null));

        Assert.Equal(admin.Id, result.Value!.RecordedByUserId);
        stockRepo.Verify(r => r.AddAsync(It.IsAny<StockBalance>(), It.IsAny<CancellationToken>()), Times.Once);
        stockRepo.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    // ── UpdateAsync ───────────────────────────────────────────────────────────

    [Fact]
    public async Task UpdateAsync_EntryNotFound_ReturnsNotFound()
    {
        Mock<IStockBalanceRepository> stockRepo = RepoThatSaves();
        stockRepo.Setup(r => r.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((StockBalance?)null);
        Mock<IInventoryRepository> invRepo = new();

        ServiceResult<StockBalanceDto> result = await BuildSut(stockRepo, invRepo).UpdateAsync(
            MakeAdmin(), Guid.NewGuid(), new UpdateStockBalanceDto(10m, null, null));

        Assert.Equal(ServiceErrorCode.NotFound, result.Code);
    }

    [Fact]
    public async Task UpdateAsync_RecomputesVariance_ExcludingOwnPriorContribution()
    {
        // Existing entry already contributed +5 to the running variance total (which the
        // repo's GetTotalVarianceByStockNosAsync mock reflects as already summed in).
        // Movements: delivered=10, distributed=0 → movementOnHand=10.
        // otherEntriesVariance = totalFromRepo(5) - excludeOwn(5) = 0.
        // New SystemOnHandAtEntry = 10 + 0 = 10. New CountedQty=13 → new VarianceQty=3.
        Guid entryId = Guid.NewGuid();
        StockBalance existing = new()
        {
            Id = entryId, StockNo = "E01", CountedQty = 5m, SystemOnHandAtEntry = 0m,
            VarianceQty = 5m, EffectiveDate = DateOnly.FromDateTime(DateTime.UtcNow),
            RecordedByUserId = Guid.NewGuid(),
        };

        Mock<IStockBalanceRepository> stockRepo = RepoThatSaves();
        stockRepo.Setup(r => r.GetByIdAsync(entryId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(existing);
        stockRepo.Setup(r => r.GetTotalVarianceByStockNosAsync(
                It.Is<IReadOnlyCollection<string>>(s => s.Contains("E01")), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<string, decimal> { ["E01"] = 5m });

        Mock<IInventoryRepository> invRepo = InventoryReturning(new ItemStockLevel("E01", 10m, 10m, 0m));

        ServiceResult<StockBalanceDto> result = await BuildSut(stockRepo, invRepo).UpdateAsync(
            MakeAdmin(), entryId, new UpdateStockBalanceDto(13m, null, null));

        Assert.Equal(10m, result.Value!.SystemOnHandAtEntry);
        Assert.Equal(3m, result.Value.VarianceQty);
    }

    // ── DeleteAsync ───────────────────────────────────────────────────────────

    [Fact]
    public async Task DeleteAsync_RemovesEntry_ReturnsDeletedDto()
    {
        Guid entryId = Guid.NewGuid();
        StockBalance existing = new()
        {
            Id = entryId, StockNo = "F01", CountedQty = 5m, SystemOnHandAtEntry = 0m,
            VarianceQty = 5m, EffectiveDate = DateOnly.FromDateTime(DateTime.UtcNow),
            RecordedByUserId = Guid.NewGuid(),
        };

        Mock<IStockBalanceRepository> stockRepo = RepoThatSaves();
        stockRepo.Setup(r => r.GetByIdAsync(entryId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(existing);
        Mock<IInventoryRepository> invRepo = new();

        ServiceResult<StockBalanceDto> result = await BuildSut(stockRepo, invRepo)
            .DeleteAsync(MakeAdmin(), entryId);

        Assert.True(result.IsSuccess);
        Assert.Equal("F01", result.Value!.StockNo);
        stockRepo.Verify(r => r.DeleteAsync(existing, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task DeleteAsync_WithoutPermission_ReturnsForbidden()
    {
        Mock<IStockBalanceRepository> stockRepo = RepoThatSaves();
        Mock<IInventoryRepository> invRepo = new();

        ServiceResult<StockBalanceDto> result = await BuildSut(stockRepo, invRepo)
            .DeleteAsync(MakeStaffNoInventory(), Guid.NewGuid());

        Assert.Equal(ServiceErrorCode.Forbidden, result.Code);
        stockRepo.Verify(r => r.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    // ── GetHistoryAsync ───────────────────────────────────────────────────────

    [Fact]
    public async Task GetHistoryAsync_WithoutPermission_ReturnsForbidden()
    {
        Mock<IStockBalanceRepository> stockRepo = RepoThatSaves();
        Mock<IInventoryRepository> invRepo = new();

        ServiceResult<IReadOnlyList<StockBalanceDto>> result = await BuildSut(stockRepo, invRepo)
            .GetHistoryAsync(MakeStaffNoInventory(), "A01");

        Assert.Equal(ServiceErrorCode.Forbidden, result.Code);
    }

    [Fact]
    public async Task GetHistoryAsync_ReturnsEntriesFromRepository()
    {
        List<StockBalance> entries =
        [
            new() { Id = Guid.NewGuid(), StockNo = "A01", CountedQty = 10m, EffectiveDate = DateOnly.FromDateTime(DateTime.UtcNow), RecordedByUserId = Guid.NewGuid() },
        ];

        Mock<IStockBalanceRepository> stockRepo = RepoThatSaves();
        stockRepo.Setup(r => r.GetByStockNoAsync("A01", It.IsAny<CancellationToken>()))
            .ReturnsAsync(entries);
        Mock<IInventoryRepository> invRepo = new();

        ServiceResult<IReadOnlyList<StockBalanceDto>> result = await BuildSut(stockRepo, invRepo)
            .GetHistoryAsync(MakeAdmin(), "A01");

        Assert.True(result.IsSuccess);
        Assert.Single(result.Value!);
    }

    // ── PreviewImportAsync ────────────────────────────────────────────────────

    [Fact]
    public async Task PreviewImportAsync_ReturnsParsedRowsFromExcelService()
    {
        Mock<IExcelService> excel = new();
        excel.Setup(e => e.ParseStockBalanceImport(It.IsAny<Stream>()))
            .Returns(
            [
                new StockBalanceImportRow { RowNumber = 2, StockNo = "A01", CountedQty = 10m, EffectiveDate = DateOnly.FromDateTime(DateTime.UtcNow), Error = null },
                new StockBalanceImportRow { RowNumber = 3, StockNo = null, Error = "StockNo is required." },
            ]);

        Mock<IStockBalanceRepository> stockRepo = RepoThatSaves();
        Mock<IInventoryRepository> invRepo = new();

        ServiceResult<StockBalanceImportPreviewDto> result = await BuildSut(
            stockRepo, invRepo, excelRepo: excel).PreviewImportAsync(MakeAdmin(), new MemoryStream());

        Assert.True(result.IsSuccess);
        Assert.Equal(2, result.Value!.Rows.Count);
        Assert.Null(result.Value.Rows[0].Error);
        Assert.NotNull(result.Value.Rows[1].Error);
    }

    [Fact]
    public async Task PreviewImportAsync_WithoutPermission_ReturnsForbidden_NeverParses()
    {
        Mock<IExcelService> excel = new();
        Mock<IStockBalanceRepository> stockRepo = RepoThatSaves();
        Mock<IInventoryRepository> invRepo = new();

        ServiceResult<StockBalanceImportPreviewDto> result = await BuildSut(
            stockRepo, invRepo, excelRepo: excel).PreviewImportAsync(MakeStaffNoInventory(), new MemoryStream());

        Assert.Equal(ServiceErrorCode.Forbidden, result.Code);
        excel.Verify(e => e.ParseStockBalanceImport(It.IsAny<Stream>()), Times.Never);
    }

    // ── CommitImportAsync — upsert behavior ───────────────────────────────────

    [Fact]
    public async Task CommitImportAsync_NewStockNoAndDate_Inserts()
    {
        Mock<IStockBalanceRepository> stockRepo = RepoThatSaves();
        stockRepo.Setup(r => r.FindByStockNoAndEffectiveDateAsync(
                "A01", It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((StockBalance?)null);
        Mock<IInventoryRepository> invRepo = InventoryReturning(EmptyLevel("A01"));

        CommitStockBalanceImportDto dto = new(
            [new CreateStockBalanceDto("A01", 10m, DateOnly.FromDateTime(DateTime.UtcNow), null)]);

        ServiceResult<StockBalanceImportResultDto> result =
            await BuildSut(stockRepo, invRepo).CommitImportAsync(MakeAdmin(), dto);

        Assert.True(result.IsSuccess);
        Assert.Equal(1, result.Value!.Inserted);
        Assert.Equal(0, result.Value.Updated);
        stockRepo.Verify(r => r.AddAsync(It.IsAny<StockBalance>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task CommitImportAsync_MatchingStockNoAndDate_UpsertsExisting()
    {
        DateOnly date = DateOnly.FromDateTime(DateTime.UtcNow);
        StockBalance existing = new()
        {
            Id = Guid.NewGuid(), StockNo = "A01", CountedQty = 5m, SystemOnHandAtEntry = 0m,
            VarianceQty = 5m, EffectiveDate = date, RecordedByUserId = Guid.NewGuid(),
        };

        Mock<IStockBalanceRepository> stockRepo = RepoThatSaves();
        stockRepo.Setup(r => r.FindByStockNoAndEffectiveDateAsync("A01", date, It.IsAny<CancellationToken>()))
            .ReturnsAsync(existing);
        Mock<IInventoryRepository> invRepo = InventoryReturning(EmptyLevel("A01"));

        CommitStockBalanceImportDto dto = new([new CreateStockBalanceDto("A01", 20m, date, "Corrected count")]);

        ServiceResult<StockBalanceImportResultDto> result =
            await BuildSut(stockRepo, invRepo).CommitImportAsync(MakeAdmin(), dto);

        Assert.True(result.IsSuccess);
        Assert.Equal(0, result.Value!.Inserted);
        Assert.Equal(1, result.Value.Updated);
        Assert.Equal(20m, existing.CountedQty);
        stockRepo.Verify(r => r.AddAsync(It.IsAny<StockBalance>(), It.IsAny<CancellationToken>()), Times.Never);
        stockRepo.Verify(r => r.UpdateAsync(existing, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task CommitImportAsync_InvalidRow_ReturnsBadRequest()
    {
        Mock<IStockBalanceRepository> stockRepo = RepoThatSaves();
        Mock<IInventoryRepository> invRepo = new();

        CommitStockBalanceImportDto dto = new(
            [new CreateStockBalanceDto("", 10m, DateOnly.FromDateTime(DateTime.UtcNow), null)]);

        ServiceResult<StockBalanceImportResultDto> result =
            await BuildSut(stockRepo, invRepo).CommitImportAsync(MakeAdmin(), dto);

        Assert.Equal(ServiceErrorCode.BadRequest, result.Code);
    }
}
