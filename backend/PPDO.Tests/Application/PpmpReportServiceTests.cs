using Moq;
using PPDO.Application.Common;
using PPDO.Application.DTOs.BudgetPlanning;
using PPDO.Application.Services;
using PPDO.Domain.Entities;
using PPDO.Domain.Interfaces;

namespace PPDO.Tests.Application;

/// <summary>
/// Unit tests for <see cref="PpmpReportService"/> (v1.5 — RAL-183). Covers: not-found paths;
/// item-grained grouping across periods with the quarter split; procurement-only filtering
/// (Non-Procurement skipped, Combined contributes only its items); account section total = sum of
/// items (Q11); per-fund-source grouping (Q4); the live Price-Index join for Stock Card No. /
/// Category (Q15, incl. free-typed → null); and division scoping (RAL-136). All repositories are
/// mocked; procurement items are canned directly (the qty×price×days math is
/// WfpExpenditureCalculator's, not this service's).
/// </summary>
public sealed class PpmpReportServiceTests
{
    private const int FiscalYear = 2027;

    private static Office MakeOffice(int id, string code, string? refCode) => new()
    {
        Id = id, OfficeCode = code, OfficeName = $"{code} Office", OfficeRefCode = refCode,
        IsActive = true, CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow,
    };

    private static AipRecord MakeAip(int id, string status) => new()
    {
        Id = id, FiscalYear = FiscalYear, Status = status, EntrySource = "Manual",
        UploadedById = Guid.NewGuid(), UploadedAt = DateTime.UtcNow,
    };

    private static AipOffice MakeAipOffice(int id, int aipRecordId, string refCode) => new()
    {
        Id = id, AipRecordId = aipRecordId, RefCode = refCode, Name = "Office", Sector = "General",
    };

    private static AipProgram MakeProgram(int id, int officeId, string refCode) => new()
    {
        Id = id, OfficeId = officeId, RefCode = refCode, Name = $"Program {refCode}", FunctionBand = "CORE",
    };

    private static AipProject MakeProject(int id, int programId, string refCode) => new()
    {
        Id = id, ProgramId = programId, RefCode = refCode, Name = $"Project {refCode}",
    };

    private static AipActivity MakeActivity(int id, int projectId, string refCode) => new()
    {
        Id = id, ProjectId = projectId, RefCode = refCode, Name = $"Activity {refCode}",
    };

    private static PriceIndexItem MakePriceItem(int id, string name, string unit, string? stockCardNo, string? category) => new()
    {
        Id = id, Name = name, Unit = unit, UnitPrice = 0m, StockCardNo = stockCardNo, Category = category,
        IsActive = true, CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow,
    };

    private static WfpProcurementItemDto Item(
        int periodNo, int? priceIndexItemId, string name, string unit, decimal unitPrice, decimal qty,
        decimal numberOfDays, decimal lineTotal) =>
        new(periodNo, priceIndexItemId, name, unit, unitPrice, qty, numberOfDays, lineTotal);

    /// <summary>A procurement/combined expenditure carrying procurement items.</summary>
    private static WfpExpenditureDto Expenditure(
        int id, int wfpActivityId, string accountNumber, string accountTitle,
        string frequency, IReadOnlyList<WfpProcurementItemDto> items,
        string nature = WfpNature.Procurement, string? fundSourceName = null, int? annualQuarterChoice = null) => new(
        id, wfpActivityId, 1, accountNumber, accountTitle,
        nature, frequency, null, null, fundSourceName,
        false, 0, annualQuarterChoice,
        0, 0, 0, 0, 0, 0,
        [], items);

    // ── Fixture ─────────────────────────────────────────────────────────────────

    private sealed record Fixture(
        PpmpReportService Sut,
        Mock<IAipRepository> AipRepo,
        Mock<IWfpRepository> WfpRepo,
        Mock<IWfpExpenditureService> Expenditures,
        List<Office> Offices,
        List<Division> Divisions,
        List<PriceIndexItem> PriceItems);

    private static Fixture Build()
    {
        Mock<IAipRepository> aipRepo = new();
        Mock<IWfpRepository> wfpRepo = new();
        Mock<IWfpExpenditureService> expenditures = new();
        Mock<IRepository<Office>> officeRepo = new();
        Mock<IRepository<Division>> divisionRepo = new();
        Mock<IPriceIndexItemRepository> priceRepo = new();

        List<Office> offices = [];
        List<Division> divisions = [];
        List<PriceIndexItem> priceItems = [];

        officeRepo.Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => (IReadOnlyList<Office>)offices);
        divisionRepo.Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => (IReadOnlyList<Division>)divisions);
        priceRepo.Setup(r => r.GetByIdsAsync(It.IsAny<IReadOnlyList<int>>(), It.IsAny<CancellationToken>()))
            .Returns((IReadOnlyList<int> ids, CancellationToken _) =>
                Task.FromResult((IReadOnlyList<PriceIndexItem>)priceItems.Where(p => ids.Contains(p.Id)).ToList()));

        aipRepo.Setup(r => r.GetLatestByFiscalYearAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((AipRecord?)null);
        aipRepo.Setup(r => r.GetProgramsByOfficeIdsAsync(It.IsAny<IReadOnlyList<int>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyList<AipProgram>)[]);
        aipRepo.Setup(r => r.GetProjectsByProgramIdsAsync(It.IsAny<IReadOnlyList<int>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyList<AipProject>)[]);
        aipRepo.Setup(r => r.GetActivitiesByProjectIdsAsync(It.IsAny<IReadOnlyList<int>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyList<AipActivity>)[]);
        wfpRepo.Setup(r => r.GetActivitiesByWfpIdAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyList<WfpActivity>)[]);

        // Delegate the batched reads to the per-id setups each test configures (same trick as
        // WfpReportServiceTests), so a test only stubs the singular methods.
        wfpRepo.Setup(r => r.GetActivitiesByWfpIdsAsync(It.IsAny<IReadOnlyList<int>>(), It.IsAny<CancellationToken>()))
            .Returns(async (IReadOnlyList<int> wfpIds, CancellationToken ct) =>
            {
                List<WfpActivity> all = [];
                foreach (int id in wfpIds) all.AddRange(await wfpRepo.Object.GetActivitiesByWfpIdAsync(id, ct));
                return all;
            });
        expenditures.Setup(e => e.GetByActivityIdsAsync(It.IsAny<IReadOnlyList<int>>(), It.IsAny<CancellationToken>()))
            .Returns(async (IReadOnlyList<int> actIds, CancellationToken ct) =>
            {
                Dictionary<int, IReadOnlyList<WfpExpenditureDto>> map = [];
                foreach (int id in actIds)
                {
                    IReadOnlyList<WfpExpenditureDto> e = await expenditures.Object.GetByActivityIdAsync(id, ct);
                    if (e is { Count: > 0 }) map[id] = e;
                }
                return map;
            });

        PpmpReportService sut = new(
            aipRepo.Object, wfpRepo.Object, expenditures.Object,
            officeRepo.Object, divisionRepo.Object, priceRepo.Object);

        return new Fixture(sut, aipRepo, wfpRepo, expenditures, offices, divisions, priceItems);
    }

    /// <summary>Wires the standard 1-office / 1-program / 1-project / 1-activity hierarchy. `divisionId` must match what the test passes to GetReportAsync (null = office-consolidated).</summary>
    private static void SeedHierarchy(Fixture f, int? divisionId = null)
    {
        f.Offices.Add(MakeOffice(1, "PPDO", "013"));
        f.AipRepo.Setup(r => r.GetLatestByFiscalYearAsync(FiscalYear, It.IsAny<CancellationToken>()))
            .ReturnsAsync(MakeAip(100, PlanningStatus.Draft));
        f.AipRepo.Setup(r => r.GetOfficesByAipIdAsync(100, It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyList<AipOffice>)[MakeAipOffice(1, 100, "1000-000-1-01-010-013")]);
        f.AipRepo.Setup(r => r.GetProgramsByOfficeIdsAsync(It.IsAny<IReadOnlyList<int>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyList<AipProgram>)[MakeProgram(1, 1, "1000-000-1-01-010-002")]);
        f.AipRepo.Setup(r => r.GetProjectsByProgramIdsAsync(It.IsAny<IReadOnlyList<int>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyList<AipProject>)[MakeProject(10, 1, "1000-000-1-01-010-002-001")]);
        f.AipRepo.Setup(r => r.GetActivitiesByProjectIdsAsync(It.IsAny<IReadOnlyList<int>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyList<AipActivity>)[MakeActivity(100, 10, "1000-000-1-01-010-002-001-001")]);
        f.WfpRepo.Setup(r => r.GetFilteredAsync(100, 1, divisionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyList<WfpRecord>)[
                new WfpRecord { Id = 1000, AipRecordId = 100, OfficeId = 1, DivisionId = 1, FiscalYear = FiscalYear, Status = PlanningStatus.Draft },
            ]);
        f.WfpRepo.Setup(r => r.GetActivitiesByWfpIdAsync(1000, It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyList<WfpActivity>)[new WfpActivity { Id = 500, WfpId = 1000, AipActivityId = 100 }]);
    }

    // ── Not-found paths ───────────────────────────────────────────────────────

    [Fact]
    public async Task GetReportAsync_UnknownOffice_ReturnsNotFound()
    {
        Fixture f = Build();
        ServiceResult<PpmpReportDto> result = await f.Sut.GetReportAsync(999, FiscalYear);
        Assert.Equal(ServiceErrorCode.NotFound, result.Code);
    }

    [Fact]
    public async Task GetReportAsync_OfficeWithNoRefCode_ReturnsNotFound()
    {
        Fixture f = Build();
        f.Offices.Add(MakeOffice(1, "PPDO", refCode: null));
        ServiceResult<PpmpReportDto> result = await f.Sut.GetReportAsync(1, FiscalYear);
        Assert.Equal(ServiceErrorCode.NotFound, result.Code);
    }

    [Fact]
    public async Task GetReportAsync_NoAipForFiscalYear_ReturnsNotFound()
    {
        Fixture f = Build();
        f.Offices.Add(MakeOffice(1, "PPDO", "013"));
        ServiceResult<PpmpReportDto> result = await f.Sut.GetReportAsync(1, FiscalYear);
        Assert.Equal(ServiceErrorCode.NotFound, result.Code);
    }

    // ── Item grouping + quarter split ───────────────────────────────────────────

    [Fact]
    public async Task GetReportAsync_GroupsItemAcrossQuarterlyPeriods_SplitsIntoQuarters()
    {
        Fixture f = Build();
        SeedHierarchy(f);
        // Same item across all 4 quarterly periods, different qty each quarter.
        f.Expenditures.Setup(e => e.GetByActivityIdAsync(500, It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyList<WfpExpenditureDto>)[
                Expenditure(1, 500, "5-02-99-030", "Representation Expenses", WfpFrequency.Quarterly, [
                    Item(1, null, "Snacks SET A", "set", 150m, 35m, 1m, 5250m),
                    Item(2, null, "Snacks SET A", "set", 150m, 30m, 1m, 4500m),
                    Item(3, null, "Snacks SET A", "set", 150m, 30m, 1m, 4500m),
                    Item(4, null, "Snacks SET A", "set", 150m, 35m, 1m, 5250m),
                ]),
            ]);

        ServiceResult<PpmpReportDto> result = await f.Sut.GetReportAsync(1, FiscalYear);

        Assert.True(result.IsSuccess);
        PpmpReportItemDto item = result.Value!
            .FundSourceReports.Single()
            .Programs.Single().Projects.Single().Activities.Single()
            .Accounts.Single().Items.Single();
        Assert.Equal("Snacks SET A", item.Description);
        Assert.Equal(130m, item.Qty);                 // 35+30+30+35
        Assert.Equal(19500m, item.EstBudget);         // 5250+4500+4500+5250
        Assert.Equal(35m, item.Q1Qty); Assert.Equal(5250m, item.Q1Amount);
        Assert.Equal(30m, item.Q2Qty); Assert.Equal(4500m, item.Q2Amount);
        Assert.Equal(30m, item.Q3Qty); Assert.Equal(4500m, item.Q3Amount);
        Assert.Equal(35m, item.Q4Qty); Assert.Equal(5250m, item.Q4Amount);
    }

    [Fact]
    public async Task GetReportAsync_MonthlyPeriods_MapIntoQuarters()
    {
        Fixture f = Build();
        SeedHierarchy(f);
        // Monthly: period 2 -> Q1, period 5 -> Q2, period 8 -> Q3, period 11 -> Q4.
        f.Expenditures.Setup(e => e.GetByActivityIdAsync(500, It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyList<WfpExpenditureDto>)[
                Expenditure(1, 500, "5-02-03-010", "Office Supplies", WfpFrequency.Monthly, [
                    Item(2, null, "Bond Paper", "ream", 500m, 1m, 1m, 500m),
                    Item(5, null, "Bond Paper", "ream", 500m, 2m, 1m, 1000m),
                    Item(8, null, "Bond Paper", "ream", 500m, 3m, 1m, 1500m),
                    Item(11, null, "Bond Paper", "ream", 500m, 4m, 1m, 2000m),
                ]),
            ]);

        ServiceResult<PpmpReportDto> result = await f.Sut.GetReportAsync(1, FiscalYear);

        PpmpReportItemDto item = result.Value!
            .FundSourceReports.Single().Programs.Single().Projects.Single()
            .Activities.Single().Accounts.Single().Items.Single();
        Assert.Equal(500m, item.Q1Amount);
        Assert.Equal(1000m, item.Q2Amount);
        Assert.Equal(1500m, item.Q3Amount);
        Assert.Equal(2000m, item.Q4Amount);
    }

    [Fact]
    public async Task GetReportAsync_AnnualExpenditure_ChargesToChosenQuarter()
    {
        Fixture f = Build();
        SeedHierarchy(f);
        f.Expenditures.Setup(e => e.GetByActivityIdAsync(500, It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyList<WfpExpenditureDto>)[
                Expenditure(1, 500, "5-02-99-050", "Rent", WfpFrequency.Annual, [
                    Item(1, null, "Venue", "lot", 10000m, 1m, 1m, 10000m),
                ], annualQuarterChoice: 3),
            ]);

        ServiceResult<PpmpReportDto> result = await f.Sut.GetReportAsync(1, FiscalYear);

        PpmpReportItemDto item = result.Value!
            .FundSourceReports.Single().Programs.Single().Projects.Single()
            .Activities.Single().Accounts.Single().Items.Single();
        Assert.Equal(0m, item.Q1Amount);
        Assert.Equal(10000m, item.Q3Amount);
    }

    // ── Procurement-only filter (Q8) ────────────────────────────────────────────

    [Fact]
    public async Task GetReportAsync_SkipsNonProcurementExpenditure()
    {
        Fixture f = Build();
        SeedHierarchy(f);
        f.Expenditures.Setup(e => e.GetByActivityIdAsync(500, It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyList<WfpExpenditureDto>)[
                // Non-procurement: no items → must not appear at all.
                new WfpExpenditureDto(9, 500, 1, "5-02-01-010", "Travelling", WfpNature.NonProcurement,
                    WfpFrequency.Quarterly, null, null, null, false, 0, null, 0, 0, 0, 0, 0, 0, [], []),
                Expenditure(1, 500, "5-02-03-010", "Office Supplies", WfpFrequency.Quarterly, [
                    Item(1, null, "Bond Paper", "ream", 500m, 2m, 1m, 1000m),
                ]),
            ]);

        ServiceResult<PpmpReportDto> result = await f.Sut.GetReportAsync(1, FiscalYear);

        IReadOnlyList<PpmpReportAccountDto> accounts = result.Value!
            .FundSourceReports.Single().Programs.Single().Projects.Single().Activities.Single().Accounts;
        Assert.Single(accounts);
        Assert.Equal("5-02-03-010", accounts[0].AccountNumber);
    }

    [Fact]
    public async Task GetReportAsync_CombinedExpenditure_ContributesOnlyItsProcurementItems()
    {
        Fixture f = Build();
        SeedHierarchy(f);
        // Combined = typed periods + procurement items. Only the items should surface; the typed
        // (non-proc) portion is ignored, so the account total is the items' total, not Net/Total.
        f.Expenditures.Setup(e => e.GetByActivityIdAsync(500, It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyList<WfpExpenditureDto>)[
                new WfpExpenditureDto(1, 500, 1, "5-02-03-010", "Office Supplies", WfpNature.Combined,
                    WfpFrequency.Quarterly, null, null, null, false, 0, null, 0, 0, 0, 0, 9999, 9999,
                    [new WfpExpenditurePeriodDto(1, 9999)],
                    [Item(1, null, "Bond Paper", "ream", 500m, 2m, 1m, 1000m)]),
            ]);

        ServiceResult<PpmpReportDto> result = await f.Sut.GetReportAsync(1, FiscalYear);

        PpmpReportAccountDto account = result.Value!
            .FundSourceReports.Single().Programs.Single().Projects.Single().Activities.Single().Accounts.Single();
        Assert.Equal(1000m, account.Total);           // items only, not the 9999 typed period
        Assert.Equal(1000m, account.Items.Single().EstBudget);
    }

    // ── Account section total (Q11) ─────────────────────────────────────────────

    [Fact]
    public async Task GetReportAsync_AccountTotal_IsSumOfItems()
    {
        Fixture f = Build();
        SeedHierarchy(f);
        f.Expenditures.Setup(e => e.GetByActivityIdAsync(500, It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyList<WfpExpenditureDto>)[
                Expenditure(1, 500, "5-02-03-010", "Office Supplies", WfpFrequency.Quarterly, [
                    Item(1, null, "Bond Paper", "ream", 500m, 2m, 1m, 1000m),
                    Item(1, null, "Ballpen", "pc", 10m, 5m, 1m, 50m),
                ]),
            ]);

        ServiceResult<PpmpReportDto> result = await f.Sut.GetReportAsync(1, FiscalYear);

        PpmpReportFundSourceDto fund = result.Value!.FundSourceReports.Single();
        PpmpReportAccountDto account = fund.Programs.Single().Projects.Single().Activities.Single().Accounts.Single();
        Assert.Equal(1050m, account.Total);
        Assert.Equal(1050m, fund.Total);              // rolls all the way up
    }

    // ── Per-fund grouping (Q4) ──────────────────────────────────────────────────

    [Fact]
    public async Task GetReportAsync_SplitsItemsByFundSource_IntoSeparateBlocks()
    {
        Fixture f = Build();
        SeedHierarchy(f);
        f.Expenditures.Setup(e => e.GetByActivityIdAsync(500, It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyList<WfpExpenditureDto>)[
                Expenditure(1, 500, "5-02-03-010", "Office Supplies", WfpFrequency.Quarterly,
                    [Item(1, null, "Bond Paper", "ream", 500m, 2m, 1m, 1000m)], fundSourceName: null), // → GENERAL FUND
                Expenditure(2, 500, "5-02-03-010", "Office Supplies", WfpFrequency.Quarterly,
                    [Item(1, null, "First Aid Kit", "box", 800m, 1m, 1m, 800m)], fundSourceName: "5% LDRRM Fund"),
            ]);

        ServiceResult<PpmpReportDto> result = await f.Sut.GetReportAsync(1, FiscalYear);

        IReadOnlyList<PpmpReportFundSourceDto> funds = result.Value!.FundSourceReports;
        Assert.Equal(2, funds.Count);
        Assert.Equal("GENERAL FUND", funds[0].FundSourceName); // default fund sorts first
        Assert.Equal("5% LDRRM FUND", funds[1].FundSourceName);
    }

    // ── Live Price-Index join (Q15) ─────────────────────────────────────────────

    [Fact]
    public async Task GetReportAsync_JoinsStockCardNoAndCategory_LiveFromPriceIndex()
    {
        Fixture f = Build();
        SeedHierarchy(f);
        f.PriceItems.Add(MakePriceItem(7, "Bond paper A4 80gsm", "ream", "OSE-1714343441", "Office Supplies Expenses"));
        f.Expenditures.Setup(e => e.GetByActivityIdAsync(500, It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyList<WfpExpenditureDto>)[
                Expenditure(1, 500, "5-02-03-010", "Office Supplies", WfpFrequency.Quarterly, [
                    Item(1, 7, "Bond paper A4 80gsm", "ream", 494m, 2m, 1m, 988m),   // linked → joined
                    Item(1, null, "Free-typed thing", "pc", 5m, 1m, 1m, 5m),         // free-typed → blank
                ]),
            ]);

        ServiceResult<PpmpReportDto> result = await f.Sut.GetReportAsync(1, FiscalYear);

        IReadOnlyList<PpmpReportItemDto> items = result.Value!
            .FundSourceReports.Single().Programs.Single().Projects.Single().Activities.Single().Accounts.Single().Items;
        PpmpReportItemDto linked = items.Single(i => i.Description == "Bond paper A4 80gsm");
        Assert.Equal("OSE-1714343441", linked.StockCardNo);
        Assert.Equal("Office Supplies Expenses", linked.Category);
        PpmpReportItemDto free = items.Single(i => i.Description == "Free-typed thing");
        Assert.Null(free.StockCardNo);
        Assert.Null(free.Category);
    }

    // ── Division scope (RAL-136) ────────────────────────────────────────────────

    [Fact]
    public async Task GetReportAsync_WithDivisionId_ScopesQueryAndSetsDivisionName()
    {
        Fixture f = Build();
        SeedHierarchy(f, divisionId: 5);
        f.Divisions.Add(new Division { Id = 5, Name = "Planning Division", OfficeId = 1, IsActive = true });
        f.Expenditures.Setup(e => e.GetByActivityIdAsync(500, It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyList<WfpExpenditureDto>)[
                Expenditure(1, 500, "5-02-03-010", "Office Supplies", WfpFrequency.Quarterly,
                    [Item(1, null, "Bond Paper", "ream", 500m, 2m, 1m, 1000m)]),
            ]);

        ServiceResult<PpmpReportDto> result = await f.Sut.GetReportAsync(1, FiscalYear, divisionId: 5);

        Assert.True(result.IsSuccess);
        Assert.Equal("Planning Division", result.Value!.DivisionName);
        f.WfpRepo.Verify(r => r.GetFilteredAsync(100, 1, 5, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task GetReportAsync_ConsolidatedView_HasNullDivisionName()
    {
        Fixture f = Build();
        SeedHierarchy(f, divisionId: null);
        f.Expenditures.Setup(e => e.GetByActivityIdAsync(500, It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyList<WfpExpenditureDto>)[
                Expenditure(1, 500, "5-02-03-010", "Office Supplies", WfpFrequency.Quarterly,
                    [Item(1, null, "Bond Paper", "ream", 500m, 2m, 1m, 1000m)]),
            ]);

        ServiceResult<PpmpReportDto> result = await f.Sut.GetReportAsync(1, FiscalYear);

        Assert.True(result.IsSuccess);
        Assert.Null(result.Value!.DivisionName);
    }
}
