namespace PPDO.Domain.Entities;

/// <summary>
/// AIP hierarchy level 2 — a program (6-segment ref code).
/// RefCode is unique within its parent AIP office.
///
/// RAL-108: some programs (e.g. the Provincial Legal Office's
/// "1000-000-1-01-011-004") carry their own line-item detail directly on the
/// program row with no child project — the same optional fields as
/// <see cref="AipActivity"/> are mirrored here (all null on a normal program
/// that only groups projects).
/// </summary>
public sealed class AipProgram
{
    /// <summary>Primary key (INT IDENTITY).</summary>
    public int Id { get; set; }

    /// <summary>FK to the parent AIP office (level 1).</summary>
    public int OfficeId { get; set; }

    /// <summary>6-segment AIP reference code. Max 50 characters.</summary>
    public string RefCode { get; set; } = string.Empty;

    /// <summary>Program name. Max 500 characters.</summary>
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

    /// <summary>The parent AIP office.</summary>
    public AipOffice Office { get; set; } = null!;

    /// <summary>Level-3 projects under this program.</summary>
    public ICollection<AipProject> Projects { get; set; } = new List<AipProject>();

    /// <summary>The funding source config record. Null when unmatched.</summary>
    public FundingSource? FundingSource { get; set; }
}
