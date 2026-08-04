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

    /// <summary>Default item-master stub for tests that don't care about the auto-create-item
    /// behavior — every StockNo resolves to an already-cataloged item, so EnsureItemMasterAsync
    /// short-circuits without needing Description/Unit on the DTO.</summary>
    private static Mock<IItemMasterRepository> ItemRepoWithExistingItem()
    {
        Mock<IItemMasterRepository> repo = new();
        repo.Setup(r => r.GetByStockNoAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string sn, CancellationToken _) => new ItemMaster
            {
                Id = Guid.NewGuid(), StockNo = sn, Description = "Existing Item", Unit = "pcs",
                UnitCost = 10m, IsNewItem = false, CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow,
            });
        return repo;
    }

    /// <summary>Item-master stub where no StockNo is cataloged — used by the auto-create tests.</summary>
    private static Mock<IItemMasterRepository> ItemRepoWithNoItems()
    {
        Mock<IItemMasterRepository> repo = new();
        repo.Setup(r => r.GetByStockNoAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((ItemMaster?)null);
        repo.Setup(r => r.AddAsync(It.IsAny<ItemMaster>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        return repo;
    }

    private static StockBalanceService BuildSut(
        Mock<IStockBalanceRepository> stockRepo,
        Mock<IInventoryRepository> invRepo,
        Mock<IItemMasterRepository>? itemRepo = null,
        Mock<IUserRepository>? userRepo = null,
        Mock<IExcelService>? excelRepo = null)
        => new(
            stockRepo.Object,
            invRepo.Object,
            (itemRepo ?? ItemRepoWithExistingItem()).Object,
            (userRepo ?? UserRepoStub()).Object,
            new PermissionService(),
            (excelRepo ?? new Mock<IExcelService>()).Object,
            NullLogger<StockBalanceService>.Instance);

    /// <summary>Builds a CreateStockBalanceDto, defaulting the item-fields to null — use named
    /// args (description:, unit:, ...) in tests that exercise the auto-create-item path.</summary>
    private static CreateStockBalanceDto MakeCreateDto(
        string stockNo, decimal countedQty, DateOnly effectiveDate, string? reason = null,
        string? description = null, string? unit = null, decimal? unitCost = null, string? itemType = null)
        => new(stockNo, countedQty, effectiveDate, reason, description, unit, unitCost, itemType);

    // ── GetSystemOnHandAsync ──────────────────────────────────────────────────

    [Fact]
    public async Task GetSystemOnHandAsync_WithoutCanAccessInventory_ReturnsForbidden()
    {
        Mock<IStockBalanceRepository> stockRepo = RepoThatSaves();
        Mock<IInventoryRepository> invRepo = InventoryReturning(EmptyLevel("A01"));

        ServiceResult<SystemOnHandDto> result = await BuildSut(stockRepo, invRepo).GetSystemOnHandAsync(
            MakeStaffNoInventory(), "A01");

        Assert.Equal(ServiceErrorCode.Forbidden, result.Code);
    }

    [Fact]
    public async Task GetSystemOnHandAsync_ReturnsMovementOnHandPlusExistingVariance()
    {
        // QtyDelivered=20, QtyDistributed=5 → movement on-hand = 15. Existing entries
        // already contributed +5 variance → current system on-hand = 20. This must equal
        // exactly what CreateAsync would compute as SystemOnHandAtEntry for a new entry
        // right now — it's the same reference value shown to the user before they submit.
        Mock<IStockBalanceRepository> stockRepo = RepoThatSaves();
        stockRepo.Setup(r => r.GetTotalVarianceByStockNosAsync(
                It.Is<IReadOnlyCollection<string>>(s => s.Contains("B01")), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<string, decimal> { ["B01"] = 5m });

        Mock<IInventoryRepository> invRepo = InventoryReturning(new ItemStockLevel("B01", 20m, 20m, 5m));

        ServiceResult<SystemOnHandDto> result = await BuildSut(stockRepo, invRepo).GetSystemOnHandAsync(
            MakeAdmin(), "B01");

        Assert.True(result.IsSuccess);
        Assert.Equal("B01", result.Value!.StockNo);
        Assert.Equal(20m, result.Value.OnHand);
    }

    [Fact]
    public async Task GetSystemOnHandAsync_BlankStockNo_ReturnsBadRequest()
    {
        Mock<IStockBalanceRepository> stockRepo = RepoThatSaves();
        Mock<IInventoryRepository> invRepo = new();

        ServiceResult<SystemOnHandDto> result = await BuildSut(stockRepo, invRepo).GetSystemOnHandAsync(
            MakeAdmin(), "   ");

        Assert.Equal(ServiceErrorCode.BadRequest, result.Code);
    }

    // ── GetImportTemplateAsync ─────────────────────────────────────────────────

    [Fact]
    public async Task GetImportTemplateAsync_WithoutCanAccessInventory_ReturnsForbidden()
    {
        Mock<IStockBalanceRepository> stockRepo = RepoThatSaves();
        Mock<IInventoryRepository> invRepo = new();

        ServiceResult<byte[]> result = await BuildSut(stockRepo, invRepo).GetImportTemplateAsync(
            MakeStaffNoInventory());

        Assert.Equal(ServiceErrorCode.Forbidden, result.Code);
    }

    [Fact]
    public async Task GetImportTemplateAsync_ReturnsBytesFromExcelService()
    {
        Mock<IStockBalanceRepository> stockRepo = RepoThatSaves();
        Mock<IInventoryRepository> invRepo = new();
        byte[] expectedBytes = [1, 2, 3];
        Mock<IExcelService> excelRepo = new();
        excelRepo.Setup(e => e.GenerateStockBalanceImportTemplate()).Returns(expectedBytes);

        ServiceResult<byte[]> result = await BuildSut(
            stockRepo, invRepo, excelRepo: excelRepo).GetImportTemplateAsync(MakeAdmin());

        Assert.True(result.IsSuccess);
        Assert.Equal(expectedBytes, result.Value);
    }

    // ── CreateAsync — permission ──────────────────────────────────────────────

    [Fact]
    public async Task CreateAsync_WithoutCanAccessInventory_ReturnsForbidden()
    {
        Mock<IStockBalanceRepository> stockRepo = RepoThatSaves();
        Mock<IInventoryRepository> invRepo = InventoryReturning(EmptyLevel("A01"));

        ServiceResult<StockBalanceDto> result = await BuildSut(stockRepo, invRepo).CreateAsync(
            MakeStaffNoInventory(),
            MakeCreateDto("A01", 10m, DateOnly.FromDateTime(DateTime.UtcNow), null));

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
            MakeCreateDto(stockNo, countedQty, DateOnly.FromDateTime(DateTime.UtcNow), null));

        Assert.Equal(ServiceErrorCode.BadRequest, result.Code);
    }

    [Fact]
    public async Task CreateAsync_FutureEffectiveDate_ReturnsBadRequest()
    {
        Mock<IStockBalanceRepository> stockRepo = RepoThatSaves();
        Mock<IInventoryRepository> invRepo = InventoryReturning(EmptyLevel("A01"));

        ServiceResult<StockBalanceDto> result = await BuildSut(stockRepo, invRepo).CreateAsync(
            MakeAdmin(),
            MakeCreateDto("A01", 10m, DateOnly.FromDateTime(DateTime.UtcNow).AddDays(1), null));

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
            MakeCreateDto("A01", 50m, DateOnly.FromDateTime(DateTime.UtcNow), "Initial count"));

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
            MakeCreateDto("B01", 12m, DateOnly.FromDateTime(DateTime.UtcNow), null));

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
            MakeCreateDto("C01", 15m, DateOnly.FromDateTime(DateTime.UtcNow), null));

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
            admin, MakeCreateDto("D01", 8m, DateOnly.FromDateTime(DateTime.UtcNow), null));

        Assert.Equal(admin.Id, result.Value!.RecordedByUserId);
        stockRepo.Verify(r => r.AddAsync(It.IsAny<StockBalance>(), It.IsAny<CancellationToken>()), Times.Once);
        stockRepo.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    // ── CreateAsync — duplicate (StockNo, EffectiveDate) ──────────────────────

    [Fact]
    public async Task CreateAsync_EntryAlreadyExistsForStockNoAndDate_ReturnsConflict_NeverThrows()
    {
        // Bug reported after RAL-193 ship: recording a second count for a StockNo + date that
        // already had one hit the DB's unique index and threw an unhandled DbUpdateException
        // instead of a friendly error. The single-entry form has no upsert semantics (unlike
        // bulk import), so a duplicate must be rejected explicitly before AddAsync.
        DateOnly today = DateOnly.FromDateTime(DateTime.UtcNow);
        StockBalance existing = new()
        {
            Id = Guid.NewGuid(), StockNo = "D01", CountedQty = 5m, SystemOnHandAtEntry = 0m,
            VarianceQty = 5m, EffectiveDate = today, RecordedByUserId = Guid.NewGuid(),
        };

        Mock<IStockBalanceRepository> stockRepo = RepoThatSaves();
        stockRepo.Setup(r => r.FindByStockNoAndEffectiveDateAsync("D01", today, It.IsAny<CancellationToken>()))
            .ReturnsAsync(existing);
        Mock<IInventoryRepository> invRepo = InventoryReturning(EmptyLevel("D01"));

        ServiceResult<StockBalanceDto> result = await BuildSut(stockRepo, invRepo).CreateAsync(
            MakeAdmin(), MakeCreateDto("D01", 8m, today, null));

        Assert.Equal(ServiceErrorCode.Conflict, result.Code);
        stockRepo.Verify(r => r.AddAsync(It.IsAny<StockBalance>(), It.IsAny<CancellationToken>()), Times.Never);
        stockRepo.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    // ── CreateAsync — unknown StockNo auto-creates Items Master entry ────────

    [Fact]
    public async Task CreateAsync_UnknownStockNo_MissingDescription_ReturnsBadRequest_NeverCreatesItem()
    {
        Mock<IStockBalanceRepository> stockRepo = RepoThatSaves();
        Mock<IInventoryRepository> invRepo = InventoryReturning(EmptyLevel("NEW01"));
        Mock<IItemMasterRepository> itemRepo = ItemRepoWithNoItems();

        ServiceResult<StockBalanceDto> result = await BuildSut(stockRepo, invRepo, itemRepo).CreateAsync(
            MakeAdmin(),
            MakeCreateDto("NEW01", 10m, DateOnly.FromDateTime(DateTime.UtcNow), unit: "pcs")); // no description

        Assert.Equal(ServiceErrorCode.BadRequest, result.Code);
        itemRepo.Verify(r => r.AddAsync(It.IsAny<ItemMaster>(), It.IsAny<CancellationToken>()), Times.Never);
        stockRepo.Verify(r => r.AddAsync(It.IsAny<StockBalance>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task CreateAsync_UnknownStockNo_MissingUnit_ReturnsBadRequest_NeverCreatesItem()
    {
        Mock<IStockBalanceRepository> stockRepo = RepoThatSaves();
        Mock<IInventoryRepository> invRepo = InventoryReturning(EmptyLevel("NEW01"));
        Mock<IItemMasterRepository> itemRepo = ItemRepoWithNoItems();

        ServiceResult<StockBalanceDto> result = await BuildSut(stockRepo, invRepo, itemRepo).CreateAsync(
            MakeAdmin(),
            MakeCreateDto("NEW01", 10m, DateOnly.FromDateTime(DateTime.UtcNow), description: "New Item")); // no unit

        Assert.Equal(ServiceErrorCode.BadRequest, result.Code);
        itemRepo.Verify(r => r.AddAsync(It.IsAny<ItemMaster>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task CreateAsync_UnknownStockNo_WithDescriptionAndUnit_CreatesItemFlaggedNew()
    {
        Mock<IStockBalanceRepository> stockRepo = RepoThatSaves();
        Mock<IInventoryRepository> invRepo = InventoryReturning(EmptyLevel("NEW01"));
        Mock<IItemMasterRepository> itemRepo = ItemRepoWithNoItems();

        ItemMaster? created = null;
        itemRepo.Setup(r => r.AddAsync(It.IsAny<ItemMaster>(), It.IsAny<CancellationToken>()))
            .Callback<ItemMaster, CancellationToken>((m, _) => created = m)
            .Returns(Task.CompletedTask);

        ServiceResult<StockBalanceDto> result = await BuildSut(stockRepo, invRepo, itemRepo).CreateAsync(
            MakeAdmin(),
            MakeCreateDto("NEW01", 10m, DateOnly.FromDateTime(DateTime.UtcNow),
                description: "Brand New Item", unit: "box", unitCost: 25m, itemType: "Office Supplies"));

        Assert.True(result.IsSuccess);
        Assert.True(result.Value!.ItemWasAutoCreated);
        Assert.NotNull(created);
        Assert.Equal("NEW01", created!.StockNo);
        Assert.Equal("Brand New Item", created.Description);
        Assert.Equal("box", created.Unit);
        Assert.Equal(25m, created.UnitCost);
        Assert.Equal("Office Supplies", created.ItemType);
        Assert.True(created.IsNewItem);
        Assert.Equal(0, created.ReorderQty);
        // The new ItemMaster is persisted via the same SaveChanges call as the StockBalance
        // entry (shared AppDbContext) — no separate save on the item repo.
        stockRepo.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task CreateAsync_KnownStockNo_IgnoresSubmittedItemFields_NeverCreatesDuplicate()
    {
        Mock<IStockBalanceRepository> stockRepo = RepoThatSaves();
        Mock<IInventoryRepository> invRepo = InventoryReturning(EmptyLevel("D01"));
        Mock<IItemMasterRepository> itemRepo = ItemRepoWithExistingItem();

        ServiceResult<StockBalanceDto> result = await BuildSut(stockRepo, invRepo, itemRepo).CreateAsync(
            MakeAdmin(),
            MakeCreateDto("D01", 10m, DateOnly.FromDateTime(DateTime.UtcNow),
                description: "Whatever the user typed", unit: "ignored-unit"));

        Assert.True(result.IsSuccess);
        Assert.False(result.Value!.ItemWasAutoCreated);
        itemRepo.Verify(r => r.AddAsync(It.IsAny<ItemMaster>(), It.IsAny<CancellationToken>()), Times.Never);
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
            [MakeCreateDto("A01", 10m, DateOnly.FromDateTime(DateTime.UtcNow), null)]);

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

        CommitStockBalanceImportDto dto = new([MakeCreateDto("A01", 20m, date, "Corrected count")]);

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
            [MakeCreateDto("", 10m, DateOnly.FromDateTime(DateTime.UtcNow), null)]);

        ServiceResult<StockBalanceImportResultDto> result =
            await BuildSut(stockRepo, invRepo).CommitImportAsync(MakeAdmin(), dto);

        Assert.Equal(ServiceErrorCode.BadRequest, result.Code);
    }

    [Fact]
    public async Task CommitImportAsync_UnknownStockNo_WithDescriptionAndUnit_CreatesItemFlaggedNew()
    {
        Mock<IStockBalanceRepository> stockRepo = RepoThatSaves();
        stockRepo.Setup(r => r.FindByStockNoAndEffectiveDateAsync(
                "NEW01", It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((StockBalance?)null);
        Mock<IInventoryRepository> invRepo = InventoryReturning(EmptyLevel("NEW01"));
        Mock<IItemMasterRepository> itemRepo = ItemRepoWithNoItems();

        CommitStockBalanceImportDto dto = new(
            [MakeCreateDto("NEW01", 10m, DateOnly.FromDateTime(DateTime.UtcNow),
                description: "Bulk New Item", unit: "ream")]);

        ServiceResult<StockBalanceImportResultDto> result =
            await BuildSut(stockRepo, invRepo, itemRepo).CommitImportAsync(MakeAdmin(), dto);

        Assert.True(result.IsSuccess);
        Assert.True(result.Value!.Entries[0].ItemWasAutoCreated);
        itemRepo.Verify(r => r.AddAsync(
            It.Is<ItemMaster>(m => m.StockNo == "NEW01" && m.IsNewItem && m.Description == "Bulk New Item"),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task CommitImportAsync_UnknownStockNo_MissingDescription_ReturnsBadRequest()
    {
        Mock<IStockBalanceRepository> stockRepo = RepoThatSaves();
        Mock<IInventoryRepository> invRepo = new();
        Mock<IItemMasterRepository> itemRepo = ItemRepoWithNoItems();

        CommitStockBalanceImportDto dto = new(
            [MakeCreateDto("NEW01", 10m, DateOnly.FromDateTime(DateTime.UtcNow), unit: "pcs")]); // no description

        ServiceResult<StockBalanceImportResultDto> result =
            await BuildSut(stockRepo, invRepo, itemRepo).CommitImportAsync(MakeAdmin(), dto);

        Assert.Equal(ServiceErrorCode.BadRequest, result.Code);
        itemRepo.Verify(r => r.AddAsync(It.IsAny<ItemMaster>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
