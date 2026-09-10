using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using PPDO.Application.Common;
using PPDO.Application.DTOs.BudgetPlanning;
using PPDO.Application.Services;
using PPDO.Domain.Entities;
using PPDO.Domain.Enums;
using PPDO.Domain.Interfaces;

namespace PPDO.Tests.Application;

/// <summary>
/// The submit gate (V18-42 / PPDO-52 for the transition, V18-49 / PPDO-59 for the rules).
///
/// <para>
/// ⚠️ <b>Each failing condition is tested individually, not just the happy path</b> — the spec's
/// §11 asks for exactly that. A gate with four checks and one all-good test passes with three of
/// them deleted.
/// </para>
///
/// <para>
/// ⚠️ <b>This is a gate, not a summary.</b> DECISION C allows over-ceiling encoding and blocks at
/// submit, so if these checks are dismissible there is no ceiling enforcement anywhere in the
/// system. There is no "submit anyway", and no test below asserts one.
/// </para>
/// </summary>
public sealed class AipSubmitGateTests
{
    private const int RecordId = 300;
    private const int OfficeId = 7;
    private const int GroupA   = 400;
    private const int GroupB   = 401;

    private readonly Mock<IAipRepository>            _aipRepo = new();
    private readonly Mock<IAipExpenditureRepository> _expRepo = new();
    private readonly Mock<IAipCeilingService>        _ceiling = new();
    private readonly Mock<IRepository<AipOffice>>    _officeRepo = new();
    private readonly Mock<IAuditService>             _audit = new();

    private readonly List<AipOffice> _groups =
    [
        new() { Id = GroupA, AipRecordId = RecordId, OfficeId = OfficeId, RefCode = "1000-000-1-01-010",
                Name = "PPDO - MAIN", Sector = "GENERAL", WorkflowStatus = AipWorkflowStatus.Draft },
    ];

    private AipSubmitService Build(params AipActivity[] activities)
    {
        _aipRepo.Setup(r => r.GetByIntIdAsync(RecordId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AipRecord
            {
                Id = RecordId, FiscalYear = 2028, EntrySource = "Manual",
                Status = PlanningStatus.Draft, UploadedById = Guid.NewGuid(), UploadedAt = DateTime.UtcNow,
            });
        _aipRepo.Setup(r => r.GetOfficesByAipIdAsync(RecordId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(_groups);

        _aipRepo.Setup(r => r.GetProgramsByOfficeIdsAsync(It.IsAny<IReadOnlyList<int>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyList<AipProgram>)[new AipProgram { Id = 500, OfficeId = GroupA, RefCode = "P", Name = "Prog" }]);
        _aipRepo.Setup(r => r.GetProjectsByProgramIdsAsync(It.IsAny<IReadOnlyList<int>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyList<AipProject>)[new AipProject { Id = 600, ProgramId = 500, RefCode = "J", Name = "Proj" }]);
        _aipRepo.Setup(r => r.GetActivitiesByProjectIdsAsync(It.IsAny<IReadOnlyList<int>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyList<AipActivity>)activities.ToList());

        // Default: the ceiling is satisfied. Individual tests override.
        _ceiling.Setup(c => c.GetStatusAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AipCeilingStatusDto(1, true, 1_000_000m, 500_000m, 500_000m, true));
        _ceiling.Setup(c => c.ValidateForSubmitAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string?)null);

        return new AipSubmitService(
            _aipRepo.Object, _expRepo.Object, _ceiling.Object, _officeRepo.Object,
            _audit.Object, NullLogger<AipSubmitService>.Instance);
    }

    /// <summary>An activity that passes every check.</summary>
    private static AipActivity GoodActivity(int id = 700) => new()
    {
        Id = id, ProjectId = 600, RefCode = $"A-{id}", Name = $"Activity {id}",
        EsreCode = "ID", CcTypologyCode = "TYP1", Total = 500_000m,
    };

    /// <summary>Activities with one line each, all of them naming a funding source.</summary>
    private void GivenLines(params int[] activityIdsWithLines)
        => _expRepo.Setup(r => r.CountByActivityIdsAsync(It.IsAny<IReadOnlyList<int>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyList<AipActivityLineCountDto>)
                activityIdsWithLines.Select(id => new AipActivityLineCountDto(id, 1, 0)).ToList());

    /// <summary>One activity with a line that names no funding source.</summary>
    private void GivenFundlessLine(int activityId)
        => _expRepo.Setup(r => r.CountByActivityIdsAsync(It.IsAny<IReadOnlyList<int>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyList<AipActivityLineCountDto>)
                [new AipActivityLineCountDto(activityId, 1, 1)]);

    private static User Encoder() => new()
    {
        Id = Guid.NewGuid(), Username = "e", PasswordHash = "h", FullName = "Encoder",
        Role = UserRole.Staff, OfficeId = OfficeId,
        Office = new Office { Id = OfficeId, OfficeCode = "O7", OfficeName = "PPDO", IsActive = true },
        IsActive = true, CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow,
    };

    // ── The happy path ────────────────────────────────────────────────────────

    [Fact]
    public async Task Submit_WhenEverythingPasses_MovesTheOfficeToDepartmentReview()
    {
        AipSubmitService sut = Build(GoodActivity());
        GivenLines(700);

        ServiceResult<AipSubmitResultDto> result = await sut.SubmitAsync(RecordId, Encoder());

        Assert.True(result.IsSuccess);
        Assert.Equal(AipWorkflowStatus.DepartmentReview, result.Value!.WorkflowStatus);
        Assert.Equal(AipWorkflowStatus.DepartmentReview, _groups[0].WorkflowStatus);
    }

    // ── Each failing condition, on its own ────────────────────────────────────

    /// <summary>
    /// ⚠️ Never costed and costed-at-zero have the SAME line count and opposite meanings (V18-34).
    /// Total is null for one and 0 for the other, and the encoder is sent to different places —
    /// "add the costing" versus "you removed the costing".
    /// </summary>
    [Fact]
    public async Task Submit_ActivityNeverCosted_IsRefusedAndNamedAsNotYetCosted()
    {
        AipActivity never = GoodActivity();
        never.Total = null;
        AipSubmitService sut = Build(never);
        GivenLines(); // no lines at all

        ServiceResult<AipSubmitResultDto> result = await sut.SubmitAsync(RecordId, Encoder());

        Assert.False(result.IsSuccess);
        Assert.Contains("not been costed", result.Error!);
        Assert.Equal(AipWorkflowStatus.Draft, _groups[0].WorkflowStatus); // unchanged
    }

    [Fact]
    public async Task Submit_ActivityWhoseLinesWereAllDeleted_IsRefusedAndNamedAsCostingRemoved()
    {
        AipActivity emptied = GoodActivity();
        emptied.Total = 0m; // costed at zero, not never-costed
        AipSubmitService sut = Build(emptied);
        GivenLines();

        ServiceResult<AipSubmitResultDto> result = await sut.SubmitAsync(RecordId, Encoder());

        Assert.False(result.IsSuccess);
        Assert.Contains("was removed", result.Error!);
    }

    /// <summary>
    /// ⚠️ The check that only a live pass would have asked for. A fundless line is <b>invisible to
    /// the ceiling</b> — that check sums General Fund only, and null is not the General Fund — so
    /// without this an office encodes any amount against no fund, satisfies "has at least one
    /// line", contributes nothing to its ceiling and submits cleanly.
    /// </summary>
    [Fact]
    public async Task Submit_ActivityWithALineThatNamesNoFundingSource_IsRefused()
    {
        AipSubmitService sut = Build(GoodActivity());
        GivenFundlessLine(700);

        ServiceResult<AipSubmitResultDto> result = await sut.SubmitAsync(RecordId, Encoder());

        Assert.False(result.IsSuccess);
        Assert.Contains("no funding source", result.Error!);
        // The message says WHY it matters, not just that a field is blank.
        Assert.Contains("not counted against any ceiling", result.Error!);
    }

    [Fact]
    public async Task Submit_ActivityMissingEsreCode_IsRefused()
    {
        AipActivity noEsre = GoodActivity();
        noEsre.EsreCode = null;
        AipSubmitService sut = Build(noEsre);
        GivenLines(700);

        ServiceResult<AipSubmitResultDto> result = await sut.SubmitAsync(RecordId, Encoder());

        Assert.False(result.IsSuccess);
        Assert.Contains("eSRE", result.Error!);
    }

    /// <summary>
    /// ↩️ **Inverted by PPDO-81** — this used to assert the submit was REFUSED. CC typology is
    /// optional: column (14) is filled only for an activity carrying a climate-change component,
    /// and most do not, so the old rule made encoders invent a code for every ordinary operating
    /// activity. The test is kept rather than deleted, pointing the other way, because the
    /// behaviour it pins is a deliberate decision someone will otherwise "restore" as a bug fix.
    ///
    /// Whitespace, not null, on purpose: the removed check used <c>IsNullOrWhiteSpace</c>, so a
    /// blank-but-present code is the value a partial revert would start refusing again.
    /// </summary>
    [Fact]
    public async Task Submit_ActivityWithNoClimateChangeTypology_IsAllowed()
    {
        AipActivity noCc = GoodActivity();
        noCc.CcTypologyCode = "  ";
        AipSubmitService sut = Build(noCc);
        GivenLines(700);

        ServiceResult<AipSubmitResultDto> result = await sut.SubmitAsync(RecordId, Encoder());

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public async Task Submit_OverCeiling_IsRefusedWithTheCeilingServicesOwnWording()
    {
        AipSubmitService sut = Build(GoodActivity());
        GivenLines(700);
        _ceiling.Setup(c => c.ValidateForSubmitAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("General Fund MOOE + CO totals ₱2,000,000.00, which is ₱1,000,000.00 over the ceiling of ₱1,000,000.00.");

        ServiceResult<AipSubmitResultDto> result = await sut.SubmitAsync(RecordId, Encoder());

        Assert.False(result.IsSuccess);
        // ⚠️ The fund, the total, the ceiling and the overage — not the bare phrase "over ceiling",
        // which tells an encoder nothing about how much to remove.
        Assert.Contains("General Fund", result.Error!);
        Assert.Contains("2,000,000", result.Error!);
        Assert.Contains("1,000,000", result.Error!);
    }

    /// <summary>An office with nothing in it is empty, not ready. Submitting would hand the department head a blank document.</summary>
    [Fact]
    public async Task Submit_OfficeWithNoActivities_IsRefusedAsEmpty()
    {
        AipSubmitService sut = Build();
        GivenLines();

        ServiceResult<AipSubmitResultDto> result = await sut.SubmitAsync(RecordId, Encoder());

        Assert.False(result.IsSuccess);
        Assert.Contains("nothing to submit", result.Error!);
    }

    // ── The multi-group rule ──────────────────────────────────────────────────

    /// <summary>
    /// ⚠️ An office holds one <c>AipOffice</c> row per sub-office group, and "submit the whole
    /// office's work in one action" means ALL of them. Moving one and leaving its siblings in Draft
    /// would put one office in two states at once and hand the department head a fraction of the
    /// work with nothing saying so.
    /// </summary>
    [Fact]
    public async Task Submit_AnOfficeWithSeveralSubOfficeGroups_MovesEveryGroup()
    {
        _groups.Add(new AipOffice
        {
            Id = GroupB, AipRecordId = RecordId, OfficeId = OfficeId, RefCode = "1000-000-1-01-010",
            Name = "PPDO - AKAP-HUB", Sector = "GENERAL", WorkflowStatus = AipWorkflowStatus.Draft,
        });
        AipSubmitService sut = Build(GoodActivity());
        GivenLines(700);

        ServiceResult<AipSubmitResultDto> result = await sut.SubmitAsync(RecordId, Encoder());

        Assert.True(result.IsSuccess);
        Assert.Equal(2, result.Value!.GroupsMoved);
        Assert.All(_groups, g => Assert.Equal(AipWorkflowStatus.DepartmentReview, g.WorkflowStatus));
    }

    // ── Re-submission ─────────────────────────────────────────────────────────

    /// <summary>
    /// A second submit is almost always a double-click or a stale tab. Answering "done" would be a
    /// lie, and would also re-fire the audit entry.
    /// </summary>
    [Fact]
    public async Task Submit_WhenAlreadyInDepartmentReview_IsRefusedByNamingTheState()
    {
        _groups[0].WorkflowStatus = AipWorkflowStatus.DepartmentReview;
        AipSubmitService sut = Build(GoodActivity());
        GivenLines(700);

        ServiceResult<AipSubmitResultDto> result = await sut.SubmitAsync(RecordId, Encoder());

        Assert.False(result.IsSuccess);
        Assert.Contains("department review", result.Error!);
    }

    // ── Readiness agrees with submit ──────────────────────────────────────────

    /// <summary>
    /// ⚠️ The GET and the POST must not drift. If readiness says ready and submit refuses, the
    /// office is told to fix something the checklist says is already fine.
    /// </summary>
    [Fact]
    public async Task Readiness_ReportsExactlyWhatSubmitWouldRefuseOn()
    {
        AipActivity bad = GoodActivity();
        bad.EsreCode = null;
        AipSubmitService sut = Build(bad);
        GivenLines(700);

        ServiceResult<AipReadinessDto> readiness = await sut.GetReadinessAsync(RecordId, Encoder());
        ServiceResult<AipSubmitResultDto> submit = await sut.SubmitAsync(RecordId, Encoder());

        Assert.True(readiness.IsSuccess);
        Assert.False(readiness.Value!.CanSubmit);
        Assert.False(submit.IsSuccess);
        Assert.Contains(readiness.Value.Issues, i => submit.Error!.Contains(i.Message));
    }

    /// <summary>Readiness is side-effect free — safe to poll while the encoder works.</summary>
    [Fact]
    public async Task Readiness_DoesNotChangeTheWorkflowState()
    {
        AipSubmitService sut = Build(GoodActivity());
        GivenLines(700);

        await sut.GetReadinessAsync(RecordId, Encoder());

        Assert.Equal(AipWorkflowStatus.Draft, _groups[0].WorkflowStatus);
        _officeRepo.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    /// <summary>Each failing activity gets its own entry naming the node, never one lumped sentence (spec §4).</summary>
    [Fact]
    public async Task Readiness_ListsOneIssuePerFailingActivityNamingTheNode()
    {
        AipActivity a = GoodActivity(700); a.EsreCode = null;
        // ↩️ Was a missing CC typology until PPDO-81 dropped that check. An uncosted activity is
        // used instead, so this still proves the per-node shape with two DIFFERENT failures —
        // re-pointing it at a second missing eSRE would have tested one rule twice.
        AipActivity b = GoodActivity(701); b.Total = null;
        AipSubmitService sut = Build(a, b);
        GivenLines(700);   // 701 gets no lines and no Total, so it fails as never costed.

        ServiceResult<AipReadinessDto> result = await sut.GetReadinessAsync(RecordId, Encoder());

        Assert.Equal(2, result.Value!.Issues.Count);
        Assert.Contains(result.Value.Issues, i => i.ActivityId == 700 && i.Kind == "missing-esre");
        Assert.Contains(result.Value.Issues, i => i.ActivityId == 701 && i.Kind == "no-lines");
        Assert.All(result.Value.Issues, i => Assert.False(string.IsNullOrWhiteSpace(i.RefCode)));
    }

    /// <summary>
    /// ⚠️ **CC typology is optional and submit must not block on it** (PPDO-81).
    ///
    /// Column (14) is filled only for an activity carrying a climate-change component, and most do
    /// not. The gate used to refuse a blank, which forced encoders to invent a code for every
    /// ordinary operating activity — fiction in a column the province reports on. This pins the
    /// absence of that check, because "we removed a rule" is otherwise invisible to the suite.
    /// </summary>
    [Fact]
    public async Task Readiness_DoesNotBlockOnAMissingCcTypologyCode()
    {
        AipActivity a = GoodActivity(700);
        a.CcTypologyCode = null;
        AipSubmitService sut = Build(a);
        GivenLines(700);

        ServiceResult<AipReadinessDto> result = await sut.GetReadinessAsync(RecordId, Encoder());

        Assert.Empty(result.Value!.Issues);
        Assert.True(result.Value.CanSubmit);
    }

    // ── The second submit: department head → PPDO (V18-51 / PPDO-69) ──────────
    //
    // ⚠️ There are TWO submits. Everything above tests the encoder's (Draft → DepartmentReview).
    // These test the department head's (→ SubmittedToPpdo), which has a different authority, two
    // valid starting states, and re-runs the same gate.

    /// <summary>The department head. Same office; the authority difference is the flag, checked at the handler.</summary>
    private static User DeptHead() => new()
    {
        Id = Guid.NewGuid(), Username = "dh", PasswordHash = "h", FullName = "Department Head",
        Role = UserRole.Staff, OfficeId = OfficeId,
        Office = new Office { Id = OfficeId, OfficeCode = "O7", OfficeName = "PPDO", IsActive = true },
        OverrideCanReviewBudgetPlanning = true,
        IsActive = true, CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow,
    };

    private void GivenGroupsAt(string status)
    {
        foreach (AipOffice g in _groups) g.WorkflowStatus = status;
    }

    /// <summary>A second sub-office group, so "every group moves together" is actually exercised.</summary>
    private void GivenASecondGroup()
        => _groups.Add(new AipOffice
        {
            Id = GroupB, AipRecordId = RecordId, OfficeId = OfficeId,
            RefCode = "3000-000-1-01-010", Name = "PPDO - ANNEX", Sector = "SOCIAL",
            WorkflowStatus = _groups[0].WorkflowStatus,
        });

    [Fact]
    public async Task SubmitToPpdo_FromDepartmentReview_MovesEveryGroupTogether()
    {
        AipSubmitService sut = Build(GoodActivity());
        GivenLines(700);
        GivenGroupsAt(AipWorkflowStatus.DepartmentReview);
        GivenASecondGroup();

        ServiceResult<AipSubmitResultDto> result =
            await sut.SubmitToPpdoAsync(RecordId, OfficeId, DeptHead());

        Assert.True(result.IsSuccess);
        Assert.Equal(AipWorkflowStatus.SubmittedToPpdo, result.Value!.WorkflowStatus);
        Assert.Equal(2, result.Value.GroupsMoved);
        // ⚠️ The point of the test: one office is several rows, and half-moving it would leave the
        // office in two states at once with nothing on screen saying so.
        Assert.All(_groups, g => Assert.Equal(AipWorkflowStatus.SubmittedToPpdo, g.WorkflowStatus));
    }

    /// <summary>Re-submitting after PPDO sent the work back is the SAME transition, not a third one.</summary>
    [Fact]
    public async Task SubmitToPpdo_FromReturnedByPpdo_IsAllowedAsAResubmit()
    {
        AipSubmitService sut = Build(GoodActivity());
        GivenLines(700);
        GivenGroupsAt(AipWorkflowStatus.ReturnedByPpdo);

        ServiceResult<AipSubmitResultDto> result =
            await sut.SubmitToPpdoAsync(RecordId, OfficeId, DeptHead());

        Assert.True(result.IsSuccess);
        Assert.Equal(AipWorkflowStatus.SubmittedToPpdo, _groups[0].WorkflowStatus);
    }

    [Fact]
    public async Task SubmitToPpdo_WhileStillDraft_IsRefusedAndSaysTheFirstSubmitIsMissing()
    {
        AipSubmitService sut = Build(GoodActivity());
        GivenLines(700); // Draft is the default state

        ServiceResult<AipSubmitResultDto> result =
            await sut.SubmitToPpdoAsync(RecordId, OfficeId, DeptHead());

        Assert.False(result.IsSuccess);
        Assert.Contains("not been submitted for department review", result.Error!);
        Assert.Equal(AipWorkflowStatus.Draft, _groups[0].WorkflowStatus);
    }

    /// <summary>A double-click or a stale tab. Answering "done" would claim edits went on that did not.</summary>
    [Fact]
    public async Task SubmitToPpdo_WhenAlreadyWithPpdo_IsRefusedNamingTheState()
    {
        AipSubmitService sut = Build(GoodActivity());
        GivenLines(700);
        GivenGroupsAt(AipWorkflowStatus.SubmittedToPpdo);

        ServiceResult<AipSubmitResultDto> result =
            await sut.SubmitToPpdoAsync(RecordId, OfficeId, DeptHead());

        Assert.False(result.IsSuccess);
        Assert.Contains("review by PPDO", result.Error!);
    }

    /// <summary>
    /// ⚠️ NotFound, not Forbidden — an office the caller does not own must be indistinguishable
    /// from one that does not exist (PPDO-46), or the response enumerates the province.
    /// </summary>
    [Fact]
    public async Task SubmitToPpdo_ForAnOfficeThatIsNotTheCallers_IsNotFoundNotForbidden()
    {
        AipSubmitService sut = Build(GoodActivity());
        GivenLines(700);
        GivenGroupsAt(AipWorkflowStatus.DepartmentReview);

        ServiceResult<AipSubmitResultDto> result =
            await sut.SubmitToPpdoAsync(RecordId, officeId: OfficeId + 1, DeptHead());

        Assert.False(result.IsSuccess);
        Assert.Equal(ServiceErrorCode.NotFound, result.Code);
        Assert.Equal(AipWorkflowStatus.DepartmentReview, _groups[0].WorkflowStatus);
    }

    /// <summary>
    /// ⚠️ The discriminating test for this ticket. The department head MAY edit values during
    /// review (spec decision 4), so the figures that passed the encoder's gate are not necessarily
    /// the figures being sent to PPDO. A version that trusted the first pass lets a review that
    /// broke the ceiling through the only gate that enforces it.
    /// </summary>
    [Fact]
    public async Task SubmitToPpdo_WhenAReviewEditBrokeTheCeiling_IsRefused()
    {
        AipSubmitService sut = Build(GoodActivity());
        GivenLines(700);
        GivenGroupsAt(AipWorkflowStatus.DepartmentReview);
        _ceiling.Setup(c => c.ValidateForSubmitAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("General Fund: encoded ₱1,400,000 exceeds the ₱1,000,000 ceiling by ₱400,000.");

        ServiceResult<AipSubmitResultDto> result =
            await sut.SubmitToPpdoAsync(RecordId, OfficeId, DeptHead());

        Assert.False(result.IsSuccess);
        Assert.Contains("exceeds the", result.Error!);
        Assert.Equal(AipWorkflowStatus.DepartmentReview, _groups[0].WorkflowStatus);
    }

    /// <summary>An activity stripped of its last line during review is caught here too.</summary>
    [Fact]
    public async Task SubmitToPpdo_WhenAReviewEditRemovedTheLastLine_IsRefused()
    {
        AipSubmitService sut = Build(GoodActivity());
        GivenLines(); // every line gone
        GivenGroupsAt(AipWorkflowStatus.DepartmentReview);

        ServiceResult<AipSubmitResultDto> result =
            await sut.SubmitToPpdoAsync(RecordId, OfficeId, DeptHead());

        Assert.False(result.IsSuccess);
        Assert.Equal(AipWorkflowStatus.DepartmentReview, _groups[0].WorkflowStatus);
    }

    /// <summary>
    /// ⚠️ Named actions, not UPDATE. A transition logged as UPDATE is indistinguishable from any
    /// other column change without parsing JSON, which would make PPDO-77's history a scan.
    /// Both hops are asserted so the chain reads end to end.
    /// </summary>
    [Fact]
    public async Task BothSubmits_AreAuditedUnderTheirOwnActionNames()
    {
        AipSubmitService sut = Build(GoodActivity());
        GivenLines(700);

        await sut.SubmitAsync(RecordId, Encoder());
        await sut.SubmitToPpdoAsync(RecordId, OfficeId, DeptHead());

        _audit.Verify(a => a.LogAsync("aip_offices", It.IsAny<int>(), AuditAction.SubmitToDeptHead,
            It.IsAny<object?>(), It.IsAny<object?>(), It.IsAny<CancellationToken>()), Times.Once);
        _audit.Verify(a => a.LogAsync("aip_offices", It.IsAny<int>(), AuditAction.SubmitToPpdo,
            It.IsAny<object?>(), It.IsAny<object?>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    /// <summary>
    /// ⚠️ Ten characters, hard. <c>audit_log.action</c> is <c>nvarchar(10)</c>; nothing in the
    /// build catches an over-long value because the in-memory provider does not enforce the width,
    /// so it would fail on the first real write to SQL Server. This spec's first draft proposed
    /// twelve-character names.
    /// </summary>
    [Fact]
    public void EveryAuditActionFitsTheColumn()
    {
        string[] all =
        [
            AuditAction.Create, AuditAction.Update, AuditAction.Delete,
            AuditAction.SubmitToDeptHead, AuditAction.SubmitToPpdo,
        ];

        Assert.All(all, a => Assert.InRange(a.Length, 1, 10));
    }
}
