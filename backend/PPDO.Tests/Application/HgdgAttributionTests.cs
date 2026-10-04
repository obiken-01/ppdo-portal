using PPDO.Application.Common;

namespace PPDO.Tests.Application;

/// <summary>
/// Unit tests for <see cref="HgdgAttribution"/> (PPDO-156): the HGDG score → attributed GAD budget
/// rule of Investment_Proposal_Spec.md decision 9. Pure/static, no mocks needed.
/// Covers every band boundary, a null score, a zero cost, out-of-range scores and the rounding.
/// </summary>
public sealed class HgdgAttributionTests
{
    // ── Percent: every boundary of decision 9 ─────────────────────────────────

    [Theory]
    [InlineData("0",    0)]
    [InlineData("3.9",  0)]
    [InlineData("4.0",  25)]
    [InlineData("7.9",  25)]
    [InlineData("8.0",  50)]
    [InlineData("14.9", 50)]
    [InlineData("15.0", 75)]
    [InlineData("19.9", 75)]
    [InlineData("20.0", 100)]
    public void Percent_EachBandBoundary(string score, int expected)
        => Assert.Equal(expected, HgdgAttribution.Percent(decimal.Parse(score)));

    [Fact]
    public void Percent_NoScore_IsNull() => Assert.Null(HgdgAttribution.Percent(null));

    [Theory]
    [InlineData("-0.1")]
    [InlineData("20.1")]
    public void Percent_OutsideZeroToTwenty_IsNull(string score)
        => Assert.Null(HgdgAttribution.Percent(decimal.Parse(score)));

    // ── AttributedBudget ──────────────────────────────────────────────────────

    [Fact]
    public void AttributedBudget_ScoreEight_HalfOfTheCost()
        => Assert.Equal(100_000.00m, HgdgAttribution.AttributedBudget(8m, 200_000m));

    [Fact]
    public void AttributedBudget_NoScore_IsNull_NotZero()
        // With no score all three GAD figures print blank (decision 9), not 0.00.
        => Assert.Null(HgdgAttribution.AttributedBudget(null, 200_000m));

    [Fact]
    public void AttributedBudget_FullScoreButNoCost_IsZero()
        => Assert.Equal(0.00m, HgdgAttribution.AttributedBudget(20m, 0m));

    [Fact]
    public void AttributedBudget_BelowFour_IsZero()
        => Assert.Equal(0.00m, HgdgAttribution.AttributedBudget(3.9m, 417_662.12m));

    [Theory]
    [InlineData("0.10", "0.03")]        // 25% of 0.10 = 0.025 → away from zero, not to even (0.02)
    [InlineData("417662.12", "104415.53")]  // 25% = 104,415.53 exactly
    [InlineData("100.02", "25.01")]     // 25.005 → 25.01
    public void AttributedBudget_RoundsToTheCentavo_AwayFromZero(string cost, string expected)
        => Assert.Equal(decimal.Parse(expected), HgdgAttribution.AttributedBudget(4m, decimal.Parse(cost)));
}
