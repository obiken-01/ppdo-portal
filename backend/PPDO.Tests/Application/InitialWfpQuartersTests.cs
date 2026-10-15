using PPDO.Application.Common;

namespace PPDO.Tests.Application;

/// <summary>
/// The quarter rules of the initial WFP (PPDO-224, <c>Initial_WFP_Report_Spec.md</c> §2 rules 2, 9
/// and 10). Written before the implementation. The invariant every case checks: Q1–Q4 sum to the
/// line's AIP figure exactly, to the centavo.
/// </summary>
public sealed class InitialWfpQuartersTests
{
    private static void AssertQuarters(decimal[] actual, decimal q1, decimal q2, decimal q3, decimal q4)
        => Assert.Equal(new[] { q1, q2, q3, q4 }, actual);

    // ── Rule 9: the activity's months ─────────────────────────────────────────

    [Theory]
    [InlineData("January", "June", 1, 6)]
    [InlineData("march", "MAY", 3, 5)]            // case does not matter
    [InlineData(" Feb ", "Apr", 2, 4)]            // short names, padding
    [InlineData("Sept", "December", 9, 12)]
    [InlineData("July", "July", 7, 7)]            // a single month
    public void ActivityMonths_ReadsMonthNames(string start, string end, int s, int e)
        => Assert.Equal((s, e), InitialWfpQuarters.ActivityMonths(start, end));

    [Theory]
    [InlineData(null, null)]
    [InlineData("", "")]
    [InlineData("January", null)]                  // one side missing
    [InlineData(null, "June")]
    [InlineData("Q1", "Q2")]                       // unreadable
    [InlineData("2028-01-01", "2028-06-30")]
    [InlineData("June", "March")]                  // End before Start
    public void ActivityMonths_MissingUnreadableOrBackwards_IsTheWholeYear(string? start, string? end)
        => Assert.Equal((1, 12), InitialWfpQuarters.ActivityMonths(start, end));

    // ── Rule 9: spreading ─────────────────────────────────────────────────────

    [Fact]
    public void Spread_JanToJun_HalvesOverQ1AndQ2()   // spec §3 case 3
        => AssertQuarters(InitialWfpQuarters.Spread(120_000m, 1, 6), 60_000m, 60_000m, 0m, 0m);

    [Fact]
    public void Spread_MarToMay_PutsTheRoundingRemainderInTheLastQuarterWithMonths()   // spec §3 case 4
        => AssertQuarters(InitialWfpQuarters.Spread(100_000m, 3, 5), 33_333.33m, 66_666.67m, 0m, 0m);

    [Fact]
    public void Spread_WholeYear_IsEvenQuarters()
        => AssertQuarters(InitialWfpQuarters.Spread(100_000m, 1, 12), 25_000m, 25_000m, 25_000m, 25_000m);

    [Fact]
    public void Spread_ThirdsAcrossTheYear_RemainderGoesToQ4_NotQ1()
        // 100 / 12 months: each quarter 33.333…, rounded 33.33, Q4 takes 33.34.
        => AssertQuarters(InitialWfpQuarters.Spread(133.33m, 1, 12), 33.33m, 33.33m, 33.33m, 33.34m);

    [Fact]
    public void Spread_RemainderGoesToTheLastQuarterWithMonths_EvenWhenLaterQuartersAreEmpty()
        // Feb–Jul: Q1 2 months, Q2 3, Q3 1 (of 6). 1,000 → 333.33 / 500.00 / 166.67.
        => AssertQuarters(InitialWfpQuarters.Spread(1_000m, 2, 7), 333.33m, 500.00m, 166.67m, 0m);

    [Fact]
    public void Spread_ASingleMonth_IsAllInItsQuarter()
        => AssertQuarters(InitialWfpQuarters.Spread(10_000m, 11, 11), 0m, 0m, 0m, 10_000m);

    [Fact]
    public void Spread_Zero_IsAllZero()
        => AssertQuarters(InitialWfpQuarters.Spread(0m, 1, 12), 0m, 0m, 0m, 0m);

    [Fact]
    public void Spread_ANegativeAmount_StillSumsExactly()
        => Assert.Equal(-100m, InitialWfpQuarters.Spread(-100m, 3, 5).Sum());

    [Theory]
    [InlineData(0.01, 1, 12)]
    [InlineData(0.05, 2, 11)]
    [InlineData(999_999.99, 3, 8)]
    [InlineData(1_234_567.89, 1, 12)]
    [InlineData(7.77, 5, 10)]
    public void Spread_AlwaysSumsToTheAmountExactly(double amount, int start, int end)
    {
        decimal a = (decimal)amount;
        decimal[] q = InitialWfpQuarters.Spread(a, start, end);
        Assert.Equal(a, q.Sum());
        Assert.All(q, v => Assert.Equal(v, Math.Round(v, 2)));   // whole centavos
    }

    // ── Rule 2 / rule 10: itemised lines ──────────────────────────────────────

    [Fact]
    public void ForLine_Itemised_PutsEachItemInItsQuarter()   // spec §3 case 5
        => AssertQuarters(
            InitialWfpQuarters.ForLine(50_000m, [(2, 20_000m), (4, 30_000m)], 1, 12),
            0m, 20_000m, 0m, 30_000m);

    [Fact]
    public void ForLine_ItemsInTheSameQuarter_Add()
        => AssertQuarters(
            InitialWfpQuarters.ForLine(3_000m, [(3, 1_000m), (3, 2_000m)], 1, 12),
            0m, 0m, 3_000m, 0m);

    [Fact]
    public void ForLine_ItemsIgnoreTheActivitysMonths()
        // An item planned in Q4 stays in Q4 even if the activity says Jan–Mar.
        => AssertQuarters(InitialWfpQuarters.ForLine(500m, [(4, 500m)], 1, 3), 0m, 0m, 0m, 500m);

    [Fact]
    public void ForLine_ItemsShortOfTheLine_SpreadTheDifferenceByRule9()   // rule 10
        // Line 10,000, items 4,000 in Q2; the 6,000 left spreads over Jan–Jun: 3,000 / 3,000.
        => AssertQuarters(
            InitialWfpQuarters.ForLine(10_000m, [(2, 4_000m)], 1, 6),
            3_000m, 7_000m, 0m, 0m);

    [Fact]
    public void ForLine_ItemsOverTheLine_SpreadTheNegativeDifference_SoTheLineStillRules()   // rule 10
    {
        decimal[] q = InitialWfpQuarters.ForLine(1_000m, [(1, 1_200m)], 1, 12);
        Assert.Equal(1_000m, q.Sum());
        AssertQuarters(q, 1_150m, -50m, -50m, -50m);
    }

    [Fact]
    public void ForLine_NoItems_IsRule9()
        => AssertQuarters(InitialWfpQuarters.ForLine(120_000m, [], 1, 6), 60_000m, 60_000m, 0m, 0m);

    [Theory]
    [InlineData(0)]
    [InlineData(5)]
    [InlineData(-1)]
    public void ForLine_AnItemOutsideQ1ToQ4_IsHeldToTheNearestQuarter_NeverLost(int period)
    {
        decimal[] q = InitialWfpQuarters.ForLine(100m, [(period, 100m)], 1, 12);
        Assert.Equal(100m, q.Sum());
        Assert.Equal(100m, period >= 5 ? q[3] : q[0]);
    }

    [Theory]
    [InlineData(10_000.01, 3_333.33)]
    [InlineData(1, 0.37)]
    [InlineData(99.99, 100.01)]
    public void ForLine_AlwaysSumsToTheLine(double line, double item)
        => Assert.Equal((decimal)line,
            InitialWfpQuarters.ForLine((decimal)line, [(2, (decimal)item)], 3, 10).Sum());
}
