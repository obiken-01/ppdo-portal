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

    private static DistributionService BuildSut(
        Mock<IDeliveryRepository> deliveryRepo,
        Mock<IItemMasterRepository> itemsRepo,
        Mock<IRepository<Distribution>> distributionsRepo,
        Mock<IRepository<Division>>? divisionRepo = null)
        => new(
            deliveryRepo.Object,
            itemsRepo.Object,
            new PermissionService(),
            distributionsRepo.Object,
            (divisionRepo ?? DivisionsRepo()).Object,
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
}
