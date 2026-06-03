using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using PPDO.Application.Common;
using PPDO.Application.DTOs.Delivery;
using PPDO.Application.Services;
using PPDO.Domain.Entities;
using PPDO.Domain.Enums;
using PPDO.Domain.Interfaces;

namespace PPDO.Tests.Application;

/// <summary>
/// Unit tests for <see cref="DeliveryService"/>.
/// Covers: permission gate, division-scope enforcement, PR not found / Completed guard,
/// PRItemId ownership, QtyDelivered validation, split-delivery distribution sum check,
/// and PR status transitions (Open → PartiallyDelivered → FullyDelivered).
/// </summary>
public sealed class DeliveryServiceTests
{
    // ── Fixtures ──────────────────────────────────────────────────────────────

    private static User MakeAdmin(Division division = Division.Admin) => new()
    {
        Id = Guid.NewGuid(), FullName = "Admin", Email = "admin@ppdo.gov.ph",
        PasswordHash = "hash", Role = UserRole.Admin, Division = division, IsActive = true,
    };

    private static User MakeStaff(Division division = Division.Planning) => new()
    {
        Id = Guid.NewGuid(), FullName = "Staff", Email = "staff@ppdo.gov.ph",
        PasswordHash = "hash", Role = UserRole.Staff, Division = division, IsActive = true,
        Group = new PermissionGroup { Id = Guid.NewGuid(), Name = "Admin Division Staff", CanAccessInventory = true },
    };

    private static User MakeStaffNoInventory() => new()
    {
        Id = Guid.NewGuid(), FullName = "No Inv", Email = "noinv@ppdo.gov.ph",
        PasswordHash = "hash", Role = UserRole.Staff, Division = Division.Planning, IsActive = true,
        Group = new PermissionGroup { Id = Guid.NewGuid(), Name = "Planning Staff", CanAccessInventory = false },
    };

    private static PRItem MakePRItem(Guid prId, decimal qty = 10m) => new()
    {
        Id = Guid.NewGuid(), PRId = prId, ItemNo = 1,
        Description = "Bond Paper", Unit = "ream",
        Quantity = qty, UnitCost = 220m, TotalCost = qty * 220m,
    };

    private static PurchaseRequest MakePR(
        Division division = Division.Admin,
        PRStatus status = PRStatus.Open,
        IEnumerable<PRItem>? items = null) => new()
    {
        Id = Guid.NewGuid(), PRNo = "101-1041-GF-2026-06-02-001",
        PRDate = DateOnly.FromDateTime(DateTime.UtcNow),
        DateCreated = DateTime.UtcNow, Department = "PPDO",
        Division = division, Fund = "GAD", RequestedBy = "Ralph",
        Position = "Staff", Status = status,
        CreatedById = Guid.NewGuid(), CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow,
        Items = items?.ToList() ?? new List<PRItem>(),
    };

    private static CreateDistributionDto ValidDist(
        decimal qty, Division division = Division.Admin) => new()
    {
        Division = division.ToString(), QtyIssued = qty,
        DateIssued = DateOnly.FromDateTime(DateTime.UtcNow),
        IssuedBy = "Ralph",
    };

    private static Mock<IDeliveryRepository> RepoDeliveryThatSaves()
    {
        Mock<IDeliveryRepository> repo = new();
        repo.Setup(r => r.AddAsync(It.IsAny<Delivery>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        repo.Setup(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);
        repo.Setup(r => r.DeliveryRefExistsAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
        repo.Setup(r => r.GetTotalDeliveredByPRAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<Guid, decimal>());
        return repo;
    }

    private static Mock<IPurchaseRequestRepository> RepoPRThatSaves(PurchaseRequest pr)
    {
        Mock<IPurchaseRequestRepository> repo = new();
        repo.Setup(r => r.GetWithItemsAsync(pr.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(pr);
        repo.Setup(r => r.GetByIdAsync(pr.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(pr);
        repo.Setup(r => r.UpdateAsync(It.IsAny<PurchaseRequest>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        repo.Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<PurchaseRequest> { pr });
        repo.Setup(r => r.GetByDivisionAsync(It.IsAny<Division>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<PurchaseRequest> { pr });
        return repo;
    }

    private static DeliveryService BuildSut(
        Mock<IDeliveryRepository> deliveryRepo,
        Mock<IPurchaseRequestRepository> prRepo)
        => new(
            deliveryRepo.Object,
            prRepo.Object,
            new PermissionService(),
            NullLogger<DeliveryService>.Instance);

    // ── Permission gate ───────────────────────────────────────────────────────

    [Fact]
    public async Task CreateAsync_WithoutInventoryPermission_ReturnsForbidden()
    {
        PurchaseRequest pr = MakePR();
        CreateDeliveryDto dto = new()
        {
            PRId = pr.Id, DeliveryDate = DateOnly.FromDateTime(DateTime.UtcNow),
            ReceivedBy = "Ralph", Items = new List<CreateDeliveryItemDto>(),
        };

        ServiceResult<DeliveryResponseDto> result =
            await BuildSut(RepoDeliveryThatSaves(), RepoPRThatSaves(pr))
                .CreateAsync(MakeStaffNoInventory(), dto);

        Assert.Equal(ServiceErrorCode.Forbidden, result.Code);
    }

    // ── Division scope ────────────────────────────────────────────────────────

    [Fact]
    public async Task CreateAsync_StaffDeliveringOtherDivisionPR_ReturnsForbidden()
    {
        PurchaseRequest pr = MakePR(Division.Admin); // PR belongs to Admin
        User staff = MakeStaff(Division.Planning);   // Staff is in Planning

        PRItem item = MakePRItem(pr.Id);
        pr.Items.Add(item);

        CreateDeliveryDto dto = new()
        {
            PRId = pr.Id, DeliveryDate = DateOnly.FromDateTime(DateTime.UtcNow),
            ReceivedBy = "Ralph",
            Items = new List<CreateDeliveryItemDto>
            {
                new() { PRItemId = item.Id, QtyDelivered = 5m,
                    Distributions = new List<CreateDistributionDto> { ValidDist(5m, Division.Planning) } },
            },
        };

        ServiceResult<DeliveryResponseDto> result =
            await BuildSut(RepoDeliveryThatSaves(), RepoPRThatSaves(pr))
                .CreateAsync(staff, dto);

        Assert.Equal(ServiceErrorCode.Forbidden, result.Code);
    }

    // ── PR validation ─────────────────────────────────────────────────────────

    [Fact]
    public async Task CreateAsync_PRNotFound_ReturnsNotFound()
    {
        Mock<IDeliveryRepository> deliveryRepo = RepoDeliveryThatSaves();
        Mock<IPurchaseRequestRepository> prRepo = new();
        prRepo.Setup(r => r.GetWithItemsAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((PurchaseRequest?)null);

        CreateDeliveryDto dto = new()
        {
            PRId = Guid.NewGuid(), DeliveryDate = DateOnly.FromDateTime(DateTime.UtcNow),
            ReceivedBy = "Ralph", Items = new List<CreateDeliveryItemDto>(),
        };

        ServiceResult<DeliveryResponseDto> result =
            await BuildSut(deliveryRepo, prRepo).CreateAsync(MakeAdmin(), dto);

        Assert.Equal(ServiceErrorCode.NotFound, result.Code);
    }

    [Fact]
    public async Task CreateAsync_PRCompleted_ReturnsBadRequest()
    {
        PurchaseRequest pr = MakePR(status: PRStatus.Completed);

        CreateDeliveryDto dto = new()
        {
            PRId = pr.Id, DeliveryDate = DateOnly.FromDateTime(DateTime.UtcNow),
            ReceivedBy = "Ralph", Items = new List<CreateDeliveryItemDto>(),
        };

        ServiceResult<DeliveryResponseDto> result =
            await BuildSut(RepoDeliveryThatSaves(), RepoPRThatSaves(pr))
                .CreateAsync(MakeAdmin(), dto);

        Assert.Equal(ServiceErrorCode.BadRequest, result.Code);
    }

    // ── Item validation ───────────────────────────────────────────────────────

    [Fact]
    public async Task CreateAsync_EmptyItems_ReturnsBadRequest()
    {
        PurchaseRequest pr = MakePR();
        CreateDeliveryDto dto = new()
        {
            PRId = pr.Id, DeliveryDate = DateOnly.FromDateTime(DateTime.UtcNow),
            ReceivedBy = "Ralph", Items = new List<CreateDeliveryItemDto>(),
        };

        ServiceResult<DeliveryResponseDto> result =
            await BuildSut(RepoDeliveryThatSaves(), RepoPRThatSaves(pr))
                .CreateAsync(MakeAdmin(), dto);

        Assert.Equal(ServiceErrorCode.BadRequest, result.Code);
    }

    [Fact]
    public async Task CreateAsync_PRItemIdNotInPR_ReturnsBadRequest()
    {
        PurchaseRequest pr = MakePR();
        pr.Items.Add(MakePRItem(pr.Id));

        CreateDeliveryDto dto = new()
        {
            PRId = pr.Id, DeliveryDate = DateOnly.FromDateTime(DateTime.UtcNow),
            ReceivedBy = "Ralph",
            Items = new List<CreateDeliveryItemDto>
            {
                // Random PRItemId that does not belong to the PR.
                new() { PRItemId = Guid.NewGuid(), QtyDelivered = 5m,
                    Distributions = new List<CreateDistributionDto> { ValidDist(5m) } },
            },
        };

        ServiceResult<DeliveryResponseDto> result =
            await BuildSut(RepoDeliveryThatSaves(), RepoPRThatSaves(pr))
                .CreateAsync(MakeAdmin(), dto);

        Assert.Equal(ServiceErrorCode.BadRequest, result.Code);
    }

    [Fact]
    public async Task CreateAsync_QtyDeliveredZero_ReturnsBadRequest()
    {
        PurchaseRequest pr = MakePR();
        PRItem item = MakePRItem(pr.Id);
        pr.Items.Add(item);

        CreateDeliveryDto dto = new()
        {
            PRId = pr.Id, DeliveryDate = DateOnly.FromDateTime(DateTime.UtcNow),
            ReceivedBy = "Ralph",
            Items = new List<CreateDeliveryItemDto>
            {
                new() { PRItemId = item.Id, QtyDelivered = 0m,
                    Distributions = new List<CreateDistributionDto> { ValidDist(0m) } },
            },
        };

        ServiceResult<DeliveryResponseDto> result =
            await BuildSut(RepoDeliveryThatSaves(), RepoPRThatSaves(pr))
                .CreateAsync(MakeAdmin(), dto);

        Assert.Equal(ServiceErrorCode.BadRequest, result.Code);
    }

    // ── Split delivery — distribution sum validation ───────────────────────────

    [Fact]
    public async Task CreateAsync_DistributionSumMismatch_ReturnsBadRequest()
    {
        PurchaseRequest pr = MakePR();
        PRItem item = MakePRItem(pr.Id, qty: 10m);
        pr.Items.Add(item);

        // QtyDelivered = 10 but distributions only sum to 7 — mismatch.
        CreateDeliveryDto dto = new()
        {
            PRId = pr.Id, DeliveryDate = DateOnly.FromDateTime(DateTime.UtcNow),
            ReceivedBy = "Ralph",
            Items = new List<CreateDeliveryItemDto>
            {
                new() { PRItemId = item.Id, QtyDelivered = 10m,
                    Distributions = new List<CreateDistributionDto>
                    {
                        ValidDist(4m, Division.Admin),
                        ValidDist(3m, Division.Planning),
                    }},
            },
        };

        ServiceResult<DeliveryResponseDto> result =
            await BuildSut(RepoDeliveryThatSaves(), RepoPRThatSaves(pr))
                .CreateAsync(MakeAdmin(), dto);

        Assert.Equal(ServiceErrorCode.BadRequest, result.Code);
    }

    [Fact]
    public async Task CreateAsync_SplitDeliveryAcrossDivisions_Succeeds()
    {
        PurchaseRequest pr = MakePR(Division.Admin);
        PRItem item = MakePRItem(pr.Id, qty: 10m);
        pr.Items.Add(item);

        // 10 units split: 6 to Admin, 4 to Planning.
        CreateDeliveryDto dto = new()
        {
            PRId = pr.Id, DeliveryDate = DateOnly.FromDateTime(DateTime.UtcNow),
            ReceivedBy = "Ralph",
            Items = new List<CreateDeliveryItemDto>
            {
                new() { PRItemId = item.Id, QtyDelivered = 10m,
                    Distributions = new List<CreateDistributionDto>
                    {
                        ValidDist(6m, Division.Admin),
                        ValidDist(4m, Division.Planning),
                    }},
            },
        };

        ServiceResult<DeliveryResponseDto> result =
            await BuildSut(RepoDeliveryThatSaves(), RepoPRThatSaves(pr))
                .CreateAsync(MakeAdmin(), dto);

        Assert.True(result.IsSuccess);
        DeliveryItemDto deliveryItem = result.Value!.Items[0];
        Assert.Equal(2, deliveryItem.Distributions.Count);
        Assert.Equal(6m, deliveryItem.Distributions.First(d => d.Division == Division.Admin).QtyIssued);
        Assert.Equal(4m, deliveryItem.Distributions.First(d => d.Division == Division.Planning).QtyIssued);
    }

    // ── PR status transitions ─────────────────────────────────────────────────

    [Fact]
    public async Task CreateAsync_PartialDelivery_SetsPRStatusToPartiallyDelivered()
    {
        PurchaseRequest pr = MakePR(Division.Admin, PRStatus.Open);
        PRItem item = MakePRItem(pr.Id, qty: 10m);
        pr.Items.Add(item);

        Mock<IDeliveryRepository> deliveryRepo = RepoDeliveryThatSaves();
        // No prior deliveries — empty dict returned.
        deliveryRepo.Setup(r => r.GetTotalDeliveredByPRAsync(pr.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<Guid, decimal>());

        // Deliver only 5 of 10 → PartiallyDelivered.
        CreateDeliveryDto dto = new()
        {
            PRId = pr.Id, DeliveryDate = DateOnly.FromDateTime(DateTime.UtcNow),
            ReceivedBy = "Ralph",
            Items = new List<CreateDeliveryItemDto>
            {
                new() { PRItemId = item.Id, QtyDelivered = 5m,
                    Distributions = new List<CreateDistributionDto> { ValidDist(5m) } },
            },
        };

        await BuildSut(deliveryRepo, RepoPRThatSaves(pr)).CreateAsync(MakeAdmin(), dto);

        Assert.Equal(PRStatus.PartiallyDelivered, pr.Status);
    }

    [Fact]
    public async Task CreateAsync_FullDelivery_SetsPRStatusToFullyDelivered()
    {
        PurchaseRequest pr = MakePR(Division.Admin, PRStatus.Open);
        PRItem item = MakePRItem(pr.Id, qty: 10m);
        pr.Items.Add(item);

        Mock<IDeliveryRepository> deliveryRepo = RepoDeliveryThatSaves();
        deliveryRepo.Setup(r => r.GetTotalDeliveredByPRAsync(pr.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<Guid, decimal>());

        // Deliver exactly 10 of 10 → FullyDelivered.
        CreateDeliveryDto dto = new()
        {
            PRId = pr.Id, DeliveryDate = DateOnly.FromDateTime(DateTime.UtcNow),
            ReceivedBy = "Ralph",
            Items = new List<CreateDeliveryItemDto>
            {
                new() { PRItemId = item.Id, QtyDelivered = 10m,
                    Distributions = new List<CreateDistributionDto> { ValidDist(10m) } },
            },
        };

        await BuildSut(deliveryRepo, RepoPRThatSaves(pr)).CreateAsync(MakeAdmin(), dto);

        Assert.Equal(PRStatus.FullyDelivered, pr.Status);
    }

    [Fact]
    public async Task CreateAsync_SecondDeliveryCompletesItem_SetsPRStatusToFullyDelivered()
    {
        PurchaseRequest pr = MakePR(Division.Admin, PRStatus.PartiallyDelivered);
        PRItem item = MakePRItem(pr.Id, qty: 10m);
        pr.Items.Add(item);

        Mock<IDeliveryRepository> deliveryRepo = RepoDeliveryThatSaves();
        // 5 already delivered in a prior delivery.
        deliveryRepo.Setup(r => r.GetTotalDeliveredByPRAsync(pr.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<Guid, decimal> { { item.Id, 5m } });

        // Second delivery of the remaining 5 → total = 10 = qty → FullyDelivered.
        CreateDeliveryDto dto = new()
        {
            PRId = pr.Id, DeliveryDate = DateOnly.FromDateTime(DateTime.UtcNow),
            ReceivedBy = "Ralph",
            Items = new List<CreateDeliveryItemDto>
            {
                new() { PRItemId = item.Id, QtyDelivered = 5m,
                    Distributions = new List<CreateDistributionDto> { ValidDist(5m) } },
            },
        };

        await BuildSut(deliveryRepo, RepoPRThatSaves(pr)).CreateAsync(MakeAdmin(), dto);

        Assert.Equal(PRStatus.FullyDelivered, pr.Status);
    }

    // ── DeliveryRef and IssueRef format ───────────────────────────────────────

    [Fact]
    public async Task CreateAsync_ValidSubmission_DeliveryRefMatchesFormat()
    {
        PurchaseRequest pr = MakePR(Division.Admin);
        PRItem item = MakePRItem(pr.Id);
        pr.Items.Add(item);

        CreateDeliveryDto dto = new()
        {
            PRId = pr.Id, DeliveryDate = DateOnly.FromDateTime(DateTime.UtcNow),
            ReceivedBy = "Ralph",
            Items = new List<CreateDeliveryItemDto>
            {
                new() { PRItemId = item.Id, QtyDelivered = 5m,
                    Distributions = new List<CreateDistributionDto> { ValidDist(5m) } },
            },
        };

        ServiceResult<DeliveryResponseDto> result =
            await BuildSut(RepoDeliveryThatSaves(), RepoPRThatSaves(pr))
                .CreateAsync(MakeAdmin(), dto);

        Assert.True(result.IsSuccess);
        Assert.Matches(@"^DEL-\d{8}-[A-Z0-9]{5}$", result.Value!.DeliveryRef);
    }

    [Fact]
    public async Task CreateAsync_TwoDistributions_IssueRefsHaveSequentialSuffix()
    {
        PurchaseRequest pr = MakePR(Division.Admin);
        PRItem item = MakePRItem(pr.Id, qty: 10m);
        pr.Items.Add(item);

        CreateDeliveryDto dto = new()
        {
            PRId = pr.Id, DeliveryDate = DateOnly.FromDateTime(DateTime.UtcNow),
            ReceivedBy = "Ralph",
            Items = new List<CreateDeliveryItemDto>
            {
                new() { PRItemId = item.Id, QtyDelivered = 10m,
                    Distributions = new List<CreateDistributionDto>
                    {
                        ValidDist(6m, Division.Admin),
                        ValidDist(4m, Division.Planning),
                    }},
            },
        };

        ServiceResult<DeliveryResponseDto> result =
            await BuildSut(RepoDeliveryThatSaves(), RepoPRThatSaves(pr))
                .CreateAsync(MakeAdmin(), dto);

        Assert.True(result.IsSuccess);
        IReadOnlyList<DistributionDto> dists = result.Value!.Items[0].Distributions;

        // Both IssueRefs must match format ISS-YYYYMMDD-XXXXX-N.
        Assert.Matches(@"^ISS-\d{8}-[A-Z0-9]{5}-1$", dists[0].IssueRef);
        Assert.Matches(@"^ISS-\d{8}-[A-Z0-9]{5}-2$", dists[1].IssueRef);

        // Both share the same random suffix (chars 12–16).
        string suffix1 = dists[0].IssueRef.Split('-')[2];
        string suffix2 = dists[1].IssueRef.Split('-')[2];
        Assert.Equal(suffix1, suffix2);
    }
}
