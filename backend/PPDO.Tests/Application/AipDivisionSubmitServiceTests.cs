using Microsoft.Extensions.Logging.Abstractions;
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
/// The division submit and return, and how the office's state follows its divisions (v1.8.0 —
/// PPDO-149, <c>Division_Submit_Spec.md</c> decisions 3, 9–11, 13, §3.2, §3.4). Also the changes to
/// the office-level hops in an office with divisions.
///
/// <para>
/// Fixture: one guest office (7) with Planning (50, activities 700 + 701), Engineering (51,
/// activity 702) and Records (52, no activities), on an FY2028 record. Every activity passes the
/// checklist unless a test breaks it. The real <see cref="AipDivisionWorkflow"/> runs over
/// <see cref="AipDivisionLockFixture"/>'s lists.
/// </para>
/// </summary>
public sealed class AipDivisionSubmitServiceTests
{
    private const int RecordId = 300;
    private const int OfficeId = 7;
    private const int Group    = 400;
    private const int Planning = 50, Engineering = 51, Records = 52;

    private readonly Mock<IAipRepository>            _aipRepo    = new();
    private readonly Mock<IAipExpenditureRepository> _expRepo    = new();
    private readonly Mock<IAipCeilingService>        _ceiling    = new();
    private readonly Mock<IRepository<AipOffice>>    _officeRepo = new();
    private readonly Mock<IAuditService>             _audit      = new();
    private readonly Mock<IUserRepository>           _userRepo   = new();
    private readonly AipDivisionLockFixture          _divisions  = new();

    private readonly AipRecord _record = new()
    {
        Id = RecordId, FiscalYear = 2028, EntrySource = "Manual", Status = PlanningStatus.Draft,
        UploadedById = Guid.NewGuid(), UploadedAt = DateTime.UtcNow,
    };

    private readonly AipOffice _group = new()
    {
        Id = Group, AipRecordId = RecordId, OfficeId = OfficeId, RefCode = "R", Name = "GSO",
        Sector = "GENERAL", WorkflowStatus = AipWorkflowStatus.Draft,
    };

    private readonly List<AipActivity> _activities =
    [
        Good(700, Planning), Good(701, Planning), Good(702, Engineering),
    ];

    public AipDivisionSubmitServiceTests()
    {
        _aipRepo.Setup(r => r.GetByIntIdAsync(RecordId, It.IsAny<CancellationToken>())).ReturnsAsync(_record);
        _aipRepo.Setup(r => r.GetOfficesByAipIdAsync(RecordId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => [_group]);
        _aipRepo.Setup(r => r.GetProgramsByOfficeIdsAsync(It.IsAny<IReadOnlyList<int>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyList<AipProgram>)[new AipProgram { Id = 500, OfficeId = Group, RefCode = "P", Name = "P" }]);
        _aipRepo.Setup(r => r.GetProjectsByProgramIdsAsync(It.IsAny<IReadOnlyList<int>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyList<AipProject>)[new AipProject { Id = 600, ProgramId = 500, RefCode = "J", Name = "J" }]);
        _aipRepo.Setup(r => r.GetActivitiesByProjectIdsAsync(It.IsAny<IReadOnlyList<int>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => _activities.ToList());
        _expRepo.Setup(r => r.CountByActivityIdsAsync(It.IsAny<IReadOnlyList<int>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyList<int> ids, CancellationToken _) =>
                (IReadOnlyList<AipActivityLineCountDto>)ids.Select(id => new AipActivityLineCountDto(id, 1, 0)).ToList());

        _ceiling.Setup(c => c.GetStatusAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AipCeilingStatusDto(1, true, 1_000_000m, 500_000m, 500_000m, true));
        _ceiling.Setup(c => c.ValidateForSubmitAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string?)null);

        _userRepo.Setup(r => r.GetNamesByIdsAsync(It.IsAny<IReadOnlyList<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyList<Guid> ids, CancellationToken _) =>
                (IReadOnlyDictionary<Guid, string>)ids.ToDictionary(id => id, _ => "Ana Reyes"));

        _divisions.AddDivision(Planning,    OfficeId, "Planning Division");
        _divisions.AddDivision(Engineering, OfficeId, "Engineering Division");
        _divisions.AddDivision(Records,     OfficeId, "Records Division");
        _divisions.AddDivision(90, 8, "Another Office's Division");
    }

    private AipSubmitService Sut() => new(
        _aipRepo.Object, _expRepo.Object, _ceiling.Object, _officeRepo.Object, _audit.Object,
        _divisions.Repo.Object, _divisions.Workflow(_audit.Object), new PermissionService(),
        _userRepo.Object, NullLogger<AipSubmitService>.Instance);

    private static AipActivity Good(int id, int? division) => new()
    {
        Id = id, ProjectId = 600, RefCode = $"A-{id}", Name = $"Activity {id}",
        EsreCode = "ID", Total = 100_000m, DivisionId = division,
    };

    private static User Staff(int? division, bool head = false, int officeId = OfficeId, UserRole role = UserRole.Staff) => new()
    {
        Id = Guid.NewGuid(), Username = "u", PasswordHash = "h", FullName = "U",
        Role = role, OfficeId = officeId, DivisionId = division,
        Office = new Office { Id = officeId, OfficeCode = "GSO", OfficeName = "GSO", IsActive = true },
        OverrideCanReviewBudgetPlanning = head ? true : null,
        IsActive = true, CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow,
    };

    private static User EncoderIn(int division) => Staff(division);
    private static User DepartmentHead()        => Staff(null, head: true);

    private AipDivisionSubmission? Row(int division) =>
        _divisions.Submissions.SingleOrDefault(s => s.DivisionId == division);

    private void AuditedOnce(string table, string action) =>
        _audit.Verify(a => a.LogAsync(table, It.IsAny<int>(), action,
            It.IsAny<object?>(), It.IsAny<object?>(), It.IsAny<CancellationToken>()), Times.Once);

    // ── Division submit (§3.2) ────────────────────────────────────────────────

    [Fact]
    public async Task Submit_HappyPath_SubmitsTheDivision_AndLeavesTheOfficeInDraft()
    {
        User encoder = EncoderIn(Planning);

        ServiceResult<AipDivisionSubmitResultDto> result =
            await Sut().SubmitDivisionAsync(RecordId, Planning, encoder);

        Assert.True(result.IsSuccess, result.Error);
        Assert.Equal(AipDivisionStatus.Submitted, result.Value!.Status);
        Assert.Equal(AipWorkflowStatus.Draft, result.Value.OfficeWorkflowStatus);
        Assert.Equal(encoder.Id, Row(Planning)!.SubmittedById);
        Assert.Equal(AipWorkflowStatus.Draft, _group.WorkflowStatus);
        AuditedOnce("aip_division_submissions", AuditAction.SubmitDivision);
        _audit.Verify(a => a.LogAsync("aip_offices", It.IsAny<int>(), It.IsAny<string>(),
            It.IsAny<object?>(), It.IsAny<object?>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    /// <summary>Decision 9 — the last division with activities moves the office by itself.</summary>
    [Fact]
    public async Task Submit_TheLastDivisionWithActivities_MovesTheOfficeToDepartmentReview()
    {
        _divisions.Submit(RecordId, OfficeId, Engineering);

        ServiceResult<AipDivisionSubmitResultDto> result =
            await Sut().SubmitDivisionAsync(RecordId, Planning, EncoderIn(Planning));

        // Records has no activities, so it does not hold the office back (decision 10).
        Assert.Equal(AipWorkflowStatus.DepartmentReview, result.Value!.OfficeWorkflowStatus);
        Assert.Equal(AipWorkflowStatus.DepartmentReview, _group.WorkflowStatus);
        AuditedOnce("aip_division_submissions", AuditAction.SubmitDivision);
        AuditedOnce("aip_offices", AuditAction.SubmitToDeptHead);
    }

    [Fact]
    public async Task Submit_Twice_IsRefused()
    {
        _divisions.Submit(RecordId, OfficeId, Planning);

        ServiceResult<AipDivisionSubmitResultDto> result =
            await Sut().SubmitDivisionAsync(RecordId, Planning, EncoderIn(Planning));

        Assert.Equal(ServiceErrorCode.BadRequest, result.Code);
        Assert.Equal("Planning Division has already been submitted.", result.Error);
    }

    [Fact]
    public async Task Submit_ADivisionWithNoActivities_IsRefused()
    {
        ServiceResult<AipDivisionSubmitResultDto> result =
            await Sut().SubmitDivisionAsync(RecordId, Records, EncoderIn(Records));

        Assert.Equal("Records Division has no activities to submit.", result.Error);
        Assert.Empty(_divisions.Submissions);
    }

    [Fact]
    public async Task Submit_WhileAnyActivityInTheOfficeIsUntagged_IsRefused()
    {
        _activities.Add(Good(703, null));
        _activities.Add(Good(704, null));

        ServiceResult<AipDivisionSubmitResultDto> result =
            await Sut().SubmitDivisionAsync(RecordId, Planning, EncoderIn(Planning));

        Assert.Equal(
            "2 activities in this office have no division. Your department head must assign them before any division can submit.",
            result.Error);
        Assert.Empty(_divisions.Submissions);
    }

    [Fact]
    public async Task Submit_WithAnIncompleteActivityOfItsOwn_IsRefused()
    {
        _activities[0].EsreCode = null;

        ServiceResult<AipDivisionSubmitResultDto> result =
            await Sut().SubmitDivisionAsync(RecordId, Planning, EncoderIn(Planning));

        Assert.Equal(ServiceErrorCode.BadRequest, result.Code);
        Assert.Contains("'Activity 700' has no eSRE code.", result.Error);
    }

    /// <summary>§3.2 "only the division's own activities are counted".</summary>
    [Fact]
    public async Task Submit_IsNotHeldBack_ByAnotherDivisionsIncompleteActivity()
    {
        _activities[2].EsreCode = null;   // Engineering's

        ServiceResult<AipDivisionSubmitResultDto> result =
            await Sut().SubmitDivisionAsync(RecordId, Planning, EncoderIn(Planning));

        Assert.True(result.IsSuccess, result.Error);
    }

    [Fact]
    public async Task Submit_AnotherDivision_IsForbidden()
    {
        ServiceResult<AipDivisionSubmitResultDto> result =
            await Sut().SubmitDivisionAsync(RecordId, Engineering, EncoderIn(Planning));

        Assert.Equal(ServiceErrorCode.Forbidden, result.Code);
        Assert.Empty(_divisions.Submissions);
    }

    [Fact]
    public async Task Submit_ByAnEncoderWithNoDivision_IsForbidden()
    {
        ServiceResult<AipDivisionSubmitResultDto> result =
            await Sut().SubmitDivisionAsync(RecordId, Planning, Staff(null));

        Assert.Equal(ServiceErrorCode.Forbidden, result.Code);
    }

    [Theory]
    [InlineData(UserRole.Staff, true)]    // the department head
    [InlineData(UserRole.Admin, false)]   // the office's Admin
    public async Task Submit_OnADivisionsBehalf_IsAllowed_AndNamesWhoDidIt(UserRole role, bool reviewGrant)
    {
        User head = Staff(null, head: reviewGrant, role: role);

        ServiceResult<AipDivisionSubmitResultDto> result =
            await Sut().SubmitDivisionAsync(RecordId, Planning, head);

        Assert.True(result.IsSuccess, result.Error);
        Assert.Equal(head.Id, Row(Planning)!.SubmittedById);
    }

    /// <summary>PPDO-46 — another office's division is answered exactly like one that does not exist.</summary>
    [Fact]
    public async Task Submit_AnotherOfficesDivision_IsNotFound_WithTheMissingDivisionsText()
    {
        ServiceResult<AipDivisionSubmitResultDto> foreign =
            await Sut().SubmitDivisionAsync(RecordId, 90, DepartmentHead());
        ServiceResult<AipDivisionSubmitResultDto> missing =
            await Sut().SubmitDivisionAsync(RecordId, 999, DepartmentHead());

        Assert.Equal(ServiceErrorCode.NotFound, foreign.Code);
        Assert.Equal(missing.Error!.Replace("999", "90"), foreign.Error);
    }

    [Fact]
    public async Task Submit_InAnOfficeWithoutDivisions_IsRefused()
    {
        _divisions.Divisions.RemoveAll(d => d.OfficeId == OfficeId && d.Id != Planning);
        _divisions.Divisions.Single(d => d.Id == Planning).IsActive = false;

        ServiceResult<AipDivisionSubmitResultDto> result =
            await Sut().SubmitDivisionAsync(RecordId, Planning, DepartmentHead());

        Assert.Equal("This office has no divisions — submit the whole office instead.", result.Error);
    }

    [Fact]
    public async Task Submit_OnAnFy2027Record_IsRefused_AsAnOfficeWithoutDivisions()
    {
        _record.FiscalYear = 2027;

        ServiceResult<AipDivisionSubmitResultDto> result =
            await Sut().SubmitDivisionAsync(RecordId, Planning, EncoderIn(Planning));

        Assert.Equal(ServiceErrorCode.BadRequest, result.Code);
    }

    [Theory]
    [InlineData(AipWorkflowStatus.SubmittedToPpdo)]
    [InlineData(AipWorkflowStatus.Consolidated)]
    public async Task Submit_OnceTheOfficeIsWithPpdo_IsRefused(string status)
    {
        _group.WorkflowStatus = status;

        ServiceResult<AipDivisionSubmitResultDto> result =
            await Sut().SubmitDivisionAsync(RecordId, Planning, EncoderIn(Planning));

        Assert.Equal(ServiceErrorCode.BadRequest, result.Code);
        Assert.Empty(_divisions.Submissions);
    }

    [Fact]
    public async Task Submit_OnAFinalRecord_IsRefused()
    {
        _record.Status = PlanningStatus.Final;

        ServiceResult<AipDivisionSubmitResultDto> result =
            await Sut().SubmitDivisionAsync(RecordId, Planning, EncoderIn(Planning));

        Assert.Equal("The FY 2028 AIP is 'Final' and cannot be submitted.", result.Error);
    }

    /// <summary>Decision 13 — over the ceiling warns at the division submit; it does not stop it.</summary>
    [Fact]
    public async Task Submit_OverTheCeiling_SucceedsWithTheWarning()
    {
        _ceiling.Setup(c => c.ValidateForSubmitAsync(Group, It.IsAny<CancellationToken>()))
            .ReturnsAsync("This office is ₱50,000 over its ceiling.");

        ServiceResult<AipDivisionSubmitResultDto> result =
            await Sut().SubmitDivisionAsync(RecordId, Planning, EncoderIn(Planning));

        Assert.True(result.IsSuccess);
        Assert.Equal("This office is ₱50,000 over its ceiling.", result.Value!.CeilingWarning);
    }

    [Fact]
    public async Task Submit_LosingTheRaceForTheFirstRow_IsAConflict()
    {
        _divisions.Repo.Setup(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new UniqueConstraintViolationException("dup", "IX", new Exception()));

        ServiceResult<AipDivisionSubmitResultDto> result =
            await Sut().SubmitDivisionAsync(RecordId, Planning, EncoderIn(Planning));

        Assert.Equal(ServiceErrorCode.Conflict, result.Code);
    }

    // ── Return one division (§3.4) ────────────────────────────────────────────

    [Fact]
    public async Task Return_OneDivision_ReopensOnlyIt_AndTakesTheOfficeOutOfReview()
    {
        _divisions.Submit(RecordId, OfficeId, Planning);
        _divisions.Submit(RecordId, OfficeId, Engineering);
        _group.WorkflowStatus = AipWorkflowStatus.DepartmentReview;
        User head = DepartmentHead();

        ServiceResult<AipDivisionSubmitResultDto> result =
            await Sut().ReturnDivisionAsync(RecordId, Planning, head);

        Assert.True(result.IsSuccess, result.Error);
        Assert.Equal(AipDivisionStatus.Draft, Row(Planning)!.Status);
        Assert.Equal(head.Id, Row(Planning)!.ReturnedById);
        Assert.Equal(AipDivisionStatus.Submitted, Row(Engineering)!.Status);
        Assert.Equal(AipWorkflowStatus.Draft, _group.WorkflowStatus);
        AuditedOnce("aip_division_submissions", AuditAction.ReturnDivision);
        AuditedOnce("aip_offices", AuditAction.ReturnToEncoder);
    }

    [Fact]
    public async Task Return_BeforeEveryoneHasSubmitted_LeavesTheOfficeInDraft()
    {
        _divisions.Submit(RecordId, OfficeId, Planning);

        ServiceResult<AipDivisionSubmitResultDto> result =
            await Sut().ReturnDivisionAsync(RecordId, Planning, DepartmentHead());

        Assert.Equal(AipWorkflowStatus.Draft, result.Value!.OfficeWorkflowStatus);
        _audit.Verify(a => a.LogAsync("aip_offices", It.IsAny<int>(), It.IsAny<string>(),
            It.IsAny<object?>(), It.IsAny<object?>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Return_ADivisionStillInDraft_IsRefused()
    {
        ServiceResult<AipDivisionSubmitResultDto> result =
            await Sut().ReturnDivisionAsync(RecordId, Planning, DepartmentHead());

        Assert.Equal("Planning Division is still with its encoders.", result.Error);
    }

    [Theory]
    [InlineData(UserRole.Staff)]   // the division's own encoder
    [InlineData(UserRole.Admin)]   // a plain Admin — matches the office-level return (spec T3 note)
    public async Task Return_ByAnyoneWithoutTheReviewGrant_IsForbidden(UserRole role)
    {
        _divisions.Submit(RecordId, OfficeId, Planning);

        ServiceResult<AipDivisionSubmitResultDto> result =
            await Sut().ReturnDivisionAsync(RecordId, Planning, Staff(Planning, role: role));

        Assert.Equal(ServiceErrorCode.Forbidden, result.Code);
        Assert.Equal(AipDivisionStatus.Submitted, Row(Planning)!.Status);
    }

    [Fact]
    public async Task Return_ByAnotherOfficesDepartmentHead_IsNotFound()
    {
        _divisions.Submit(RecordId, OfficeId, Planning);

        ServiceResult<AipDivisionSubmitResultDto> result =
            await Sut().ReturnDivisionAsync(RecordId, Planning, Staff(null, head: true, officeId: 8));

        Assert.Equal(ServiceErrorCode.NotFound, result.Code);
    }

    // ── The office-level hops in an office with divisions ─────────────────────

    [Fact]
    public async Task OfficeSubmit_InAnOfficeWithDivisions_IsRefused()
    {
        ServiceResult<AipSubmitResultDto> result = await Sut().SubmitAsync(RecordId, EncoderIn(Planning));

        Assert.Equal("This office submits by division. Each division head submits their own division.", result.Error);
        Assert.Equal(AipWorkflowStatus.Draft, _group.WorkflowStatus);
    }

    [Fact]
    public async Task ReturnToEncoder_InAnOfficeWithDivisions_ReturnsEveryDivision()
    {
        _divisions.Submit(RecordId, OfficeId, Planning);
        _divisions.Submit(RecordId, OfficeId, Engineering);
        _group.WorkflowStatus = AipWorkflowStatus.DepartmentReview;

        ServiceResult<AipSubmitResultDto> result =
            await Sut().ReturnToEncoderAsync(RecordId, OfficeId, DepartmentHead());

        Assert.True(result.IsSuccess);
        Assert.All(_divisions.Submissions, r => Assert.Equal(AipDivisionStatus.Draft, r.Status));
        _audit.Verify(a => a.LogAsync("aip_division_submissions", It.IsAny<int>(), AuditAction.ReturnDivision,
            It.IsAny<object?>(), It.IsAny<object?>(), It.IsAny<CancellationToken>()), Times.Exactly(2));
        AuditedOnce("aip_offices", AuditAction.ReturnToEncoder);
    }

    [Fact]
    public async Task SubmitToPpdo_WhileADivisionHasNotSubmitted_IsRefusedNamingEveryWaitingDivision()
    {
        _group.WorkflowStatus = AipWorkflowStatus.DepartmentReview;   // e.g. a legacy UAT office

        ServiceResult<AipSubmitResultDto> result =
            await Sut().SubmitToPpdoAsync(RecordId, OfficeId, DepartmentHead());

        Assert.Equal(ServiceErrorCode.BadRequest, result.Code);
        Assert.Contains("Planning Division and Engineering Division have not submitted yet.", result.Error);
        Assert.DoesNotContain("Records Division", result.Error);
        Assert.Equal(AipWorkflowStatus.DepartmentReview, _group.WorkflowStatus);
    }

    [Fact]
    public async Task SubmitToPpdo_WhenEveryDivisionHasSubmitted_GoesToPpdo()
    {
        _divisions.Submit(RecordId, OfficeId, Planning);
        _divisions.Submit(RecordId, OfficeId, Engineering);
        _group.WorkflowStatus = AipWorkflowStatus.DepartmentReview;

        ServiceResult<AipSubmitResultDto> result =
            await Sut().SubmitToPpdoAsync(RecordId, OfficeId, DepartmentHead());

        Assert.True(result.IsSuccess, result.Error);
        Assert.Equal(AipWorkflowStatus.SubmittedToPpdo, _group.WorkflowStatus);
    }

    /// <summary>Decision 13 — the ceiling still blocks the send to PPDO in a divisioned office.</summary>
    [Fact]
    public async Task SubmitToPpdo_AllSubmittedButOverTheCeiling_IsStillRefused()
    {
        _divisions.Submit(RecordId, OfficeId, Planning);
        _divisions.Submit(RecordId, OfficeId, Engineering);
        _group.WorkflowStatus = AipWorkflowStatus.DepartmentReview;
        _ceiling.Setup(c => c.ValidateForSubmitAsync(Group, It.IsAny<CancellationToken>()))
            .ReturnsAsync("This office is ₱50,000 over its ceiling.");

        ServiceResult<AipSubmitResultDto> result =
            await Sut().SubmitToPpdoAsync(RecordId, OfficeId, DepartmentHead());

        Assert.Contains("over its ceiling", result.Error);
        Assert.Equal(AipWorkflowStatus.DepartmentReview, _group.WorkflowStatus);
    }

    [Fact]
    public async Task Readiness_InAnOfficeWithDivisions_ClosesTheOfficeSubmit_AndListsWhoIsWaiting()
    {
        _divisions.Submit(RecordId, OfficeId, Planning);

        AipReadinessDto readiness = (await Sut().GetReadinessAsync(RecordId, DepartmentHead())).Value!;

        Assert.True(readiness.SubmitsByDivision);
        Assert.False(readiness.CanSubmit);
        Assert.False(readiness.CanSubmitToPpdo);
        Assert.Equal(["Engineering Division"], readiness.WaitingDivisions);
    }

    // ── Resubmitting after a PPDO return (§3.4, decision 11) ──────────────────

    [Fact]
    public async Task AfterAPpdoReturn_TheOfficeStaysReturned_UntilItsLastDivisionResubmits()
    {
        _group.WorkflowStatus = AipWorkflowStatus.ReturnedByPpdo;   // every division back in Draft

        ServiceResult<AipDivisionSubmitResultDto> first =
            await Sut().SubmitDivisionAsync(RecordId, Planning, EncoderIn(Planning));
        Assert.Equal(AipWorkflowStatus.ReturnedByPpdo, first.Value!.OfficeWorkflowStatus);

        ServiceResult<AipDivisionSubmitResultDto> last =
            await Sut().SubmitDivisionAsync(RecordId, Engineering, EncoderIn(Engineering));
        Assert.Equal(AipWorkflowStatus.DepartmentReview, last.Value!.OfficeWorkflowStatus);
    }

    [Fact]
    public async Task ReturnOne_WhileTheOfficeIsReturnedByPpdo_KeepsTheReturnedState()
    {
        _group.WorkflowStatus = AipWorkflowStatus.ReturnedByPpdo;
        _divisions.Submit(RecordId, OfficeId, Planning);

        ServiceResult<AipDivisionSubmitResultDto> result =
            await Sut().ReturnDivisionAsync(RecordId, Planning, DepartmentHead());

        Assert.Equal(AipWorkflowStatus.ReturnedByPpdo, result.Value!.OfficeWorkflowStatus);
    }

    // ── The division list (§4 GET) ───────────────────────────────────────────

    [Fact]
    public async Task List_ShowsEveryDivision_WithCountsState_AndTheCallersActions()
    {
        _divisions.Submit(RecordId, OfficeId, Engineering);
        _divisions.Submissions[0].SubmittedById = Guid.NewGuid();

        AipDivisionStatusListDto list =
            (await Sut().GetDivisionsAsync(RecordId, OfficeId, EncoderIn(Planning))).Value!;

        Assert.True(list.HasDivisions);
        Assert.Equal([Planning, Engineering, Records], list.Divisions.Select(d => d.DivisionId));
        AipDivisionStatusDto plan = list.Divisions[0], eng = list.Divisions[1], rec = list.Divisions[2];
        Assert.Equal((2, AipDivisionStatus.Draft, true, false), (plan.ActivityCount, plan.Status, plan.CanSubmit, plan.CanReturn));
        Assert.Equal((1, AipDivisionStatus.Submitted, false), (eng.ActivityCount, eng.Status, eng.CanSubmit));
        Assert.Equal("Ana Reyes", eng.SubmittedByName);
        Assert.Equal(0, rec.ActivityCount);
        Assert.Contains("Records Division has no activities to submit.", rec.Blockers);
    }

    [Fact]
    public async Task List_ForTheDepartmentHead_OffersReturnOnSubmittedDivisions()
    {
        _divisions.Submit(RecordId, OfficeId, Engineering);

        AipDivisionStatusListDto list =
            (await Sut().GetDivisionsAsync(RecordId, OfficeId, DepartmentHead())).Value!;

        Assert.True(list.Divisions.Single(d => d.DivisionId == Engineering).CanReturn);
        Assert.True(list.Divisions.Single(d => d.DivisionId == Planning).CanSubmit);
    }

    [Fact]
    public async Task List_WithUntaggedWork_BlocksEveryDivision_AndCountsIt()
    {
        _activities.Add(Good(703, null));

        AipDivisionStatusListDto list =
            (await Sut().GetDivisionsAsync(RecordId, OfficeId, DepartmentHead())).Value!;

        Assert.Equal(1, list.UntaggedActivityCount);
        Assert.All(list.Divisions, d => Assert.False(d.CanSubmit));
    }

    [Fact]
    public async Task List_ForAnotherOfficesStaff_IsNotFound()
    {
        ServiceResult<AipDivisionStatusListDto> result =
            await Sut().GetDivisionsAsync(RecordId, OfficeId, Staff(90, officeId: 8));

        Assert.Equal(ServiceErrorCode.NotFound, result.Code);
    }

    [Fact]
    public async Task List_OutsideTheDivisionFlow_SaysSo()
    {
        _record.FiscalYear = 2027;

        AipDivisionStatusListDto list =
            (await Sut().GetDivisionsAsync(RecordId, OfficeId, EncoderIn(Planning))).Value!;

        Assert.False(list.HasDivisions);
        Assert.Empty(list.Divisions);
    }
}
