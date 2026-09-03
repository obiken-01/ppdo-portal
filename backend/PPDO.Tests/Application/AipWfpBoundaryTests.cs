using Moq;
using PPDO.Application.DTOs.BudgetPlanning;
using PPDO.Application.Services;
using PPDO.Domain.Entities;
using PPDO.Domain.Interfaces;

namespace PPDO.Tests.Application;

/// <summary>
/// The AIP↔WFP numeric boundary (V18-36 / PPDO-35 — spec §11).
///
/// <para>
/// An AIP activity's budget and a WFP expenditure's total meet in exactly one place: the ceiling
/// check in <see cref="WfpCeilingService"/>. That comparison has silently carried a ×1000
/// thousands-to-pesos conversion for months at three separate sites, and nothing asserted the
/// resulting peso figure — <see cref="WfpCeilingServiceTests"/> pins the conversion on the read
/// path only, and its reject cases assert that AN error came back, not which number produced it.
/// A wrong factor is permissive and silent: the ceiling stops tripping, and the first symptom is
/// a WFP over its AIP activity, found by someone adding up a printed report by hand.
/// </para>
///
/// <para>
/// Every case below asserts a literal peso amount, never <c>activity.Total * factor</c> — the
/// latter restates the implementation and passes whatever the factor happens to be. The accept
/// and reject cases are deliberately one centavo apart so they bracket the boundary from both
/// sides: a factor that is too small makes the accept case reject, and a factor that is too
/// large makes the reject case accept. Neither direction can pass unnoticed.
/// </para>
///
/// <para>
/// ⚠️ V18-35 (PPDO-34) migrates <c>aip_activities.total</c> from thousands to pesos and deletes
/// the ×1000 sites. That changes exactly one line here — <see cref="StoredAipActivityTotal"/>.
/// Every assertion stays as written, because ₱250,000 of AIP budget is ₱250,000 of WFP ceiling
/// on both sides of the migration. If the migration gets the conversion wrong, this file turns
/// red; if it is right, this file is untouched apart from that one constant.
/// </para>
/// </summary>
public sealed class AipWfpBoundaryTests
{
    // ── The one fixture V18-35 touches ────────────────────────────────────────

    /// <summary>
    /// What <c>aip_activities.total</c> stores for an activity budgeted at ₱250,000.
    /// Thousands today; V18-35 rewrites this single line to <c>250_000m</c>.
    /// </summary>
    private const decimal StoredAipActivityTotal = 250m;

    /// <summary>
    /// What that activity is worth as a WFP ceiling: ₱250,000. Not ₱250. Not ₱250,000,000.
    /// True before and after V18-35 — this constant never changes.
    /// </summary>
    private const decimal AipBudgetPesos = 250_000m;

    /// <summary>The smallest amount that can put a save over the boundary.</summary>
    private const decimal OneCentavo = 0.01m;

    private const int WfpActivityId = 900;
    private const int WfpRecordId   = 1;
    private const int DivisionId    = 5;
    private const int OfficeId      = 3;
    private const int FiscalYear    = 2027;
    private const int AipActivityId = 10;
    private const int GfFundId      = 1;

    private const string ActivityRefCode = "1000-000-1-01-011-001-001-001";

    private static (
        WfpCeilingService               sut,
        Mock<IWfpExpenditureRepository> wfpExpRepo,
        Mock<IWfpRepository>            wfpRepo)
        Build(int? divisionOnRecord = null)
    {
        Mock<IWfpExpenditureRepository>      wfpExpRepo        = new();
        Mock<IWfpRepository>                 wfpRepo           = new();
        Mock<IWfpAllocationLedgerRepository> ledgerRepo        = new();
        Mock<IAipRepository>                 aipRepo           = new();
        Mock<IAllocationService>             allocation        = new();
        Mock<IRepository<Division>>          divisionRepo      = new();
        Mock<IRepository<FundingSource>>     fundingSourceRepo = new();

        AipActivity activity = new()
        {
            Id        = AipActivityId,
            ProjectId = 1,
            RefCode   = ActivityRefCode,
            Name      = "Sample Activity",
            Total     = StoredAipActivityTotal,
        };
        aipRepo.Setup(r => r.GetActivityByIdAsync(AipActivityId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(activity);

        // The save and finalize cases run against a record with NO division, so the division-
        // allocation ceiling is skipped entirely and the AIP budget is the only thing that can
        // trip. Isolating the boundary is the whole point — an allocation that happened to sit
        // near ₱250,000 would make these tests pass for the wrong reason.
        wfpExpRepo.Setup(r => r.GetActivityContextAsync(WfpActivityId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new WfpExpenditureContext(
                WfpRecordId, divisionOnRecord, OfficeId, FiscalYear, AipActivityId));

        divisionRepo.Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync([new Division
            {
                Id = DivisionId, OfficeId = OfficeId, Name = "Planning Division",
                IsActive = true, CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow,
            }]);

        fundingSourceRepo.Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync([new FundingSource
            {
                Id = GfFundId, Code = "GF", Name = "General Fund", IsActive = true,
                CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow,
            }]);

        allocation.Setup(a => a.GetGeneralFundIdAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(GfFundId);

        // Deliberately far above anything asserted here — GetStatus reports the division
        // allocation alongside the AIP budget, and it must not be what these cases measure.
        allocation.Setup(a => a.GetAllocationsAsync(
                OfficeId, FiscalYear, It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((int _, int _, int fundId, CancellationToken _) =>
                (IReadOnlyList<DivisionAllocationDto>)
                    [new DivisionAllocationDto(1, DivisionId, "Planning Division", FiscalYear,
                        fundId, "GF", "General Fund", 999_999_999m)]);

        ledgerRepo.Setup(r => r.SumUsedAmountAsync(
                It.IsAny<int>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<int?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(0m);
        ledgerRepo.Setup(r => r.GetFundingSourceIdsForRecordAsync(
                It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyList<int>)[]);

        WfpCeilingService sut = new(
            wfpExpRepo.Object, wfpRepo.Object, ledgerRepo.Object, aipRepo.Object,
            allocation.Object, divisionRepo.Object, fundingSourceRepo.Object);

        return (sut, wfpExpRepo, wfpRepo);
    }

    /// <summary>How much of the AIP activity other expenditures have already consumed.</summary>
    private static void AipActivityAlreadyUsed(Mock<IWfpExpenditureRepository> wfpExpRepo, decimal pesos) =>
        wfpExpRepo.Setup(r => r.SumTotalByAipActivityAsync(
                AipActivityId, OfficeId, FiscalYear, It.IsAny<int?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(pesos);

    private static void GivenRecordWithOneActivity(Mock<IWfpRepository> wfpRepo)
    {
        wfpRepo.Setup(r => r.GetByIntIdAsync(WfpRecordId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new WfpRecord
            {
                Id = WfpRecordId, OfficeId = OfficeId, FiscalYear = FiscalYear, DivisionId = null,
            });
        wfpRepo.Setup(r => r.GetActivitiesByWfpIdAsync(WfpRecordId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyList<WfpActivity>)
                [new WfpActivity { Id = WfpActivityId, WfpId = WfpRecordId, AipActivityId = AipActivityId }]);
    }

    // ── Read path: the number the frontend renders ────────────────────────────

    [Fact]
    public async Task GetStatus_ActivityBudgetedAtTwoHundredFiftyThousandPesos_ReportsThatManyPesos()
    {
        var (sut, wfpExpRepo, _) = Build(divisionOnRecord: DivisionId);
        AipActivityAlreadyUsed(wfpExpRepo, 100_000m);

        WfpCeilingStatusDto status =
            await sut.GetStatusAsync(AipActivityId, DivisionId, FiscalYear, CancellationToken.None);

        // A ₱250,000 AIP activity is ₱250,000 of ceiling. Not ₱250. Not ₱250,000,000.
        Assert.Equal(AipBudgetPesos, status.AipBudget);
        Assert.Equal(100_000m, status.AipUsed);
    }

    // ── Save path: the guard a user actually hits ─────────────────────────────

    [Fact]
    public async Task ValidateExpenditureSave_LandingExactlyOnTheAipBudget_IsAccepted()
    {
        var (sut, wfpExpRepo, _) = Build();
        AipActivityAlreadyUsed(wfpExpRepo, 200_000m);

        // 200,000 + 50,000 = exactly ₱250,000. Accepted only if the budget really is ₱250,000 —
        // a factor smaller than ×1000 makes this reject.
        string? result = await sut.ValidateExpenditureSaveAsync(
            WfpActivityId, 50_000m, null, null, CancellationToken.None);

        Assert.Null(result);
    }

    [Fact]
    public async Task ValidateExpenditureSave_OneCentavoOverTheAipBudget_IsRejectedNamingThePesoBudget()
    {
        var (sut, wfpExpRepo, _) = Build();
        AipActivityAlreadyUsed(wfpExpRepo, 200_000m);

        // ₱250,000.01. Rejected only if the budget really is ₱250,000 — a factor larger than
        // ×1000 lets this through silently, which is the dangerous direction.
        string? result = await sut.ValidateExpenditureSaveAsync(
            WfpActivityId, 50_000m + OneCentavo, null, null, CancellationToken.None);

        Assert.NotNull(result);
        // The message must quote the peso budget itself, not merely say "over the AIP budget".
        // Formatted through the same culture the service formats with.
        Assert.Contains(AipBudgetPesos.ToString("N2"), result);
        Assert.Contains((AipBudgetPesos + OneCentavo).ToString("N2"), result);
    }

    // ── Finalize path: the backstop before a WFP is locked ────────────────────

    [Fact]
    public async Task ValidateRecordForFinalize_UsageExactlyAtTheAipBudget_IsAccepted()
    {
        var (sut, wfpExpRepo, wfpRepo) = Build();
        GivenRecordWithOneActivity(wfpRepo);
        AipActivityAlreadyUsed(wfpExpRepo, AipBudgetPesos);

        string? result = await sut.ValidateRecordForFinalizeAsync(WfpRecordId, CancellationToken.None);

        Assert.Null(result);
    }

    [Fact]
    public async Task ValidateRecordForFinalize_OneCentavoOverTheAipBudget_IsRejectedNamingThePesoBudget()
    {
        var (sut, wfpExpRepo, wfpRepo) = Build();
        GivenRecordWithOneActivity(wfpRepo);
        AipActivityAlreadyUsed(wfpExpRepo, AipBudgetPesos + OneCentavo);

        string? result = await sut.ValidateRecordForFinalizeAsync(WfpRecordId, CancellationToken.None);

        Assert.NotNull(result);
        Assert.Contains(ActivityRefCode, result);
        Assert.Contains(AipBudgetPesos.ToString("N2"), result);
        Assert.Contains((AipBudgetPesos + OneCentavo).ToString("N2"), result);
    }
}
