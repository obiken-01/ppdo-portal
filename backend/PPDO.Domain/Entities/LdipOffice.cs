namespace PPDO.Domain.Entities;

/// <summary>
/// LDIP hierarchy level 1 (RAL-61) — one sector grouping of programs under an LDIP
/// document, mirroring <see cref="AipOffice"/>. The record's office is fixed, so each
/// row corresponds to one sector choice; its RefCode is the office-level AIP ref code
/// for that sector (e.g. "1000-000-1-01-010" = General + office ref code "01-010").
///
/// Name is the office/sub-office display name for this sector group — the same office
/// ref code can legitimately carry a different display name per sector (e.g.
/// "…OFFICE" under General vs "…OFFICE - SPECIAL PROJECTS" under Economic), matching
/// how real AIP files are encoded.
/// </summary>
public sealed class LdipOffice
{
    /// <summary>Primary key (INT IDENTITY).</summary>
    public int Id { get; set; }

    /// <summary>FK to the parent LDIP record.</summary>
    public int LdipRecordId { get; set; }

    /// <summary>Office-level AIP ref code for this sector group. Max 50 characters.</summary>
    public string RefCode { get; set; } = string.Empty;

    /// <summary>Office/sub-office display name for this sector group. Max 500 characters.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>"General", "Social", "Economic", or "Others". Max 20 characters.</summary>
    public string Sector { get; set; } = string.Empty;

    // ── Navigation ────────────────────────────────────────────────────────────

    /// <summary>The parent LDIP record.</summary>
    public LdipRecord LdipRecord { get; set; } = null!;

    /// <summary>Programs under this sector group, in ref-code order.</summary>
    public ICollection<LdipProgram> Programs { get; set; } = new List<LdipProgram>();
}
