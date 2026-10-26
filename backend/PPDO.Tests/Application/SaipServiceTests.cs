using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using PPDO.Application.Common;
using PPDO.Application.DTOs.BudgetPlanning;
using PPDO.Application.Services;
using PPDO.Domain.Common;
using PPDO.Domain.Entities;
using PPDO.Domain.Enums;
using PPDO.Domain.Interfaces;

using PPDO.Tests.TestHelpers;

namespace PPDO.Tests.Application;

/// <summary>
/// <see cref="SaipService"/> — start, cancel, list, get (v1.10.0 — PPDO-246, SAIP_Spec.md §3–§4).
/// The guards and the per-role table, against mocked repositories and the real
/// <see cref="PermissionService"/>. The copy itself and the transaction are pinned against SQL Server
/// in <c>SaipCopySqlServerTests</c>; the copy's field-by-field rules in <see cref="SaipTreeCopierTests"/>.
///
/// <para>
/// ⚠️ <b>Red-tested (PPDO-246).</b> Each guard was removed in turn and its test watched fail: the
/// entered-year check, the head check (and its own-office half), the archived check, the
/// every-group Consolidated check, the open-SAIP check, the read scope, the cancel state check and
/// the cancel's saip-belongs-to-office check.
/// </para>
/// </summary>
public sealed partial class SaipServiceTests
{
    private const int Record   = 28;
    private const int Pto      = 3;     // the guest office starting the SAIP
    private const int Gso      = 9;     // another guest office
    private const int Ppdo     = 1;     // the host office
    private const int NewSaip  = 501;

    private readonly Mock<ISaipRepository>           _saipRepo = new();
    private readonly Mock<IAipRepository>            _aipRepo  = new();
    private readonly Mock<IAipExpenditureRepository> _expRepo  = new();
    private readonly Mock<IAuditService>             _audit    = new();
    private readonly Mock<IUserRepository>           _users    = new();
    // PPDO-247 — the letter, the send and the live changes (SaipServiceTests.EditSubmit.cs).
    private readonly Mock<IFileStore>                       _files     = new();
    private readonly Mock<IAipSubmitService>                _submit    = new();
    private readonly Mock<IInvestmentProposalRepository>    _proposals = new();
    private readonly RecordingNotificationService           _notifications = new();
    private readonly Mock<IAipCeilingService>               _ceiling   = new();
    private readonly Mock<IAipDivisionSubmissionRepository> _divRepo   = new();

    private AipRecord _record = new() { Id = Record, FiscalYear = 2028, Status = PlanningStatus.Draft };

    private readonly List<AipOffice> _groups =
    [
        new() { Id = 10, AipRecordId = Record, OfficeId = Pto, RefCode = "1000-000-1-01-020", Name = "PTO", Sector = "GENERAL", WorkflowStatus = AipWorkflowStatus.Consolidated },
        new() { Id = 11, AipRecordId = Record, OfficeId = Pto, RefCode = "1000-000-1-01-020", Name = "PTO - Assessor", Sector = "GENERAL", WorkflowStatus = AipWorkflowStatus.Consolidated },
        new() { Id = 12, AipRecordId = Record, OfficeId = Gso, RefCode = "1000-000-1-01-030", Name = "GSO", Sector = "GENERAL", WorkflowStatus = AipWorkflowStatus.Consolidated },
    ];

    private SaipTreeCopier.DraftCopy? _copied;
    private AipSaip? _added;

    public SaipServiceTests()
    {
        _aipRepo.Setup(r => r.GetByIntIdAsync(Record, It.IsAny<CancellationToken>())).ReturnsAsync(() => _record);
        _aipRepo.Setup(r => r.GetOfficesByAipIdNoTrackingAsync(Record, It.IsAny<CancellationToken>())).ReturnsAsync(() => _groups);
        _aipRepo.Setup(r => r.GetProgramsByOfficeIdsNoTrackingAsync(It.IsAny<IReadOnlyList<int>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyList<int> ids, CancellationToken _) =>
                ids.Select(id => new AipProgram { Id = id * 10, OfficeId = id, RefCode = $"P{id}", Name = "Program" }).ToList());
        _aipRepo.Setup(r => r.GetProjectsByProgramIdsNoTrackingAsync(It.IsAny<IReadOnlyList<int>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);
        _aipRepo.Setup(r => r.GetActivitiesByProjectIdsNoTrackingAsync(It.IsAny<IReadOnlyList<int>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);
        _expRepo.Setup(r => r.GetByActivityIdsAsync(It.IsAny<IReadOnlyList<int>>(), It.IsAny<CancellationToken>())).ReturnsAsync([]);
        _expRepo.Setup(r => r.GetProcurementItemsByExpenditureIdsAsync(It.IsAny<IReadOnlyList<int>>(), It.IsAny<CancellationToken>())).ReturnsAsync([]);

        _saipRepo.Setup(r => r.GetMaxNumberAsync(Record, It.IsAny<int>(), It.IsAny<CancellationToken>())).ReturnsAsync(0);
        _saipRepo.Setup(r => r.AddAsync(It.IsAny<AipSaip>(), It.IsAny<CancellationToken>()))
            .Callback((AipSaip s, CancellationToken _) => { s.Id = NewSaip; _added = s; })
            .Returns(Task.CompletedTask);
        _saipRepo.Setup(r => r.AddDraftCopyAsync(It.IsAny<IReadOnlyList<AipOffice>>(), It.IsAny<IReadOnlyList<AipExpenditure>>(), It.IsAny<CancellationToken>()))
            .Callback((IReadOnlyList<AipOffice> g, IReadOnlyList<AipExpenditure> e, CancellationToken _) => _copied = new(g, e))
            .Returns(Task.CompletedTask);
        _saipRepo.Setup(r => r.ExecuteInTransactionAsync(It.IsAny<Func<Task>>(), It.IsAny<CancellationToken>()))
            .Returns((Func<Task> op, CancellationToken _) => op());
        _saipRepo.Setup(r => r.GetDraftOfficesAsync(It.IsAny<int>(), It.IsAny<CancellationToken>())).ReturnsAsync([]);
        _saipRepo.Setup(r => r.GetForOfficeAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>())).ReturnsAsync([]);

        _users.Setup(r => r.GetNamesByIdsAsync(It.IsAny<IReadOnlyList<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<Guid, string>());
    }

    private SaipService Sut() => new(
        _saipRepo.Object, _aipRepo.Object, _expRepo.Object, new PermissionService(),
        _audit.Object, _users.Object, _files.Object, _submit.Object, _proposals.Object, _notifications,
        _ceiling.Object, _divRepo.Object, NullLogger<SaipService>.Instance);

    // ── Callers (spec §3, per-role table) ─────────────────────────────────────

    private static User GuestUser(int officeId, UserRole role = UserRole.Staff, bool head = false) => new()
    {
        Id = Guid.NewGuid(), Role = role, OfficeId = officeId,
        Office = new Office { Id = officeId, OfficeCode = "X", IsHostOffice = false },
        OverrideCanReviewBudgetPlanning = head ? true : null,
    };

    private static User Head(int officeId = Pto)    => GuestUser(officeId, head: true);
    private static User Encoder(int officeId = Pto) => GuestUser(officeId);
    private static User GuestAdmin(int officeId = Pto) => GuestUser(officeId, UserRole.Admin);

    private static User HostAdmin() => new()
    {
        Id = Guid.NewGuid(), Role = UserRole.Admin, OfficeId = Ppdo,
        Office = new Office { Id = Ppdo, OfficeCode = "PPDO", IsHostOffice = true },
    };

    private static User PpdoReviewer() => new()
    {
        Id = Guid.NewGuid(), Role = UserRole.Staff, OfficeId = Ppdo,
        Office = new Office { Id = Ppdo, OfficeCode = "PPDO", IsHostOffice = true },
        OverrideCanReviewAllOffices = true,
    };

    private static User SuperAdmin() => new() { Id = Guid.NewGuid(), Role = UserRole.SuperAdmin };

    private AipSaip Saip(string status, int officeId = Pto, int number = 1) => new()
    {
        Id = 77, AipRecordId = Record, OfficeId = officeId, Number = number, Status = status,
        StartedById = Guid.NewGuid(), StartedAt = DateTime.UtcNow,
    };

    // ── Start: the happy path ─────────────────────────────────────────────────

    [Fact]
    public async Task StartAsync_HeadOnConsolidatedOffice_CreatesSaip1AndCopiesOnlyThatOfficesGroups()
    {
        User head = Head();
        ServiceResult<SaipDto> result = await Sut().StartAsync(Record, Pto, head);

        Assert.True(result.IsSuccess, result.Error);
        Assert.Equal(1, result.Value!.Number);
        Assert.Equal(SaipStatus.Draft, result.Value.Status);
        Assert.True(result.Value.IsOpen);
        Assert.Equal(2028, result.Value.FiscalYear);

        Assert.NotNull(_added);
        Assert.Equal(head.Id, _added!.StartedById);
        Assert.Equal(Pto, _added.OfficeId);

        // Both PTO groups, never GSO's, each pointing back at its approved row and at the new SAIP.
        Assert.NotNull(_copied);
        Assert.Equal([10, 11], _copied!.Groups.Select(g => g.SourceId!.Value).Order());
        Assert.All(_copied.Groups, g => Assert.Equal(NewSaip, g.SaipId));
        Assert.All(_copied.Groups, g => Assert.Equal(AipWorkflowStatus.Draft, g.WorkflowStatus));

        _audit.Verify(a => a.LogAsync("aip_saips", NewSaip, AuditAction.SaipStart,
            null, It.IsAny<object>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task StartAsync_SavesTheSaipAndTheCopyInsideOneTransaction()
    {
        List<string> order = [];
        _saipRepo.Setup(r => r.ExecuteInTransactionAsync(It.IsAny<Func<Task>>(), It.IsAny<CancellationToken>()))
            .Returns(async (Func<Task> op, CancellationToken _) =>
            {
                order.Add("begin");
                await op();
                order.Add("commit");
            });
        _saipRepo.Setup(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .Callback(() => order.Add("save")).ReturnsAsync(1);

        await Sut().StartAsync(Record, Pto, Head());

        // Both saves happen between begin and commit — a failed copy rolls the SAIP row back too.
        Assert.Equal(["begin", "save", "save", "commit"], order);
    }

    [Fact]
    public async Task StartAsync_AfterACancelledSaip_TakesTheNextNumber()
    {
        _saipRepo.Setup(r => r.GetMaxNumberAsync(Record, Pto, It.IsAny<CancellationToken>())).ReturnsAsync(2);

        ServiceResult<SaipDto> result = await Sut().StartAsync(Record, Pto, Head());

        Assert.Equal(3, result.Value!.Number);
    }

    // ── Start: per role (spec §3) ─────────────────────────────────────────────

    [Fact]
    public async Task StartAsync_GuestAdminOfTheOffice_IsTheHead()
        => Assert.True((await Sut().StartAsync(Record, Pto, GuestAdmin())).IsSuccess);

    [Fact]
    public async Task StartAsync_SuperAdmin_StartsAsHead()
        => Assert.True((await Sut().StartAsync(Record, Pto, SuperAdmin())).IsSuccess);

    [Fact]
    public async Task StartAsync_Encoder_Returns403()
    {
        ServiceResult<SaipDto> result = await Sut().StartAsync(Record, Pto, Encoder());

        Assert.Equal(ServiceErrorCode.Forbidden, result.Code);
        Assert.Equal("Only the department head can start an SAIP.", result.Error);
        _saipRepo.Verify(r => r.AddAsync(It.IsAny<AipSaip>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    /// <summary>
    /// ⚠️ A host-office Admin can READ every office, so scope alone would let them in. The head rule's
    /// own-office half is what refuses them.
    /// </summary>
    [Fact]
    public async Task StartAsync_HostOfficeAdminOnAnotherOffice_Returns403()
        => Assert.Equal(ServiceErrorCode.Forbidden, (await Sut().StartAsync(Record, Pto, HostAdmin())).Code);

    [Fact]
    public async Task StartAsync_PpdoReviewer_Returns403()
        => Assert.Equal(ServiceErrorCode.Forbidden, (await Sut().StartAsync(Record, Pto, PpdoReviewer())).Code);

    /// <summary>Another office's department head: 404, the same answer as an office that is not there.</summary>
    [Fact]
    public async Task StartAsync_AnotherOfficesHead_Returns404()
    {
        ServiceResult<SaipDto> result = await Sut().StartAsync(Record, Pto, Head(Gso));

        Assert.Equal(ServiceErrorCode.NotFound, result.Code);
        Assert.Equal($"AIP office {Pto} not found in record {Record}.", result.Error);
    }

    [Fact]
    public async Task StartAsync_UnknownRecord_Returns404()
        => Assert.Equal(ServiceErrorCode.NotFound, (await Sut().StartAsync(999, Pto, Head())).Code);

    [Fact]
    public async Task StartAsync_OfficeWithNoGroupsInTheRecord_Returns404()
    {
        const int empty = 44;
        Assert.Equal(ServiceErrorCode.NotFound, (await Sut().StartAsync(Record, empty, Head(empty))).Code);
    }

    // ── Start: state guards (spec §3) ─────────────────────────────────────────

    [Fact]
    public async Task StartAsync_Fy2027_Returns400ForAnyone()
    {
        _record = new AipRecord { Id = Record, FiscalYear = 2027, Status = PlanningStatus.Draft };

        // Asked of the encoder on purpose: the year refuses before the head check does.
        ServiceResult<SaipDto> result = await Sut().StartAsync(Record, Pto, Encoder());

        Assert.Equal(ServiceErrorCode.BadRequest, result.Code);
    }

    [Fact]
    public async Task StartAsync_ArchivedRecord_Returns409()
    {
        _record = new AipRecord { Id = Record, FiscalYear = 2028, Status = PlanningStatus.Archived };

        Assert.Equal(ServiceErrorCode.Conflict, (await Sut().StartAsync(Record, Pto, Head())).Code);
    }

    [Fact]
    public async Task StartAsync_OfficeInDepartmentReview_Returns409WithTheSpecMessage()
    {
        foreach (AipOffice g in _groups) g.WorkflowStatus = AipWorkflowStatus.DepartmentReview;

        ServiceResult<SaipDto> result = await Sut().StartAsync(Record, Pto, Head());

        Assert.Equal(ServiceErrorCode.Conflict, result.Code);
        Assert.Equal("An SAIP starts once your AIP is marked as done.", result.Error);
    }

    /// <summary>⚠️ One group not Consolidated is enough to refuse: every group row is checked, not Groups[0].</summary>
    [Fact]
    public async Task StartAsync_OneGroupNotConsolidated_Returns409()
    {
        _groups[1].WorkflowStatus = AipWorkflowStatus.SubmittedToPpdo;

        Assert.Equal(ServiceErrorCode.Conflict, (await Sut().StartAsync(Record, Pto, Head())).Code);
    }

    [Fact]
    public async Task StartAsync_SaipAlreadyOpen_Returns409NamingIt()
    {
        _saipRepo.Setup(r => r.GetOpenAsync(Record, Pto, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Saip(SaipStatus.Draft, number: 1));

        ServiceResult<SaipDto> result = await Sut().StartAsync(Record, Pto, Head());

        Assert.Equal(ServiceErrorCode.Conflict, result.Code);
        Assert.Equal("SAIP 1 is still open.", result.Error);
        _saipRepo.Verify(r => r.AddAsync(It.IsAny<AipSaip>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    /// <summary>Two starts that both pass the open check: the filtered unique index refuses the second.</summary>
    [Fact]
    public async Task StartAsync_LosesTheRaceToTheOneOpenIndex_Returns409()
    {
        AipSaip winner = Saip(SaipStatus.Draft, number: 1);
        _saipRepo.SetupSequence(r => r.GetOpenAsync(Record, Pto, It.IsAny<CancellationToken>()))
            .ReturnsAsync((AipSaip?)null)
            .ReturnsAsync(winner);
        _saipRepo.Setup(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new UniqueConstraintViolationException("dup", "UX_aip_saips_one_open_per_office", new Exception()));

        ServiceResult<SaipDto> result = await Sut().StartAsync(Record, Pto, Head());

        Assert.Equal(ServiceErrorCode.Conflict, result.Code);
        Assert.Equal("SAIP 1 is still open.", result.Error);
    }

    // ── Cancel ────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData(SaipStatus.Draft)]
    [InlineData(SaipStatus.DepartmentReview)]
    [InlineData(SaipStatus.SubmittedToPpdo)]
    [InlineData(SaipStatus.ReturnedByPpdo)]
    public async Task CancelAsync_OpenSaip_DeletesTheCopyAndMarksCancelled(string status)
    {
        AipSaip saip = Saip(status);
        _saipRepo.Setup(r => r.GetByIntIdAsync(77, It.IsAny<CancellationToken>())).ReturnsAsync(saip);
        _saipRepo.Setup(r => r.DeleteDraftCopyAsync(77, It.IsAny<CancellationToken>())).ReturnsAsync(2);
        User head = Head();

        ServiceResult<SaipDto> result = await Sut().CancelAsync(Record, Pto, 77, head);

        Assert.True(result.IsSuccess, result.Error);
        Assert.Equal(SaipStatus.Cancelled, result.Value!.Status);
        Assert.False(result.Value.IsOpen);
        Assert.Empty(result.Value.DraftAipOfficeIds);
        Assert.Equal(head.Id, saip.CancelledById);
        Assert.NotNull(saip.CancelledAt);
        _saipRepo.Verify(r => r.DeleteDraftCopyAsync(77, It.IsAny<CancellationToken>()), Times.Once);
        _audit.Verify(a => a.LogAsync("aip_saips", 77, AuditAction.SaipCancel,
            It.IsAny<object>(), It.IsAny<object>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Theory]
    [InlineData(SaipStatus.Approved, "SAIP 1 is already approved.")]
    [InlineData(SaipStatus.Cancelled, "SAIP 1 is already cancelled.")]
    public async Task CancelAsync_ClosedSaip_Returns409AndDeletesNothing(string status, string message)
    {
        _saipRepo.Setup(r => r.GetByIntIdAsync(77, It.IsAny<CancellationToken>())).ReturnsAsync(Saip(status));

        ServiceResult<SaipDto> result = await Sut().CancelAsync(Record, Pto, 77, Head());

        Assert.Equal(ServiceErrorCode.Conflict, result.Code);
        Assert.Equal(message, result.Error);
        _saipRepo.Verify(r => r.DeleteDraftCopyAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task CancelAsync_Encoder_Returns403AndDeletesNothing()
    {
        _saipRepo.Setup(r => r.GetByIntIdAsync(77, It.IsAny<CancellationToken>())).ReturnsAsync(Saip(SaipStatus.Draft));

        ServiceResult<SaipDto> result = await Sut().CancelAsync(Record, Pto, 77, Encoder());

        Assert.Equal(ServiceErrorCode.Forbidden, result.Code);
        _saipRepo.Verify(r => r.DeleteDraftCopyAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    /// <summary>⚠️ An SAIP id from another office, asked for under PTO's route: 404, nothing deleted.</summary>
    [Fact]
    public async Task CancelAsync_SaipOfAnotherOfficeUnderThisRoute_Returns404()
    {
        _saipRepo.Setup(r => r.GetByIntIdAsync(77, It.IsAny<CancellationToken>())).ReturnsAsync(Saip(SaipStatus.Draft, officeId: Gso));

        ServiceResult<SaipDto> result = await Sut().CancelAsync(Record, Pto, 77, Head());

        Assert.Equal(ServiceErrorCode.NotFound, result.Code);
        _saipRepo.Verify(r => r.DeleteDraftCopyAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task CancelAsync_AnotherOfficesHead_Returns404()
    {
        _saipRepo.Setup(r => r.GetByIntIdAsync(77, It.IsAny<CancellationToken>())).ReturnsAsync(Saip(SaipStatus.Draft));

        Assert.Equal(ServiceErrorCode.NotFound, (await Sut().CancelAsync(Record, Pto, 77, Head(Gso))).Code);
    }

    [Fact]
    public async Task CancelAsync_ChangedBySomeoneElse_Returns409()
    {
        _saipRepo.Setup(r => r.GetByIntIdAsync(77, It.IsAny<CancellationToken>())).ReturnsAsync(Saip(SaipStatus.Draft));
        _saipRepo.Setup(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new ConcurrencyConflictException("changed", new Exception()));

        Assert.Equal(ServiceErrorCode.Conflict, (await Sut().CancelAsync(Record, Pto, 77, Head())).Code);
    }

    // ── List / Get (spec §3: office readers and reviewers; other offices 404) ─

    [Fact]
    public async Task ListAsync_OfficeEncoder_SeesTheOfficesSaips()
    {
        _saipRepo.Setup(r => r.GetForOfficeAsync(Record, Pto, It.IsAny<CancellationToken>()))
            .ReturnsAsync([Saip(SaipStatus.Draft, number: 2), Saip(SaipStatus.Cancelled, number: 1)]);
        _saipRepo.Setup(r => r.GetDraftOfficesAsync(77, It.IsAny<CancellationToken>()))
            .ReturnsAsync([new AipOffice { Id = 900, SaipId = 77 }]);

        ServiceResult<IReadOnlyList<SaipDto>> result = await Sut().ListAsync(Record, Pto, Encoder());

        Assert.True(result.IsSuccess);
        Assert.Equal([2, 1], result.Value!.Select(s => s.Number));
        Assert.Equal([900], result.Value[0].DraftAipOfficeIds);
        Assert.Empty(result.Value[1].DraftAipOfficeIds);
    }

    [Fact]
    public async Task ListAsync_PpdoReviewer_ReadsAnyOffice()
        => Assert.True((await Sut().ListAsync(Record, Pto, PpdoReviewer())).IsSuccess);

    [Fact]
    public async Task ListAsync_AnotherOfficesUser_Returns404()
        => Assert.Equal(ServiceErrorCode.NotFound, (await Sut().ListAsync(Record, Pto, Encoder(Gso))).Code);

    [Fact]
    public async Task GetAsync_SaipOfAnotherOfficeUnderThisRoute_Returns404()
    {
        _saipRepo.Setup(r => r.GetByIntIdAsync(77, It.IsAny<CancellationToken>())).ReturnsAsync(Saip(SaipStatus.Draft, officeId: Gso));

        Assert.Equal(ServiceErrorCode.NotFound, (await Sut().GetAsync(Record, Pto, 77, Head())).Code);
    }

    [Fact]
    public async Task GetAsync_OwnOfficesSaip_ReturnsIt()
    {
        _saipRepo.Setup(r => r.GetByIntIdAsync(77, It.IsAny<CancellationToken>())).ReturnsAsync(Saip(SaipStatus.Draft));

        ServiceResult<SaipDto> result = await Sut().GetAsync(Record, Pto, 77, Encoder());

        Assert.True(result.IsSuccess);
        Assert.Equal(77, result.Value!.Id);
    }
}
