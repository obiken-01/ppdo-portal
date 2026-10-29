using System.Text.Json;
using Moq;
using PPDO.Application.Common;
using PPDO.Application.DTOs.BudgetPlanning;
using PPDO.Application.Services;
using PPDO.Domain.Entities;
using PPDO.Domain.Enums;
using PPDO.Domain.Interfaces;

namespace PPDO.Tests.Application;

/// <summary>
/// The initial PPMP read off the consolidated AIP's procurement items (v1.9.2 — PPDO-263,
/// <c>docs/v1.9/Initial_PPMP_Report_Spec.md</c>). Real <see cref="PermissionService"/>; the AIP, its
/// lines and items are mocks. Test names carry the spec §3 row they pin.
///
/// <para>
/// Three things are pinned: <b>the mapping</b> (quarter placement, grouping by identity, days, free-typed
/// items, unitemised lines left out, totals = Σ item LineTotal), <b>the gate</b> (every group Consolidated,
/// else 409), and <b>the readers</b> (PPDO only: a guest office's head is refused even for their own office,
/// which the initial WFP's rule would allow).
/// </para>
/// </summary>
public sealed class PpmpInitialReportTests
{
    private const int FiscalYear = 2028;
    private const int RecordId = 44;
    private const int Pto = 3, Ppdo = 7;
    private const int PtoCash = 14, PtoRevenue = 15, PpdoPlanning = 1, PpdoAdminDiv = 2;
    private const int Ream = 501, Laptop = 502;

    private readonly Mock<IAipRepository>            _aip      = new();
    private readonly Mock<IAipExpenditureRepository> _lines    = new();
    private readonly Mock<IOfficeRepository>         _offices  = new();
    private readonly Mock<IDivisionRepository>       _divisions = new();
    private readonly Mock<IPriceIndexItemRepository> _prices   = new();

    private readonly List<AipOffice>          _groups = [];
    private readonly List<AipProgram>         _programs = [];
    private readonly List<AipProject>         _projects = [];
    private readonly List<AipActivity>        _activities = [];
    private readonly List<AipExpenditure>     _expenditures = [];
    private readonly List<AipProcurementItem> _items = [];

    private readonly PpmpReportService _sut;

    // ── Callers ──
    private readonly User _ppdoFinance  = Make(Ppdo, true, PpdoPlanning, u => u.OverrideCanManagePpdoAllocation = true);
    private readonly User _ppdoAdmin    = Make(Ppdo, true, null, role: UserRole.Admin);
    private readonly User _ppdoStaff    = Make(Ppdo, true, PpdoPlanning);
    private readonly User _ppdoNoWfp    = Make(Ppdo, true, PpdoPlanning, u => u.OverrideCanAccessBudgetPlanning = false);
    private readonly User _ptoHead      = Make(Pto, false, null, u => u.OverrideCanReviewBudgetPlanning = true);
    private readonly User _ptoAdmin     = Make(Pto, false, null, role: UserRole.Admin);
    private readonly User _superAdmin   = Make(null, false, null, role: UserRole.SuperAdmin);

    public PpmpInitialReportTests()
    {
        _offices.Setup(r => r.GetByIdAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((int id, CancellationToken _) => id switch
            {
                Pto => Office(Pto, "PTO", false),
                Ppdo => Office(Ppdo, "PPDO", true),
                _ => null,
            });
        _divisions.Setup(r => r.GetByIntIdAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((int id, CancellationToken _) => new Division { Id = id, Name = $"Division {id}" });
        _prices.Setup(r => r.GetByIdsAsync(It.IsAny<IReadOnlyList<int>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyList<int> ids, CancellationToken _) => (IReadOnlyList<PriceIndexItem>)new[]
            {
                new PriceIndexItem { Id = Ream, Name = "Bond paper", Unit = "ream", StockCardNo = "OSE-001", Category = "Office Supplies" },
                new PriceIndexItem { Id = Laptop, Name = "Laptop", Unit = "unit", StockCardNo = "ICT-009", Category = "ICT" },
            }.Where(p => ids.Contains(p.Id)).ToList());

        _aip.Setup(r => r.GetLatestByFiscalYearAsync(FiscalYear, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AipRecord { Id = RecordId, FiscalYear = FiscalYear, Status = PlanningStatus.Draft });
        _aip.Setup(r => r.GetOfficesByAipIdNoTrackingAsync(RecordId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => _groups.ToList());
        _aip.Setup(r => r.GetProgramsByOfficeIdsNoTrackingAsync(It.IsAny<IReadOnlyList<int>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyList<int> ids, CancellationToken _) => _programs.Where(p => ids.Contains(p.OfficeId)).ToList());
        _aip.Setup(r => r.GetProjectsByProgramIdsNoTrackingAsync(It.IsAny<IReadOnlyList<int>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyList<int> ids, CancellationToken _) => _projects.Where(p => ids.Contains(p.ProgramId)).ToList());
        _aip.Setup(r => r.GetActivitiesByProjectIdsNoTrackingAsync(It.IsAny<IReadOnlyList<int>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyList<int> ids, CancellationToken _) => _activities.Where(a => ids.Contains(a.ProjectId)).ToList());
        _lines.Setup(r => r.GetByActivityIdsAsync(It.IsAny<IReadOnlyList<int>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyList<int> ids, CancellationToken _) => _expenditures.Where(e => ids.Contains(e.ActivityId)).ToList());
        _lines.Setup(r => r.GetProcurementItemsByExpenditureIdsAsync(It.IsAny<IReadOnlyList<int>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyList<int> ids, CancellationToken _) => _items.Where(i => ids.Contains(i.ExpenditureId)).ToList());

        // PTO: two groups, both Consolidated. PPDO: one, Consolidated.
        Group(201, Pto, "GENERAL"); Group(202, Pto, "SOCIAL"); Group(204, Ppdo, "GENERAL");
        Program(301, 201, "1000-000-1-01-003-001"); Program(302, 202, "3000-000-1-01-003-001");
        Program(304, 204, "1000-000-1-01-010-001");
        Project(401, 301); Project(402, 302); Project(404, 304);

        _sut = new PpmpReportService(
            _aip.Object, new Mock<IWfpRepository>(MockBehavior.Strict).Object,
            new Mock<IWfpExpenditureService>(MockBehavior.Strict).Object,
            _offices.Object, _divisions.Object, _prices.Object, _lines.Object, new PermissionService());
    }

    private Task<ServiceResult<PpmpReportDto>> Report(User caller, int officeId = Pto, int? divisionId = null)
        => _sut.GetInitialReportAsync(officeId, FiscalYear, divisionId, caller);

    private static IEnumerable<PpmpReportItemDto> ItemsOf(PpmpReportDto report)
        => report.FundSourceReports.SelectMany(f => f.Programs).SelectMany(p => p.Projects)
            .SelectMany(p => p.Activities).SelectMany(a => a.Accounts).SelectMany(a => a.Items);

    private static IEnumerable<PpmpReportActivityDto> ActivitiesOf(PpmpReportDto report)
        => report.FundSourceReports.SelectMany(f => f.Programs).SelectMany(p => p.Projects).SelectMany(p => p.Activities);

    // ── The mapping: spec §3 rows 3–10 ────────────────────────────────────────

    [Fact]
    public async Task Row3_ItemInQ1AndQ3_IsOneRow_WithEachQuarterInPlace()
    {
        AipExpenditure line = Line(Activity(501, 401), mooe: 3_750m);
        Item(line, 1, "Bond paper", "ream", 250m, 10m, priceIndexId: Ream);
        Item(line, 3, "Bond paper", "ream", 250m, 5m, priceIndexId: Ream);

        PpmpReportDto report = (await Report(_ppdoFinance)).Value!;

        PpmpReportItemDto item = Assert.Single(ItemsOf(report));
        Assert.Equal(new PpmpReportItemDto("OSE-001", "Office Supplies", "Bond paper", "ream", 250m, 15m, 3_750m,
            10m, 2_500m, 0m, 0m, 5m, 1_250m, 0m, 0m), item);
        Assert.Equal(WfpReportSources.Aip, report.Source);
        Assert.Equal("PTO", report.OfficeCode);
    }

    [Fact]
    public async Task Row4_SamePriceIndexItemOnTwoLinesOfOneAccount_IsOneRow_Summed()
    {
        AipExpenditure first = Line(Activity(501, 401), mooe: 500m);
        AipExpenditure second = Line(_activities[0], mooe: 750m);
        Item(first, 2, "Bond paper", "ream", 250m, 2m, priceIndexId: Ream);
        Item(second, 2, "Bond paper", "ream", 250m, 3m, priceIndexId: Ream);

        PpmpReportItemDto item = Assert.Single(ItemsOf((await Report(_ppdoFinance)).Value!));

        Assert.Equal(5m, item.Qty);
        Assert.Equal(1_250m, item.EstBudget);
        Assert.Equal(1_250m, item.Q2Amount);
    }

    [Fact]
    public async Task Row5_DaysMultiplyIntoTheAmount()
    {
        Item(Line(Activity(501, 401), mooe: 6_000m), 4, "Venue", "day", 1_000m, 2m, days: 3m);

        PpmpReportItemDto item = Assert.Single(ItemsOf((await Report(_ppdoFinance)).Value!));

        Assert.Equal(2m, item.Qty);
        Assert.Equal(6_000m, item.EstBudget);
        Assert.Equal((2m, 6_000m), (item.Q4Qty, item.Q4Amount));
    }

    [Fact]
    public async Task Row6_FreeTypedItem_IsListed_WithStockCardAndCategoryBlank()
    {
        Item(Line(Activity(501, 401), mooe: 100m), 1, "Hand-typed thing", "pc", 100m, 1m);

        PpmpReportItemDto item = Assert.Single(ItemsOf((await Report(_ppdoFinance)).Value!));

        Assert.Equal("Hand-typed thing", item.Description);
        Assert.Null(item.StockCardNo);
        Assert.Null(item.Category);
    }

    [Fact]
    public async Task Row7_LineWithNoItems_IsNotListed()
    {
        AipActivity activity = Activity(501, 401);
        Line(activity, mooe: 90_000m, account: "5-02-01-010");        // no items
        Item(Line(activity, mooe: 100m), 1, "Bond paper", "ream", 100m, 1m, priceIndexId: Ream);

        PpmpReportDto report = (await Report(_ppdoFinance)).Value!;

        PpmpReportAccountDto account = Assert.Single(ActivitiesOf(report).SelectMany(a => a.Accounts));
        Assert.NotEqual("5-02-01-010", account.AccountNumber);
        Assert.Equal(100m, report.FundSourceReports.Sum(f => f.Total));
    }

    [Fact]
    public async Task Row8_PartlyItemisedLine_ShowsItsItemsOnly()
    {
        AipExpenditure line = Line(Activity(501, 401), mooe: 10_000m);
        Item(line, 1, "Bond paper", "ream", 250m, 24m, priceIndexId: Ream);   // 6,000 of 10,000

        PpmpReportDto report = (await Report(_ppdoFinance)).Value!;

        Assert.Equal(6_000m, report.FundSourceReports.Single().Total);
        Assert.Equal(6_000m, ActivitiesOf(report).Single().Total);
    }

    [Fact]
    public async Task Row9_ActivityWithLinesButNoneItemised_DoesNotAppear()
    {
        Line(Activity(501, 401), mooe: 50_000m);
        Item(Line(Activity(502, 401), mooe: 100m), 1, "Bond paper", "ream", 100m, 1m, priceIndexId: Ream);

        PpmpReportActivityDto activity = Assert.Single(ActivitiesOf((await Report(_ppdoFinance)).Value!));

        Assert.Equal("A-502", activity.RefCode);
    }

    [Fact]
    public async Task Row10_OfficeWithNoItemsAtAll_IsOk_WithNoFundBlocks()
    {
        Line(Activity(501, 401), mooe: 50_000m);

        ServiceResult<PpmpReportDto> result = await Report(_ppdoFinance);

        Assert.True(result.IsSuccess);
        Assert.Empty(result.Value!.FundSourceReports);
        Assert.Equal(WfpReportSources.Aip, result.Value.Source);
    }

    [Fact]
    public async Task Decision10_OneBlockPerFund_BlankIsGeneralFund_AndEachTotalIsItsItemsSum()
    {
        AipActivity activity = Activity(501, 401);
        Item(Line(activity, mooe: 500m), 1, "Bond paper", "ream", 250m, 2m, priceIndexId: Ream);
        Item(Line(activity, mooe: 999_999m, fund: "5% LDRRM Fund"), 2, "Laptop", "unit", 60_000m, 2m, priceIndexId: Laptop);

        PpmpReportDto report = (await Report(_ppdoFinance)).Value!;

        Assert.Equal(["GENERAL FUND", "5% LDRRM FUND"], report.FundSourceReports.Select(f => f.FundSourceName));
        Assert.Equal([500m, 120_000m], report.FundSourceReports.Select(f => f.Total));
        // Totals are the items', never the line's amount, and carry no uplift or reserve (decision 12).
        Assert.Equal(_items.Sum(i => i.LineTotal), report.FundSourceReports.Sum(f => f.Total));
    }

    // ── The gate: rows 11 and 12 ──────────────────────────────────────────────

    [Fact]
    public async Task Row11_OneGroupNotConsolidated_Answers409()
    {
        _groups.Single(g => g.Id == 202).WorkflowStatus = AipWorkflowStatus.SubmittedToPpdo;
        Item(Line(Activity(501, 401), mooe: 100m), 1, "Bond paper", "ream", 100m, 1m);

        ServiceResult<PpmpReportDto> result = await Report(_ppdoFinance);

        Assert.Equal(ServiceErrorCode.Conflict, result.Code);
        Assert.Equal("PTO's FY 2028 AIP is not marked as done yet.", result.Error);
    }

    [Fact]
    public async Task Row12_AnEditAfterConsolidation_ShowsOnTheNextRequest()
    {
        AipExpenditure line = Line(Activity(501, 401), mooe: 500m);
        Item(line, 1, "Bond paper", "ream", 250m, 2m, priceIndexId: Ream);
        Assert.Equal(500m, (await Report(_ppdoFinance)).Value!.FundSourceReports.Single().Total);

        AipProcurementItem item = _items.Single();
        item.Qty = 4m;
        item.Recalculate();

        Assert.Equal(1_000m, (await Report(_ppdoFinance)).Value!.FundSourceReports.Single().Total);
    }

    // ── The division: row 13 and 16 ───────────────────────────────────────────

    [Fact]
    public async Task Row13_DivisionFilter_KeepsOnlyActivitiesTaggedWithIt()
    {
        Item(Line(Activity(501, 401, PtoCash), mooe: 100m), 1, "Bond paper", "ream", 100m, 1m);
        Item(Line(Activity(502, 401, PtoRevenue), mooe: 200m), 1, "Folder", "pc", 200m, 1m);
        Item(Line(Activity(503, 401), mooe: 400m), 1, "Pen", "box", 400m, 1m);    // untagged

        PpmpReportDto asked = (await Report(_ppdoFinance, divisionId: PtoCash)).Value!;
        PpmpReportDto whole = (await Report(_ppdoFinance)).Value!;

        Assert.Equal("A-501", Assert.Single(ActivitiesOf(asked)).RefCode);
        Assert.Equal($"Division {PtoCash}", asked.DivisionName);
        Assert.Equal(700m, whole.FundSourceReports.Sum(f => f.Total));
    }

    [Fact]
    public async Task Row16_PpdoStaff_OnPpdosOwnPpmp_AreForcedToTheirDivision()
    {
        Item(Line(Activity(601, 404, PpdoPlanning), mooe: 100m), 1, "Bond paper", "ream", 100m, 1m);
        Item(Line(Activity(602, 404, PpdoAdminDiv), mooe: 200m), 1, "Folder", "pc", 200m, 1m);

        PpmpReportDto report = (await Report(_ppdoStaff, Ppdo, divisionId: PpdoAdminDiv)).Value!;

        Assert.Equal("A-601", Assert.Single(ActivitiesOf(report)).RefCode);
    }

    [Fact]
    public async Task Row16_PpdoStaff_OnAGuestOffice_AreNotNarrowedByTheirPpdoDivision()
    {
        Item(Line(Activity(501, 401, PtoCash), mooe: 100m), 1, "Bond paper", "ream", 100m, 1m);
        Item(Line(Activity(502, 401), mooe: 200m), 1, "Folder", "pc", 200m, 1m);

        Assert.Equal(300m, (await Report(_ppdoStaff)).Value!.FundSourceReports.Sum(f => f.Total));
    }

    // ── The readers: rows 15–18 ───────────────────────────────────────────────

    [Fact]
    public async Task Row15And16_SuperAdminPpdoAdminAndPpdoFinance_ReadAnyOffice()
    {
        Item(Line(Activity(501, 401), mooe: 100m), 1, "Bond paper", "ream", 100m, 1m);

        foreach (User user in new[] { _superAdmin, _ppdoAdmin, _ppdoFinance, _ppdoStaff })
            Assert.True((await Report(user)).IsSuccess, user.FullName);
    }

    [Fact]
    public async Task Row17_PpdoStaffWithoutWfpAccess_AreForbidden()
    {
        Assert.Equal(ServiceErrorCode.Forbidden, (await Report(_ppdoNoWfp)).Code);
    }

    /// <summary>
    /// Red-tested: with the service's <c>CanAccessWfp</c> check removed, both callers read PTO's PPMP
    /// (the office scope alone, like the initial WFP's rule, would let a head read their own office).
    /// </summary>
    [Fact]
    public async Task Row18_AGuestOfficesHead_IsForbidden_EvenForTheirOwnOffice()
    {
        Item(Line(Activity(501, 401), mooe: 100m), 1, "Bond paper", "ream", 100m, 1m);

        foreach (User head in new[] { _ptoHead, _ptoAdmin })
        {
            ServiceResult<PpmpReportDto> result = await Report(head, Pto);
            Assert.Equal(ServiceErrorCode.Forbidden, result.Code);
            Assert.Null(result.Value);
        }
        _aip.Verify(r => r.GetLatestByFiscalYearAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    // ── Queries and the wire ──────────────────────────────────────────────────

    [Fact]
    public async Task Reads_AFixedNumberOfBatchedQueries_NeverOnePerActivity()
    {
        for (int id = 501; id <= 505; id++)
            Item(Line(Activity(id, 401), mooe: 100m), 1, "Bond paper", "ream", 100m, 1m, priceIndexId: Ream);

        await Report(_ppdoFinance);

        _lines.Verify(r => r.GetByActivityIdsAsync(It.IsAny<IReadOnlyList<int>>(), It.IsAny<CancellationToken>()), Times.Once);
        _lines.Verify(r => r.GetProcurementItemsByExpenditureIdsAsync(It.IsAny<IReadOnlyList<int>>(), It.IsAny<CancellationToken>()), Times.Once);
        _prices.Verify(r => r.GetByIdsAsync(It.IsAny<IReadOnlyList<int>>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public void Source_IsOnTheWire_ForBothSources()
    {
        JsonSerializerOptions json = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

        Assert.Contains("\"source\":\"Wfp\"", JsonSerializer.Serialize(new PpmpReportDto(2027, "PPDO", "PPDO", null, []), json));
        Assert.Contains("\"source\":\"Aip\"",
            JsonSerializer.Serialize(new PpmpReportDto(2028, "PTO", "PTO", null, [], WfpReportSources.Aip), json));
    }

    // ── Fixture helpers ───────────────────────────────────────────────────────

    private void Group(int id, int officeId, string sector) => _groups.Add(new AipOffice
    {
        Id = id, AipRecordId = RecordId, OfficeId = officeId, Sector = sector, RefCode = $"R{id}", Name = $"G{id}",
        WorkflowStatus = AipWorkflowStatus.Consolidated,
    });

    private void Program(int id, int groupId, string refCode)
        => _programs.Add(new AipProgram { Id = id, OfficeId = groupId, RefCode = refCode, Name = $"Program {id}", FunctionBand = "CORE" });

    private void Project(int id, int programId)
        => _projects.Add(new AipProject { Id = id, ProgramId = programId, RefCode = $"P-{id}", Name = $"Project {id}" });

    private AipActivity Activity(int id, int projectId, int? division = null)
    {
        AipActivity a = new() { Id = id, ProjectId = projectId, RefCode = $"A-{id}", Name = $"Activity {id}", DivisionId = division };
        _activities.Add(a);
        return a;
    }

    private AipExpenditure Line(AipActivity activity, decimal mooe, string? fund = null, string account = "5-02-03-010")
    {
        AipExpenditure e = new()
        {
            Id = 900 + _expenditures.Count, ActivityId = activity.Id,
            AccountNumberSnapshot = account, AccountTitleSnapshot = $"Account {account}",
            FundingSourceNameSnapshot = fund, Mooe = mooe,
        };
        e.Recalculate();
        _expenditures.Add(e);
        return e;
    }

    private void Item(AipExpenditure line, int periodNo, string name, string unit, decimal price, decimal qty,
        decimal days = 1m, int? priceIndexId = null)
    {
        AipProcurementItem item = new()
        {
            Id = 7000 + _items.Count, ExpenditureId = line.Id, PeriodNo = periodNo, PriceIndexItemId = priceIndexId,
            Name = name, Unit = unit, UnitPrice = price, Qty = qty, NumberOfDays = days,
        };
        item.Recalculate();
        _items.Add(item);
    }

    private static Office Office(int id, string code, bool host) => new()
    {
        Id = id, OfficeCode = code, OfficeName = code, IsHostOffice = host, IsActive = true, OfficeRefCode = $"01-{id:000}",
    };

    private static User Make(int? officeId, bool host, int? divisionId, Action<User>? tweak = null, UserRole role = UserRole.Staff)
    {
        User u = new()
        {
            Id = Guid.NewGuid(), FullName = $"{role}@{officeId}/{divisionId}", Role = role,
            OfficeId = officeId, Office = officeId is int o ? Office(o, $"O{o}", host) : null,
            DivisionId = divisionId,
            Division = divisionId is int d ? new Division { Id = d, OfficeId = officeId ?? 0, Name = $"D{d}", CanAccessBudgetPlanning = true } : null,
        };
        tweak?.Invoke(u);
        return u;
    }
}
