using Moq;
using PPDO.Application.DTOs.BudgetPlanning;
using PPDO.Application.Services;
using PPDO.Domain.Entities;
using PPDO.Domain.Interfaces;

namespace PPDO.Tests.Application;

/// <summary>
/// Unit tests for <see cref="WfpCeilingService"/> (RAL-122). Covers the 6 Acceptance Criteria
/// scenarios verbatim: under both ceilings (succeeds), over AIP only (rejected), over
/// allocation only (rejected), over both (rejected), exactly at the boundary (not a false
/// rejection), and the ledger row upserting as expenditure totals change. All repositories
/// and IAllocationService are mocked.
/// </summary>
public sealed class WfpCeilingServiceTests
{
    private const int WfpActivityId = 900;
    private const int WfpRecordId   = 1;
    private const int DivisionId    = 5;
    private const int OfficeId      = 3;
    private const int FiscalYear    = 2027;
    private const int AipActivityId = 10;

    private static Division MakeDivision() => new()
    {
        Id = DivisionId, OfficeId = OfficeId, Name = "Planning Division",
        IsActive = true, CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow,
    };

    private static AipActivity MakeAipActivity(decimal totalInThousands) => new()
    {
        Id = AipActivityId, ProjectId = 1, RefCode = "1000-000-1-01-011-001-001-001",
        Name = "Sample Activity", Total = totalInThousands,
    };

    private static (
        WfpCeilingService sut,
        Mock<IWfpExpenditureRepository>      wfpExpRepo,
        Mock<IWfpRepository>                 wfpRepo,
        Mock<IWfpAllocationLedgerRepository> ledgerRepo,
        Mock<IAipRepository>                 aipRepo,
        Mock<IAllocationService>             allocation,
        Mock<IRepository<Division>>          divisionRepo)
        Build(
            WfpExpenditureContext? context = null,
            decimal aipTotalThousands = 1000m,
            decimal divisionAllocationAmount = 1000000m,
            List<Division>? divisions = null)
    {
        Mock<IWfpExpenditureRepository>      wfpExpRepo  = new();
        Mock<IWfpRepository>                 wfpRepo     = new();
        Mock<IWfpAllocationLedgerRepository> ledgerRepo  = new();
        Mock<IAipRepository>                 aipRepo     = new();
        Mock<IAllocationService>             allocation  = new();
        Mock<IRepository<Division>>          divisionRepo = new();

        context ??= new WfpExpenditureContext(WfpRecordId, DivisionId, OfficeId, FiscalYear, AipActivityId);
        wfpExpRepo.Setup(r => r.GetActivityContextAsync(WfpActivityId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(context);

        aipRepo.Setup(r => r.GetActivityByIdAsync(AipActivityId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(MakeAipActivity(aipTotalThousands));

        divisionRepo.Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(divisions ?? [MakeDivision()]);

        allocation.Setup(a => a.GetAllocationsAsync(OfficeId, FiscalYear, It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyList<DivisionAllocationDto>)
                [new DivisionAllocationDto(1, DivisionId, "Planning Division", FiscalYear, divisionAllocationAmount)]);

        ledgerRepo.Setup(r => r.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);

        WfpCeilingService sut = new(
            wfpExpRepo.Object, wfpRepo.Object, ledgerRepo.Object, aipRepo.Object, allocation.Object, divisionRepo.Object);

        return (sut, wfpExpRepo, wfpRepo, ledgerRepo, aipRepo, allocation, divisionRepo);
    }

    // ── Acceptance criterion 1: under both ceilings → succeeds ────────────────

    [Fact]
    public async Task ValidateExpenditureSave_UnderBothCeilings_ReturnsNull()
    {
        // AIP budget = 100,000 pesos (100 thousand). Others already used 50,000.
        var (sut, wfpExpRepo, _, ledgerRepo, _, _, _) = Build(aipTotalThousands: 100m, divisionAllocationAmount: 80000m);
        wfpExpRepo.Setup(r => r.SumTotalByAipActivityAsync(
                AipActivityId, OfficeId, FiscalYear, It.IsAny<int?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(50000m);
        wfpExpRepo.Setup(r => r.SumTotalByWfpRecordAsync(WfpRecordId, It.IsAny<int?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(10000m);
        ledgerRepo.Setup(r => r.SumUsedAmountAsync(DivisionId, FiscalYear, WfpRecordId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(30000m); // other records' usage

        // New expenditure total = 20,000: AIP would-be = 70,000 ≤ 100,000; division would-be = 30,000+30,000 = 60,000 ≤ 80,000.
        string? result = await sut.ValidateExpenditureSaveAsync(WfpActivityId, 20000m, null, CancellationToken.None);

        Assert.Null(result);
    }

    // ── Acceptance criterion 2: over AIP only → rejected ──────────────────────

    [Fact]
    public async Task ValidateExpenditureSave_OverAipBudgetOnly_ReturnsError()
    {
        var (sut, wfpExpRepo, _, ledgerRepo, _, _, _) = Build(aipTotalThousands: 100m, divisionAllocationAmount: 1_000_000m);
        wfpExpRepo.Setup(r => r.SumTotalByAipActivityAsync(
                AipActivityId, OfficeId, FiscalYear, It.IsAny<int?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(90000m);
        wfpExpRepo.Setup(r => r.SumTotalByWfpRecordAsync(WfpRecordId, It.IsAny<int?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(0m);
        ledgerRepo.Setup(r => r.SumUsedAmountAsync(DivisionId, FiscalYear, WfpRecordId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(0m);

        // AIP would-be = 90,000 + 20,000 = 110,000 > 100,000 budget. Division allocation is huge, not the cause.
        string? result = await sut.ValidateExpenditureSaveAsync(WfpActivityId, 20000m, null, CancellationToken.None);

        Assert.NotNull(result);
        Assert.Contains("AIP", result, StringComparison.OrdinalIgnoreCase);
    }

    // ── Acceptance criterion 3: over allocation only → rejected ───────────────

    [Fact]
    public async Task ValidateExpenditureSave_OverDivisionAllocationOnly_ReturnsError()
    {
        var (sut, wfpExpRepo, _, ledgerRepo, _, _, _) = Build(aipTotalThousands: 100_000m, divisionAllocationAmount: 50000m);
        wfpExpRepo.Setup(r => r.SumTotalByAipActivityAsync(
                AipActivityId, OfficeId, FiscalYear, It.IsAny<int?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(0m); // AIP budget is enormous (100M pesos) — never the cause here
        wfpExpRepo.Setup(r => r.SumTotalByWfpRecordAsync(WfpRecordId, It.IsAny<int?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(5000m);
        ledgerRepo.Setup(r => r.SumUsedAmountAsync(DivisionId, FiscalYear, WfpRecordId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(40000m); // other records already used 40,000 of the 50,000 allocation

        // Division would-be = 40,000 + (5,000 + 10,000) = 55,000 > 50,000 allocation.
        string? result = await sut.ValidateExpenditureSaveAsync(WfpActivityId, 10000m, null, CancellationToken.None);

        Assert.NotNull(result);
        Assert.Contains("division", result, StringComparison.OrdinalIgnoreCase);
    }

    // ── Acceptance criterion 4: over both → rejected ──────────────────────────

    [Fact]
    public async Task ValidateExpenditureSave_OverBothCeilings_ReturnsError()
    {
        var (sut, wfpExpRepo, _, ledgerRepo, _, _, _) = Build(aipTotalThousands: 60m, divisionAllocationAmount: 50000m);
        wfpExpRepo.Setup(r => r.SumTotalByAipActivityAsync(
                AipActivityId, OfficeId, FiscalYear, It.IsAny<int?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(55000m); // AIP budget = 60,000; would-be = 55,000+10,000 = 65,000 > 60,000
        wfpExpRepo.Setup(r => r.SumTotalByWfpRecordAsync(WfpRecordId, It.IsAny<int?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(5000m);
        ledgerRepo.Setup(r => r.SumUsedAmountAsync(DivisionId, FiscalYear, WfpRecordId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(48000m); // would-be division = 48,000+15,000 = 63,000 > 50,000

        string? result = await sut.ValidateExpenditureSaveAsync(WfpActivityId, 10000m, null, CancellationToken.None);

        Assert.NotNull(result); // rejected — which specific message wins (AIP is checked first) isn't the point here
    }

    // ── Acceptance criterion 5: exactly at boundary → not a false rejection ──

    [Fact]
    public async Task ValidateExpenditureSave_ExactlyAtBothBoundaries_ReturnsNull()
    {
        var (sut, wfpExpRepo, _, ledgerRepo, _, _, _) = Build(aipTotalThousands: 100m, divisionAllocationAmount: 80000m);
        wfpExpRepo.Setup(r => r.SumTotalByAipActivityAsync(
                AipActivityId, OfficeId, FiscalYear, It.IsAny<int?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(80000m); // would-be AIP = 80,000 + 20,000 = 100,000 == budget exactly
        wfpExpRepo.Setup(r => r.SumTotalByWfpRecordAsync(WfpRecordId, It.IsAny<int?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(20000m);
        ledgerRepo.Setup(r => r.SumUsedAmountAsync(DivisionId, FiscalYear, WfpRecordId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(40000m); // would-be division = 40,000 + (20,000+20,000) = 80,000 == allocation exactly

        string? result = await sut.ValidateExpenditureSaveAsync(WfpActivityId, 20000m, null, CancellationToken.None);

        Assert.Null(result);
    }

    // ── Excludes the expenditure's own OLD total when updating ────────────────

    [Fact]
    public async Task ValidateExpenditureSave_WhenUpdating_ExcludesTheExpendituresOwnOldTotal()
    {
        var (sut, wfpExpRepo, _, ledgerRepo, _, _, _) = Build(aipTotalThousands: 100m, divisionAllocationAmount: 1_000_000m);

        // Verify the exclude id is actually passed through to both sum queries.
        wfpExpRepo.Setup(r => r.SumTotalByAipActivityAsync(
                AipActivityId, OfficeId, FiscalYear, 77, It.IsAny<CancellationToken>()))
            .ReturnsAsync(10000m);
        wfpExpRepo.Setup(r => r.SumTotalByWfpRecordAsync(WfpRecordId, 77, It.IsAny<CancellationToken>()))
            .ReturnsAsync(10000m);
        ledgerRepo.Setup(r => r.SumUsedAmountAsync(DivisionId, FiscalYear, WfpRecordId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(0m);

        string? result = await sut.ValidateExpenditureSaveAsync(WfpActivityId, 15000m, excludeExpenditureId: 77, CancellationToken.None);

        Assert.Null(result);
        wfpExpRepo.Verify(r => r.SumTotalByAipActivityAsync(AipActivityId, OfficeId, FiscalYear, 77, It.IsAny<CancellationToken>()), Times.Once);
        wfpExpRepo.Verify(r => r.SumTotalByWfpRecordAsync(WfpRecordId, 77, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ValidateExpenditureSave_NoDivisionOnRecord_SkipsAllocationCheck()
    {
        // A legacy/no-division WFP record — only the AIP check applies.
        WfpExpenditureContext context = new(WfpRecordId, DivisionId: null, OfficeId, FiscalYear, AipActivityId);
        var (sut, wfpExpRepo, _, ledgerRepo, _, allocation, _) = Build(context, aipTotalThousands: 100m);
        wfpExpRepo.Setup(r => r.SumTotalByAipActivityAsync(
                AipActivityId, OfficeId, FiscalYear, It.IsAny<int?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(10000m);

        string? result = await sut.ValidateExpenditureSaveAsync(WfpActivityId, 5000m, null, CancellationToken.None);

        Assert.Null(result);
        ledgerRepo.Verify(r => r.SumUsedAmountAsync(
            It.IsAny<int>(), It.IsAny<int>(), It.IsAny<int?>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    // ── Acceptance criterion 6: ledger row upserts as totals change ──────────

    [Fact]
    public async Task UpsertLedgerForActivity_NoExistingRow_CreatesOne()
    {
        var (sut, wfpExpRepo, _, ledgerRepo, _, _, _) = Build(divisionAllocationAmount: 80000m);
        wfpExpRepo.Setup(r => r.SumTotalByWfpRecordAsync(WfpRecordId, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(42000m);
        ledgerRepo.Setup(r => r.FindAsync(DivisionId, FiscalYear, WfpRecordId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((WfpDivisionAllocationLedger?)null);

        WfpDivisionAllocationLedger? added = null;
        ledgerRepo.Setup(r => r.AddAsync(It.IsAny<WfpDivisionAllocationLedger>(), It.IsAny<CancellationToken>()))
            .Callback<WfpDivisionAllocationLedger, CancellationToken>((l, _) => added = l)
            .Returns(Task.CompletedTask);

        await sut.UpsertLedgerForActivityAsync(WfpActivityId, CancellationToken.None);

        Assert.NotNull(added);
        Assert.Equal(DivisionId, added!.DivisionId);
        Assert.Equal(FiscalYear, added.FiscalYear);
        Assert.Equal(WfpRecordId, added.WfpRecordId);
        Assert.Equal(42000m, added.UsedAmount);
        Assert.Equal(80000m, added.AllocatedAmountSnapshot);
        ledgerRepo.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task UpsertLedgerForActivity_ExistingRow_UpdatesUsedAmount()
    {
        var (sut, wfpExpRepo, _, ledgerRepo, _, _, _) = Build(divisionAllocationAmount: 80000m);
        wfpExpRepo.Setup(r => r.SumTotalByWfpRecordAsync(WfpRecordId, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(60000m); // totals changed since the row was created

        WfpDivisionAllocationLedger existing = new()
        {
            Id = 1, DivisionId = DivisionId, FiscalYear = FiscalYear, WfpRecordId = WfpRecordId,
            AllocatedAmountSnapshot = 80000m, UsedAmount = 42000m, UpdatedAt = DateTime.UtcNow.AddDays(-1),
        };
        ledgerRepo.Setup(r => r.FindAsync(DivisionId, FiscalYear, WfpRecordId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(existing);

        await sut.UpsertLedgerForActivityAsync(WfpActivityId, CancellationToken.None);

        Assert.Equal(60000m, existing.UsedAmount);
        ledgerRepo.Verify(r => r.UpdateAsync(existing, It.IsAny<CancellationToken>()), Times.Once);
        ledgerRepo.Verify(r => r.AddAsync(It.IsAny<WfpDivisionAllocationLedger>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task UpsertLedgerForActivity_NoDivisionOnRecord_NoOps()
    {
        WfpExpenditureContext context = new(WfpRecordId, DivisionId: null, OfficeId, FiscalYear, AipActivityId);
        var (sut, _, _, ledgerRepo, _, _, _) = Build(context);

        await sut.UpsertLedgerForActivityAsync(WfpActivityId, CancellationToken.None);

        ledgerRepo.Verify(r => r.AddAsync(It.IsAny<WfpDivisionAllocationLedger>(), It.IsAny<CancellationToken>()), Times.Never);
        ledgerRepo.Verify(r => r.UpdateAsync(It.IsAny<WfpDivisionAllocationLedger>(), It.IsAny<CancellationToken>()), Times.Never);
        ledgerRepo.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    // ── Read status (×1000 conversion happens exactly here) ───────────────────

    [Fact]
    public async Task GetStatus_ConvertsAipTotalFromThousandsToPesos()
    {
        var (sut, wfpExpRepo, _, ledgerRepo, _, _, _) = Build(aipTotalThousands: 250m, divisionAllocationAmount: 90000m);
        wfpExpRepo.Setup(r => r.SumTotalByAipActivityAsync(
                AipActivityId, OfficeId, FiscalYear, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(75000m);
        ledgerRepo.Setup(r => r.SumUsedAmountAsync(DivisionId, FiscalYear, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(30000m);

        WfpCeilingStatusDto status = await sut.GetStatusAsync(AipActivityId, DivisionId, FiscalYear, CancellationToken.None);

        Assert.Equal(250000m, status.AipBudget); // 250 (thousands) x 1000
        Assert.Equal(75000m, status.AipUsed);
        Assert.Equal(90000m, status.DivisionAllocation);
        Assert.Equal(60000m, status.DivisionRemaining); // 90,000 - 30,000
    }

    // ── Finalize backstop ─────────────────────────────────────────────────────

    [Fact]
    public async Task ValidateRecordForFinalize_UnknownRecord_ReturnsNull()
    {
        var (sut, _, wfpRepo, _, _, _, _) = Build();
        wfpRepo.Setup(r => r.GetByIntIdAsync(999, It.IsAny<CancellationToken>())).ReturnsAsync((WfpRecord?)null);

        string? result = await sut.ValidateRecordForFinalizeAsync(999, CancellationToken.None);

        Assert.Null(result);
    }

    [Fact]
    public async Task ValidateRecordForFinalize_ActivityOverAipBudget_ReturnsError()
    {
        var (sut, wfpExpRepo, wfpRepo, ledgerRepo, _, _, _) = Build(aipTotalThousands: 50m, divisionAllocationAmount: 1_000_000m);
        WfpRecord record = new() { Id = WfpRecordId, OfficeId = OfficeId, FiscalYear = FiscalYear, DivisionId = null };
        wfpRepo.Setup(r => r.GetByIntIdAsync(WfpRecordId, It.IsAny<CancellationToken>())).ReturnsAsync(record);
        wfpRepo.Setup(r => r.GetActivitiesByWfpIdAsync(WfpRecordId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyList<WfpActivity>)[new WfpActivity { Id = WfpActivityId, WfpId = WfpRecordId, AipActivityId = AipActivityId }]);
        wfpExpRepo.Setup(r => r.SumTotalByAipActivityAsync(
                AipActivityId, OfficeId, FiscalYear, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(60000m); // over the 50,000 budget

        string? result = await sut.ValidateRecordForFinalizeAsync(WfpRecordId, CancellationToken.None);

        Assert.NotNull(result);
    }

    [Fact]
    public async Task ValidateRecordForFinalize_WithinBothCeilings_ReturnsNull()
    {
        var (sut, wfpExpRepo, wfpRepo, ledgerRepo, _, _, _) = Build(aipTotalThousands: 100m, divisionAllocationAmount: 80000m);
        WfpRecord record = new() { Id = WfpRecordId, OfficeId = OfficeId, FiscalYear = FiscalYear, DivisionId = DivisionId };
        wfpRepo.Setup(r => r.GetByIntIdAsync(WfpRecordId, It.IsAny<CancellationToken>())).ReturnsAsync(record);
        wfpRepo.Setup(r => r.GetActivitiesByWfpIdAsync(WfpRecordId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyList<WfpActivity>)[new WfpActivity { Id = WfpActivityId, WfpId = WfpRecordId, AipActivityId = AipActivityId }]);
        wfpExpRepo.Setup(r => r.SumTotalByAipActivityAsync(
                AipActivityId, OfficeId, FiscalYear, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(50000m);
        ledgerRepo.Setup(r => r.SumUsedAmountAsync(DivisionId, FiscalYear, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(60000m);

        string? result = await sut.ValidateRecordForFinalizeAsync(WfpRecordId, CancellationToken.None);

        Assert.Null(result);
    }
}
