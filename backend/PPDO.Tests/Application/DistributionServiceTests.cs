using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using PPDO.Application.Common;
using PPDO.Application.DTOs.Distribution;
using PPDO.Application.Services;
using PPDO.Domain.Entities;
using PPDO.Domain.Enums;
using PPDO.Domain.Interfaces;

namespace PPDO.Tests.Application;

/// <summary>
/// Unit tests for <see cref="DistributionService.AllocateAsync"/> — server-side FIFO
/// batch allocation (RAL-194). Covers: permission gate, split validation, division
/// resolution, division-scope enforcement, over-allocation rejection, and FIFO ordering
/// (oldest DeliveryDate consumed first, splits able to span multiple batches).
/// </summary>
public sealed class DistributionServiceTests
{
    // ── Fixtures ──────────────────────────────────────────────────────────────

    private const int AdminDiv    = 1;
    private const int PlanningDiv = 2;

    private static Mock<IRepository<Division>> DivisionsRepo()
    {
        Mock<IRepository<Division>> repo = new();
        repo.Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Division>
            {
                new() { Id = AdminDiv,    OfficeId = 100, Name = "Administrative Division", IsActive = true },
                new() { Id = PlanningDiv, OfficeId = 100, Name = "Planning Division",       IsActive = true },
            });
        return repo;
    }

    private static User MakeAdmin() => new()
    {
        Id = Guid.NewGuid(), FullName = "Admin", Email = "admin@ppdo.gov.ph",
        PasswordHash = "hash", Role = UserRole.Admin, IsActive = true,
    };

    private static User MakeStaff(int? divisionId) => new()
    {
        Id = Guid.NewGuid(), FullName = "Staff", Email = "staff@ppdo.gov.ph",
        PasswordHash = "hash", Role = UserRole.Staff, DivisionId = divisionId,
        Division = divisionId is int id
            ? new Division { Id = id, OfficeId = 100, Name = "Administrative Division", CanAccessInventory = true }
            : null,
        // Staff with no division has no Division navigation to inherit CanAccessInventory
        // from — grant it via override so tests can reach the division-scope check itself.
        OverrideCanAccessInventory = divisionId is null ? true : null,
        IsActive = true,
    };

    private static DeliveryItemBreakdownRow Batch(
        Guid deliveryItemId, DateOnly date, decimal delivered,
        decimal alreadyDistributed = 0m, string deliveryRef = "DEL-20260101-ABCDE")
        => new(
            DeliveryItemId: deliveryItemId,
            DeliveryRef:    deliveryRef,
            DeliveryDate:   date,
            PRId:           Guid.NewGuid(),
            PRNo:           "101-1041-GF-2026-01-01-001",
            QtyDelivered:   delivered,
            Distributions:  alreadyDistributed > 0
                ? new List<DistributionBreakdownRow>
                    {
                        new(Guid.NewGuid(), "ISS-20260101-AAAAA-1", AdminDiv,
                            alreadyDistributed, date, "Prior", null),
                    }
                : new List<DistributionBreakdownRow>());

    private static DistributionSplitDto Split(
        decimal qty, string division = "Administrative Division", string issuedBy = "Ralph")
        => new()
        {
            Division   = division,
            QtyIssued  = qty,
            DateIssued = DateOnly.FromDateTime(DateTime.UtcNow),
            IssuedBy   = issuedBy,
        };

    private static Mock<IDeliveryRepository> RepoWithBatches(
        string stockNo, IReadOnlyList<DeliveryItemBreakdownRow> batches, int? expectedScopeDivision = -1)
    {
        Mock<IDeliveryRepository> repo = new();
        if (expectedScopeDivision == -1)
        {
            // No scope assertion — match any division argument.
            repo.Setup(r => r.GetDeliveryItemBreakdownsByStockNoAsync(
                    stockNo, It.IsAny<int?>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(batches);
        }
        else
        {
            repo.Setup(r => r.GetDeliveryItemBreakdownsByStockNoAsync(
                    stockNo, expectedScopeDivision, It.IsAny<CancellationToken>()))
                .ReturnsAsync(batches);
        }
        return repo;
    }

    private static Mock<IRepository<Distribution>> DistributionsRepoThatSaves()
    {
        Mock<IRepository<Distribution>> repo = new();
        repo.Setup(r => r.AddAsync(It.IsAny<Distribution>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        repo.Setup(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);
        return repo;
    }

    private static Mock<IItemMasterRepository> ItemsRepo(string stockNo)
    {
        Mock<IItemMasterRepository> repo = new();
        repo.Setup(r => r.GetByStockNoAsync(stockNo, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ItemMaster { Id = Guid.NewGuid(), StockNo = stockNo, Description = "Bond Paper", Unit = "ream" });
        return repo;
    }

    /// <summary>
    /// Unconfigured GetPoolByStockNoAsync/GetDistributionsByStockNoAsync return null / empty
    /// via Moq's loose-mock Task defaulting — this is "no warehouse count exists for this
    /// StockNo," matching every pre-RAL-223 test's assumption.
    /// </summary>
    private static Mock<IStockBalanceRepository> NoWarehouseCountRepo() => new();

    private static Mock<IStockBalanceRepository> WarehouseCountRepo(
        string stockNo, WarehouseCountPoolRow pool,
        IReadOnlyList<DistributionBreakdownRow>? distributions = null)
    {
        Mock<IStockBalanceRepository> repo = new();
        repo.Setup(r => r.GetPoolByStockNoAsync(stockNo, It.IsAny<CancellationToken>()))
            .ReturnsAsync(pool);
        repo.Setup(r => r.GetDistributionsByStockNoAsync(stockNo, It.IsAny<CancellationToken>()))
            .ReturnsAsync(distributions ?? Array.Empty<DistributionBreakdownRow>());
        return repo;
    }

    private static DistributionService BuildSut(
        Mock<IDeliveryRepository> deliveryRepo,
        Mock<IItemMasterRepository> itemsRepo,
        Mock<IRepository<Distribution>> distributionsRepo,
        Mock<IRepository<Division>>? divisionRepo = null,
        Mock<IAuditService>? auditService = null,
        Mock<IStockBalanceRepository>? stockBalanceRepo = null)
        => new(
            deliveryRepo.Object,
            (stockBalanceRepo ?? NoWarehouseCountRepo()).Object,
            itemsRepo.Object,
            new PermissionService(),
            distributionsRepo.Object,
            (divisionRepo ?? DivisionsRepo()).Object,
            (auditService ?? new Mock<IAuditService>()).Object,
            NullLogger<DistributionService>.Instance);

    // ── Permission gate ───────────────────────────────────────────────────────

    [Fact]
    public async Task AllocateAsync_WithoutInventoryPermission_ReturnsForbidden()
    {
        User staff = MakeStaff(PlanningDiv);
        staff.Division!.CanAccessInventory = false;

        CreateItemDistributionDto dto = new() { Splits = new List<DistributionSplitDto> { Split(5m) } };

        ServiceResult<IReadOnlyList<DistributionCreatedDto>> result =
            await BuildSut(RepoWithBatches("STK-1", Array.Empty<DeliveryItemBreakdownRow>()),
                    ItemsRepo("STK-1"), DistributionsRepoThatSaves())
                .AllocateAsync(staff, "STK-1", dto);

        Assert.Equal(ServiceErrorCode.Forbidden, result.Code);
    }

    // ── Split validation ──────────────────────────────────────────────────────

    [Fact]
    public async Task AllocateAsync_NoSplits_ReturnsBadRequest()
    {
        CreateItemDistributionDto dto = new() { Splits = new List<DistributionSplitDto>() };

        ServiceResult<IReadOnlyList<DistributionCreatedDto>> result =
            await BuildSut(RepoWithBatches("STK-1", Array.Empty<DeliveryItemBreakdownRow>()),
                    ItemsRepo("STK-1"), DistributionsRepoThatSaves())
                .AllocateAsync(MakeAdmin(), "STK-1", dto);

        Assert.Equal(ServiceErrorCode.BadRequest, result.Code);
    }

    [Fact]
    public async Task AllocateAsync_SplitWithZeroQty_ReturnsBadRequest()
    {
        CreateItemDistributionDto dto = new() { Splits = new List<DistributionSplitDto> { Split(0m) } };

        ServiceResult<IReadOnlyList<DistributionCreatedDto>> result =
            await BuildSut(RepoWithBatches("STK-1", Array.Empty<DeliveryItemBreakdownRow>()),
                    ItemsRepo("STK-1"), DistributionsRepoThatSaves())
                .AllocateAsync(MakeAdmin(), "STK-1", dto);

        Assert.Equal(ServiceErrorCode.BadRequest, result.Code);
    }

    [Fact]
    public async Task AllocateAsync_SplitWithBlankIssuedBy_ReturnsBadRequest()
    {
        CreateItemDistributionDto dto = new()
        {
            Splits = new List<DistributionSplitDto> { Split(5m, issuedBy: "  ") },
        };

        ServiceResult<IReadOnlyList<DistributionCreatedDto>> result =
            await BuildSut(RepoWithBatches("STK-1", Array.Empty<DeliveryItemBreakdownRow>()),
                    ItemsRepo("STK-1"), DistributionsRepoThatSaves())
                .AllocateAsync(MakeAdmin(), "STK-1", dto);

        Assert.Equal(ServiceErrorCode.BadRequest, result.Code);
    }

    [Fact]
    public async Task AllocateAsync_UnknownDivision_ReturnsBadRequest()
    {
        CreateItemDistributionDto dto = new()
        {
            Splits = new List<DistributionSplitDto> { Split(5m, division: "Nonexistent Division") },
        };

        ServiceResult<IReadOnlyList<DistributionCreatedDto>> result =
            await BuildSut(RepoWithBatches("STK-1", Array.Empty<DeliveryItemBreakdownRow>()),
                    ItemsRepo("STK-1"), DistributionsRepoThatSaves())
                .AllocateAsync(MakeAdmin(), "STK-1", dto);

        Assert.Equal(ServiceErrorCode.BadRequest, result.Code);
    }

    // ── Division scope ────────────────────────────────────────────────────────

    [Fact]
    public async Task AllocateAsync_StaffWithNoDivision_ReturnsNotFound()
    {
        User staff = MakeStaff(null);
        CreateItemDistributionDto dto = new() { Splits = new List<DistributionSplitDto> { Split(5m) } };

        ServiceResult<IReadOnlyList<DistributionCreatedDto>> result =
            await BuildSut(RepoWithBatches("STK-1", Array.Empty<DeliveryItemBreakdownRow>()),
                    ItemsRepo("STK-1"), DistributionsRepoThatSaves())
                .AllocateAsync(staff, "STK-1", dto);

        Assert.Equal(ServiceErrorCode.NotFound, result.Code);
    }

    [Fact]
    public async Task AllocateAsync_StaffScope_QueriesOnlyOwnDivisionBatches()
    {
        User staff = MakeStaff(PlanningDiv);
        Guid batchId = Guid.NewGuid();
        var batches = new List<DeliveryItemBreakdownRow>
        {
            Batch(batchId, DateOnly.FromDateTime(DateTime.UtcNow), 10m),
        };

        CreateItemDistributionDto dto = new()
        {
            Splits = new List<DistributionSplitDto> { Split(5m) },
        };

        Mock<IDeliveryRepository> repo = RepoWithBatches("STK-1", batches, expectedScopeDivision: PlanningDiv);

        ServiceResult<IReadOnlyList<DistributionCreatedDto>> result =
            await BuildSut(repo, ItemsRepo("STK-1"), DistributionsRepoThatSaves())
                .AllocateAsync(staff, "STK-1", dto);

        Assert.True(result.IsSuccess);
        repo.Verify(r => r.GetDeliveryItemBreakdownsByStockNoAsync(
            "STK-1", PlanningDiv, It.IsAny<CancellationToken>()), Times.Once);
    }

    // ── No activity / over-allocation ─────────────────────────────────────────

    [Fact]
    public async Task AllocateAsync_NoDeliveryActivity_ReturnsNotFound()
    {
        CreateItemDistributionDto dto = new() { Splits = new List<DistributionSplitDto> { Split(5m) } };

        ServiceResult<IReadOnlyList<DistributionCreatedDto>> result =
            await BuildSut(RepoWithBatches("STK-1", Array.Empty<DeliveryItemBreakdownRow>()),
                    ItemsRepo("STK-1"), DistributionsRepoThatSaves())
                .AllocateAsync(MakeAdmin(), "STK-1", dto);

        Assert.Equal(ServiceErrorCode.NotFound, result.Code);
    }

    [Fact]
    public async Task AllocateAsync_RequestExceedsAvailable_ReturnsBadRequest()
    {
        var batches = new List<DeliveryItemBreakdownRow>
        {
            Batch(Guid.NewGuid(), DateOnly.FromDateTime(DateTime.UtcNow), delivered: 5m),
        };
        CreateItemDistributionDto dto = new() { Splits = new List<DistributionSplitDto> { Split(10m) } };

        ServiceResult<IReadOnlyList<DistributionCreatedDto>> result =
            await BuildSut(RepoWithBatches("STK-1", batches), ItemsRepo("STK-1"), DistributionsRepoThatSaves())
                .AllocateAsync(MakeAdmin(), "STK-1", dto);

        Assert.Equal(ServiceErrorCode.BadRequest, result.Code);
    }

    // ── FIFO allocation ───────────────────────────────────────────────────────

    [Fact]
    public async Task AllocateAsync_SingleSplitWithinOneBatch_CreatesOneDistribution()
    {
        Guid batchId = Guid.NewGuid();
        var batches = new List<DeliveryItemBreakdownRow>
        {
            Batch(batchId, DateOnly.FromDateTime(DateTime.UtcNow), delivered: 20m),
        };
        CreateItemDistributionDto dto = new() { Splits = new List<DistributionSplitDto> { Split(8m) } };

        Mock<IRepository<Distribution>> distRepo = DistributionsRepoThatSaves();

        ServiceResult<IReadOnlyList<DistributionCreatedDto>> result =
            await BuildSut(RepoWithBatches("STK-1", batches), ItemsRepo("STK-1"), distRepo)
                .AllocateAsync(MakeAdmin(), "STK-1", dto);

        Assert.True(result.IsSuccess);
        Assert.Single(result.Value!);
        Assert.Equal(8m, result.Value![0].QtyIssued);
        Assert.Equal(batchId, result.Value![0].DeliveryItemId);
        distRepo.Verify(r => r.AddAsync(It.IsAny<Distribution>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task AllocateAsync_SplitLargerThanOldestBatch_SpansOldestFirstThenNextBatch()
    {
        // Oldest batch has 5 available; newer batch has 20. FIFO must draw 5 from the
        // oldest first, then 5 more from the newer one — this is the exact behavior the
        // old frontend implementation got backwards (it consumed newest-first because the
        // API returns batches newest-first for display purposes).
        DateOnly older = new(2026, 1, 1);
        DateOnly newer = new(2026, 6, 1);
        Guid oldBatchId = Guid.NewGuid();
        Guid newBatchId = Guid.NewGuid();

        var batches = new List<DeliveryItemBreakdownRow>
        {
            // Deliberately returned newest-first, mirroring the real repository's ordering —
            // the service must not rely on input order for FIFO correctness.
            Batch(newBatchId, newer, delivered: 20m),
            Batch(oldBatchId, older, delivered: 5m),
        };

        CreateItemDistributionDto dto = new() { Splits = new List<DistributionSplitDto> { Split(10m) } };

        ServiceResult<IReadOnlyList<DistributionCreatedDto>> result =
            await BuildSut(RepoWithBatches("STK-1", batches), ItemsRepo("STK-1"), DistributionsRepoThatSaves())
                .AllocateAsync(MakeAdmin(), "STK-1", dto);

        Assert.True(result.IsSuccess);
        Assert.Equal(2, result.Value!.Count);

        DistributionCreatedDto fromOldBatch = result.Value!.Single(d => d.DeliveryItemId == oldBatchId);
        DistributionCreatedDto fromNewBatch = result.Value!.Single(d => d.DeliveryItemId == newBatchId);

        Assert.Equal(5m, fromOldBatch.QtyIssued);
        Assert.Equal(5m, fromNewBatch.QtyIssued);
    }

    [Fact]
    public async Task AllocateAsync_MultipleSplitsAcrossDivisions_EachRecordedSeparately()
    {
        Guid batchId = Guid.NewGuid();
        var batches = new List<DeliveryItemBreakdownRow>
        {
            Batch(batchId, DateOnly.FromDateTime(DateTime.UtcNow), delivered: 30m),
        };
        CreateItemDistributionDto dto = new()
        {
            Splits = new List<DistributionSplitDto>
            {
                Split(10m, "Administrative Division"),
                Split(15m, "Planning Division"),
            },
        };

        ServiceResult<IReadOnlyList<DistributionCreatedDto>> result =
            await BuildSut(RepoWithBatches("STK-1", batches), ItemsRepo("STK-1"), DistributionsRepoThatSaves())
                .AllocateAsync(MakeAdmin(), "STK-1", dto);

        Assert.True(result.IsSuccess);
        Assert.Equal(2, result.Value!.Count);
        Assert.Contains(result.Value!, d => d.Division == "Administrative Division" && d.QtyIssued == 10m);
        Assert.Contains(result.Value!, d => d.Division == "Planning Division" && d.QtyIssued == 15m);
        // Distinct IssueRefs — same shared date suffix, different running sequence.
        Assert.Equal(2, result.Value!.Select(d => d.IssueRef).Distinct().Count());
    }

    [Fact]
    public async Task AllocateAsync_AlreadyPartlyDistributedBatch_OnlyDrawsRemaining()
    {
        Guid batchId = Guid.NewGuid();
        var batches = new List<DeliveryItemBreakdownRow>
        {
            Batch(batchId, DateOnly.FromDateTime(DateTime.UtcNow), delivered: 10m, alreadyDistributed: 6m),
        };
        CreateItemDistributionDto dto = new() { Splits = new List<DistributionSplitDto> { Split(4m) } };

        ServiceResult<IReadOnlyList<DistributionCreatedDto>> result =
            await BuildSut(RepoWithBatches("STK-1", batches), ItemsRepo("STK-1"), DistributionsRepoThatSaves())
                .AllocateAsync(MakeAdmin(), "STK-1", dto);

        Assert.True(result.IsSuccess);
        Assert.Single(result.Value!);
        Assert.Equal(4m, result.Value![0].QtyIssued);
    }

    // ── Audit logging (RAL-200) ───────────────────────────────────────────────

    [Fact]
    public async Task AllocateAsync_SingleSplitWithinOneBatch_CallsAuditLogOnce()
    {
        Guid batchId = Guid.NewGuid();
        var batches = new List<DeliveryItemBreakdownRow>
        {
            Batch(batchId, DateOnly.FromDateTime(DateTime.UtcNow), delivered: 20m),
        };
        CreateItemDistributionDto dto = new() { Splits = new List<DistributionSplitDto> { Split(8m) } };
        Mock<IAuditService> audit = new();

        ServiceResult<IReadOnlyList<DistributionCreatedDto>> result =
            await BuildSut(RepoWithBatches("STK-1", batches), ItemsRepo("STK-1"), DistributionsRepoThatSaves(),
                    auditService: audit)
                .AllocateAsync(MakeAdmin(), "STK-1", dto);

        audit.Verify(a => a.LogAsync(
            "Distributions", result.Value![0].Id, AuditAction.Create,
            null, It.IsAny<object?>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task AllocateAsync_SplitSpanningTwoBatches_CallsAuditLogOncePerCreatedRow()
    {
        DateOnly older = new(2026, 1, 1);
        DateOnly newer = new(2026, 6, 1);
        Guid oldBatchId = Guid.NewGuid();
        Guid newBatchId = Guid.NewGuid();

        var batches = new List<DeliveryItemBreakdownRow>
        {
            Batch(newBatchId, newer, delivered: 20m),
            Batch(oldBatchId, older, delivered: 5m),
        };
        CreateItemDistributionDto dto = new() { Splits = new List<DistributionSplitDto> { Split(10m) } };
        Mock<IAuditService> audit = new();

        await BuildSut(RepoWithBatches("STK-1", batches), ItemsRepo("STK-1"), DistributionsRepoThatSaves(),
                auditService: audit)
            .AllocateAsync(MakeAdmin(), "STK-1", dto);

        // Two Distribution rows created (one per batch drawn from) — two entries, not one.
        audit.Verify(a => a.LogAsync(
            "Distributions", It.IsAny<Guid>(), AuditAction.Create,
            null, It.IsAny<object?>(), It.IsAny<CancellationToken>()), Times.Exactly(2));
    }

    // ── Warehouse-count pool sourcing (RAL-223) ───────────────────────────────
    //
    // Reproduces the prod bug: a StockNo with a warehouse stock count but zero deliveries
    // showed On Hand on Stock Overview yet was undistributable ("No delivery batches found").
    // These tests cover the fix — Distribution now folds StockBalance's remaining
    // (net-of-already-distributed) variance in as an extra FIFO-poolable source, gated to the
    // Admin/SuperAdmin unscoped view (StockBalance carries no division).

    private static Guid _stockBalanceId = Guid.NewGuid();

    private static WarehouseCountPoolRow Pool(decimal grossVariance, decimal totalDistributed = 0m, DateOnly? effectiveDate = null)
        => new(_stockBalanceId, effectiveDate ?? DateOnly.FromDateTime(DateTime.UtcNow), grossVariance, totalDistributed);

    [Fact]
    public async Task GetItemSummaryAsync_AdminWithCountOnlyStock_IncludesWarehouseCountSource()
    {
        Mock<IStockBalanceRepository> stockBalances = WarehouseCountRepo("STK-1", Pool(5m));

        ServiceResult<ItemDistributionSummaryDto> result =
            await BuildSut(RepoWithBatches("STK-1", Array.Empty<DeliveryItemBreakdownRow>()),
                    ItemsRepo("STK-1"), DistributionsRepoThatSaves(), stockBalanceRepo: stockBalances)
                .GetItemSummaryAsync(MakeAdmin(), "STK-1");

        Assert.True(result.IsSuccess);
        Assert.Equal(5m, result.Value!.OnHand);
        Assert.Equal(5m, result.Value!.TotalDelivered);
        DeliveryItemBreakdownDto source = Assert.Single(result.Value!.DeliveryItems);
        Assert.Equal("Warehouse Count", source.Source);
        Assert.Null(source.DeliveryItemId);
        Assert.Equal(_stockBalanceId, source.StockBalanceId);
        Assert.Equal(5m, source.QtyAvailable);
    }

    [Fact]
    public async Task GetItemSummaryAsync_StaffScope_NeverIncludesWarehouseCountSource()
    {
        // StockBalance is PPDO-wide, no division scope — Staff must never see it, even if a
        // pool exists, same precedent as InventoryService's on-hand formula (RAL-193).
        Mock<IStockBalanceRepository> stockBalances = WarehouseCountRepo("STK-1", Pool(5m));
        User staff = MakeStaff(PlanningDiv);

        ServiceResult<ItemDistributionSummaryDto> result =
            await BuildSut(RepoWithBatches("STK-1", Array.Empty<DeliveryItemBreakdownRow>(), expectedScopeDivision: PlanningDiv),
                    ItemsRepo("STK-1"), DistributionsRepoThatSaves(), stockBalanceRepo: stockBalances)
                .GetItemSummaryAsync(staff, "STK-1");

        // Catalog entry exists (mocked master) so this still resolves Ok — just with no
        // warehouse-count source folded in, and the pool never queried for a Staff scope.
        Assert.True(result.IsSuccess);
        Assert.Empty(result.Value!.DeliveryItems);
        Assert.Equal(0m, result.Value!.OnHand);
        stockBalances.Verify(r => r.GetPoolByStockNoAsync(
            It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task GetItemSummaryAsync_AdminWithBothSources_SumsDeliveryAndWarehouseCount()
    {
        var batches = new List<DeliveryItemBreakdownRow>
        {
            Batch(Guid.NewGuid(), DateOnly.FromDateTime(DateTime.UtcNow), delivered: 10m),
        };
        Mock<IStockBalanceRepository> stockBalances = WarehouseCountRepo("STK-1", Pool(5m));

        ServiceResult<ItemDistributionSummaryDto> result =
            await BuildSut(RepoWithBatches("STK-1", batches), ItemsRepo("STK-1"), DistributionsRepoThatSaves(),
                    stockBalanceRepo: stockBalances)
                .GetItemSummaryAsync(MakeAdmin(), "STK-1");

        Assert.True(result.IsSuccess);
        Assert.Equal(15m, result.Value!.TotalDelivered);
        Assert.Equal(15m, result.Value!.OnHand);
        Assert.Equal(2, result.Value!.DeliveryItems.Count);
    }

    [Fact]
    public async Task GetItemSummaryAsync_DeliveryBatchDistribution_ResolvesDivisionNameNotId()
    {
        // RAL-236 regression: ExistingDistributionDto.Division must be the division's
        // display name, not the raw DivisionId stringified (e.g. "1" instead of
        // "Administrative Division").
        var batches = new List<DeliveryItemBreakdownRow>
        {
            Batch(Guid.NewGuid(), DateOnly.FromDateTime(DateTime.UtcNow), delivered: 10m, alreadyDistributed: 4m),
        };

        ServiceResult<ItemDistributionSummaryDto> result =
            await BuildSut(RepoWithBatches("STK-1", batches), ItemsRepo("STK-1"), DistributionsRepoThatSaves())
                .GetItemSummaryAsync(MakeAdmin(), "STK-1");

        Assert.True(result.IsSuccess);
        DeliveryItemBreakdownDto source = Assert.Single(result.Value!.DeliveryItems);
        ExistingDistributionDto dist = Assert.Single(source.Distributions);
        Assert.Equal("Administrative Division", dist.Division);
    }

    [Fact]
    public async Task GetItemSummaryAsync_WarehouseCountDistribution_ResolvesDivisionNameNotId()
    {
        // Same RAL-236 regression, but for the warehouse-count pool's distributions
        // (second, separate ExistingDistributionDto construction site in the same method).
        var poolDistributions = new List<DistributionBreakdownRow>
        {
            new(Guid.NewGuid(), "ISS-20260101-AAAAA-1", PlanningDiv, 5m,
                DateOnly.FromDateTime(DateTime.UtcNow), "Ralph", null),
        };
        Mock<IStockBalanceRepository> stockBalances =
            WarehouseCountRepo("STK-1", Pool(5m), poolDistributions);

        ServiceResult<ItemDistributionSummaryDto> result =
            await BuildSut(RepoWithBatches("STK-1", Array.Empty<DeliveryItemBreakdownRow>()),
                    ItemsRepo("STK-1"), DistributionsRepoThatSaves(), stockBalanceRepo: stockBalances)
                .GetItemSummaryAsync(MakeAdmin(), "STK-1");

        Assert.True(result.IsSuccess);
        DeliveryItemBreakdownDto source = Assert.Single(result.Value!.DeliveryItems);
        ExistingDistributionDto dist = Assert.Single(source.Distributions);
        Assert.Equal("Planning Division", dist.Division);
    }

    [Fact]
    public async Task AllocateAsync_CountOnlyStockNoDeliveries_AllocatesFromWarehouseCount()
    {
        // The exact prod scenario: a PR item with a warehouse count recorded but never
        // delivered. Previously this returned NotFound ("No delivery activity found").
        Mock<IStockBalanceRepository> stockBalances = WarehouseCountRepo("STK-1", Pool(5m));
        CreateItemDistributionDto dto = new() { Splits = new List<DistributionSplitDto> { Split(5m) } };
        Mock<IRepository<Distribution>> distRepo = DistributionsRepoThatSaves();

        ServiceResult<IReadOnlyList<DistributionCreatedDto>> result =
            await BuildSut(RepoWithBatches("STK-1", Array.Empty<DeliveryItemBreakdownRow>()),
                    ItemsRepo("STK-1"), distRepo, stockBalanceRepo: stockBalances)
                .AllocateAsync(MakeAdmin(), "STK-1", dto);

        Assert.True(result.IsSuccess);
        Assert.Single(result.Value!);
        Assert.Equal(5m, result.Value![0].QtyIssued);
        Assert.Null(result.Value![0].DeliveryItemId);
        Assert.Equal(_stockBalanceId, result.Value![0].StockBalanceId);
        Assert.Equal("Warehouse Count", result.Value![0].Source);
        distRepo.Verify(r => r.AddAsync(
            It.Is<Distribution>(d => d.DeliveryItemId == null && d.StockBalanceId == _stockBalanceId),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task AllocateAsync_StaffScope_CannotDrawFromWarehouseCountEvenIfPoolExists()
    {
        Mock<IStockBalanceRepository> stockBalances = WarehouseCountRepo("STK-1", Pool(5m));
        User staff = MakeStaff(PlanningDiv);
        CreateItemDistributionDto dto = new() { Splits = new List<DistributionSplitDto> { Split(5m) } };

        ServiceResult<IReadOnlyList<DistributionCreatedDto>> result =
            await BuildSut(RepoWithBatches("STK-1", Array.Empty<DeliveryItemBreakdownRow>(), expectedScopeDivision: PlanningDiv),
                    ItemsRepo("STK-1"), DistributionsRepoThatSaves(), stockBalanceRepo: stockBalances)
                .AllocateAsync(staff, "STK-1", dto);

        Assert.Equal(ServiceErrorCode.NotFound, result.Code);
        stockBalances.Verify(r => r.GetPoolByStockNoAsync(
            It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task AllocateAsync_DeliveryBatchOlderThanWarehouseCount_DrawsBatchFirst()
    {
        // FIFO must span both source types by date, not treat the warehouse-count pool as
        // always-last or always-first.
        DateOnly older = new(2026, 1, 1);
        DateOnly newer = new(2026, 6, 1);
        Guid batchId = Guid.NewGuid();

        var batches = new List<DeliveryItemBreakdownRow>
        {
            Batch(batchId, older, delivered: 3m),
        };
        Mock<IStockBalanceRepository> stockBalances = WarehouseCountRepo("STK-1", Pool(10m, effectiveDate: newer));
        CreateItemDistributionDto dto = new() { Splits = new List<DistributionSplitDto> { Split(5m) } };

        ServiceResult<IReadOnlyList<DistributionCreatedDto>> result =
            await BuildSut(RepoWithBatches("STK-1", batches), ItemsRepo("STK-1"), DistributionsRepoThatSaves(),
                    stockBalanceRepo: stockBalances)
                .AllocateAsync(MakeAdmin(), "STK-1", dto);

        Assert.True(result.IsSuccess);
        Assert.Equal(2, result.Value!.Count);

        DistributionCreatedDto fromBatch = result.Value!.Single(d => d.DeliveryItemId == batchId);
        DistributionCreatedDto fromPool  = result.Value!.Single(d => d.StockBalanceId == _stockBalanceId);

        Assert.Equal(3m, fromBatch.QtyIssued);  // older delivery batch fully drawn first
        Assert.Equal(2m, fromPool.QtyIssued);   // remainder from the newer warehouse count
    }

    [Fact]
    public async Task AllocateAsync_WarehouseCountFullyDistributedAlready_ExcludedFromPool()
    {
        // Remaining = GrossVariance - TotalDistributed = 0 — must not be offered as available
        // stock (mirrors the existing "batch fully drawn" exclusion for delivery batches).
        Mock<IStockBalanceRepository> stockBalances = WarehouseCountRepo("STK-1", Pool(5m, totalDistributed: 5m));
        CreateItemDistributionDto dto = new() { Splits = new List<DistributionSplitDto> { Split(1m) } };

        ServiceResult<IReadOnlyList<DistributionCreatedDto>> result =
            await BuildSut(RepoWithBatches("STK-1", Array.Empty<DeliveryItemBreakdownRow>()),
                    ItemsRepo("STK-1"), DistributionsRepoThatSaves(), stockBalanceRepo: stockBalances)
                .AllocateAsync(MakeAdmin(), "STK-1", dto);

        Assert.Equal(ServiceErrorCode.NotFound, result.Code);
    }

    [Fact]
    public async Task GetItemSummaryAsync_UnknownDivisionId_RendersPlaceholderNotRawId()
    {
        // RAL-239: divisions.id is an FK target, so this is unreachable in practice — but if
        // referential integrity ever breaks, show a placeholder rather than silently
        // reproducing the RAL-236 bug by printing the bare id as if it were a name.
        var poolDistributions = new List<DistributionBreakdownRow>
        {
            new(Guid.NewGuid(), "ISS-20260101-AAAAA-1", 999, 5m,
                DateOnly.FromDateTime(DateTime.UtcNow), "Ralph", null),
        };
        Mock<IStockBalanceRepository> stockBalances =
            WarehouseCountRepo("STK-1", Pool(5m), poolDistributions);

        ServiceResult<ItemDistributionSummaryDto> result =
            await BuildSut(RepoWithBatches("STK-1", Array.Empty<DeliveryItemBreakdownRow>()),
                    ItemsRepo("STK-1"), DistributionsRepoThatSaves(), stockBalanceRepo: stockBalances)
                .GetItemSummaryAsync(MakeAdmin(), "STK-1");

        Assert.True(result.IsSuccess);
        DeliveryItemBreakdownDto source = Assert.Single(result.Value!.DeliveryItems);
        ExistingDistributionDto dist = Assert.Single(source.Distributions);
        Assert.Equal("—", dist.Division);
        Assert.DoesNotContain("999", dist.Division);
    }
}
