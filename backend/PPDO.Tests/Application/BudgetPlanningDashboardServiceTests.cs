using Moq;
using PPDO.Application.Common;
using PPDO.Application.DTOs.BudgetPlanning;
using PPDO.Application.Services;
using PPDO.Domain.Entities;
using PPDO.Domain.Interfaces;

namespace PPDO.Tests.Application;

/// <summary>
/// Unit tests for <see cref="BudgetPlanningDashboardService"/> (RAL-80, RAL-92).
/// All repositories are mocked — no database access occurs.
/// GetRecentActivityAsync tests use <see cref="IAuditRepository.GetRecentAsync"/>; actor
/// names are read from the <see cref="AuditLog.ChangedBy"/> navigation populated by the mock,
/// mirroring what the real <see cref="AuditRepository"/> returns via its Include(a=>a.ChangedBy).
/// </summary>
public sealed class BudgetPlanningDashboardServiceTests
{
    // ── Helpers ──────────────────────────────────────────────────────────────

    private static LdipRecord Ldip(int id, string status) => new()
    {
        Id = id, Status = status, RefCode = $"LDIP-{id}", Title = "T",
        EntryMode = "New", FiscalYearStart = 2027, FiscalYearEnd = 2029,
        CreatedById = Guid.NewGuid(), CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow,
    };

    private static AipRecord Aip(int id, int fiscalYear, string status = "Draft") => new()
    {
        Id = id, FiscalYear = fiscalYear, Status = status, EntrySource = "Upload",
        UploadedById = Guid.NewGuid(), UploadedAt = DateTime.UtcNow,
    };

    private static WfpRecord Wfp(int id, int aipId, int officeId, string status = "Draft", int fy = 2027) => new()
    {
        Id = id, AipRecordId = aipId, OfficeId = officeId, FiscalYear = fy, Status = status,
        CreatedById = Guid.NewGuid(), CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow,
    };

    private static Office Off(int id, string name, bool active = true, string? refCode = null) => new()
    {
        Id = id, OfficeCode = $"O{id}", OfficeName = name, IsActive = active,
        OfficeRefCode = refCode,
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
    /// Builds a service with mocked dependencies.
    /// The audit mock is returned so callers can call Verify() on it.
    /// By default GetRecentAsync returns the supplied <paramref name="audits"/> list
    /// for any (take, officeId) combination — tests control what's in the list.
    ///
    /// <paramref name="aipRepoMock"/> / <paramref name="allocationMock"/> let
    /// office-dashboard tests inject custom hierarchy/allocation behaviour; when
    /// omitted, defaults return empty results so existing global-dashboard tests
    /// are unaffected by the extra constructor dependencies.
    /// </summary>
    private static (BudgetPlanningDashboardService svc, Mock<IAuditRepository> auditMock) Build(
        List<LdipRecord> ldips,
        List<AipRecord> aips,
        List<WfpRecord> wfps,
        List<Office> offices,
        List<AuditLog> audits,
        Mock<IAipRepository>? aipRepoMock = null,
        Mock<IAllocationService>? allocationMock = null)
    {
        Mock<IRepository<LdipRecord>> ldipRepo = new();
        ldipRepo.Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>())).ReturnsAsync(ldips);

        Mock<IAipRepository> aipRepo = aipRepoMock ?? new Mock<IAipRepository>();
        aipRepo.Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>())).ReturnsAsync(aips);
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

        Mock<IRepository<WfpRecord>> wfpRepo = new();
        wfpRepo.Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>())).ReturnsAsync(wfps);

        Mock<IRepository<Office>> officeRepo = new();
        officeRepo.Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>())).ReturnsAsync(offices);

        Mock<IAuditRepository> auditRepo = new();
        auditRepo
            .Setup(r => r.GetRecentAsync(It.IsAny<int>(), It.IsAny<int?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(audits);

        Mock<IAllocationService> allocation = allocationMock ?? new Mock<IAllocationService>();
        if (allocationMock is null)
        {
            allocation.Setup(a => a.GetCeilingAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(ServiceResult<BudgetCeilingDto>.NotFound("no ceiling"));
            allocation.Setup(a => a.GetAllocationsAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((IReadOnlyList<DivisionAllocationDto>)[]);
            allocation.Setup(a => a.GetProgramAssignmentsAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((IReadOnlyList<ProgramAssignmentDto>)[]);
            allocation.Setup(a => a.GetSetupOverviewAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new AllocationSetupOverviewDto(0, 0, 0, 0));
        }

        BudgetPlanningDashboardService svc = new(
            ldipRepo.Object, aipRepo.Object, wfpRepo.Object,
            officeRepo.Object, auditRepo.Object, allocation.Object);

        return (svc, auditRepo);
    }

    // ── GetDashboardAsync — FY resolution ─────────────────────────────────

    [Fact]
    public async Task GetDashboardAsync_NoAipRecords_DefaultsToNextCalendarYear()
    {
        (BudgetPlanningDashboardService sut, _) = Build([], [], [], [], []);

        PlanningDashboardDto result = await sut.GetDashboardAsync(fiscalYear: null);

        Assert.Equal(DateTime.UtcNow.Year + 1, result.FiscalYear);
        Assert.Empty(result.AvailableFiscalYears);
    }

    [Fact]
    public async Task GetDashboardAsync_AipRecords_ReturnsDistinctFiscalYearsDescending()
    {
        List<AipRecord> aips = [Aip(1, 2027), Aip(2, 2026), Aip(3, 2027)];
        (BudgetPlanningDashboardService sut, _) = Build([], aips, [], [], []);

        PlanningDashboardDto result = await sut.GetDashboardAsync(fiscalYear: null);

        Assert.Equal([2027, 2026], result.AvailableFiscalYears);
    }

    // ── GetDashboardAsync — LDIP / AIP counts ─────────────────────────────

    [Fact]
    public async Task GetDashboardAsync_LdipGroupsByStatus()
    {
        List<LdipRecord> ldips = [Ldip(1, "Final"), Ldip(2, "Draft"), Ldip(3, "Archived")];
        (BudgetPlanningDashboardService sut, _) = Build(ldips, [], [], [], []);

        PlanningDashboardDto result = await sut.GetDashboardAsync(fiscalYear: 2027);

        Assert.Equal(3, result.Ldip.Total);
        Assert.Equal(3, result.Ldip.Breakdown.Count);
    }

    [Fact]
    public async Task GetDashboardAsync_AipFiltersByFiscalYear()
    {
        List<AipRecord> aips = [Aip(1, 2027), Aip(2, 2026)];
        (BudgetPlanningDashboardService sut, _) = Build([], aips, [], [], []);

        PlanningDashboardDto result = await sut.GetDashboardAsync(fiscalYear: 2027);

        Assert.Equal(1, result.Aip.Total);
    }

    // ── GetDashboardAsync — WFP by office ─────────────────────────────────

    [Fact]
    public async Task GetDashboardAsync_ActiveOfficeWithNoWfpRow_StatusIsNotStarted()
    {
        List<AipRecord> aips = [Aip(10, 2027, "Final")];
        List<Office> offices = [Off(1, "PPDO")];
        (BudgetPlanningDashboardService sut, _) = Build([], aips, [], offices, []);

        PlanningDashboardDto result = await sut.GetDashboardAsync(fiscalYear: 2027);

        Assert.Single(result.WfpByOffice);
        Assert.Equal("Not started", result.WfpByOffice[0].WfpStatus);
        Assert.Equal("PPDO", result.WfpByOffice[0].OfficeName);
    }

    [Fact]
    public async Task GetDashboardAsync_WfpRecordExists_ShowsItsStatus()
    {
        List<AipRecord> aips = [Aip(10, 2027, "Final")];
        List<Office> offices = [Off(1, "PPDO")];
        List<WfpRecord> wfps = [Wfp(1, aipId: 10, officeId: 1, status: "Draft")];
        (BudgetPlanningDashboardService sut, _) = Build([], aips, wfps, offices, []);

        PlanningDashboardDto result = await sut.GetDashboardAsync(fiscalYear: 2027);

        Assert.Single(result.WfpByOffice);
        Assert.Equal("Draft", result.WfpByOffice[0].WfpStatus);
        Assert.Equal(10, result.WfpByOffice[0].AipRecordId);
    }

    [Fact]
    public async Task GetDashboardAsync_WfpByOfficeSorted_NotStartedBeforeDraftBeforeFinal()
    {
        List<AipRecord> aips = [Aip(10, 2027, "Final")];
        List<Office> offices = [Off(1, "O1"), Off(2, "O2"), Off(3, "O3")];
        List<WfpRecord> wfps =
        [
            Wfp(1, aipId: 10, officeId: 1, status: "Final"),
            Wfp(2, aipId: 10, officeId: 2, status: "Draft"),
            // office 3 has no WFP → "Not started"
        ];
        (BudgetPlanningDashboardService sut, _) = Build([], aips, wfps, offices, []);

        PlanningDashboardDto result = await sut.GetDashboardAsync(fiscalYear: 2027);

        Assert.Equal(3, result.WfpByOffice.Count);
        Assert.Equal("Not started", result.WfpByOffice[0].WfpStatus); // O3
        Assert.Equal("Draft",       result.WfpByOffice[1].WfpStatus); // O2
        Assert.Equal("Final",       result.WfpByOffice[2].WfpStatus); // O1
    }

    // ── GetDashboardAsync — allocation-setup overview (RAL-60) ────────────

    [Fact]
    public async Task GetDashboardAsync_IncludesAllocationOverview_FromAllocationService()
    {
        Mock<IAllocationService> allocation = new();
        allocation.Setup(a => a.GetCeilingAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ServiceResult<BudgetCeilingDto>.NotFound("no ceiling"));
        allocation.Setup(a => a.GetAllocationsAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyList<DivisionAllocationDto>)[]);
        allocation.Setup(a => a.GetProgramAssignmentsAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyList<ProgramAssignmentDto>)[]);
        allocation.Setup(a => a.GetSetupOverviewAsync(2027, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AllocationSetupOverviewDto(TotalOffices: 5, FullySetupCount: 2,
                IncompleteCount: 1, NotStartedCount: 2));
        (BudgetPlanningDashboardService sut, _) =
            Build([], [], [], [], [], allocationMock: allocation);

        PlanningDashboardDto result = await sut.GetDashboardAsync(fiscalYear: 2027);

        Assert.Equal(5, result.Allocation.TotalOffices);
        Assert.Equal(2, result.Allocation.FullySetupCount);
        Assert.Equal(1, result.Allocation.IncompleteCount);
        Assert.Equal(2, result.Allocation.NotStartedCount);
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
        allocation.Setup(a => a.GetCeilingAsync(1, 2027, It.IsAny<CancellationToken>()))
            .ReturnsAsync(ServiceResult<BudgetCeilingDto>.NotFound("no ceiling"));
        allocation.Setup(a => a.GetAllocationsAsync(1, 2027, It.IsAny<CancellationToken>()))
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
        allocation.Setup(a => a.GetCeilingAsync(1, 2027, It.IsAny<CancellationToken>()))
            .ReturnsAsync(ServiceResult<BudgetCeilingDto>.Ok(new BudgetCeilingDto(1, 1, 2027, 100_000m)));
        allocation.Setup(a => a.GetAllocationsAsync(1, 2027, It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyList<DivisionAllocationDto>)
                [new DivisionAllocationDto(1, 1, "Div A", 2027, 60_000m)]);
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
        allocation.Setup(a => a.GetCeilingAsync(1, 2027, It.IsAny<CancellationToken>()))
            .ReturnsAsync(ServiceResult<BudgetCeilingDto>.Ok(new BudgetCeilingDto(1, 1, 2027, 100_000m)));
        allocation.Setup(a => a.GetAllocationsAsync(1, 2027, It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyList<DivisionAllocationDto>)
            [
                new DivisionAllocationDto(1, 1, "Div A", 2027, 60_000m),
                new DivisionAllocationDto(2, 2, "Div B", 2027, 50_000m),
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
        allocation.Setup(a => a.GetCeilingAsync(1, 2027, It.IsAny<CancellationToken>()))
            .ReturnsAsync(ServiceResult<BudgetCeilingDto>.NotFound("no ceiling"));
        allocation.Setup(a => a.GetAllocationsAsync(1, 2027, It.IsAny<CancellationToken>()))
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

    // ── GetOfficeDashboardAsync — LDIP panel is stubbed until RAL-61 ──────────

    [Fact]
    public async Task GetOfficeDashboardAsync_LdipPanelAlwaysStubbed_RegardlessOfLdipRecords()
    {
        List<LdipRecord> ldips = [Ldip(1, "Final"), Ldip(2, "Draft")];
        (BudgetPlanningDashboardService sut, _) = Build(ldips, [], [], [Off(1, "PPDO")], []);

        OfficeDashboardDto result = await sut.GetOfficeDashboardAsync(1, 2027);

        Assert.False(result.Ldip.ScopingSupported);
        Assert.Equal(0, result.Ldip.Total);
        Assert.Empty(result.Ldip.Breakdown);
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
