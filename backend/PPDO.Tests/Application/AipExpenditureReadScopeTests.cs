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
/// Who may read an activity's expenditure lines — <see cref="AipExpenditureService.GetByActivityAsync"/>.
///
/// <para>
/// ⚠️ <b>The bug this class exists for.</b> The read scoped with <c>OfficeScope.Resolve</c>, which
/// gives every host-office (PPDO) user all offices and everybody else only their own. The PPDO
/// reviewer's one-office review screen loads its lines through this read, so a
/// <c>CanReviewAllOffices</c> holder sitting in a <b>guest</b> office — explicitly supported by
/// <c>OfficeScope.ResolveForReview</c> — got a 404 on every other office's lines. Every fixture the
/// project had put its reviewer in PPDO, which is exactly why nothing caught it: the reviewers in
/// these tests deliberately sit elsewhere.
/// </para>
///
/// <para>
/// ⚠️ <b>The fix is read scope, and must stay read scope.</b> The last test pins that the same
/// reviewer still cannot write another office's lines — <c>ResolveForReview</c> reaching a write
/// path would make a comment-only reviewer an editor of every office.
/// </para>
/// </summary>
public sealed class AipExpenditureReadScopeTests
{
    private const int ActivityId   = 41;
    private const int ProjectId    = 31;
    private const int ProgramId    = 21;
    private const int AipOfficeId  = 11;
    private const int AipRecordId  = 5;
    private const int LinesOffice  = 7;   // the office whose activity is being read
    private const int GuestOffice  = 9;   // somewhere else, and NOT the host office
    private const int MissingId    = 4242;

    private readonly Mock<IAipRepository>             _aipRepo     = new();
    private readonly Mock<IAipExpenditureRepository>  _expRepo     = new();
    private readonly Mock<IAipActivityTotalsService>  _totals      = new();
    private readonly Mock<IAipCeilingService>         _ceiling     = new();
    private readonly Mock<IRepository<Account>>       _accounts    = new();
    private readonly Mock<IRepository<FundingSource>> _funds       = new();
    private readonly Mock<IAuditService>              _audit       = new();
    private readonly Mock<IPermissionService>         _permissions = new();

    public AipExpenditureReadScopeTests()
    {
        _aipRepo.Setup(r => r.GetActivityByIdAsync(ActivityId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AipActivity { Id = ActivityId, ProjectId = ProjectId });
        _aipRepo.Setup(r => r.GetProjectByIdAsync(ProjectId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AipProject { Id = ProjectId, ProgramId = ProgramId });
        _aipRepo.Setup(r => r.GetProgramByIdAsync(ProgramId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AipProgram { Id = ProgramId, OfficeId = AipOfficeId });
        _aipRepo.Setup(r => r.GetOfficeByIdAsync(AipOfficeId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AipOffice
            {
                Id = AipOfficeId, OfficeId = LinesOffice, AipRecordId = AipRecordId,
                WorkflowStatus = AipWorkflowStatus.Draft,
            });
        _aipRepo.Setup(r => r.GetByIntIdAsync(AipRecordId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AipRecord { Id = AipRecordId, FiscalYear = 2028, Status = PlanningStatus.Draft });

        _expRepo.Setup(r => r.GetByActivityIdAsync(ActivityId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyList<AipExpenditure>)[Line()]);
        _expRepo.Setup(r => r.GetProcurementItemsByExpenditureIdsAsync(
                It.IsAny<IReadOnlyList<int>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyList<AipProcurementItem>)[]);
    }

    private AipExpenditureService Build() => new(
        _aipRepo.Object, _expRepo.Object, _totals.Object, _ceiling.Object,
        _accounts.Object, _funds.Object, _audit.Object, _permissions.Object,
        NullLogger<AipExpenditureService>.Instance);

    /// <summary>One typed ₱500 MOOE line. <c>Total</c> is computed, so it goes through Recalculate.</summary>
    private static AipExpenditure Line()
    {
        AipExpenditure line = new() { Id = 900, ActivityId = ActivityId, Mooe = 500m };
        line.Recalculate();
        return line;
    }

    // ── Callers ───────────────────────────────────────────────────────────────

    private User MakeUser(int officeId, bool crossOfficeReviewer)
    {
        User u = new()
        {
            Id = Guid.NewGuid(), Role = UserRole.Staff, OfficeId = officeId,
            // ⚠️ IsHostOffice false on purpose, for both callers — see the class remarks.
            Office = new Office { Id = officeId, OfficeCode = $"O{officeId}", IsHostOffice = false },
        };
        _permissions.Setup(p => p.CanReviewAllOfficesAsync(u, It.IsAny<CancellationToken>()))
            .ReturnsAsync(crossOfficeReviewer);
        return u;
    }

    /// <summary>A PPDO consolidated reviewer who sits in a guest office.</summary>
    private User GuestSeatedReviewer() => MakeUser(GuestOffice, crossOfficeReviewer: true);

    private User GuestEncoder() => MakeUser(GuestOffice, crossOfficeReviewer: false);

    private User OwnOfficeEncoder() => MakeUser(LinesOffice, crossOfficeReviewer: false);

    // ── Reads ─────────────────────────────────────────────────────────────────

    /// <summary>⚠️ The regression: this was a 404 before the fix.</summary>
    [Fact]
    public async Task GetByActivity_ByACrossOfficeReviewerSittingInAGuestOffice_ReadsAnotherOfficesLines()
    {
        ServiceResult<IReadOnlyList<AipExpenditureDto>> result =
            await Build().GetByActivityAsync(ActivityId, GuestSeatedReviewer());

        Assert.True(result.IsSuccess);
        Assert.Equal(500m, Assert.Single(result.Value!).Total);
    }

    /// <summary>
    /// ⚠️ Still refused, and refused with the <b>byte-identical sentence</b> a missing activity
    /// produces (PPDO-46) — otherwise the response confirms another office's activity exists. Both
    /// sentences are asserted literally, from the same template, so a change to one wording that
    /// forgets the other fails here.
    /// </summary>
    [Fact]
    public async Task GetByActivity_ByAGuestEncoderOnAnotherOffice_IsIndistinguishableFromAMissingActivity()
    {
        AipExpenditureService sut = Build();

        ServiceResult<IReadOnlyList<AipExpenditureDto>> notYours =
            await sut.GetByActivityAsync(ActivityId, GuestEncoder());
        ServiceResult<IReadOnlyList<AipExpenditureDto>> missing =
            await sut.GetByActivityAsync(MissingId, GuestEncoder());

        Assert.Equal(ServiceErrorCode.NotFound, notYours.Code);
        Assert.Equal(ServiceErrorCode.NotFound, missing.Code);
        Assert.Equal($"AIP activity {ActivityId} not found.", notYours.Error);
        Assert.Equal($"AIP activity {MissingId} not found.", missing.Error);
        _expRepo.Verify(r => r.GetByActivityIdAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    /// <summary>The ordinary case the entry page depends on, unchanged by the fix.</summary>
    [Fact]
    public async Task GetByActivity_ByAnEncoderOfThatOffice_ReadsTheLines()
    {
        ServiceResult<IReadOnlyList<AipExpenditureDto>> result =
            await Build().GetByActivityAsync(ActivityId, OwnOfficeEncoder());

        Assert.True(result.IsSuccess);
        Assert.Single(result.Value!);
    }

    // ── The boundary ──────────────────────────────────────────────────────────

    /// <summary>
    /// ⚠️ <b>The line the fix must not cross.</b> The reviewer who may now READ another office's
    /// lines still cannot ADD one: writes resolve through <c>AipWriteGuard</c> with
    /// <c>OfficeScope.Resolve</c>, and the cross-office grant is read scope only
    /// (<c>OfficeScope.ResolveForReview</c>'s remarks). Asserted on the repository as well as the
    /// status — a refusal that had already written the row would pass a status-only check.
    /// </summary>
    [Fact]
    public async Task AddAsync_ByACrossOfficeReviewer_StillCannotWriteAnotherOffice()
    {
        ServiceResult<AipExpenditureWriteResultDto> result = await Build().AddAsync(
            ActivityId, new CreateAipExpenditureDto(null, null, 0m, 500m, 0m, []), GuestSeatedReviewer());

        Assert.Equal(ServiceErrorCode.NotFound, result.Code);
        _expRepo.Verify(r => r.AddAsync(It.IsAny<AipExpenditure>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
