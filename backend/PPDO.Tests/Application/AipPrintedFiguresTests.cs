using PPDO.Application.Common;
using PPDO.Application.DTOs.BudgetPlanning;
using PPDO.Domain.Entities;

namespace PPDO.Tests.Application;

/// <summary>
/// The figures the AIP form prints (PPDO-73): round up per figure, the +30% on MOOE and CO applied
/// before rounding, and every level a sum of the level below.
///
/// ⚠️ Nothing here asserts <c>printed == 1.3 × base</c> — once rounded they are not, by design
/// (<see cref="AipRounding"/>).
/// </summary>
public sealed class AipPrintedFiguresTests
{
    private static AipActivity Activity(
        decimal? ps = null, decimal? mooe = null, decimal? co = null,
        decimal? cca = null, decimal? ccm = null)
        => new()
        {
            Id = 1, ProjectId = 1, RefCode = "x", Name = "x",
            Ps = ps, Mooe = mooe, Co = co, CcAdaptation = cca, CcMitigation = ccm,
        };

    /// <summary>The worked example in <c>AIP_Form_Spec.md</c> §6.1, under uplift-then-round (G5).</summary>
    [Fact]
    public void ForActivity_Fy2028_UpliftsMooeThenRoundsUp()
    {
        AipPrintedAmountsDto printed = AipPrintedFigures.ForActivity(Activity(mooe: 1_000_400m), 2028);

        Assert.Equal(1_301_000m, printed.Mooe);
    }

    [Fact]
    public void ForActivity_Fy2028_PsIsRoundedButNeverUplifted()
    {
        AipPrintedAmountsDto printed = AipPrintedFigures.ForActivity(Activity(ps: 1_200m), 2028);

        Assert.Equal(2_000m, printed.Ps);
    }

    [Fact]
    public void ForActivity_Fy2028_CoIsUpliftedLikeMooe()
    {
        // 1,000 × 1.3 = 1,300 → up to 2,000. Without the uplift it would have stayed 1,000.
        AipPrintedAmountsDto printed = AipPrintedFigures.ForActivity(Activity(co: 1_000m), 2028);

        Assert.Equal(2_000m, printed.Co);
    }

    /// <summary>The uplift is on MOOE and CO only; the climate-change columns round but do not grow.</summary>
    [Fact]
    public void ForActivity_Fy2028_ClimateChangeColumnsAreRoundedNotUplifted()
    {
        AipPrintedAmountsDto printed = AipPrintedFigures.ForActivity(Activity(cca: 1_000m, ccm: 1_001m), 2028);

        Assert.Equal(1_000m, printed.CcAdaptation);
        Assert.Equal(2_000m, printed.CcMitigation);
    }

    /// <summary>⚠️ Column (11) is printed 8+9+10 — the sum of the printed figures, not a rounded exact total.</summary>
    [Fact]
    public void ForActivity_Fy2028_TotalIsTheSumOfThePrintedColumns()
    {
        AipPrintedAmountsDto printed = AipPrintedFigures.ForActivity(
            Activity(ps: 1_200m, mooe: 1_000_400m, co: 1_000m), 2028);

        Assert.Equal(printed.Ps + printed.Mooe + printed.Co, printed.Total);
        Assert.Equal(1_305_000m, printed.Total);
    }

    [Fact]
    public void ForActivity_NullAmounts_PrintAsZero()
    {
        AipPrintedAmountsDto printed = AipPrintedFigures.ForActivity(Activity(), 2028);

        Assert.Equal(AipPrintedFigures.Zero, printed);
    }

    /// <summary>FY≤2027 keeps its old shape and is not re-rendered under the new rules (<c>AIP_Form_Spec.md</c> §8).</summary>
    [Fact]
    public void ForActivity_BeforeTheRedesign_PrintsExactAmounts()
    {
        AipPrintedAmountsDto printed = AipPrintedFigures.ForActivity(
            Activity(ps: 1_200m, mooe: 1_000_400m), 2027);

        Assert.Equal(1_200m, printed.Ps);
        Assert.Equal(1_000_400m, printed.Mooe);
        Assert.Equal(1_001_600m, printed.Total);
    }

    /// <summary>
    /// ⚠️ Round first, then add — three ₱1,200 MOOE figures print 2,000 each and sum to 6,000.
    /// Summing the exact uplifted amounts first would give ceil(4,680) = 5,000 and disagree with the rows.
    /// </summary>
    [Fact]
    public void Sum_AddsAlreadyPrintedFiguresWithoutRoundingAgain()
    {
        AipPrintedAmountsDto one = AipPrintedFigures.ForActivity(Activity(mooe: 1_200m), 2028);

        AipPrintedAmountsDto total = AipPrintedFigures.Sum([one, one, one]);

        Assert.Equal(6_000m, total.Mooe);
        Assert.Equal(6_000m, total.Total);
    }
}
