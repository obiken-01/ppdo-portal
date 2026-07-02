namespace PPDO.Domain.Entities;

/// <summary>
/// LDIP hierarchy level 1 (RAL-61) — one office/sub-office grouping of programs under
/// an LDIP document, mirroring <see cref="AipOffice"/>. RefCode is the office-level
/// AIP ref code for the group's sector (e.g. "1000-000-1-01-010" = General + office
/// ref code "01-010").
///
/// A sector may hold MULTIPLE groups sharing that same ref code, distinguished by
/// Name (e.g. "PGO - WARDEN" / "PGO - AKAP-HUB" / "PGO - HOUSING" all under
/// 3000-000-1-01-001) — matching how real AIP files are encoded. Group identity
/// within a record is the (Sector, Name) pair.
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
