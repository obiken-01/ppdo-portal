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
/// Expenditure lines follow their activity's division lock (v1.8.0 — PPDO-148,
/// <c>Division_Submit_Spec.md</c> "Further rules" and §3.3). One test per call site of
/// <see cref="AipWriteGuard.CheckAsync{T}"/> in <see cref="AipExpenditureService"/> — add, update,
/// delete — because a site that forgot the lock would still compile and still pass every other test.
/// </summary>
public sealed class AipExpenditureDivisionLockTests
{
    private const int ActivityId    = 41;
    private const int ProjectId     = 31;
    private const int ProgramId     = 21;
    private const int AipOfficeId   = 11;
    private const int AipRecordId   = 5;
    private const int ConfigOffice  = 7;
    private const int AccountId     = 100;
    private const int LineId        = 900;

    private const int DivPlanning    = 50;
    private const int DivEngineering = 51;

    private readonly Mock<IAipRepository>             _aipRepo     = new();
    private readonly Mock<IAipExpenditureRepository>  _expRepo     = new();
    private readonly Mock<IAipActivityTotalsService>  _totals      = new();
    private readonly Mock<IAipCeilingService>         _ceiling     = new();
    private readonly Mock<IAccountRepository>       _accounts    = new();
    private readonly Mock<IFundingSourceRepository> _funds       = new();
    private readonly Mock<IAuditService>              _audit       = new();
    private readonly AipDivisionLockFixture           _divisions   = new();
    private readonly AipOffice                        _office;

    public AipExpenditureDivisionLockTests()
    {
        _office = new AipOffice
        {
            Id = AipOfficeId, OfficeId = ConfigOffice, AipRecordId = AipRecordId,
            WorkflowStatus = AipWorkflowStatus.Draft,
        };
        _aipRepo.Setup(r => r.GetActivityByIdAsync(ActivityId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AipActivity { Id = ActivityId, ProjectId = ProjectId, DivisionId = DivPlanning });
        _aipRepo.Setup(r => r.GetProjectByIdAsync(ProjectId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AipProject { Id = ProjectId, ProgramId = ProgramId });
        _aipRepo.Setup(r => r.GetProgramByIdAsync(ProgramId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AipProgram { Id = ProgramId, OfficeId = AipOfficeId });
        _aipRepo.Setup(r => r.GetOfficeByIdAsync(AipOfficeId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(_office);
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
                new() { Id = 1, Code = "GF", Name = "General Fund", IsActive = true,
                        CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow },
            });

        _expRepo.Setup(r => r.GetByIntIdAsync(LineId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AipExpenditure { Id = LineId, ActivityId = ActivityId, AccountId = AccountId, Mooe = 500m });
        _expRepo.Setup(r => r.AddAsync(It.IsAny<AipExpenditure>(), It.IsAny<CancellationToken>()))
            .Callback<AipExpenditure, CancellationToken>((e, _) => e.Id = LineId)
            .Returns(Task.CompletedTask);
        _expRepo.Setup(r => r.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);
        _expRepo.Setup(r => r.SumByActivityIdAsync(ActivityId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AipExpenditureTotalsDto(0m, 0m, 0m, 0m, 1));
        _expRepo.Setup(r => r.GetProcurementItemsByExpenditureIdsAsync(
                It.IsAny<IReadOnlyList<int>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<AipProcurementItem>());

        _divisions.AddDivision(DivPlanning,    ConfigOffice, "Planning Division");
        _divisions.AddDivision(DivEngineering, ConfigOffice, "Engineering Division");
    }

    private AipExpenditureService Build() => new(
        _aipRepo.Object, _expRepo.Object, _totals.Object, _ceiling.Object,
        _accounts.Object, _funds.Object, _audit.Object, new PermissionService(),
        _divisions.Build(_aipRepo.Object, new PermissionService()),
        NullLogger<AipExpenditureService>.Instance);

    private static User Staff(int? divisionId, bool departmentHead = false) => new()
    {
        Id = Guid.NewGuid(), Role = UserRole.Staff, OfficeId = ConfigOffice, DivisionId = divisionId,
        Office = new Office { Id = ConfigOffice, OfficeCode = "GSO" },
        OverrideCanReviewBudgetPlanning = departmentHead ? true : null,
    };

    private static CreateAipExpenditureDto Create() => new(AccountId, 1, 0m, 1_000m, 0m, []);
    private static UpdateAipExpenditureDto Update() => new(AccountId, 1, 0m, 2_000m, 0m, []);

    private void SubmitPlanning() => _divisions.Submit(AipRecordId, ConfigOffice, DivPlanning);

    private void AssertNothingWritten()
    {
        _expRepo.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
        _expRepo.Verify(r => r.DeleteAsync(It.IsAny<AipExpenditure>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    // ── The encoder whose division has submitted ──────────────────────────────

    [Fact]
    public async Task Add_ByEncoder_OnceTheirDivisionHasSubmitted_IsRefused()
    {
        SubmitPlanning();

        ServiceResult<AipExpenditureWriteResultDto> result =
            await Build().AddAsync(ActivityId, Create(), Staff(DivPlanning));

        Assert.Equal(ServiceErrorCode.BadRequest, result.Code);
        Assert.Contains("Planning Division's work has been submitted", result.Error);
        AssertNothingWritten();
    }

    [Fact]
    public async Task Update_ByEncoder_OnceTheirDivisionHasSubmitted_IsRefused()
    {
        SubmitPlanning();

        ServiceResult<AipExpenditureWriteResultDto> result =
            await Build().UpdateAsync(LineId, Update(), Staff(DivPlanning));

        Assert.Equal(ServiceErrorCode.BadRequest, result.Code);
        AssertNothingWritten();
    }

    [Fact]
    public async Task Delete_ByEncoder_OnceTheirDivisionHasSubmitted_IsRefused()
    {
        SubmitPlanning();

        ServiceResult<AipExpenditureWriteResultDto> result =
            await Build().DeleteAsync(LineId, Staff(DivPlanning));

        Assert.Equal(ServiceErrorCode.BadRequest, result.Code);
        AssertNothingWritten();
    }

    // ── Another division's activity ───────────────────────────────────────────

    [Fact]
    public async Task Add_ByAnotherDivisionsEncoder_IsRefused()
    {
        ServiceResult<AipExpenditureWriteResultDto> result =
            await Build().AddAsync(ActivityId, Create(), Staff(DivEngineering));

        Assert.Equal("This activity belongs to Planning Division.", result.Error);
        AssertNothingWritten();
    }

    [Fact]
    public async Task Add_ByEncoderWithNoDivision_IsRefused()
    {
        ServiceResult<AipExpenditureWriteResultDto> result =
            await Build().AddAsync(ActivityId, Create(), Staff(null));

        Assert.Equal(AipDivisionContext.NotAssignedMessage, result.Error);
        AssertNothingWritten();
    }

    // ── What stays allowed ────────────────────────────────────────────────────

    [Fact]
    public async Task Add_ByOwnDivisionsEncoder_WhileDraft_IsAllowed()
    {
        ServiceResult<AipExpenditureWriteResultDto> result =
            await Build().AddAsync(ActivityId, Create(), Staff(DivPlanning));

        Assert.True(result.IsSuccess, result.Error);
    }

    [Fact]
    public async Task Add_ByDepartmentHead_OnSubmittedWork_IsAllowed()
    {
        SubmitPlanning();

        ServiceResult<AipExpenditureWriteResultDto> result =
            await Build().AddAsync(ActivityId, Create(), Staff(null, departmentHead: true));

        Assert.True(result.IsSuccess, result.Error);
    }

    /// <summary>The division rule never grants a write the office state refuses (§3.3).</summary>
    [Fact]
    public async Task Add_ByDepartmentHead_OnceTheOfficeIsWithPpdo_IsStillRefused()
    {
        _office.WorkflowStatus = AipWorkflowStatus.SubmittedToPpdo;

        ServiceResult<AipExpenditureWriteResultDto> result =
            await Build().AddAsync(ActivityId, Create(), Staff(null, departmentHead: true));

        Assert.Equal(ServiceErrorCode.BadRequest, result.Code);
        Assert.Contains("no longer editable here", result.Error);
        AssertNothingWritten();
    }
}
