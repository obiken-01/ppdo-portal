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
/// The funding source an AIP expenditure line may name (v1.8.0, follow-up to PPDO-109).
///
/// PPDO-109 scoped every fund READ; these pin the WRITE. An office may name a province-wide fund or
/// one of its own, and nothing else — enforced here rather than in the picker, because the picker is
/// the half a hand-crafted request skips.
///
/// ⚠️ The refusal is checked by its EFFECT (no row written), not only by the status code. A guard
/// that returned BadRequest after already mutating the entity would pass a status-only assertion.
/// </summary>
public sealed class AipExpenditureFundScopeTests
{
    private const int ActivityId   = 41;
    private const int ProjectId    = 31;
    private const int ProgramId    = 21;
    private const int AipOfficeId  = 11;
    private const int AipRecordId  = 5;
    private const int ConfigOffice = 7;    // the office that owns this activity
    private const int OtherOffice  = 9;
    private const int AccountId    = 100;

    private const int SharedFundId  = 1;
    private const int OwnFundId     = 3;
    private const int ForeignFundId = 4;

    private readonly Mock<IAipRepository>             _aipRepo     = new();
    private readonly Mock<IAipExpenditureRepository>  _expRepo     = new();
    private readonly Mock<IAipActivityTotalsService>  _totals      = new();
    private readonly Mock<IAipCeilingService>         _ceiling     = new();
    private readonly Mock<IRepository<Account>>       _accounts    = new();
    private readonly Mock<IRepository<FundingSource>> _funds       = new();
    private readonly Mock<IAuditService>              _audit       = new();
    private readonly Mock<IPermissionService>         _permissions = new();

    private static readonly User Encoder = new()
    {
        Id = Guid.NewGuid(), Role = UserRole.Staff, OfficeId = ConfigOffice,
        Office = new Office { Id = ConfigOffice, OfficeCode = "GSO" },
    };

    private static FundingSource Fs(int id, string code, int? officeId = null) => new()
    {
        Id = id, Code = code, Name = $"Fund {code}", IsActive = true, OfficeId = officeId,
        CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow,
    };

    public AipExpenditureFundScopeTests()
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
            .ReturnsAsync(new List<FundingSource>
            {
                Fs(SharedFundId,  "GF"),
                Fs(OwnFundId,     "GSOX", ConfigOffice),
                Fs(ForeignFundId, "PHOX", OtherOffice),
            });

        _expRepo.Setup(r => r.AddAsync(It.IsAny<AipExpenditure>(), It.IsAny<CancellationToken>()))
            .Callback<AipExpenditure, CancellationToken>((e, _) => e.Id = 900)
            .Returns(Task.CompletedTask);
        _expRepo.Setup(r => r.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);
        _expRepo.Setup(r => r.SumByActivityIdAsync(ActivityId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AipExpenditureTotalsDto(0m, 0m, 0m, 0m, 1));
        _expRepo.Setup(r => r.GetProcurementItemsByExpenditureIdsAsync(
                It.IsAny<IReadOnlyList<int>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<AipProcurementItem>());
    }

    private AipExpenditureService Build() => new(
        _aipRepo.Object, _expRepo.Object, _totals.Object, _ceiling.Object,
        _accounts.Object, _funds.Object, _audit.Object, _permissions.Object,
        NullLogger<AipExpenditureService>.Instance);

    private static CreateAipExpenditureDto Create(int? fundId) =>
        new(AccountId, fundId, 0m, 1_000m, 0m, []);

    private Task<ServiceResult<AipExpenditureWriteResultDto>> AddWithFund(int? fundId)
        => Build().AddAsync(ActivityId, Create(fundId), Encoder, CancellationToken.None);

    private void AssertNothingWritten()
        => _expRepo.Verify(r => r.AddAsync(It.IsAny<AipExpenditure>(), It.IsAny<CancellationToken>()), Times.Never);

    // ── Add ───────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Add_WithASharedFund_Succeeds()
    {
        ServiceResult<AipExpenditureWriteResultDto> result = await AddWithFund(SharedFundId);
        Assert.True(result.IsSuccess);
    }

    [Fact]
    public async Task Add_WithTheOfficesOwnFund_Succeeds()
    {
        ServiceResult<AipExpenditureWriteResultDto> result = await AddWithFund(OwnFundId);
        Assert.True(result.IsSuccess);
    }

    /// <summary>The hole this change closes: office 7's line naming office 9's private fund.</summary>
    [Fact]
    public async Task Add_WithAnotherOfficesFund_IsRefusedAndWritesNothing()
    {
        ServiceResult<AipExpenditureWriteResultDto> result = await AddWithFund(ForeignFundId);

        Assert.Equal(ServiceErrorCode.BadRequest, result.Code);
        AssertNothingWritten();
    }

    [Fact]
    public async Task Add_WithAnotherOfficesFund_DoesNotNameTheOwningOffice()
    {
        // ⚠️ The message must not confirm the fund exists or say whose it is — otherwise a rejected
        // save enumerates other offices' funds one id at a time.
        ServiceResult<AipExpenditureWriteResultDto> result = await AddWithFund(ForeignFundId);

        // ⚠️ Assert the refusal FIRST. Without it the Error is null, and Assert.DoesNotContain on a
        // null string passes vacuously — so this test would still be green with the guard removed,
        // which is the one thing a guard test must never do.
        Assert.False(result.IsSuccess);
        Assert.NotNull(result.Error);
        Assert.DoesNotContain("office", result.Error, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("PHOX",   result.Error, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// ↩️ The second bug the guard closes. This used to SUCCEED: <c>FundingSourceId</c> was assigned
    /// from the request before the row was looked up, so an id matching nothing was persisted with
    /// both snapshots left null — a dangling FK rendering as a line with no fund.
    /// </summary>
    [Fact]
    public async Task Add_WithAFundThatDoesNotExistAtAll_IsRefusedRatherThanStoredDangling()
    {
        ServiceResult<AipExpenditureWriteResultDto> result = await AddWithFund(4242);

        Assert.Equal(ServiceErrorCode.BadRequest, result.Code);
        AssertNothingWritten();
    }

    [Fact]
    public async Task Add_WithNoFund_IsStillAllowed()
    {
        // A fundless line remains legal — the scope check must not turn an optional field required.
        ServiceResult<AipExpenditureWriteResultDto> result = await AddWithFund(null);
        Assert.True(result.IsSuccess);
    }

    [Fact]
    public async Task Add_WithAVisibleFund_SnapshotsItsCodeAndName()
    {
        AipExpenditure? written = null;
        _expRepo.Setup(r => r.AddAsync(It.IsAny<AipExpenditure>(), It.IsAny<CancellationToken>()))
            .Callback<AipExpenditure, CancellationToken>((e, _) => { e.Id = 900; written = e; })
            .Returns(Task.CompletedTask);

        await AddWithFund(OwnFundId);

        Assert.Equal(OwnFundId, written!.FundingSourceId);
        Assert.Equal("GSOX",      written.FundingSourceSnapshot);
        Assert.Equal("Fund GSOX", written.FundingSourceNameSnapshot);
    }

    // ── Update ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Update_SwitchingToAnotherOfficesFund_IsRefused()
    {
        // The edit path is its own door into the same field, and it is the likelier one: the line is
        // created legitimately through the UI, then re-saved with a swapped id.
        _expRepo.Setup(r => r.GetByIntIdAsync(900, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AipExpenditure
            {
                Id = 900, ActivityId = ActivityId, AccountId = AccountId,
                FundingSourceId = SharedFundId, FundingSourceSnapshot = "GF",
            });

        ServiceResult<AipExpenditureWriteResultDto> result = await Build().UpdateAsync(
            900, new UpdateAipExpenditureDto(AccountId, ForeignFundId, 0m, 1_000m, 0m, null),
            Encoder, CancellationToken.None);

        Assert.Equal(ServiceErrorCode.BadRequest, result.Code);
        _expRepo.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Update_ToASharedFund_Succeeds()
    {
        _expRepo.Setup(r => r.GetByIntIdAsync(900, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AipExpenditure
            {
                Id = 900, ActivityId = ActivityId, AccountId = AccountId,
                FundingSourceId = OwnFundId, FundingSourceSnapshot = "GSOX",
            });

        ServiceResult<AipExpenditureWriteResultDto> result = await Build().UpdateAsync(
            900, new UpdateAipExpenditureDto(AccountId, SharedFundId, 0m, 1_000m, 0m, null),
            Encoder, CancellationToken.None);

        Assert.True(result.IsSuccess);
    }
}
