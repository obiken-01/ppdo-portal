namespace PPDO.Domain.Entities;

/// <summary>
/// AIP hierarchy level 2 — a program (6-segment ref code).
/// RefCode is unique within its parent AIP office.
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

    // ── Navigation ────────────────────────────────────────────────────────────

    /// <summary>The parent AIP office.</summary>
    public AipOffice Office { get; set; } = null!;

    /// <summary>Level-3 projects under this program.</summary>
    public ICollection<AipProject> Projects { get; set; } = new List<AipProject>();
}
