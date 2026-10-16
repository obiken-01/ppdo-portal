namespace PPDO.Application.Common;

/// <summary>
/// How an AIP line's amount is laid over Q1–Q4 for the initial WFP (v1.9.0 — PPDO-224,
/// <c>docs/v1.9/Initial_WFP_Report_Spec.md</c> §2 decisions 2, 9 and 10). Pure and static.
///
/// <para>
/// <b>The invariant: Q1–Q4 always sum to the line's AIP figure exactly, to the centavo.</b> Every
/// rounding remainder goes to the <b>last</b> quarter that has months, never dropped and never
/// spread, so the initial WFP's totals are the AIP's totals.
/// </para>
/// </summary>
public static class InitialWfpQuarters
{
    private static readonly string[] MonthNames =
    [
        "january", "february", "march", "april", "may", "june",
        "july", "august", "september", "october", "november", "december",
    ];

    /// <summary>
    /// The activity's months from its Start and End month names (rule 9). Full names or any prefix
    /// of at least three letters ("Feb", "Sept"), any case. Missing, unreadable, or End before Start
    /// is the whole year, January–December.
    /// </summary>
    public static (int Start, int End) ActivityMonths(string? startMonth, string? endMonth)
        => ParseMonth(startMonth) is int start && ParseMonth(endMonth) is int end && end >= start
            ? (start, end)
            : (1, 12);

    /// <summary>
    /// Rule 9: each quarter gets <c>amount × (months in that quarter ÷ total months)</c>, rounded to
    /// the centavo, with the remainder added to the last quarter that has months.
    /// </summary>
    public static decimal[] Spread(decimal amount, int startMonth, int endMonth)
    {
        int[] monthsIn = new int[4];
        for (int m = startMonth; m <= endMonth; m++) monthsIn[(m - 1) / 3]++;
        int total = endMonth - startMonth + 1;
        int last = Array.FindLastIndex(monthsIn, n => n > 0);

        decimal[] quarters = new decimal[4];
        decimal placed = 0m;
        for (int q = 0; q < 4; q++)
        {
            if (monthsIn[q] == 0 || q == last) continue;
            quarters[q] = Math.Round(amount * monthsIn[q] / total, 2, MidpointRounding.AwayFromZero);
            placed += quarters[q];
        }
        quarters[last] = amount - placed;
        return quarters;
    }

    /// <summary>
    /// One line's quarters. Rule 2: each item's amount goes to the quarter it was entered in
    /// (<c>AipProcurementItem.PeriodNo</c>, held to 1–4). Rule 10: whatever the items do not account
    /// for (positive or negative) is spread by rule 9, so the line's total is always the AIP's figure.
    /// A line without items is rule 9 alone.
    /// </summary>
    public static decimal[] ForLine(
        decimal lineAmount, IEnumerable<(int PeriodNo, decimal Amount)> items, int startMonth, int endMonth)
    {
        decimal[] quarters = new decimal[4];
        decimal itemised = 0m;
        foreach ((int periodNo, decimal amount) in items)
        {
            quarters[Math.Clamp(periodNo, 1, 4) - 1] += amount;
            itemised += amount;
        }

        decimal[] rest = Spread(lineAmount - itemised, startMonth, endMonth);
        for (int q = 0; q < 4; q++) quarters[q] += rest[q];
        return quarters;
    }

    private static int? ParseMonth(string? value)
    {
        string token = (value ?? string.Empty).Trim().ToLowerInvariant();
        if (token.Length < 3 || !token.All(char.IsLetter)) return null;
        int index = Array.FindIndex(MonthNames, name => name.StartsWith(token, StringComparison.Ordinal));
        return index < 0 ? null : index + 1;
    }
}
