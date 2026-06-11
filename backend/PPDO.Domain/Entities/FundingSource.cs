namespace PPDO.Domain.Entities;

/// <summary>
/// Config table: a budget funding source (e.g. GF, GAD, LDRRMF).
/// Soft delete via IsActive only — never hard-delete a referenced funding source.
/// Seeded manually via the Funding Source Config page CSV upload (RAL-73).
/// </summary>
public sealed class FundingSource
{
    /// <summary>Primary key (INT IDENTITY).</summary>
    public int Id { get; set; }

    /// <summary>Short unique code — e.g. "GF", "GAD", "LDRRMF". Max 20 characters.</summary>
    public string Code { get; set; } = string.Empty;

    /// <summary>Display name. Max 100 characters.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Optional free-text description.</summary>
    public string? Description { get; set; }

    /// <summary>Soft-delete flag. Inactive sources are hidden from pickers but kept for history.</summary>
    public bool IsActive { get; set; } = true;

    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}
