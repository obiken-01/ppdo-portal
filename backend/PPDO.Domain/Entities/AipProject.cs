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
    /// What the project is, in the encoder's own words (PPDO-99, asked for at the 2026-09-15 PDC
    /// demo). Optional, unbounded free text, null when never filled in.
    /// <para>
    /// ⚠️ <b>Not printed on Annex B.</b> The form's columns are fixed by the province's template;
    /// this and <see cref="Objective"/> feed a separate report that is not yet specified. Adding
    /// either to <c>AipFormRowBuilder</c> would put a column on the sheet that the province does
    /// not have.
    /// </para>
    /// </summary>
    public string? Description { get; set; }

    /// <summary>
    /// What the project is meant to achieve (PPDO-99). Optional, unbounded free text. Same
    /// non-printing caveat as <see cref="Description"/>.
    /// </summary>
    public string? Objective { get; set; }

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
