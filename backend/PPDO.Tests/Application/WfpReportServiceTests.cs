using Moq;
using PPDO.Application.Common;
using PPDO.Application.DTOs.BudgetPlanning;
using PPDO.Application.Services;
using PPDO.Domain.Entities;
using PPDO.Domain.Interfaces;

namespace PPDO.Tests.Application;

/// <summary>
/// Unit tests for <see cref="WfpReportService"/> (RAL-132). Covers: eligible-office filtering,
/// office/AIP/hierarchy not-found paths, function-band grouping (including UNASSIGNED),
/// expense-class grouping/ordering, sub-total/grand-total aggregation, and merging WFP
/// expenditures across every division of an office. All repositories/services are mocked —
/// GetByActivityIdAsync results are canned directly rather than exercising the real
/// WfpExpenditureCalculator (that pipeline is covered by WfpExpenditureServiceTests /
/// WfpExpenditureCalculatorTests; this service only aggregates its output).
/// </summary>
public sealed class WfpReportServiceTests
{
    private const int FiscalYear = 2027;

    private static Office MakeOffice(int id, string code, string? refCode) => new()
    {
        Id = id, OfficeCode = code, OfficeName = $"{code} Office", OfficeRefCode = refCode,
        IsActive = true, CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow,
    };

    private static AipRecord MakeAip(int id, int fiscalYear, string status) => new()
    {
        Id = id, FiscalYear = fiscalYear, Status = status, EntrySource = "Manual",
        UploadedById = Guid.NewGuid(), UploadedAt = DateTime.UtcNow,
    };

    private static AipOffice MakeAipOffice(int id, int aipRecordId, string refCode) => new()
    {
        Id = id, AipRecordId = aipRecordId, RefCode = refCode, Name = "Office", Sector = "General",
    };

    private static AipProgram MakeProgram(int id, int officeId, string refCode, string? functionBand) => new()
    {
        Id = id, OfficeId = officeId, RefCode = refCode, Name = $"Program {refCode}", FunctionBand = functionBand,
    };

    private static AipProject MakeProject(int id, int programId, string refCode) => new()
    {
        Id = id, ProgramId = programId, RefCode = refCode, Name = $"Project {refCode}",
    };

    private static AipActivity MakeActivity(int id, int projectId, string refCode, bool isCreation = false) => new()
    {
        Id = id, ProjectId = projectId, RefCode = refCode, Name = $"Activity {refCode}", IsCreation = isCreation,
    };

    private static Account MakeAccount(int id, string number, string title, string expenseClass) => new()
    {
        Id = id, AccountNumber = number, AccountTitle = title, ExpenseClass = expenseClass, IsActive = true,
        CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow,
    };

    private static WfpExpenditureDto MakeExpenditure(
        int id, int wfpActivityId, int? accountId, string? accountNumber, string? accountTitle,
        decimal net, decimal total, string? fundSourceName = null) => new(
        id, wfpActivityId, accountId, accountNumber, accountTitle,
        WfpNature.NonProcurement, WfpFrequency.Quarterly, null, null, fundSourceName,
        total > net, total - net, null,
        net, 0, 0, 0, net, total,
        [], []);

    // ── Build ─────────────────────────────────────────────────────────────────

    private sealed record Fixture(
        WfpReportService Sut,
        Mock<IBudgetPlanningDashboardService> Dashboard,
        Mock<IAipRepository> AipRepo,
        Mock<IWfpRepository> WfpRepo,
        Mock<IWfpExpenditureService> Expenditures,
        List<Office> Offices,
        List<Account> Accounts);

    private static Fixture Build()
    {
        Mock<IBudgetPlanningDashboardService> dashboard = new();
        Mock<IAipRepository> aipRepo = new();
        Mock<IWfpRepository> wfpRepo = new();
        Mock<IWfpExpenditureService> expenditures = new();
        Mock<IRepository<Office>> officeRepo = new();
        Mock<IRepository<Account>> accountRepo = new();

        List<Office> offices = [];
        List<Account> accounts = [];

        officeRepo.Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => (IReadOnlyList<Office>)offices);
        accountRepo.Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => (IReadOnlyList<Account>)accounts);

        // Defaults so tests only need to stub the hierarchy levels they actually populate.
        aipRepo.Setup(r => r.GetProjectsByProgramIdsAsync(It.IsAny<IReadOnlyList<int>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyList<AipProject>)[]);
        aipRepo.Setup(r => r.GetActivitiesByProjectIdsAsync(It.IsAny<IReadOnlyList<int>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyList<AipActivity>)[]);
        wfpRepo.Setup(r => r.GetActivitiesByWfpIdAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyList<WfpActivity>)[]);

        WfpReportService sut = new(
            dashboard.Object, aipRepo.Object, wfpRepo.Object, expenditures.Object,
            officeRepo.Object, accountRepo.Object);

        return new Fixture(sut, dashboard, aipRepo, wfpRepo, expenditures, offices, accounts);
    }

    // ── GetEligibleOfficesAsync ───────────────────────────────────────────────

    [Fact]
    public async Task GetEligibleOfficesAsync_ExcludesNotStarted_IncludesDraftAndFinal()
    {
        Fixture f = Build();
        f.Dashboard.Setup(d => d.GetDashboardAsync(FiscalYear, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PlanningDashboardDto(
                FiscalYear, [FiscalYear],
                new LdipSummaryDto(0, []), new AipSummaryDto(0, []), new WfpSummaryDto(0, 3),
                [
                    new WfpOfficeStatusDto(1, "PPDO", "PPDO Office", "Draft", 10),
                    new WfpOfficeStatusDto(2, "PGO", "PGO Office", "Final", 10),
                    new WfpOfficeStatusDto(3, "PTO", "PTO Office", "Not started", null),
                ],
                new AllocationSetupOverviewDto(3, 0, 0, 3)));

        IReadOnlyList<WfpReportOfficeDto> result = await f.Sut.GetEligibleOfficesAsync(FiscalYear);

        Assert.Equal(2, result.Count);
        Assert.Contains(result, o => o.OfficeId == 1 && o.WfpStatus == "Draft");
        Assert.Contains(result, o => o.OfficeId == 2 && o.WfpStatus == "Final");
        Assert.DoesNotContain(result, o => o.OfficeId == 3);
    }

    // ── GetReportAsync — not-found paths ──────────────────────────────────────

    [Fact]
    public async Task GetReportAsync_UnknownOffice_ReturnsNotFound()
    {
        Fixture f = Build();
        ServiceResult<WfpReportDto> result = await f.Sut.GetReportAsync(999, FiscalYear);
        Assert.False(result.IsSuccess);
        Assert.Equal(ServiceErrorCode.NotFound, result.Code);
    }

    [Fact]
    public async Task GetReportAsync_OfficeWithNoRefCode_ReturnsNotFound()
    {
        Fixture f = Build();
        f.Offices.Add(MakeOffice(1, "PPDO", refCode: null));

        ServiceResult<WfpReportDto> result = await f.Sut.GetReportAsync(1, FiscalYear);
        Assert.False(result.IsSuccess);
        Assert.Equal(ServiceErrorCode.NotFound, result.Code);
    }

    [Fact]
    public async Task GetReportAsync_NoAipForFiscalYear_ReturnsNotFound()
    {
        Fixture f = Build();
        f.Offices.Add(MakeOffice(1, "PPDO", "013"));
        f.AipRepo.Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyList<AipRecord>)[]);

        ServiceResult<WfpReportDto> result = await f.Sut.GetReportAsync(1, FiscalYear);
        Assert.False(result.IsSuccess);
        Assert.Equal(ServiceErrorCode.NotFound, result.Code);
    }

    [Fact]
    public async Task GetReportAsync_NoMatchingAipOffice_ReturnsNotFound()
    {
        Fixture f = Build();
        f.Offices.Add(MakeOffice(1, "PPDO", "013"));
        f.AipRepo.Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyList<AipRecord>)[MakeAip(100, FiscalYear, PlanningStatus.Draft)]);
        f.AipRepo.Setup(r => r.GetOfficesByAipIdAsync(100, It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyList<AipOffice>)[MakeAipOffice(1, 100, "3000-000-1-01-099")]);

        ServiceResult<WfpReportDto> result = await f.Sut.GetReportAsync(1, FiscalYear);
        Assert.False(result.IsSuccess);
        Assert.Equal(ServiceErrorCode.NotFound, result.Code);
    }

    // ── GetReportAsync — happy path ───────────────────────────────────────────

    [Fact]
    public async Task GetReportAsync_PrefersFinalAipOverDraft_ForSameFiscalYear()
    {
        Fixture f = Build();
        f.Offices.Add(MakeOffice(1, "PPDO", "013"));
        f.AipRepo.Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyList<AipRecord>)[
                MakeAip(100, FiscalYear, PlanningStatus.Draft),
                MakeAip(101, FiscalYear, PlanningStatus.Final),
            ]);
        f.AipRepo.Setup(r => r.GetOfficesByAipIdAsync(101, It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyList<AipOffice>)[MakeAipOffice(1, 101, "3000-000-1-01-013")]);
        f.AipRepo.Setup(r => r.GetProgramsByOfficeIdsAsync(It.IsAny<IReadOnlyList<int>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyList<AipProgram>)[]);
        f.WfpRepo.Setup(r => r.GetFilteredAsync(101, 1, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyList<WfpRecord>)[]);

        ServiceResult<WfpReportDto> result = await f.Sut.GetReportAsync(1, FiscalYear);

        Assert.True(result.IsSuccess);
        // GetOfficesByAipIdAsync(101, ...) — the Final AIP — was actually called (verifies choice).
        f.AipRepo.Verify(r => r.GetOfficesByAipIdAsync(101, It.IsAny<CancellationToken>()), Times.Once);
        f.AipRepo.Verify(r => r.GetOfficesByAipIdAsync(100, It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task GetReportAsync_GroupsByFunctionBand_NullBandGoesToUnassigned()
    {
        Fixture f = Build();
        f.Offices.Add(MakeOffice(1, "PPDO", "013"));
        f.AipRepo.Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyList<AipRecord>)[MakeAip(100, FiscalYear, PlanningStatus.Draft)]);
        f.AipRepo.Setup(r => r.GetOfficesByAipIdAsync(100, It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyList<AipOffice>)[MakeAipOffice(1, 100, "3000-000-1-01-013")]);

        AipProgram coreProgram = MakeProgram(1, 1, "PGM-CORE", "CORE");
        AipProgram unbandedProgram = MakeProgram(2, 1, "PGM-NONE", null);
        f.AipRepo.Setup(r => r.GetProgramsByOfficeIdsAsync(It.IsAny<IReadOnlyList<int>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyList<AipProgram>)[coreProgram, unbandedProgram]);

        AipProject coreProject = MakeProject(10, 1, "PRJ-CORE");
        AipProject unbandedProject = MakeProject(11, 2, "PRJ-NONE");
        f.AipRepo.Setup(r => r.GetProjectsByProgramIdsAsync(It.IsAny<IReadOnlyList<int>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyList<AipProject>)[coreProject, unbandedProject]);

        AipActivity coreActivity = MakeActivity(100, 10, "ACT-CORE");
        AipActivity unbandedActivity = MakeActivity(101, 11, "ACT-NONE");
        f.AipRepo.Setup(r => r.GetActivitiesByProjectIdsAsync(It.IsAny<IReadOnlyList<int>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyList<AipActivity>)[coreActivity, unbandedActivity]);

        // Both activities need at least one expenditure to appear in the report at all.
        f.WfpRepo.Setup(r => r.GetFilteredAsync(100, 1, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyList<WfpRecord>)[
                new WfpRecord { Id = 1000, AipRecordId = 100, OfficeId = 1, DivisionId = 1, FiscalYear = FiscalYear, Status = PlanningStatus.Draft },
            ]);
        f.WfpRepo.Setup(r => r.GetActivitiesByWfpIdAsync(1000, It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyList<WfpActivity>)[
                new WfpActivity { Id = 500, WfpId = 1000, AipActivityId = 100 },
                new WfpActivity { Id = 501, WfpId = 1000, AipActivityId = 101 },
            ]);
        f.Accounts.Add(MakeAccount(1, "5-02-03-010", "Office Supplies Expenses", "MOOE"));
        f.Expenditures.Setup(e => e.GetByActivityIdAsync(500, It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyList<WfpExpenditureDto>)[
                MakeExpenditure(1, 500, 1, "5-02-03-010", "Office Supplies Expenses", net: 1000, total: 1000),
            ]);
        f.Expenditures.Setup(e => e.GetByActivityIdAsync(501, It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyList<WfpExpenditureDto>)[
                MakeExpenditure(2, 501, 1, "5-02-03-010", "Office Supplies Expenses", net: 500, total: 500),
            ]);

        ServiceResult<WfpReportDto> result = await f.Sut.GetReportAsync(1, FiscalYear);

        Assert.True(result.IsSuccess);
        IReadOnlyList<WfpReportFunctionBandSectionDto> sections = result.Value!.FundSourceReports.Single().Sections;
        Assert.Equal(2, sections.Count);
        Assert.Equal("CORE", sections[0].FunctionBand);
        Assert.Equal("CORE FUNCTIONS", sections[0].FunctionBandLabel);
        Assert.Equal("UNASSIGNED", sections[1].FunctionBand);
        Assert.Equal("UNASSIGNED FUNCTIONS", sections[1].FunctionBandLabel);
    }

    [Fact]
    public async Task GetReportAsync_MergesExpendituresAcrossDivisions_AndGroupsByExpenseClass()
    {
        Fixture f = Build();
        f.Offices.Add(MakeOffice(1, "PPDO", "013"));
        f.AipRepo.Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyList<AipRecord>)[MakeAip(100, FiscalYear, PlanningStatus.Draft)]);
        f.AipRepo.Setup(r => r.GetOfficesByAipIdAsync(100, It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyList<AipOffice>)[MakeAipOffice(1, 100, "3000-000-1-01-013")]);

        AipProgram program = MakeProgram(1, 1, "PGM-1", "STRATEGIC");
        f.AipRepo.Setup(r => r.GetProgramsByOfficeIdsAsync(It.IsAny<IReadOnlyList<int>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyList<AipProgram>)[program]);
        AipProject project = MakeProject(10, 1, "PRJ-1");
        f.AipRepo.Setup(r => r.GetProjectsByProgramIdsAsync(It.IsAny<IReadOnlyList<int>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyList<AipProject>)[project]);
        AipActivity activity = MakeActivity(100, 10, "ACT-1", isCreation: true);
        f.AipRepo.Setup(r => r.GetActivitiesByProjectIdsAsync(It.IsAny<IReadOnlyList<int>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyList<AipActivity>)[activity]);

        // Two WFP records (two divisions), each with a wfp_activity row for the same AipActivityId.
        f.WfpRepo.Setup(r => r.GetFilteredAsync(100, 1, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyList<WfpRecord>)[
                new WfpRecord { Id = 1000, AipRecordId = 100, OfficeId = 1, DivisionId = 1, FiscalYear = FiscalYear, Status = PlanningStatus.Draft },
                new WfpRecord { Id = 1001, AipRecordId = 100, OfficeId = 1, DivisionId = 2, FiscalYear = FiscalYear, Status = PlanningStatus.Draft },
            ]);
        f.WfpRepo.Setup(r => r.GetActivitiesByWfpIdAsync(1000, It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyList<WfpActivity>)[new WfpActivity { Id = 500, WfpId = 1000, AipActivityId = 100 }]);
        f.WfpRepo.Setup(r => r.GetActivitiesByWfpIdAsync(1001, It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyList<WfpActivity>)[new WfpActivity { Id = 501, WfpId = 1001, AipActivityId = 100 }]);

        f.Accounts.Add(MakeAccount(1, "5-01-01-010", "Salaries and Wages - Regular", "PS"));
        f.Accounts.Add(MakeAccount(2, "5-02-03-010", "Office Supplies Expenses", "MOOE"));

        f.Expenditures.Setup(e => e.GetByActivityIdAsync(500, It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyList<WfpExpenditureDto>)[
                MakeExpenditure(1, 500, 1, "5-01-01-010", "Salaries and Wages - Regular", net: 90000, total: 100000),
            ]);
        f.Expenditures.Setup(e => e.GetByActivityIdAsync(501, It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyList<WfpExpenditureDto>)[
                MakeExpenditure(2, 501, 2, "5-02-03-010", "Office Supplies Expenses", net: 5000, total: 5000),
            ]);

        ServiceResult<WfpReportDto> result = await f.Sut.GetReportAsync(1, FiscalYear);

        Assert.True(result.IsSuccess);
        WfpReportActivityDto activityDto = result.Value!.FundSourceReports.Single().Sections
            .Single().Programs.Single().Projects.Single().Activities.Single();

        Assert.True(activityDto.IsCreation);
        Assert.Equal(2, activityDto.ExpenseClasses.Count);
        // PS ordered before MOOE regardless of division-processing order.
        Assert.Equal("PS", activityDto.ExpenseClasses[0].ExpenseClass);
        Assert.Equal("MOOE", activityDto.ExpenseClasses[1].ExpenseClass);

        Assert.Equal(90000, activityDto.ExpenseClasses[0].SubTotal.NetAppropriation);
        Assert.Equal(100000, activityDto.ExpenseClasses[0].SubTotal.TotalAppropriation);
        Assert.Equal(10000, activityDto.ExpenseClasses[0].SubTotal.Reserved);
        Assert.Equal(5000, activityDto.ExpenseClasses[1].SubTotal.NetAppropriation);

        // Grand total merges BOTH divisions' expenditures.
        Assert.Equal(95000, activityDto.GrandTotal.NetAppropriation);
        Assert.Equal(105000, activityDto.GrandTotal.TotalAppropriation);
        Assert.Equal(95000, activityDto.GrandTotal.AmountToBeReleased);
    }

    // ── GetReportAsync — division scoping (RAL-136) ───────────────────────────

    [Fact]
    public async Task GetReportAsync_WithDivisionId_ScopesToThatDivisionOnly()
    {
        Fixture f = Build();
        f.Offices.Add(MakeOffice(1, "PPDO", "013"));
        f.AipRepo.Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyList<AipRecord>)[MakeAip(100, FiscalYear, PlanningStatus.Draft)]);
        f.AipRepo.Setup(r => r.GetOfficesByAipIdAsync(100, It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyList<AipOffice>)[MakeAipOffice(1, 100, "3000-000-1-01-013")]);

        AipProgram program = MakeProgram(1, 1, "PGM-1", "STRATEGIC");
        f.AipRepo.Setup(r => r.GetProgramsByOfficeIdsAsync(It.IsAny<IReadOnlyList<int>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyList<AipProgram>)[program]);
        AipProject project = MakeProject(10, 1, "PRJ-1");
        f.AipRepo.Setup(r => r.GetProjectsByProgramIdsAsync(It.IsAny<IReadOnlyList<int>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyList<AipProject>)[project]);
        AipActivity activity = MakeActivity(100, 10, "ACT-1");
        f.AipRepo.Setup(r => r.GetActivitiesByProjectIdsAsync(It.IsAny<IReadOnlyList<int>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyList<AipActivity>)[activity]);

        // The service must pass divisionId straight through to GetFilteredAsync — only division
        // 2's WfpRecord is registered under the (aipRecordId=100, officeId=1, divisionId=2) mock;
        // any other filter combination (e.g. the unscoped null-division call) returns empty,
        // which would make this test fail if divisionId were dropped instead of threaded through.
        f.WfpRepo.Setup(r => r.GetFilteredAsync(100, 1, 2, It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyList<WfpRecord>)[
                new WfpRecord { Id = 1001, AipRecordId = 100, OfficeId = 1, DivisionId = 2, FiscalYear = FiscalYear, Status = PlanningStatus.Draft },
            ]);
        f.WfpRepo.Setup(r => r.GetActivitiesByWfpIdAsync(1001, It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyList<WfpActivity>)[new WfpActivity { Id = 501, WfpId = 1001, AipActivityId = 100 }]);

        f.Accounts.Add(MakeAccount(2, "5-02-03-010", "Office Supplies Expenses", "MOOE"));
        f.Expenditures.Setup(e => e.GetByActivityIdAsync(501, It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyList<WfpExpenditureDto>)[
                MakeExpenditure(2, 501, 2, "5-02-03-010", "Office Supplies Expenses", net: 5000, total: 5000),
            ]);

        ServiceResult<WfpReportDto> result = await f.Sut.GetReportAsync(1, FiscalYear, divisionId: 2);

        Assert.True(result.IsSuccess);
        WfpReportActivityDto activityDto = result.Value!.FundSourceReports.Single().Sections
            .Single().Programs.Single().Projects.Single().Activities.Single();
        Assert.Equal(5000, activityDto.GrandTotal.NetAppropriation);
        f.WfpRepo.Verify(r => r.GetFilteredAsync(100, 1, 2, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task GetReportAsync_ActivityWithNoExpenditures_IsExcluded()
    {
        Fixture f = Build();
        f.Offices.Add(MakeOffice(1, "PPDO", "013"));
        f.AipRepo.Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyList<AipRecord>)[MakeAip(100, FiscalYear, PlanningStatus.Draft)]);
        f.AipRepo.Setup(r => r.GetOfficesByAipIdAsync(100, It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyList<AipOffice>)[MakeAipOffice(1, 100, "3000-000-1-01-013")]);
        AipProgram program = MakeProgram(1, 1, "PGM-1", "SUPPORT");
        f.AipRepo.Setup(r => r.GetProgramsByOfficeIdsAsync(It.IsAny<IReadOnlyList<int>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyList<AipProgram>)[program]);
        AipProject project = MakeProject(10, 1, "PRJ-1");
        f.AipRepo.Setup(r => r.GetProjectsByProgramIdsAsync(It.IsAny<IReadOnlyList<int>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyList<AipProject>)[project]);
        AipActivity activity = MakeActivity(100, 10, "ACT-1");
        f.AipRepo.Setup(r => r.GetActivitiesByProjectIdsAsync(It.IsAny<IReadOnlyList<int>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyList<AipActivity>)[activity]);
        f.WfpRepo.Setup(r => r.GetFilteredAsync(100, 1, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyList<WfpRecord>)[]);

        ServiceResult<WfpReportDto> result = await f.Sut.GetReportAsync(1, FiscalYear);

        Assert.True(result.IsSuccess);
        // No expenditures anywhere in the office — there's nothing to report under ANY fund
        // source, so the fund-source list itself is empty (not one empty-sections entry).
        Assert.Empty(result.Value!.FundSourceReports);
    }

    [Fact]
    public async Task GetReportAsync_OnlyOneActivityHasExpenditures_SiblingsWithNoneAreExcluded()
    {
        Fixture f = Build();
        f.Offices.Add(MakeOffice(1, "PPDO", "013"));
        f.AipRepo.Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyList<AipRecord>)[MakeAip(100, FiscalYear, PlanningStatus.Draft)]);
        f.AipRepo.Setup(r => r.GetOfficesByAipIdAsync(100, It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyList<AipOffice>)[MakeAipOffice(1, 100, "3000-000-1-01-013")]);
        AipProgram programWithData = MakeProgram(1, 1, "PGM-1", "SUPPORT");
        AipProgram emptyProgram = MakeProgram(2, 1, "PGM-2", "SUPPORT");
        f.AipRepo.Setup(r => r.GetProgramsByOfficeIdsAsync(It.IsAny<IReadOnlyList<int>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyList<AipProgram>)[programWithData, emptyProgram]);
        AipProject projectWithData = MakeProject(10, 1, "PRJ-1");
        AipProject emptyProject = MakeProject(11, 1, "PRJ-2");
        AipProject projectUnderEmptyProgram = MakeProject(12, 2, "PRJ-3");
        f.AipRepo.Setup(r => r.GetProjectsByProgramIdsAsync(It.IsAny<IReadOnlyList<int>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyList<AipProject>)[projectWithData, emptyProject, projectUnderEmptyProgram]);
        AipActivity activityWithData = MakeActivity(100, 10, "ACT-1");
        AipActivity activityWithoutData = MakeActivity(101, 11, "ACT-2");
        AipActivity activityUnderEmptyProgram = MakeActivity(102, 12, "ACT-3");
        f.AipRepo.Setup(r => r.GetActivitiesByProjectIdsAsync(It.IsAny<IReadOnlyList<int>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyList<AipActivity>)[activityWithData, activityWithoutData, activityUnderEmptyProgram]);

        f.WfpRepo.Setup(r => r.GetFilteredAsync(100, 1, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyList<WfpRecord>)[
                new WfpRecord { Id = 1000, AipRecordId = 100, OfficeId = 1, DivisionId = 1, FiscalYear = FiscalYear, Status = PlanningStatus.Draft },
            ]);
        f.WfpRepo.Setup(r => r.GetActivitiesByWfpIdAsync(1000, It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyList<WfpActivity>)[new WfpActivity { Id = 500, WfpId = 1000, AipActivityId = 100 }]);
        f.Accounts.Add(MakeAccount(1, "5-02-03-010", "Office Supplies Expenses", "MOOE"));
        f.Expenditures.Setup(e => e.GetByActivityIdAsync(500, It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyList<WfpExpenditureDto>)[
                MakeExpenditure(1, 500, 1, "5-02-03-010", "Office Supplies Expenses", net: 1000, total: 1000),
            ]);

        ServiceResult<WfpReportDto> result = await f.Sut.GetReportAsync(1, FiscalYear);

        Assert.True(result.IsSuccess);
        WfpReportProgramDto onlyProgram = result.Value!.FundSourceReports.Single().Sections.Single().Programs.Single();
        Assert.Equal("PGM-1", onlyProgram.RefCode);
        WfpReportProjectDto onlyProject = onlyProgram.Projects.Single();
        Assert.Equal("PRJ-1", onlyProject.RefCode);
        WfpReportActivityDto onlyActivity = onlyProject.Activities.Single();
        Assert.Equal("ACT-1", onlyActivity.RefCode);
    }

    [Fact]
    public async Task GetReportAsync_ReturnsReserveRateFromWfpReserveRule()
    {
        Fixture f = Build();
        f.Offices.Add(MakeOffice(1, "PPDO", "013"));
        f.AipRepo.Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyList<AipRecord>)[MakeAip(100, FiscalYear, PlanningStatus.Draft)]);
        f.AipRepo.Setup(r => r.GetOfficesByAipIdAsync(100, It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyList<AipOffice>)[MakeAipOffice(1, 100, "3000-000-1-01-013")]);
        f.AipRepo.Setup(r => r.GetProgramsByOfficeIdsAsync(It.IsAny<IReadOnlyList<int>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyList<AipProgram>)[]);
        f.WfpRepo.Setup(r => r.GetFilteredAsync(100, 1, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyList<WfpRecord>)[]);

        ServiceResult<WfpReportDto> result = await f.Sut.GetReportAsync(1, FiscalYear);

        Assert.True(result.IsSuccess);
        Assert.Equal(WfpReserveRule.Rate, result.Value!.ReserveRate);
        Assert.Equal("PPDO", result.Value!.OfficeCode);
        Assert.Equal(FiscalYear, result.Value!.FiscalYear);
    }

    [Fact]
    public async Task GetReportAsync_ComputesProjectAndProgramGrandTotals_AcrossMultipleActivitiesAndProjects()
    {
        Fixture f = Build();
        f.Offices.Add(MakeOffice(1, "PPDO", "013"));
        f.AipRepo.Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyList<AipRecord>)[MakeAip(100, FiscalYear, PlanningStatus.Draft)]);
        f.AipRepo.Setup(r => r.GetOfficesByAipIdAsync(100, It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyList<AipOffice>)[MakeAipOffice(1, 100, "3000-000-1-01-013")]);

        AipProgram program = MakeProgram(1, 1, "PGM-1", "CORE");
        f.AipRepo.Setup(r => r.GetProgramsByOfficeIdsAsync(It.IsAny<IReadOnlyList<int>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyList<AipProgram>)[program]);

        // Two projects; the first has two activities, the second has one.
        AipProject project1 = MakeProject(10, 1, "PRJ-1");
        AipProject project2 = MakeProject(11, 1, "PRJ-2");
        f.AipRepo.Setup(r => r.GetProjectsByProgramIdsAsync(It.IsAny<IReadOnlyList<int>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyList<AipProject>)[project1, project2]);

        AipActivity act1 = MakeActivity(100, 10, "ACT-1");
        AipActivity act2 = MakeActivity(101, 10, "ACT-2");
        AipActivity act3 = MakeActivity(102, 11, "ACT-3");
        f.AipRepo.Setup(r => r.GetActivitiesByProjectIdsAsync(It.IsAny<IReadOnlyList<int>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyList<AipActivity>)[act1, act2, act3]);

        f.WfpRepo.Setup(r => r.GetFilteredAsync(100, 1, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyList<WfpRecord>)[
                new WfpRecord { Id = 1000, AipRecordId = 100, OfficeId = 1, DivisionId = 1, FiscalYear = FiscalYear, Status = PlanningStatus.Draft },
            ]);
        f.WfpRepo.Setup(r => r.GetActivitiesByWfpIdAsync(1000, It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyList<WfpActivity>)[
                new WfpActivity { Id = 500, WfpId = 1000, AipActivityId = 100 },
                new WfpActivity { Id = 501, WfpId = 1000, AipActivityId = 101 },
                new WfpActivity { Id = 502, WfpId = 1000, AipActivityId = 102 },
            ]);
        f.Accounts.Add(MakeAccount(1, "5-02-03-010", "Office Supplies Expenses", "MOOE"));
        f.Expenditures.Setup(e => e.GetByActivityIdAsync(500, It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyList<WfpExpenditureDto>)[MakeExpenditure(1, 500, 1, "5-02-03-010", "Office Supplies Expenses", net: 1000, total: 1000)]);
        f.Expenditures.Setup(e => e.GetByActivityIdAsync(501, It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyList<WfpExpenditureDto>)[MakeExpenditure(2, 501, 1, "5-02-03-010", "Office Supplies Expenses", net: 2000, total: 2000)]);
        f.Expenditures.Setup(e => e.GetByActivityIdAsync(502, It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyList<WfpExpenditureDto>)[MakeExpenditure(3, 502, 1, "5-02-03-010", "Office Supplies Expenses", net: 4000, total: 4000)]);

        ServiceResult<WfpReportDto> result = await f.Sut.GetReportAsync(1, FiscalYear);

        Assert.True(result.IsSuccess);
        WfpReportProgramDto programDto = result.Value!.FundSourceReports.Single().Sections.Single().Programs.Single();
        WfpReportProjectDto projectDto1 = programDto.Projects.Single(p => p.RefCode == "PRJ-1");
        WfpReportProjectDto projectDto2 = programDto.Projects.Single(p => p.RefCode == "PRJ-2");

        // PROJECT GRAND TOTAL — sums its own activities only.
        Assert.Equal(3000, projectDto1.GrandTotal.NetAppropriation);
        Assert.Equal(4000, projectDto2.GrandTotal.NetAppropriation);
        // PROGRAM GRAND TOTAL — sums every project under it.
        Assert.Equal(7000, programDto.GrandTotal.NetAppropriation);
    }

    [Fact]
    public async Task GetReportAsync_SectionBreakdown_SplitsPersonalServicesAndMooeByCreationFlag()
    {
        Fixture f = Build();
        f.Offices.Add(MakeOffice(1, "PPDO", "013"));
        f.AipRepo.Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyList<AipRecord>)[MakeAip(100, FiscalYear, PlanningStatus.Draft)]);
        f.AipRepo.Setup(r => r.GetOfficesByAipIdAsync(100, It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyList<AipOffice>)[MakeAipOffice(1, 100, "3000-000-1-01-013")]);
        AipProgram program = MakeProgram(1, 1, "PGM-1", "SUPPORT");
        f.AipRepo.Setup(r => r.GetProgramsByOfficeIdsAsync(It.IsAny<IReadOnlyList<int>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyList<AipProgram>)[program]);
        AipProject project = MakeProject(10, 1, "PRJ-1");
        f.AipRepo.Setup(r => r.GetProjectsByProgramIdsAsync(It.IsAny<IReadOnlyList<int>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyList<AipProject>)[project]);

        // One ordinary activity (PS + MOOE + CO), one Creation-flagged activity (PS + MOOE).
        AipActivity ordinary = MakeActivity(100, 10, "ACT-ORDINARY", isCreation: false);
        AipActivity creation = MakeActivity(101, 10, "ACT-CREATION", isCreation: true);
        f.AipRepo.Setup(r => r.GetActivitiesByProjectIdsAsync(It.IsAny<IReadOnlyList<int>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyList<AipActivity>)[ordinary, creation]);

        f.WfpRepo.Setup(r => r.GetFilteredAsync(100, 1, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyList<WfpRecord>)[
                new WfpRecord { Id = 1000, AipRecordId = 100, OfficeId = 1, DivisionId = 1, FiscalYear = FiscalYear, Status = PlanningStatus.Draft },
            ]);
        f.WfpRepo.Setup(r => r.GetActivitiesByWfpIdAsync(1000, It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyList<WfpActivity>)[
                new WfpActivity { Id = 500, WfpId = 1000, AipActivityId = 100 },
                new WfpActivity { Id = 501, WfpId = 1000, AipActivityId = 101 },
            ]);

        f.Accounts.Add(MakeAccount(1, "5-01-01-010", "Salaries and Wages - Regular", "PS"));
        f.Accounts.Add(MakeAccount(2, "5-02-03-010", "Office Supplies Expenses", "MOOE"));
        f.Accounts.Add(MakeAccount(3, "1-07-05-010", "Machinery", "CO"));

        f.Expenditures.Setup(e => e.GetByActivityIdAsync(500, It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyList<WfpExpenditureDto>)[
                MakeExpenditure(1, 500, 1, "5-01-01-010", "Salaries and Wages - Regular", net: 10000, total: 10000),
                MakeExpenditure(2, 500, 2, "5-02-03-010", "Office Supplies Expenses", net: 3000, total: 3000),
                MakeExpenditure(3, 500, 3, "1-07-05-010", "Machinery", net: 5000, total: 5000),
            ]);
        f.Expenditures.Setup(e => e.GetByActivityIdAsync(501, It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyList<WfpExpenditureDto>)[
                MakeExpenditure(4, 501, 1, "5-01-01-010", "Salaries and Wages - Regular", net: 20000, total: 20000),
                MakeExpenditure(5, 501, 2, "5-02-03-010", "Office Supplies Expenses", net: 1000, total: 1000),
            ]);

        ServiceResult<WfpReportDto> result = await f.Sut.GetReportAsync(1, FiscalYear);

        Assert.True(result.IsSuccess);
        WfpReportBreakdownDto breakdown = result.Value!.FundSourceReports.Single().Breakdown;

        Assert.Equal(10000, breakdown.PersonalServices.NetAppropriation);
        Assert.Equal(3000, breakdown.MooeExcludingCreation.NetAppropriation);
        Assert.Equal(5000, breakdown.CapitalOutlay.NetAppropriation);
        Assert.Equal(20000, breakdown.PersonalServicesCreation.NetAppropriation);
        Assert.Equal(1000, breakdown.MooeCreation.NetAppropriation);
        // GrandTotal = 10000 + 3000 + 5000 + 20000 + 1000 = 39000, matching both activities' totals.
        Assert.Equal(39000, breakdown.GrandTotal.NetAppropriation);
    }

    [Fact]
    public async Task GetReportAsync_Breakdown_IsOnePerFundSource_NotOnePerFunctionBandSection()
    {
        Fixture f = Build();
        f.Offices.Add(MakeOffice(1, "PPDO", "013"));
        f.AipRepo.Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyList<AipRecord>)[MakeAip(100, FiscalYear, PlanningStatus.Draft)]);
        f.AipRepo.Setup(r => r.GetOfficesByAipIdAsync(100, It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyList<AipOffice>)[MakeAipOffice(1, 100, "3000-000-1-01-013")]);

        // Two programs in TWO DIFFERENT function-band sections (CORE and STRATEGIC).
        AipProgram coreProgram = MakeProgram(1, 1, "PGM-CORE", "CORE");
        AipProgram strategicProgram = MakeProgram(2, 1, "PGM-STRATEGIC", "STRATEGIC");
        f.AipRepo.Setup(r => r.GetProgramsByOfficeIdsAsync(It.IsAny<IReadOnlyList<int>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyList<AipProgram>)[coreProgram, strategicProgram]);
        AipProject coreProject = MakeProject(10, 1, "PRJ-CORE");
        AipProject strategicProject = MakeProject(11, 2, "PRJ-STRATEGIC");
        f.AipRepo.Setup(r => r.GetProjectsByProgramIdsAsync(It.IsAny<IReadOnlyList<int>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyList<AipProject>)[coreProject, strategicProject]);
        AipActivity coreActivity = MakeActivity(100, 10, "ACT-CORE");
        AipActivity strategicActivity = MakeActivity(101, 11, "ACT-STRATEGIC");
        f.AipRepo.Setup(r => r.GetActivitiesByProjectIdsAsync(It.IsAny<IReadOnlyList<int>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyList<AipActivity>)[coreActivity, strategicActivity]);

        f.WfpRepo.Setup(r => r.GetFilteredAsync(100, 1, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyList<WfpRecord>)[
                new WfpRecord { Id = 1000, AipRecordId = 100, OfficeId = 1, DivisionId = 1, FiscalYear = FiscalYear, Status = PlanningStatus.Draft },
            ]);
        f.WfpRepo.Setup(r => r.GetActivitiesByWfpIdAsync(1000, It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyList<WfpActivity>)[
                new WfpActivity { Id = 500, WfpId = 1000, AipActivityId = 100 },
                new WfpActivity { Id = 501, WfpId = 1000, AipActivityId = 101 },
            ]);
        f.Accounts.Add(MakeAccount(1, "5-02-03-010", "Office Supplies Expenses", "MOOE"));
        f.Expenditures.Setup(e => e.GetByActivityIdAsync(500, It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyList<WfpExpenditureDto>)[
                MakeExpenditure(1, 500, 1, "5-02-03-010", "Office Supplies Expenses", net: 1000, total: 1000),
            ]);
        f.Expenditures.Setup(e => e.GetByActivityIdAsync(501, It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyList<WfpExpenditureDto>)[
                MakeExpenditure(2, 501, 1, "5-02-03-010", "Office Supplies Expenses", net: 2000, total: 2000),
            ]);

        ServiceResult<WfpReportDto> result = await f.Sut.GetReportAsync(1, FiscalYear);

        Assert.True(result.IsSuccess);
        WfpReportFundSourceDto fundReport = result.Value!.FundSourceReports.Single();
        // Two sections (CORE + STRATEGIC), but exactly ONE breakdown for the whole fund source,
        // combining both sections' MOOE totals (1000 + 2000 = 3000) — not one breakdown each.
        Assert.Equal(2, fundReport.Sections.Count);
        Assert.Equal(3000, fundReport.Breakdown.MooeExcludingCreation.NetAppropriation);
        Assert.Equal(3000, fundReport.Breakdown.GrandTotal.NetAppropriation);
    }

    [Fact]
    public async Task GetReportAsync_SegmentsByFundSource_SameActivityAppearsInBothFundBlocks()
    {
        Fixture f = Build();
        f.Offices.Add(MakeOffice(1, "PPDO", "013"));
        f.AipRepo.Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyList<AipRecord>)[MakeAip(100, FiscalYear, PlanningStatus.Draft)]);
        f.AipRepo.Setup(r => r.GetOfficesByAipIdAsync(100, It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyList<AipOffice>)[MakeAipOffice(1, 100, "3000-000-1-01-013")]);
        AipProgram program = MakeProgram(1, 1, "PGM-1", "CORE");
        f.AipRepo.Setup(r => r.GetProgramsByOfficeIdsAsync(It.IsAny<IReadOnlyList<int>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyList<AipProgram>)[program]);
        AipProject project = MakeProject(10, 1, "PRJ-1");
        f.AipRepo.Setup(r => r.GetProjectsByProgramIdsAsync(It.IsAny<IReadOnlyList<int>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyList<AipProject>)[project]);
        AipActivity activity = MakeActivity(100, 10, "ACT-1");
        f.AipRepo.Setup(r => r.GetActivitiesByProjectIdsAsync(It.IsAny<IReadOnlyList<int>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyList<AipActivity>)[activity]);

        f.WfpRepo.Setup(r => r.GetFilteredAsync(100, 1, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyList<WfpRecord>)[
                new WfpRecord { Id = 1000, AipRecordId = 100, OfficeId = 1, DivisionId = 1, FiscalYear = FiscalYear, Status = PlanningStatus.Draft },
            ]);
        f.WfpRepo.Setup(r => r.GetActivitiesByWfpIdAsync(1000, It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyList<WfpActivity>)[new WfpActivity { Id = 500, WfpId = 1000, AipActivityId = 100 }]);
        f.Accounts.Add(MakeAccount(1, "5-02-03-010", "Office Supplies Expenses", "MOOE"));

        // The SAME activity has two expenditure rows under two different fund sources — one
        // with no fund source set at all (defaults to General Fund), one explicitly GAD Fund.
        f.Expenditures.Setup(e => e.GetByActivityIdAsync(500, It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyList<WfpExpenditureDto>)[
                MakeExpenditure(1, 500, 1, "5-02-03-010", "Office Supplies Expenses", net: 1000, total: 1000, fundSourceName: null),
                MakeExpenditure(2, 500, 1, "5-02-03-010", "Office Supplies Expenses", net: 500, total: 500, fundSourceName: "5% GAD Fund"),
            ]);

        ServiceResult<WfpReportDto> result = await f.Sut.GetReportAsync(1, FiscalYear);

        Assert.True(result.IsSuccess);
        IReadOnlyList<WfpReportFundSourceDto> fundReports = result.Value!.FundSourceReports;
        Assert.Equal(2, fundReports.Count);
        // General Fund (the default) sorts first.
        Assert.Equal("GENERAL FUND", fundReports[0].FundSourceName);
        Assert.Equal("5% GAD FUND", fundReports[1].FundSourceName);

        WfpReportActivityDto gfActivity = fundReports[0].Sections.Single().Programs.Single().Projects.Single().Activities.Single();
        Assert.Equal(1000, gfActivity.GrandTotal.NetAppropriation);

        WfpReportActivityDto gadActivity = fundReports[1].Sections.Single().Programs.Single().Projects.Single().Activities.Single();
        Assert.Equal(500, gadActivity.GrandTotal.NetAppropriation);
    }

    [Fact]
    public async Task GetReportAsync_Row_CarriesSectorFromAipOfficeSector()
    {
        Fixture f = Build();
        f.Offices.Add(MakeOffice(1, "PPDO", "013"));
        f.AipRepo.Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyList<AipRecord>)[MakeAip(100, FiscalYear, PlanningStatus.Draft)]);
        AipOffice aipOffice = MakeAipOffice(1, 100, "3000-000-1-01-013");
        aipOffice.Sector = "SOCIAL";
        f.AipRepo.Setup(r => r.GetOfficesByAipIdAsync(100, It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyList<AipOffice>)[aipOffice]);
        AipProgram program = MakeProgram(1, 1, "PGM-1", "CORE");
        f.AipRepo.Setup(r => r.GetProgramsByOfficeIdsAsync(It.IsAny<IReadOnlyList<int>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyList<AipProgram>)[program]);
        AipProject project = MakeProject(10, 1, "PRJ-1");
        f.AipRepo.Setup(r => r.GetProjectsByProgramIdsAsync(It.IsAny<IReadOnlyList<int>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyList<AipProject>)[project]);
        AipActivity activity = MakeActivity(100, 10, "ACT-1");
        f.AipRepo.Setup(r => r.GetActivitiesByProjectIdsAsync(It.IsAny<IReadOnlyList<int>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyList<AipActivity>)[activity]);
        f.WfpRepo.Setup(r => r.GetFilteredAsync(100, 1, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyList<WfpRecord>)[
                new WfpRecord { Id = 1000, AipRecordId = 100, OfficeId = 1, DivisionId = 1, FiscalYear = FiscalYear, Status = PlanningStatus.Draft },
            ]);
        f.WfpRepo.Setup(r => r.GetActivitiesByWfpIdAsync(1000, It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyList<WfpActivity>)[new WfpActivity { Id = 500, WfpId = 1000, AipActivityId = 100 }]);
        f.Accounts.Add(MakeAccount(1, "5-02-03-010", "Office Supplies Expenses", "MOOE"));
        f.Expenditures.Setup(e => e.GetByActivityIdAsync(500, It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyList<WfpExpenditureDto>)[
                MakeExpenditure(1, 500, 1, "5-02-03-010", "Office Supplies Expenses", net: 1000, total: 1000),
            ]);

        ServiceResult<WfpReportDto> result = await f.Sut.GetReportAsync(1, FiscalYear);

        Assert.True(result.IsSuccess);
        WfpReportRowDto row = result.Value!.FundSourceReports.Single().Sections.Single()
            .Programs.Single().Projects.Single().Activities.Single().ExpenseClasses.Single().Rows.Single();
        Assert.Equal("SOCIAL SERVICES", row.Sector);
    }
}
