namespace PPDO.Domain.Entities;

/// <summary>
/// LDIP hierarchy level 2 (RAL-61) — one program row under an office/sub-office
/// group, mirroring <see cref="AipProgram"/>. RefCode is server-computed as the
/// group's ref code + a 3-digit sequence ("…-001", "…-002", …) that runs
/// CONTINUOUSLY across all groups sharing that ref code, and is renumbered on
/// every save so removals never leave gaps — correct AIP ref codes are a hard
/// requirement for the downstream AIP/WFP linkage.
///
/// RAL-113 adds the detail columns below. Unlike AIP (Office → Program → Project
/// → Activity), the real LDIP source file is only a 2-level hierarchy — the
/// 6-segment Program row IS the leaf and carries all the activity-like detail
/// directly (confirmed: every row in the reference workbook is either 5 segments
/// [Office] or 6 segments [Program], never 7 or 8). All new columns are nullable
/// because manually-created programs (the "+ Add Program" flow) only ever set
/// Name/Budget — these columns are populated exclusively by file upload.
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

    /// <summary>
    /// Combined budget for the document's full year range, in thousands (₱000) like AIP totals.
    /// For uploaded programs this is Ps + Mooe + Co (the file's own Total column);
    /// for manually-added programs it is the single value the user entered.
    /// </summary>
    public decimal Budget { get; set; }

    /// <summary>
    /// Implementing office name as written in the file. Upload-only. Unbounded —
    /// can combine multiple offices, e.g. "PGO Housing, PEO".
    /// </summary>
    public string? ImplementingOffice { get; set; }

    /// <summary>
    /// Schedule start — stored as string (the source file uses a bare year, e.g. "2026",
    /// but real data has been seen with stray characters, so this is not parsed as a number).
    /// Max 50 characters. Upload-only.
    /// </summary>
    public string? StartDate { get; set; }

    /// <summary>Schedule end — stored as string (bare year, e.g. "2029"). Max 50 characters. Upload-only.</summary>
    public string? EndDate { get; set; }

    /// <summary>Expected outputs free text. Upload-only.</summary>
    public string? ExpectedOutputs { get; set; }

    /// <summary>FK to the funding source config record. Null when unmatched at import. Upload-only.</summary>
    public int? FundingSourceId { get; set; }

    /// <summary>
    /// Snapshot of FundingSource.Code at import time. Upload-only. Unbounded — the
    /// raw file text can list multiple funding sources, e.g. "GF, LDRRMF, GADF, NGA (ER 1-94)".
    /// </summary>
    public string? FundingSourceSnapshot { get; set; }

    /// <summary>Personal Services amount. Upload-only.</summary>
    public decimal? Ps { get; set; }

    /// <summary>Maintenance and Other Operating Expenses amount. Upload-only.</summary>
    public decimal? Mooe { get; set; }

    /// <summary>Capital Outlay amount. Upload-only.</summary>
    public decimal? Co { get; set; }

    /// <summary>Climate change adaptation amount. Upload-only.</summary>
    public decimal? CcAdaptation { get; set; }

    /// <summary>Climate change mitigation amount. Upload-only.</summary>
    public decimal? CcMitigation { get; set; }

    /// <summary>
    /// Climate change typology code(s). Upload-only. Unbounded — real files hold a
    /// comma/newline-separated list of codes (observed up to ~150 chars), not a
    /// single short code.
    /// </summary>
    public string? CcTypologyCode { get; set; }

    /// <summary>PDP/RDP alignment tag. Unbounded free text. Upload-only. No AIP equivalent.</summary>
    public string? PdpRdp { get; set; }

    /// <summary>SDG alignment tag. Unbounded free text. Upload-only. No AIP equivalent.</summary>
    public string? Sdgs { get; set; }

    /// <summary>Sendai Framework alignment tag. Unbounded free text. Upload-only. No AIP equivalent.</summary>
    public string? SendaiFramework { get; set; }

    /// <summary>NDRRM Plan alignment tag. Unbounded free text. Upload-only. No AIP equivalent.</summary>
    public string? NdrrmPlan { get; set; }

    /// <summary>NSP alignment tag. Unbounded free text. Upload-only. No AIP equivalent.</summary>
    public string? Nsp { get; set; }

    /// <summary>PDPDFP alignment tag. Unbounded free text. Upload-only. No AIP equivalent.</summary>
    public string? Pdpdfp { get; set; }

    // ── Navigation ────────────────────────────────────────────────────────────

    /// <summary>The parent LDIP office (sector group).</summary>
    public LdipOffice Office { get; set; } = null!;

    /// <summary>The funding source config record. Null when unmatched or manually entered.</summary>
    public FundingSource? FundingSource { get; set; }
}
