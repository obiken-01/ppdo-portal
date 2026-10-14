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
/// The office's side of a PPDO reviewer edit (v1.9.0 — PPDO-213,
/// <c>docs/v1.9/AIP_Reviewer_Edit_Spec.md</c> decision 7): the comment carries
/// <c>IsReviewerEdit</c>, the dashboard notice counts the ones the office has not seen, and
/// <b>Mark as seen</b> stamps them.
///
/// <para>
/// ⚠️ <b>Acknowledging is not resolving.</b> The comment was created resolved (PPDO-212), so the
/// resolve rule (only the authoring side) is never involved. Acknowledgement belongs to the office
/// that was edited: a PPDO reviewer acting on another office is refused with a 403, because they
/// can see the office but it is not theirs to sign for.
/// </para>
/// </summary>
public sealed class AipReviewerEditNoticeTests
{
    private const int RecordId    = 900;
    private const int OfficeX     = 7;
    private const int OfficeY     = 8;
    private const int GroupX1     = 950;
    private const int GroupX2     = 951;   // a second sector group of the same office
    private const int GroupY      = 960;

    private readonly Mock<IAipReviewCommentRepository> _comments    = new();
    private readonly Mock<IAipRepository>              _aipRepo     = new();
    private readonly Mock<IAuditRepository>            _auditRepo   = new();
    private readonly Mock<IPermissionService>          _permissions = new();
    private readonly Mock<IAipDivisionSubmissionRepository> _divisionSubmissions = new();

    private IReadOnlyList<int>? _acknowledgedGroups;
    private Guid? _acknowledgedBy;

    public AipReviewerEditNoticeTests()
    {
        _aipRepo.Setup(r => r.GetByIntIdAsync(RecordId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AipRecord { Id = RecordId, FiscalYear = 2028, Status = PlanningStatus.Draft });
        _aipRepo.Setup(r => r.GetOfficesByAipIdAsync(RecordId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<AipOffice>
            {
                new() { Id = GroupX1, AipRecordId = RecordId, OfficeId = OfficeX, WorkflowStatus = AipWorkflowStatus.Consolidated },
                new() { Id = GroupX2, AipRecordId = RecordId, OfficeId = OfficeX, WorkflowStatus = AipWorkflowStatus.Consolidated },
                new() { Id = GroupY,  AipRecordId = RecordId, OfficeId = OfficeY, WorkflowStatus = AipWorkflowStatus.SubmittedToPpdo },
            });

        _comments.Setup(r => r.AcknowledgeReviewerEditsAsync(
                It.IsAny<IReadOnlyList<int>>(), It.IsAny<Guid>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .Callback((IReadOnlyList<int> ids, Guid by, DateTime _, CancellationToken _) =>
            {
                _acknowledgedGroups = ids;
                _acknowledgedBy = by;
            })
            .ReturnsAsync(3);
    }

    private AipReviewCommentService Comments() => new(
        _comments.Object, _aipRepo.Object, _auditRepo.Object, _permissions.Object,
        NullLogger<AipReviewCommentService>.Instance);

    private User MakeUser(int officeId, bool crossOffice = false, bool hostOffice = false)
    {
        User u = new()
        {
            Id = Guid.NewGuid(), Role = UserRole.Staff, OfficeId = officeId, FullName = "Caller",
            Office = new Office { Id = officeId, OfficeCode = $"O{officeId}", IsHostOffice = hostOffice },
        };
        _permissions.Setup(p => p.CanReviewAllOfficesAsync(u, It.IsAny<CancellationToken>())).ReturnsAsync(crossOffice);
        _permissions.Setup(p => p.CanReviewBudgetPlanningAsync(u, It.IsAny<CancellationToken>())).ReturnsAsync(false);
        return u;
    }

    // ── Mark as seen ──────────────────────────────────────────────────────────

    [Fact]
    public async Task AcknowledgeReviewerEdits_ByTheEditedOfficesUser_StampsEveryGroupOfThatOffice()
    {
        User encoder = MakeUser(OfficeX);

        ServiceResult<AipReviewerEditsAcknowledgedDto> result =
            await Comments().AcknowledgeReviewerEditsAsync(RecordId, OfficeX, encoder);

        Assert.True(result.IsSuccess, result.Error);
        Assert.Equal(3, result.Value!.Acknowledged);
        Assert.Equal([GroupX1, GroupX2], _acknowledgedGroups!.Order());   // both sector groups, never Y's
        Assert.Equal(encoder.Id, _acknowledgedBy);
    }

    /// <summary>
    /// ⚠️ The case to get right: the reviewer CAN see office X, so scope alone would let this through.
    /// The acknowledgement is the office's, so seeing is not enough.
    /// </summary>
    [Fact]
    public async Task AcknowledgeReviewerEdits_ByAPpdoReviewerOnAnotherOffice_IsForbidden()
    {
        ServiceResult<AipReviewerEditsAcknowledgedDto> result =
            await Comments().AcknowledgeReviewerEditsAsync(RecordId, OfficeX, MakeUser(OfficeY, crossOffice: true));

        Assert.Equal(ServiceErrorCode.Forbidden, result.Code);
        Assert.Null(_acknowledgedGroups);
    }

    /// <summary>A host-office (PPDO) user also sees every office, and is refused the same way.</summary>
    [Fact]
    public async Task AcknowledgeReviewerEdits_ByAHostOfficeUserOnAnotherOffice_IsForbidden()
    {
        ServiceResult<AipReviewerEditsAcknowledgedDto> result =
            await Comments().AcknowledgeReviewerEditsAsync(RecordId, OfficeX, MakeUser(OfficeY, hostOffice: true));

        Assert.Equal(ServiceErrorCode.Forbidden, result.Code);
        Assert.Null(_acknowledgedGroups);
    }

    /// <summary>An office user who cannot see office X gets the same not-found a missing office does.</summary>
    [Fact]
    public async Task AcknowledgeReviewerEdits_ByAnotherOfficesEncoder_IsNotFound()
    {
        ServiceResult<AipReviewerEditsAcknowledgedDto> result =
            await Comments().AcknowledgeReviewerEditsAsync(RecordId, OfficeX, MakeUser(OfficeY));

        Assert.Equal(ServiceErrorCode.NotFound, result.Code);
        Assert.Null(_acknowledgedGroups);
    }

    [Fact]
    public async Task AcknowledgeReviewerEdits_OnAMissingRecord_IsNotFound()
    {
        ServiceResult<AipReviewerEditsAcknowledgedDto> result =
            await Comments().AcknowledgeReviewerEditsAsync(RecordId + 1, OfficeX, MakeUser(OfficeX));

        Assert.Equal(ServiceErrorCode.NotFound, result.Code);
    }

    /// <summary>Nothing pending is a success with 0, so a double click is harmless.</summary>
    [Fact]
    public async Task AcknowledgeReviewerEdits_WithNothingPending_SucceedsWithZero()
    {
        _comments.Setup(r => r.AcknowledgeReviewerEditsAsync(
                It.IsAny<IReadOnlyList<int>>(), It.IsAny<Guid>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(0);

        ServiceResult<AipReviewerEditsAcknowledgedDto> result =
            await Comments().AcknowledgeReviewerEditsAsync(RecordId, OfficeX, MakeUser(OfficeX));

        Assert.True(result.IsSuccess);
        Assert.Equal(0, result.Value!.Acknowledged);
    }

    // ── The comment DTO ───────────────────────────────────────────────────────

    [Fact]
    public async Task GetForOffice_ExposesTheReviewerEditFlagAndTheAcknowledgement()
    {
        DateTime seenAt = new(2026, 10, 14, 6, 0, 0, DateTimeKind.Utc);
        _comments.Setup(r => r.GetByOfficeIdsAsync(It.IsAny<IReadOnlyList<int>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<AipReviewComment>
            {
                new() { Id = 1, AipOfficeId = GroupX1, NodeType = AipCommentNodeType.Activity, NodeId = 41,
                        AuthorSide = AipCommentSide.Ppdo, Body = "PPDO edited …", CreatedAt = seenAt,
                        ResolvedAt = seenAt, IsReviewerEdit = true, AcknowledgedAt = seenAt,
                        AcknowledgedBy = new User { FullName = "Ana Encoder" } },
                new() { Id = 2, AipOfficeId = GroupX1, NodeType = AipCommentNodeType.Activity, NodeId = 41,
                        AuthorSide = AipCommentSide.Ppdo, Body = "Please check", CreatedAt = seenAt },
            });
        _aipRepo.Setup(r => r.GetProgramsByOfficeIdsNoTrackingAsync(It.IsAny<IReadOnlyList<int>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);
        _aipRepo.Setup(r => r.GetProjectsByProgramIdsNoTrackingAsync(It.IsAny<IReadOnlyList<int>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);
        _aipRepo.Setup(r => r.GetActivitiesByProjectIdsNoTrackingAsync(It.IsAny<IReadOnlyList<int>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        ServiceResult<AipReviewCommentsDto> result =
            await Comments().GetForOfficeAsync(RecordId, OfficeX, MakeUser(OfficeX));

        AipReviewCommentDto edit = result.Value!.Comments.Single(c => c.Id == 1);
        AipReviewCommentDto plain = result.Value!.Comments.Single(c => c.Id == 2);
        Assert.True(edit.IsReviewerEdit);
        Assert.Equal(seenAt, edit.AcknowledgedAt);
        Assert.Equal("Ana Encoder", edit.AcknowledgedByName);
        Assert.False(plain.IsReviewerEdit);
        Assert.Null(plain.AcknowledgedAt);
    }

    // ── The dashboard notice ──────────────────────────────────────────────────

    private AipNotificationService Notifications()
    {
        _aipRepo.Setup(r => r.CountOfficesAtStatusByFiscalYearAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<int, int>());
        _aipRepo.Setup(r => r.GetOfficeStatusesAsync(OfficeX, PlanningStatus.Draft,
                AipFiscalYears.FirstEnteredFiscalYear, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<AipOfficeStatusRow>
            {
                new(RecordId,     2028, GroupX1, AipWorkflowStatus.Consolidated),
                new(RecordId,     2028, GroupX2, AipWorkflowStatus.Consolidated),
                new(RecordId + 1, 2029, 970,     AipWorkflowStatus.SubmittedToPpdo),
            });
        _aipRepo.Setup(r => r.GetOfficeStatusesAsync(OfficeY, PlanningStatus.Draft,
                AipFiscalYears.FirstEnteredFiscalYear, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<AipOfficeStatusRow> { new(RecordId, 2028, GroupY, AipWorkflowStatus.SubmittedToPpdo) });

        // ⚠️ Filters on the ids it is GIVEN, like the real GROUP BY — so a service that asked about
        // every office, or about office Y, would show up here as a wrong count.
        Dictionary<int, int> pending = new() { [GroupX1] = 2, [GroupX2] = 1, [970] = 4, [GroupY] = 5 };
        _comments.Setup(r => r.CountUnacknowledgedReviewerEditsAsync(It.IsAny<IReadOnlyList<int>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyList<int> ids, CancellationToken _) =>
                pending.Where(kv => ids.Contains(kv.Key)).ToDictionary(kv => kv.Key, kv => kv.Value));

        return new AipNotificationService(
            _aipRepo.Object, _auditRepo.Object, _permissions.Object, _divisionSubmissions.Object, _comments.Object);
    }

    [Fact]
    public async Task Notifications_CountTheOwnOfficesUnseenEditsAcrossItsGroupsAndYears()
    {
        AipReviewNotificationsDto dto = (await Notifications().GetForCallerAsync(MakeUser(OfficeX))).Value!;

        Assert.Equal(7, dto.ReviewerEditCount);       // 2 + 1 in FY2028, 4 in FY2029
        Assert.Equal(2028, dto.ReviewerEditFiscalYear); // the earliest year with something to see
    }

    /// <summary>⚠️ Office Y's 5 never reach office X, and X's never reach Y.</summary>
    [Fact]
    public async Task Notifications_NeverCountAnotherOfficesEdits()
    {
        AipReviewNotificationsDto dto = (await Notifications().GetForCallerAsync(MakeUser(OfficeY))).Value!;

        Assert.Equal(5, dto.ReviewerEditCount);
        Assert.Equal(2028, dto.ReviewerEditFiscalYear);
    }

    /// <summary>A PPDO reviewer's own office is what counts for them too, not every office they review.</summary>
    [Fact]
    public async Task Notifications_ForACrossOfficeReviewer_CountOnlyTheirOwnOffice()
    {
        AipReviewNotificationsDto dto =
            (await Notifications().GetForCallerAsync(MakeUser(OfficeY, crossOffice: true))).Value!;

        Assert.Equal(5, dto.ReviewerEditCount);
    }

    [Fact]
    public async Task Notifications_WithNothingPending_AreZeroWithNoYear()
    {
        AipNotificationService sut = Notifications();
        _comments.Setup(r => r.CountUnacknowledgedReviewerEditsAsync(It.IsAny<IReadOnlyList<int>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<int, int>());

        AipReviewNotificationsDto dto = (await sut.GetForCallerAsync(MakeUser(OfficeX))).Value!;

        Assert.Equal(0, dto.ReviewerEditCount);
        Assert.Null(dto.ReviewerEditFiscalYear);
    }
}
