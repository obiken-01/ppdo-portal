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

    /// <summary>
    /// Builds an office. Defaults to the host office (DECISION F, RAL-258) because the dashboard
    /// resolves its subject office by that flag now, not by the code "PPDO".
    /// </summary>
    private static Office Off(int id, string name, bool active = true, string? refCode = null,
        string code = "PPDO", bool isHostOffice = true) => new()
    {
        Id = id, OfficeCode = code, OfficeName = name, IsActive = active,
        OfficeRefCode = refCode, IsHostOffice = isHostOffice,
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
    /// An <see cref="IAipRepository"/> mock whose four hierarchy reads all answer empty, with the
    /// AipOffice read stubbed to <paramref name="aipOffices"/>. Needed by any test that supplies
    /// its own aipRepoMock: <see cref="Build"/> only fills those defaults in when it creates the
    /// mock itself, so a bare mock returns null into BuildOfficeAipSummaryAsync's Select.
    /// </summary>
    private static Mock<IAipRepository> AipMockWithOffices(int aipRecordId, params AipOffice[] aipOffices)
    {
        Mock<IAipRepository> aipRepo = new();
        aipRepo.Setup(r => r.GetOfficesByAipIdAsync(aipRecordId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyList<AipOffice>)aipOffices);
        aipRepo.Setup(r => r.GetProgramsByOfficeIdsAsync(
                It.IsAny<IReadOnlyList<int>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyList<AipProgram>)[]);
        aipRepo.Setup(r => r.GetProjectsByProgramIdsAsync(
                It.IsAny<IReadOnlyList<int>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyList<AipProject>)[]);
        aipRepo.Setup(r => r.GetActivitiesByProjectIdsAsync(
                It.IsAny<IReadOnlyList<int>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyList<AipActivity>)[]);
        return aipRepo;
    }

    /// <summary>
    /// An <see cref="IAllocationService"/> mock answering every read with "nothing configured".
    /// <see cref="Build"/> uses it when no allocation mock is supplied; a test that needs to
    /// override ONE read calls this and re-stubs that one, rather than starting from a bare mock
    /// and having the other four return null mid-build.
    /// </summary>
    private static Mock<IAllocationService> AllocationMockWithDefaults()
    {
        Mock<IAllocationService> allocation = new();
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
        allocation.Setup(a => a.GetProgramAssignmentsAsync(
                It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyList<ProgramAssignmentDto>)[]);
        return allocation;
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
        Mock<IWfpExpenditureRepository>? wfpExpRepoMock = null,
        Mock<IWfpAllocationLedgerRepository>? ledgerRepoMock = null,
        Mock<IBudgetCeilingRepository>? ceilingRepoMock = null,
        Mock<IUserRepository>? userRepoMock = null,
        Mock<IPermissionService>? permissionsMock = null,
        List<AipOfficeRollupDto>? officeRollups = null,
        List<AipProgramRollupDto>? programRollups = null)
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
        // Rollups are set up on EVERY aipRepo, caller-supplied or not: a caller-supplied mock is
        // configured for the four hierarchy reads and would otherwise return null here. Tests that
        // need real rollup data pass them through the officeRollups/programRollups parameters
        // rather than configuring the mock themselves, so this setup can safely be unconditional.
        aipRepo.Setup(r => r.GetOfficeRollupsAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyList<AipOfficeRollupDto>)(officeRollups ?? []));
        aipRepo.Setup(r => r.GetProgramRollupsAsync(
                It.IsAny<IReadOnlyList<int>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyList<AipProgramRollupDto>)(programRollups ?? []));

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
        officeRepo.Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>())).ReturnsAsync(offices);
        officeRepo.Setup(r => r.GetByCodeAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string code, CancellationToken _) => offices.FirstOrDefault(o => o.OfficeCode == code));
        officeRepo.Setup(r => r.GetHostOfficeAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => offices.FirstOrDefault(o => o.IsHostOffice));
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

        Mock<IWfpAllocationLedgerRepository> ledgerRepo = ledgerRepoMock ?? new Mock<IWfpAllocationLedgerRepository>();
        if (ledgerRepoMock is null)
        {
            ledgerRepo.Setup(r => r.SumUsedAmountsByDivisionsAsync(
                    It.IsAny<IReadOnlyList<int>>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((IReadOnlyList<DivisionFundUsedAmountDto>)[]);
        }

        Mock<IAuditRepository> auditRepo = new();
        auditRepo
            .Setup(r => r.GetRecentAsync(
                It.IsAny<int>(), It.IsAny<int?>(), It.IsAny<IReadOnlyList<string>?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(audits);

        Mock<IAllocationService> allocation = allocationMock ?? AllocationMockWithDefaults();

        Mock<IBudgetCeilingRepository> ceilingRepo = ceilingRepoMock ?? new Mock<IBudgetCeilingRepository>();
        if (ceilingRepoMock is null)
        {
            ceilingRepo.Setup(r => r.GetByFiscalYearAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((IReadOnlyList<BudgetCeiling>)[]);
        }

        Mock<IUserRepository> userRepo = userRepoMock ?? new Mock<IUserRepository>();
        if (userRepoMock is null)
        {
            userRepo.Setup(r => r.GetReviewerNamesByOfficeAsync(
                    It.IsAny<IReadOnlyList<int>>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((IReadOnlyDictionary<int, string>)new Dictionary<int, string>());
        }

        // Default: no cross-office grant at all. Every GetOfficesAsync test opts in explicitly,
        // so a test that forgets to grant one gets the Forbidden path rather than a silent pass.
        Mock<IPermissionService> permissions = permissionsMock ?? new Mock<IPermissionService>();
        if (permissionsMock is null)
        {
            permissions.Setup(p => p.CanReviewAllOfficesAsync(It.IsAny<User>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(false);
            permissions.Setup(p => p.CanManagePboCeilingAsync(It.IsAny<User>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(false);
        }

        BudgetPlanningDashboardService svc = new(
            ldipRepo.Object, aipRepo.Object, wfpRepo.Object, wfpExpRepo.Object, ledgerRepo.Object,
            officeRepo.Object, divisionRepo.Object, fundingSourceRepo.Object,
            auditRepo.Object, allocation.Object,
            ceilingRepo.Object, userRepo.Object, permissions.Object);

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
        List<Office> offices =
            [Off(1, "PPDO", code: "PPDO"), Off(2, "Other Office", code: "OTH", isHostOffice: false)];
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

    // ── GetDashboardAsync — AIP by division (PPDO-20) ─────────────────────
    // These replace the WFP-by-division tests. The old DTO reported a WfpStatus and a count of
    // activities carrying a WFP expenditure; decisions 3 and 4 of the PPDO-20 spec retire both
    // from this page in favour of what the division has costed in the AIP.

    [Fact]
    public async Task GetDashboardAsync_DivisionWithNoAipWork_IsTodoWithZeroes()
    {
        List<AipRecord> aips = [Aip(10, 2027, "Final")];
        List<Office> offices = [Off(PpdoOfficeId, "PPDO", refCode: "1-01-010")];
        List<Division> divisions = [Div(1, PpdoOfficeId, "Administrative")];
        (BudgetPlanningDashboardService sut, _) = Build([], aips, [], offices, [], divisions);

        PpdoDashboardDto result = await sut.GetDashboardAsync(fiscalYear: 2027, divisionId: null);

        DivisionSummaryDto row = Assert.Single(result.ByDivision);
        Assert.Equal("Administrative", row.DivisionName);
        // The AIP record is Final, but this division contributed nothing to it. Todo, not Done —
        // reporting an absent division as complete is how it goes unnoticed until the deadline.
        Assert.Equal(PlanningStage.Todo, row.AipStatus);
        Assert.Equal(0, row.TotalActivities);
        Assert.Equal(0m, row.CostedInAip);
    }

    [Fact]
    public async Task GetDashboardAsync_DivisionWithAssignedProgram_SumsThatProgramsAipMoney()
    {
        List<AipRecord> aips = [Aip(10, 2027, "Draft")];
        List<Office> offices = [Off(PpdoOfficeId, "PPDO", refCode: "1-01-010")];
        List<Division> divisions = [Div(1, PpdoOfficeId, "Administrative")];

        Mock<IAipRepository> aipRepo = AipMockWithOffices(10, AipOff(50, 10, "1000-000-1-01-010"));

        Mock<IAllocationService> allocation = AllocationMockWithDefaults();
        allocation.Setup(a => a.GetProgramAssignmentsAsync(
                PpdoOfficeId, 2027, It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyList<ProgramAssignmentDto>)
                [new ProgramAssignmentDto("1000-000-1-01-010", "PROG-1", "Program 1", "General", [1])]);

        (BudgetPlanningDashboardService sut, _) = Build(
            [], aips, [], offices, [], divisions,
            aipRepoMock: aipRepo, allocationMock: allocation,
            programRollups: [new AipProgramRollupDto(50, "PROG-1", 4, 3, 750_000m)]);

        PpdoDashboardDto result = await sut.GetDashboardAsync(fiscalYear: 2027, divisionId: null);

        DivisionSummaryDto row = Assert.Single(result.ByDivision);
        Assert.Equal(750_000m, row.CostedInAip);
        Assert.Equal(3, row.CostedActivityCount);
        Assert.Equal(4, row.TotalActivities);
        Assert.Equal(PlanningStage.InProgress, row.AipStatus);
    }

    [Fact]
    public async Task GetDashboardAsync_UnassignedProgram_CountsAgainstNoDivision()
    {
        // An unassigned PPA is surfaced by the allocation-setup panel's "unassigned" count. It
        // must not be spread across divisions here, which would silently invent attribution.
        List<AipRecord> aips = [Aip(10, 2027, "Draft")];
        List<Office> offices = [Off(PpdoOfficeId, "PPDO", refCode: "1-01-010")];
        List<Division> divisions = [Div(1, PpdoOfficeId, "Administrative")];

        Mock<IAipRepository> aipRepo = AipMockWithOffices(10, AipOff(50, 10, "1000-000-1-01-010"));

        (BudgetPlanningDashboardService sut, _) = Build(
            [], aips, [], offices, [], divisions, aipRepoMock: aipRepo,
            programRollups: [new AipProgramRollupDto(50, "PROG-UNASSIGNED", 4, 3, 750_000m)]);

        PpdoDashboardDto result = await sut.GetDashboardAsync(fiscalYear: 2027, divisionId: null);

        Assert.Equal(0m, Assert.Single(result.ByDivision).CostedInAip);
    }

    [Fact]
    public async Task GetDashboardAsync_DivisionWithAllocationAndNoAip_RemainingEqualsAllocated()
    {
        // Named in the PPDO-20 test focus: Remaining must be the allocation, never null or zero.
        List<Office> offices = [Off(PpdoOfficeId, "PPDO", refCode: "1-01-010")];
        List<Division> divisions = [Div(1, PpdoOfficeId, "Administrative")];
        List<FundingSource> funds = [Fund(GfFundId, "GF", "General Fund")];

        Mock<IAllocationService> allocation = AllocationMockWithDefaults();
        allocation.Setup(a => a.GetAllocationsForAllFundsAsync(
                PpdoOfficeId, 2027, It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyList<DivisionAllocationDto>)
                [new DivisionAllocationDto(1, 1, "Administrative", 2027, GfFundId, "GF", "General Fund", 100_000m)]);

        (BudgetPlanningDashboardService sut, _) =
            Build([], [], [], offices, [], divisions, funds, allocationMock: allocation);

        PpdoDashboardDto result = await sut.GetDashboardAsync(fiscalYear: 2027, divisionId: null);

        DivisionSummaryDto row = Assert.Single(result.ByDivision);
        Assert.Equal(100_000m, row.Allocated);
        Assert.Equal(0m, row.CostedInAip);
        Assert.Equal(100_000m, row.Remaining);
    }

    [Fact]
    public async Task GetDashboardAsync_DivisionWithNullCode_StillReturnsItsName()
    {
        // Allocation_Requirements.md §5 makes the code optional with the name as the fallback
        // identifier — the UI must have a name to render instead of an empty pill.
        List<Office> offices = [Off(PpdoOfficeId, "PPDO", refCode: "1-01-010")];
        List<Division> divisions = [Div(1, PpdoOfficeId, "Administrative", code: null)];
        (BudgetPlanningDashboardService sut, _) = Build([], [], [], offices, [], divisions);

        PpdoDashboardDto result = await sut.GetDashboardAsync(fiscalYear: 2027, divisionId: null);

        DivisionSummaryDto row = Assert.Single(result.ByDivision);
        Assert.Null(row.DivisionCode);
        Assert.Equal("Administrative", row.DivisionName);
    }

    [Fact]
    public async Task GetDashboardAsync_SubmissionStatus_IsTodoUntilPhase4()
    {
        List<Office> offices = [Off(PpdoOfficeId, "PPDO", refCode: "1-01-010")];
        List<Division> divisions = [Div(1, PpdoOfficeId, "Administrative")];
        (BudgetPlanningDashboardService sut, _) = Build([], [], [], offices, [], divisions);

        PpdoDashboardDto result = await sut.GetDashboardAsync(fiscalYear: 2027, divisionId: null);

        Assert.Equal(PlanningStage.Todo, Assert.Single(result.ByDivision).SubmissionStatus);
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

        // The division clamp survives the WfpByDivision → ByDivision rename (PPDO-20 test focus).
        // This is the server-side mechanism behind "money and tables clamped to RMED" — it must be
        // carried over, not dropped with the old DTO.
        Assert.Single(result.ByDivision);
        Assert.Equal(2, result.ByDivision[0].DivisionId);
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

        Assert.Equal(2, result.ByDivision.Count); // inactive division excluded
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

    // ── GetDashboardAsync — WFP-by-division per-fund Remaining (RAL-176) ─────

    [Fact]
    public async Task GetDashboardAsync_DivisionFundAmount_RemainingIsAllocationMinusLedgerUsage()
    {
        // The dashboard's per-division Remaining must match what the WFP Entry Wizard shows for
        // the same division+fund (WfpCeilingService.GetStatusAsync: allocation - usedForFund) —
        // NOT the office-wide unallocated-ceiling figure on FundCeilingDto.Remaining.
        List<Office> offices = [Off(PpdoOfficeId, "PPDO")];
        List<Division> divisions = [Div(1, PpdoOfficeId, "Administrative")];
        List<FundingSource> funds = [Fund(GfFundId, "GF", "General Fund")];

        Mock<IAllocationService> allocation = new();
        allocation.Setup(a => a.GetCeilingsAsync(PpdoOfficeId, 2027, It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyList<BudgetCeilingDto>)[]);
        allocation.Setup(a => a.GetAllocationsForAllFundsAsync(PpdoOfficeId, 2027, It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyList<DivisionAllocationDto>)
                [new DivisionAllocationDto(1, 1, "Administrative", 2027, GfFundId, "GF", "General Fund", 100_000m)]);
        allocation.Setup(a => a.GetGeneralFundIdAsync(It.IsAny<CancellationToken>())).ReturnsAsync(GfFundId);
        allocation.Setup(a => a.GetProgramAssignmentsAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyList<ProgramAssignmentDto>)[]);
        allocation.Setup(a => a.GetCeilingAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ServiceResult<BudgetCeilingDto>.NotFound("n/a"));

        Mock<IWfpAllocationLedgerRepository> ledger = new();
        ledger.Setup(l => l.SumUsedAmountsByDivisionsAsync(
                It.IsAny<IReadOnlyList<int>>(), 2027, It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyList<DivisionFundUsedAmountDto>)
                [new DivisionFundUsedAmountDto(1, GfFundId, 35_000m)]);

        (BudgetPlanningDashboardService sut, _) = Build(
            [], [], [], offices, [], divisions, funds, allocationMock: allocation, ledgerRepoMock: ledger);

        PpdoDashboardDto result = await sut.GetDashboardAsync(fiscalYear: 2027, divisionId: null);

        DivisionFundAmountDto fund = Assert.Single(result.ByDivision[0].AllocationByFund);
        Assert.Equal(100_000m, fund.Amount);
        Assert.Equal(35_000m, fund.Used);
        Assert.Equal(65_000m, fund.Remaining); // 100,000 - 35,000, independent of any fund ceiling
    }

    [Fact]
    public async Task GetDashboardAsync_MultipleDivisionsAndFunds_CallsSumUsedAmountsByDivisionsOnce_NeverPerDivisionFundLoop()
    {
        // N+1 guard: the naive fix would call SumUsedAmountAsync once per division per fund
        // (2 divisions x 3 funds = 6 queries here). Must be exactly one batched call instead.
        List<Office> offices = [Off(PpdoOfficeId, "PPDO")];
        List<Division> divisions =
        [
            Div(1, PpdoOfficeId, "Administrative"),
            Div(2, PpdoOfficeId, "ICT"),
        ];
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

        Mock<IWfpAllocationLedgerRepository> ledger = new();
        ledger.Setup(l => l.SumUsedAmountsByDivisionsAsync(
                It.IsAny<IReadOnlyList<int>>(), 2027, It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyList<DivisionFundUsedAmountDto>)[]);

        (BudgetPlanningDashboardService sut, _) = Build(
            [], [], [], offices, [], divisions, funds, allocationMock: allocation, ledgerRepoMock: ledger);

        await sut.GetDashboardAsync(fiscalYear: 2027, divisionId: null);

        ledger.Verify(l => l.SumUsedAmountsByDivisionsAsync(
            It.IsAny<IReadOnlyList<int>>(), 2027, It.IsAny<CancellationToken>()), Times.Once);
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
            r => r.GetRecentAsync(10, 5, It.IsAny<IReadOnlyList<string>?>(), It.IsAny<CancellationToken>()),
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
            r => r.GetRecentAsync(10, null, It.IsAny<IReadOnlyList<string>?>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task GetRecentActivityAsync_ScopesToBudgetPlanningTables_ExcludingUsersAndConfig()
    {
        (BudgetPlanningDashboardService sut, Mock<IAuditRepository> auditMock) = Build([], [], [], [], []);

        await sut.GetRecentActivityAsync(officeId: null);

        // Recent Activity is Budget Planning-only -- User Management and Config activity
        // surface instead on the dedicated Audit Log page (RAL-174), not here.
        auditMock.Verify(
            r => r.GetRecentAsync(
                10, null,
                It.Is<IReadOnlyList<string>?>(names =>
                    names != null &&
                    names.Contains("wfp_records") &&
                    names.Contains("aip_records") &&
                    names.Contains("ldip_records") &&
                    names.Contains("division_allocations") &&
                    !names.Contains("users") &&
                    !names.Contains("accounts")),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task GetRecentActivityAsync_ChangedAtHasUnspecifiedKind_DtoStampsItUtc()
    {
        // Mirrors what EF Core actually returns after a SQL Server datetime2 round-trip —
        // DateTimeKind.Unspecified, even though AuditService always writes DateTime.UtcNow.
        DateTime unspecified = DateTime.SpecifyKind(new DateTime(2026, 7, 17, 7, 58, 41), DateTimeKind.Unspecified);
        AuditLog audit = Audit(1, at: unspecified);
        (BudgetPlanningDashboardService sut, _) = Build([], [], [], [], [audit]);

        IReadOnlyList<RecentActivityDto> result = await sut.GetRecentActivityAsync(officeId: null);

        // Without the Utc re-stamp, System.Text.Json omits the "Z" suffix and the browser's
        // new Date(...) misparses the value as local time instead of UTC.
        Assert.Equal(DateTimeKind.Utc, result[0].ChangedAt.Kind);
        Assert.Equal(unspecified, result[0].ChangedAt, TimeSpan.FromSeconds(1));
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

    // ── GetOfficesAsync — scope resolution (PPDO-20, ticket B) ────────────
    // The load-bearing part of the endpoint. A wrong resolver here compiles cleanly and leaks
    // data, which is why these are written first and why "own office only" is asserted to be an
    // INVALID result for either grant — that outcome means OfficeScope.Resolve was used.

    /// <summary>Caller in a guest office, so <c>OfficeScope.Resolve</c> would scope them to it.</summary>
    private static User GuestOfficeCaller(int officeId, Office office) => new()
    {
        Id = Guid.NewGuid(), FullName = "Guest", Username = "guest", PasswordHash = "x",
        OfficeId = officeId, Office = office,
    };

    private static BudgetCeiling Ceiling(int id, int officeId, int fundingSourceId, decimal amount,
        int fiscalYear = 2028) => new()
    {
        Id = id, OfficeId = officeId, FiscalYear = fiscalYear,
        FundingSourceId = fundingSourceId, Amount = amount,
    };

    private static (BudgetPlanningDashboardService Svc, User Caller) BuildForOffices(
        List<Office> offices,
        bool canReviewAllOffices = false,
        bool canManagePboCeiling = false,
        List<AipRecord>? aips = null,
        List<AipOfficeRollupDto>? officeRollups = null,
        Mock<IBudgetCeilingRepository>? ceilingRepoMock = null,
        Mock<IUserRepository>? userRepoMock = null,
        Mock<IAipRepository>? aipRepoMock = null)
    {
        // Deliberately a GUEST-office caller in every case: OfficeScope.Resolve would scope them
        // to their own office, so "every office came back" is real evidence the cross-office
        // resolver ran, not an artefact of the caller happening to sit in the host office.
        Office guest = offices.First(o => !o.IsHostOffice);
        User caller = GuestOfficeCaller(guest.Id, guest);

        Mock<IPermissionService> permissions = new();
        permissions.Setup(p => p.CanReviewAllOfficesAsync(It.IsAny<User>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(canReviewAllOffices);
        permissions.Setup(p => p.CanManagePboCeilingAsync(It.IsAny<User>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(canManagePboCeiling);

        (BudgetPlanningDashboardService svc, _) = Build(
            [], aips ?? [], [], offices, [],
            aipRepoMock: aipRepoMock,
            ceilingRepoMock: ceilingRepoMock,
            userRepoMock: userRepoMock,
            permissionsMock: permissions,
            officeRollups: officeRollups);

        return (svc, caller);
    }

    private static List<Office> TwoOffices() =>
    [
        Off(1, "Provincial Planning and Development Office", code: "PPDO"),
        Off(2, "General Services Office", code: "GSO", isHostOffice: false),
    ];

    [Fact]
    public async Task GetOfficesAsync_CanReviewAllOffices_ReturnsEveryOffice()
    {
        (BudgetPlanningDashboardService sut, User caller) =
            BuildForOffices(TwoOffices(), canReviewAllOffices: true);

        ServiceResult<IReadOnlyList<OfficeSummaryDto>> result = await sut.GetOfficesAsync(caller, 2028);

        Assert.True(result.IsSuccess);
        // Not one row. The caller sits in GSO; one row would mean OfficeScope.Resolve was used
        // and the cross-office grant silently did nothing.
        Assert.Equal(2, result.Value!.Count);
    }

    [Fact]
    public async Task GetOfficesAsync_CanManagePboCeiling_ReturnsEveryOffice()
    {
        (BudgetPlanningDashboardService sut, User caller) =
            BuildForOffices(TwoOffices(), canManagePboCeiling: true);

        ServiceResult<IReadOnlyList<OfficeSummaryDto>> result = await sut.GetOfficesAsync(caller, 2028);

        Assert.True(result.IsSuccess);
        Assert.Equal(2, result.Value!.Count);
    }

    [Fact]
    public async Task GetOfficesAsync_NeitherGrant_IsForbidden_NotAnEmptyList()
    {
        // An empty list would read as "no offices exist". A caller legitimately scoped to one
        // office has GetOfficeDashboardAsync to call instead.
        (BudgetPlanningDashboardService sut, User caller) = BuildForOffices(TwoOffices());

        ServiceResult<IReadOnlyList<OfficeSummaryDto>> result = await sut.GetOfficesAsync(caller, 2028);

        Assert.False(result.IsSuccess);
        Assert.Equal(ServiceErrorCode.Forbidden, result.Code);
    }

    [Fact]
    public async Task GetOfficesAsync_InactiveOffice_IsExcluded()
    {
        List<Office> offices =
        [
            Off(1, "PPDO", code: "PPDO"),
            Off(2, "Retired Office", code: "OLD", active: false, isHostOffice: false),
        ];
        (BudgetPlanningDashboardService sut, User caller) =
            BuildForOffices(offices, canReviewAllOffices: true);

        ServiceResult<IReadOnlyList<OfficeSummaryDto>> result = await sut.GetOfficesAsync(caller, 2028);

        Assert.Single(result.Value!);
    }

    // ── GetOfficesAsync — row content ─────────────────────────────────────

    [Fact]
    public async Task GetOfficesAsync_NoCeilingPublished_CeilingIsNull_NotZero()
    {
        // Null = PBO has not published. 0 = a published decision. Stage 1 renders differently
        // for each, so the two must not be coalesced.
        (BudgetPlanningDashboardService sut, User caller) =
            BuildForOffices(TwoOffices(), canManagePboCeiling: true);

        ServiceResult<IReadOnlyList<OfficeSummaryDto>> result = await sut.GetOfficesAsync(caller, 2028);

        Assert.All(result.Value!, row => Assert.Null(row.CeilingAmount));
    }

    [Fact]
    public async Task GetOfficesAsync_CeilingsAcrossFunds_AreSummedPerOffice()
    {
        Mock<IBudgetCeilingRepository> ceilingRepo = new();
        ceilingRepo.Setup(r => r.GetByFiscalYearAsync(2028, It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyList<BudgetCeiling>)
            [
                Ceiling(1, officeId: 2, fundingSourceId: 1, amount: 400_000m),
                Ceiling(2, officeId: 2, fundingSourceId: 2, amount: 100_000m),
            ]);

        (BudgetPlanningDashboardService sut, User caller) = BuildForOffices(
            TwoOffices(), canManagePboCeiling: true, ceilingRepoMock: ceilingRepo);

        ServiceResult<IReadOnlyList<OfficeSummaryDto>> result = await sut.GetOfficesAsync(caller, 2028);

        OfficeSummaryDto gso = result.Value!.Single(r => r.OfficeCode == "GSO");
        Assert.Equal(500_000m, gso.CeilingAmount);
        OfficeSummaryDto ppdo = result.Value!.Single(r => r.OfficeCode == "PPDO");
        Assert.Null(ppdo.CeilingAmount);
    }

    [Fact]
    public async Task GetOfficesAsync_CostedAboveCeiling_FlagsOverCeiling()
    {
        List<Office> offices =
        [
            Off(1, "PPDO", code: "PPDO", refCode: "1-01-010"),
            Off(2, "GSO", code: "GSO", refCode: "1-02-020", isHostOffice: false),
        ];

        Mock<IBudgetCeilingRepository> ceilingRepo = new();
        ceilingRepo.Setup(r => r.GetByFiscalYearAsync(2028, It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyList<BudgetCeiling>)
                [Ceiling(1, officeId: 2, fundingSourceId: 1, amount: 100_000m)]);

        (BudgetPlanningDashboardService sut, User caller) = BuildForOffices(
            offices, canReviewAllOffices: true,
            aips: [Aip(10, 2028, "Draft")],
            aipRepoMock: AipMockWithOffices(10),
            ceilingRepoMock: ceilingRepo,
            officeRollups: [new AipOfficeRollupDto(50, "1000-000-1-02-020", 3, 3, 150_000m)]);

        ServiceResult<IReadOnlyList<OfficeSummaryDto>> result = await sut.GetOfficesAsync(caller, 2028);

        OfficeSummaryDto gso = result.Value!.Single(r => r.OfficeCode == "GSO");
        Assert.Equal(150_000m, gso.CostedInAip);
        Assert.Equal(3, gso.ActivityCount);
        Assert.True(gso.IsOverCeiling);
        Assert.Equal(PlanningStage.InProgress, gso.AipStatus);

        // No ceiling published for PPDO — nothing to be over, whatever it has costed.
        Assert.False(result.Value!.Single(r => r.OfficeCode == "PPDO").IsOverCeiling);
    }

    [Fact]
    public async Task GetOfficesAsync_OfficeWithNoReviewer_ReturnsNullReviewerName()
    {
        Mock<IUserRepository> userRepo = new();
        userRepo.Setup(r => r.GetReviewerNamesByOfficeAsync(
                It.IsAny<IReadOnlyList<int>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyDictionary<int, string>)new Dictionary<int, string> { [1] = "R. Alcaide" });

        (BudgetPlanningDashboardService sut, User caller) = BuildForOffices(
            TwoOffices(), canReviewAllOffices: true, userRepoMock: userRepo);

        ServiceResult<IReadOnlyList<OfficeSummaryDto>> result = await sut.GetOfficesAsync(caller, 2028);

        Assert.Equal("R. Alcaide", result.Value!.Single(r => r.OfficeCode == "PPDO").ReviewerName);
        // Null, not "" — the row's "Cannot submit / None — assign" state.
        Assert.Null(result.Value!.Single(r => r.OfficeCode == "GSO").ReviewerName);
    }

    [Fact]
    public async Task GetOfficesAsync_SubmissionStatus_IsTodoUntilPhase4()
    {
        (BudgetPlanningDashboardService sut, User caller) =
            BuildForOffices(TwoOffices(), canReviewAllOffices: true);

        ServiceResult<IReadOnlyList<OfficeSummaryDto>> result = await sut.GetOfficesAsync(caller, 2028);

        Assert.All(result.Value!, row => Assert.Equal(PlanningStage.Todo, row.SubmissionStatus));
    }

    [Fact]
    public async Task GetOfficesAsync_NoAipForTheYear_EveryRowIsTodo()
    {
        (BudgetPlanningDashboardService sut, User caller) =
            BuildForOffices(TwoOffices(), canReviewAllOffices: true);

        ServiceResult<IReadOnlyList<OfficeSummaryDto>> result = await sut.GetOfficesAsync(caller, 2029);

        Assert.All(result.Value!, row =>
        {
            Assert.Equal(PlanningStage.Todo, row.AipStatus);
            Assert.Equal(0m, row.CostedInAip);
        });
    }

    [Fact]
    public async Task GetOfficesAsync_HostOfficeSortsFirst()
    {
        List<Office> offices =
        [
            Off(2, "General Services Office", code: "GSO", isHostOffice: false),
            Off(1, "Provincial Planning and Development Office", code: "PPDO"),
        ];
        (BudgetPlanningDashboardService sut, User caller) =
            BuildForOffices(offices, canReviewAllOffices: true);

        ServiceResult<IReadOnlyList<OfficeSummaryDto>> result = await sut.GetOfficesAsync(caller, 2028);

        Assert.True(result.Value![0].IsHostOffice);
        Assert.Equal("PPDO", result.Value![0].OfficeCode);
    }

    [Fact]
    public async Task GetDashboardAsync_ProgramAssignedToTwoDivisions_CountsInBoth_ButNotInTheOfficeTotal()
    {
        // The one case where the per-division column is deliberately NOT additive. A shared PPA is
        // each division's responsibility in full, so both rows carry it — but the office's own
        // total must still be the real figure, or the dashboard shows two different activity
        // counts for the same office on one screen. Found live: the rail read 140 while the office
        // table read 139, because one PPDO program is assigned to two divisions.
        List<AipRecord> aips = [Aip(10, 2027, "Draft")];
        List<Office> offices = [Off(PpdoOfficeId, "PPDO", refCode: "1-01-010")];
        List<Division> divisions =
        [
            Div(1, PpdoOfficeId, "Administrative"),
            Div(2, PpdoOfficeId, "ICT"),
        ];

        Mock<IAipRepository> aipRepo = AipMockWithOffices(10, AipOff(50, 10, "1000-000-1-01-010"));
        // The office's real hierarchy: one program, one project, two activities worth ₱100 total.
        aipRepo.Setup(r => r.GetProgramsByOfficeIdsAsync(
                It.IsAny<IReadOnlyList<int>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyList<AipProgram>)[AipProg(60, 50, "PROG-1")]);
        aipRepo.Setup(r => r.GetProjectsByProgramIdsAsync(
                It.IsAny<IReadOnlyList<int>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyList<AipProject>)[AipProj(70, 60, "PROJ-1")]);
        aipRepo.Setup(r => r.GetActivitiesByProjectIdsAsync(
                It.IsAny<IReadOnlyList<int>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyList<AipActivity>)
            [
                new AipActivity { Id = 80, ProjectId = 70, RefCode = "A1", Name = "A1", Total = 60m },
                new AipActivity { Id = 81, ProjectId = 70, RefCode = "A2", Name = "A2", Total = 40m },
            ]);

        Mock<IAllocationService> allocation = AllocationMockWithDefaults();
        allocation.Setup(a => a.GetProgramAssignmentsAsync(
                PpdoOfficeId, 2027, It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyList<ProgramAssignmentDto>)
                [new ProgramAssignmentDto("1000-000-1-01-010", "PROG-1", "Program 1", "General", [1, 2])]);

        (BudgetPlanningDashboardService sut, _) = Build(
            [], aips, [], offices, [], divisions,
            aipRepoMock: aipRepo, allocationMock: allocation,
            programRollups: [new AipProgramRollupDto(50, "PROG-1", 2, 2, 100m)]);

        PpdoDashboardDto result = await sut.GetDashboardAsync(fiscalYear: 2027, divisionId: null);

        // Both divisions carry the whole program — this is the documented rule, not a bug.
        Assert.All(result.ByDivision, row =>
        {
            Assert.Equal(2, row.TotalActivities);
            Assert.Equal(100m, row.CostedInAip);
        });
        Assert.Equal(200m, result.ByDivision.Sum(r => r.CostedInAip)); // the sum overstates…

        // …and the office's own figures do not. This is what the dashboard tiles read.
        Assert.Equal(2, result.Aip.ActivityCount);
        Assert.Equal(100m, result.Aip.CostedInAip);
    }
}
