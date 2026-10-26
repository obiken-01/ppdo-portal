using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using PPDO.Application.Common;
using PPDO.Application.DTOs.BudgetPlanning;
using PPDO.Application.Services;
using PPDO.Domain.Entities;
using PPDO.Domain.Interfaces;

namespace PPDO.Tests.Application;

/// <summary>
/// <see cref="AipCeilingService"/> on an SAIP's working copy (v1.10.0 — PPDO-247, decision 10).
///
/// <para>
/// ⚠️ <b>Red-tested.</b> Making <c>SumEncodedBaseRoundedAsync</c> ignore the SAIP fails
/// <see cref="GetStatus_ForACopyGroup_MeasuresTheAmendedTotals"/>; switching the ledger's office
/// lookup to <c>AipGroupLookup</c> fails <see cref="UpsertLedger_ForACopyActivity_WritesNothing"/>.
/// </para>
/// </summary>
public sealed class AipCeilingSaipTests
{
    private const int Record = 28, Pto = 3, Gf = 5, SaipId = 9;
    private const int ApprovedGroup = 10, CopyGroup = 110;

    private readonly Mock<IAipRepository>                 _aipRepo    = new();
    private readonly Mock<IAipExpenditureRepository>      _expRepo    = new();
    private readonly Mock<IAipAllocationLedgerRepository> _ledgerRepo = new();
    private readonly Mock<IOfficeRepository>              _offices    = new();
    private readonly Mock<IAllocationService>             _allocation = new();

    private readonly AipOffice _copy = new()
    {
        Id = CopyGroup, AipRecordId = Record, OfficeId = Pto, RefCode = "R", Name = "PTO", Sector = "GENERAL",
        WorkflowStatus = AipWorkflowStatus.Draft, SaipId = SaipId, SourceId = ApprovedGroup,
    };

    public AipCeilingSaipTests()
    {
        _aipRepo.Setup(r => r.GetByIntIdAsync(Record, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AipRecord { Id = Record, FiscalYear = 2028, Status = PlanningStatus.Draft });
        // As in the database: the filtered lookup cannot see the copy, the copy lookup can.
        _aipRepo.Setup(r => r.GetOfficeByIdAsync(CopyGroup, It.IsAny<CancellationToken>())).ReturnsAsync((AipOffice?)null);
        _aipRepo.Setup(r => r.GetDraftGroupByIdAsync(CopyGroup, It.IsAny<CancellationToken>())).ReturnsAsync(_copy);

        _allocation.Setup(a => a.GetGeneralFundIdAsync(It.IsAny<CancellationToken>())).ReturnsAsync(Gf);
        _allocation.Setup(a => a.GetCeilingAsync(Pto, 2028, Gf, It.IsAny<CancellationToken>()))
            .ReturnsAsync(ServiceResult<BudgetCeilingDto>.Ok(new BudgetCeilingDto(1, Pto, 2028, Gf, "GF", "General Fund", 10_000m)));
        _allocation.Setup(a => a.GetEnforcedAipCeilingFundsAsync(It.IsAny<CancellationToken>())).ReturnsAsync([]);

        // Approved: ₱8,000 of General Fund. The copy raised it to ₱12,000.
        _expRepo.Setup(r => r.SumMooeCoByConfigOfficeAndFundAsync(Record, Pto, Gf, It.IsAny<CancellationToken>()))
            .ReturnsAsync([new AipActivityFundTotalsDto(1, 8_000m, 0m)]);
        _expRepo.Setup(r => r.SumMooeCoBySaipAndFundAsync(SaipId, Gf, It.IsAny<CancellationToken>()))
            .ReturnsAsync([new AipActivityFundTotalsDto(101, 12_000m, 0m)]);
    }

    private AipCeilingService Sut() => new(
        _aipRepo.Object, _expRepo.Object, _ledgerRepo.Object, Mock.Of<IAllocationRepository>(),
        _offices.Object, _allocation.Object, NullLogger<AipCeilingService>.Instance);

    [Fact]
    public async Task GetStatus_ForACopyGroup_MeasuresTheAmendedTotals()
    {
        AipCeilingStatusDto status = await Sut().GetStatusAsync(CopyGroup);

        Assert.Equal(12_000m, status.EncodedBaseRounded);
        Assert.Equal(-2_000m, status.Remaining);
        Assert.False(status.WithinCeiling);
        _expRepo.Verify(r => r.SumMooeCoByConfigOfficeAndFundAsync(
            It.IsAny<int>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ValidateForSubmit_ForACopyGroup_RefusesOnTheAmendedTotals()
    {
        string? refusal = await Sut().ValidateForSubmitAsync(CopyGroup);

        Assert.NotNull(refusal);
        Assert.Contains("12,000.00", refusal);
    }

    [Fact]
    public async Task UpsertLedger_ForACopyActivity_WritesNothing()
    {
        // A copy activity under a HOST office, tagged to a division: exactly the case that would write
        // a reservation if the copy's office resolved. It must not — the AIP would count twice.
        _aipRepo.Setup(r => r.GetActivityByIdAsync(101, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AipActivity { Id = 101, ProjectId = 201, RefCode = "A", Name = "A", DivisionId = 6, SourceId = 1 });
        _aipRepo.Setup(r => r.GetProjectByIdAsync(201, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AipProject { Id = 201, ProgramId = 301, RefCode = "J", Name = "J" });
        _aipRepo.Setup(r => r.GetProgramByIdAsync(301, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AipProgram { Id = 301, OfficeId = CopyGroup, RefCode = "P", Name = "P" });
        _offices.Setup(o => o.GetByIdAsync(Pto, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Office { Id = Pto, IsHostOffice = true });
        _expRepo.Setup(r => r.GetByActivityIdAsync(101, It.IsAny<CancellationToken>()))
            .ReturnsAsync([new AipExpenditure { Id = 1, ActivityId = 101, FundingSourceId = Gf, Mooe = 500m }]);

        await Sut().UpsertLedgerForActivityAsync(101);

        _ledgerRepo.Verify(r => r.AddAsync(It.IsAny<AipDivisionAllocationLedger>(), It.IsAny<CancellationToken>()), Times.Never);
        _ledgerRepo.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }
}
