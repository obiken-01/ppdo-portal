using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using PPDO.Application.Common;
using PPDO.Application.DTOs.PurchaseRequest;
using PPDO.Application.Services;
using PPDO.Domain.Entities;
using PPDO.Domain.Enums;
using PPDO.Domain.Interfaces;

namespace PPDO.Tests.Application;

/// <summary>
/// Unit tests for <see cref="PRReportService"/>.
/// Covers: permission gate, division-scope enforcement, PR not found,
/// Section 3 distribution row mapping, correct ordering, and Excel export delegation.
/// </summary>
public sealed class PRReportServiceTests
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

    private static User MakeStaffNoInventory() => new()
    {
        Id = Guid.NewGuid(), FullName = "No Inv", Email = "noinv@ppdo.gov.ph",
        PasswordHash = "hash", Role = UserRole.Staff, Division = Division.Planning, IsActive = true,
        Group = new PermissionGroup { Id = Guid.NewGuid(), Name = "Planning Staff", CanAccessInventory = false },
    };

    private static PRItem MakePRItem(Guid prId, int itemNo = 1, decimal qty = 10m) => new()
    {
        Id = Guid.NewGuid(), PRId = prId, ItemNo = itemNo,
        Description = "Bond Paper", Unit = "ream",
        Quantity = qty, UnitCost = 220m, TotalCost = qty * 220m,
    };

    private static PurchaseRequest MakePR(
        Division division = Division.Admin,
        IEnumerable<PRItem>? items = null) => new()
    {
        Id = Guid.NewGuid(), PRNo = "101-1041-GF-2026-06-02-001",
        PRDate = DateOnly.FromDateTime(DateTime.UtcNow),
        DateCreated = DateTime.UtcNow, Department = "PPDO",
        Division = division, Fund = "General Fund", RequestedBy = "Ralph",
        Position = "Staff", Status = PRStatus.Open,
        CreatedById = Guid.NewGuid(), CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow,
        Items = items?.ToList() ?? new List<PRItem>(),
        Deliveries = new List<Delivery>(),
    };

    private static Delivery MakeDelivery(
        Guid prId,
        PRItem prItem,
        decimal qtyDelivered = 5m,
        Division distDivision = Division.Admin) => new()
    {
        Id = Guid.NewGuid(),
        DeliveryRef = "DEL-20260602-ABCDE",
        PRId = prId,
        DeliveryDate = DateOnly.FromDateTime(DateTime.UtcNow),
        ReceivedBy = "Ralph",
        CreatedAt = DateTime.UtcNow,
        Items = new List<DeliveryItem>
        {
            new()
            {
                Id = Guid.NewGuid(),
                PRItemId = prItem.Id,
                QtyDelivered = qtyDelivered,
                PRItem = prItem,
                Distributions = new List<Distribution>
                {
                    new()
                    {
                        Id = Guid.NewGuid(),
                        IssueRef = "ISS-20260602-ABCDE-1",
                        Division = distDivision,
                        QtyIssued = qtyDelivered,
                        DateIssued = DateOnly.FromDateTime(DateTime.UtcNow),
                        IssuedBy = "Ralph",
                    },
                },
            },
        },
    };

    private static PRReportService BuildSut(
        Mock<IPurchaseRequestRepository> prRepo,
        Mock<IDeliveryRepository> deliveryRepo,
        Mock<IExcelService>? excel = null)
        => new(
            prRepo.Object,
            deliveryRepo.Object,
            new PermissionService(),
            (excel ?? new Mock<IExcelService>()).Object,
            NullLogger<PRReportService>.Instance);

    // ── Permission gate ───────────────────────────────────────────────────────

    [Fact]
    public async Task GetReportAsync_WithoutInventoryPermission_ReturnsForbidden()
    {
        Mock<IPurchaseRequestRepository> prRepo = new();
        Mock<IDeliveryRepository> deliveryRepo = new();

        ServiceResult<PRReportDto> result =
            await BuildSut(prRepo, deliveryRepo)
                .GetReportAsync(MakeStaffNoInventory(), Guid.NewGuid());

        Assert.Equal(ServiceErrorCode.Forbidden, result.Code);
    }

    // ── PR not found ──────────────────────────────────────────────────────────

    [Fact]
    public async Task GetReportAsync_PRNotFound_ReturnsNotFound()
    {
        Mock<IPurchaseRequestRepository> prRepo = new();
        prRepo.Setup(r => r.GetWithItemsAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((PurchaseRequest?)null);

        ServiceResult<PRReportDto> result =
            await BuildSut(prRepo, new Mock<IDeliveryRepository>())
                .GetReportAsync(MakeAdmin(), Guid.NewGuid());

        Assert.Equal(ServiceErrorCode.NotFound, result.Code);
    }

    // ── Division scope ────────────────────────────────────────────────────────

    [Fact]
    public async Task GetReportAsync_StaffViewingOtherDivisionPR_ReturnsForbidden()
    {
        PurchaseRequest pr = MakePR(Division.Admin);

        Mock<IPurchaseRequestRepository> prRepo = new();
        prRepo.Setup(r => r.GetWithItemsAsync(pr.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(pr);

        ServiceResult<PRReportDto> result =
            await BuildSut(prRepo, new Mock<IDeliveryRepository>())
                .GetReportAsync(MakeStaff(Division.Planning), pr.Id);

        Assert.Equal(ServiceErrorCode.Forbidden, result.Code);
    }

    [Fact]
    public async Task GetReportAsync_AdminViewingAnyDivision_ReturnsOk()
    {
        PurchaseRequest pr = MakePR(Division.Planning);
        pr.Items.Add(MakePRItem(pr.Id));

        Mock<IPurchaseRequestRepository> prRepo = new();
        prRepo.Setup(r => r.GetWithItemsAsync(pr.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(pr);

        Mock<IDeliveryRepository> deliveryRepo = new();
        deliveryRepo.Setup(r => r.GetDeliveriesForPRReportAsync(pr.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Delivery>());

        ServiceResult<PRReportDto> result =
            await BuildSut(prRepo, deliveryRepo)
                .GetReportAsync(MakeAdmin(), pr.Id);

        Assert.True(result.IsSuccess);
    }

    // ── Section 1 + 2 mapping ─────────────────────────────────────────────────

    [Fact]
    public async Task GetReportAsync_ReturnsCorrectPRHeaderAndItems()
    {
        PurchaseRequest pr = MakePR(Division.Admin);
        PRItem item = MakePRItem(pr.Id, itemNo: 1, qty: 10m);
        pr.Items.Add(item);

        Mock<IPurchaseRequestRepository> prRepo = new();
        prRepo.Setup(r => r.GetWithItemsAsync(pr.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(pr);

        Mock<IDeliveryRepository> deliveryRepo = new();
        deliveryRepo.Setup(r => r.GetDeliveriesForPRReportAsync(pr.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Delivery>());

        ServiceResult<PRReportDto> result =
            await BuildSut(prRepo, deliveryRepo).GetReportAsync(MakeAdmin(), pr.Id);

        Assert.True(result.IsSuccess);
        Assert.Equal(pr.PRNo, result.Value!.PR.PRNo);
        Assert.Equal(pr.Division.ToString(), result.Value.PR.Division);
        Assert.Single(result.Value.PR.Items);
        Assert.Equal(item.Description, result.Value.PR.Items[0].Description);
    }

    // ── Section 3 distribution mapping ───────────────────────────────────────

    [Fact]
    public async Task GetReportAsync_WithDelivery_ReturnsDistributionRows()
    {
        PurchaseRequest pr = MakePR(Division.Admin);
        PRItem item = MakePRItem(pr.Id, itemNo: 1, qty: 10m);
        pr.Items.Add(item);

        Delivery delivery = MakeDelivery(pr.Id, item, qtyDelivered: 5m, Division.Admin);

        Mock<IPurchaseRequestRepository> prRepo = new();
        prRepo.Setup(r => r.GetWithItemsAsync(pr.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(pr);

        Mock<IDeliveryRepository> deliveryRepo = new();
        deliveryRepo.Setup(r => r.GetDeliveriesForPRReportAsync(pr.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Delivery> { delivery });

        ServiceResult<PRReportDto> result =
            await BuildSut(prRepo, deliveryRepo).GetReportAsync(MakeAdmin(), pr.Id);

        Assert.True(result.IsSuccess);
        Assert.Single(result.Value!.Distributions);

        PRReportDistributionDto dist = result.Value.Distributions[0];
        Assert.Equal(1, dist.ItemNo);
        Assert.Equal("Bond Paper", dist.Description);
        Assert.Equal(5m, dist.QtyDelivered);
        Assert.Equal(Division.Admin, dist.Division);
        Assert.Equal(5m, dist.QtyIssued);
        Assert.Equal("DEL-20260602-ABCDE", dist.DeliveryRef);
        Assert.Equal("ISS-20260602-ABCDE-1", dist.IssueRef);
    }

    [Fact]
    public async Task GetReportAsync_NoDeliveries_ReturnsEmptyDistributions()
    {
        PurchaseRequest pr = MakePR(Division.Admin);
        pr.Items.Add(MakePRItem(pr.Id));

        Mock<IPurchaseRequestRepository> prRepo = new();
        prRepo.Setup(r => r.GetWithItemsAsync(pr.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(pr);

        Mock<IDeliveryRepository> deliveryRepo = new();
        deliveryRepo.Setup(r => r.GetDeliveriesForPRReportAsync(pr.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Delivery>());

        ServiceResult<PRReportDto> result =
            await BuildSut(prRepo, deliveryRepo).GetReportAsync(MakeAdmin(), pr.Id);

        Assert.True(result.IsSuccess);
        Assert.Empty(result.Value!.Distributions);
    }

    // ── Excel export delegation ───────────────────────────────────────────────

    [Fact]
    public async Task ExportReportAsync_CallsExcelServiceWithWiredPR()
    {
        PurchaseRequest pr = MakePR(Division.Admin);
        PRItem item = MakePRItem(pr.Id);
        pr.Items.Add(item);

        Delivery delivery = MakeDelivery(pr.Id, item);
        byte[] expectedBytes = new byte[] { 1, 2, 3 };

        Mock<IPurchaseRequestRepository> prRepo = new();
        prRepo.Setup(r => r.GetWithItemsAsync(pr.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(pr);

        Mock<IDeliveryRepository> deliveryRepo = new();
        deliveryRepo.Setup(r => r.GetDeliveriesForPRReportAsync(pr.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Delivery> { delivery });

        PurchaseRequest? capturedPR = null;
        Mock<IExcelService> excel = new();
        excel.Setup(e => e.ExportPRReport(It.IsAny<PurchaseRequest>()))
            .Callback<PurchaseRequest>(p => capturedPR = p)
            .Returns(expectedBytes);

        ServiceResult<byte[]> result =
            await BuildSut(prRepo, deliveryRepo, excel)
                .ExportReportAsync(MakeAdmin(), pr.Id);

        Assert.True(result.IsSuccess);
        Assert.Equal(expectedBytes, result.Value);

        // Verify deliveries were wired onto the PR before passing to ExcelService.
        Assert.NotNull(capturedPR);
        Assert.Single(capturedPR!.Deliveries);
        Assert.Equal(delivery.DeliveryRef, capturedPR.Deliveries.First().DeliveryRef);
    }

    [Fact]
    public async Task ExportReportAsync_PRNotFound_ReturnsForbiddenPropagated()
    {
        Mock<IPurchaseRequestRepository> prRepo = new();
        prRepo.Setup(r => r.GetWithItemsAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((PurchaseRequest?)null);

        ServiceResult<byte[]> result =
            await BuildSut(prRepo, new Mock<IDeliveryRepository>())
                .ExportReportAsync(MakeAdmin(), Guid.NewGuid());

        Assert.Equal(ServiceErrorCode.NotFound, result.Code);
    }
}
