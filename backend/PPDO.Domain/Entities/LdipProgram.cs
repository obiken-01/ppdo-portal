namespace PPDO.Domain.Entities;

/// <summary>
/// LDIP hierarchy level 2 (RAL-61) — one program row under a sector group,
/// mirroring <see cref="AipProgram"/>. RefCode is server-computed as the parent
/// group's ref code + a contiguous 3-digit sequence ("…-001", "…-002", …) and is
/// renumbered on every save so removals never leave gaps — correct AIP ref codes
/// are a hard requirement for the downstream AIP/WFP linkage.
/// </summary>
public sealed class LdipProgram
{
    /// <summary>Primary key (INT IDENTITY).</summary>
    public int Id { get; set; }

    /// <summary>FK to the parent LDIP office (sector group).</summary>
    public int LdipOfficeId { get; set; }

    /// <summary>Program-level AIP ref code (parent ref code + "-NNN"). Max 50 characters.</summary>
    public string RefCode { get; set; } = string.Empty;

    /// <summary>Program name (PPA nomenclature). Max 500 characters.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Combined budget for the document's full year range, in thousands (₱000) like AIP totals.</summary>
    public decimal Budget { get; set; }

    // ── Navigation ────────────────────────────────────────────────────────────

    /// <summary>The parent LDIP office (sector group).</summary>
    public LdipOffice Office { get; set; } = null!;
}
