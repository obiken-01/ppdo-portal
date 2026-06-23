using Moq;
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

    private static Office Off(int id, string name, bool active = true) => new()
    {
        Id = id, OfficeCode = $"O{id}", OfficeName = name, IsActive = active,
        CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow,
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
    /// </summary>
    private static (BudgetPlanningDashboardService svc, Mock<IAuditRepository> auditMock) Build(
        List<LdipRecord> ldips,
        List<AipRecord> aips,
        List<WfpRecord> wfps,
        List<Office> offices,
        List<AuditLog> audits)
    {
        Mock<IRepository<LdipRecord>> ldipRepo = new();
        ldipRepo.Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>())).ReturnsAsync(ldips);

        Mock<IRepository<AipRecord>> aipRepo = new();
        aipRepo.Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>())).ReturnsAsync(aips);

        Mock<IRepository<WfpRecord>> wfpRepo = new();
        wfpRepo.Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>())).ReturnsAsync(wfps);

        Mock<IRepository<Office>> officeRepo = new();
        officeRepo.Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>())).ReturnsAsync(offices);

        Mock<IAuditRepository> auditRepo = new();
        auditRepo
            .Setup(r => r.GetRecentAsync(It.IsAny<int>(), It.IsAny<int?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(audits);

        BudgetPlanningDashboardService svc = new(
            ldipRepo.Object, aipRepo.Object, wfpRepo.Object,
            officeRepo.Object, auditRepo.Object);

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
}
