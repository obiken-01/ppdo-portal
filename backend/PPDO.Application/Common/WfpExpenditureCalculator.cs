namespace PPDO.Application.Common;

/// <summary>
/// The single computation pipeline for WFP expenditure totals (v1.4 WFP Rework — RAL-120,
/// §2/§9). Used by the entry save path today and, later, the report generator — nobody else
/// recomputes this math. Pure/static and side-effect-free so it is trivially unit-testable.
///
/// Roll-up rules (§2), applied to period amounts keyed by 1-based period_no:
///   Monthly:   12 periods -> Q1=1-3, Q2=4-6, Q3=7-9, Q4=10-12
///   Quarterly: 4 periods  -> direct Q1..Q4
///   Bi-annual: 2 periods  -> 1st Half -> Q1, 2nd Half -> Q3
///   Annual:    1 period   -> charged to annualQuarterChoice (default Q1)
///
/// Reserve is excluded from the quarterly release plan: Net = ΣQ (what the periods sum to),
/// Total = Net + reserveAmount — reserveAmount never contributes to Q1–Q4 or Net.
/// </summary>
public static class WfpExpenditureCalculator
{
    public sealed record RollUp(decimal Q1, decimal Q2, decimal Q3, decimal Q4, decimal Net, decimal Total);

    /// <summary>Rolls up period amounts into Q1–Q4/Net/Total per the frequency rules above.</summary>
    public static RollUp Compute(
        string frequency,
        IReadOnlyDictionary<int, decimal> periodAmounts,
        decimal reserveAmount,
        int? annualQuarterChoice)
    {
        decimal q1 = 0m, q2 = 0m, q3 = 0m, q4 = 0m;

        switch (frequency)
        {
            case WfpFrequency.Monthly:
                q1 = Sum(periodAmounts, 1, 2, 3);
                q2 = Sum(periodAmounts, 4, 5, 6);
                q3 = Sum(periodAmounts, 7, 8, 9);
                q4 = Sum(periodAmounts, 10, 11, 12);
                break;

            case WfpFrequency.Quarterly:
                q1 = Get(periodAmounts, 1);
                q2 = Get(periodAmounts, 2);
                q3 = Get(periodAmounts, 3);
                q4 = Get(periodAmounts, 4);
                break;

            case WfpFrequency.BiAnnual:
                q1 = Get(periodAmounts, 1); // 1st Half -> Q1
                q3 = Get(periodAmounts, 2); // 2nd Half -> Q3
                break;

            case WfpFrequency.Annual:
                decimal amount = Get(periodAmounts, 1);
                switch (annualQuarterChoice ?? 1)
                {
                    case 2: q2 = amount; break;
                    case 3: q3 = amount; break;
                    case 4: q4 = amount; break;
                    default: q1 = amount; break; // 1 or unset -> Q1
                }
                break;

            default:
                throw new ArgumentException($"Unknown WFP frequency '{frequency}'.", nameof(frequency));
        }

        decimal net = q1 + q2 + q3 + q4;
        decimal total = net + reserveAmount;
        return new RollUp(q1, q2, q3, q4, net, total);
    }

    /// <summary>
    /// Merges typed period amounts with Σ(qty × unitPrice × numberOfDays) per period from
    /// procurement items (RAL-127 added the days factor). Works uniformly for Procurement
    /// (periods empty), Non-Procurement (items empty), or Combined (both present, summed) — no
    /// nature-specific branching (§5.3).
    /// </summary>
    public static Dictionary<int, decimal> MergePeriodAmounts(
        IEnumerable<(int PeriodNo, decimal Amount)> typedPeriods,
        IEnumerable<(int PeriodNo, decimal Qty, decimal UnitPrice, decimal NumberOfDays)> procurementItems)
    {
        Dictionary<int, decimal> merged = new();
        foreach ((int periodNo, decimal amount) in typedPeriods)
            merged[periodNo] = merged.GetValueOrDefault(periodNo) + amount;
        foreach ((int periodNo, decimal qty, decimal unitPrice, decimal numberOfDays) in procurementItems)
            merged[periodNo] = merged.GetValueOrDefault(periodNo) + (qty * unitPrice * numberOfDays);
        return merged;
    }

    /// <summary>Valid 1-based period-number range for a frequency, e.g. Monthly -> (1, 12).</summary>
    public static (int Min, int Max) PeriodRange(string frequency) => frequency switch
    {
        WfpFrequency.Monthly   => (1, 12),
        WfpFrequency.Quarterly => (1, 4),
        WfpFrequency.BiAnnual  => (1, 2),
        WfpFrequency.Annual    => (1, 1),
        _                      => (1, 0), // empty range signals "invalid frequency" to callers
    };

    private static decimal Get(IReadOnlyDictionary<int, decimal> amounts, int periodNo)
        => amounts.TryGetValue(periodNo, out decimal v) ? v : 0m;

    private static decimal Sum(IReadOnlyDictionary<int, decimal> amounts, params int[] periodNos)
    {
        decimal sum = 0m;
        foreach (int p in periodNos) sum += Get(amounts, p);
        return sum;
    }
}
