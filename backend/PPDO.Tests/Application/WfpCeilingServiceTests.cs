using Moq;
using PPDO.Application.DTOs.BudgetPlanning;
using PPDO.Application.Services;
using PPDO.Domain.Entities;
using PPDO.Domain.Interfaces;

namespace PPDO.Tests.Application;

/// <summary>
/// Unit tests for <see cref="WfpCeilingService"/> (RAL-122; fund-scoped v1.4.3 — RAL-154).
/// Covers the original 6 Acceptance Criteria scenarios verbatim (all implicitly against
/// General Fund, matching this suite's pre-v1.4.3 single-fund behavior since expenditures
/// default their funding source to null → General Fund), plus the new fund-scoping behavior:
/// the AIP check stays aggregate across funds while the division-allocation check is
/// independent per fund, a null expenditure fund resolves to General Fund, and the ledger
/// posts one row per distinct funding source used by a record.
/// </summary>
public sealed class WfpCeilingServiceTests
{
    private const int WfpActivityId = 900;
    private const int WfpRecordId   = 1;
    private const int DivisionId    = 5;
    private const int OfficeId      = 3;
    private const int FiscalYear    = 2027;
    private const int AipActivityId = 10;
    private const int GfFundId      = 1;
    private const int GadFundId     = 2;

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

    private static FundingSource MakeFundingSource(int id, string code, string name) => new()
    {
        Id = id, Code = code, Name = name, IsActive = true,
        CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow,
    };

    private static (
        WfpCeilingService sut,
        Mock<IWfpExpenditureRepository>      wfpExpRepo,
        Mock<IWfpRepository>                 wfpRepo,
        Mock<IWfpAllocationLedgerRepository> ledgerRepo,
        Mock<IAipRepository>                 aipRepo,
        Mock<IAllocationService>             allocation,
        Mock<IRepository<Division>>          divisionRepo,
        Mock<IRepository<FundingSource>>     fundingSourceRepo)
        Build(
            WfpExpenditureContext? context = null,
            decimal aipTotalThousands = 1000m,
            decimal divisionAllocationAmount = 1000000m,
            List<Division>? divisions = null,
            List<FundingSource>? fundingSources = null)
    {
        Mock<IWfpExpenditureRepository>      wfpExpRepo   = new();
        Mock<IWfpRepository>                 wfpRepo      = new();
        Mock<IWfpAllocationLedgerRepository> ledgerRepo   = new();
        Mock<IAipRepository>                 aipRepo      = new();
        Mock<IAllocationService>             allocation   = new();
        Mock<IRepository<Division>>          divisionRepo = new();
        Mock<IRepository<FundingSource>>     fundingSourceRepo = new();

        context ??= new WfpExpenditureContext(WfpRecordId, DivisionId, OfficeId, FiscalYear, AipActivityId);
        wfpExpRepo.Setup(r => r.GetActivityContextAsync(WfpActivityId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(context);

        aipRepo.Setup(r => r.GetActivityByIdAsync(AipActivityId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(MakeAipActivity(aipTotalThousands));

        divisionRepo.Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(divisions ?? [MakeDivision()]);

        List<FundingSource> fundList = fundingSources ?? [MakeFundingSource(GfFundId, "GF", "General Fund")];
        fundingSourceRepo.Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>())).ReturnsAsync(fundList);

        allocation.Setup(a => a.GetGeneralFundIdAsync(It.IsAny<CancellationToken>())).ReturnsAsync(GfFundId);

        // Default: any fund queried gets the same divisionAllocationAmount, tagged with whatever
        // fund was asked — keeps every pre-v1.4.3 test (which never inspects the fund fields)
        // working unchanged, since they only ever resolve to GF anyway (null fund → GF fallback).
        allocation.Setup(a => a.GetAllocationsAsync(OfficeId, FiscalYear, It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((int _, int _, int fundId, CancellationToken _) =>
                (IReadOnlyList<DivisionAllocationDto>)
                    [new DivisionAllocationDto(1, DivisionId, "Planning Division", FiscalYear,
                        fundId, "GF", "General Fund", divisionAllocationAmount)]);

        ledgerRepo.Setup(r => r.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);
        ledgerRepo.Setup(r => r.GetFundingSourceIdsForRecordAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyList<int>)[]);
        wfpExpRepo.Setup(r => r.GetDistinctFundingSourceIdsByWfpRecordAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyList<int?>)[null]);

        WfpCeilingService sut = new(
            wfpExpRepo.Object, wfpRepo.Object, ledgerRepo.Object, aipRepo.Object,
            allocation.Object, divisionRepo.Object, fundingSourceRepo.Object);

        return (sut, wfpExpRepo, wfpRepo, ledgerRepo, aipRepo, allocation, divisionRepo, fundingSourceRepo);
    }

    // ── Acceptance criterion 1: under both ceilings → succeeds ────────────────

    [Fact]
    public async Task ValidateExpenditureSave_UnderBothCeilings_ReturnsNull()
    {
        // AIP budget = 100,000 pesos (100 thousand). Others already used 50,000.
        var (sut, wfpExpRepo, _, ledgerRepo, _, _, _, _) = Build(aipTotalThousands: 100m, divisionAllocationAmount: 80000m);
        wfpExpRepo.Setup(r => r.SumTotalByAipActivityAsync(
                AipActivityId, OfficeId, FiscalYear, It.IsAny<int?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(50000m);
        wfpExpRepo.Setup(r => r.SumTotalByWfpRecordAsync(
                WfpRecordId, GfFundId, GfFundId, It.IsAny<int?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(10000m);
        ledgerRepo.Setup(r => r.SumUsedAmountAsync(DivisionId, FiscalYear, GfFundId, WfpRecordId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(30000m); // other records' usage

        // New expenditure total = 20,000: AIP would-be = 70,000 ≤ 100,000; division would-be = 30,000+30,000 = 60,000 ≤ 80,000.
        string? result = await sut.ValidateExpenditureSaveAsync(WfpActivityId, 20000m, null, null, CancellationToken.None);

        Assert.Null(result);
    }

    // ── Acceptance criterion 2: over AIP only → rejected ──────────────────────

    [Fact]
    public async Task ValidateExpenditureSave_OverAipBudgetOnly_ReturnsError()
    {
        var (sut, wfpExpRepo, _, ledgerRepo, _, _, _, _) = Build(aipTotalThousands: 100m, divisionAllocationAmount: 1_000_000m);
        wfpExpRepo.Setup(r => r.SumTotalByAipActivityAsync(
                AipActivityId, OfficeId, FiscalYear, It.IsAny<int?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(90000m);
        wfpExpRepo.Setup(r => r.SumTotalByWfpRecordAsync(
                WfpRecordId, GfFundId, GfFundId, It.IsAny<int?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(0m);
        ledgerRepo.Setup(r => r.SumUsedAmountAsync(DivisionId, FiscalYear, GfFundId, WfpRecordId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(0m);

        // AIP would-be = 90,000 + 20,000 = 110,000 > 100,000 budget. Division allocation is huge, not the cause.
        string? result = await sut.ValidateExpenditureSaveAsync(WfpActivityId, 20000m, null, null, CancellationToken.None);

        Assert.NotNull(result);
        Assert.Contains("AIP", result, StringComparison.OrdinalIgnoreCase);
    }

    // ── Acceptance criterion 3: over allocation only → rejected ───────────────

    [Fact]
    public async Task ValidateExpenditureSave_OverDivisionAllocationOnly_ReturnsError()
    {
        var (sut, wfpExpRepo, _, ledgerRepo, _, _, _, _) = Build(aipTotalThousands: 100_000m, divisionAllocationAmount: 50000m);
        wfpExpRepo.Setup(r => r.SumTotalByAipActivityAsync(
                AipActivityId, OfficeId, FiscalYear, It.IsAny<int?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(0m); // AIP budget is enormous (100M pesos) — never the cause here
        wfpExpRepo.Setup(r => r.SumTotalByWfpRecordAsync(
                WfpRecordId, GfFundId, GfFundId, It.IsAny<int?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(5000m);
        ledgerRepo.Setup(r => r.SumUsedAmountAsync(DivisionId, FiscalYear, GfFundId, WfpRecordId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(40000m); // other records already used 40,000 of the 50,000 allocation

        // Division would-be = 40,000 + (5,000 + 10,000) = 55,000 > 50,000 allocation.
        string? result = await sut.ValidateExpenditureSaveAsync(WfpActivityId, 10000m, null, null, CancellationToken.None);

        Assert.NotNull(result);
        Assert.Contains("division", result, StringComparison.OrdinalIgnoreCase);
    }

    // ── Acceptance criterion 4: over both → rejected ──────────────────────────

    [Fact]
    public async Task ValidateExpenditureSave_OverBothCeilings_ReturnsError()
    {
        var (sut, wfpExpRepo, _, ledgerRepo, _, _, _, _) = Build(aipTotalThousands: 60m, divisionAllocationAmount: 50000m);
        wfpExpRepo.Setup(r => r.SumTotalByAipActivityAsync(
                AipActivityId, OfficeId, FiscalYear, It.IsAny<int?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(55000m); // AIP budget = 60,000; would-be = 55,000+10,000 = 65,000 > 60,000
        wfpExpRepo.Setup(r => r.SumTotalByWfpRecordAsync(
                WfpRecordId, GfFundId, GfFundId, It.IsAny<int?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(5000m);
        ledgerRepo.Setup(r => r.SumUsedAmountAsync(DivisionId, FiscalYear, GfFundId, WfpRecordId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(48000m); // would-be division = 48,000+15,000 = 63,000 > 50,000

        string? result = await sut.ValidateExpenditureSaveAsync(WfpActivityId, 10000m, null, null, CancellationToken.None);

        Assert.NotNull(result); // rejected — which specific message wins (AIP is checked first) isn't the point here
    }

    // ── Acceptance criterion 5: exactly at boundary → not a false rejection ──

    [Fact]
    public async Task ValidateExpenditureSave_ExactlyAtBothBoundaries_ReturnsNull()
    {
        var (sut, wfpExpRepo, _, ledgerRepo, _, _, _, _) = Build(aipTotalThousands: 100m, divisionAllocationAmount: 80000m);
        wfpExpRepo.Setup(r => r.SumTotalByAipActivityAsync(
                AipActivityId, OfficeId, FiscalYear, It.IsAny<int?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(80000m); // would-be AIP = 80,000 + 20,000 = 100,000 == budget exactly
        wfpExpRepo.Setup(r => r.SumTotalByWfpRecordAsync(
                WfpRecordId, GfFundId, GfFundId, It.IsAny<int?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(20000m);
        ledgerRepo.Setup(r => r.SumUsedAmountAsync(DivisionId, FiscalYear, GfFundId, WfpRecordId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(40000m); // would-be division = 40,000 + (20,000+20,000) = 80,000 == allocation exactly

        string? result = await sut.ValidateExpenditureSaveAsync(WfpActivityId, 20000m, null, null, CancellationToken.None);

        Assert.Null(result);
    }

    // ── Excludes the expenditure's own OLD total when updating ────────────────

    [Fact]
    public async Task ValidateExpenditureSave_WhenUpdating_ExcludesTheExpendituresOwnOldTotal()
    {
        var (sut, wfpExpRepo, _, ledgerRepo, _, _, _, _) = Build(aipTotalThousands: 100m, divisionAllocationAmount: 1_000_000m);

        // Verify the exclude id is actually passed through to both sum queries.
        wfpExpRepo.Setup(r => r.SumTotalByAipActivityAsync(
                AipActivityId, OfficeId, FiscalYear, 77, It.IsAny<CancellationToken>()))
            .ReturnsAsync(10000m);
        wfpExpRepo.Setup(r => r.SumTotalByWfpRecordAsync(WfpRecordId, GfFundId, GfFundId, 77, It.IsAny<CancellationToken>()))
            .ReturnsAsync(10000m);
        ledgerRepo.Setup(r => r.SumUsedAmountAsync(DivisionId, FiscalYear, GfFundId, WfpRecordId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(0m);

        string? result = await sut.ValidateExpenditureSaveAsync(
            WfpActivityId, 15000m, fundingSourceId: null, excludeExpenditureId: 77, CancellationToken.None);

        Assert.Null(result);
        wfpExpRepo.Verify(r => r.SumTotalByAipActivityAsync(AipActivityId, OfficeId, FiscalYear, 77, It.IsAny<CancellationToken>()), Times.Once);
        wfpExpRepo.Verify(r => r.SumTotalByWfpRecordAsync(WfpRecordId, GfFundId, GfFundId, 77, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ValidateExpenditureSave_NoDivisionOnRecord_SkipsAllocationCheck()
    {
        // A legacy/no-division WFP record — only the AIP check applies.
        WfpExpenditureContext context = new(WfpRecordId, DivisionId: null, OfficeId, FiscalYear, AipActivityId);
        var (sut, wfpExpRepo, _, ledgerRepo, _, allocation, _, _) = Build(context, aipTotalThousands: 100m);
        wfpExpRepo.Setup(r => r.SumTotalByAipActivityAsync(
                AipActivityId, OfficeId, FiscalYear, It.IsAny<int?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(10000m);

        string? result = await sut.ValidateExpenditureSaveAsync(WfpActivityId, 5000m, null, null, CancellationToken.None);

        Assert.Null(result);
        ledgerRepo.Verify(r => r.SumUsedAmountAsync(
            It.IsAny<int>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<int?>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    // ── Fund-scoping (v1.4.3 — RAL-154) ───────────────────────────────────────

    [Fact]
    public async Task ValidateExpenditureSave_GadExpenditure_DebitsGadAllocation_NotGf()
    {
        // GAD allocation is tiny (10,000); GF allocation is huge (1,000,000). A GAD-funded
        // expenditure of 15,000 must be checked against GAD's allocation and rejected — even
        // though it would be trivially within GF's — proving the check uses the expenditure's
        // OWN fund, not always General Fund.
        var (sut, wfpExpRepo, _, ledgerRepo, _, allocation, _, _) = Build(aipTotalThousands: 100_000m);
        allocation.Setup(a => a.GetAllocationsAsync(OfficeId, FiscalYear, GadFundId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyList<DivisionAllocationDto>)
                [new DivisionAllocationDto(2, DivisionId, "Planning Division", FiscalYear, GadFundId, "GAD", "5% GAD Fund", 10_000m)]);
        wfpExpRepo.Setup(r => r.SumTotalByAipActivityAsync(
                AipActivityId, OfficeId, FiscalYear, It.IsAny<int?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(0m);
        wfpExpRepo.Setup(r => r.SumTotalByWfpRecordAsync(
                WfpRecordId, GadFundId, GfFundId, It.IsAny<int?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(0m);
        ledgerRepo.Setup(r => r.SumUsedAmountAsync(DivisionId, FiscalYear, GadFundId, WfpRecordId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(0m);

        string? result = await sut.ValidateExpenditureSaveAsync(WfpActivityId, 15000m, GadFundId, null, CancellationToken.None);

        Assert.NotNull(result);
        Assert.Contains("division", result, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ValidateExpenditureSave_GfAndGad_AreIndependentForAllocationCheck()
    {
        // Same division, same activity: a GF expenditure and a GAD expenditure are each
        // checked against their OWN fund's allocation, independently of the other fund's usage.
        var (sut, wfpExpRepo, _, ledgerRepo, _, allocation, _, _) = Build(aipTotalThousands: 1_000_000m);
        allocation.Setup(a => a.GetAllocationsAsync(OfficeId, FiscalYear, GfFundId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyList<DivisionAllocationDto>)
                [new DivisionAllocationDto(1, DivisionId, "Planning Division", FiscalYear, GfFundId, "GF", "General Fund", 100_000m)]);
        allocation.Setup(a => a.GetAllocationsAsync(OfficeId, FiscalYear, GadFundId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyList<DivisionAllocationDto>)
                [new DivisionAllocationDto(2, DivisionId, "Planning Division", FiscalYear, GadFundId, "GAD", "5% GAD Fund", 20_000m)]);
        wfpExpRepo.Setup(r => r.SumTotalByAipActivityAsync(
                AipActivityId, OfficeId, FiscalYear, It.IsAny<int?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(0m);
        // GF already fully used (100,000 of 100,000) — a new GF expenditure would be rejected.
        wfpExpRepo.Setup(r => r.SumTotalByWfpRecordAsync(
                WfpRecordId, GfFundId, GfFundId, It.IsAny<int?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(100_000m);
        ledgerRepo.Setup(r => r.SumUsedAmountAsync(DivisionId, FiscalYear, GfFundId, WfpRecordId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(0m);
        // GAD is untouched (0 of 20,000) — a new GAD expenditure of 15,000 fits comfortably.
        wfpExpRepo.Setup(r => r.SumTotalByWfpRecordAsync(
                WfpRecordId, GadFundId, GfFundId, It.IsAny<int?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(0m);
        ledgerRepo.Setup(r => r.SumUsedAmountAsync(DivisionId, FiscalYear, GadFundId, WfpRecordId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(0m);

        string? gfResult  = await sut.ValidateExpenditureSaveAsync(WfpActivityId, 5000m, GfFundId, null, CancellationToken.None);
        string? gadResult = await sut.ValidateExpenditureSaveAsync(WfpActivityId, 15000m, GadFundId, null, CancellationToken.None);

        Assert.NotNull(gfResult);   // GF allocation exhausted
        Assert.Null(gadResult);    // GAD allocation independent, still has room
    }

    [Fact]
    public async Task ValidateExpenditureSave_GfAndGad_SummedTogetherForAipCheck()
    {
        // The AIP-budget check (§2 D3) stays aggregate across ALL funds — a GF expenditure and
        // a GAD expenditure on the same activity both count against the same AIP total.
        var (sut, wfpExpRepo, _, ledgerRepo, _, allocation, _, _) = Build(aipTotalThousands: 100m, divisionAllocationAmount: 1_000_000m);
        // AIP budget = 100,000. Other expenditures across ALL funds already used 90,000.
        wfpExpRepo.Setup(r => r.SumTotalByAipActivityAsync(
                AipActivityId, OfficeId, FiscalYear, It.IsAny<int?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(90_000m);
        wfpExpRepo.Setup(r => r.SumTotalByWfpRecordAsync(
                WfpRecordId, GadFundId, GfFundId, It.IsAny<int?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(0m);
        ledgerRepo.Setup(r => r.SumUsedAmountAsync(DivisionId, FiscalYear, GadFundId, WfpRecordId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(0m);

        // A NEW GAD expenditure of 15,000 would bring the AIP total to 105,000 > 100,000 —
        // rejected on the AIP check even though it's a different fund than what was already used.
        string? result = await sut.ValidateExpenditureSaveAsync(WfpActivityId, 15000m, GadFundId, null, CancellationToken.None);

        Assert.NotNull(result);
        Assert.Contains("AIP", result, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ValidateExpenditureSave_NullFundingSource_TreatedAsGeneralFund()
    {
        var (sut, wfpExpRepo, _, ledgerRepo, _, allocation, _, _) = Build(aipTotalThousands: 100_000m, divisionAllocationAmount: 10_000m);
        wfpExpRepo.Setup(r => r.SumTotalByAipActivityAsync(
                AipActivityId, OfficeId, FiscalYear, It.IsAny<int?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(0m);
        wfpExpRepo.Setup(r => r.SumTotalByWfpRecordAsync(
                WfpRecordId, GfFundId, GfFundId, It.IsAny<int?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(0m);
        ledgerRepo.Setup(r => r.SumUsedAmountAsync(DivisionId, FiscalYear, GfFundId, WfpRecordId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(0m);

        // fundingSourceId: null — must resolve to GF and check against GF's 10,000 allocation.
        string? result = await sut.ValidateExpenditureSaveAsync(WfpActivityId, 15000m, null, null, CancellationToken.None);

        Assert.NotNull(result);
        Assert.Contains("division", result, StringComparison.OrdinalIgnoreCase);
        allocation.Verify(a => a.GetAllocationsAsync(OfficeId, FiscalYear, GfFundId, It.IsAny<CancellationToken>()), Times.AtLeastOnce);
    }

    // ── Acceptance criterion 6: ledger row upserts as totals change ──────────

    [Fact]
    public async Task UpsertLedgerForActivity_NoExistingRow_CreatesOne()
    {
        var (sut, wfpExpRepo, _, ledgerRepo, _, _, _, _) = Build(divisionAllocationAmount: 80000m);
        wfpExpRepo.Setup(r => r.SumTotalByWfpRecordAsync(WfpRecordId, GfFundId, GfFundId, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(42000m);
        ledgerRepo.Setup(r => r.FindAsync(DivisionId, FiscalYear, GfFundId, WfpRecordId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((WfpDivisionAllocationLedger?)null);

        WfpDivisionAllocationLedger? added = null;
        ledgerRepo.Setup(r => r.AddAsync(It.IsAny<WfpDivisionAllocationLedger>(), It.IsAny<CancellationToken>()))
            .Callback<WfpDivisionAllocationLedger, CancellationToken>((l, _) => added = l)
            .Returns(Task.CompletedTask);

        await sut.UpsertLedgerForActivityAsync(WfpActivityId, CancellationToken.None);

        Assert.NotNull(added);
        Assert.Equal(DivisionId, added!.DivisionId);
        Assert.Equal(FiscalYear, added.FiscalYear);
        Assert.Equal(GfFundId, added.FundingSourceId);
        Assert.Equal(WfpRecordId, added.WfpRecordId);
        Assert.Equal(42000m, added.UsedAmount);
        Assert.Equal(80000m, added.AllocatedAmountSnapshot);
        ledgerRepo.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task UpsertLedgerForActivity_ExistingRow_UpdatesUsedAmount()
    {
        var (sut, wfpExpRepo, _, ledgerRepo, _, _, _, _) = Build(divisionAllocationAmount: 80000m);
        wfpExpRepo.Setup(r => r.SumTotalByWfpRecordAsync(WfpRecordId, GfFundId, GfFundId, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(60000m); // totals changed since the row was created

        WfpDivisionAllocationLedger existing = new()
        {
            Id = 1, DivisionId = DivisionId, FiscalYear = FiscalYear, FundingSourceId = GfFundId, WfpRecordId = WfpRecordId,
            AllocatedAmountSnapshot = 80000m, UsedAmount = 42000m, UpdatedAt = DateTime.UtcNow.AddDays(-1),
        };
        ledgerRepo.Setup(r => r.FindAsync(DivisionId, FiscalYear, GfFundId, WfpRecordId, It.IsAny<CancellationToken>()))
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
        var (sut, _, _, ledgerRepo, _, _, _, _) = Build(context);

        await sut.UpsertLedgerForActivityAsync(WfpActivityId, CancellationToken.None);

        ledgerRepo.Verify(r => r.AddAsync(It.IsAny<WfpDivisionAllocationLedger>(), It.IsAny<CancellationToken>()), Times.Never);
        ledgerRepo.Verify(r => r.UpdateAsync(It.IsAny<WfpDivisionAllocationLedger>(), It.IsAny<CancellationToken>()), Times.Never);
        ledgerRepo.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task UpsertLedgerForActivity_MultipleFunds_PostsOneRowPerFund()
    {
        // The record's expenditures use both GF and GAD — one ledger row per fund is expected,
        // each with that fund's own used amount and allocation snapshot.
        var (sut, wfpExpRepo, _, ledgerRepo, _, allocation, _, _) = Build();
        wfpExpRepo.Setup(r => r.GetDistinctFundingSourceIdsByWfpRecordAsync(WfpRecordId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyList<int?>)[GfFundId, GadFundId]);

        allocation.Setup(a => a.GetAllocationsAsync(OfficeId, FiscalYear, GfFundId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyList<DivisionAllocationDto>)
                [new DivisionAllocationDto(1, DivisionId, "Planning Division", FiscalYear, GfFundId, "GF", "General Fund", 100_000m)]);
        allocation.Setup(a => a.GetAllocationsAsync(OfficeId, FiscalYear, GadFundId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyList<DivisionAllocationDto>)
                [new DivisionAllocationDto(2, DivisionId, "Planning Division", FiscalYear, GadFundId, "GAD", "5% GAD Fund", 20_000m)]);

        wfpExpRepo.Setup(r => r.SumTotalByWfpRecordAsync(WfpRecordId, GfFundId, GfFundId, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(30_000m);
        wfpExpRepo.Setup(r => r.SumTotalByWfpRecordAsync(WfpRecordId, GadFundId, GfFundId, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(5_000m);

        ledgerRepo.Setup(r => r.FindAsync(DivisionId, FiscalYear, It.IsAny<int>(), WfpRecordId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((WfpDivisionAllocationLedger?)null);

        List<WfpDivisionAllocationLedger> added = [];
        ledgerRepo.Setup(r => r.AddAsync(It.IsAny<WfpDivisionAllocationLedger>(), It.IsAny<CancellationToken>()))
            .Callback<WfpDivisionAllocationLedger, CancellationToken>((l, _) => added.Add(l))
            .Returns(Task.CompletedTask);

        await sut.UpsertLedgerForActivityAsync(WfpActivityId, CancellationToken.None);

        Assert.Equal(2, added.Count);
        WfpDivisionAllocationLedger gfRow  = added.Single(l => l.FundingSourceId == GfFundId);
        WfpDivisionAllocationLedger gadRow = added.Single(l => l.FundingSourceId == GadFundId);
        Assert.Equal(30_000m, gfRow.UsedAmount);
        Assert.Equal(100_000m, gfRow.AllocatedAmountSnapshot);
        Assert.Equal(5_000m, gadRow.UsedAmount);
        Assert.Equal(20_000m, gadRow.AllocatedAmountSnapshot);
    }

    [Fact]
    public async Task UpsertLedgerForActivity_FundNoLongerUsed_ZeroesOutItsStaleRow()
    {
        // GAD was previously used (existing ledger row) but the record's expenditures no longer
        // reference it (e.g. the only GAD expenditure was deleted/reassigned) — its row must be
        // recomputed to zero, not left stale at its old positive amount.
        var (sut, wfpExpRepo, _, ledgerRepo, _, allocation, _, _) = Build();
        wfpExpRepo.Setup(r => r.GetDistinctFundingSourceIdsByWfpRecordAsync(WfpRecordId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyList<int?>)[GfFundId]); // GAD no longer present
        ledgerRepo.Setup(r => r.GetFundingSourceIdsForRecordAsync(WfpRecordId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyList<int>)[GfFundId, GadFundId]); // but still tracked from before

        allocation.Setup(a => a.GetAllocationsAsync(OfficeId, FiscalYear, GfFundId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyList<DivisionAllocationDto>)
                [new DivisionAllocationDto(1, DivisionId, "Planning Division", FiscalYear, GfFundId, "GF", "General Fund", 100_000m)]);
        allocation.Setup(a => a.GetAllocationsAsync(OfficeId, FiscalYear, GadFundId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyList<DivisionAllocationDto>)
                [new DivisionAllocationDto(2, DivisionId, "Planning Division", FiscalYear, GadFundId, "GAD", "5% GAD Fund", 20_000m)]);

        wfpExpRepo.Setup(r => r.SumTotalByWfpRecordAsync(WfpRecordId, GfFundId, GfFundId, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(30_000m);
        wfpExpRepo.Setup(r => r.SumTotalByWfpRecordAsync(WfpRecordId, GadFundId, GfFundId, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(0m); // no more GAD expenditures

        WfpDivisionAllocationLedger gadRow = new()
        {
            Id = 9, DivisionId = DivisionId, FiscalYear = FiscalYear, FundingSourceId = GadFundId, WfpRecordId = WfpRecordId,
            AllocatedAmountSnapshot = 20_000m, UsedAmount = 15_000m, UpdatedAt = DateTime.UtcNow.AddDays(-1),
        };
        ledgerRepo.Setup(r => r.FindAsync(DivisionId, FiscalYear, GfFundId, WfpRecordId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((WfpDivisionAllocationLedger?)null);
        ledgerRepo.Setup(r => r.FindAsync(DivisionId, FiscalYear, GadFundId, WfpRecordId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(gadRow);

        await sut.UpsertLedgerForActivityAsync(WfpActivityId, CancellationToken.None);

        Assert.Equal(0m, gadRow.UsedAmount);
        ledgerRepo.Verify(r => r.UpdateAsync(gadRow, It.IsAny<CancellationToken>()), Times.Once);
    }

    // ── Read status (×1000 conversion happens exactly here) ───────────────────

    [Fact]
    public async Task GetStatus_ConvertsAipTotalFromThousandsToPesos()
    {
        var (sut, wfpExpRepo, _, ledgerRepo, _, _, _, _) = Build(aipTotalThousands: 250m, divisionAllocationAmount: 90000m);
        wfpExpRepo.Setup(r => r.SumTotalByAipActivityAsync(
                AipActivityId, OfficeId, FiscalYear, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(75000m);
        ledgerRepo.Setup(r => r.SumUsedAmountAsync(DivisionId, FiscalYear, GfFundId, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(30000m);

        WfpCeilingStatusDto status = await sut.GetStatusAsync(AipActivityId, DivisionId, FiscalYear, CancellationToken.None);

        Assert.Equal(250000m, status.AipBudget); // 250 (thousands) x 1000
        Assert.Equal(75000m, status.AipUsed);
        Assert.Equal(90000m, status.DivisionAllocation);  // General Fund's, at the top level
        Assert.Equal(60000m, status.DivisionRemaining);   // 90,000 - 30,000
    }

    [Fact]
    public async Task GetStatus_IncludesOneFundsEntryPerActiveFundingSource()
    {
        var (sut, wfpExpRepo, _, ledgerRepo, _, allocation, _, _) = Build(
            aipTotalThousands: 100m,
            fundingSources: [MakeFundingSource(GfFundId, "GF", "General Fund"), MakeFundingSource(GadFundId, "GAD", "5% GAD Fund")]);
        wfpExpRepo.Setup(r => r.SumTotalByAipActivityAsync(
                AipActivityId, OfficeId, FiscalYear, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(0m);
        allocation.Setup(a => a.GetAllocationsAsync(OfficeId, FiscalYear, GfFundId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyList<DivisionAllocationDto>)
                [new DivisionAllocationDto(1, DivisionId, "Planning Division", FiscalYear, GfFundId, "GF", "General Fund", 100_000m)]);
        allocation.Setup(a => a.GetAllocationsAsync(OfficeId, FiscalYear, GadFundId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyList<DivisionAllocationDto>)
                [new DivisionAllocationDto(2, DivisionId, "Planning Division", FiscalYear, GadFundId, "GAD", "5% GAD Fund", 20_000m)]);
        ledgerRepo.Setup(r => r.SumUsedAmountAsync(DivisionId, FiscalYear, GfFundId, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(10_000m);
        ledgerRepo.Setup(r => r.SumUsedAmountAsync(DivisionId, FiscalYear, GadFundId, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(5_000m);

        WfpCeilingStatusDto status = await sut.GetStatusAsync(AipActivityId, DivisionId, FiscalYear, CancellationToken.None);

        Assert.Equal(2, status.Funds.Count);
        WfpFundCeilingDto gf  = status.Funds.Single(f => f.FundingSourceId == GfFundId);
        WfpFundCeilingDto gad = status.Funds.Single(f => f.FundingSourceId == GadFundId);
        Assert.Equal(100_000m, gf.Allocation);
        Assert.Equal(90_000m, gf.Remaining);
        Assert.Equal(20_000m, gad.Allocation);
        Assert.Equal(15_000m, gad.Remaining);
    }

    // ── Finalize backstop ─────────────────────────────────────────────────────

    [Fact]
    public async Task ValidateRecordForFinalize_UnknownRecord_ReturnsNull()
    {
        var (sut, _, wfpRepo, _, _, _, _, _) = Build();
        wfpRepo.Setup(r => r.GetByIntIdAsync(999, It.IsAny<CancellationToken>())).ReturnsAsync((WfpRecord?)null);

        string? result = await sut.ValidateRecordForFinalizeAsync(999, CancellationToken.None);

        Assert.Null(result);
    }

    [Fact]
    public async Task ValidateRecordForFinalize_ActivityOverAipBudget_ReturnsError()
    {
        var (sut, wfpExpRepo, wfpRepo, ledgerRepo, _, _, _, _) = Build(aipTotalThousands: 50m, divisionAllocationAmount: 1_000_000m);
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
        var (sut, wfpExpRepo, wfpRepo, ledgerRepo, _, _, _, _) = Build(aipTotalThousands: 100m, divisionAllocationAmount: 80000m);
        WfpRecord record = new() { Id = WfpRecordId, OfficeId = OfficeId, FiscalYear = FiscalYear, DivisionId = DivisionId };
        wfpRepo.Setup(r => r.GetByIntIdAsync(WfpRecordId, It.IsAny<CancellationToken>())).ReturnsAsync(record);
        wfpRepo.Setup(r => r.GetActivitiesByWfpIdAsync(WfpRecordId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyList<WfpActivity>)[new WfpActivity { Id = WfpActivityId, WfpId = WfpRecordId, AipActivityId = AipActivityId }]);
        wfpExpRepo.Setup(r => r.SumTotalByAipActivityAsync(
                AipActivityId, OfficeId, FiscalYear, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(50000m);
        ledgerRepo.Setup(r => r.GetFundingSourceIdsForRecordAsync(WfpRecordId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyList<int>)[GfFundId]);
        ledgerRepo.Setup(r => r.SumUsedAmountAsync(DivisionId, FiscalYear, GfFundId, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(60000m);

        string? result = await sut.ValidateRecordForFinalizeAsync(WfpRecordId, CancellationToken.None);

        Assert.Null(result);
    }

    [Fact]
    public async Task ValidateRecordForFinalize_OneOfMultipleFundsOverAllocation_ReturnsError()
    {
        var (sut, wfpExpRepo, wfpRepo, ledgerRepo, _, allocation, _, _) = Build(aipTotalThousands: 1_000_000m);
        WfpRecord record = new() { Id = WfpRecordId, OfficeId = OfficeId, FiscalYear = FiscalYear, DivisionId = DivisionId };
        wfpRepo.Setup(r => r.GetByIntIdAsync(WfpRecordId, It.IsAny<CancellationToken>())).ReturnsAsync(record);
        wfpRepo.Setup(r => r.GetActivitiesByWfpIdAsync(WfpRecordId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyList<WfpActivity>)[new WfpActivity { Id = WfpActivityId, WfpId = WfpRecordId, AipActivityId = AipActivityId }]);
        wfpExpRepo.Setup(r => r.SumTotalByAipActivityAsync(
                AipActivityId, OfficeId, FiscalYear, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(0m);

        ledgerRepo.Setup(r => r.GetFundingSourceIdsForRecordAsync(WfpRecordId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyList<int>)[GfFundId, GadFundId]);
        allocation.Setup(a => a.GetAllocationsAsync(OfficeId, FiscalYear, GfFundId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyList<DivisionAllocationDto>)
                [new DivisionAllocationDto(1, DivisionId, "Planning Division", FiscalYear, GfFundId, "GF", "General Fund", 100_000m)]);
        allocation.Setup(a => a.GetAllocationsAsync(OfficeId, FiscalYear, GadFundId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyList<DivisionAllocationDto>)
                [new DivisionAllocationDto(2, DivisionId, "Planning Division", FiscalYear, GadFundId, "GAD", "5% GAD Fund", 10_000m)]);
        ledgerRepo.Setup(r => r.SumUsedAmountAsync(DivisionId, FiscalYear, GfFundId, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(50_000m);   // within GF's 100,000
        ledgerRepo.Setup(r => r.SumUsedAmountAsync(DivisionId, FiscalYear, GadFundId, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(15_000m);  // over GAD's 10,000

        string? result = await sut.ValidateRecordForFinalizeAsync(WfpRecordId, CancellationToken.None);

        Assert.NotNull(result);
    }
}
