namespace PPDO.Domain.Entities;

/// <summary>
/// AIP hierarchy level 3 — a project (7-segment ref code).
/// RefCode is unique within its parent AIP program.
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

    /// <summary>
    /// True when this project was materialized to hold a line item recorded directly on its
    /// parent program row (RAL-108) — it has no corresponding row of its own in the source file.
    /// </summary>
    public bool IsSynthetic { get; set; }

    // ── Navigation ────────────────────────────────────────────────────────────

    /// <summary>The parent AIP program.</summary>
    public AipProgram Program { get; set; } = null!;

    /// <summary>Level-4 activities under this project.</summary>
    public ICollection<AipActivity> Activities { get; set; } = new List<AipActivity>();
}
