namespace PPDO.Domain.Entities;

/// <summary>
/// One expenditure line inside a WFP activity (entered via the per-activity popup modal).
/// Account and funding source snapshots preserve the config display values at save time
/// so historical records stay accurate if config is edited later.
/// Business rule (enforced in Application layer, not the DB): QuarterlyTotal
/// (Q1+Q2+Q3+Q4) must not exceed NetAppropriation.
/// </summary>
public sealed class WfpExpenditureLine
{
    /// <summary>Primary key (INT IDENTITY).</summary>
    public int Id { get; set; }

    /// <summary>FK to the parent WFP activity.</summary>
    public int WfpActivityId { get; set; }

    /// <summary>"PS", "MOOE", or "CO". Max 10 characters.</summary>
    public string ExpenditureType { get; set; } = string.Empty;

    /// <summary>Resources needed free text. Typically filled on the first line of an activity.</summary>
    public string? ResourcesNeeded { get; set; }

    /// <summary>Responsible unit. Max 200 characters.</summary>
    public string? ResponsibleUnit { get; set; }

    /// <summary>Success indicator free text.</summary>
    public string? SuccessIndicator { get; set; }

    /// <summary>Means of verification free text.</summary>
    public string? MeansOfVerification { get; set; }

    /// <summary>FK to the Chart of Accounts config record. Null when not selected.</summary>
    public int? AccountId { get; set; }

    /// <summary>Snapshot of Account.AccountNumber at save time. Max 20 characters.</summary>
    public string? AccountNumberSnapshot { get; set; }

    /// <summary>Snapshot of Account.AccountTitle at save time. Max 300 characters.</summary>
    public string? AccountTitleSnapshot { get; set; }

    /// <summary>Total appropriation for this line.</summary>
    public decimal? TotalAppropriation { get; set; }

    /// <summary>Whether the 10% reserve is applied to this line.</summary>
    public bool ApplyReserve { get; set; }

    /// <summary>Auto-computed: 10% of TotalAppropriation when ApplyReserve is set.</summary>
    public decimal? ReserveAmount { get; set; }

    /// <summary>Auto-computed: TotalAppropriation − ReserveAmount.</summary>
    public decimal? NetAppropriation { get; set; }

    /// <summary>Quarter 1 allocation.</summary>
    public decimal? Q1 { get; set; }

    /// <summary>Quarter 2 allocation.</summary>
    public decimal? Q2 { get; set; }

    /// <summary>Quarter 3 allocation.</summary>
    public decimal? Q3 { get; set; }

    /// <summary>Quarter 4 allocation.</summary>
    public decimal? Q4 { get; set; }

    /// <summary>Auto-computed: Q1 + Q2 + Q3 + Q4.</summary>
    public decimal? QuarterlyTotal { get; set; }

    /// <summary>FK to the funding source config record. Null when not selected.</summary>
    public int? FundingSourceId { get; set; }

    /// <summary>Snapshot of FundingSource.Code at save time. Max 20 characters.</summary>
    public string? FundingSourceSnapshot { get; set; }

    /// <summary>Preserves the user-defined row order within the activity popup.</summary>
    public int SortOrder { get; set; }

    // ── Navigation ────────────────────────────────────────────────────────────

    /// <summary>The parent WFP activity.</summary>
    public WfpActivity WfpActivity { get; set; } = null!;

    /// <summary>The Chart of Accounts config record. Null when not selected.</summary>
    public Account? Account { get; set; }

    /// <summary>The funding source config record. Null when not selected.</summary>
    public FundingSource? FundingSource { get; set; }
}
