using Moq;
using PPDO.Application.Common;
using PPDO.Application.DTOs.BudgetPlanning;
using PPDO.Application.Services;
using PPDO.Domain.Common;
using PPDO.Domain.Entities;
using PPDO.Domain.Enums;
using PPDO.Domain.Interfaces;

namespace PPDO.Tests.Application;

/// <summary>
/// Ref-code allocation under concurrent creates (v1.8.0 Phase 3 — V18-44 / PPDO-50).
///
/// <para>
/// <b>What was actually broken.</b> Generation already existed and was correct; the unique
/// indexes already made a duplicate impossible. The gap was between them: all three create paths
/// do <i>load siblings → compute → insert</i> with nothing in between, so two encoders adding
/// under one parent computed the same code, the first committed, and the second hit
/// <c>UX_aip_activities_project_id_ref_code</c> and surfaced as an unhandled exception. Tracker
/// D5 confirms two-or-more encoders per office is the normal case.
/// </para>
///
/// <para>
/// ⚠️ <b>The test that matters most is <see cref="AddActivity_WhenASiblingWinsTheRace_RetriesOntoTheNextCode"/>.</b>
/// A retry that does not RE-READ the siblings recomputes the same code every attempt and burns
/// the budget — it looks like a fix and changes nothing. That test simulates the winning row
/// actually landing, so a stale-read retry fails it.
/// </para>
/// </summary>
public sealed class AipRefCodeConcurrencyTests
{
    private const int OfficeConfigId = 7;
    private const int AipRecordId    = 100;
    private const int AipOfficeId    = 10;
    private const int ProgramId      = 20;
    private const int ProjectId      = 30;

    // ── Fixtures ─────────────────────────────────────────────────────────────

    private static AipRecord Record() => new()
    {
        Id = AipRecordId, FiscalYear = AipShape.FirstOfficeOwnedFiscalYear,
        OfficeId = OfficeConfigId, EntrySource = "Manual", Status = PlanningStatus.Draft,
        UploadedById = Guid.NewGuid(), UploadedAt = DateTime.UtcNow,
    };

    private static AipOffice Office() => new()
    {
        Id = AipOfficeId, AipRecordId = AipRecordId, OfficeId = OfficeConfigId,
        RefCode = "3000-000-1-01-001", Name = "PPDO", Sector = "SOCIAL",
    };

    private static AipProgram Program() => new()
    {
        Id = ProgramId, OfficeId = AipOfficeId,
        RefCode = "3000-000-1-01-001-001", Name = "Program A", FunctionBand = AipFunctionBand.Core,
    };

    private static AipProject Project() => new()
    {
        Id = ProjectId, ProgramId = ProgramId,
        RefCode = "3000-000-1-01-001-001-001", Name = "Project A",
    };

    private static AipActivity Activity(int id, string refCode) => new()
    {
        Id = id, ProjectId = ProjectId, RefCode = refCode, Name = "Activity " + refCode,
    };

    private static UniqueConstraintViolationException Conflict() =>
        new("Duplicate ref code.", "UX_aip_activities_project_id_ref_code", new InvalidOperationException());

    /// <summary>
    /// A caller with Budget Planning access in the host office — enough to pass
    /// <c>CheckWritableAsync</c>, which is not what these tests are about.
    /// </summary>
    private static User Caller() => new()
    {
        Id = Guid.NewGuid(), Username = "encoder", Role = UserRole.Admin,
        OfficeId = OfficeConfigId, IsActive = true,
    };

    /// <summary>The minimum valid activity payload — this file is about ref codes, not fields.</summary>
    private static CreateAipActivityDto NewActivity() => new(
        "New activity", null, null, null, null, null, null,
        null, null, null, null, null, null);

    // ── The harness ──────────────────────────────────────────────────────────

    /// <param name="saveFailures">
    /// How many times the activity repo's SaveChangesAsync throws a unique violation before
    /// succeeding. Each failure also lands a competing sibling in <paramref name="activities"/>,
    /// which is what makes the re-read observable.
    /// </param>
    private static (AipService sut, List<AipActivity> activities, Mock<IRepository<AipActivity>> activityRepo)
        BuildForActivity(int saveFailures)
    {
        List<AipActivity> activities = [Activity(1, "3000-000-1-01-001-001-001-001")];
        List<AipProject>  projects   = [Project()];
        List<AipProgram>  programs   = [Program()];
        List<AipOffice>   offices    = [Office()];
        List<AipRecord>   records    = [Record()];

        Mock<IAipRepository> aipRepo = new();
        aipRepo.Setup(r => r.GetActivitiesByProjectIdsAsync(It.IsAny<IReadOnlyList<int>>(), It.IsAny<CancellationToken>()))
            // Re-evaluated per call on purpose: a retry must see rows added since the last read.
            .ReturnsAsync((IReadOnlyList<int> ids, CancellationToken _) =>
                (IReadOnlyList<AipActivity>)activities.Where(a => ids.Contains(a.ProjectId)).ToList());
        aipRepo.Setup(r => r.GetProjectByIdAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((int id, CancellationToken _) => projects.FirstOrDefault(p => p.Id == id));
        aipRepo.Setup(r => r.GetProgramByIdAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((int id, CancellationToken _) => programs.FirstOrDefault(p => p.Id == id));
        aipRepo.Setup(r => r.GetOfficeByIdAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((int id, CancellationToken _) => offices.FirstOrDefault(o => o.Id == id));
        aipRepo.Setup(r => r.GetByIntIdAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((int id, CancellationToken _) => records.FirstOrDefault(r2 => r2.Id == id));

        AipActivity? pending = null;
        Mock<IRepository<AipActivity>> activityRepo = new();
        activityRepo.Setup(r => r.AddAsync(It.IsAny<AipActivity>(), It.IsAny<CancellationToken>()))
            .Callback<AipActivity, CancellationToken>((a, _) => pending = a)
            .Returns(Task.CompletedTask);

        int attempts = 0;
        activityRepo.Setup(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(() =>
            {
                if (attempts++ < saveFailures)
                {
                    // The competing encoder's row lands, taking the code this attempt wanted.
                    activities.Add(Activity(500 + attempts, pending!.RefCode));
                    throw Conflict();
                }
                if (pending is not null) { pending.Id = 900 + attempts; activities.Add(pending); }
                return 1;
            });

        Mock<IRepository<FundingSource>> fsRepo = new();
        fsRepo.Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyList<FundingSource>)[]);

        Mock<IOfficeRepository> officeConfigRepo = new();
        officeConfigRepo.Setup(r => r.GetByIdAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((int id, CancellationToken _) =>
                id == OfficeConfigId ? new Office { Id = OfficeConfigId, OfficeCode = "PPDO", OfficeName = "PPDO", OfficeRefCode = "01-001", IsHostOffice = true, IsActive = true } : null);
        officeConfigRepo.Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyList<Office>)[new Office { Id = OfficeConfigId, OfficeCode = "PPDO", OfficeName = "PPDO", OfficeRefCode = "01-001", IsHostOffice = true, IsActive = true }]);

        // Built directly rather than through AipServiceTests.Build(...) on purpose. That harness
        // appends to its activity list inside AddAsync, so a FAILED save would still leave the row
        // visible to the retry's re-read — and the retry would then compute a fresh code for the
        // wrong reason, passing the test while proving nothing. Here only a SUCCESSFUL save lands
        // a row, which is what the database actually does.
        Mock<IWfpRepository> wfpRepo = new();
        wfpRepo.Setup(r => r.AnyForAipRecordAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        Mock<IAllocationRepository> allocationRepo = new();
        allocationRepo.Setup(r => r.GetProgramDivisionsByOfficeIdAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyList<ProgramDivision>)[]);

        AipService sut = new(
            aipRepo.Object, fsRepo.Object, new Mock<IUserRepository>().Object,
            new Mock<IAipXlsmParser>().Object, new Mock<IAuditService>().Object, new CallerContext(),
            new Mock<IRepository<AipOffice>>().Object, wfpRepo.Object, officeConfigRepo.Object,
            new Mock<IRepository<AipProgram>>().Object, new Mock<IRepository<AipProject>>().Object,
            activityRepo.Object, new Mock<ILdipRepository>().Object, allocationRepo.Object);

        return (sut, activities, activityRepo);
    }

    // ── The non-racing path must keep working ────────────────────────────────

    [Fact]
    public async Task AddActivity_WithNoContention_AllocatesTheNextSiblingCode()
    {
        // Pins the ordinary path so the retry cannot quietly change it. Without this, a retry
        // that always skipped a number would pass every other test in this file.
        var (sut, _, _) = BuildForActivity(saveFailures: 0);

        ServiceResult<AipActivityDto> result = await sut.AddActivityAsync(
            ProjectId, NewActivity(), Caller(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("3000-000-1-01-001-001-001-002", result.Value!.RefCode);
    }

    // ── The race ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task AddActivity_WhenASiblingWinsTheRace_RetriesOntoTheNextCode()
    {
        // ⚠️ The test this ticket exists for. The first attempt computes -002 and loses; the
        // winner's -002 row is now present, so a correct retry re-reads and computes -003.
        // A retry that reuses the stale sibling set recomputes -002 and fails here.
        var (sut, activities, _) = BuildForActivity(saveFailures: 1);

        ServiceResult<AipActivityDto> result = await sut.AddActivityAsync(
            ProjectId, NewActivity(), Caller(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("3000-000-1-01-001-001-001-003", result.Value!.RefCode);
        Assert.Equal(
            activities.Select(a => a.RefCode).Distinct().Count(),
            activities.Count);
    }

    [Fact]
    public async Task AddActivity_WhenContentionPersists_ReturnsConflictRatherThanThrowing()
    {
        // The budget is finite on purpose. Sustained contention is not a transient race, and
        // retrying forever would hold a request open instead of telling the user anything.
        var (sut, _, _) = BuildForActivity(saveFailures: 99);

        ServiceResult<AipActivityDto> result = await sut.AddActivityAsync(
            ProjectId, NewActivity(), Caller(), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(ServiceErrorCode.Conflict, result.Code);
    }

    [Fact]
    public async Task AddActivity_WhenContentionPersists_StopsAtTheAttemptBudget()
    {
        // Pins the budget itself. Without this, "returns Conflict" is satisfied by a single
        // attempt that never retries at all — which is the behaviour this ticket replaces.
        var (sut, _, activityRepo) = BuildForActivity(saveFailures: 99);

        await sut.AddActivityAsync(
            ProjectId, NewActivity(), Caller(), CancellationToken.None);

        activityRepo.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()),
            Times.Exactly(RefCodeAllocator.MaxAttempts));
    }

    [Fact]
    public void TheAttemptBudget_IsThree()
    {
        // ⚠️ Pinned as a literal on purpose. StopsAtTheAttemptBudget derives its expectation from
        // MaxAttempts, so it adapts to any value and stays green even at 1 — which is the retry
        // switched off. Verified by mutation: setting MaxAttempts = 1 left that test passing.
        // A test derived from a constant cannot notice the constant is wrong, so this one names
        // the number and makes changing the budget a deliberate edit rather than a silent one.
        Assert.Equal(3, RefCodeAllocator.MaxAttempts);
    }

    [Fact]
    public async Task AddActivity_ARejectionThatIsNotAUniqueViolation_IsNotRetriedOrSwallowed()
    {
        // ⚠️ The guard against the fix hiding real defects. An FK or NOT NULL failure is a bug;
        // retrying it wastes attempts and reporting it as "conflict" sends whoever reads the log
        // looking for a concurrency problem that does not exist.
        var (sut, _, activityRepo) = BuildForActivity(saveFailures: 0);
        activityRepo.Setup(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("FK violation"));

        await Assert.ThrowsAsync<InvalidOperationException>(() => sut.AddActivityAsync(
            ProjectId, NewActivity(), Caller(), CancellationToken.None));

        activityRepo.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    // ── The generator itself ─────────────────────────────────────────────────

    [Fact]
    public void NextRefCode_WithNoSiblings_StartsAt001()
        => Assert.Equal("A-001", RefCodeAllocator.NextRefCode("A", []));

    [Fact]
    public void NextRefCode_TakesTheHighestSibling_NotTheCount()
    {
        // A gap in the middle must not produce a code that already exists.
        Assert.Equal("A-004", RefCodeAllocator.NextRefCode("A", ["A-001", "A-003"]));
    }

    [Fact]
    public void NextRefCode_AnUnparseableSegment_IsIgnoredRatherThanCountedAsZero()
    {
        // ⚠️ Recorded behaviour, deliberately unchanged by this ticket beyond not crashing:
        // a malformed legacy sibling must not drag the next code down onto an existing one.
        Assert.Equal("A-003", RefCodeAllocator.NextRefCode("A", ["A-001", "A-XX", "A-002"]));
    }
}
