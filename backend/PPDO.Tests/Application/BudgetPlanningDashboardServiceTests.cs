using Moq;
using PPDO.Application.Common;
using PPDO.Application.DTOs.BudgetPlanning;
using PPDO.Application.Services;
using PPDO.Domain.Entities;
using PPDO.Domain.Interfaces;

namespace PPDO.Tests.Application;

/// <summary>
/// Unit tests for <see cref="BudgetPlanningDashboardService"/> (RAL-80, RAL-92; PPDO-scoped
/// rework — v1.4.5, RAL-161). All repositories are mocked — no database access occurs.
/// GetDashboardAsync always resolves the office whose OfficeCode == "PPDO" — every test must
/// seed an office with that exact code or GetDashboardAsync throws.
/// GetRecentActivityAsync tests use <see cref="IAuditRepository.GetRecentAsync"/>; actor
/// names are read from the <see cref="AuditLog.ChangedBy"/> navigation populated by the mock,
/// mirroring what the real <see cref="AuditRepository"/> returns via its Include(a=>a.ChangedBy).
/// </summary>
public sealed class BudgetPlanningDashboardServiceTests
{
    // v1.4.3 (RAL-154): the readiness summary reads General Fund only — matches GetGeneralFundIdAsync().
    private const int GfFundId = 1;
    private const int PpdoOfficeId = 1;

    // ── Helpers ──────────────────────────────────────────────────────────────

    private static LdipRecord Ldip(int id, string status, int? officeId = null,
        int fyStart = 2027, int fyEnd = 2029) => new()
    {
        Id = id, Status = status, RefCode = $"LDIP-{id}", Title = "T",
        EntryMode = "New", FiscalYearStart = fyStart, FiscalYearEnd = fyEnd, OfficeId = officeId,
        CreatedById = Guid.NewGuid(), CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow,
    };

    private static AipRecord Aip(int id, int fiscalYear, string status = "Draft") => new()
    {
        Id = id, FiscalYear = fiscalYear, Status = status, EntrySource = "Upload",
        UploadedById = Guid.NewGuid(), UploadedAt = DateTime.UtcNow,
    };

    private static WfpRecord Wfp(
        int id, int aipId, int officeId, string status = "Draft", int fy = 2027,
        int? divisionId = null, DateTime? updatedAt = null) => new()
    {
        Id = id, AipRecordId = aipId, OfficeId = officeId, DivisionId = divisionId, FiscalYear = fy,
        Status = status, CreatedById = Guid.NewGuid(), CreatedAt = DateTime.UtcNow,
        UpdatedAt = updatedAt ?? DateTime.UtcNow,
    };

    private static Office Off(int id, string name, bool active = true, string? refCode = null,
        string code = "PPDO") => new()
    {
        Id = id, OfficeCode = code, OfficeName = name, IsActive = active,
        OfficeRefCode = refCode,
        CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow,
    };

    private static Division Div(int id, int officeId, string name, string? code = null, bool active = true) => new()
    {
        Id = id, OfficeId = officeId, Name = name, Code = code, IsActive = active,
        CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow,
    };

    private static FundingSource Fund(int id, string code, string name, bool active = true) => new()
    {
        Id = id, Code = code, Name = name, IsActive = active,
        CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow,
    };

    private static AipOffice AipOff(int id, int aipRecordId, string refCode, string sector = "General") => new()
    {
        Id = id, AipRecordId = aipRecordId, RefCode = refCode, Name = "Office", Sector = sector,
    };

    private static AipProgram AipProg(int id, int officeId, string refCode) => new()
    {
        Id = id, OfficeId = officeId, RefCode = refCode, Name = "Program",
    };

    private static AipProject AipProj(int id, int programId, string refCode) => new()
    {
        Id = id, ProgramId = programId, RefCode = refCode, Name = "Project",
    };

    private static AipActivity AipAct(int id, int projectId, string refCode) => new()
    {
        Id = id, ProjectId = projectId, RefCode = refCode, Name = "Activity",
    };

    private static User AppUser(Guid id, string name, int? officeId = null) => new()
    {
        Id = id, FullName = name, Username = name.ToLower(), PasswordHash = "x",
        OfficeId = officeId,
    };

    /// <summary>
    /// Builds an AuditLog with ChangedBy already populated — mirrors what
    /// AuditRepository.GetRecentAsync returns via its Include(a => a.ChangedBy).
    /// </summary>
    private static AuditLog Audit(long id, User? changedBy = null, DateTime? at = null)
    {
        User actor = changedBy ?? AppUser(Guid.NewGuid(), "R. Alcaide");
        return new()
        {
            Id = id, TableName = "accounts", RecordId = 1, Action = "CREATE",
            ChangedById = actor.Id, ChangedAt = at ?? DateTime.UtcNow,
            NewValues = "{}",
            ChangedBy = actor,
        };
    }

    /// <summary>
    /// Builds a service with mocked dependencies. The audit mock is returned so callers can call
    /// Verify() on it. GetDashboardAsync resolves the office via OfficeCode == "PPDO" — tests that
    /// exercise it must include an office built with the default Off() code ("PPDO").
    /// </summary>
    private static (BudgetPlanningDashboardService svc, Mock<IAuditRepository> auditMock) Build(
        List<LdipRecord> ldips,
        List<AipRecord> aips,
        List<WfpRecord> wfps,
        List<Office> offices,
        List<AuditLog> audits,
        List<Division>? divisions = null,
        List<FundingSource>? fundingSources = null,
        Mock<IAipRepository>? aipRepoMock = null,
        Mock<IAllocationService>? allocationMock = null,
        Mock<IWfpExpenditureRepository>? wfpExpRepoMock = null)
    {
        divisions      ??= [];
        fundingSources ??= [];

        Mock<ILdipRepository> ldipRepo = new();
        ldipRepo.Setup(r => r.GetListAsync(It.IsAny<int?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((int? officeId, string? status, CancellationToken _) =>
                (IReadOnlyList<LdipRecord>)ldips
                    .Where(l => officeId == null || l.OfficeId == officeId)
                    .Where(l => string.IsNullOrWhiteSpace(status) || l.Status == status)
                    .ToList());

        Mock<IAipRepository> aipRepo = aipRepoMock ?? new Mock<IAipRepository>();
        aipRepo.Setup(r => r.GetDistinctFiscalYearsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyList<int>)aips.Select(a => a.FiscalYear).Distinct()
                .OrderByDescending(y => y).ToList());
        aipRepo.Setup(r => r.GetLatestByFiscalYearAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((int fy, CancellationToken _) => aips
                .Where(a => a.FiscalYear == fy && a.Status != PlanningStatus.Archived)
                .OrderBy(a => a.Id)
                .FirstOrDefault());
        if (aipRepoMock is null)
        {
            aipRepo.Setup(r => r.GetOfficesByAipIdAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((IReadOnlyList<AipOffice>)[]);
            aipRepo.Setup(r => r.GetProgramsByOfficeIdsAsync(It.IsAny<IReadOnlyList<int>>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((IReadOnlyList<AipProgram>)[]);
            aipRepo.Setup(r => r.GetProjectsByProgramIdsAsync(It.IsAny<IReadOnlyList<int>>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((IReadOnlyList<AipProject>)[]);
            aipRepo.Setup(r => r.GetActivitiesByProjectIdsAsync(It.IsAny<IReadOnlyList<int>>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((IReadOnlyList<AipActivity>)[]);
        }

        Mock<IWfpRepository> wfpRepo = new();
        wfpRepo.Setup(r => r.GetFilteredAsync(
                It.IsAny<int?>(), It.IsAny<int?>(), It.IsAny<int?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((int? aipRecordId, int? officeId, int? divisionId, CancellationToken _) =>
                (IReadOnlyList<WfpRecord>)wfps
                    .Where(w => aipRecordId == null || w.AipRecordId == aipRecordId)
                    .Where(w => officeId == null || w.OfficeId == officeId)
                    .Where(w => divisionId == null || w.DivisionId == divisionId)
                    .ToList());

        Mock<IOfficeRepository> officeRepo = new();
        officeRepo.Setup(r => r.GetByCodeAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string code, CancellationToken _) => offices.FirstOrDefault(o => o.OfficeCode == code));
        officeRepo.Setup(r => r.GetByIdAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((int id, CancellationToken _) => offices.FirstOrDefault(o => o.Id == id));

        Mock<IRepository<Division>> divisionRepo = new();
        divisionRepo.Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>())).ReturnsAsync(divisions);

        Mock<IRepository<FundingSource>> fundingSourceRepo = new();
        fundingSourceRepo.Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>())).ReturnsAsync(fundingSources);

        Mock<IWfpExpenditureRepository> wfpExpRepo = wfpExpRepoMock ?? new Mock<IWfpExpenditureRepository>();
        if (wfpExpRepoMock is null)
        {
            wfpExpRepo.Setup(r => r.GetActivityCoverageAsync(
                    It.IsAny<int>(), It.IsAny<int?>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new WfpActivityCoverageDto(0, 0));
        }

        Mock<IAuditRepository> auditRepo = new();
        auditRepo
            .Setup(r => r.GetRecentAsync(It.IsAny<int>(), It.IsAny<int?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(audits);

        Mock<IAllocationService> allocation = allocationMock ?? new Mock<IAllocationService>();
        if (allocationMock is null)
        {
            allocation.Setup(a => a.GetGeneralFundIdAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync(GfFundId);
            allocation.Setup(a => a.GetCeilingAsync(
                    It.IsAny<int>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(ServiceResult<BudgetCeilingDto>.NotFound("no ceiling"));
            allocation.Setup(a => a.GetCeilingsAsync(
                    It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((IReadOnlyList<BudgetCeilingDto>)[]);
            allocation.Setup(a => a.GetAllocationsAsync(
                    It.IsAny<int>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((IReadOnlyList<DivisionAllocationDto>)[]);
            allocation.Setup(a => a.GetAllocationsForAllFundsAsync(
                    It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((IReadOnlyList<DivisionAllocationDto>)[]);
            allocation.Setup(a => a.GetProgramAssignmentsAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((IReadOnlyList<ProgramAssignmentDto>)[]);
        }

        BudgetPlanningDashboardService svc = new(
            ldipRepo.Object, aipRepo.Object, wfpRepo.Object, wfpExpRepo.Object,
            officeRepo.Object, divisionRepo.Object, fundingSourceRepo.Object,
            auditRepo.Object, allocation.Object);

        return (svc, auditRepo);
    }

    // ── GetDashboardAsync — office resolution ─────────────────────────────

    [Fact]
    public async Task GetDashboardAsync_NoPpdoOfficeSeeded_Throws()
    {
        (BudgetPlanningDashboardService sut, _) = Build([], [], [], [], []);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => sut.GetDashboardAsync(fiscalYear: 2027, divisionId: null));
    }

    [Fact]
    public async Task GetDashboardAsync_ResolvesPpdoOfficeByCode_IgnoresOtherOffices()
    {
        List<Office> offices = [Off(1, "PPDO", code: "PPDO"), Off(2, "Other Office", code: "OTH")];
        (BudgetPlanningDashboardService sut, _) = Build([], [], [], offices, []);

        PpdoDashboardDto result = await sut.GetDashboardAsync(fiscalYear: 2027, divisionId: null);

        Assert.Equal(1, result.OfficeId);
        Assert.Equal("PPDO", result.OfficeCode);
    }

    // ── GetDashboardAsync — FY resolution ─────────────────────────────────

    [Fact]
    public async Task GetDashboardAsync_NoAipRecords_DefaultsToNextCalendarYear()
    {
        (BudgetPlanningDashboardService sut, _) = Build([], [], [], [Off(PpdoOfficeId, "PPDO")], []);

        PpdoDashboardDto result = await sut.GetDashboardAsync(fiscalYear: null, divisionId: null);

        Assert.Equal(DateTime.UtcNow.Year + 1, result.FiscalYear);
        Assert.Empty(result.AvailableFiscalYears);
    }

    [Fact]
    public async Task GetDashboardAsync_AipRecords_ReturnsDistinctFiscalYearsDescending()
    {
        List<AipRecord> aips = [Aip(1, 2027), Aip(2, 2026), Aip(3, 2027)];
        (BudgetPlanningDashboardService sut, _) = Build([], aips, [], [Off(PpdoOfficeId, "PPDO")], []);

        PpdoDashboardDto result = await sut.GetDashboardAsync(fiscalYear: null, divisionId: null);

        Assert.Equal([2027, 2026], result.AvailableFiscalYears);
    }

    // ── GetFiscalYearsAsync (RAL-166 follow-up) ───────────────────────────

    [Fact]
    public async Task GetFiscalYearsAsync_NoAipRecords_DefaultsToNextCalendarYear()
    {
        (BudgetPlanningDashboardService sut, _) = Build([], [], [], [], []);

        FiscalYearsDto result = await sut.GetFiscalYearsAsync(fiscalYear: null);

        Assert.Equal(DateTime.UtcNow.Year + 1, result.FiscalYear);
        Assert.Empty(result.AvailableFiscalYears);
    }

    [Fact]
    public async Task GetFiscalYearsAsync_AipRecords_ReturnsDistinctFiscalYearsDescending()
    {
        List<AipRecord> aips = [Aip(1, 2027), Aip(2, 2026), Aip(3, 2027)];
        (BudgetPlanningDashboardService sut, _) = Build([], aips, [], [], []);

        FiscalYearsDto result = await sut.GetFiscalYearsAsync(fiscalYear: null);

        Assert.Equal(2027, result.FiscalYear);
        Assert.Equal([2027, 2026], result.AvailableFiscalYears);
    }

    [Fact]
    public async Task GetFiscalYearsAsync_ExplicitFiscalYear_OverridesDefault()
    {
        List<AipRecord> aips = [Aip(1, 2027)];
        (BudgetPlanningDashboardService sut, _) = Build([], aips, [], [], []);

        FiscalYearsDto result = await sut.GetFiscalYearsAsync(fiscalYear: 2025);

        Assert.Equal(2025, result.FiscalYear);
    }

    [Fact]
    public async Task GetFiscalYearsAsync_DoesNotRequirePpdoOfficeSeeded()
    {
        // Unlike GetDashboardAsync, this must not throw when no "PPDO" office exists — it
        // never resolves an office at all.
        (BudgetPlanningDashboardService sut, _) = Build([], [], [], [], []);

        FiscalYearsDto result = await sut.GetFiscalYearsAsync(fiscalYear: 2027);

        Assert.Equal(2027, result.FiscalYear);
    }

    // ── GetDashboardAsync — LDIP / AIP counts (reuses the office-scoped builders) ──

    [Fact]
    public async Task GetDashboardAsync_LdipScopedToPpdoOffice()
    {
        List<LdipRecord> ldips =
        [
            Ldip(1, "Final", officeId: PpdoOfficeId),
            Ldip(2, "Draft", officeId: PpdoOfficeId),
            Ldip(3, "Draft", officeId: 999), // other office — excluded
        ];
        (BudgetPlanningDashboardService sut, _) = Build(ldips, [], [], [Off(PpdoOfficeId, "PPDO")], []);

        PpdoDashboardDto result = await sut.GetDashboardAsync(fiscalYear: 2027, divisionId: null);

        Assert.Equal(2, result.Ldip.Total);
    }

    // ── GetDashboardAsync — WFP by division ───────────────────────────────

    [Fact]
    public async Task GetDashboardAsync_ActiveDivisionWithNoWfpRow_StatusIsNotStarted()
    {
        List<AipRecord> aips = [Aip(10, 2027, "Final")];
        List<Office> offices = [Off(PpdoOfficeId, "PPDO")];
        List<Division> divisions = [Div(1, PpdoOfficeId, "Administrative")];
        (BudgetPlanningDashboardService sut, _) = Build([], aips, [], offices, [], divisions);

        PpdoDashboardDto result = await sut.GetDashboardAsync(fiscalYear: 2027, divisionId: null);

        Assert.Single(result.WfpByDivision);
        Assert.Equal("Not started", result.WfpByDivision[0].WfpStatus);
        Assert.Equal("Administrative", result.WfpByDivision[0].DivisionName);
    }

    [Fact]
    public async Task GetDashboardAsync_WfpRecordExists_ShowsItsStatus()
    {
        List<AipRecord> aips = [Aip(10, 2027, "Final")];
        List<Office> offices = [Off(PpdoOfficeId, "PPDO")];
        List<Division> divisions = [Div(1, PpdoOfficeId, "Administrative")];
        List<WfpRecord> wfps = [Wfp(1, aipId: 10, officeId: PpdoOfficeId, status: "Draft", divisionId: 1)];
        (BudgetPlanningDashboardService sut, _) = Build([], aips, wfps, offices, [], divisions);

        PpdoDashboardDto result = await sut.GetDashboardAsync(fiscalYear: 2027, divisionId: null);

        Assert.Single(result.WfpByDivision);
        Assert.Equal("Draft", result.WfpByDivision[0].WfpStatus);
    }

    [Fact]
    public async Task GetDashboardAsync_MultipleWfpRecordsSameDivision_AnyFinal_ShowsFinal()
    {
        List<AipRecord> aips = [Aip(10, 2027, "Final")];
        List<Office> offices = [Off(PpdoOfficeId, "PPDO")];
        List<Division> divisions = [Div(1, PpdoOfficeId, "Administrative")];
        List<WfpRecord> wfps =
        [
            Wfp(1, aipId: 10, officeId: PpdoOfficeId, status: "Draft", divisionId: 1,
                updatedAt: DateTime.UtcNow),
            Wfp(2, aipId: 10, officeId: PpdoOfficeId, status: "Final", divisionId: 1,
                updatedAt: DateTime.UtcNow.AddDays(-5)),
        ];
        (BudgetPlanningDashboardService sut, _) = Build([], aips, wfps, offices, [], divisions);

        PpdoDashboardDto result = await sut.GetDashboardAsync(fiscalYear: 2027, divisionId: null);

        Assert.Equal("Final", result.WfpByDivision[0].WfpStatus);
    }

    [Fact]
    public async Task GetDashboardAsync_ActivityCoverage_ReadFromRepository()
    {
        List<AipRecord> aips = [Aip(10, 2027, "Final")];
        List<Office> offices = [Off(PpdoOfficeId, "PPDO")];
        List<Division> divisions = [Div(1, PpdoOfficeId, "Administrative")];
        Mock<IWfpExpenditureRepository> wfpExpRepo = new();
        wfpExpRepo.Setup(r => r.GetActivityCoverageAsync(
                PpdoOfficeId, 1, 2027, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new WfpActivityCoverageDto(3, 8));
        (BudgetPlanningDashboardService sut, _) =
            Build([], aips, [], offices, [], divisions, wfpExpRepoMock: wfpExpRepo);

        PpdoDashboardDto result = await sut.GetDashboardAsync(fiscalYear: 2027, divisionId: null);

        Assert.Equal(3, result.WfpByDivision[0].ActivitiesWithExpenditures);
        Assert.Equal(8, result.WfpByDivision[0].TotalActivities);
    }

    // ── GetDashboardAsync — division clamp (RAL-161 / RAL-136 pattern) ────

    [Fact]
    public async Task GetDashboardAsync_DivisionIdSupplied_OnlyThatDivisionReturned()
    {
        List<Office> offices = [Off(PpdoOfficeId, "PPDO")];
        List<Division> divisions =
        [
            Div(1, PpdoOfficeId, "Administrative"),
            Div(2, PpdoOfficeId, "ICT"),
        ];
        (BudgetPlanningDashboardService sut, _) = Build([], [], [], offices, [], divisions);

        PpdoDashboardDto result = await sut.GetDashboardAsync(fiscalYear: 2027, divisionId: 2);

        Assert.Single(result.WfpByDivision);
        Assert.Equal(2, result.WfpByDivision[0].DivisionId);
        Assert.All(result.CeilingByFund, fund => Assert.Single(fund.ByDivision));
    }

    [Fact]
    public async Task GetDashboardAsync_NoDivisionIdSupplied_EveryActiveDivisionReturned()
    {
        List<Office> offices = [Off(PpdoOfficeId, "PPDO")];
        List<Division> divisions =
        [
            Div(1, PpdoOfficeId, "Administrative"),
            Div(2, PpdoOfficeId, "ICT"),
            Div(3, PpdoOfficeId, "Inactive Division", active: false),
        ];
        (BudgetPlanningDashboardService sut, _) = Build([], [], [], offices, [], divisions);

        PpdoDashboardDto result = await sut.GetDashboardAsync(fiscalYear: 2027, divisionId: null);

        Assert.Equal(2, result.WfpByDivision.Count); // inactive division excluded
    }

    // ── GetDashboardAsync — ceiling/allocation by fund ────────────────────

    [Fact]
    public async Task GetDashboardAsync_CeilingByFund_ComputesRemainingFromAllDivisions_EvenWhenClamped()
    {
        List<Office> offices = [Off(PpdoOfficeId, "PPDO")];
        List<Division> divisions =
        [
            Div(1, PpdoOfficeId, "Administrative"),
            Div(2, PpdoOfficeId, "ICT"),
        ];
        List<FundingSource> funds = [Fund(GfFundId, "GF", "General Fund")];

        Mock<IAllocationService> allocation = new();
        allocation.Setup(a => a.GetCeilingsAsync(PpdoOfficeId, 2027, It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyList<BudgetCeilingDto>)
                [new BudgetCeilingDto(1, PpdoOfficeId, 2027, GfFundId, "GF", "General Fund", 100_000m)]);
        allocation.Setup(a => a.GetAllocationsForAllFundsAsync(PpdoOfficeId, 2027, It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyList<DivisionAllocationDto>)
            [
                new DivisionAllocationDto(1, 1, "Administrative", 2027, GfFundId, "GF", "General Fund", 60_000m),
                new DivisionAllocationDto(2, 2, "ICT", 2027, GfFundId, "GF", "General Fund", 30_000m),
            ]);
        allocation.Setup(a => a.GetGeneralFundIdAsync(It.IsAny<CancellationToken>())).ReturnsAsync(GfFundId);
        allocation.Setup(a => a.GetProgramAssignmentsAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyList<ProgramAssignmentDto>)[]);
        allocation.Setup(a => a.GetCeilingAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ServiceResult<BudgetCeilingDto>.NotFound("n/a"));

        (BudgetPlanningDashboardService sut, _) = Build(
            [], [], [], offices, [], divisions, funds, allocationMock: allocation);

        // Clamped to division 1 only — Remaining must still reflect BOTH divisions' allocations.
        PpdoDashboardDto result = await sut.GetDashboardAsync(fiscalYear: 2027, divisionId: 1);

        FundCeilingDto gf = Assert.Single(result.CeilingByFund);
        Assert.Equal(100_000m, gf.Ceiling);
        Assert.Equal(10_000m, gf.Remaining); // 100,000 - (60,000 + 30,000), not just division 1's 60,000
        FundDivisionShareDto share = Assert.Single(gf.ByDivision);
        Assert.Equal(1, share.DivisionId);
        Assert.Equal(60_000m, share.Amount);
    }

    [Fact]
    public async Task GetDashboardAsync_MultipleFunds_CallsGetAllocationsForAllFundsOnce_NeverPerFundLoop()
    {
        // RAL-166 follow-up, round 2: GetAllocationsByFundAsync used to call GetAllocationsAsync
        // once PER active fund (3 queries each, so 3N total). With 3 funds here, the fixed path
        // must call the batched GetAllocationsForAllFundsAsync exactly once and never fall back
        // to the per-fund singular read.
        List<Office> offices = [Off(PpdoOfficeId, "PPDO")];
        List<Division> divisions = [Div(1, PpdoOfficeId, "Administrative")];
        List<FundingSource> funds =
        [
            Fund(GfFundId, "GF", "General Fund"),
            Fund(2, "GAD", "5% GAD Fund"),
            Fund(3, "LDRRM", "5% LDRRM Fund"),
        ];

        Mock<IAllocationService> allocation = new();
        allocation.Setup(a => a.GetCeilingsAsync(PpdoOfficeId, 2027, It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyList<BudgetCeilingDto>)[]);
        allocation.Setup(a => a.GetAllocationsForAllFundsAsync(PpdoOfficeId, 2027, It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyList<DivisionAllocationDto>)[]);
        allocation.Setup(a => a.GetGeneralFundIdAsync(It.IsAny<CancellationToken>())).ReturnsAsync(GfFundId);
        allocation.Setup(a => a.GetProgramAssignmentsAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyList<ProgramAssignmentDto>)[]);
        allocation.Setup(a => a.GetCeilingAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ServiceResult<BudgetCeilingDto>.NotFound("n/a"));

        (BudgetPlanningDashboardService sut, _) = Build(
            [], [], [], offices, [], divisions, funds, allocationMock: allocation);

        await sut.GetDashboardAsync(fiscalYear: 2027, divisionId: null);

        allocation.Verify(a => a.GetAllocationsForAllFundsAsync(
            PpdoOfficeId, 2027, It.IsAny<CancellationToken>()), Times.Once);
        allocation.Verify(a => a.GetAllocationsAsync(
            It.IsAny<int>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    // ── GetRecentActivityAsync ─────────────────────────────────────────────

    [Fact]
    public async Task GetRecentActivityAsync_NoOfficeFilter_ReturnsRepositoryResultsMapped()
    {
        User actor = AppUser(Guid.NewGuid(), "R. Alcaide");
        // Mock returns exactly 10 (as the real repo would after OrderBy+Take(10)).
        List<AuditLog> audits = Enumerable.Range(1, 10)
            .Select(i => Audit(i, actor, DateTime.UtcNow.AddMinutes(-i)))
            .ToList();
        (BudgetPlanningDashboardService sut, _) = Build([], [], [], [], audits);

        IReadOnlyList<RecentActivityDto> result = await sut.GetRecentActivityAsync(officeId: null);

        Assert.Equal(10, result.Count);
        Assert.Equal(1, result[0].Id);                    // first in list = id 1 (most recent)
        Assert.Equal("R. Alcaide", result[0].ActorName);  // actor name read from ChangedBy
    }

    [Fact]
    public async Task GetRecentActivityAsync_WithOfficeId_PassesOfficeIdToRepository()
    {
        (BudgetPlanningDashboardService sut, Mock<IAuditRepository> auditMock) = Build([], [], [], [], []);

        await sut.GetRecentActivityAsync(officeId: 5);

        // Service must forward officeId=5 and take=10 to the repository.
        auditMock.Verify(
            r => r.GetRecentAsync(10, 5, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task GetRecentActivityAsync_WithOfficeId_MapsReturnedAuditsToDto()
    {
        User alice = AppUser(Guid.NewGuid(), "Alice", officeId: 1);
        // Mock returns pre-filtered results (the real repo scopes by officeId in SQL).
        List<AuditLog> office1Audits =
        [
            Audit(1, alice, DateTime.UtcNow.AddMinutes(-1)),
            Audit(3, alice, DateTime.UtcNow.AddMinutes(-3)),
        ];
        (BudgetPlanningDashboardService sut, _) = Build([], [], [], [], office1Audits);

        IReadOnlyList<RecentActivityDto> result = await sut.GetRecentActivityAsync(officeId: 1);

        Assert.Equal(2, result.Count);
        Assert.All(result, r => Assert.Equal("Alice", r.ActorName));
    }

    [Fact]
    public async Task GetRecentActivityAsync_NullChangedBy_FallsBackToUnknown()
    {
        // ChangedBy can be null if the user was deleted after the audit entry was written.
        AuditLog orphaned = new()
        {
            Id = 99, TableName = "wfp_records", RecordId = 7, Action = "UPDATE",
            ChangedById = Guid.NewGuid(), ChangedAt = DateTime.UtcNow,
            ChangedBy = null,  // navigation not populated / user deleted
        };
        (BudgetPlanningDashboardService sut, _) = Build([], [], [], [], [orphaned]);

        IReadOnlyList<RecentActivityDto> result = await sut.GetRecentActivityAsync(officeId: null);

        Assert.Single(result);
        Assert.Equal("Unknown", result[0].ActorName);
    }

    [Fact]
    public async Task GetRecentActivityAsync_NoOfficeFilter_PassesNullOfficeIdToRepository()
    {
        (BudgetPlanningDashboardService sut, Mock<IAuditRepository> auditMock) = Build([], [], [], [], []);

        await sut.GetRecentActivityAsync(officeId: null);

        auditMock.Verify(
            r => r.GetRecentAsync(10, null, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    // ── GetOfficeDashboardAsync — allocation-setup summary (RAL-60) ───────────

    [Fact]
    public async Task GetOfficeDashboardAsync_EchoesOfficeIdAndFiscalYear()
    {
        (BudgetPlanningDashboardService sut, _) = Build([], [], [], [Off(1, "PPDO")], []);

        OfficeDashboardDto result = await sut.GetOfficeDashboardAsync(1, 2027);

        Assert.Equal(1, result.OfficeId);
        Assert.Equal(2027, result.FiscalYear);
    }

    [Fact]
    public async Task GetOfficeDashboardAsync_NoCeiling_CeilingAmountAndRemainingAreNull()
    {
        Mock<IAllocationService> allocation = new();
        allocation.Setup(a => a.GetGeneralFundIdAsync(It.IsAny<CancellationToken>())).ReturnsAsync(GfFundId);
        allocation.Setup(a => a.GetCeilingAsync(1, 2027, GfFundId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(ServiceResult<BudgetCeilingDto>.NotFound("no ceiling"));
        allocation.Setup(a => a.GetAllocationsAsync(1, 2027, GfFundId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyList<DivisionAllocationDto>)[]);
        allocation.Setup(a => a.GetProgramAssignmentsAsync(1, 2027, It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyList<ProgramAssignmentDto>)[]);
        (BudgetPlanningDashboardService sut, _) =
            Build([], [], [], [Off(1, "PPDO")], [], allocationMock: allocation);

        OfficeDashboardDto result = await sut.GetOfficeDashboardAsync(1, 2027);

        Assert.Null(result.Allocation.CeilingAmount);
        Assert.Null(result.Allocation.Remaining);
        Assert.False(result.Allocation.IsOverAllocated);
        Assert.Equal(0, result.Allocation.Allocated);
    }

    [Fact]
    public async Task GetOfficeDashboardAsync_UnderCeiling_ComputesRemainingAndNotOverAllocated()
    {
        Mock<IAllocationService> allocation = new();
        allocation.Setup(a => a.GetGeneralFundIdAsync(It.IsAny<CancellationToken>())).ReturnsAsync(GfFundId);
        allocation.Setup(a => a.GetCeilingAsync(1, 2027, GfFundId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(ServiceResult<BudgetCeilingDto>.Ok(new BudgetCeilingDto(1, 1, 2027, GfFundId, "GF", "General Fund", 100_000m)));
        allocation.Setup(a => a.GetAllocationsAsync(1, 2027, GfFundId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyList<DivisionAllocationDto>)
                [new DivisionAllocationDto(1, 1, "Div A", 2027, GfFundId, "GF", "General Fund", 60_000m)]);
        allocation.Setup(a => a.GetProgramAssignmentsAsync(1, 2027, It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyList<ProgramAssignmentDto>)[]);
        (BudgetPlanningDashboardService sut, _) =
            Build([], [], [], [Off(1, "PPDO")], [], allocationMock: allocation);

        OfficeDashboardDto result = await sut.GetOfficeDashboardAsync(1, 2027);

        Assert.Equal(100_000m, result.Allocation.CeilingAmount);
        Assert.Equal(60_000m, result.Allocation.Allocated);
        Assert.Equal(40_000m, result.Allocation.Remaining);
        Assert.False(result.Allocation.IsOverAllocated);
    }

    [Fact]
    public async Task GetOfficeDashboardAsync_OverCeiling_FlagsOverAllocated()
    {
        Mock<IAllocationService> allocation = new();
        allocation.Setup(a => a.GetGeneralFundIdAsync(It.IsAny<CancellationToken>())).ReturnsAsync(GfFundId);
        allocation.Setup(a => a.GetCeilingAsync(1, 2027, GfFundId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(ServiceResult<BudgetCeilingDto>.Ok(new BudgetCeilingDto(1, 1, 2027, GfFundId, "GF", "General Fund", 100_000m)));
        allocation.Setup(a => a.GetAllocationsAsync(1, 2027, GfFundId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyList<DivisionAllocationDto>)
            [
                new DivisionAllocationDto(1, 1, "Div A", 2027, GfFundId, "GF", "General Fund", 60_000m),
                new DivisionAllocationDto(2, 2, "Div B", 2027, GfFundId, "GF", "General Fund", 50_000m),
            ]);
        allocation.Setup(a => a.GetProgramAssignmentsAsync(1, 2027, It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyList<ProgramAssignmentDto>)[]);
        (BudgetPlanningDashboardService sut, _) =
            Build([], [], [], [Off(1, "PPDO")], [], allocationMock: allocation);

        OfficeDashboardDto result = await sut.GetOfficeDashboardAsync(1, 2027);

        Assert.Equal(110_000m, result.Allocation.Allocated);
        Assert.Equal(-10_000m, result.Allocation.Remaining);
        Assert.True(result.Allocation.IsOverAllocated);
    }

    [Fact]
    public async Task GetOfficeDashboardAsync_ProgramAssignments_CountsAssignedAndUnassigned()
    {
        Mock<IAllocationService> allocation = new();
        allocation.Setup(a => a.GetGeneralFundIdAsync(It.IsAny<CancellationToken>())).ReturnsAsync(GfFundId);
        allocation.Setup(a => a.GetCeilingAsync(1, 2027, GfFundId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(ServiceResult<BudgetCeilingDto>.NotFound("no ceiling"));
        allocation.Setup(a => a.GetAllocationsAsync(1, 2027, GfFundId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyList<DivisionAllocationDto>)[]);
        allocation.Setup(a => a.GetProgramAssignmentsAsync(1, 2027, It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyList<ProgramAssignmentDto>)
            [
                new ProgramAssignmentDto("013", "P1", "Program 1", "General", [1]),
                new ProgramAssignmentDto("013", "P2", "Program 2", "General", []),
                new ProgramAssignmentDto("013", "P3", "Program 3", "General", [1, 2]),
            ]);
        (BudgetPlanningDashboardService sut, _) =
            Build([], [], [], [Off(1, "PPDO")], [], allocationMock: allocation);

        OfficeDashboardDto result = await sut.GetOfficeDashboardAsync(1, 2027);

        Assert.Equal(2, result.Allocation.AssignedProgramCount);
        Assert.Equal(1, result.Allocation.UnassignedProgramCount);
    }

    // ── GetOfficeDashboardAsync — LDIP panel (office-scoped since RAL-61) ─────

    [Fact]
    public async Task GetOfficeDashboardAsync_LdipPanel_CountsOnlyThisOfficesRecords()
    {
        List<LdipRecord> ldips =
        [
            Ldip(1, "Final", officeId: 1),
            Ldip(2, "Draft", officeId: 1),
            Ldip(3, "Draft", officeId: 2),   // other office — excluded
        ];
        (BudgetPlanningDashboardService sut, _) = Build(ldips, [], [], [Off(1, "PPDO")], []);

        OfficeDashboardDto result = await sut.GetOfficeDashboardAsync(1, 2027);

        Assert.True(result.Ldip.ScopingSupported);
        Assert.Equal(2, result.Ldip.Total);
        Assert.Equal(1, result.Ldip.Breakdown.First(b => b.Status == "Final").Count);
        Assert.Equal(1, result.Ldip.Breakdown.First(b => b.Status == "Draft").Count);
    }

    [Fact]
    public async Task GetOfficeDashboardAsync_LdipPanel_ExcludesRecordsOutsideFiscalYearRange()
    {
        List<LdipRecord> ldips =
        [
            Ldip(1, "Draft", officeId: 1, fyStart: 2027, fyEnd: 2029),   // covers FY2027
            Ldip(2, "Draft", officeId: 1, fyStart: 2030, fyEnd: 2032),   // future range — excluded
        ];
        (BudgetPlanningDashboardService sut, _) = Build(ldips, [], [], [Off(1, "PPDO")], []);

        OfficeDashboardDto result = await sut.GetOfficeDashboardAsync(1, 2027);

        Assert.Equal(1, result.Ldip.Total);
    }

    // ── GetOfficeDashboardAsync — AIP presence + PPA/activity count ───────────

    [Fact]
    public async Task GetOfficeDashboardAsync_OfficeHasNoRefCode_AipDoesNotExist()
    {
        List<Office> offices = [Off(1, "PPDO", refCode: null)];
        (BudgetPlanningDashboardService sut, _) = Build([], [Aip(10, 2027, "Final")], [], offices, []);

        OfficeDashboardDto result = await sut.GetOfficeDashboardAsync(1, 2027);

        Assert.False(result.Aip.Exists);
        Assert.Null(result.Aip.Status);
        Assert.Equal(0, result.Aip.ProgramCount);
    }

    [Fact]
    public async Task GetOfficeDashboardAsync_NoAipRecordForFiscalYear_AipDoesNotExist()
    {
        List<Office> offices = [Off(1, "PPDO", refCode: "013")];
        List<AipRecord> aips = [Aip(10, 2026, "Final")]; // different FY
        (BudgetPlanningDashboardService sut, _) = Build([], aips, [], offices, []);

        OfficeDashboardDto result = await sut.GetOfficeDashboardAsync(1, 2027);

        Assert.False(result.Aip.Exists);
    }

    [Fact]
    public async Task GetOfficeDashboardAsync_ArchivedAipRecord_IsIgnored()
    {
        List<Office> offices = [Off(1, "PPDO", refCode: "013")];
        List<AipRecord> aips = [Aip(10, 2027, "Archived")];
        (BudgetPlanningDashboardService sut, _) = Build([], aips, [], offices, []);

        OfficeDashboardDto result = await sut.GetOfficeDashboardAsync(1, 2027);

        Assert.False(result.Aip.Exists);
    }

    [Fact]
    public async Task GetOfficeDashboardAsync_NoMatchingAipOfficeRefCode_AipDoesNotExist()
    {
        List<Office> offices = [Off(1, "PPDO", refCode: "013")];
        List<AipRecord> aips = [Aip(10, 2027, "Final")];
        Mock<IAipRepository> aipRepo = new();
        aipRepo.Setup(r => r.GetOfficesByAipIdAsync(10, It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyList<AipOffice>)[AipOff(100, 10, "3000-000-1-01-099")]);
        (BudgetPlanningDashboardService sut, _) =
            Build([], aips, [], offices, [], aipRepoMock: aipRepo);

        OfficeDashboardDto result = await sut.GetOfficeDashboardAsync(1, 2027);

        Assert.False(result.Aip.Exists);
    }

    [Fact]
    public async Task GetOfficeDashboardAsync_MatchingAipOffice_ReturnsProgramProjectActivityCounts()
    {
        List<Office> offices = [Off(1, "PPDO", refCode: "013")];
        List<AipRecord> aips = [Aip(10, 2027, "Final")];
        Mock<IAipRepository> aipRepo = new();
        aipRepo.Setup(r => r.GetOfficesByAipIdAsync(10, It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyList<AipOffice>)[AipOff(100, 10, "3000-000-1-01-013", "Social")]);
        aipRepo.Setup(r => r.GetProgramsByOfficeIdsAsync(
                It.Is<IReadOnlyList<int>>(ids => ids.SequenceEqual(new[] { 100 })),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyList<AipProgram>)[AipProg(200, 100, "3000-000-1-01-013-001")]);
        aipRepo.Setup(r => r.GetProjectsByProgramIdsAsync(
                It.Is<IReadOnlyList<int>>(ids => ids.SequenceEqual(new[] { 200 })),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyList<AipProject>)
            [
                AipProj(300, 200, "3000-000-1-01-013-001-001"),
                AipProj(301, 200, "3000-000-1-01-013-001-002"),
            ]);
        aipRepo.Setup(r => r.GetActivitiesByProjectIdsAsync(
                It.Is<IReadOnlyList<int>>(ids => ids.OrderBy(x => x).SequenceEqual(new[] { 300, 301 })),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyList<AipActivity>)
            [
                AipAct(400, 300, "3000-000-1-01-013-001-001-001"),
                AipAct(401, 300, "3000-000-1-01-013-001-001-002"),
                AipAct(402, 301, "3000-000-1-01-013-001-002-001"),
            ]);
        (BudgetPlanningDashboardService sut, _) =
            Build([], aips, [], offices, [], aipRepoMock: aipRepo);

        OfficeDashboardDto result = await sut.GetOfficeDashboardAsync(1, 2027);

        Assert.True(result.Aip.Exists);
        Assert.Equal("Final", result.Aip.Status);
        Assert.Equal(1, result.Aip.ProgramCount);
        Assert.Equal(2, result.Aip.ProjectCount);
        Assert.Equal(3, result.Aip.ActivityCount);
    }
}
