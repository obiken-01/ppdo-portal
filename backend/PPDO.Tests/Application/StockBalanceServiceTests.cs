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
        // No real transaction in a unit test — just run the delegate inline, same as the
        // real Repository<T> does once it's inside the (mocked-away) execution strategy.
        repo.Setup(r => r.ExecuteInTransactionAsync(It.IsAny<Func<Task>>(), It.IsAny<CancellationToken>()))
            .Returns((Func<Task> operation, CancellationToken _) => operation());
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
        Mock<IExcelService>? excelRepo = null,
        Mock<IAuditService>? auditService = null,
        bool delegateBatchLookups = true)
    {
        itemRepo ??= ItemRepoWithExistingItem();
        if (delegateBatchLookups) WithBatchLookups(stockRepo, invRepo, itemRepo);

        return new(
            stockRepo.Object,
            invRepo.Object,
            itemRepo.Object,
            (userRepo ?? UserRepoStub()).Object,
            new PermissionService(),
            (excelRepo ?? new Mock<IExcelService>()).Object,
            (auditService ?? new Mock<IAuditService>()).Object,
            NullLogger<StockBalanceService>.Instance);
    }

    /// <summary>
    /// PPDO-189: the bulk import loads item masters, existing balances and on-hand inputs for the
    /// whole file in batch calls. These tests still describe their data through the single-key
    /// lookups (GetByStockNoAsync, FindByStockNoAndEffectiveDateAsync, GetItemStockLevelAsync), so
    /// the batch calls delegate to those setups at call time. A test that sets up a batch call
    /// itself is unaffected only if it does so after BuildSut — none do.
    /// </summary>
    private static void WithBatchLookups(
        Mock<IStockBalanceRepository> stockRepo,
        Mock<IInventoryRepository> invRepo,
        Mock<IItemMasterRepository> itemRepo)
    {
        itemRepo.Setup(r => r.GetByStockNosAsync(It.IsAny<IReadOnlyCollection<string>>(), It.IsAny<CancellationToken>()))
            .Returns(async (IReadOnlyCollection<string> nos, CancellationToken ct) =>
            {
                List<ItemMaster> found = [];
                foreach (string no in nos)
                {
                    ItemMaster? m = await itemRepo.Object.GetByStockNoAsync(no, ct);
                    if (m is not null) found.Add(m);
                }
                return (IReadOnlyList<ItemMaster>)found;
            });

        stockRepo.Setup(r => r.GetByStockNosAndDatesAsync(
                It.IsAny<IReadOnlyCollection<string>>(), It.IsAny<IReadOnlyCollection<DateOnly>>(), It.IsAny<CancellationToken>()))
            .Returns(async (IReadOnlyCollection<string> nos, IReadOnlyCollection<DateOnly> dates, CancellationToken ct) =>
            {
                List<StockBalance> found = [];
                foreach (string no in nos)
                    foreach (DateOnly d in dates)
                    {
                        StockBalance? b = await stockRepo.Object.FindByStockNoAndEffectiveDateAsync(no, d, ct);
                        if (b is not null) found.Add(b);
                    }
                return (IReadOnlyList<StockBalance>)found;
            });

        invRepo.Setup(r => r.GetItemStockLevelsByStockNosAsync(It.IsAny<IReadOnlyCollection<string>>(), It.IsAny<CancellationToken>()))
            .Returns(async (IReadOnlyCollection<string> nos, CancellationToken ct) =>
            {
                Dictionary<string, ItemStockLevel> levels = new(StringComparer.OrdinalIgnoreCase);
                foreach (string no in nos)
                    levels[no] = await invRepo.Object.GetItemStockLevelAsync(no, ct) ?? EmptyLevel(no);
                return (IReadOnlyDictionary<string, ItemStockLevel>)levels;
            });
    }

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

    // ── CommitImportAsync — transaction boundary (RAL-207) ────────────────────

    [Fact]
    public async Task CommitImportAsync_RunsInsideRepositoryTransaction()
    {
        // Structural regression guard: the per-row loop must stay wrapped in
        // ExecuteInTransactionAsync so a mid-loop failure rolls the whole file back
        // instead of leaving earlier rows partially committed.
        Mock<IStockBalanceRepository> stockRepo = RepoThatSaves();
        stockRepo.Setup(r => r.FindByStockNoAndEffectiveDateAsync(
                It.IsAny<string>(), It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((StockBalance?)null);
        Mock<IInventoryRepository> invRepo = InventoryReturning(EmptyLevel("A01"));

        CommitStockBalanceImportDto dto = new(
            [MakeCreateDto("A01", 10m, DateOnly.FromDateTime(DateTime.UtcNow), null)]);

        await BuildSut(stockRepo, invRepo).CommitImportAsync(MakeAdmin(), dto);

        stockRepo.Verify(r => r.ExecuteInTransactionAsync(
            It.IsAny<Func<Task>>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task CommitImportAsync_SecondRowInvalid_ReturnsBadRequest_ReferencingFailingRow()
    {
        // Row 1 is valid and would have saved under the old (unwrapped) code before row 2's
        // failure was discovered — that partial-commit gap is exactly what RAL-207 closes.
        Mock<IStockBalanceRepository> stockRepo = RepoThatSaves();
        stockRepo.Setup(r => r.FindByStockNoAndEffectiveDateAsync(
                It.IsAny<string>(), It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((StockBalance?)null);
        Mock<IInventoryRepository> invRepo = new();
        invRepo.Setup(r => r.GetItemStockLevelAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string sn, CancellationToken _) => new ItemStockLevel(sn, 0m, 0m, 0m));

        DateOnly date = DateOnly.FromDateTime(DateTime.UtcNow);
        CommitStockBalanceImportDto dto = new(
        [
            MakeCreateDto("A01", 10m, date, null),
            MakeCreateDto("B01", -5m, date, null), // negative CountedQty fails Validate()
        ]);

        ServiceResult<StockBalanceImportResultDto> result =
            await BuildSut(stockRepo, invRepo).CommitImportAsync(MakeAdmin(), dto);

        Assert.Equal(ServiceErrorCode.BadRequest, result.Code);
        Assert.Contains("B01", result.Error);
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

    // ── Audit logging (RAL-200) ───────────────────────────────────────────────
    //
    // Highest-value target in this ticket — this table has no approval step, so the audit
    // trail is the only accountability mechanism for an Admin/SuperAdmin directly overwriting
    // computed on-hand.

    [Fact]
    public async Task CreateAsync_CallsAuditLog_WithCreateAction()
    {
        Mock<IStockBalanceRepository> stockRepo = RepoThatSaves();
        Mock<IInventoryRepository> invRepo = InventoryReturning(EmptyLevel("A01"));
        Mock<IAuditService> audit = new();

        ServiceResult<StockBalanceDto> result = await BuildSut(stockRepo, invRepo, auditService: audit).CreateAsync(
            MakeAdmin(), MakeCreateDto("A01", 50m, DateOnly.FromDateTime(DateTime.UtcNow), "Initial count"));

        Assert.True(result.IsSuccess);
        audit.Verify(a => a.LogAsync(
            "stock_balances", result.Value!.Id, AuditAction.Create,
            null, It.IsAny<object?>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task UpdateAsync_ChangedCountedQty_CallsAuditLog_CapturingOldAndNewVariance()
    {
        Guid entryId = Guid.NewGuid();
        StockBalance existing = new()
        {
            Id = entryId, StockNo = "E01", CountedQty = 5m, SystemOnHandAtEntry = 0m,
            VarianceQty = 5m, EffectiveDate = DateOnly.FromDateTime(DateTime.UtcNow),
            RecordedByUserId = Guid.NewGuid(),
        };

        Mock<IStockBalanceRepository> stockRepo = RepoThatSaves();
        stockRepo.Setup(r => r.GetByIdAsync(entryId, It.IsAny<CancellationToken>())).ReturnsAsync(existing);
        Mock<IInventoryRepository> invRepo = InventoryReturning(new ItemStockLevel("E01", 10m, 10m, 0m));
        Mock<IAuditService> audit = new();

        await BuildSut(stockRepo, invRepo, auditService: audit).UpdateAsync(
            MakeAdmin(), entryId, new UpdateStockBalanceDto(13m, null, null));

        audit.Verify(a => a.LogAsync(
            "stock_balances", entryId, AuditAction.Update,
            It.Is<object>(o => ((IDictionary<string, object?>)o).ContainsKey("CountedQty")),
            It.IsAny<object?>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task DeleteAsync_CallsAuditLog_WithDeleteAction_CapturingRemovedEntry()
    {
        Guid entryId = Guid.NewGuid();
        StockBalance existing = new()
        {
            Id = entryId, StockNo = "F01", CountedQty = 5m, SystemOnHandAtEntry = 0m,
            VarianceQty = 5m, EffectiveDate = DateOnly.FromDateTime(DateTime.UtcNow),
            RecordedByUserId = Guid.NewGuid(),
        };

        Mock<IStockBalanceRepository> stockRepo = RepoThatSaves();
        stockRepo.Setup(r => r.GetByIdAsync(entryId, It.IsAny<CancellationToken>())).ReturnsAsync(existing);
        Mock<IInventoryRepository> invRepo = new();
        Mock<IAuditService> audit = new();

        await BuildSut(stockRepo, invRepo, auditService: audit).DeleteAsync(MakeAdmin(), entryId);

        audit.Verify(a => a.LogAsync(
            "stock_balances", entryId, AuditAction.Delete,
            It.IsNotNull<object>(), null, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task CommitImportAsync_NewRow_CallsAuditLog_WithCreateAction()
    {
        Mock<IStockBalanceRepository> stockRepo = RepoThatSaves();
        stockRepo.Setup(r => r.FindByStockNoAndEffectiveDateAsync(
                "A01", It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((StockBalance?)null);
        Mock<IInventoryRepository> invRepo = InventoryReturning(EmptyLevel("A01"));
        Mock<IAuditService> audit = new();

        CommitStockBalanceImportDto dto = new(
            [MakeCreateDto("A01", 10m, DateOnly.FromDateTime(DateTime.UtcNow), null)]);

        ServiceResult<StockBalanceImportResultDto> result =
            await BuildSut(stockRepo, invRepo, auditService: audit).CommitImportAsync(MakeAdmin(), dto);

        Assert.True(result.IsSuccess);
        audit.Verify(a => a.LogAsync(
            "stock_balances", result.Value!.Entries[0].Id, AuditAction.Create,
            null, It.IsAny<object?>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task CommitImportAsync_UpsertingRow_CallsAuditLog_WithUpdateAction_CapturingOldAndNew()
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
        Mock<IAuditService> audit = new();

        CommitStockBalanceImportDto dto = new([MakeCreateDto("A01", 20m, date, "Corrected count")]);

        await BuildSut(stockRepo, invRepo, auditService: audit).CommitImportAsync(MakeAdmin(), dto);

        audit.Verify(a => a.LogAsync(
            "stock_balances", existing.Id, AuditAction.Update,
            It.IsNotNull<object>(), It.IsNotNull<object>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task CommitImportAsync_MultipleRows_CallsAuditLogOncePerRow_NotOneSummarizedEntry()
    {
        // Unlike PR Excel import (deliberately summarized), a bulk stock-balance overwrite
        // has no approval step — each row needs its own accountable entry, not a rollup that
        // hides which specific rows changed.
        Mock<IStockBalanceRepository> stockRepo = RepoThatSaves();
        stockRepo.Setup(r => r.FindByStockNoAndEffectiveDateAsync(
                It.IsAny<string>(), It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((StockBalance?)null);
        Mock<IInventoryRepository> invRepo = new();
        invRepo.Setup(r => r.GetItemStockLevelAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string sn, CancellationToken _) => new ItemStockLevel(sn, 0m, 0m, 0m));
        Mock<IAuditService> audit = new();

        DateOnly date = DateOnly.FromDateTime(DateTime.UtcNow);
        CommitStockBalanceImportDto dto = new(
        [
            MakeCreateDto("A01", 10m, date, null),
            MakeCreateDto("B01", 20m, date, null),
        ]);

        await BuildSut(stockRepo, invRepo, auditService: audit).CommitImportAsync(MakeAdmin(), dto);

        audit.Verify(a => a.LogAsync(
            "stock_balances", It.IsAny<Guid>(), AuditAction.Create,
            null, It.IsAny<object?>(), It.IsAny<CancellationToken>()), Times.Exactly(2));
    }

    // ── CommitImportAsync — repeated StockNo within one file (PPDO-189) ───────
    //
    // These pin what the per-row loop does today, with a stateful fake standing in for the
    // database: a row's writes are visible to every later row of the same file. They have to
    // keep passing when the loop is rewritten to load everything up front.

    /// <summary>
    /// Minimal stateful stand-in for the shared AppDbContext. Writes are staged by AddAsync and
    /// become visible (to lookups, to the variance total) only on SaveChangesAsync — exactly the
    /// behaviour the import relies on.
    /// </summary>
    private sealed class FakeImportDb
    {
        public List<StockBalance> Balances { get; } = [];
        public List<ItemMaster>   Items    { get; } = [];
        private readonly List<StockBalance> _stagedBalances = [];
        private readonly List<ItemMaster>   _stagedItems    = [];

        public Mock<IStockBalanceRepository> StockRepo { get; }
        public Mock<IItemMasterRepository>   ItemRepo  { get; }
        public Mock<IInventoryRepository>    InvRepo   { get; }

        private readonly decimal _qtyDelivered;
        private readonly decimal _qtyDistributed;

        public FakeImportDb(decimal qtyDelivered, decimal qtyDistributed)
        {
            _qtyDelivered   = qtyDelivered;
            _qtyDistributed = qtyDistributed;
            StockRepo = RepoThatSaves();
            StockRepo.Setup(r => r.AddAsync(It.IsAny<StockBalance>(), It.IsAny<CancellationToken>()))
                .Callback((StockBalance b, CancellationToken _) => _stagedBalances.Add(b))
                .Returns(Task.CompletedTask);
            StockRepo.Setup(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync(() =>
                {
                    Balances.AddRange(_stagedBalances); _stagedBalances.Clear();
                    Items.AddRange(_stagedItems);       _stagedItems.Clear();
                    return 1;
                });
            StockRepo.Setup(r => r.FindByStockNoAndEffectiveDateAsync(
                    It.IsAny<string>(), It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((string sn, DateOnly d, CancellationToken _) =>
                    Balances.FirstOrDefault(b =>
                        string.Equals(b.StockNo, sn, StringComparison.OrdinalIgnoreCase) && b.EffectiveDate == d));
            StockRepo.Setup(r => r.GetTotalVarianceByStockNosAsync(
                    It.IsAny<IReadOnlyCollection<string>>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((IReadOnlyCollection<string> nos, CancellationToken _) =>
                    (IReadOnlyDictionary<string, decimal>)Balances
                        .Where(b => nos.Contains(b.StockNo, StringComparer.OrdinalIgnoreCase))
                        .GroupBy(b => b.StockNo)
                        .ToDictionary(g => g.Key, g => g.Sum(b => b.VarianceQty)));

            ItemRepo = new Mock<IItemMasterRepository>();
            ItemRepo.Setup(r => r.GetByStockNoAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((string sn, CancellationToken _) =>
                    Items.FirstOrDefault(i => string.Equals(i.StockNo, sn, StringComparison.OrdinalIgnoreCase)));
            ItemRepo.Setup(r => r.AddAsync(It.IsAny<ItemMaster>(), It.IsAny<CancellationToken>()))
                .Callback((ItemMaster m, CancellationToken _) => _stagedItems.Add(m))
                .Returns(Task.CompletedTask);

            InvRepo = new Mock<IInventoryRepository>();
            InvRepo.Setup(r => r.GetItemStockLevelAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((string sn, CancellationToken _) => new ItemStockLevel(sn, 0m, qtyDelivered, qtyDistributed));
        }

        public StockBalanceService Build(Mock<IAuditService>? audit = null)
        {
            // The batch calls the import actually makes, answered from the fake's own state — no
            // delegation to the single-key lookups, so tests can assert those are never used.
            ItemRepo.Setup(r => r.GetByStockNosAsync(It.IsAny<IReadOnlyCollection<string>>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((IReadOnlyCollection<string> nos, CancellationToken _) =>
                    (IReadOnlyList<ItemMaster>)Items
                        .Where(i => nos.Contains(i.StockNo, StringComparer.OrdinalIgnoreCase)).ToList());
            StockRepo.Setup(r => r.GetByStockNosAndDatesAsync(
                    It.IsAny<IReadOnlyCollection<string>>(), It.IsAny<IReadOnlyCollection<DateOnly>>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((IReadOnlyCollection<string> nos, IReadOnlyCollection<DateOnly> dates, CancellationToken _) =>
                    (IReadOnlyList<StockBalance>)Balances
                        .Where(b => nos.Contains(b.StockNo, StringComparer.OrdinalIgnoreCase) && dates.Contains(b.EffectiveDate))
                        .ToList());
            InvRepo.Setup(r => r.GetItemStockLevelsByStockNosAsync(It.IsAny<IReadOnlyCollection<string>>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((IReadOnlyCollection<string> nos, CancellationToken _) =>
                    (IReadOnlyDictionary<string, ItemStockLevel>)nos.ToDictionary(
                        n => n, n => new ItemStockLevel(n, 0m, _qtyDelivered, _qtyDistributed), StringComparer.OrdinalIgnoreCase));

            return BuildSut(StockRepo, InvRepo, ItemRepo, auditService: audit, delegateBatchLookups: false);
        }
    }

    [Fact]
    public async Task CommitImportAsync_SameStockNoOnTwoDates_StacksTheVarianceAndCreatesTheItemOnce()
    {
        DateOnly d1 = new(2026, 9, 1), d2 = new(2026, 9, 15);
        FakeImportDb db = new(qtyDelivered: 100m, qtyDistributed: 0m);

        CommitStockBalanceImportDto dto = new(
        [
            MakeCreateDto("NEW-1", 10m, d1, description: "Bond paper", unit: "ream", unitCost: 250m),
            MakeCreateDto("NEW-1", 25m, d2, description: "Bond paper", unit: "ream", unitCost: 250m),
        ]);

        ServiceResult<StockBalanceImportResultDto> result = await db.Build().CommitImportAsync(MakeAdmin(), dto);

        Assert.True(result.IsSuccess);
        Assert.Equal(2, result.Value!.Inserted);
        Assert.Equal(0, result.Value.Updated);

        // Row 1: on hand = 100 delivered -> variance 10 - 100 = -90.
        // Row 2 sees row 1's -90: on hand = 100 - 90 = 10 -> variance 25 - 10 = +15.
        Assert.Equal(100m, result.Value.Entries[0].SystemOnHandAtEntry);
        Assert.Equal(-90m, result.Value.Entries[0].VarianceQty);
        Assert.Equal(10m,  result.Value.Entries[1].SystemOnHandAtEntry);
        Assert.Equal(15m,  result.Value.Entries[1].VarianceQty);

        // The catalogue gets ONE new item, and only the first row reports having created it.
        Assert.Single(db.Items);
        Assert.True(result.Value.Entries[0].ItemWasAutoCreated);
        Assert.False(result.Value.Entries[1].ItemWasAutoCreated);
        Assert.Equal(2, db.Balances.Count);
    }

    [Fact]
    public async Task CommitImportAsync_SameStockNoAndDateTwice_SecondRowOverwritesTheFirst()
    {
        DateOnly date = new(2026, 9, 1);
        FakeImportDb db = new(qtyDelivered: 100m, qtyDistributed: 0m);

        CommitStockBalanceImportDto dto = new(
        [
            MakeCreateDto("NEW-1", 10m, date, description: "Bond paper", unit: "ream"),
            MakeCreateDto("NEW-1", 30m, date, reason: "second count"),
        ]);

        ServiceResult<StockBalanceImportResultDto> result = await db.Build().CommitImportAsync(MakeAdmin(), dto);

        Assert.True(result.IsSuccess);
        Assert.Equal(1, result.Value!.Inserted);
        Assert.Equal(1, result.Value.Updated);

        // One balance row survives, holding the second count. Its own earlier -90 is excluded
        // from the on-hand it is compared against: 100 + (-90) - (-90) = 100 -> variance -70.
        StockBalance only = Assert.Single(db.Balances);
        Assert.Equal(30m, only.CountedQty);
        Assert.Equal(100m, only.SystemOnHandAtEntry);
        Assert.Equal(-70m, only.VarianceQty);
        Assert.Equal("second count", only.Reason);

        // Both result entries are the same row.
        Assert.Equal(result.Value.Entries[0].Id, result.Value.Entries[1].Id);
        Assert.Single(db.Items);
    }

    [Fact]
    public async Task CommitImportAsync_ManyRows_LoadsInBatchesAndSavesOnce()
    {
        FakeImportDb db = new(qtyDelivered: 0m, qtyDistributed: 0m);
        DateOnly date = new(2026, 9, 1);

        List<CreateStockBalanceDto> rows = [];
        for (int i = 0; i < 50; i++)
            rows.Add(MakeCreateDto($"ITEM-{i:D3}", i, date, description: "Test item", unit: "pc"));

        ServiceResult<StockBalanceImportResultDto> result =
            await db.Build().CommitImportAsync(MakeAdmin(), new CommitStockBalanceImportDto(rows));

        Assert.True(result.IsSuccess);
        Assert.Equal(50, result.Value!.Inserted);
        Assert.Equal(50, db.Items.Count);

        // Every lookup is one call for the whole file...
        db.ItemRepo.Verify(r => r.GetByStockNosAsync(It.IsAny<IReadOnlyCollection<string>>(), It.IsAny<CancellationToken>()), Times.Once);
        db.StockRepo.Verify(r => r.GetByStockNosAndDatesAsync(
            It.IsAny<IReadOnlyCollection<string>>(), It.IsAny<IReadOnlyCollection<DateOnly>>(), It.IsAny<CancellationToken>()), Times.Once);
        db.InvRepo.Verify(r => r.GetItemStockLevelsByStockNosAsync(It.IsAny<IReadOnlyCollection<string>>(), It.IsAny<CancellationToken>()), Times.Once);
        db.StockRepo.Verify(r => r.GetTotalVarianceByStockNosAsync(It.IsAny<IReadOnlyCollection<string>>(), It.IsAny<CancellationToken>()), Times.Once);

        // ...and nothing is looked up row by row any more.
        db.ItemRepo.Verify(r => r.GetByStockNoAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        db.StockRepo.Verify(r => r.FindByStockNoAndEffectiveDateAsync(It.IsAny<string>(), It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()), Times.Never);
        db.InvRepo.Verify(r => r.GetItemStockLevelAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);

        // One flush for the whole file.
        db.StockRepo.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task CommitImportAsync_ReuploadingTheSameFile_OverwritesRatherThanDuplicates()
    {
        DateOnly d1 = new(2026, 9, 1), d2 = new(2026, 9, 2);
        FakeImportDb db = new(qtyDelivered: 100m, qtyDistributed: 20m);   // movement on hand = 80
        CommitStockBalanceImportDto file = new(
        [
            MakeCreateDto("NEW-1", 10m, d1, description: "Bond paper", unit: "ream"),
            MakeCreateDto("NEW-1", 25m, d2),
            MakeCreateDto("NEW-2", 7m,  d1, description: "Folder", unit: "pc"),
        ]);

        ServiceResult<StockBalanceImportResultDto> first = await db.Build().CommitImportAsync(MakeAdmin(), file);
        List<Guid> idsAfterFirst = db.Balances.Select(b => b.Id).ToList();
        ServiceResult<StockBalanceImportResultDto> second = await db.Build().CommitImportAsync(MakeAdmin(), file);

        // Same three rows, overwritten in place: no new balance rows, no new items.
        Assert.Equal(3, first.Value!.Inserted);
        Assert.Equal(0, second.Value!.Inserted);
        Assert.Equal(3, second.Value.Updated);
        Assert.Equal(3, db.Balances.Count);
        Assert.Equal(idsAfterFirst, db.Balances.Select(b => b.Id).ToList());
        Assert.Equal(2, db.Items.Count);

        // First upload: NEW-1/d1 = 10-80 = -70; NEW-1/d2 sees -70 -> 25-10 = +15; NEW-2/d1 = 7-80 = -73.
        // Re-upload recomputes each row against the OTHER rows' current totals (not a replay of the
        // file), so NEW-1/d1 now also sees d2's +15: onHand 80+15 = 95 -> -85, then d2 sees d1's new
        // -85: onHand 80-85 = -5 -> +30. That is how the per-row loop behaved; it is pinned here.
        StockBalance n1d1 = db.Balances.Single(b => b.StockNo == "NEW-1" && b.EffectiveDate == d1);
        StockBalance n1d2 = db.Balances.Single(b => b.StockNo == "NEW-1" && b.EffectiveDate == d2);
        StockBalance n2d1 = db.Balances.Single(b => b.StockNo == "NEW-2");
        Assert.Equal((95m, -85m), (n1d1.SystemOnHandAtEntry, n1d1.VarianceQty));
        Assert.Equal((-5m, 30m),  (n1d2.SystemOnHandAtEntry, n1d2.VarianceQty));
        Assert.Equal((80m, -73m), (n2d1.SystemOnHandAtEntry, n2d1.VarianceQty));
    }

    [Fact]
    public async Task CommitImportAsync_TransactionRetried_DropsTheFailedAttemptsTrackedEntities()
    {
        FakeImportDb db = new(qtyDelivered: 0m, qtyDistributed: 0m);
        // The execution strategy re-runs the delegate after a transient fault.
        db.StockRepo.Setup(r => r.ExecuteInTransactionAsync(It.IsAny<Func<Task>>(), It.IsAny<CancellationToken>()))
            .Returns(async (Func<Task> operation, CancellationToken _) => { await operation(); await operation(); });

        await db.Build().CommitImportAsync(MakeAdmin(), new CommitStockBalanceImportDto(
            [MakeCreateDto("NEW-1", 10m, new DateOnly(2026, 9, 1), description: "Bond paper", unit: "ream")]));

        db.StockRepo.Verify(r => r.ResetChangeTracking(), Times.Once);
    }
}
