using PPDO.Application.Common;

namespace PPDO.Tests.Application;

/// <summary>
/// Unit tests for <see cref="WfpExpenditureCalculator"/> (RAL-120) — the single computation
/// pipeline for WFP expenditure totals. Pure/static, no mocks needed.
/// Covers: one test per frequency roll-up rule (§2), reserve exclusion from the quarterly
/// release plan, procurement item merging (line_total via Σqty×price per period), and the
/// valid period-number range per frequency.
/// </summary>
public sealed class WfpExpenditureCalculatorTests
{
    private static Dictionary<int, decimal> Periods(params (int PeriodNo, decimal Amount)[] entries)
        => entries.ToDictionary(e => e.PeriodNo, e => e.Amount);

    // ── Frequency roll-up rules (§2) ──────────────────────────────────────────

    [Fact]
    public void Compute_Monthly_RollsUpJanFebMarToQ1AndSoOn()
    {
        // Jan..Dec = 100 each. Q1=Jan+Feb+Mar=300, Q2=Apr+May+Jun=300, etc.
        Dictionary<int, decimal> periods = Periods(
            (1, 100), (2, 100), (3, 100), (4, 100), (5, 100), (6, 100),
            (7, 100), (8, 100), (9, 100), (10, 100), (11, 100), (12, 100));

        WfpExpenditureCalculator.RollUp result = WfpExpenditureCalculator.Compute(
            WfpFrequency.Monthly, periods, reserveAmount: 0m, annualQuarterChoice: null);

        Assert.Equal(300m, result.Q1);
        Assert.Equal(300m, result.Q2);
        Assert.Equal(300m, result.Q3);
        Assert.Equal(300m, result.Q4);
        Assert.Equal(1200m, result.Net);
    }

    [Fact]
    public void Compute_Quarterly_MapsDirectlyToQ1ThroughQ4()
    {
        Dictionary<int, decimal> periods = Periods((1, 1000), (2, 2000), (3, 3000), (4, 4000));

        WfpExpenditureCalculator.RollUp result = WfpExpenditureCalculator.Compute(
            WfpFrequency.Quarterly, periods, reserveAmount: 0m, annualQuarterChoice: null);

        Assert.Equal(1000m, result.Q1);
        Assert.Equal(2000m, result.Q2);
        Assert.Equal(3000m, result.Q3);
        Assert.Equal(4000m, result.Q4);
        Assert.Equal(10000m, result.Net);
    }

    [Fact]
    public void Compute_BiAnnual_FirstHalfToQ1_SecondHalfToQ3()
    {
        Dictionary<int, decimal> periods = Periods((1, 5000), (2, 7000));

        WfpExpenditureCalculator.RollUp result = WfpExpenditureCalculator.Compute(
            WfpFrequency.BiAnnual, periods, reserveAmount: 0m, annualQuarterChoice: null);

        Assert.Equal(5000m, result.Q1);
        Assert.Equal(0m, result.Q2);
        Assert.Equal(7000m, result.Q3);
        Assert.Equal(0m, result.Q4);
        Assert.Equal(12000m, result.Net);
    }

    [Fact]
    public void Compute_Annual_DefaultsToQ1WhenChoiceIsNull()
    {
        Dictionary<int, decimal> periods = Periods((1, 12000));

        WfpExpenditureCalculator.RollUp result = WfpExpenditureCalculator.Compute(
            WfpFrequency.Annual, periods, reserveAmount: 0m, annualQuarterChoice: null);

        Assert.Equal(12000m, result.Q1);
        Assert.Equal(0m, result.Q2);
        Assert.Equal(0m, result.Q3);
        Assert.Equal(0m, result.Q4);
    }

    [Fact]
    public void Compute_Annual_ChargesToChosenQuarter()
    {
        Dictionary<int, decimal> periods = Periods((1, 12000));

        WfpExpenditureCalculator.RollUp result = WfpExpenditureCalculator.Compute(
            WfpFrequency.Annual, periods, reserveAmount: 0m, annualQuarterChoice: 4);

        Assert.Equal(0m, result.Q1);
        Assert.Equal(0m, result.Q2);
        Assert.Equal(0m, result.Q3);
        Assert.Equal(12000m, result.Q4);
    }

    [Fact]
    public void Compute_UnknownFrequency_Throws()
    {
        Assert.Throws<ArgumentException>(() =>
            WfpExpenditureCalculator.Compute("X", new Dictionary<int, decimal>(), 0m, null));
    }

    // ── Reserve exclusion from the quarterly release plan (§6) ───────────────

    [Fact]
    public void Compute_ReserveAmount_NeverAffectsQuartersOrNet_OnlyTotal()
    {
        Dictionary<int, decimal> periods = Periods((1, 1000), (2, 1000), (3, 1000), (4, 1000));

        WfpExpenditureCalculator.RollUp withoutReserve = WfpExpenditureCalculator.Compute(
            WfpFrequency.Quarterly, periods, reserveAmount: 0m, annualQuarterChoice: null);
        WfpExpenditureCalculator.RollUp withReserve = WfpExpenditureCalculator.Compute(
            WfpFrequency.Quarterly, periods, reserveAmount: 400m, annualQuarterChoice: null);

        // Q1-4 and Net are identical regardless of reserve — periods ARE the net plan.
        Assert.Equal(withoutReserve.Q1, withReserve.Q1);
        Assert.Equal(withoutReserve.Q2, withReserve.Q2);
        Assert.Equal(withoutReserve.Q3, withReserve.Q3);
        Assert.Equal(withoutReserve.Q4, withReserve.Q4);
        Assert.Equal(withoutReserve.Net, withReserve.Net);
        Assert.Equal(4000m, withReserve.Net);

        // Total = Net + Reserved (not a display trick — reserve genuinely adds to Total).
        Assert.Equal(4000m, withoutReserve.Total);
        Assert.Equal(4400m, withReserve.Total);
    }

    // ── Procurement item merging (line_total via qty × unit price) ───────────

    [Fact]
    public void MergePeriodAmounts_ProcurementItemsOnly_SumsQtyTimesPricePerPeriod()
    {
        var typedPeriods = Array.Empty<(int PeriodNo, decimal Amount)>();
        var items = new[]
        {
            (PeriodNo: 1, Qty: 10m, UnitPrice: 25m),  // 250
            (PeriodNo: 1, Qty: 2m,  UnitPrice: 100m), // 200 -> period 1 total 450
            (PeriodNo: 2, Qty: 5m,  UnitPrice: 40m),  // 200
        };

        Dictionary<int, decimal> merged = WfpExpenditureCalculator.MergePeriodAmounts(typedPeriods, items);

        Assert.Equal(450m, merged[1]);
        Assert.Equal(200m, merged[2]);
    }

    [Fact]
    public void MergePeriodAmounts_CombinedTypedAndProcurement_SumsBothForSamePeriod()
    {
        // "Combined" nature: a typed non-procurement amount AND procurement items share period 1.
        var typedPeriods = new[] { (PeriodNo: 1, Amount: 1000m) };
        var items = new[] { (PeriodNo: 1, Qty: 3m, UnitPrice: 50m) }; // 150

        Dictionary<int, decimal> merged = WfpExpenditureCalculator.MergePeriodAmounts(typedPeriods, items);

        Assert.Equal(1150m, merged[1]);
    }

    [Fact]
    public void MergePeriodAmounts_ThenCompute_ProducesCorrectLineTotalDrivenRollup()
    {
        // Two procurement items in Q1 (Quarterly frequency): 10x25 + 2x100 = 450.
        var typedPeriods = Array.Empty<(int PeriodNo, decimal Amount)>();
        var items = new[]
        {
            (PeriodNo: 1, Qty: 10m, UnitPrice: 25m),
            (PeriodNo: 1, Qty: 2m,  UnitPrice: 100m),
        };
        Dictionary<int, decimal> merged = WfpExpenditureCalculator.MergePeriodAmounts(typedPeriods, items);

        WfpExpenditureCalculator.RollUp result = WfpExpenditureCalculator.Compute(
            WfpFrequency.Quarterly, merged, reserveAmount: 0m, annualQuarterChoice: null);

        Assert.Equal(450m, result.Q1);
        Assert.Equal(450m, result.Net);
    }

    // ── Period range per frequency ────────────────────────────────────────────

    [Theory]
    [InlineData(WfpFrequency.Monthly, 1, 12)]
    [InlineData(WfpFrequency.Quarterly, 1, 4)]
    [InlineData(WfpFrequency.BiAnnual, 1, 2)]
    [InlineData(WfpFrequency.Annual, 1, 1)]
    public void PeriodRange_ReturnsExpectedBoundsPerFrequency(string frequency, int expectedMin, int expectedMax)
    {
        (int min, int max) = WfpExpenditureCalculator.PeriodRange(frequency);

        Assert.Equal(expectedMin, min);
        Assert.Equal(expectedMax, max);
    }

    [Fact]
    public void PeriodRange_UnknownFrequency_ReturnsEmptyRange()
    {
        (int min, int max) = WfpExpenditureCalculator.PeriodRange("X");

        Assert.True(max < min);
    }
}
