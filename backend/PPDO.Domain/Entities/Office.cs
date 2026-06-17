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

    /// <summary>
    /// Last segment of the office-level AIP ref code (e.g. "013" from "3000-000-1-01-013").
    /// Used to match this config office to the correct AIP office hierarchy row.
    /// Nullable — populated manually via CSV download/upload.
    /// </summary>
    public string? OfficeRefCode { get; set; }

    /// <summary>Soft-delete flag. Inactive offices are hidden from pickers but kept for history.</summary>
    public bool IsActive { get; set; } = true;

    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    // ── Navigation ────────────────────────────────────────────────────────────

    /// <summary>WFP records scoped to this office.</summary>
    public ICollection<WfpRecord> WfpRecords { get; set; } = new List<WfpRecord>();

    /// <summary>Non-PPDO users belonging to this office (encoders / viewers). Added in RAL-81.</summary>
    public ICollection<User> Users { get; set; } = new List<User>();
}
