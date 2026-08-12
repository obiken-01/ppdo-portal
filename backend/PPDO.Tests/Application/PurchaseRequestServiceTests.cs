using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using PPDO.Application.Common;
using PPDO.Application.DTOs.Config;
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
                new() { Id = AdminDiv,    OfficeId = 100, Code = "ADMIN",    Name = "Administrative Division", IsActive = true },
                new() { Id = PlanningDiv, OfficeId = 100, Code = "PLANNING", Name = "Planning Division",       IsActive = true },
            });
        return repo;
    }

    /// <summary>
    /// Offices repo for the PPDO-office scoping in division resolution. Office 100 matches the
    /// OfficeId on the fixtures in <see cref="DivisionsRepo"/>.
    /// </summary>
    private static Mock<IOfficeRepository> OfficesRepo()
    {
        Mock<IOfficeRepository> repo = new();
        repo.Setup(r => r.GetByCodeAsync("PPDO", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Office { Id = 100, OfficeCode = "PPDO", OfficeName = "Provincial Planning and Development Office", IsActive = true });
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
        IReadOnlyList<PurchaseRequest>? existing = null,
        int? maxSequence = null)
    {
        Mock<IPurchaseRequestRepository> repo = new();
        repo.Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(existing ?? new List<PurchaseRequest>());
        repo.Setup(r => r.GetMaxPrSequenceAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(maxSequence);
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
        repo.Setup(r => r.GetByStockNosAsync(It.IsAny<IReadOnlyCollection<string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ItemMaster>());
        return repo;
    }

    /// <summary>Default IAccountService mock — GetAllAsync returns empty rather than Moq's
    /// unconfigured-Task default of null, which would NRE any caller that enumerates it.</summary>
    private static Mock<IAccountService> DefaultAccountService()
    {
        Mock<IAccountService> accounts = new();
        accounts.Setup(a => a.GetAllAsync(
                It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<ActiveFilter>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<AccountDto>());
        return accounts;
    }

    private static PurchaseRequestService BuildSut(
        Mock<IPurchaseRequestRepository> prRepo,
        Mock<IItemMasterRepository>? itemRepo = null,
        Mock<IExcelService>? excelService = null,
        Mock<IPdfService>? pdfService = null,
        Mock<IAccountService>? accountService = null,
        Mock<IRepository<Division>>? divisionRepo = null,
        Mock<IOfficeRepository>? officeRepo = null,
        Mock<IAuditService>? auditService = null)
        => new(
            prRepo.Object,
            (itemRepo ?? RepoItemThatSaves()).Object,
            new PermissionService(),
            (excelService ?? new Mock<IExcelService>()).Object,
            (pdfService ?? new Mock<IPdfService>()).Object,
            (accountService ?? DefaultAccountService()).Object,
            (divisionRepo ?? DivisionsRepo()).Object,
            (officeRepo ?? OfficesRepo()).Object,
            (auditService ?? new Mock<IAuditService>()).Object,
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
    public async Task CreateAsync_WithExistingMaxSequence007_IncrementsToSequence008()
    {
        Mock<IPurchaseRequestRepository> prRepo = RepoPRThatSaves(maxSequence: 7);
        Mock<IItemMasterRepository> itemRepo = RepoItemThatSaves();
        itemRepo.Setup(r => r.GetByStockNoAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((ItemMaster?)null);

        ServiceResult<PRResponseDto> result =
            await BuildSut(prRepo, itemRepo).CreateAsync(MakeAdmin(), ValidDto("Administrative Division"));

        Assert.True(result.IsSuccess);
        Assert.EndsWith("-008", result.Value!.PRNo);
    }

    [Fact]
    public async Task CreateAsync_QueriesMaxSequenceViaScopedRepositoryMethod_NeverFullTableLoad()
    {
        // Regression guard for RAL-191: GeneratePRNoAsync must never fall back to loading
        // every PR into memory to compute the next sequence.
        Mock<IPurchaseRequestRepository> prRepo = RepoPRThatSaves(maxSequence: 3);
        Mock<IItemMasterRepository> itemRepo = RepoItemThatSaves();
        itemRepo.Setup(r => r.GetByStockNoAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((ItemMaster?)null);

        ServiceResult<PRResponseDto> result =
            await BuildSut(prRepo, itemRepo).CreateAsync(MakeAdmin(), ValidDto("Administrative Division"));

        Assert.True(result.IsSuccess);
        prRepo.Verify(r => r.GetMaxPrSequenceAsync(It.IsAny<CancellationToken>()), Times.Once);
        prRepo.Verify(r => r.GetAllAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task CreateAsync_WhenMaxSequenceIsNull_GeneratesPRNoWithSequence001()
    {
        // GetMaxPrSequenceAsync returns null both when the table is empty and when every
        // PRNo is malformed/legacy (TRY_CAST-filtered out in SQL) — both cases must fall
        // back to sequence 1, never throw.
        Mock<IPurchaseRequestRepository> prRepo = RepoPRThatSaves(maxSequence: null);
        Mock<IItemMasterRepository> itemRepo = RepoItemThatSaves();
        itemRepo.Setup(r => r.GetByStockNoAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((ItemMaster?)null);

        ServiceResult<PRResponseDto> result =
            await BuildSut(prRepo, itemRepo).CreateAsync(MakeAdmin(), ValidDto("Administrative Division"));

        Assert.True(result.IsSuccess);
        Assert.Matches(@"^101-1041-GF-\d{4}-\d{2}-\d{2}-001$", result.Value!.PRNo);
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

    /// <summary>
    /// PR No. on the import sheet is optional. When the sheet supplies one, it should be
    /// used verbatim — the same "supplied value wins, blank auto-generates" contract as the
    /// manual Create PR form's PrNo field.
    /// </summary>
    [Fact]
    public async Task ImportFromExcelAsync_SheetSuppliesPrNo_UsesSuppliedValue()
    {
        IReadOnlyList<PurchaseRequestImportRow> rows = new List<PurchaseRequestImportRow>
        {
            new()
            {
                SheetName    = "PR-001",
                PrNo         = "101-1041-GF-2026-06-01-099",
                DivisionName = "Administrative Division",
                RequestedBy  = "Test",
                PRDate       = DateOnly.FromDateTime(DateTime.UtcNow),
                Items        = new List<PRItemImportRow>
                {
                    new() { Description = "Bond Paper", Unit = "ream", Quantity = 1m },
                },
            },
        };

        Mock<IExcelService> excel = new();
        excel.Setup(e => e.ParsePRImport(It.IsAny<Stream>())).Returns(rows);

        Mock<IPurchaseRequestRepository> prRepo = RepoPRThatSaves();

        ServiceResult<IReadOnlyList<PRResponseDto>> result =
            await BuildSut(prRepo, excelService: excel).ImportFromExcelAsync(MakeAdmin(), Stream.Null);

        Assert.True(result.IsSuccess);
        Assert.Equal("101-1041-GF-2026-06-01-099", result.Value![0].PRNo);
    }

    /// <summary>
    /// The sheet's Division cell is free text and may carry the division's short code rather
    /// than its full name. The Staff pre-check must resolve before comparing, not string-match
    /// the raw cell against the user's division name.
    /// </summary>
    [Fact]
    public async Task ImportFromExcelAsync_OwnDivisionByCode_IsAccepted()
    {
        User staff = MakeStaff(PlanningDiv);

        IReadOnlyList<PurchaseRequestImportRow> rows = new List<PurchaseRequestImportRow>
        {
            new()
            {
                SheetName    = "PR-001",
                DivisionName = "PLANNING", // the code, not "Planning Division"
                RequestedBy  = "Test",
                PRDate       = DateOnly.FromDateTime(DateTime.UtcNow),
                Items        = new List<PRItemImportRow>
                {
                    new() { Description = "Bond Paper", Unit = "ream", Quantity = 1m },
                },
            },
        };

        Mock<IExcelService> excel = new();
        excel.Setup(e => e.ParsePRImport(It.IsAny<Stream>())).Returns(rows);

        Mock<IPurchaseRequestRepository> prRepo = RepoPRThatSaves();

        ServiceResult<IReadOnlyList<PRResponseDto>> result =
            await BuildSut(prRepo, excelService: excel).ImportFromExcelAsync(staff, Stream.Null);

        Assert.True(result.IsSuccess);
        prRepo.Verify(r => r.AddAsync(It.IsAny<PurchaseRequest>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    // ── PreviewGsoImportAsync (RAL-196) ───────────────────────────────────────────

    private static GsoPRImportRow SampleGsoRow(IReadOnlyList<PRItemImportRow>? items = null) => new()
    {
        PrNo      = "101-1041-GF-2026-04-28-757",
        Fund      = "General Fund",
        PRDate    = new DateOnly(2026, 4, 28),
        AccountNo = "5 02 03 990",
        Program   = "1000-000-1-01-010-001 - PLANNING PROGRAM",
        Items     = items ?? new List<PRItemImportRow>
        {
            new() { StockNo = "OSAME-1", Description = "Bathroom Tissue", Unit = "roll", Quantity = 35m, UnitCost = 21m },
        },
    };

    private static AccountDto SampleAccount(string number, string title) => new(
        Id: 1, AccountTitle: title, AccountNumber: number, NormalBalance: null, Description: null,
        IsActive: true, AccountType: "MOOE", ExpenseClass: "MOOE", DefaultNature: null, DefaultApplyReserve: false);

    [Fact]
    public async Task PreviewGsoImportAsync_WithoutInventoryPermission_ReturnsForbidden()
    {
        Mock<IExcelService> excel = new();
        excel.Setup(e => e.ParseGsoPRImport(It.IsAny<Stream>())).Returns(SampleGsoRow());

        ServiceResult<GsoPRImportPreviewDto> result =
            await BuildSut(RepoPRThatSaves(), excelService: excel)
                .PreviewGsoImportAsync(MakeStaffNoInventory(), Stream.Null);

        Assert.Equal(ServiceErrorCode.Forbidden, result.Code);
    }

    [Fact]
    public async Task PreviewGsoImportAsync_ParseError_ReturnsBadRequest()
    {
        Mock<IExcelService> excel = new();
        excel.Setup(e => e.ParseGsoPRImport(It.IsAny<Stream>()))
            .Throws(new ImportParseException(new[] { "bad file" }));

        ServiceResult<GsoPRImportPreviewDto> result =
            await BuildSut(RepoPRThatSaves(), excelService: excel)
                .PreviewGsoImportAsync(MakeAdmin(), Stream.Null);

        Assert.Equal(ServiceErrorCode.BadRequest, result.Code);
    }

    [Fact]
    public async Task PreviewGsoImportAsync_ResolvesAccountTitleFromConfig()
    {
        // SampleGsoRow's AccountNo ("5 02 03 990") is space-separated, matching the real GSO
        // export; the config table stores dash-separated codes ("5-02-03-990"). A plain
        // Contains/exact-string search would never find this — matching must be digits-only.
        Mock<IExcelService> excel = new();
        excel.Setup(e => e.ParseGsoPRImport(It.IsAny<Stream>())).Returns(SampleGsoRow());

        Mock<IAccountService> accounts = new();
        accounts.Setup(a => a.GetAllAsync(null, null, ActiveFilter.Active, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<AccountDto> { SampleAccount("5-02-03-990", "Other Supplies and Materials Expenses") });

        ServiceResult<GsoPRImportPreviewDto> result =
            await BuildSut(RepoPRThatSaves(), excelService: excel, accountService: accounts)
                .PreviewGsoImportAsync(MakeAdmin(), Stream.Null);

        Assert.True(result.IsSuccess);
        Assert.Equal("Other Supplies and Materials Expenses", result.Value!.AccountTitle);
        // AccountNo is normalized to the config table's own canonical formatting, not a
        // guessed reformat of the GSO text.
        Assert.Equal("5-02-03-990", result.Value!.AccountNo);
    }

    [Fact]
    public async Task PreviewGsoImportAsync_AccountNotInConfig_KeepsRawValueAndNullTitle()
    {
        Mock<IExcelService> excel = new();
        excel.Setup(e => e.ParseGsoPRImport(It.IsAny<Stream>())).Returns(SampleGsoRow());

        Mock<IAccountService> accounts = new();
        accounts.Setup(a => a.GetAllAsync(null, null, ActiveFilter.Active, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<AccountDto>());

        ServiceResult<GsoPRImportPreviewDto> result =
            await BuildSut(RepoPRThatSaves(), excelService: excel, accountService: accounts)
                .PreviewGsoImportAsync(MakeAdmin(), Stream.Null);

        Assert.True(result.IsSuccess);
        Assert.Null(result.Value!.AccountTitle);
        // Nothing found in Config Accounts — keep the raw parsed value rather than dropping it.
        Assert.Equal("5 02 03 990", result.Value!.AccountNo);
    }

    [Fact]
    public async Task PreviewGsoImportAsync_MultipleAccountsSameDigitsDifferentPunctuation_MatchesCorrectOne()
    {
        List<PRItemImportRow> items = new()
        {
            new() { StockNo = "OSAME-1", Description = "Item", Unit = "pcs", Quantity = 1m },
        };
        GsoPRImportRow row = SampleGsoRow(items) with { AccountNo = "5.02.03.990" }; // yet another punctuation style
        Mock<IExcelService> excel = new();
        excel.Setup(e => e.ParseGsoPRImport(It.IsAny<Stream>())).Returns(row);

        Mock<IAccountService> accounts = new();
        accounts.Setup(a => a.GetAllAsync(null, null, ActiveFilter.Active, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<AccountDto>
            {
                SampleAccount("5-01-01-010", "Salaries and Wages - Regular"),
                SampleAccount("5-02-03-990", "Other Supplies and Materials Expenses"),
            });

        ServiceResult<GsoPRImportPreviewDto> result =
            await BuildSut(RepoPRThatSaves(), excelService: excel, accountService: accounts)
                .PreviewGsoImportAsync(MakeAdmin(), Stream.Null);

        Assert.True(result.IsSuccess);
        Assert.Equal("Other Supplies and Materials Expenses", result.Value!.AccountTitle);
        Assert.Equal("5-02-03-990", result.Value!.AccountNo);
    }

    [Fact]
    public async Task PreviewGsoImportAsync_FlagsUnknownStockNo()
    {
        List<PRItemImportRow> items = new()
        {
            new() { StockNo = "UNKNOWN-1", Description = "Mystery Item", Unit = "pcs", Quantity = 1m },
        };
        Mock<IExcelService> excel = new();
        excel.Setup(e => e.ParseGsoPRImport(It.IsAny<Stream>())).Returns(SampleGsoRow(items));

        Mock<IItemMasterRepository> itemRepo = RepoItemThatSaves();
        itemRepo.Setup(r => r.GetByStockNosAsync(It.IsAny<IReadOnlyCollection<string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ItemMaster>()); // nothing known

        ServiceResult<GsoPRImportPreviewDto> result =
            await BuildSut(RepoPRThatSaves(), itemRepo: itemRepo, excelService: excel)
                .PreviewGsoImportAsync(MakeAdmin(), Stream.Null);

        Assert.True(result.IsSuccess);
        Assert.True(result.Value!.Items[0].IsUnknownStock);
    }

    [Fact]
    public async Task PreviewGsoImportAsync_KnownStockNo_NotFlagged()
    {
        List<PRItemImportRow> items = new()
        {
            new() { StockNo = "OSAME-1", Description = "Bathroom Tissue", Unit = "roll", Quantity = 35m, UnitCost = 21m },
        };
        Mock<IExcelService> excel = new();
        excel.Setup(e => e.ParseGsoPRImport(It.IsAny<Stream>())).Returns(SampleGsoRow(items));

        Mock<IItemMasterRepository> itemRepo = RepoItemThatSaves();
        itemRepo.Setup(r => r.GetByStockNosAsync(It.IsAny<IReadOnlyCollection<string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ItemMaster>
            {
                new() { Id = Guid.NewGuid(), StockNo = "OSAME-1", Description = "Bathroom Tissue", Unit = "roll", UnitCost = 21m },
            });

        ServiceResult<GsoPRImportPreviewDto> result =
            await BuildSut(RepoPRThatSaves(), itemRepo: itemRepo, excelService: excel)
                .PreviewGsoImportAsync(MakeAdmin(), Stream.Null);

        Assert.True(result.IsSuccess);
        Assert.False(result.Value!.Items[0].IsUnknownStock);
    }

    [Fact]
    public async Task PreviewGsoImportAsync_NeverCreatesAPurchaseRequest()
    {
        Mock<IExcelService> excel = new();
        excel.Setup(e => e.ParseGsoPRImport(It.IsAny<Stream>())).Returns(SampleGsoRow());

        Mock<IPurchaseRequestRepository> prRepo = RepoPRThatSaves();

        await BuildSut(prRepo, excelService: excel).PreviewGsoImportAsync(MakeAdmin(), Stream.Null);

        prRepo.Verify(r => r.AddAsync(It.IsAny<PurchaseRequest>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task PreviewGsoImportAsync_PdfMagicBytes_DispatchesToPdfServiceNotExcelService()
    {
        Mock<IExcelService> excel = new();
        Mock<IPdfService> pdf = new();
        pdf.Setup(p => p.ParseGsoPRImport(It.IsAny<Stream>())).Returns(SampleGsoRow());

        using MemoryStream pdfStream = new(new byte[] { 0x25, 0x50, 0x44, 0x46, 0x2D, 0x31, 0x2E, 0x34 }); // "%PDF-1.4"

        ServiceResult<GsoPRImportPreviewDto> result =
            await BuildSut(RepoPRThatSaves(), excelService: excel, pdfService: pdf)
                .PreviewGsoImportAsync(MakeAdmin(), pdfStream);

        Assert.True(result.IsSuccess);
        pdf.Verify(p => p.ParseGsoPRImport(It.IsAny<Stream>()), Times.Once);
        excel.Verify(e => e.ParseGsoPRImport(It.IsAny<Stream>()), Times.Never);
    }

    [Fact]
    public async Task PreviewGsoImportAsync_NonPdfBytes_DispatchesToExcelServiceNotPdfService()
    {
        Mock<IExcelService> excel = new();
        excel.Setup(e => e.ParseGsoPRImport(It.IsAny<Stream>())).Returns(SampleGsoRow());
        Mock<IPdfService> pdf = new();

        // Zip local-file-header signature (PK\x03\x04) — how a real .xlsx export starts.
        using MemoryStream xlsxStream = new(new byte[] { 0x50, 0x4B, 0x03, 0x04 });

        ServiceResult<GsoPRImportPreviewDto> result =
            await BuildSut(RepoPRThatSaves(), excelService: excel, pdfService: pdf)
                .PreviewGsoImportAsync(MakeAdmin(), xlsxStream);

        Assert.True(result.IsSuccess);
        excel.Verify(e => e.ParseGsoPRImport(It.IsAny<Stream>()), Times.Once);
        pdf.Verify(p => p.ParseGsoPRImport(It.IsAny<Stream>()), Times.Never);
    }

    // ── Division resolution ───────────────────────────────────────────────────

    [Fact]
    public async Task CreateAsync_DivisionSuppliedAsCode_ResolvesToTheDivision()
    {
        Mock<IPurchaseRequestRepository> prRepo = RepoPRThatSaves();

        ServiceResult<PRResponseDto> result =
            await BuildSut(prRepo).CreateAsync(MakeStaff(PlanningDiv), ValidDto(division: "PLANNING"));

        Assert.True(result.IsSuccess);
        prRepo.Verify(r => r.AddAsync(
            It.Is<PurchaseRequest>(p => p.DivisionId == PlanningDiv), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task CreateAsync_DivisionNameDiffersInCase_StillResolves()
    {
        Mock<IPurchaseRequestRepository> prRepo = RepoPRThatSaves();

        ServiceResult<PRResponseDto> result =
            await BuildSut(prRepo).CreateAsync(MakeStaff(PlanningDiv), ValidDto(division: "  planning division  "));

        Assert.True(result.IsSuccess);
    }

    /// <summary>
    /// Division names are unique only WITHIN an office. Resolution must be scoped to PPDO's
    /// own divisions so a same-named division under another office can never be selected.
    /// </summary>
    [Fact]
    public async Task CreateAsync_DivisionNameOwnedByAnotherOffice_IsNotResolved()
    {
        Mock<IRepository<Division>> divisions = new();
        divisions.Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Division>
            {
                new() { Id = AdminDiv,    OfficeId = 100, Code = "ADMIN",    Name = "Administrative Division", IsActive = true },
                new() { Id = PlanningDiv, OfficeId = 100, Code = "PLANNING", Name = "Planning Division",       IsActive = true },
                // Same name, different office — must never win.
                new() { Id = 999,         OfficeId = 300, Code = "ADMIN",    Name = "Administrative",          IsActive = true },
            });

        Mock<IPurchaseRequestRepository> prRepo = RepoPRThatSaves();

        ServiceResult<PRResponseDto> result = await BuildSut(prRepo, divisionRepo: divisions)
            .CreateAsync(MakeAdmin(), ValidDto(division: "Administrative"));

        Assert.Equal(ServiceErrorCode.BadRequest, result.Code);
        prRepo.Verify(r => r.AddAsync(It.IsAny<PurchaseRequest>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task CreateAsync_UnknownDivision_ErrorListsTheValidDivisions()
    {
        ServiceResult<PRResponseDto> result =
            await BuildSut(RepoPRThatSaves()).CreateAsync(MakeAdmin(), ValidDto(division: "SPD"));

        Assert.Equal(ServiceErrorCode.BadRequest, result.Code);
        Assert.Contains("Administrative Division", result.Error);
        Assert.Contains("Planning Division", result.Error);
    }

    [Fact]
    public async Task CreateAsync_InactiveDivision_IsNotResolved()
    {
        Mock<IRepository<Division>> divisions = new();
        divisions.Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Division>
            {
                new() { Id = AdminDiv, OfficeId = 100, Code = "ADMIN", Name = "Administrative Division", IsActive = false },
            });

        ServiceResult<PRResponseDto> result = await BuildSut(RepoPRThatSaves(), divisionRepo: divisions)
            .CreateAsync(MakeAdmin(), ValidDto(division: "Administrative Division"));

        Assert.Equal(ServiceErrorCode.BadRequest, result.Code);
    }

    // ── SearchAsync (RAL-192 step 3) ──────────────────────────────────────────

    private static PRSearchFilterDto DefaultSearchFilter() => new(
        Page: 1, PageSize: 50, Search: null, DateFrom: null, DateTo: null, Statuses: null,
        Division: null, RequestedBy: null, Fund: null, AIPCode: null, AccountNo: null,
        AccountTitle: null, Program: null, Project: null, Activity: null,
        SortBy: null, SortDescending: false);

    [Fact]
    public async Task SearchAsync_StaffWithNullDivision_ReturnsEmptyWithoutCallingRepo()
    {
        User officeStaff = new()
        {
            Id = Guid.NewGuid(), FullName = "Office Encoder", Email = "office@lgu.gov.ph",
            PasswordHash = "hash", Role = UserRole.Staff, DivisionId = null, IsActive = true,
        };
        Mock<IPurchaseRequestRepository> prRepo = new();

        PRSearchResultDto result =
            await BuildSut(prRepo).SearchAsync(officeStaff, DefaultSearchFilter());

        Assert.Empty(result.Items);
        Assert.Equal(0, result.TotalCount);
        prRepo.Verify(r => r.SearchAsync(
            It.IsAny<PRSearchCriteria>(), It.IsAny<int?>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task SearchAsync_AdminRole_PassesNullDivisionToRepo()
    {
        int? captured = 999; // sentinel — not null
        Mock<IPurchaseRequestRepository> prRepo = new();
        prRepo.Setup(r => r.SearchAsync(It.IsAny<PRSearchCriteria>(), It.IsAny<int?>(), It.IsAny<CancellationToken>()))
            .Callback<PRSearchCriteria, int?, CancellationToken>((_, d, _) => captured = d)
            .ReturnsAsync(new PRSearchResult(new List<PurchaseRequest>(), 0, new PRStatusCounts(0, 0, 0, 0)));

        await BuildSut(prRepo).SearchAsync(MakeAdmin(), DefaultSearchFilter());

        Assert.Null(captured);
    }

    [Fact]
    public async Task SearchAsync_StaffRole_PassesOwnDivisionToRepo()
    {
        int? captured = null;
        Mock<IPurchaseRequestRepository> prRepo = new();
        prRepo.Setup(r => r.SearchAsync(It.IsAny<PRSearchCriteria>(), It.IsAny<int?>(), It.IsAny<CancellationToken>()))
            .Callback<PRSearchCriteria, int?, CancellationToken>((_, d, _) => captured = d)
            .ReturnsAsync(new PRSearchResult(new List<PurchaseRequest>(), 0, new PRStatusCounts(0, 0, 0, 0)));

        await BuildSut(prRepo).SearchAsync(MakeStaff(PlanningDiv), DefaultSearchFilter());

        Assert.Equal(PlanningDiv, captured);
    }

    [Fact]
    public async Task SearchAsync_PassesEveryFilterFieldThroughToCriteria()
    {
        PRSearchCriteria? captured = null;
        Mock<IPurchaseRequestRepository> prRepo = new();
        prRepo.Setup(r => r.SearchAsync(It.IsAny<PRSearchCriteria>(), It.IsAny<int?>(), It.IsAny<CancellationToken>()))
            .Callback<PRSearchCriteria, int?, CancellationToken>((c, _, _) => captured = c)
            .ReturnsAsync(new PRSearchResult(new List<PurchaseRequest>(), 0, new PRStatusCounts(0, 0, 0, 0)));

        PRSearchFilterDto filter = new(
            Page: 2, PageSize: 25, Search: "bond", DateFrom: new DateOnly(2026, 1, 1),
            DateTo: new DateOnly(2026, 3, 31), Statuses: [PRStatus.Open, PRStatus.PartiallyDelivered],
            Division: "Administrative Division", RequestedBy: "Ralph", Fund: "GAD",
            AIPCode: "AIP-1", AccountNo: "ACC-1", AccountTitle: "Supplies", Program: "Prog",
            Project: "Proj", Activity: "Act", SortBy: "totalAmount", SortDescending: true);

        await BuildSut(prRepo).SearchAsync(MakeAdmin(), filter);

        Assert.NotNull(captured);
        Assert.Equal(2, captured!.Page);
        Assert.Equal(25, captured.PageSize);
        Assert.Equal("bond", captured.Search);
        Assert.Equal(new DateOnly(2026, 1, 1), captured.DateFrom);
        Assert.Equal(new DateOnly(2026, 3, 31), captured.DateTo);
        Assert.Equal([PRStatus.Open, PRStatus.PartiallyDelivered], captured.Statuses);
        Assert.Equal("Administrative Division", captured.Division);
        Assert.Equal("Ralph", captured.RequestedBy);
        Assert.Equal("GAD", captured.Fund);
        Assert.Equal("AIP-1", captured.AIPCode);
        Assert.Equal("ACC-1", captured.AccountNo);
        Assert.Equal("Supplies", captured.AccountTitle);
        Assert.Equal("Prog", captured.Program);
        Assert.Equal("Proj", captured.Project);
        Assert.Equal("Act", captured.Activity);
        Assert.Equal("totalAmount", captured.SortBy);
        Assert.True(captured.SortDescending);
    }

    [Fact]
    public async Task SearchAsync_MapsRepositoryResultToResponseDto()
    {
        PurchaseRequest pr = MakePR("101-1041-GF-2026-06-02-001");
        Mock<IPurchaseRequestRepository> prRepo = new();
        prRepo.Setup(r => r.SearchAsync(It.IsAny<PRSearchCriteria>(), It.IsAny<int?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PRSearchResult(
                new List<PurchaseRequest> { pr }, TotalCount: 7,
                new PRStatusCounts(Open: 3, PartiallyDelivered: 2, FullyDelivered: 1, Completed: 1)));

        PRSearchFilterDto filter = DefaultSearchFilter() with { Page = 2, PageSize = 10 };
        PRSearchResultDto result = await BuildSut(prRepo).SearchAsync(MakeAdmin(), filter);

        Assert.Single(result.Items);
        Assert.Equal(pr.PRNo, result.Items[0].PRNo);
        Assert.Equal(7, result.TotalCount);
        Assert.Equal(2, result.Page);
        Assert.Equal(10, result.PageSize);
        Assert.Equal(3, result.StatusCounts.Open);
        Assert.Equal(2, result.StatusCounts.PartiallyDelivered);
        Assert.Equal(1, result.StatusCounts.FullyDelivered);
        Assert.Equal(1, result.StatusCounts.Completed);
    }

    // ── Field length validation ───────────────────────────────────────────────
    //
    // Regression guard: an oversized field (e.g. a GSO PDF/Excel import misreading a long
    // description into a short field like AccountNo) must fail with a clear BadRequest, not
    // an unhandled SqlException 500 from EF hitting the DB column's MaxLength constraint.

    [Fact]
    public async Task CreateAsync_AccountNoExceeds50Chars_ReturnsBadRequest_NeverHitsRepository()
    {
        Mock<IPurchaseRequestRepository> prRepo = RepoPRThatSaves();
        CreatePRDto dto = ValidDto("Administrative Division") with
        {
            AccountNo = new string('x', 51),
        };

        ServiceResult<PRResponseDto> result = await BuildSut(prRepo).CreateAsync(MakeAdmin(), dto);

        Assert.Equal(ServiceErrorCode.BadRequest, result.Code);
        Assert.Contains("Account No.", result.Error);
        prRepo.Verify(r => r.AddAsync(It.IsAny<PurchaseRequest>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task UpdateAsync_AccountTitleExceeds200Chars_ReturnsBadRequest_NeverSaves()
    {
        PurchaseRequest pr = MakePR("101-1041-GF-2026-06-01-009");
        Mock<IPurchaseRequestRepository> prRepo = RepoPRThatSaves();
        prRepo.Setup(r => r.GetWithItemsAsync(pr.Id, It.IsAny<CancellationToken>())).ReturnsAsync(pr);

        ServiceResult<PRResponseDto> result = await BuildSut(prRepo).UpdateAsync(
            MakeAdmin(), pr.Id, new UpdatePRDto { AccountTitle = new string('x', 201) });

        Assert.Equal(ServiceErrorCode.BadRequest, result.Code);
        Assert.Contains("Account Title", result.Error);
        prRepo.Verify(r => r.UpdateAsync(It.IsAny<PurchaseRequest>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    // ── Item Description length (RAL-226) ─────────────────────────────────────
    // Aligned to Price Index's 300-char convention (PriceIndexItem.Name), with a friendly
    // BadRequest instead of relying on the DB's nvarchar(300) cap.

    [Fact]
    public async Task CreateAsync_ItemDescriptionExceeds300Chars_ReturnsBadRequest_NeverHitsRepository()
    {
        Mock<IPurchaseRequestRepository> prRepo = RepoPRThatSaves();
        CreatePRDto dto = ValidDto("Administrative Division") with
        {
            Items = new List<CreatePRItemDto>
            {
                new() { Description = new string('x', 301), Unit = "ream", Quantity = 5m, UnitCost = 220m },
            },
        };

        ServiceResult<PRResponseDto> result = await BuildSut(prRepo).CreateAsync(MakeAdmin(), dto);

        Assert.Equal(ServiceErrorCode.BadRequest, result.Code);
        Assert.Contains("Description", result.Error);
        prRepo.Verify(r => r.AddAsync(It.IsAny<PurchaseRequest>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task UpdateAsync_ItemDescriptionExceeds300Chars_ReturnsBadRequest_NeverSaves()
    {
        PurchaseRequest pr = MakePR("101-1041-GF-2026-06-01-011");
        Mock<IPurchaseRequestRepository> prRepo = RepoPRThatSaves();
        prRepo.Setup(r => r.GetWithItemsAsync(pr.Id, It.IsAny<CancellationToken>())).ReturnsAsync(pr);

        UpdatePRDto dto = new()
        {
            Items = new List<CreatePRItemDto>
            {
                new() { Description = new string('x', 301), Unit = "ream", Quantity = 5m, UnitCost = 220m },
            },
        };

        ServiceResult<PRResponseDto> result = await BuildSut(prRepo).UpdateAsync(MakeAdmin(), pr.Id, dto);

        Assert.Equal(ServiceErrorCode.BadRequest, result.Code);
        Assert.Contains("Description", result.Error);
        prRepo.Verify(r => r.UpdateAsync(It.IsAny<PurchaseRequest>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    // ── Audit logging (RAL-200) ───────────────────────────────────────────────

    [Fact]
    public async Task CreateAsync_CallsAuditLog_WithCreateAction()
    {
        Mock<IPurchaseRequestRepository> prRepo = RepoPRThatSaves();
        Mock<IAuditService> audit = new();

        ServiceResult<PRResponseDto> result = await BuildSut(prRepo, auditService: audit)
            .CreateAsync(MakeAdmin(), ValidDto("Administrative Division"));

        Assert.True(result.IsSuccess);
        audit.Verify(a => a.LogAsync(
            "PurchaseRequests", result.Value!.Id, AuditAction.Create,
            null, It.IsAny<object?>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task UpdateAsync_ChangedField_CallsAuditLog_WithOnlyThatFieldTracked()
    {
        PurchaseRequest pr = MakePR("101-1041-GF-2026-06-01-005");
        pr.Fund = "General Fund";
        pr.Program = "Original Program";

        Mock<IPurchaseRequestRepository> prRepo = RepoPRThatSaves();
        prRepo.Setup(r => r.GetWithItemsAsync(pr.Id, It.IsAny<CancellationToken>())).ReturnsAsync(pr);
        Mock<IAuditService> audit = new();

        // Program resent with the SAME value it already has — must not appear in the diff;
        // only Fund actually changed.
        await BuildSut(prRepo, auditService: audit).UpdateAsync(
            MakeAdmin(), pr.Id, new UpdatePRDto { Fund = "Special Fund", Program = "Original Program" });

        audit.Verify(a => a.LogAsync(
            "PurchaseRequests", pr.Id, AuditAction.Update,
            It.Is<object>(o => ((IDictionary<string, object?>)o).ContainsKey("Fund")
                             && !((IDictionary<string, object?>)o).ContainsKey("Program")),
            It.IsAny<object?>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task UpdateAsync_NoFieldsActuallyChanged_DoesNotCallAuditLog()
    {
        PurchaseRequest pr = MakePR("101-1041-GF-2026-06-01-006");
        pr.Fund = "General Fund";

        Mock<IPurchaseRequestRepository> prRepo = RepoPRThatSaves();
        prRepo.Setup(r => r.GetWithItemsAsync(pr.Id, It.IsAny<CancellationToken>())).ReturnsAsync(pr);
        Mock<IAuditService> audit = new();

        // Resend the exact same value — nothing actually changed.
        await BuildSut(prRepo, auditService: audit).UpdateAsync(
            MakeAdmin(), pr.Id, new UpdatePRDto { Fund = "General Fund" });

        audit.Verify(a => a.LogAsync(
            It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<string>(),
            It.IsAny<object?>(), It.IsAny<object?>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task MarkCompletedAsync_CallsAuditLog_WithOldAndNewStatus()
    {
        PurchaseRequest pr = MakePR("101-1041-GF-2026-06-01-007");
        pr.Status = PRStatus.FullyDelivered;

        Mock<IPurchaseRequestRepository> prRepo = RepoPRThatSaves();
        prRepo.Setup(r => r.GetByIdAsync(pr.Id, It.IsAny<CancellationToken>())).ReturnsAsync(pr);
        Mock<IAuditService> audit = new();

        await BuildSut(prRepo, auditService: audit).MarkCompletedAsync(MakeAdmin(), pr.Id);

        audit.Verify(a => a.LogAsync(
            "PurchaseRequests", pr.Id, AuditAction.Update,
            It.IsNotNull<object>(), It.IsNotNull<object>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task UnmarkCompletedAsync_CallsAuditLog_WithOldAndNewStatus()
    {
        PurchaseRequest pr = MakePR("101-1041-GF-2026-06-01-008");
        pr.Status = PRStatus.Completed;

        Mock<IPurchaseRequestRepository> prRepo = RepoPRThatSaves();
        prRepo.Setup(r => r.GetByIdAsync(pr.Id, It.IsAny<CancellationToken>())).ReturnsAsync(pr);
        Mock<IAuditService> audit = new();

        await BuildSut(prRepo, auditService: audit).UnmarkCompletedAsync(MakeAdmin(), pr.Id);

        audit.Verify(a => a.LogAsync(
            "PurchaseRequests", pr.Id, AuditAction.Update,
            It.IsNotNull<object>(), It.IsNotNull<object>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ImportFromExcelAsync_CallsAuditLog_ExactlyOnce_NotOncePerPR()
    {
        List<PurchaseRequestImportRow> rows =
        [
            new()
            {
                SheetName = "PR-001", DivisionName = "Administrative Division", RequestedBy = "Ralph",
                PRDate = DateOnly.FromDateTime(DateTime.UtcNow),
                Items = [new() { Description = "Item A", Unit = "pc", Quantity = 1m }],
            },
            new()
            {
                SheetName = "PR-002", DivisionName = "Administrative Division", RequestedBy = "Ralph",
                PRDate = DateOnly.FromDateTime(DateTime.UtcNow),
                Items = [new() { Description = "Item B", Unit = "pc", Quantity = 1m }],
            },
        ];

        Mock<IExcelService> excel = new();
        excel.Setup(e => e.ParsePRImport(It.IsAny<Stream>())).Returns(rows);
        Mock<IPurchaseRequestRepository> prRepo = RepoPRThatSaves();
        Mock<IItemMasterRepository> itemRepo = RepoItemThatSaves();
        itemRepo.Setup(r => r.GetByStockNoAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((ItemMaster?)null);
        Mock<IAuditService> audit = new();

        ServiceResult<IReadOnlyList<PRResponseDto>> result = await BuildSut(
                prRepo, itemRepo, excelService: excel, auditService: audit)
            .ImportFromExcelAsync(MakeAdmin(), Stream.Null);

        Assert.True(result.IsSuccess);
        Assert.Equal(2, result.Value!.Count);
        audit.Verify(a => a.LogAsync(
            "PurchaseRequests", It.IsAny<Guid>(), AuditAction.Create,
            null, It.IsAny<object?>(), It.IsAny<CancellationToken>()), Times.Once);
    }
}
