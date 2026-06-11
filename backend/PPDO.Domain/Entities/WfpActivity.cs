namespace PPDO.Domain.Entities;

/// <summary>
/// One AIP activity included in a WFP. Unique per (WfpId, AipActivityId).
/// Expenditure detail lives in <see cref="WfpExpenditureLine"/> rows under this activity.
/// </summary>
public sealed class WfpActivity
{
    /// <summary>Primary key (INT IDENTITY).</summary>
    public int Id { get; set; }

    /// <summary>FK to the parent WFP record.</summary>
    public int WfpId { get; set; }

    /// <summary>FK to the AIP activity this row mirrors.</summary>
    public int AipActivityId { get; set; }

    // ── Navigation ────────────────────────────────────────────────────────────

    /// <summary>The parent WFP record.</summary>
    public WfpRecord Wfp { get; set; } = null!;

    /// <summary>The AIP activity this row mirrors.</summary>
    public AipActivity AipActivity { get; set; } = null!;

    /// <summary>Expenditure lines entered for this activity.</summary>
    public ICollection<WfpExpenditureLine> ExpenditureLines { get; set; } = new List<WfpExpenditureLine>();
}
