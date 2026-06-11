namespace PPDO.Domain.Entities;

/// <summary>
/// AIP hierarchy level 1 — an office grouping (5-segment ref code).
/// RefCode is unique within its parent AIP record.
/// </summary>
public sealed class AipOffice
{
    /// <summary>Primary key (INT IDENTITY).</summary>
    public int Id { get; set; }

    /// <summary>FK to the parent AIP record.</summary>
    public int AipRecordId { get; set; }

    /// <summary>5-segment AIP reference code. Max 50 characters.</summary>
    public string RefCode { get; set; } = string.Empty;

    /// <summary>Office name as it appears in the AIP. Max 500 characters.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>"General", "Social", "Economic", or "Others". Max 20 characters.</summary>
    public string Sector { get; set; } = string.Empty;

    // ── Navigation ────────────────────────────────────────────────────────────

    /// <summary>The parent AIP record.</summary>
    public AipRecord AipRecord { get; set; } = null!;

    /// <summary>Level-2 programs under this office.</summary>
    public ICollection<AipProgram> Programs { get; set; } = new List<AipProgram>();
}
