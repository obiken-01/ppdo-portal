using PPDO.Application.Common;

namespace PPDO.Tests.Application;

/// <summary>
/// Where an itemised AIP line's money lands (V18-80 / PPDO-54).
///
/// <para>
/// This is the question WFP never had to answer — its expenditure carries one <c>amount</c> per
/// period, the AIP's carries three columns. Every failure mode here is silent: money in the wrong
/// column of a statutory form still prints, still sums, and (for MOOE and CO) still moves the
/// office's General Fund ceiling consumption.
/// </para>
/// </summary>
public sealed class AipProcurementRoutingTests
{
    [Theory]
    [InlineData("PS")]
    [InlineData("ps")]
    [InlineData("  PS  ")]
    public void Route_PersonalServices_PutsTheWholeTotalInPsAndNothingElsewhere(string expenseClass)
    {
        (decimal Ps, decimal Mooe, decimal Co)? routed =
            AipProcurementRouting.Route(expenseClass, 250_000m);

        Assert.NotNull(routed);
        Assert.Equal(250_000m, routed!.Value.Ps);
        Assert.Equal(0m, routed.Value.Mooe);
        Assert.Equal(0m, routed.Value.Co);
    }

    [Fact]
    public void Route_Mooe_PutsTheWholeTotalInMooe()
    {
        (decimal Ps, decimal Mooe, decimal Co)? routed =
            AipProcurementRouting.Route("MOOE", 80_000m);

        Assert.Equal((0m, 80_000m, 0m), routed);
    }

    [Fact]
    public void Route_CapitalOutlay_PutsTheWholeTotalInCo()
    {
        (decimal Ps, decimal Mooe, decimal Co)? routed =
            AipProcurementRouting.Route("CO", 1_500_000m);

        Assert.Equal((0m, 0m, 1_500_000m), routed);
    }

    /// <summary>
    /// ⚠️ The important one. An account with no expense class, or one nobody recognises, must
    /// produce <b>no answer</b> so the caller refuses the write.
    ///
    /// Defaulting to MOOE — the tempting fallback, since most procurement is MOOE — would put real
    /// money in the wrong column of the printed AIP <i>and</i> silently change what the office has
    /// consumed of its General Fund ceiling, which is computed from MOOE + CO. Neither is
    /// detectable by reading the form.
    /// </summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("Personal Services")]  // the human label, not the stored code
    [InlineData("MOE")]                 // a plausible typo
    public void Route_UnrecognisedExpenseClass_ReturnsNullRatherThanGuessing(string? expenseClass)
        => Assert.Null(AipProcurementRouting.Route(expenseClass, 100_000m));

    /// <summary>A zero-cost itemised line is expressible, and still routes to a real column.</summary>
    [Fact]
    public void Route_ZeroTotal_StillRoutesToTheNamedColumn()
        => Assert.Equal((0m, 0m, 0m), AipProcurementRouting.Route("MOOE", 0m));
}
