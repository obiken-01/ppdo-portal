using Moq;
using PPDO.Application.Common;
using PPDO.Application.DTOs.BudgetPlanning;
using PPDO.Application.Services;
using PPDO.Domain.Entities;
using PPDO.Domain.Enums;
using PPDO.Domain.Interfaces;

namespace PPDO.Tests.Application;

/// <summary>
/// The division lock on <see cref="AipService"/>'s write paths, activity tagging on create, and
/// the department head's re-tag (v1.8.0 — PPDO-148, <c>Division_Submit_Spec.md</c> §3.1, §3.3).
///
/// <para>
/// The fixture is one guest office with two active divisions (Planning, Engineering) and one
/// inactive one, on an FY2028 record. Activity 40 is Planning's, 41 is Engineering's and 42 is
/// untagged. Every service below goes through the <b>real</b> <see cref="AipDivisionLock"/> — only
/// its two repositories are faked (<see cref="AipDivisionLockFixture"/>).
/// </para>
/// </summary>
public sealed partial class AipServiceTests
{
    private const int DivPlanning    = 5;
    private const int DivEngineering = 6;
    private const int DivInactive    = 7;
    private const int DivHostOffice  = 9;

    private const int ActPlanning    = 40;
    private const int ActEngineering = 41;
    private const int ActUntagged    = 42;

    private static User GuestStaff(int? divisionId, bool reviewGrant = false, UserRole role = UserRole.Staff) => new()
    {
        Id = Guid.NewGuid(), Username = "g", PasswordHash = "h", FullName = "G",
        Role = role, OfficeId = GuestOfficeId, DivisionId = divisionId,
        Office = ConfigOffice(GuestOfficeId, isHost: false),
        OverrideCanReviewBudgetPlanning = reviewGrant ? true : null,
        IsActive = true, CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow,
    };

    private static User EncoderPlanning()  => GuestStaff(DivPlanning);
    private static User EncoderNoDivision() => GuestStaff(null);
    private static User DepartmentHead()    => GuestStaff(null, reviewGrant: true);

    private sealed record DivisionWorld(
        AipService Sut,
        AipDivisionLockFixture Divisions,
        List<AipActivity> Activities,
        Mock<IAuditService> Audit,
        Mock<IAipCeilingService> Ceiling,
        Mock<IAipRepository> Repo);

    private static DivisionWorld BuildDivisionWorld(
        int fiscalYear = 2028,
        string workflowStatus = AipWorkflowStatus.Draft,
        bool withDivisions = true)
    {
        List<AipRecord> recs =
        [
            new() { Id = AipRecordId, FiscalYear = fiscalYear, EntrySource = "Manual", Status = "Draft",
                    UploadedById = Guid.NewGuid(), UploadedAt = DateTime.UtcNow },
        ];
        List<AipOffice> offices =
        [
            new() { Id = 10, AipRecordId = AipRecordId, RefCode = "1000-000-1-01-020",
                    Name = "GSO", Sector = "GENERAL", OfficeId = GuestOfficeId,
                    WorkflowStatus = workflowStatus },
        ];
        List<AipProgram> programs = [new() { Id = 20, OfficeId = 10, RefCode = "P", Name = "Program" }];
        List<AipProject> projects =
        [
            new() { Id = 30, ProgramId = 20, RefCode = "P-001", Name = "Project" },
            new() { Id = 31, ProgramId = 20, RefCode = "P-002", Name = "Planning only" },
        ];
        List<AipActivity> acts =
        [
            new() { Id = ActPlanning,    ProjectId = 30, RefCode = "P-001-001", Name = "Plan",  DivisionId = DivPlanning },
            new() { Id = ActEngineering, ProjectId = 30, RefCode = "P-001-002", Name = "Build", DivisionId = DivEngineering },
            new() { Id = ActUntagged,    ProjectId = 30, RefCode = "P-001-003", Name = "Loose" },
            new() { Id = 43,             ProjectId = 31, RefCode = "P-002-001", Name = "Plan 2", DivisionId = DivPlanning },
        ];

        AipDivisionLockFixture divisions = new();
        if (withDivisions)
        {
            divisions.AddDivision(DivPlanning,    GuestOfficeId, "Planning Division");
            divisions.AddDivision(DivEngineering, GuestOfficeId, "Engineering Division");
            divisions.AddDivision(DivInactive,    GuestOfficeId, "Old Division", active: false);
        }
        divisions.AddDivision(DivHostOffice, HostOfficeId, "PPDO Planning");

        Mock<IAipCeilingService> ceiling = new();
        var built = Build(recs, [], officeSeed: offices, programSeed: programs, projectSeed: projects,
            actSeed: acts,
            officeConfigSeed: [ConfigOffice(HostOfficeId, true), ConfigOffice(GuestOfficeId, false)],
            divisions: divisions,
            // The read side still narrows a division encoder to their division's programs
            // (AipReadScope, PPDO-134), so the program is assigned to both divisions here.
            programDivisionSeed:
            [
                new() { OfficeId = GuestOfficeId, ProgramRefCode = "P", DivisionId = DivPlanning },
                new() { OfficeId = GuestOfficeId, ProgramRefCode = "P", DivisionId = DivEngineering },
            ],
            ceiling: ceiling);

        return new DivisionWorld(built.Item1, divisions, acts, built.Item6, ceiling, built.Item2);
    }

    private static CreateAipActivityDto NewActivity(int? divisionId = null) =>
        new("New", null, null, null, null, null, null, null, null, null, null, null, null, divisionId);

    private static UpdateAipActivityDetailsDto Details() =>
        new("Renamed", null, null, null, null, null, null, null, null);

    // ── Tagging on create (§3.1) ─────────────────────────────────────────────

    [Fact]
    public async Task AddActivity_ByEncoder_IsTaggedWithTheirDivision_AndIgnoresTheClientValue()
    {
        DivisionWorld w = BuildDivisionWorld();

        ServiceResult<AipActivityDto> result =
            await w.Sut.AddActivityAsync(30, NewActivity(DivEngineering), EncoderPlanning());

        Assert.True(result.IsSuccess);
        Assert.Equal(DivPlanning, result.Value!.DivisionId);
        Assert.Equal("Planning Division", result.Value.DivisionName);
        Assert.True(result.Value.CanEdit);
    }

    [Fact]
    public async Task AddActivity_ByDepartmentHead_WithoutADivision_IsRefused()
    {
        DivisionWorld w = BuildDivisionWorld();

        ServiceResult<AipActivityDto> result =
            await w.Sut.AddActivityAsync(30, NewActivity(), DepartmentHead());

        Assert.Equal(ServiceErrorCode.BadRequest, result.Code);
        Assert.Equal(AipDivisionContext.ChooseDivisionMessage, result.Error);
    }

    [Fact]
    public async Task AddActivity_ByDepartmentHead_IsTaggedWithTheChosenDivision()
    {
        DivisionWorld w = BuildDivisionWorld();

        ServiceResult<AipActivityDto> result =
            await w.Sut.AddActivityAsync(30, NewActivity(DivEngineering), DepartmentHead());

        Assert.True(result.IsSuccess);
        Assert.Equal(DivEngineering, result.Value!.DivisionId);
        Assert.Equal(DivEngineering, w.Activities.Single(a => a.Name == "New").DivisionId);
    }

    [Theory]
    [InlineData(DivHostOffice, "That division is not part of this office.")]
    [InlineData(DivInactive,   "That division is inactive.")]
    [InlineData(999,           "That division is not part of this office.")]
    public async Task AddActivity_ByDepartmentHead_ToADivisionTheyCannotUse_IsRefused(int divisionId, string message)
    {
        DivisionWorld w = BuildDivisionWorld();

        ServiceResult<AipActivityDto> result =
            await w.Sut.AddActivityAsync(30, NewActivity(divisionId), DepartmentHead());

        Assert.Equal(ServiceErrorCode.BadRequest, result.Code);
        Assert.Equal(message, result.Error);
    }

    [Fact]
    public async Task AddActivity_ByEncoderWithNoDivision_IsRefused()
    {
        DivisionWorld w = BuildDivisionWorld();

        ServiceResult<AipActivityDto> result =
            await w.Sut.AddActivityAsync(30, NewActivity(DivPlanning), EncoderNoDivision());

        Assert.Equal(ServiceErrorCode.BadRequest, result.Code);
        Assert.Equal(AipDivisionContext.NotAssignedMessage, result.Error);
    }

    [Fact]
    public async Task AddActivity_ByEncoder_WhoseDivisionHasSubmitted_IsRefused()
    {
        DivisionWorld w = BuildDivisionWorld();
        w.Divisions.Submit(AipRecordId, GuestOfficeId, DivPlanning);

        ServiceResult<AipActivityDto> result =
            await w.Sut.AddActivityAsync(30, NewActivity(), EncoderPlanning());

        Assert.Equal(ServiceErrorCode.BadRequest, result.Code);
        Assert.Contains("Planning Division's work has been submitted", result.Error);
    }

    [Fact]
    public async Task AddActivity_InAnOfficeWithoutDivisions_IsUntagged_AsToday()
    {
        DivisionWorld w = BuildDivisionWorld(withDivisions: false);

        ServiceResult<AipActivityDto> result =
            await w.Sut.AddActivityAsync(30, NewActivity(DivHostOffice), EncoderNoDivision());

        Assert.True(result.IsSuccess);
        Assert.Null(result.Value!.DivisionId);
    }

    /// <summary>Decision 12 — FY2027 has no division flow, even in an office that has divisions.</summary>
    [Fact]
    public async Task AddActivity_OnAnFy2027Record_IsUntagged_EvenWithDivisions()
    {
        DivisionWorld w = BuildDivisionWorld(fiscalYear: 2027);

        ServiceResult<AipActivityDto> result =
            await w.Sut.AddActivityAsync(30, NewActivity(), EncoderNoDivision());

        Assert.True(result.IsSuccess);
        Assert.Null(result.Value!.DivisionId);
    }

    // ── Editing under the lock (§3.3) ─────────────────────────────────────────

    [Fact]
    public async Task EditActivity_OwnDivision_WhileDraft_IsAllowed()
    {
        DivisionWorld w = BuildDivisionWorld();

        ServiceResult<AipActivityDto> result =
            await w.Sut.UpdateActivityDetailsAsync(ActPlanning, Details(), EncoderPlanning());

        Assert.True(result.IsSuccess);
        Assert.True(result.Value!.CanEdit);
    }

    [Fact]
    public async Task EditActivity_OwnDivision_OnceSubmitted_IsRefused()
    {
        DivisionWorld w = BuildDivisionWorld();
        w.Divisions.Submit(AipRecordId, GuestOfficeId, DivPlanning);

        ServiceResult<AipActivityDto> result =
            await w.Sut.UpdateActivityDetailsAsync(ActPlanning, Details(), EncoderPlanning());

        Assert.Equal(ServiceErrorCode.BadRequest, result.Code);
        Assert.Equal(
            "Planning Division's work has been submitted to the department head and can no longer be edited here.",
            result.Error);
        Assert.Equal("Plan", w.Activities.Single(a => a.Id == ActPlanning).Name);
    }

    [Fact]
    public async Task EditActivity_OfAnotherDivision_IsRefused()
    {
        DivisionWorld w = BuildDivisionWorld();

        ServiceResult<AipActivityDto> result =
            await w.Sut.UpdateActivityAsync(AipRecordId, ActEngineering, UpdateActivity(), EncoderPlanning());

        Assert.Equal(ServiceErrorCode.BadRequest, result.Code);
        Assert.Equal("This activity belongs to Engineering Division.", result.Error);
    }

    [Fact]
    public async Task EditActivity_Untagged_ByEncoder_IsRefused()
    {
        DivisionWorld w = BuildDivisionWorld();

        ServiceResult<AipActivityDto> result =
            await w.Sut.UpdateActivityDetailsAsync(ActUntagged, Details(), EncoderPlanning());

        Assert.Equal(AipDivisionContext.UntaggedActivityMessage, result.Error);
    }

    [Fact]
    public async Task EditActivity_ByDepartmentHead_OnSubmittedWork_IsAllowed()
    {
        DivisionWorld w = BuildDivisionWorld();
        w.Divisions.Submit(AipRecordId, GuestOfficeId, DivPlanning);

        ServiceResult<AipActivityDto> result =
            await w.Sut.UpdateActivityDetailsAsync(ActPlanning, Details(), DepartmentHead());

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public async Task EditActivity_ByGuestOfficeAdmin_OnSubmittedWork_IsAllowed()
    {
        DivisionWorld w = BuildDivisionWorld();
        w.Divisions.Submit(AipRecordId, GuestOfficeId, DivPlanning);

        ServiceResult<AipActivityDto> result =
            await w.Sut.UpdateActivityDetailsAsync(ActPlanning, Details(), GuestStaff(null, role: UserRole.Admin));

        Assert.True(result.IsSuccess);
    }

    /// <summary>
    /// ⚠️ <b>The pin the spec asks for</b> (§3.3 "Office lock unchanged"): the department head is
    /// exempt from the division rule and from nothing else. Once the office is with PPDO, the
    /// office-state guard refuses them with its own text, which must not be the division lock's.
    /// </summary>
    [Theory]
    [InlineData(AipWorkflowStatus.SubmittedToPpdo)]
    [InlineData(AipWorkflowStatus.Consolidated)]
    public async Task EditActivity_ByDepartmentHead_OnceTheOfficeIsWithPpdo_IsStillRefused(string status)
    {
        DivisionWorld w = BuildDivisionWorld(workflowStatus: status);

        ServiceResult<AipActivityDto> result =
            await w.Sut.UpdateActivityDetailsAsync(ActPlanning, Details(), DepartmentHead());

        Assert.Equal(ServiceErrorCode.BadRequest, result.Code);
        Assert.Contains("It has been sent on and is no longer editable here.", result.Error);
    }

    [Fact]
    public async Task EditActivity_ByEncoderWithNoDivision_IsRefused()
    {
        DivisionWorld w = BuildDivisionWorld();

        ServiceResult<AipActivityDto> result =
            await w.Sut.UpdateActivityDetailsAsync(ActPlanning, Details(), EncoderNoDivision());

        Assert.Equal(AipDivisionContext.NotAssignedMessage, result.Error);
    }

    /// <summary>
    /// A department head is the <c>CanReviewBudgetPlanning</c> holder of THIS office. The same grant
    /// held in another office — here, a host-office user who may otherwise write any office — does
    /// not make them this office's head, and they are no member of its divisions either.
    /// </summary>
    [Fact]
    public async Task EditActivity_ByAnotherOfficesDepartmentHead_IsRefused()
    {
        DivisionWorld w = BuildDivisionWorld();
        User hostHead = CallerFor(HostOfficeId, isHost: true);
        hostHead.OverrideCanReviewBudgetPlanning = true;
        hostHead.DivisionId = DivHostOffice;

        ServiceResult<AipActivityDto> result =
            await w.Sut.UpdateActivityDetailsAsync(ActPlanning, Details(), hostHead);

        Assert.Equal(AipDivisionContext.NotAssignedMessage, result.Error);
    }

    [Fact]
    public async Task EditActivity_InAnOfficeWithoutDivisions_IsUnchanged()
    {
        DivisionWorld w = BuildDivisionWorld(withDivisions: false);

        ServiceResult<AipActivityDto> result =
            await w.Sut.UpdateActivityDetailsAsync(ActEngineering, Details(), EncoderNoDivision());

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public async Task EditActivity_OnAnFy2027Record_IgnoresDivisions()
    {
        DivisionWorld w = BuildDivisionWorld(fiscalYear: 2027);

        ServiceResult<AipActivityDto> result =
            await w.Sut.UpdateActivityAsync(AipRecordId, ActEngineering, UpdateActivity(), EncoderPlanning());

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public async Task DeleteActivity_OfAnotherDivision_IsRefused_AndNothingIsDeleted()
    {
        DivisionWorld w = BuildDivisionWorld();

        ServiceResult<AipDeleteResultDto> result = await w.Sut.DeleteActivityAsync(ActEngineering, EncoderPlanning());

        Assert.Equal(ServiceErrorCode.BadRequest, result.Code);
        Assert.Contains(w.Activities, a => a.Id == ActEngineering);
    }

    [Fact]
    public async Task UpdateIsCreation_OnSubmittedWork_ByEncoder_IsRefused()
    {
        DivisionWorld w = BuildDivisionWorld();
        w.Divisions.Submit(AipRecordId, GuestOfficeId, DivPlanning);

        ServiceResult<AipActivityDto> result = await w.Sut.UpdateActivityIsCreationAsync(ActPlanning, true, EncoderPlanning());

        Assert.Equal(ServiceErrorCode.BadRequest, result.Code);
        Assert.False(w.Activities.Single(a => a.Id == ActPlanning).IsCreation);
    }

    // ── Containers ────────────────────────────────────────────────────────────

    [Fact]
    public async Task DeleteProject_HoldingAnotherDivisionsActivity_ByEncoder_IsRefused()
    {
        DivisionWorld w = BuildDivisionWorld();

        ServiceResult<AipDeleteResultDto> result = await w.Sut.DeleteProjectAsync(30, EncoderPlanning());

        Assert.Equal(ServiceErrorCode.BadRequest, result.Code);
        Assert.Equal("This project contains another division's activities.", result.Error);
        Assert.Equal(4, w.Activities.Count);
    }

    [Fact]
    public async Task DeleteProgram_HoldingAnotherDivisionsActivity_ByEncoder_IsRefused()
    {
        DivisionWorld w = BuildDivisionWorld();

        ServiceResult<AipDeleteResultDto> result = await w.Sut.DeleteProgramAsync(20, EncoderPlanning());

        Assert.Equal("This program contains another division's activities.", result.Error);
    }

    [Fact]
    public async Task DeleteOffice_HoldingOtherDivisionsWork_ByEncoder_IsRefused()
    {
        DivisionWorld w = BuildDivisionWorld();

        ServiceResult<bool> result = await w.Sut.DeleteOfficeAsync(10, EncoderPlanning());

        Assert.Equal("This office contains another division's activities.", result.Error);
    }

    [Fact]
    public async Task DeleteProject_OnlyOwnOpenActivities_ByEncoder_IsAllowed()
    {
        DivisionWorld w = BuildDivisionWorld();

        ServiceResult<AipDeleteResultDto> result = await w.Sut.DeleteProjectAsync(31, EncoderPlanning());

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public async Task DeleteProject_OnlyOwnActivities_OnceSubmitted_ByEncoder_IsRefused()
    {
        DivisionWorld w = BuildDivisionWorld();
        w.Divisions.Submit(AipRecordId, GuestOfficeId, DivPlanning);

        ServiceResult<AipDeleteResultDto> result = await w.Sut.DeleteProjectAsync(31, EncoderPlanning());

        Assert.Equal("This project contains Planning Division's submitted activities.", result.Error);
    }

    [Fact]
    public async Task DeleteProject_HoldingAnotherDivisionsActivity_ByDepartmentHead_IsAllowed()
    {
        DivisionWorld w = BuildDivisionWorld();

        ServiceResult<AipDeleteResultDto> result = await w.Sut.DeleteProjectAsync(30, DepartmentHead());

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public async Task RenameProject_ByEncoder_IsAllowed_ButNotByEncoderWithNoDivision()
    {
        DivisionWorld w = BuildDivisionWorld();

        ServiceResult<AipProjectDto> ok = await w.Sut.UpdateProjectAsync(
            30, new UpdateAipProjectDto("Renamed", null, null), EncoderPlanning());
        ServiceResult<AipProjectDto> refused = await w.Sut.UpdateProjectAsync(
            30, new UpdateAipProjectDto("Renamed again", null, null), EncoderNoDivision());

        Assert.True(ok.IsSuccess);
        Assert.Equal(AipDivisionContext.NotAssignedMessage, refused.Error);
    }

    // ── Re-tag (§3.1, §4) ─────────────────────────────────────────────────────

    [Fact]
    public async Task Retag_ByDepartmentHead_MovesTheActivity_AndAuditsOldAndNew()
    {
        DivisionWorld w = BuildDivisionWorld();

        ServiceResult<AipActivityDto> result =
            await w.Sut.RetagActivityDivisionAsync(ActPlanning, DivEngineering, DepartmentHead());

        Assert.True(result.IsSuccess);
        Assert.Equal(DivEngineering, w.Activities.Single(a => a.Id == ActPlanning).DivisionId);
        Assert.Equal("Engineering Division", result.Value!.DivisionName);
        w.Audit.Verify(a => a.LogAsync("aip_activities", ActPlanning, AuditAction.RetagActivityDivision,
            It.IsAny<object?>(), It.IsAny<object?>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    /// <summary>PPDO-150 — the reservation follows the tag, so a re-tag re-posts the ledger.</summary>
    [Fact]
    public async Task Retag_RePostsTheActivitysCeilingReservation()
    {
        DivisionWorld w = BuildDivisionWorld();

        await w.Sut.RetagActivityDivisionAsync(ActPlanning, DivEngineering, DepartmentHead());

        w.Ceiling.Verify(c => c.UpsertLedgerForActivityAsync(ActPlanning, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Retag_ToTheSameDivision_ChangesNothing_AndTouchesNoLedger()
    {
        DivisionWorld w = BuildDivisionWorld();

        await w.Sut.RetagActivityDivisionAsync(ActPlanning, DivPlanning, DepartmentHead());

        w.Ceiling.Verify(c => c.UpsertLedgerForActivityAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Retag_TagsAnUntaggedActivity()
    {
        DivisionWorld w = BuildDivisionWorld();

        ServiceResult<AipActivityDto> result =
            await w.Sut.RetagActivityDivisionAsync(ActUntagged, DivPlanning, DepartmentHead());

        Assert.True(result.IsSuccess);
        Assert.Equal(DivPlanning, w.Activities.Single(a => a.Id == ActUntagged).DivisionId);
    }

    /// <summary>Decision 2 — the department head moves work into or out of a submitted division.</summary>
    [Fact]
    public async Task Retag_IntoASubmittedDivision_IsAllowed()
    {
        DivisionWorld w = BuildDivisionWorld();
        w.Divisions.Submit(AipRecordId, GuestOfficeId, DivEngineering);

        ServiceResult<AipActivityDto> result =
            await w.Sut.RetagActivityDivisionAsync(ActPlanning, DivEngineering, DepartmentHead());

        Assert.True(result.IsSuccess);
    }

    [Theory]
    [MemberData(nameof(NonHeadCallers))]
    public async Task Retag_ByAnyoneButTheDepartmentHead_IsForbidden(string who)
    {
        DivisionWorld w = BuildDivisionWorld();
        User caller = who == "encoder" ? EncoderPlanning() : EncoderNoDivision();

        ServiceResult<AipActivityDto> result =
            await w.Sut.RetagActivityDivisionAsync(ActPlanning, DivEngineering, caller);

        Assert.Equal(ServiceErrorCode.Forbidden, result.Code);
        Assert.Equal(DivPlanning, w.Activities.Single(a => a.Id == ActPlanning).DivisionId);
    }

    public static TheoryData<string> NonHeadCallers() => new() { "encoder", "no-division" };

    [Theory]
    [InlineData(DivHostOffice, "That division is not part of this office.")]
    [InlineData(DivInactive,   "That division is inactive.")]
    public async Task Retag_ToADivisionTheyCannotUse_IsRefused(int divisionId, string message)
    {
        DivisionWorld w = BuildDivisionWorld();

        ServiceResult<AipActivityDto> result =
            await w.Sut.RetagActivityDivisionAsync(ActPlanning, divisionId, DepartmentHead());

        Assert.Equal(ServiceErrorCode.BadRequest, result.Code);
        Assert.Equal(message, result.Error);
        Assert.Equal(DivPlanning, w.Activities.Single(a => a.Id == ActPlanning).DivisionId);
    }

    [Fact]
    public async Task Retag_OnceTheOfficeIsWithPpdo_IsRefused()
    {
        DivisionWorld w = BuildDivisionWorld(workflowStatus: AipWorkflowStatus.SubmittedToPpdo);

        ServiceResult<AipActivityDto> result =
            await w.Sut.RetagActivityDivisionAsync(ActPlanning, DivEngineering, DepartmentHead());

        Assert.Equal(ServiceErrorCode.BadRequest, result.Code);
        Assert.Equal(DivPlanning, w.Activities.Single(a => a.Id == ActPlanning).DivisionId);
    }

    [Fact]
    public async Task Retag_InAnOfficeWithoutDivisions_IsRefused()
    {
        DivisionWorld w = BuildDivisionWorld(withDivisions: false);

        ServiceResult<AipActivityDto> result =
            await w.Sut.RetagActivityDivisionAsync(ActPlanning, DivEngineering, DepartmentHead());

        Assert.Equal(ServiceErrorCode.BadRequest, result.Code);
    }

    // ── The tree read's CanEdit and division name (§4) ────────────────────────

    [Fact]
    public async Task GetById_ForAnEncoder_MarksOnlyTheirOwnOpenActivitiesEditable()
    {
        DivisionWorld w = BuildDivisionWorld();

        ServiceResult<AipRecordDetailDto> result = await w.Sut.GetByIdAsync(AipRecordId, EncoderPlanning());

        Dictionary<int, AipActivityDto> byId = result.Value!.Offices
            .SelectMany(o => o.Programs).SelectMany(p => p.Projects).SelectMany(j => j.Activities)
            .ToDictionary(a => a.Id);
        Assert.True(byId[ActPlanning].CanEdit);
        Assert.False(byId[ActEngineering].CanEdit);
        Assert.False(byId[ActUntagged].CanEdit);
        Assert.Equal("Engineering Division", byId[ActEngineering].DivisionName);
        Assert.Null(byId[ActUntagged].DivisionName);
    }

    [Fact]
    public async Task GetById_AfterTheEncodersDivisionSubmits_MarksNothingEditableForThem()
    {
        DivisionWorld w = BuildDivisionWorld();
        w.Divisions.Submit(AipRecordId, GuestOfficeId, DivPlanning);

        ServiceResult<AipRecordDetailDto> result = await w.Sut.GetByIdAsync(AipRecordId, EncoderPlanning());

        Assert.All(
            result.Value!.Offices.SelectMany(o => o.Programs).SelectMany(p => p.Projects).SelectMany(j => j.Activities),
            a => Assert.False(a.CanEdit));
    }

    [Fact]
    public async Task GetById_ForTheDepartmentHead_MarksEveryActivityEditable_UntilPpdoHasIt()
    {
        DivisionWorld open = BuildDivisionWorld();
        open.Divisions.Submit(AipRecordId, GuestOfficeId, DivPlanning);
        DivisionWorld withPpdo = BuildDivisionWorld(workflowStatus: AipWorkflowStatus.SubmittedToPpdo);

        ServiceResult<AipRecordDetailDto> editable = await open.Sut.GetByIdAsync(AipRecordId, DepartmentHead());
        ServiceResult<AipRecordDetailDto> closed   = await withPpdo.Sut.GetByIdAsync(AipRecordId, DepartmentHead());

        static IEnumerable<AipActivityDto> Acts(AipRecordDetailDto d) =>
            d.Offices.SelectMany(o => o.Programs).SelectMany(p => p.Projects).SelectMany(j => j.Activities);
        Assert.All(Acts(editable.Value!), a => Assert.True(a.CanEdit));
        Assert.All(Acts(closed.Value!), a => Assert.False(a.CanEdit));
    }
}
