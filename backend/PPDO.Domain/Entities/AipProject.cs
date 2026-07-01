namespace PPDO.Domain.Entities;

/// <summary>
/// AIP hierarchy level 3 — a project (7-segment ref code).
/// RefCode is unique within its parent AIP program.
///
/// RAL-108: some projects carry their own line-item detail directly on the
/// project row with no child activity — the same optional fields as
/// <see cref="AipActivity"/> are mirrored here (all null on a normal project
/// that only groups activities).
/// </summary>
public sealed class AipProject
{
    /// <summary>Primary key (INT IDENTITY).</summary>
    public int Id { get; set; }

    /// <summary>FK to the parent AIP program (level 2).</summary>
    public int ProgramId { get; set; }

    /// <summary>7-segment AIP reference code. Max 50 characters.</summary>
    public string RefCode { get; set; } = string.Empty;

    /// <summary>Project name. Max 500 characters.</summary>
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

    // ── Navigation ────────────────────────────────────────────────────────────

    /// <summary>The parent AIP program.</summary>
    public AipProgram Program { get; set; } = null!;

    /// <summary>Level-4 activities under this project.</summary>
    public ICollection<AipActivity> Activities { get; set; } = new List<AipActivity>();

    /// <summary>The funding source config record. Null when unmatched.</summary>
    public FundingSource? FundingSource { get; set; }
}
