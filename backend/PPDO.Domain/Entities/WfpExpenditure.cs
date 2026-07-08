namespace PPDO.Domain.Entities;

/// <summary>
/// One expenditure entry under a WFP activity (v1.4 WFP Rework — RAL-120).
/// Replaces the per-line estimate on <see cref="WfpExpenditureLine"/> with an
/// entry built from either typed period amounts (<see cref="WfpExpenditurePeriod"/>,
/// non-procurement) or line items (<see cref="WfpProcurementItem"/>, procurement) —
/// an expenditure may carry both (Nature = "Combined").
///
/// Q1–Q4/NetAppropriation/TotalAppropriation are always computed server-side by
/// <c>WfpExpenditureCalculator</c> on save — never accepted as client input.
/// Account/funding-source snapshots preserve config display values at save time,
/// matching the existing <see cref="WfpExpenditureLine"/> convention.
/// </summary>
public sealed class WfpExpenditure
{
    /// <summary>Primary key (INT IDENTITY).</summary>
    public int Id { get; set; }

    /// <summary>FK to the parent WFP activity.</summary>
    public int WfpActivityId { get; set; }

    /// <summary>FK to the Chart of Accounts config record. Null when not selected.</summary>
    public int? AccountId { get; set; }

    /// <summary>Snapshot of Account.AccountNumber at save time. Max 20 characters.</summary>
    public string? AccountNumberSnapshot { get; set; }

    /// <summary>Snapshot of Account.AccountTitle at save time. Max 300 characters.</summary>
    public string? AccountTitleSnapshot { get; set; }

    /// <summary>"Procurement", "Non-Procurement", or "Combined" — see <c>WfpNature</c>. Max 20 characters.</summary>
    public string Nature { get; set; } = string.Empty;

    /// <summary>"M", "Q", "B", or "A" — see <c>WfpFrequency</c>. Max 5 characters.</summary>
    public string Frequency { get; set; } = string.Empty;

    /// <summary>FK to the funding source config record. Null when not selected.</summary>
    public int? FundingSourceId { get; set; }

    /// <summary>Snapshot of FundingSource.Code at save time. Max 20 characters.</summary>
    public string? FundingSourceSnapshot { get; set; }

    /// <summary>Snapshot of FundingSource.Name at save time. Max 100 characters.</summary>
    public string? FundingSourceNameSnapshot { get; set; }

    /// <summary>Whether the reserve is applied to this expenditure.</summary>
    public bool ApplyReserve { get; set; }

    /// <summary>
    /// Reserve amount, excluded from the quarterly release plan (Total = Net + Reserved).
    /// Caller-supplied for this ticket — rate default/hard-cap validation is RAL-121's scope.
    /// Forced to 0 when ApplyReserve is false.
    /// </summary>
    public decimal ReserveAmount { get; set; }

    /// <summary>
    /// Which quarter an Annual-frequency amount charges to (1–4). Only meaningful when
    /// Frequency = "A"; null otherwise. Defaults to 1 (Q1) when unset for an Annual entry.
    /// </summary>
    public int? AnnualQuarterChoice { get; set; }

    /// <summary>Computed on save: sum of period amounts rolled up per §2's frequency rules.</summary>
    public decimal Q1 { get; set; }
    public decimal Q2 { get; set; }
    public decimal Q3 { get; set; }
    public decimal Q4 { get; set; }

    /// <summary>Computed on save: Q1 + Q2 + Q3 + Q4 — the NET release plan (reserve excluded).</summary>
    public decimal NetAppropriation { get; set; }

    /// <summary>Computed on save: NetAppropriation + ReserveAmount.</summary>
    public decimal TotalAppropriation { get; set; }

    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    // ── Navigation ────────────────────────────────────────────────────────────

    /// <summary>The parent WFP activity.</summary>
    public WfpActivity WfpActivity { get; set; } = null!;

    /// <summary>The Chart of Accounts config record. Null when not selected.</summary>
    public Account? Account { get; set; }

    /// <summary>The funding source config record. Null when not selected.</summary>
    public FundingSource? FundingSource { get; set; }

    /// <summary>Typed period amounts (non-procurement grain). Empty for a pure-procurement entry.</summary>
    public ICollection<WfpExpenditurePeriod> Periods { get; set; } = new List<WfpExpenditurePeriod>();

    /// <summary>Procurement line items (procurement grain). Empty for a pure non-procurement entry.</summary>
    public ICollection<WfpProcurementItem> ProcurementItems { get; set; } = new List<WfpProcurementItem>();
}
