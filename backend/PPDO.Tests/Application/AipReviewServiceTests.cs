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
/// The PPDO reviewer's return transition (V18-54 / PPDO-72, <c>AIP_Review_Spec.md</c> §3.3, §4.2).
///
/// <para>
/// ⚠️ <b>The 409/400 split is the point of this class.</b> Both refuse a return, and reading them
/// as one case is the easy mistake: <b>409 means another reviewer already moved it</b> and <b>400
/// means the work never reached PPDO in the first place</b>. Collapsing them tells a reviewer that
/// their colleague's action was their own mistake, which is the opposite of what happened.
/// </para>
///
/// <para>
/// ⚠️ <b>Scope here is <c>ResolveForReview</c>, never <c>Resolve</c></b> — the caller is acting on
/// somebody else's office, which is the whole shape of this action and the reason it cannot live on
/// <c>AipSubmitService</c> (whose resolver narrows to the caller's own office by design).
/// </para>
/// </summary>
public sealed class AipReviewServiceTests
{
    private const int RecordId    = 900;
    private const int OfficeId    = 7;   // the office being returned
    private const int OtherOffice = 8;   // where the PPDO reviewer sits
    private const int GroupA      = 950;
    private const int GroupB      = 951;

    private readonly Mock<IAipRepository>         _aipRepo    = new();
    private readonly Mock<IRepository<AipOffice>> _officeRepo = new();
    private readonly Mock<IPermissionService>     _permissions = new();
    private readonly Mock<IAuditService>          _audit      = new();

    /// <summary>
    /// Two group rows for one office — the province's FY2027 SOCIAL sheet has three. Every test
    /// that moves state asserts against <b>both</b>: one office in two states is the defect
    /// decision 21 exists to prevent, and a single-group fixture cannot see it.
    /// </summary>
    private readonly List<AipOffice> _groups =
    [
        new()
        {
            Id = GroupA, AipRecordId = RecordId, OfficeId = OfficeId,
            RefCode = "1000-000-1-01-010", Name = "PPDO - MAIN", Sector = "GENERAL",
            WorkflowStatus = AipWorkflowStatus.SubmittedToPpdo,
        },
        new()
        {
            Id = GroupB, AipRecordId = RecordId, OfficeId = OfficeId,
            RefCode = "3000-000-1-01-010", Name = "PPDO - SOCIAL", Sector = "SOCIAL",
            WorkflowStatus = AipWorkflowStatus.SubmittedToPpdo,
        },
    ];

    private string _recordStatus = PlanningStatus.Draft;

    private AipReviewService Build()
    {
        _aipRepo.Setup(r => r.GetByIntIdAsync(RecordId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => new AipRecord
            {
                Id = RecordId, FiscalYear = 2028, EntrySource = "Manual",
                Status = _recordStatus, UploadedById = Guid.NewGuid(), UploadedAt = DateTime.UtcNow,
            });
        _aipRepo.Setup(r => r.GetOfficesByAipIdAsync(RecordId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => _groups);

        _officeRepo.Setup(r => r.UpdateAsync(It.IsAny<AipOffice>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        _officeRepo.Setup(r => r.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);

        return new AipReviewService(
            _aipRepo.Object, _officeRepo.Object, _permissions.Object, _audit.Object,
            NullLogger<AipReviewService>.Instance);
    }

    // ── Callers ───────────────────────────────────────────────────────────────

    private User MakeUser(int? officeId, bool hostOffice, bool ppdoReviewer)
    {
        User u = new()
        {
            Id = Guid.NewGuid(), Username = "u", PasswordHash = "h", FullName = "Test User",
            Role = UserRole.Staff, OfficeId = officeId,
            Office = officeId is int oid
                ? new Office
                {
                    Id = oid, OfficeCode = $"O{oid}", OfficeName = "Office",
                    IsHostOffice = hostOffice, IsActive = true,
                }
                : null,
            IsActive = true, CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow,
        };
        _permissions.Setup(p => p.CanReviewAllOfficesAsync(u, It.IsAny<CancellationToken>()))
            .ReturnsAsync(ppdoReviewer);
        return u;
    }

    /// <summary>A PPDO consolidated reviewer, sitting in an office that is not the one returned.</summary>
    private User PpdoReviewer() => MakeUser(OtherOffice, hostOffice: false, ppdoReviewer: true);

    /// <summary>An encoder in the office whose work is at PPDO. Holds no reviewer flag.</summary>
    private User Encoder() => MakeUser(OfficeId, hostOffice: false, ppdoReviewer: false);

    /// <summary>
    /// A user inside the <b>host</b> office who holds no reviewer flag — a PPDO division encoder.
    /// ⚠️ The tempting wrong axis (tracker B4): being in PPDO is not the same as being a reviewer.
    /// </summary>
    private User HostOfficeNonReviewer()
        => MakeUser(99, hostOffice: true, ppdoReviewer: false);

    private void GivenEveryGroupIs(string status)
    {
        foreach (AipOffice g in _groups) g.WorkflowStatus = status;
    }

    // ── The happy path ────────────────────────────────────────────────────────

    [Fact]
    public async Task ReturnToOffice_FromSubmittedToPpdo_MovesEveryGroupTogether()
    {
        AipReviewService sut = Build();

        ServiceResult<AipSubmitResultDto> result =
            await sut.ReturnToOfficeAsync(RecordId, OfficeId, PpdoReviewer());

        Assert.True(result.IsSuccess);
        Assert.Equal(AipWorkflowStatus.ReturnedByPpdo, result.Value!.WorkflowStatus);
        Assert.Equal(2, result.Value.GroupsMoved);

        // ⚠️ Both rows, not just the first. Submitting one group and leaving its sibling behind
        // would put one office in two states at once (decision 21).
        Assert.All(_groups, g => Assert.Equal(AipWorkflowStatus.ReturnedByPpdo, g.WorkflowStatus));
        _officeRepo.Verify(r => r.UpdateAsync(It.IsAny<AipOffice>(), It.IsAny<CancellationToken>()),
            Times.Exactly(2));
        _officeRepo.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    /// <summary>
    /// The unlock. ⚠️ It should need <b>no</b> code in this ticket: PPDO-70 already widened
    /// <c>IsOfficeEditable</c> to include <c>ReturnedByPpdo</c>, so returning re-opens the work by
    /// virtue of the state alone. If this fails, something in PPDO-70 regressed — do not "fix" it
    /// by teaching this service about the guard.
    /// </summary>
    [Fact]
    public async Task ReturnToOffice_ReOpensTheWorkForTheOffice()
    {
        AipReviewService sut = Build();

        Assert.False(AipWorkflowStatus.IsOfficeEditable(_groups[0].WorkflowStatus));

        await sut.ReturnToOfficeAsync(RecordId, OfficeId, PpdoReviewer());

        Assert.All(_groups, g => Assert.True(AipWorkflowStatus.IsOfficeEditable(g.WorkflowStatus)));
    }

    /// <summary>
    /// ⚠️ <b>One audit row per transition, not one per group</b> — matching the shipped
    /// <c>SubmitToPpdoAsync</c>, whose row carries the group ids in its payload. The spec's §5.2
    /// warning that a transition writes several rows describes an intent PPDO-69 already departed
    /// from; keeping the two transitions the same shape is what lets PPDO-77 read the whole chain
    /// with one strategy.
    /// </summary>
    [Fact]
    public async Task ReturnToOffice_WritesOneAuditRowNamingTheTransition()
    {
        AipReviewService sut = Build();

        await sut.ReturnToOfficeAsync(RecordId, OfficeId, PpdoReviewer());

        _audit.Verify(a => a.LogAsync(
            "aip_offices", GroupA, AuditAction.ReturnByPpdo,
            It.IsAny<object?>(), It.IsAny<object?>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    // ── 409: somebody else already moved it ───────────────────────────────────

    [Fact]
    public async Task ReturnToOffice_AlreadyReturned_IsConflictNotBadRequest()
    {
        GivenEveryGroupIs(AipWorkflowStatus.ReturnedByPpdo);
        AipReviewService sut = Build();

        ServiceResult<AipSubmitResultDto> result =
            await sut.ReturnToOfficeAsync(RecordId, OfficeId, PpdoReviewer());

        Assert.Equal(ServiceErrorCode.Conflict, result.Code);
        // Verbatim from spec §4.2 — PPDO-74 asserts the same sentence for accept.
        Assert.Equal(
            "This office was already returned by someone else. Reload to see the current state.",
            result.Error);
    }

    [Fact]
    public async Task ReturnToOffice_AlreadyAccepted_IsConflictNamingTheState()
    {
        GivenEveryGroupIs(AipWorkflowStatus.Consolidated);
        AipReviewService sut = Build();

        ServiceResult<AipSubmitResultDto> result =
            await sut.ReturnToOfficeAsync(RecordId, OfficeId, PpdoReviewer());

        Assert.Equal(ServiceErrorCode.Conflict, result.Code);
        Assert.Contains(AipWriteGuard.Describe(AipWorkflowStatus.Consolidated), result.Error);
        Assert.Contains("Reload", result.Error);
    }

    // ── 400: it never reached PPDO ────────────────────────────────────────────

    [Theory]
    [InlineData(AipWorkflowStatus.Draft)]
    [InlineData(AipWorkflowStatus.DepartmentReview)]
    public async Task ReturnToOffice_NeverSentToPpdo_IsBadRequestNotConflict(string status)
    {
        GivenEveryGroupIs(status);
        AipReviewService sut = Build();

        ServiceResult<AipSubmitResultDto> result =
            await sut.ReturnToOfficeAsync(RecordId, OfficeId, PpdoReviewer());

        // ⚠️ NOT Conflict. Nobody moved this — it was never at PPDO to be sent back.
        Assert.Equal(ServiceErrorCode.BadRequest, result.Code);
        Assert.Contains("nothing to return", result.Error);
    }

    [Fact]
    public async Task ReturnToOffice_InANonDraftRecord_IsRefused()
    {
        _recordStatus = PlanningStatus.Archived;
        AipReviewService sut = Build();

        ServiceResult<AipSubmitResultDto> result =
            await sut.ReturnToOfficeAsync(RecordId, OfficeId, PpdoReviewer());

        Assert.Equal(ServiceErrorCode.BadRequest, result.Code);
        Assert.All(_groups, g => Assert.Equal(AipWorkflowStatus.SubmittedToPpdo, g.WorkflowStatus));
    }

    // ── 404: existence is never confirmed ─────────────────────────────────────

    /// <summary>
    /// ⚠️ <b>NotFound, not Forbidden</b> (PPDO-46) — and the message must be byte-identical to the
    /// one an office that genuinely does not exist produces, or the response becomes an oracle for
    /// which offices are in the record.
    /// </summary>
    [Fact]
    public async Task ReturnToOffice_ByAnEncoder_IsIndistinguishableFromAMissingOffice()
    {
        AipReviewService sut = Build();

        // The office the encoder belongs to, and which really is in this record.
        ServiceResult<AipSubmitResultDto> refused =
            await sut.ReturnToOfficeAsync(RecordId, OfficeId, Encoder());

        // ⚠️ Asserted against the literal sentence, not against another call's message. The two
        // ids would have to differ for the comparison call to be about a missing office, and then
        // the strings differ for that reason alone — which is what a first draft of this test did,
        // and it proved nothing. The invariant is that the message is a function of the ids ONLY:
        // asking about office 7 says the same thing whether office 7 is absent or merely not
        // yours, so the response can never be used to enumerate the record's offices.
        Assert.Equal(ServiceErrorCode.NotFound, refused.Code);
        Assert.Equal($"AIP office {OfficeId} not found in record {RecordId}.", refused.Error);

        ServiceResult<AipSubmitResultDto> missing =
            await sut.ReturnToOfficeAsync(RecordId, 4242, PpdoReviewer());
        Assert.Equal(ServiceErrorCode.NotFound, missing.Code);
        Assert.Equal($"AIP office {4242} not found in record {RecordId}.", missing.Error);

        Assert.All(_groups, g => Assert.Equal(AipWorkflowStatus.SubmittedToPpdo, g.WorkflowStatus));
    }

    /// <summary>
    /// ⚠️ <b>The gate is the reviewer flag, not the host office</b> (tracker B4). A PPDO division
    /// encoder resolves to <c>OfficeScope.All</c> on the office axis, so scope alone would let them
    /// return any office in the province. The route gates on the flag too; this is the service
    /// refusing on its own account rather than trusting that.
    /// </summary>
    [Fact]
    public async Task ReturnToOffice_ByAHostOfficeUserWithoutTheReviewerFlag_IsNotFound()
    {
        AipReviewService sut = Build();

        ServiceResult<AipSubmitResultDto> result =
            await sut.ReturnToOfficeAsync(RecordId, OfficeId, HostOfficeNonReviewer());

        Assert.Equal(ServiceErrorCode.NotFound, result.Code);
        Assert.All(_groups, g => Assert.Equal(AipWorkflowStatus.SubmittedToPpdo, g.WorkflowStatus));
    }

    [Fact]
    public async Task ReturnToOffice_MissingRecord_IsNotFound()
    {
        AipReviewService sut = Build();

        ServiceResult<AipSubmitResultDto> result =
            await sut.ReturnToOfficeAsync(4242, OfficeId, PpdoReviewer());

        Assert.Equal(ServiceErrorCode.NotFound, result.Code);
    }
}
