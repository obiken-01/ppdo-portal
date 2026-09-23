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
/// The concurrent-edit guard on expenditure lines (V18-71 / PPDO-118).
///
/// <para>
/// The activity tests cover the same mechanism one level up; these exist for the two things that
/// are only true here. <b>Procurement items have no token of their own</b> — no endpoints either —
/// so they are protected by the parent line's, and that only holds while the line's
/// <c>UpdatedAt</c> write stays unconditional. And <b>delete is guarded too</b>, which forced the
/// audit call to move after the save: a delete can now fail, and an audit row saying "deleted"
/// must not outlive a row that still exists.
/// </para>
///
/// <para>
/// ⚠️ As in <c>AipConcurrentEditTests</c>, the conflict is injected rather than provoked — SQLite
/// has no rowversion, so a real <c>DbUpdateConcurrencyException</c> cannot fire in these fixtures
/// (PPDO-117). The database half was verified against SQL Server there.
/// </para>
/// </summary>
public sealed class AipExpenditureConflictTests
{
    private const int ActivityId   = 41;
    private const int ProjectId    = 31;
    private const int ProgramId    = 21;
    private const int AipOfficeId  = 11;
    private const int AipRecordId  = 5;
    private const int ConfigOffice = 7;
    private const int AccountId    = 100;
    private const int LineId       = 900;

    private static readonly byte[] StaleVersion   = [0, 0, 0, 0, 0, 0, 0, 1];
    private static readonly byte[] CurrentVersion = [0, 0, 0, 0, 0, 0, 0, 9];
    private static readonly Guid   OtherEditorId  = Guid.NewGuid();

    private readonly Mock<IAipRepository>             _aipRepo     = new();
    private readonly Mock<IAipExpenditureRepository>  _expRepo     = new();
    private readonly Mock<IAipActivityTotalsService>  _totals      = new();
    private readonly Mock<IAipCeilingService>         _ceiling     = new();
    private readonly Mock<IRepository<Account>>       _accounts    = new();
    private readonly Mock<IRepository<FundingSource>> _funds       = new();
    private readonly Mock<IAuditService>              _audit       = new();
    private readonly Mock<IPermissionService>         _permissions = new();
    private readonly Mock<IUserRepository>            _users       = new();

    private readonly AipExpenditure _line = new()
    {
        Id = LineId, ActivityId = ActivityId, AccountId = AccountId, Mooe = 1_000m,
        RowVersion = CurrentVersion, UpdatedById = OtherEditorId,
        UpdatedAt = new DateTime(2026, 9, 22, 1, 14, 9, DateTimeKind.Utc),
    };

    private static readonly User Encoder = new()
    {
        Id = Guid.NewGuid(), Role = UserRole.Staff, OfficeId = ConfigOffice,
        Office = new Office { Id = ConfigOffice, OfficeCode = "GSO" },
    };

    public AipExpenditureConflictTests()
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
                Id = AipOfficeId, OfficeId = ConfigOffice, AipRecordId = AipRecordId,
                WorkflowStatus = AipWorkflowStatus.Draft,
            });
        _aipRepo.Setup(r => r.GetByIntIdAsync(AipRecordId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AipRecord { Id = AipRecordId, FiscalYear = 2028, Status = PlanningStatus.Draft });

        _accounts.Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Account>
            {
                new() { Id = AccountId, AccountNumber = "5-02-03-010", AccountTitle = "Office Supplies", ExpenseClass = "MOOE" },
            });
        _funds.Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<FundingSource>());

        _expRepo.Setup(r => r.GetByIntIdAsync(LineId, It.IsAny<CancellationToken>())).ReturnsAsync(_line);
        _expRepo.Setup(r => r.SumByActivityIdAsync(ActivityId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AipExpenditureTotalsDto(0m, 0m, 0m, 0m, 1));
        _expRepo.Setup(r => r.GetProcurementItemsByExpenditureIdsAsync(
                It.IsAny<IReadOnlyList<int>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);
        _expRepo.Setup(r => r.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);

        _users.Setup(r => r.GetNamesByIdsAsync(It.IsAny<IReadOnlyList<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyList<Guid> ids, CancellationToken _) =>
                (IReadOnlyDictionary<Guid, string>)(ids.Contains(OtherEditorId)
                    ? new Dictionary<Guid, string> { [OtherEditorId] = "Ben Reyes" }
                    : new Dictionary<Guid, string>()));
    }

    private AipExpenditureService Build() => new(
        _aipRepo.Object, _expRepo.Object, _totals.Object, _ceiling.Object,
        _accounts.Object, _funds.Object, _audit.Object, _permissions.Object,
        _users.Object, NullLogger<AipExpenditureService>.Instance);

    /// <summary>Makes the save reject, and makes the reload behave like a real one.</summary>
    private void ArrangeConflict()
    {
        _expRepo.Setup(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new ConcurrencyConflictException(
                "The row changed since it was loaded.", new InvalidOperationException()));

        // The service stamps the current caller before saving, so without a reload that behaves
        // like one the payload would name the person being refused.
        _expRepo.Setup(r => r.ReloadAsync(It.IsAny<IRowVersioned>(), It.IsAny<CancellationToken>()))
            .Callback(() =>
            {
                _line.UpdatedById = OtherEditorId;
                _line.RowVersion  = CurrentVersion;
            })
            .Returns(Task.CompletedTask);
    }

    private static UpdateAipExpenditureDto Dto(
        IReadOnlyList<SaveAipProcurementItemDto>? items = null, string? rowVersion = null) =>
        new(AccountId, null, 0m, 2_000m, 0m, items, rowVersion);

    // ── Update ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Update_WhenTheLineMovedUnderneath_ReturnsConflictNamingTheOtherEditor()
    {
        ArrangeConflict();

        ServiceResult<AipExpenditureWriteResultDto> result =
            await Build().UpdateAsync(LineId, Dto(), Encoder, StaleVersion);

        Assert.Equal(ServiceErrorCode.Conflict, result.Code);
        Assert.Contains("Ben Reyes", result.Error);
        Assert.Contains("expenditure line", result.Error);

        AipConflictDto<AipExpenditureDto> conflict =
            Assert.IsType<AipConflictDto<AipExpenditureDto>>(result.Details);
        Assert.Equal(Convert.ToBase64String(CurrentVersion), conflict.CurrentRowVersion);
    }

    [Fact]
    public async Task Update_PassesTheCallersVersionToTheRepository()
    {
        await Build().UpdateAsync(LineId, Dto(), Encoder, StaleVersion);

        _expRepo.Verify(r => r.ExpectRowVersion(_line, StaleVersion), Times.Once);
    }

    [Fact]
    public async Task Update_StampsTheActorSoTheNextConflictCanNameThem()
    {
        await Build().UpdateAsync(LineId, Dto(), Encoder);

        Assert.Equal(Encoder.Id, _line.UpdatedById);
    }

    [Fact]
    public async Task Update_WithOnlyProcurementItemsChanged_StillDirtiesTheParentLine()
    {
        // ⚠️ This is what protects procurement items. They have no token and no endpoints, so the
        // parent line's rowversion is their only guard — and it only bumps if the parent row is
        // written. Make AipExpenditureService's `line.UpdatedAt` assignment conditional on the
        // amounts changing and this test fails, which is the point: two encoders editing items
        // under one line would otherwise both succeed, silently.
        DateTime? before = _line.UpdatedAt;

        await Build().UpdateAsync(
            LineId,
            Dto(items: [new SaveAipProcurementItemDto(null, "Bond paper", "ream", 250m, 8m, 1m, 1)]),
            Encoder);

        Assert.NotEqual(before, _line.UpdatedAt);
        Assert.Equal(Encoder.Id, _line.UpdatedById);
    }

    // ── Delete ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Delete_WhenTheLineMovedUnderneath_ReturnsConflict()
    {
        ArrangeConflict();

        ServiceResult<AipExpenditureWriteResultDto> result =
            await Build().DeleteAsync(LineId, Encoder, StaleVersion);

        Assert.Equal(ServiceErrorCode.Conflict, result.Code);
    }

    [Fact]
    public async Task Delete_WhenTheSaveIsRejected_WritesNoAuditRow()
    {
        // ⚠️ The reason the audit call moved after the save. It performs its own SaveChangesAsync,
        // so it is NOT part of a transaction that rolls back with the delete — logging first would
        // leave a permanent record of a deletion that never happened.
        ArrangeConflict();

        await Build().DeleteAsync(LineId, Encoder, StaleVersion);

        _audit.Verify(a => a.LogAsync(
            It.IsAny<string>(), It.IsAny<int>(), AuditAction.Delete,
            It.IsAny<object?>(), It.IsAny<object?>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Delete_WhenItSucceeds_StillWritesItsAuditRow()
    {
        // The other half of the reorder: moving the audit later must not lose it.
        await Build().DeleteAsync(LineId, Encoder);

        _audit.Verify(a => a.LogAsync(
            "aip_expenditures", LineId, AuditAction.Delete,
            It.IsAny<object?>(), null, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Delete_PassesTheCallersVersionToTheRepository()
    {
        await Build().DeleteAsync(LineId, Encoder, StaleVersion);

        _expRepo.Verify(r => r.ExpectRowVersion(_line, StaleVersion), Times.Once);
    }
}
