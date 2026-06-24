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
/// Unit tests for <see cref="PurchaseRequestService"/>.
/// Covers the non-trivial business logic: PRNo generation, division-scope enforcement,
/// unknown StockNo auto-creation, Items Master unit-cost override, update status gate,
/// and Excel import division pre-check.
///
/// All repositories and IExcelService are mocked. PermissionService is used directly
/// to exercise real permission resolution without mocking.
/// </summary>
public sealed class PurchaseRequestServiceTests
{
    // ── Fixtures ──────────────────────────────────────────────────────────────

    // Division ids: 1 = "Administrative Division", 2 = "Planning Division" (see DivisionsRepo()).
    private const int AdminDiv = 1;
    private const int PlanningDiv = 2;

    private static Division DivEntity(int id, bool inventory) => new()
    {
        Id = id, OfficeId = 100,
        Name = id == AdminDiv ? "Administrative Division" : "Planning Division",
        CanAccessInventory = inventory,
    };

    private static User MakeAdmin(int? divisionId = null) => new()
    {
        Id = Guid.NewGuid(), FullName = "Admin User", Email = "admin@ppdo.gov.ph",
        PasswordHash = "hash", Role = UserRole.Admin, DivisionId = divisionId, IsActive = true,
    };

    private static User MakeStaff(int divisionId = PlanningDiv) => new()
    {
        Id = Guid.NewGuid(), FullName = "Staff User", Email = "staff@ppdo.gov.ph",
        PasswordHash = "hash", Role = UserRole.Staff, DivisionId = divisionId,
        Division = DivEntity(divisionId, inventory: true), IsActive = true,
    };

    private static User MakeStaffNoInventory(int divisionId = PlanningDiv) => new()
    {
        Id = Guid.NewGuid(), FullName = "Staff No Inv", Email = "staff2@ppdo.gov.ph",
        PasswordHash = "hash", Role = UserRole.Staff, DivisionId = divisionId,
        Division = DivEntity(divisionId, inventory: false), IsActive = true,
    };

    private static PurchaseRequest MakePR(string prNo, int divisionId = AdminDiv) => new()
    {
        Id = Guid.NewGuid(), PRNo = prNo, PRDate = DateOnly.FromDateTime(DateTime.UtcNow),
        DateCreated = DateTime.UtcNow, Department = "PPDO", DivisionId = divisionId,
        Fund = "General Fund", RequestedBy = "Test Staff", Position = "Staff",
        Status = PRStatus.Open, TotalAmount = 0m,
        CreatedById = Guid.NewGuid(), CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow,
        Items = new List<PRItem>(),
    };

    // Divisions repo for name → id resolution during create/update.
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

    private static ItemMaster MakeItemMaster(string stockNo, decimal unitCost = 220m) => new()
    {
        Id = Guid.NewGuid(), StockNo = stockNo, Description = "Bond Paper A4",
        Unit = "ream", UnitCost = unitCost, IsNewItem = false,
        CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow,
    };

    private static CreatePRDto ValidDto(string division = "Planning Division") => new()
    {
        PRDate = DateOnly.FromDateTime(DateTime.UtcNow),
        Division = division,
        Fund = "General Fund",
        RequestedBy = "Test Staff",
        Position = "Staff I",
        Items = new List<CreatePRItemDto>
        {
            new() { Description = "Bond Paper", Unit = "ream", Quantity = 5m, UnitCost = 220m },
        },
    };

    private static Mock<IPurchaseRequestRepository> RepoPRThatSaves(
        IReadOnlyList<PurchaseRequest>? existing = null)
    {
        Mock<IPurchaseRequestRepository> repo = new();
        repo.Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(existing ?? new List<PurchaseRequest>());
        repo.Setup(r => r.AddAsync(It.IsAny<PurchaseRequest>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        repo.Setup(r => r.UpdateAsync(It.IsAny<PurchaseRequest>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        repo.Setup(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);
        return repo;
    }

    private static Mock<IItemMasterRepository> RepoItemThatSaves()
    {
        Mock<IItemMasterRepository> repo = new();
        repo.Setup(r => r.AddAsync(It.IsAny<ItemMaster>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        repo.Setup(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);
        return repo;
    }

    private static PurchaseRequestService BuildSut(
        Mock<IPurchaseRequestRepository> prRepo,
        Mock<IItemMasterRepository>? itemRepo = null,
        Mock<IExcelService>? excelService = null,
        Mock<IRepository<Division>>? divisionRepo = null)
        => new(
            prRepo.Object,
            (itemRepo ?? RepoItemThatSaves()).Object,
            new PermissionService(),
            (excelService ?? new Mock<IExcelService>()).Object,
            (divisionRepo ?? DivisionsRepo()).Object,
            NullLogger<PurchaseRequestService>.Instance);

    // ── PRNo generation ───────────────────────────────────────────────────────

    [Fact]
    public async Task CreateAsync_WithNoPriorPRs_GeneratesPRNoWithSequence001()
    {
        Mock<IPurchaseRequestRepository> prRepo = RepoPRThatSaves();
        Mock<IItemMasterRepository> itemRepo = RepoItemThatSaves();
        itemRepo.Setup(r => r.GetByStockNoAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((ItemMaster?)null);

        ServiceResult<PRResponseDto> result =
            await BuildSut(prRepo, itemRepo).CreateAsync(MakeAdmin(), ValidDto("Administrative Division"));

        Assert.True(result.IsSuccess);
        Assert.Matches(@"^101-1041-GF-\d{4}-\d{2}-\d{2}-001$", result.Value!.PRNo);
    }

    [Fact]
    public async Task CreateAsync_WithExistingPRs_IncrementsSequenceByOne()
    {
        string existingPRNo = $"101-1041-GF-{DateTime.UtcNow:yyyy-MM-dd}-005";
        List<PurchaseRequest> existing = [MakePR(existingPRNo)];

        Mock<IPurchaseRequestRepository> prRepo = RepoPRThatSaves(existing);
        Mock<IItemMasterRepository> itemRepo = RepoItemThatSaves();
        itemRepo.Setup(r => r.GetByStockNoAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((ItemMaster?)null);

        ServiceResult<PRResponseDto> result =
            await BuildSut(prRepo, itemRepo).CreateAsync(MakeAdmin(), ValidDto("Administrative Division"));

        Assert.True(result.IsSuccess);
        Assert.EndsWith("-006", result.Value!.PRNo);
    }

    // ── Division-scope enforcement on create ──────────────────────────────────

    [Fact]
    public async Task CreateAsync_StaffSubmittingForOtherDivision_ReturnsForbidden()
    {
        // Staff is in Planning; tries to submit a PR for Admin division.
        User staff = MakeStaff(PlanningDiv);
        Mock<IPurchaseRequestRepository> prRepo = RepoPRThatSaves();

        ServiceResult<PRResponseDto> result =
            await BuildSut(prRepo).CreateAsync(staff, ValidDto("Administrative Division"));

        Assert.Equal(ServiceErrorCode.Forbidden, result.Code);
    }

    [Fact]
    public async Task CreateAsync_StaffSubmittingForOwnDivision_Succeeds()
    {
        User staff = MakeStaff(PlanningDiv);
        Mock<IPurchaseRequestRepository> prRepo = RepoPRThatSaves();
        Mock<IItemMasterRepository> itemRepo = RepoItemThatSaves();
        itemRepo.Setup(r => r.GetByStockNoAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((ItemMaster?)null);

        ServiceResult<PRResponseDto> result =
            await BuildSut(prRepo, itemRepo).CreateAsync(staff, ValidDto("Planning Division"));

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public async Task CreateAsync_WithoutInventoryPermission_ReturnsForbidden()
    {
        Mock<IPurchaseRequestRepository> prRepo = RepoPRThatSaves();

        ServiceResult<PRResponseDto> result =
            await BuildSut(prRepo).CreateAsync(MakeStaffNoInventory(), ValidDto("Planning Division"));

        Assert.Equal(ServiceErrorCode.Forbidden, result.Code);
    }

    // ── Division-scope enforcement on read ────────────────────────────────────

    [Fact]
    public async Task GetByIdAsync_StaffViewingOtherDivisionPR_ReturnsForbidden()
    {
        User staff = MakeStaff(PlanningDiv);
        PurchaseRequest adminPR = MakePR("101-1041-GF-2026-06-01-001", AdminDiv);
        adminPR.Items = new List<PRItem>();

        Mock<IPurchaseRequestRepository> prRepo = new();
        prRepo.Setup(r => r.GetWithItemsAsync(adminPR.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(adminPR);

        ServiceResult<PRResponseDto> result =
            await BuildSut(prRepo).GetByIdAsync(staff, adminPR.Id);

        Assert.Equal(ServiceErrorCode.Forbidden, result.Code);
    }

    [Fact]
    public async Task GetByIdAsync_StaffViewingOwnDivisionPR_ReturnsOk()
    {
        User staff = MakeStaff(PlanningDiv);
        PurchaseRequest planningPR = MakePR("101-1041-GF-2026-06-01-002", PlanningDiv);
        planningPR.Items = new List<PRItem>();

        Mock<IPurchaseRequestRepository> prRepo = new();
        prRepo.Setup(r => r.GetWithItemsAsync(planningPR.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(planningPR);

        ServiceResult<PRResponseDto> result =
            await BuildSut(prRepo).GetByIdAsync(staff, planningPR.Id);

        Assert.True(result.IsSuccess);
        Assert.Equal(planningPR.PRNo, result.Value!.PRNo);
    }

    // ── Unknown StockNo auto-creation ─────────────────────────────────────────

    [Fact]
    public async Task CreateAsync_UnknownStockNo_AutoCreatesItemMasterWithIsNewItemTrue()
    {
        ItemMaster? saved = null;
        Mock<IPurchaseRequestRepository> prRepo = RepoPRThatSaves();
        Mock<IItemMasterRepository> itemRepo = RepoItemThatSaves();

        itemRepo.Setup(r => r.GetByStockNoAsync("UNKNOWN-99", It.IsAny<CancellationToken>()))
            .ReturnsAsync((ItemMaster?)null);
        itemRepo.Setup(r => r.AddAsync(It.IsAny<ItemMaster>(), It.IsAny<CancellationToken>()))
            .Callback<ItemMaster, CancellationToken>((e, _) => saved = e)
            .Returns(Task.CompletedTask);

        CreatePRDto dto = new()
        {
            PRDate = DateOnly.FromDateTime(DateTime.UtcNow),
            Division = "Administrative Division",
            Fund = "General Fund",
            RequestedBy = "Test",
            Position = "Staff",
            Items = new List<CreatePRItemDto>
            {
                new() { StockNo = "UNKNOWN-99", Description = "New Supply", Unit = "pcs", Quantity = 2m, UnitCost = 50m },
            },
        };

        ServiceResult<PRResponseDto> result =
            await BuildSut(prRepo, itemRepo).CreateAsync(MakeAdmin(), dto);

        Assert.True(result.IsSuccess);
        Assert.NotNull(saved);
        Assert.True(saved!.IsNewItem);
        Assert.Equal("UNKNOWN-99", saved.StockNo);
    }

    // ── Known StockNo uses Items Master unit cost ─────────────────────────────

    [Fact]
    public async Task CreateAsync_KnownStockNo_UsesItemsMasterUnitCostNotSubmittedValue()
    {
        ItemMaster master = MakeItemMaster("01-01-01-01", unitCost: 350m);

        Mock<IPurchaseRequestRepository> prRepo = RepoPRThatSaves();
        Mock<IItemMasterRepository> itemRepo = RepoItemThatSaves();
        itemRepo.Setup(r => r.GetByStockNoAsync("01-01-01-01", It.IsAny<CancellationToken>()))
            .ReturnsAsync(master);

        CreatePRDto dto = new()
        {
            PRDate = DateOnly.FromDateTime(DateTime.UtcNow),
            Division = "Administrative Division",
            Fund = "General Fund",
            RequestedBy = "Test",
            Position = "Staff",
            Items = new List<CreatePRItemDto>
            {
                // Caller submits 999m but Items Master has 350m — service must use 350m.
                new() { StockNo = "01-01-01-01", Description = "Bond Paper", Unit = "ream", Quantity = 2m, UnitCost = 999m },
            },
        };

        ServiceResult<PRResponseDto> result =
            await BuildSut(prRepo, itemRepo).CreateAsync(MakeAdmin(), dto);

        Assert.True(result.IsSuccess);
        PRItemDto item = result.Value!.Items[0];
        Assert.Equal(350m, item.UnitCost);
        Assert.Equal(700m, item.TotalCost); // 2 × 350
    }

    // ── Update status gate ────────────────────────────────────────────────────

    [Fact]
    public async Task UpdateAsync_PRNotOpen_ReturnsBadRequest()
    {
        PurchaseRequest pr = MakePR("101-1041-GF-2026-06-01-003");
        pr.Status = PRStatus.PartiallyDelivered;
        pr.Items   = new List<PRItem>();

        Mock<IPurchaseRequestRepository> prRepo = RepoPRThatSaves();
        prRepo.Setup(r => r.GetWithItemsAsync(pr.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(pr);

        ServiceResult<PRResponseDto> result =
            await BuildSut(prRepo).UpdateAsync(MakeAdmin(), pr.Id, new UpdatePRDto { Fund = "Special Fund" });

        Assert.Equal(ServiceErrorCode.BadRequest, result.Code);
    }

    [Fact]
    public async Task UpdateAsync_StaffRole_ReturnsForbidden()
    {
        PurchaseRequest pr = MakePR("101-1041-GF-2026-06-01-004");
        Mock<IPurchaseRequestRepository> prRepo = RepoPRThatSaves();
        prRepo.Setup(r => r.GetWithItemsAsync(pr.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(pr);

        ServiceResult<PRResponseDto> result =
            await BuildSut(prRepo).UpdateAsync(MakeStaff(), pr.Id, new UpdatePRDto { Fund = "Special Fund" });

        Assert.Equal(ServiceErrorCode.Forbidden, result.Code);
    }

    // ── Excel import division pre-check ───────────────────────────────────────

    [Fact]
    public async Task ImportFromExcelAsync_WrongDivisionSheet_ReturnsForbidden_WithoutCreatingAnyPR()
    {
        User staff = MakeStaff(PlanningDiv);

        IReadOnlyList<PurchaseRequestImportRow> rows = new List<PurchaseRequestImportRow>
        {
            new()
            {
                SheetName   = "PR-001",
                DivisionName = "Administrative Division", // wrong division for Planning staff
                RequestedBy = "Test",
                PRDate      = DateOnly.FromDateTime(DateTime.UtcNow),
                Items       = new List<PRItemImportRow>
                {
                    new() { Description = "Bond Paper", Unit = "ream", Quantity = 1m },
                },
            },
        };

        Mock<IExcelService> excel = new();
        excel.Setup(e => e.ParsePRImport(It.IsAny<Stream>()))
            .Returns(rows);

        Mock<IPurchaseRequestRepository> prRepo = RepoPRThatSaves();

        ServiceResult<IReadOnlyList<PRResponseDto>> result =
            await BuildSut(prRepo, excelService: excel)
                .ImportFromExcelAsync(staff, Stream.Null);

        Assert.Equal(ServiceErrorCode.Forbidden, result.Code);
        prRepo.Verify(r => r.AddAsync(It.IsAny<PurchaseRequest>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
