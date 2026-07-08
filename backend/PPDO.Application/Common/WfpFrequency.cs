namespace PPDO.Application.Common;

/// <summary>
/// String constants for the WFP expenditure "Frequency" field (v1.4 WFP Rework — §2).
/// Drives how many periods are entered and how they roll up to Q1–Q4 —
/// see <c>WfpExpenditureCalculator</c> for the roll-up rules.
/// </summary>
public static class WfpFrequency
{
    /// <summary>Monthly — 12 periods (Jan–Dec).</summary>
    public const string Monthly = "M";

    /// <summary>Quarterly — 4 periods (Q1–Q4), direct roll-up.</summary>
    public const string Quarterly = "Q";

    /// <summary>Bi-annual — 2 periods (1st Half, 2nd Half).</summary>
    public const string BiAnnual = "B";

    /// <summary>Annual — 1 period, charged to a chosen quarter (default Q1).</summary>
    public const string Annual = "A";
}
