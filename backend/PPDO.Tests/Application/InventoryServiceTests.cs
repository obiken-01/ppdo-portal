using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using PPDO.Application.DTOs.Inventory;
using PPDO.Application.Services;
using PPDO.Domain.Entities;
using PPDO.Domain.Enums;
using PPDO.Domain.Interfaces;

namespace PPDO.Tests.Application;

/// <summary>
/// Unit tests for <see cref="InventoryService"/>.
/// Covers: PR stat card counts (Group 1), stock alert counts and totals (Group 2),
/// division scope for Staff/Observer, Item Ledger OnHand computation, IsLowStock,
/// IsOutOfStock flags, and items with no catalog entry.
/// </summary>
public sealed class InventoryServiceTests
{
    // ── Fixtures ──────────────────────────────────────────────────────────────

    private static User MakeAdmin() => new()
    {
        Id = Guid.NewGuid(), FullName = "Admin", Email = "admin@ppdo.gov.ph",
        PasswordHash = "hash", Role = UserRole.Admin, Division = Division.Admin, IsActive = true,
    };

    private static User MakeStaff(Division division = Division.Planning) => new()
    {
        Id = Guid.NewGuid(), FullName = "Staff", Email = "staff@ppdo.gov.ph",
        PasswordHash = "hash", Role = UserRole.Staff, Division = division, IsActive = true,
        Group = new PermissionGroup { Id = Guid.NewGuid(), Name = "Admin Division Staff", CanAccessInventory = true },
    };

    private static PurchaseRequest MakePR(PRStatus status, Division division = Division.Admin,
        decimal total = 0m) => new()
    {
        Id = Guid.NewGuid(), PRNo = "101-1041-GF-2026-06-02-001",
        PRDate = DateOnly.FromDateTime(DateTime.UtcNow),
        DateCreated = DateTime.UtcNow, Department = "PPDO",
        Division = division, Fund = "GAD", RequestedBy = "Ralph",
        Position = "Staff", Status = status, TotalAmount = total,
        CreatedById = Guid.NewGuid(), CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow,
        Items = new List<PRItem>(), Deliveries = new List<Delivery>(),
    };

    private static ItemMaster MakeMaster(string stockNo, int reorderQty = 5) => new()
    {
        Id = Guid.NewGuid(), StockNo = stockNo, Description = "Test Item",
        Unit = "pcs", UnitCost = 100m, ReorderQty = reorderQty,
        IsNewItem = false, CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow,
    };

    private static InventoryService BuildSut(
        Mock<IInventoryRepository> invRepo,
        Mock<IPurchaseRequestRepository> prRepo,
        Mock<IItemMasterRepository> itemRepo)
        => new(invRepo.Object, prRepo.Object, itemRepo.Object,
               NullLogger<InventoryService>.Instance);

    // ── Group 1 — PR stat cards ───────────────────────────────────────────────

    [Fact]
    public async Task GetStatsAsync_CountsPRsByStatus()
    {
        List<PurchaseRequest> prs =
        [
            MakePR(PRStatus.Open),
            MakePR(PRStatus.Open),
            MakePR(PRStatus.PartiallyDelivered),
            MakePR(PRStatus.FullyDelivered),
            MakePR(PRStatus.Completed),
        ];

        Mock<IPurchaseRequestRepository> prRepo = new();
        prRepo.Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>())).ReturnsAsync(prs);

        Mock<IInventoryRepository> invRepo = new();
        invRepo.Setup(r => r.GetItemStockLevelsAsync(null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ItemStockLevel>());

        Mock<IItemMasterRepository> itemRepo = new();
        itemRepo.Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ItemMaster>());

        InventoryStatsDto result = await BuildSut(invRepo, prRepo, itemRepo).GetStatsAsync(MakeAdmin());

        Assert.Equal(5, result.PurchaseRequests.Total);
        Assert.Equal(2, result.PurchaseRequests.Open);
        Assert.Equal(1, result.PurchaseRequests.PartiallyDelivered);
        Assert.Equal(2, result.PurchaseRequests.FullyDeliveredOrCompleted);
    }

    [Fact]
    public async Task GetStatsAsync_TotalPRValueSumsAllPRAmounts()
    {
        List<PurchaseRequest> prs =
        [
            MakePR(PRStatus.Open, total: 1000m),
            MakePR(PRStatus.FullyDelivered, total: 2500m),
        ];

        Mock<IPurchaseRequestRepository> prRepo = new();
        prRepo.Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>())).ReturnsAsync(prs);

        Mock<IInventoryRepository> invRepo = new();
        invRepo.Setup(r => r.GetItemStockLevelsAsync(null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ItemStockLevel>());

        Mock<IItemMasterRepository> itemRepo = new();
        itemRepo.Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ItemMaster>());

        InventoryStatsDto result = await BuildSut(invRepo, prRepo, itemRepo).GetStatsAsync(MakeAdmin());

        Assert.Equal(3500m, result.InventoryAlerts.TotalPRValue);
    }

    // ── Division scope ────────────────────────────────────────────────────────

    [Fact]
    public async Task GetStatsAsync_StaffRole_ScopesQueryToDivision()
    {
        User staff = MakeStaff(Division.Planning);
        Division? capturedDivision = null;

        Mock<IPurchaseRequestRepository> prRepo = new();
        prRepo.Setup(r => r.GetByDivisionAsync(Division.Planning, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<PurchaseRequest>());

        Mock<IInventoryRepository> invRepo = new();
        invRepo.Setup(r => r.GetItemStockLevelsAsync(It.IsAny<Division?>(), It.IsAny<CancellationToken>()))
            .Callback<Division?, CancellationToken>((d, _) => capturedDivision = d)
            .ReturnsAsync(new List<ItemStockLevel>());

        Mock<IItemMasterRepository> itemRepo = new();
        itemRepo.Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ItemMaster>());

        await BuildSut(invRepo, prRepo, itemRepo).GetStatsAsync(staff);

        // Staff → should scope to Planning division.
        prRepo.Verify(r => r.GetByDivisionAsync(Division.Planning, It.IsAny<CancellationToken>()), Times.Once);
        Assert.Equal(Division.Planning, capturedDivision);
    }

    [Fact]
    public async Task GetStatsAsync_AdminRole_PassesNullDivision()
    {
        Division? capturedDivision = (Division?)999; // sentinel — not null

        Mock<IPurchaseRequestRepository> prRepo = new();
        prRepo.Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<PurchaseRequest>());

        Mock<IInventoryRepository> invRepo = new();
        invRepo.Setup(r => r.GetItemStockLevelsAsync(It.IsAny<Division?>(), It.IsAny<CancellationToken>()))
            .Callback<Division?, CancellationToken>((d, _) => capturedDivision = d)
            .ReturnsAsync(new List<ItemStockLevel>());

        Mock<IItemMasterRepository> itemRepo = new();
        itemRepo.Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ItemMaster>());

        await BuildSut(invRepo, prRepo, itemRepo).GetStatsAsync(MakeAdmin());

        Assert.Null(capturedDivision); // Admin → null (all divisions)
    }

    // ── Group 2 — Stock alert counts ──────────────────────────────────────────

    [Fact]
    public async Task GetStatsAsync_CountsInStockAndLowStock_UsingReorderQty()
    {
        // Item A: OnHand = 10, ReorderQty = 5 → InStock
        // Item B: OnHand = 3,  ReorderQty = 5 → LowOrOutOfStock
        // Item C: OnHand = 0,  ReorderQty = 5 → LowOrOutOfStock
        List<ItemStockLevel> levels =
        [
            new("A01", QtyOrdered: 10, QtyDelivered: 10, QtyDistributed: 0),
            new("B01", QtyOrdered: 10, QtyDelivered: 10, QtyDistributed: 7),
            new("C01", QtyOrdered: 10, QtyDelivered: 10, QtyDistributed: 10),
        ];

        Mock<IPurchaseRequestRepository> prRepo = new();
        prRepo.Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<PurchaseRequest>());

        Mock<IInventoryRepository> invRepo = new();
        invRepo.Setup(r => r.GetItemStockLevelsAsync(null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(levels);

        Mock<IItemMasterRepository> itemRepo = new();
        itemRepo.Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ItemMaster>
            {
                MakeMaster("A01", reorderQty: 5),
                MakeMaster("B01", reorderQty: 5),
                MakeMaster("C01", reorderQty: 5),
            });

        InventoryStatsDto result = await BuildSut(invRepo, prRepo, itemRepo).GetStatsAsync(MakeAdmin());

        Assert.Equal(1, result.InventoryAlerts.InStock);          // A01 only
        Assert.Equal(2, result.InventoryAlerts.LowOrOutOfStock);  // B01 + C01
        Assert.Equal(3, result.InventoryAlerts.UniqueItemsTracked);
    }

    // ── Item Ledger ───────────────────────────────────────────────────────────

    [Fact]
    public async Task GetItemLedgerAsync_ComputesOnHandCorrectly()
    {
        // QtyDelivered=10, QtyDistributed=3 → OnHand=7
        List<ItemStockLevel> levels =
        [
            new("01-01", QtyOrdered: 20, QtyDelivered: 10, QtyDistributed: 3),
        ];

        Mock<IPurchaseRequestRepository> prRepo = new();
        prRepo.Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<PurchaseRequest>());

        Mock<IInventoryRepository> invRepo = new();
        invRepo.Setup(r => r.GetItemStockLevelsAsync(null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(levels);

        Mock<IItemMasterRepository> itemRepo = new();
        itemRepo.Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ItemMaster> { MakeMaster("01-01", reorderQty: 5) });

        IReadOnlyList<ItemLedgerRowDto> result =
            await BuildSut(invRepo, prRepo, itemRepo).GetItemLedgerAsync(MakeAdmin());

        Assert.Single(result);
        ItemLedgerRowDto row = result[0];
        Assert.Equal(7m, row.OnHand);
        Assert.Equal(20m, row.QtyOrdered);
        Assert.Equal(10m, row.QtyDelivered);
        Assert.Equal(3m, row.QtyDistributed);
        Assert.False(row.IsLowStock);   // 7 > ReorderQty 5
        Assert.False(row.IsOutOfStock);
    }

    [Fact]
    public async Task GetItemLedgerAsync_SetsIsLowStockWhenOnHandAtOrBelowReorderQty()
    {
        // OnHand = 4, ReorderQty = 5 → IsLowStock = true
        List<ItemStockLevel> levels =
        [
            new("02-01", QtyOrdered: 10, QtyDelivered: 10, QtyDistributed: 6),
        ];

        Mock<IPurchaseRequestRepository> prRepo = new();
        prRepo.Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<PurchaseRequest>());

        Mock<IInventoryRepository> invRepo = new();
        invRepo.Setup(r => r.GetItemStockLevelsAsync(null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(levels);

        Mock<IItemMasterRepository> itemRepo = new();
        itemRepo.Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ItemMaster> { MakeMaster("02-01", reorderQty: 5) });

        IReadOnlyList<ItemLedgerRowDto> result =
            await BuildSut(invRepo, prRepo, itemRepo).GetItemLedgerAsync(MakeAdmin());

        Assert.True(result[0].IsLowStock);
        Assert.False(result[0].IsOutOfStock);
    }

    [Fact]
    public async Task GetItemLedgerAsync_SetsIsOutOfStockWhenOnHandZeroOrNegative()
    {
        // OnHand = 0 → IsOutOfStock = true
        List<ItemStockLevel> levels =
        [
            new("03-01", QtyOrdered: 10, QtyDelivered: 10, QtyDistributed: 10),
        ];

        Mock<IPurchaseRequestRepository> prRepo = new();
        prRepo.Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<PurchaseRequest>());

        Mock<IInventoryRepository> invRepo = new();
        invRepo.Setup(r => r.GetItemStockLevelsAsync(null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(levels);

        Mock<IItemMasterRepository> itemRepo = new();
        itemRepo.Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ItemMaster> { MakeMaster("03-01", reorderQty: 5) });

        IReadOnlyList<ItemLedgerRowDto> result =
            await BuildSut(invRepo, prRepo, itemRepo).GetItemLedgerAsync(MakeAdmin());

        Assert.True(result[0].IsOutOfStock);
        Assert.False(result[0].IsLowStock); // 0 is not > 0, so IsLowStock = false
    }

    [Fact]
    public async Task GetItemLedgerAsync_ItemNotInCatalog_UsesStockNoAsDescription()
    {
        // StockNo exists in PRs but has no ItemMaster entry (e.g. new item not yet reviewed)
        List<ItemStockLevel> levels = [ new("UNKNOWN-99", 5, 0, 0) ];

        Mock<IPurchaseRequestRepository> prRepo = new();
        prRepo.Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<PurchaseRequest>());

        Mock<IInventoryRepository> invRepo = new();
        invRepo.Setup(r => r.GetItemStockLevelsAsync(null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(levels);

        Mock<IItemMasterRepository> itemRepo = new();
        itemRepo.Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ItemMaster>()); // empty catalog

        IReadOnlyList<ItemLedgerRowDto> result =
            await BuildSut(invRepo, prRepo, itemRepo).GetItemLedgerAsync(MakeAdmin());

        Assert.Single(result);
        Assert.Equal("UNKNOWN-99", result[0].Description); // falls back to StockNo
    }

    [Fact]
    public async Task GetItemLedgerAsync_ResultsOrderedByStockNo()
    {
        List<ItemStockLevel> levels =
        [
            new("C-03", 5, 5, 0),
            new("A-01", 5, 5, 0),
            new("B-02", 5, 5, 0),
        ];

        Mock<IPurchaseRequestRepository> prRepo = new();
        prRepo.Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<PurchaseRequest>());

        Mock<IInventoryRepository> invRepo = new();
        invRepo.Setup(r => r.GetItemStockLevelsAsync(null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(levels);

        Mock<IItemMasterRepository> itemRepo = new();
        itemRepo.Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ItemMaster>());

        IReadOnlyList<ItemLedgerRowDto> result =
            await BuildSut(invRepo, prRepo, itemRepo).GetItemLedgerAsync(MakeAdmin());

        Assert.Equal("A-01", result[0].StockNo);
        Assert.Equal("B-02", result[1].StockNo);
        Assert.Equal("C-03", result[2].StockNo);
    }
}
