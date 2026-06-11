namespace PPDO.Domain.Entities;

/// <summary>
/// Config table: a provincial government office (e.g. PPDO, PGO).
/// Soft delete via IsActive only — never hard-delete a referenced office.
/// Seeded manually via the Office Config page CSV upload (RAL-72).
/// </summary>
public sealed class Office
{
    /// <summary>Primary key (INT IDENTITY).</summary>
    public int Id { get; set; }

    /// <summary>Short unique office code — e.g. "PPDO", "PGO". Max 20 characters.</summary>
    public string OfficeCode { get; set; } = string.Empty;

    /// <summary>Full office name. Max 200 characters.</summary>
    public string OfficeName { get; set; } = string.Empty;

    /// <summary>Soft-delete flag. Inactive offices are hidden from pickers but kept for history.</summary>
    public bool IsActive { get; set; } = true;

    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    // ── Navigation ────────────────────────────────────────────────────────────

    /// <summary>WFP records scoped to this office.</summary>
    public ICollection<WfpRecord> WfpRecords { get; set; } = new List<WfpRecord>();
}
