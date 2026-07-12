namespace PPDO.Domain.Entities;

/// <summary>
/// One typed period amount under a WFP expenditure (v1.4 WFP Rework — RAL-120).
/// Captures exactly what the user typed at the grain they typed it (12 months, 4 quarters,
/// 2 halves, or 1 annual figure — see <c>WfpFrequency</c>) so re-opening an entry never
/// loses the original breakdown to a lossy quarter split. Quarters/Net/Total are always
/// derived from these rows by <c>WfpExpenditureCalculator</c>, never stored here.
/// </summary>
public sealed class WfpExpenditurePeriod
{
    /// <summary>Primary key (INT IDENTITY).</summary>
    public int Id { get; set; }

    /// <summary>FK to the parent expenditure.</summary>
    public int ExpenditureId { get; set; }

    /// <summary>
    /// 1-based period number within the expenditure's frequency:
    /// 1–12 for Monthly, 1–4 for Quarterly, 1–2 for Bi-annual (1st/2nd half), 1 for Annual.
    /// </summary>
    public int PeriodNo { get; set; }

    /// <summary>The amount typed for this period, in pesos.</summary>
    public decimal Amount { get; set; }

    // ── Navigation ────────────────────────────────────────────────────────────

    /// <summary>The parent expenditure.</summary>
    public WfpExpenditure Expenditure { get; set; } = null!;
}
