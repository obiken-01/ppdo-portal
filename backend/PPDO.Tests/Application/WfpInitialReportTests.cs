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
/// The initial WFP read off the consolidated AIP (v1.9.0 — PPDO-224, <c>Initial_WFP_Report_Spec.md</c>).
/// Real <see cref="PermissionService"/>; the AIP and its lines are mocks.
///
/// <para>
/// Three things are pinned here: <b>the money</b> (each fund block's Net equals the AIP's base total for
/// that fund, Q1–Q4 sum to it, reserve only on MOOE/CO), <b>the gate</b> (every group Consolidated, else
/// 409), and <b>the scope</b> (PPDO reads every office, an office only its own, another office's id 404).
/// The quarter arithmetic itself is <see cref="InitialWfpQuartersTests"/>.
/// </para>
/// </summary>
public sealed class WfpInitialReportTests
{
    private const int FiscalYear = 2028;
    private const int RecordId = 44;
    private const int Pto = 3, Ppdo = 7, Opa = 15, Gso = 10;
    private const int PtoCash = 14, PtoRevenue = 15, PpdoPlanning = 1;
    private const int Ps = 1, Mooe = 2, Co = 3;

    private readonly Mock<IAipRepository>            _aip      = new();
    private readonly Mock<IAipExpenditureRepository> _lines    = new();
    private readonly Mock<IOfficeRepository>         _offices  = new();
    private readonly Mock<IRepository<Account>>      _accounts = new();

    private readonly List<AipOffice>          _groups = [];
    private readonly List<AipProgram>         _programs = [];
    private readonly List<AipProject>         _projects = [];
    private readonly List<AipActivity>        _activities = [];
    private readonly List<AipExpenditure>     _expenditures = [];
    private readonly List<AipProcurementItem> _items = [];

    private readonly WfpReportService _sut;

    // ── Callers ──
    private readonly User _ppdoFinance   = Make(Ppdo, true, PpdoPlanning, u => u.OverrideCanManagePpdoAllocation = true);
    private readonly User _ppdoAdmin     = Make(Ppdo, true, null, role: UserRole.Admin);
    private readonly User _ppdoUnassigned = Make(Ppdo, true, null, u => u.OverrideCanAccessBudgetPlanning = true);
    // ↩️ PPDO-240: a guest office's initial WFP is its department heads' — Admin, or Staff holding a
    // review grant. The plain encoders below read nothing.
    private readonly User _ptoCash       = Make(Pto, false, PtoCash, u => u.OverrideCanReviewBudgetPlanning = true);
    private readonly User _ptoUnassigned = Make(Pto, false, null, u => u.OverrideCanReviewBudgetPlanning = true);
    private readonly User _ptoCashEncoder = Make(Pto, false, PtoCash);
    private readonly User _ptoUnassignedEncoder = Make(Pto, false, null);
    private readonly User _ptoReviewer   = Make(Pto, false, PtoCash, u => u.OverrideCanReviewAllOffices = true);
    private readonly User _ptoAdmin      = Make(Pto, false, null, role: UserRole.Admin);
    private readonly User _ptoNoBudget   = Make(Pto, false, null, u => u.OverrideCanAccessBudgetPlanning = false);
    private readonly User _gsoReviewer   = Make(Gso, false, null, u => u.OverrideCanReviewAllOffices = true);
    private readonly User _noOffice      = Make(null, false, null);
    private readonly User _superAdmin    = Make(null, false, null, role: UserRole.SuperAdmin);

    public WfpInitialReportTests()
    {
        _offices.Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>())).ReturnsAsync(() => (IReadOnlyList<Office>)
        [
            Office(Pto, "PTO", false), Office(Ppdo, "PPDO", true), Office(Opa, "OPA", false), Office(Gso, "GSO", false),
        ]);
        _offices.Setup(r => r.GetByIdAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .Returns(async (int id, CancellationToken ct) => (await _offices.Object.GetAllAsync(ct)).FirstOrDefault(o => o.Id == id));

        _accounts.Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>())).ReturnsAsync((IReadOnlyList<Account>)
        [
            new Account { Id = Ps,   AccountNumber = "5-01-01-010", AccountTitle = "Salaries", ExpenseClass = "PS" },
            new Account { Id = Mooe, AccountNumber = "5-02-03-010", AccountTitle = "Office Supplies", ExpenseClass = "MOOE" },
            new Account { Id = Co,   AccountNumber = "1-07-05-030", AccountTitle = "ICT Equipment", ExpenseClass = "CO" },
        ]);

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

        // PTO: two groups (decision 21), both Consolidated. OPA: one, Consolidated. PPDO: one, Consolidated.
        Group(201, Pto, "GENERAL"); Group(202, Pto, "SOCIAL"); Group(203, Opa, "ECONOMIC"); Group(204, Ppdo, "GENERAL");
        Program(301, 201, "1000-000-1-01-003-001", "CORE");
        Program(302, 202, "3000-000-1-01-003-001", "SUPPORT");
        Program(303, 203, "8000-000-1-01-016-001", "CORE");
        Program(304, 204, "1000-000-1-01-010-001", "CORE");
        Project(401, 301); Project(402, 302); Project(403, 303); Project(404, 304);

        _sut = new WfpReportService(
            _aip.Object, new Mock<IWfpRepository>(MockBehavior.Strict).Object,
            new Mock<IWfpExpenditureService>(MockBehavior.Strict).Object,
            _offices.Object, _accounts.Object, _lines.Object, new PermissionService(),
            new PassThroughConfigCache(offices: _offices.Object));
    }

    private Task<ServiceResult<WfpReportDto>> Report(User caller, int officeId = Pto, int? divisionId = null)
        => _sut.GetInitialReportAsync(officeId, FiscalYear, divisionId, caller);

    private static WfpReportRowDto OnlyRow(WfpReportDto report)
        => Assert.Single(report.FundSourceReports.SelectMany(f => f.Sections).SelectMany(s => s.Programs)
            .SelectMany(p => p.Projects).SelectMany(p => p.Activities).SelectMany(a => a.ExpenseClasses).SelectMany(c => c.Rows));

    private static IEnumerable<WfpReportRowDto> Rows(WfpReportDto report)
        => report.FundSourceReports.SelectMany(f => f.Sections).SelectMany(s => s.Programs)
            .SelectMany(p => p.Projects).SelectMany(p => p.Activities).SelectMany(a => a.ExpenseClasses).SelectMany(c => c.Rows);

    // ── The money: spec §3 cases 3–7 ──────────────────────────────────────────

    [Fact]
    public async Task JanToJun_MooeLineWithoutItems_SplitsOverQ1Q2_WithTheReserveOnTop()   // case 3
    {
        Line(Activity(501, 401, "January", "June"), Mooe, mooe: 120_000m);

        WfpReportDto report = (await Report(_ppdoFinance)).Value!;

        WfpReportRowDto row = OnlyRow(report);
        Assert.Equal(new WfpReportAmountsDto(132_000m, 12_000m, 120_000m, 60_000m, 60_000m, 0m, 0m, 120_000m), row.Amounts);
        Assert.Equal(WfpReportSources.Aip, report.Source);
        Assert.Equal("PTO", report.OfficeCode);
    }

    [Fact]
    public async Task MarToMay_PutsTheRemainderInQ2()   // case 4
    {
        Line(Activity(501, 401, "March", "May"), Mooe, mooe: 100_000m);

        WfpReportAmountsDto a = OnlyRow((await Report(_ppdoFinance)).Value!).Amounts;

        Assert.Equal((33_333.33m, 66_666.67m, 0m, 0m), (a.Q1, a.Q2, a.Q3, a.Q4));
    }

    [Fact]
    public async Task ItemisedLine_PutsEachItemInItsQuarter_AsProcurement()   // case 5
    {
        AipExpenditure line = Line(Activity(501, 401, "January", "March"), Co, co: 50_000m);
        Item(line, 2, 20_000m); Item(line, 4, 30_000m);

        WfpReportRowDto row = OnlyRow((await Report(_ppdoFinance)).Value!);

        Assert.Equal((0m, 20_000m, 0m, 30_000m), (row.Amounts.Q1, row.Amounts.Q2, row.Amounts.Q3, row.Amounts.Q4));
        Assert.Equal(5_000m, row.Amounts.Reserved);   // CO carries the reserve
        Assert.Equal(WfpNature.Procurement, row.Nature);
    }

    [Fact]
    public async Task PsLine_IsSpread_WithNoReserve()   // case 6, decision 7
    {
        Line(Activity(501, 401, "January", "December"), Ps, ps: 400_000m);

        WfpReportAmountsDto a = OnlyRow((await Report(_ppdoFinance)).Value!).Amounts;

        Assert.Equal(new WfpReportAmountsDto(400_000m, 0m, 400_000m, 100_000m, 100_000m, 100_000m, 100_000m, 400_000m), a);
    }

    [Fact]
    public async Task NoStartOrEnd_SpreadsOverTheWholeYear()   // case 7
    {
        Line(Activity(501, 401, null, null), Mooe, mooe: 1_000m);

        WfpReportAmountsDto a = OnlyRow((await Report(_ppdoFinance)).Value!).Amounts;

        Assert.Equal((250m, 250m, 250m, 250m), (a.Q1, a.Q2, a.Q3, a.Q4));
    }

    [Fact]
    public async Task ItemsShortOfTheLine_SpreadTheRest_AndTheRowIsCombined()   // rule 10
    {
        AipExpenditure line = Line(Activity(501, 401, "January", "June"), Mooe, mooe: 10_000m);
        Item(line, 2, 4_000m);

        WfpReportRowDto row = OnlyRow((await Report(_ppdoFinance)).Value!);

        Assert.Equal((3_000m, 7_000m, 0m, 0m), (row.Amounts.Q1, row.Amounts.Q2, row.Amounts.Q3, row.Amounts.Q4));
        Assert.Equal(10_000m, row.Amounts.NetAppropriation);
        Assert.Equal(WfpNature.Combined, row.Nature);
    }

    [Fact]
    public async Task ALineWithMoneyInTwoColumns_GivesTwoRows_OnePerClass()
    {
        Line(Activity(501, 401, "January", "June"), Mooe, mooe: 1_000m, co: 2_000m);

        List<WfpReportRowDto> rows = Rows((await Report(_ppdoFinance)).Value!).ToList();

        Assert.Equal([1_000m, 2_000m], rows.Select(r => r.Amounts.NetAppropriation).Order());
    }

    [Fact]
    public async Task AZeroLine_AddsNoRow()
    {
        Line(Activity(501, 401, "January", "June"), Mooe);
        Assert.Empty((await Report(_ppdoFinance)).Value!.FundSourceReports);
    }

    // ── Totals: each fund block equals the AIP's base total for that fund ─────

    [Fact]
    public async Task EachFundBlocksNet_EqualsTheAipBaseTotalForThatFund_AcrossGroupsAndClasses()
    {
        AipActivity a1 = Activity(501, 401, "January", "June");
        AipActivity a2 = Activity(502, 401, "March", "May", isCreation: true);
        AipActivity a3 = Activity(503, 402, null, null);              // the SOCIAL group
        Line(a1, Mooe, mooe: 120_000m);
        Line(a1, Ps, ps: 333_333.33m, fund: "General Fund");
        AipExpenditure itemised = Line(a2, Co, co: 99_999.99m);
        Item(itemised, 3, 50_000m);
        Line(a3, Mooe, mooe: 7_777.77m, fund: "Trust Fund - PhilHealth");
        Line(a3, Co, co: 1_000_000m, fund: "Trust Fund - PhilHealth");
        Line(Activity(504, 403, "June", "July"), Mooe, mooe: 5m);      // OPA's: not in PTO's report

        WfpReportDto report = (await Report(_ppdoFinance)).Value!;

        Dictionary<string, decimal> aipBase = _expenditures
            .Where(e => new[] { 501, 502, 503 }.Contains(e.ActivityId))
            .GroupBy(e => (e.FundingSourceNameSnapshot ?? "General Fund").ToUpperInvariant())
            .ToDictionary(g => g.Key, g => g.Sum(e => e.Total));
        Assert.Equal(aipBase.Keys.Order(), report.FundSourceReports.Select(f => f.FundSourceName).Order());
        foreach (WfpReportFundSourceDto fund in report.FundSourceReports)
        {
            WfpReportAmountsDto t = fund.Breakdown.GrandTotal;
            Assert.Equal(aipBase[fund.FundSourceName], t.NetAppropriation);                 // the AIP's base figure
            Assert.Equal(t.NetAppropriation, t.Q1 + t.Q2 + t.Q3 + t.Q4);                    // the quarters add up
            Assert.Equal(t.NetAppropriation, t.AmountToBeReleased);
            Assert.Equal(t.NetAppropriation + t.Reserved, t.TotalAppropriation);            // reserve on top, as in the WFP
        }
        // GENERAL FUND first, then by name (the WFP report's order).
        Assert.Equal(["GENERAL FUND", "TRUST FUND - PHILHEALTH"], report.FundSourceReports.Select(f => f.FundSourceName));
    }

    [Fact]
    public async Task EveryRowsQuarters_SumToItsNet()
    {
        AipActivity a = Activity(501, 401, "February", "November");
        Line(a, Mooe, mooe: 1_234_567.89m);
        Line(a, Co, co: 0.05m);
        Line(a, Ps, ps: 10m / 3m);

        foreach (WfpReportRowDto row in Rows((await Report(_ppdoFinance)).Value!))
            Assert.Equal(row.Amounts.NetAppropriation, row.Amounts.Q1 + row.Amounts.Q2 + row.Amounts.Q3 + row.Amounts.Q4);
    }

    [Fact]
    public async Task AFigureEditedAfterConsolidation_ShowsOnTheNextRead()   // case 9, decision 1
    {
        AipExpenditure line = Line(Activity(501, 401, "January", "June"), Mooe, mooe: 100m);
        Assert.Equal(100m, OnlyRow((await Report(_ppdoFinance)).Value!).Amounts.NetAppropriation);

        line.Mooe = 200m; line.Recalculate();

        Assert.Equal(200m, OnlyRow((await Report(_ppdoFinance)).Value!).Amounts.NetAppropriation);
    }

    // ── The gate (decision 5, case 8) ─────────────────────────────────────────

    [Fact]
    public async Task OneGroupNotConsolidated_Is409_NamingTheOffice()
    {
        _groups.Single(g => g.Id == 202).WorkflowStatus = AipWorkflowStatus.SubmittedToPpdo;

        ServiceResult<WfpReportDto> result = await Report(_ppdoFinance);

        Assert.Equal(ServiceErrorCode.Conflict, result.Code);
        Assert.Equal("PTO's FY 2028 AIP is not marked as done yet.", result.Error);
    }

    [Fact]
    public async Task NotConsolidated_SaidToTheOfficeItself_IsYour()   // case 12
    {
        _groups.Single(g => g.Id == 201).WorkflowStatus = AipWorkflowStatus.ReturnedByPpdo;

        ServiceResult<WfpReportDto> result = await Report(_ptoCash);

        Assert.Equal(ServiceErrorCode.Conflict, result.Code);
        Assert.Equal("Your FY 2028 AIP is not marked as done yet.", result.Error);
    }

    [Fact]
    public async Task EligibleOffices_AreOnlyThoseWithEveryGroupConsolidated()
    {
        _groups.Single(g => g.Id == 202).WorkflowStatus = AipWorkflowStatus.SubmittedToPpdo;

        IReadOnlyList<WfpReportOfficeDto> offices = await _sut.GetInitialEligibleOfficesAsync(FiscalYear, _ppdoFinance);

        Assert.Equal(["OPA", "PPDO"], offices.Select(o => o.OfficeCode).Order());
        Assert.All(offices, o => Assert.Equal(AipWorkflowStatus.Consolidated, o.WfpStatus));
    }

    [Fact]
    public async Task EligibleOffices_NoRecordForTheYear_IsEmpty()
        => Assert.Empty(await _sut.GetInitialEligibleOfficesAsync(2031, _ppdoFinance));

    // ── The scope (decision 6, case 11) ───────────────────────────────────────

    [Fact]
    public async Task APpdoReader_ReadsEveryOffice()
    {
        foreach (User reader in new[] { _ppdoFinance, _ppdoAdmin, _superAdmin })
        foreach (int office in new[] { Pto, Opa })
            Assert.True((await Report(reader, office)).IsSuccess, $"{reader.FullName} on {office}");
    }

    [Fact]
    public async Task AnOfficesDepartmentHead_ReadsTheirOwnOffice()
    {
        foreach (User user in new[] { _ptoCash, _ptoUnassigned, _ptoAdmin, _ptoReviewer })
            Assert.True((await Report(user)).IsSuccess, user.FullName);
    }

    [Fact]
    public async Task AnOfficeEncoder_ReadsNothing_NotEvenTheirOwnOffice()   // PPDO-240
    {
        foreach (User user in new[] { _ptoCashEncoder, _ptoUnassignedEncoder })
        {
            Assert.Equal(ServiceErrorCode.NotFound, (await Report(user)).Code);
            Assert.Empty(await _sut.GetInitialEligibleOfficesAsync(FiscalYear, user));
            Assert.False(await _sut.MayReadInitialReportsAsync(user), user.FullName);
        }
    }

    [Fact]
    public async Task MayReadInitialReports_IsTrueForEveryReaderOfSomeOffice()   // PPDO-240 front door
    {
        foreach (User user in new[] { _ppdoFinance, _ppdoAdmin, _ppdoUnassigned, _superAdmin, _ptoCash, _ptoUnassigned, _ptoAdmin, _ptoReviewer, _gsoReviewer })
            Assert.True(await _sut.MayReadInitialReportsAsync(user), user.FullName);
        Assert.False(await _sut.MayReadInitialReportsAsync(_ptoNoBudget));
    }

    [Fact]
    public async Task AnOfficeUser_OnAnotherOffice_Is404_LikeAMissingOne()
    {
        foreach (User user in new[] { _ptoCash, _ptoAdmin })
        {
            ServiceResult<WfpReportDto> other = await Report(user, Opa);
            ServiceResult<WfpReportDto> missing = await Report(user, 999);
            Assert.Equal(ServiceErrorCode.NotFound, other.Code);
            Assert.Equal((missing.Code, missing.Error), (other.Code, other.Error));
        }
    }

    [Fact]
    public async Task AGuestOfficeCrossOfficeReviewer_IsNotAWfpReader_AndGets404OnAnotherOffice()
        => Assert.Equal(ServiceErrorCode.NotFound, (await Report(_gsoReviewer, Pto)).Code);

    [Fact]
    public async Task NoOfficeOrNoBudgetPlanning_ReadsNothing()
    {
        Assert.Equal(ServiceErrorCode.NotFound, (await Report(_noOffice)).Code);
        Assert.Equal(ServiceErrorCode.NotFound, (await Report(_ptoNoBudget)).Code);
    }

    [Fact]
    public async Task ScopeIsCheckedBeforeTheGate_SoA409NeverConfirmsAnotherOffice()
    {
        _groups.Single(g => g.Id == 203).WorkflowStatus = AipWorkflowStatus.Draft;   // OPA not done
        Assert.Equal(ServiceErrorCode.NotFound, (await Report(_ptoCash, Opa)).Code);
    }

    [Fact]
    public async Task EligibleOffices_ForAnOfficeUser_AreAtMostTheirOwn()
    {
        Assert.Equal(["PTO"], (await _sut.GetInitialEligibleOfficesAsync(FiscalYear, _ptoCash)).Select(o => o.OfficeCode));
        Assert.Empty(await _sut.GetInitialEligibleOfficesAsync(FiscalYear, _gsoReviewer));   // GSO has no AIP groups
        Assert.Empty(await _sut.GetInitialEligibleOfficesAsync(FiscalYear, _noOffice));
    }

    // ── The division (decision 12, RAL-136) ───────────────────────────────────

    [Fact]
    public async Task DivisionFilter_IsByTheActivitysTag_AndUntaggedIsWholeOfficeOnly()
    {
        Line(Activity(501, 401, null, null, division: PtoCash), Mooe, mooe: 100m);
        Line(Activity(502, 401, null, null, division: PtoRevenue), Mooe, mooe: 200m);
        Line(Activity(503, 401, null, null, division: null), Mooe, mooe: 400m);

        decimal Net(WfpReportDto r) => r.FundSourceReports.Sum(f => f.Breakdown.GrandTotal.NetAppropriation);
        Assert.Equal(700m, Net((await Report(_ppdoFinance)).Value!));
        Assert.Equal(200m, Net((await Report(_ppdoFinance, Pto, PtoRevenue)).Value!));
    }

    [Fact]
    public async Task AnOfficeStaffMemberWithADivision_IsForcedToIt_WhateverTheyAsk()
    {
        Line(Activity(501, 401, null, null, division: PtoCash), Mooe, mooe: 100m);
        Line(Activity(502, 401, null, null, division: PtoRevenue), Mooe, mooe: 200m);

        WfpReportDto asked = (await Report(_ptoCash, Pto, PtoRevenue)).Value!;
        WfpReportDto whole = (await Report(_ptoCash)).Value!;

        Assert.Equal(100m, asked.FundSourceReports.Sum(f => f.Breakdown.GrandTotal.NetAppropriation));
        Assert.Equal(100m, whole.FundSourceReports.Sum(f => f.Breakdown.GrandTotal.NetAppropriation));
    }

    [Fact]
    public async Task AnOfficesAdminOrUnassignedStaff_SeeTheWholeOffice()
    {
        Line(Activity(501, 401, null, null, division: PtoCash), Mooe, mooe: 100m);
        Line(Activity(502, 401, null, null, division: PtoRevenue), Mooe, mooe: 200m);

        foreach (User user in new[] { _ptoAdmin, _ptoUnassigned })
            Assert.Equal(300m, (await Report(user)).Value!.FundSourceReports.Sum(f => f.Breakdown.GrandTotal.NetAppropriation));
    }

    [Fact]
    public async Task HostStaffWithNoDivision_OnPpdosOwnInitialWfp_SeeNothing_AsTheWfpReportSays()
    {
        Assert.Equal(ServiceErrorCode.NotFound, (await Report(_ppdoUnassigned, Ppdo)).Code);
        // ...but a guest office is not narrowed by PPDO's divisions.
        Assert.True((await Report(_ppdoUnassigned, Pto)).IsSuccess);
    }

    // ── Source on the wire ────────────────────────────────────────────────────

    [Fact]
    public void Source_IsAbsentFromTheEnteredWfpsJson_AndAipOnTheInitialOne()
    {
        JsonSerializerOptions json = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
        string entered = JsonSerializer.Serialize(new WfpReportDto(2027, "PPDO", "PPDO", 0.10m, []), json);
        string initial = JsonSerializer.Serialize(new WfpReportDto(2028, "PTO", "PTO", 0.10m, [], WfpReportSources.Aip), json);

        Assert.DoesNotContain("source", entered);
        Assert.Contains("\"source\":\"Aip\"", initial);
    }

    // ── Fixture helpers ───────────────────────────────────────────────────────

    private void Group(int id, int officeId, string sector) => _groups.Add(new AipOffice
    {
        Id = id, AipRecordId = RecordId, OfficeId = officeId, Sector = sector, RefCode = $"R{id}", Name = $"G{id}",
        WorkflowStatus = AipWorkflowStatus.Consolidated,
    });

    private void Program(int id, int groupId, string refCode, string band)
        => _programs.Add(new AipProgram { Id = id, OfficeId = groupId, RefCode = refCode, Name = $"Program {id}", FunctionBand = band });

    private void Project(int id, int programId)
        => _projects.Add(new AipProject { Id = id, ProgramId = programId, RefCode = $"P-{id}", Name = $"Project {id}" });

    private AipActivity Activity(int id, int projectId, string? start, string? end, int? division = null, bool isCreation = false)
    {
        AipActivity a = new()
        {
            Id = id, ProjectId = projectId, RefCode = $"A-{id:000}", Name = $"Activity {id}",
            StartDate = start, EndDate = end, DivisionId = division, IsCreation = isCreation,
        };
        _activities.Add(a);
        return a;
    }

    private AipExpenditure Line(AipActivity activity, int accountId, decimal ps = 0m, decimal mooe = 0m, decimal co = 0m, string? fund = null)
    {
        AipExpenditure e = new()
        {
            Id = 900 + _expenditures.Count, ActivityId = activity.Id, AccountId = accountId,
            AccountNumberSnapshot = $"ACC-{accountId}", AccountTitleSnapshot = $"Account {accountId}",
            FundingSourceNameSnapshot = fund, Ps = ps, Mooe = mooe, Co = co,
        };
        e.Recalculate();
        _expenditures.Add(e);
        return e;
    }

    private void Item(AipExpenditure line, int periodNo, decimal amount)
    {
        AipProcurementItem item = new() { Id = 7000 + _items.Count, ExpenditureId = line.Id, PeriodNo = periodNo, Qty = 1m, UnitPrice = amount, NumberOfDays = 1m };
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
