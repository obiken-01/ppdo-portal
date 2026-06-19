using Moq;
using PPDO.Application.DTOs.BudgetPlanning;
using PPDO.Application.Services;
using PPDO.Domain.Entities;
using PPDO.Domain.Interfaces;

namespace PPDO.Tests.Application;

/// <summary>
/// Unit tests for <see cref="BudgetPlanningDashboardService"/> (RAL-80).
/// All repositories are mocked via GetAllAsync(); no database access occurs.
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

    private static AuditLog Audit(long id, Guid userId, DateTime? at = null) => new()
    {
        Id = id, TableName = "accounts", RecordId = 1, Action = "CREATE",
        ChangedById = userId, ChangedAt = at ?? DateTime.UtcNow,
        NewValues = "{}",
    };

    private static User AppUser(Guid id, string name, int? officeId = null) => new()
    {
        Id = id, FullName = name, Username = name.ToLower(), PasswordHash = "x",
        OfficeId = officeId,
    };

    private static BudgetPlanningDashboardService Build(
        List<LdipRecord> ldips,
        List<AipRecord> aips,
        List<WfpRecord> wfps,
        List<Office> offices,
        List<AuditLog> audits,
        List<User> users)
    {
        Mock<IRepository<LdipRecord>> ldipRepo = new();
        ldipRepo.Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>())).ReturnsAsync(ldips);

        Mock<IRepository<AipRecord>> aipRepo = new();
        aipRepo.Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>())).ReturnsAsync(aips);

        Mock<IRepository<WfpRecord>> wfpRepo = new();
        wfpRepo.Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>())).ReturnsAsync(wfps);

        Mock<IRepository<Office>> officeRepo = new();
        officeRepo.Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>())).ReturnsAsync(offices);

        Mock<IRepository<AuditLog>> auditRepo = new();
        auditRepo.Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>())).ReturnsAsync(audits);

        Mock<IRepository<User>> userRepo = new();
        userRepo.Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>())).ReturnsAsync(users);

        return new BudgetPlanningDashboardService(
            ldipRepo.Object, aipRepo.Object, wfpRepo.Object,
            officeRepo.Object, auditRepo.Object, userRepo.Object);
    }

    // ── GetDashboardAsync — FY resolution ─────────────────────────────────

    [Fact]
    public async Task GetDashboardAsync_NoAipRecords_DefaultsToNextCalendarYear()
    {
        BudgetPlanningDashboardService sut = Build([], [], [], [], [], []);

        PlanningDashboardDto result = await sut.GetDashboardAsync(fiscalYear: null);

        Assert.Equal(DateTime.UtcNow.Year + 1, result.FiscalYear);
        Assert.Empty(result.AvailableFiscalYears);
    }

    [Fact]
    public async Task GetDashboardAsync_AipRecords_ReturnsDistinctFiscalYearsDescending()
    {
        List<AipRecord> aips = [Aip(1, 2027), Aip(2, 2026), Aip(3, 2027)]; // 2027 twice
        BudgetPlanningDashboardService sut = Build([], aips, [], [], [], []);

        PlanningDashboardDto result = await sut.GetDashboardAsync(fiscalYear: null);

        Assert.Equal([2027, 2026], result.AvailableFiscalYears);
    }

    // ── GetDashboardAsync — LDIP / AIP counts ─────────────────────────────

    [Fact]
    public async Task GetDashboardAsync_LdipGroupsByStatus()
    {
        List<LdipRecord> ldips = [Ldip(1, "Final"), Ldip(2, "Draft"), Ldip(3, "Archived")];
        BudgetPlanningDashboardService sut = Build(ldips, [], [], [], [], []);

        PlanningDashboardDto result = await sut.GetDashboardAsync(fiscalYear: 2027);

        Assert.Equal(3, result.Ldip.Total);
        Assert.Equal(3, result.Ldip.Breakdown.Count);
    }

    [Fact]
    public async Task GetDashboardAsync_AipFiltersByFiscalYear()
    {
        List<AipRecord> aips = [Aip(1, 2027), Aip(2, 2026)];
        BudgetPlanningDashboardService sut = Build([], aips, [], [], [], []);

        PlanningDashboardDto result = await sut.GetDashboardAsync(fiscalYear: 2027);

        Assert.Equal(1, result.Aip.Total);
    }

    // ── GetDashboardAsync — WFP by office ─────────────────────────────────

    [Fact]
    public async Task GetDashboardAsync_ActiveOfficeWithNoWfpRow_StatusIsNotStarted()
    {
        List<AipRecord> aips = [Aip(10, 2027, "Final")];
        List<Office> offices = [Off(1, "PPDO")];
        BudgetPlanningDashboardService sut = Build([], aips, [], offices, [], []);

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
        BudgetPlanningDashboardService sut = Build([], aips, wfps, offices, [], []);

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
        BudgetPlanningDashboardService sut = Build([], aips, wfps, offices, [], []);

        PlanningDashboardDto result = await sut.GetDashboardAsync(fiscalYear: 2027);

        Assert.Equal(3, result.WfpByOffice.Count);
        Assert.Equal("Not started", result.WfpByOffice[0].WfpStatus); // O3
        Assert.Equal("Draft",       result.WfpByOffice[1].WfpStatus); // O2
        Assert.Equal("Final",       result.WfpByOffice[2].WfpStatus); // O1
    }

    // ── GetRecentActivityAsync ─────────────────────────────────────────────

    [Fact]
    public async Task GetRecentActivityAsync_NoOfficeFilter_ReturnsLast10ByDate()
    {
        Guid userId = Guid.NewGuid();
        List<AuditLog> audits = Enumerable.Range(1, 12)
            .Select(i => Audit(i, userId, DateTime.UtcNow.AddMinutes(-i)))
            .ToList();
        List<User> users = [AppUser(userId, "R. Alcaide")];
        BudgetPlanningDashboardService sut = Build([], [], [], [], audits, users);

        IReadOnlyList<RecentActivityDto> result = await sut.GetRecentActivityAsync(officeId: null);

        Assert.Equal(10, result.Count);
        // Most recent first (id=1 has the smallest negative offset = most recent)
        Assert.Equal(1, result[0].Id);
    }

    [Fact]
    public async Task GetRecentActivityAsync_WithOfficeId_FiltersToUsersInThatOffice()
    {
        Guid user1Id = Guid.NewGuid();
        Guid user2Id = Guid.NewGuid();
        List<AuditLog> audits =
        [
            Audit(1, user1Id, DateTime.UtcNow.AddMinutes(-1)), // office 1
            Audit(2, user2Id, DateTime.UtcNow.AddMinutes(-2)), // office 2
            Audit(3, user1Id, DateTime.UtcNow.AddMinutes(-3)), // office 1
        ];
        List<User> users =
        [
            AppUser(user1Id, "Alice", officeId: 1),
            AppUser(user2Id, "Bob",   officeId: 2),
        ];
        BudgetPlanningDashboardService sut = Build([], [], [], [], audits, users);

        IReadOnlyList<RecentActivityDto> result = await sut.GetRecentActivityAsync(officeId: 1);

        Assert.Equal(2, result.Count);
        Assert.All(result, r => Assert.Equal("Alice", r.ActorName));
    }
}
