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

    /// <summary>Optional hex colour (#RRGGBB) for WFP report total groups. Null = default green.</summary>
    public string? Color { get; set; }

    /// <summary>
    /// Optional pipe-delimited list of alternate names for this fund source (RAL-157).
    /// Used to map an AIP activity's free-text funding-source label — which has no controlled
    /// vocabulary (e.g. "GAD FUND", "5% GAD", "GAD" all mean the 5% GAD Fund) — to this canonical
    /// record. Matched in addition to <see cref="Code"/> and <see cref="Name"/>. Null when none.
    /// Multi-fund AIP values (slash-separated, e.g. "GF/20% DF") are never resolved via aliases.
    /// </summary>
    public string? Aliases { get; set; }

    /// <summary>Soft-delete flag. Inactive sources are hidden from pickers but kept for history.</summary>
    public bool IsActive { get; set; } = true;

    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}
