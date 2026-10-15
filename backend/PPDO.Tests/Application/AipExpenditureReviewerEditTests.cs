using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using PPDO.Application.Common;
using PPDO.Application.DTOs.BudgetPlanning;
using PPDO.Application.Services;
using PPDO.Domain.Entities;
using PPDO.Domain.Enums;
using PPDO.Domain.Interfaces;

using PPDO.Tests.TestHelpers;

namespace PPDO.Tests.Application;

/// <summary>
/// The PPDO reviewer's edit path on <see cref="AipExpenditureService"/> (v1.9.0 — PPDO-212,
/// <c>docs/v1.9/AIP_Reviewer_Edit_Spec.md</c>).
///
/// <para>
/// A cross-office reviewer may add a line, or change a line's amounts, on an office whose AIP is
/// <c>SubmittedToPpdo</c> or <c>Consolidated</c> — to bring a part-year activity up to the full year
/// during the LFC adjustment. Every such write needs a reason, and leaves a PPDO comment on the
/// activity that is <b>already resolved</b>, because only the authoring side may resolve a comment
/// and unresolved PPDO comments feed the office's re-submit gate.
/// </para>
///
/// <para>
/// ⚠️ The reviewers here sit in a <b>guest</b> office (not the host office, not the edited office),
/// the same deliberate choice as <see cref="AipExpenditureReadScopeTests"/>: a reviewer seated in
/// PPDO would pass <c>OfficeScope.Resolve</c> too, and hide a write path that forgot to switch to
/// <c>ResolveForReview</c>.
/// </para>
/// </summary>
public sealed partial class AipExpenditureReviewerEditTests
{
    // PPDO-221 — records what each event stages; see RecordingNotificationService.
    private readonly RecordingNotificationService _notifications = new();

    private const int ActivityId  = 41;
    private const int ProjectId   = 31;
    private const int ProgramId   = 21;
    private const int AipOfficeId = 11;
    private const int AipRecordId = 5;
    private const int EditedOffice = 7;
    private const int GuestOffice  = 9;
    private const int LineId       = 900;
    private const int MooeAccount  = 3;
    private const int OtherAccount = 4;
    private const string Reason    = "Activity runs Jan–Jun only; LFC annualised it.";

    private static readonly byte[] Version = [0, 0, 0, 0, 0, 0, 0, 9];

    private readonly Mock<IAipRepository>              _aipRepo     = new();
    private readonly Mock<IAipExpenditureRepository>   _expRepo     = new();
    private readonly Mock<IAipActivityTotalsService>   _totals      = new();
    private readonly Mock<IAipCeilingService>          _ceiling     = new();
    private readonly Mock<IAccountRepository>          _accounts    = new();
    private readonly Mock<IFundingSourceRepository>    _funds       = new();
    private readonly Mock<IAuditService>               _audit       = new();
    private readonly Mock<IPermissionService>          _permissions = new();
    private readonly Mock<IUserRepository>             _users       = new(MockBehavior.Loose);
    private readonly Mock<IAipReviewCommentRepository> _comments    = new();

    private readonly AipOffice _office = new()
    {
        Id = AipOfficeId, OfficeId = EditedOffice, AipRecordId = AipRecordId,
        WorkflowStatus = AipWorkflowStatus.SubmittedToPpdo,
    };

    private readonly AipRecord _record = new() { Id = AipRecordId, FiscalYear = 2028, Status = PlanningStatus.Draft };

    private readonly AipExpenditure _line;

    private readonly List<AipReviewComment> _posted = [];

    public AipExpenditureReviewerEditTests()
    {
        _line = new AipExpenditure
        {
            Id = LineId, ActivityId = ActivityId, AccountId = MooeAccount,
            AccountNumberSnapshot = "5-02-03-010", AccountTitleSnapshot = "Office Supplies Expenses",
            Mooe = 60_000m, RowVersion = Version,
        };
        _line.Recalculate();

        _aipRepo.Setup(r => r.GetActivityByIdAsync(ActivityId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AipActivity { Id = ActivityId, ProjectId = ProjectId });
        _aipRepo.Setup(r => r.GetProjectByIdAsync(ProjectId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AipProject { Id = ProjectId, ProgramId = ProgramId });
        _aipRepo.Setup(r => r.GetProgramByIdAsync(ProgramId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AipProgram { Id = ProgramId, OfficeId = AipOfficeId });
        _aipRepo.Setup(r => r.GetOfficeByIdAsync(AipOfficeId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(_office);
        _aipRepo.Setup(r => r.GetByIntIdAsync(AipRecordId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(_record);

        Account mooe  = new() { Id = MooeAccount,  AccountNumber = "5-02-03-010", AccountTitle = "Office Supplies Expenses", ExpenseClass = "MOOE" };
        Account other = new() { Id = OtherAccount, AccountNumber = "5-02-01-010", AccountTitle = "Traveling Expenses - Local", ExpenseClass = "MOOE" };
        _accounts.Setup(r => r.GetByIntIdAsync(MooeAccount,  It.IsAny<CancellationToken>())).ReturnsAsync(mooe);
        _accounts.Setup(r => r.GetByIntIdAsync(OtherAccount, It.IsAny<CancellationToken>())).ReturnsAsync(other);
        _funds.Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>())).ReturnsAsync(new List<FundingSource>());

        _expRepo.Setup(r => r.GetByIntIdAsync(LineId, It.IsAny<CancellationToken>())).ReturnsAsync(_line);
        _expRepo.Setup(r => r.SumByActivityIdAsync(ActivityId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AipExpenditureTotalsDto(0m, 0m, 0m, 0m, 1));
        _expRepo.Setup(r => r.GetProcurementItemsByExpenditureIdsAsync(
                It.IsAny<IReadOnlyList<int>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);
        _expRepo.Setup(r => r.GetFundCodesByActivityIdAsync(ActivityId, It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);
        _expRepo.Setup(r => r.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);

        _comments.Setup(r => r.AddAsync(It.IsAny<AipReviewComment>(), It.IsAny<CancellationToken>()))
            .Callback((AipReviewComment c, CancellationToken _) => _posted.Add(c))
            .Returns(Task.CompletedTask);
        _comments.Setup(r => r.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);
    }

    private AipExpenditureService Build() => new(
        _aipRepo.Object, _expRepo.Object, _totals.Object, _ceiling.Object,
        _accounts.Object, _funds.Object, _audit.Object, _permissions.Object,
        AipDivisionLockFixture.None(_aipRepo.Object, _permissions.Object),
        _users.Object, _comments.Object, _notifications, NullLogger<AipExpenditureService>.Instance);

    // ── Callers ───────────────────────────────────────────────────────────────

    private User MakeUser(int officeId, bool crossOfficeReviewer, UserRole role = UserRole.Staff)
    {
        User u = new()
        {
            Id = Guid.NewGuid(), Role = role, OfficeId = officeId, FullName = "Pia Reviewer",
            Office = new Office { Id = officeId, OfficeCode = $"O{officeId}", IsHostOffice = false },
        };
        _permissions.Setup(p => p.CanReviewAllOfficesAsync(u, It.IsAny<CancellationToken>()))
            .ReturnsAsync(crossOfficeReviewer);
        return u;
    }

    private User Reviewer() => MakeUser(GuestOffice, crossOfficeReviewer: true);
    private User OwnEncoder() => MakeUser(EditedOffice, crossOfficeReviewer: false);

    private static UpdateAipExpenditureDto Annualise(string? reason = Reason, int accountId = MooeAccount)
        => new(accountId, null, 0m, 120_000m, 0m, null, Convert.ToBase64String(Version), reason);

    private static CreateAipExpenditureDto AddTravel(string? reason = Reason)
        => new(OtherAccount, null, 0m, 45_500m, 0m, [], reason);

    private void VerifyNothingWritten()
    {
        _expRepo.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
        _expRepo.Verify(r => r.AddAsync(It.IsAny<AipExpenditure>(), It.IsAny<CancellationToken>()), Times.Never);
        Assert.Empty(_posted);
    }

    // ── The happy paths ───────────────────────────────────────────────────────

    [Theory]
    [InlineData(AipWorkflowStatus.SubmittedToPpdo)]
    [InlineData(AipWorkflowStatus.Consolidated)]
    public async Task UpdateAsync_ByReviewerOnASubmittedOffice_SavesTheNewAmount(string status)
    {
        _office.WorkflowStatus = status;

        ServiceResult<AipExpenditureWriteResultDto> result =
            await Build().UpdateAsync(LineId, Annualise(), Reviewer(), Version);

        Assert.True(result.IsSuccess, result.Error);
        Assert.Equal(120_000m, _line.Mooe);
        _expRepo.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.AtLeastOnce);
    }

    /// <summary>An edit is a record of a change, not a hand-off: the office stays where it was.</summary>
    [Theory]
    [InlineData(AipWorkflowStatus.SubmittedToPpdo)]
    [InlineData(AipWorkflowStatus.Consolidated)]
    public async Task UpdateAsync_ByReviewer_LeavesTheWorkflowStateAlone(string status)
    {
        _office.WorkflowStatus = status;

        await Build().UpdateAsync(LineId, Annualise(), Reviewer(), Version);

        Assert.Equal(status, _office.WorkflowStatus);
    }

    [Fact]
    public async Task UpdateAsync_ByReviewer_PostsAResolvedPpdoCommentOnTheActivity()
    {
        User reviewer = Reviewer();

        await Build().UpdateAsync(LineId, Annualise(), reviewer, Version);

        AipReviewComment c = Assert.Single(_posted);
        Assert.Equal(AipOfficeId, c.AipOfficeId);
        Assert.Equal(AipCommentNodeType.Activity, c.NodeType);
        Assert.Equal(ActivityId, c.NodeId);
        Assert.Equal(reviewer.Id, c.AuthorId);
        Assert.Equal(AipCommentSide.Ppdo, c.AuthorSide);
        // PPDO-213 — flagged, so the office's notice can count it; pending until the office sees it.
        Assert.True(c.IsReviewerEdit);
        Assert.Null(c.AcknowledgedAt);
        // ⚠️ The point of the design: born resolved, so it never counts as an open PPDO comment.
        Assert.NotNull(c.ResolvedAt);
        Assert.Equal(reviewer.Id, c.ResolvedById);
        Assert.False(c.IsUnresolved);
    }

    [Fact]
    public async Task UpdateAsync_ByReviewer_CommentStatesTheAccountTheChangeAndTheReason()
    {
        await Build().UpdateAsync(LineId, Annualise(), Reviewer(), Version);

        Assert.Equal(
            "PPDO edited 5-02-03-010 Office Supplies Expenses: MOOE ₱60,000.00 → ₱120,000.00. "
            + $"Reason: {Reason}",
            Assert.Single(_posted).Body);
    }

    [Fact]
    public async Task AddAsync_ByReviewerOnAConsolidatedOffice_AddsTheLineAndCommentsOnIt()
    {
        _office.WorkflowStatus = AipWorkflowStatus.Consolidated;

        ServiceResult<AipExpenditureWriteResultDto> result =
            await Build().AddAsync(ActivityId, AddTravel(), Reviewer());

        Assert.True(result.IsSuccess, result.Error);
        _expRepo.Verify(r => r.AddAsync(
            It.Is<AipExpenditure>(l => l.AccountId == OtherAccount && l.Mooe == 45_500m),
            It.IsAny<CancellationToken>()), Times.Once);
        AipReviewComment c = Assert.Single(_posted);
        Assert.Equal(
            $"PPDO added 5-02-01-010 Traveling Expenses - Local: MOOE ₱45,500.00. Reason: {Reason}",
            c.Body);
        Assert.NotNull(c.ResolvedAt);
        Assert.Equal(AipWorkflowStatus.Consolidated, _office.WorkflowStatus);
    }

    /// <summary>
    /// An itemised line's amount is its items' total, so a reviewer annualises it by changing the
    /// items (follow-up A). The comment still reports the column's old and new figure.
    /// </summary>
    [Fact]
    public async Task UpdateAsync_ByReviewerChangingProcurementItems_CommentsWithTheLinesOldAndNewTotal()
    {
        UpdateAipExpenditureDto dto = new(
            MooeAccount, null, 0m, 0m, 0m,
            [new SaveAipProcurementItemDto(null, "Bond paper, A4", "ream", 250m, 480m, 1m, 1)],
            Convert.ToBase64String(Version), Reason);

        ServiceResult<AipExpenditureWriteResultDto> result =
            await Build().UpdateAsync(LineId, dto, Reviewer(), Version);

        Assert.True(result.IsSuccess, result.Error);
        Assert.Equal(
            "PPDO edited 5-02-03-010 Office Supplies Expenses: MOOE ₱60,000.00 → ₱120,000.00. "
            + $"Reason: {Reason}",
            Assert.Single(_posted).Body);
    }

    // ── Refusals ──────────────────────────────────────────────────────────────

    /// <summary>
    /// The office's own turn. ⚠️ A 400 naming the rule, not a 404: the reviewer can already READ this
    /// office (ResolveForReview), so pretending it does not exist would only confuse them.
    /// </summary>
    [Theory]
    [InlineData(AipWorkflowStatus.Draft)]
    [InlineData(AipWorkflowStatus.DepartmentReview)]
    [InlineData(AipWorkflowStatus.ReturnedByPpdo)]
    public async Task UpdateAsync_ByReviewerBeforeTheOfficeSubmitsToPpdo_IsRefused(string status)
    {
        _office.WorkflowStatus = status;

        ServiceResult<AipExpenditureWriteResultDto> result =
            await Build().UpdateAsync(LineId, Annualise(), Reviewer(), Version);

        Assert.Equal(ServiceErrorCode.BadRequest, result.Code);
        Assert.Equal("PPDO can edit this office's AIP only after it is submitted to PPDO.", result.Error);
        VerifyNothingWritten();
        Assert.Equal(60_000m, _line.Mooe);
    }

    [Theory]
    [InlineData(AipWorkflowStatus.Draft)]
    [InlineData(AipWorkflowStatus.DepartmentReview)]
    [InlineData(AipWorkflowStatus.ReturnedByPpdo)]
    public async Task AddAsync_ByReviewerBeforeTheOfficeSubmitsToPpdo_IsRefused(string status)
    {
        _office.WorkflowStatus = status;

        ServiceResult<AipExpenditureWriteResultDto> result =
            await Build().AddAsync(ActivityId, AddTravel(), Reviewer());

        Assert.Equal(ServiceErrorCode.BadRequest, result.Code);
        Assert.Equal("PPDO can edit this office's AIP only after it is submitted to PPDO.", result.Error);
        VerifyNothingWritten();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task UpdateAsync_ByReviewerWithoutAReason_IsRefused(string? reason)
    {
        ServiceResult<AipExpenditureWriteResultDto> result =
            await Build().UpdateAsync(LineId, Annualise(reason), Reviewer(), Version);

        Assert.Equal(ServiceErrorCode.BadRequest, result.Code);
        Assert.Equal("A reason is required for a PPDO edit (up to 500 characters).", result.Error);
        VerifyNothingWritten();
    }

    [Fact]
    public async Task UpdateAsync_ByReviewerWithA501CharacterReason_IsRefused()
    {
        ServiceResult<AipExpenditureWriteResultDto> result =
            await Build().UpdateAsync(LineId, Annualise(new string('x', 501)), Reviewer(), Version);

        Assert.Equal(ServiceErrorCode.BadRequest, result.Code);
        VerifyNothingWritten();
    }

    [Fact]
    public async Task UpdateAsync_ByReviewerWithA500CharacterReason_Saves()
    {
        ServiceResult<AipExpenditureWriteResultDto> result =
            await Build().UpdateAsync(LineId, Annualise(new string('x', 500)), Reviewer(), Version);

        Assert.True(result.IsSuccess, result.Error);
    }

    [Fact]
    public async Task AddAsync_ByReviewerWithoutAReason_IsRefused()
    {
        ServiceResult<AipExpenditureWriteResultDto> result =
            await Build().AddAsync(ActivityId, AddTravel(reason: null), Reviewer());

        Assert.Equal(ServiceErrorCode.BadRequest, result.Code);
        VerifyNothingWritten();
    }

    /// <summary>
    /// Decision 3: amounts only. Moving a line to another account is a different line, so the
    /// reviewer adds one instead — and the comment can then always name the account it changed.
    /// </summary>
    [Fact]
    public async Task UpdateAsync_ByReviewerChangingTheLinesAccount_IsRefused()
    {
        ServiceResult<AipExpenditureWriteResultDto> result =
            await Build().UpdateAsync(LineId, Annualise(accountId: OtherAccount), Reviewer(), Version);

        Assert.Equal(ServiceErrorCode.BadRequest, result.Code);
        Assert.Equal(
            "PPDO can change a line's amounts only. Add a new line for a different account or fund.",
            result.Error);
        VerifyNothingWritten();
    }

    [Fact]
    public async Task UpdateAsync_ByReviewerOnAPre2028Record_IsRefused()
    {
        _record.FiscalYear = 2027;

        ServiceResult<AipExpenditureWriteResultDto> result =
            await Build().UpdateAsync(LineId, Annualise(), Reviewer(), Version);

        Assert.Equal(ServiceErrorCode.BadRequest, result.Code);
        VerifyNothingWritten();
    }

    // ── Everyone else is unchanged ────────────────────────────────────────────

    /// <summary>
    /// ⚠️ The office's own people still cannot touch an office sitting with PPDO — the reviewer path
    /// opens the door for the reviewer only.
    /// </summary>
    [Theory]
    [InlineData(AipWorkflowStatus.SubmittedToPpdo)]
    [InlineData(AipWorkflowStatus.Consolidated)]
    public async Task UpdateAsync_ByTheOfficesOwnEncoderAfterSubmission_IsStillRefused(string status)
    {
        _office.WorkflowStatus = status;

        ServiceResult<AipExpenditureWriteResultDto> result =
            await Build().UpdateAsync(LineId, Annualise(), OwnEncoder(), Version);

        Assert.Equal(ServiceErrorCode.BadRequest, result.Code);
        Assert.NotEqual("PPDO can edit this office's AIP only after it is submitted to PPDO.", result.Error);
        VerifyNothingWritten();
    }

    /// <summary>A reason sent by an encoder is ignored: their saves never post a PPDO comment.</summary>
    [Fact]
    public async Task UpdateAsync_ByEncoderInDraftWithAReason_SavesWithoutAComment()
    {
        _office.WorkflowStatus = AipWorkflowStatus.Draft;

        ServiceResult<AipExpenditureWriteResultDto> result =
            await Build().UpdateAsync(LineId, Annualise(), OwnEncoder(), Version);

        Assert.True(result.IsSuccess, result.Error);
        Assert.Empty(_posted);
    }

    /// <summary>
    /// SuperAdmin is never routed through the reviewer path (it is exempt from the reviewer guard), so
    /// its saves keep the ordinary rules and post nothing. Which refusal it gets is the ordinary
    /// path's business, not this feature's, so only the outcome is asserted.
    /// </summary>
    [Fact]
    public async Task UpdateAsync_BySuperAdminOnASubmittedOffice_KeepsTheOrdinaryRefusal()
    {
        User admin = MakeUser(GuestOffice, crossOfficeReviewer: true, role: UserRole.SuperAdmin);

        ServiceResult<AipExpenditureWriteResultDto> result =
            await Build().UpdateAsync(LineId, Annualise(), admin, Version);

        Assert.False(result.IsSuccess);
        Assert.NotEqual("PPDO can edit this office's AIP only after it is submitted to PPDO.", result.Error);
        VerifyNothingWritten();
    }
}
