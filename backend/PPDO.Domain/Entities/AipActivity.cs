namespace PPDO.Domain.Entities;

/// <summary>
/// AIP hierarchy level 4 (leaf) — an activity (8-segment ref code).
/// RefCode is unique within its parent AIP project.
/// StartDate / EndDate are stored as strings because the source file uses
/// month names ("January", "December"), not proper dates.
/// FundingSourceSnapshot preserves the funding source code at import time so
/// historical records stay accurate if config is edited later.
/// </summary>
public sealed class AipActivity
{
    /// <summary>Primary key (INT IDENTITY).</summary>
    public int Id { get; set; }

    /// <summary>FK to the parent AIP project (level 3).</summary>
    public int ProjectId { get; set; }

    /// <summary>8-segment AIP reference code. Max 50 characters.</summary>
    public string RefCode { get; set; } = string.Empty;

    /// <summary>Activity description. Max 1000 characters.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>ESRE classification code — "SS", "ES", "ID", or "EN". Max 10 characters.</summary>
    public string? EsreCode { get; set; }

    /// <summary>Implementing office name as written in the AIP. Max 200 characters.</summary>
    public string? ImplementingOffice { get; set; }

    /// <summary>Schedule start — stored as string (e.g. "January"). Max 50 characters.</summary>
    public string? StartDate { get; set; }

    /// <summary>Schedule end — stored as string (e.g. "December"). Max 50 characters.</summary>
    public string? EndDate { get; set; }

    /// <summary>Expected outputs free text.</summary>
    public string? ExpectedOutputs { get; set; }

    /// <summary>FK to the funding source config record. Null when unmatched at import.</summary>
    public int? FundingSourceId { get; set; }

    /// <summary>Snapshot of FundingSource.Code at import time. Max 20 characters.</summary>
    public string? FundingSourceSnapshot { get; set; }

    /// <summary>Personal Services amount.</summary>
    public decimal? Ps { get; set; }

    /// <summary>Maintenance and Other Operating Expenses amount.</summary>
    public decimal? Mooe { get; set; }

    /// <summary>Capital Outlay amount.</summary>
    public decimal? Co { get; set; }

    /// <summary>Auto-computed: Ps + Mooe + Co. Stored to match source file and for faster queries.</summary>
    public decimal? Total { get; set; }

    /// <summary>Climate change adaptation amount.</summary>
    public decimal? CcAdaptation { get; set; }

    /// <summary>Climate change mitigation amount.</summary>
    public decimal? CcMitigation { get; set; }

    /// <summary>Climate change typology code. Max 50 characters.</summary>
    public string? CcTypologyCode { get; set; }

    /// <summary>
    /// True when this activity was materialized to hold a line item recorded directly on its
    /// parent program or project row (RAL-108) — it has no corresponding row of its own in the
    /// source file. Financial data must always live on an activity so it flows into WFP,
    /// reports, and the external AIP API the same way every other activity does.
    /// </summary>
    public bool IsSynthetic { get; set; }

    // ── Navigation ────────────────────────────────────────────────────────────

    /// <summary>The parent AIP project.</summary>
    public AipProject Project { get; set; } = null!;

    /// <summary>The funding source config record. Null when unmatched.</summary>
    public FundingSource? FundingSource { get; set; }
}
