namespace PPDO.Domain.Entities;

/// <summary>
/// Annual Investment Program record. Independent from LDIP (optional FK only). The
/// office/program/project/activity hierarchy hangs off this record via <see cref="AipOffice"/>.
/// Status workflow: Draft / Final / Archived.
///
/// <para>
/// <b>One record per fiscal year, holding every office</b> (PPDO-61, 2026-09-05). An Admin opens
/// the year, which creates this record and populates each office's programs from its own LDIP;
/// the offices then build their own <see cref="AipOffice"/> subtree.
/// </para>
///
/// <para>
/// ↩️ V18-40 briefly added an <c>OfficeId</c> here, so that FY≥2028 could hold one record per
/// office. That was withdrawn before it ever reached production. <b>Office identity lives on
/// <see cref="AipOffice"/>, and only there</b> — which is what every scoped read already filters
/// on (<c>AipReadScope</c>). Do not reintroduce an owner on this row: two carriers of the same
/// fact is how they drift apart.
/// </para>
///
/// <para>
/// ⚠️ <b>PPDO is an ordinary office here</b> (tracker B12-b, 2026-08-26). No per-division AIP
/// records, no division column on <see cref="AipOffice"/>, and divisions never print. Division of
/// work is carried on the <i>program</i>, through <c>ProgramDivision</c>, exactly as WFP does. The
/// tempting alternative — one record per PPDO division — would make PPDO structurally different
/// from all 18 other offices, and every downstream feature would carry two code paths forever.
/// </para>
///
/// <para>
/// A sub-unit that genuinely does print is an <see cref="AipOffice"/> row <b>sharing the office
/// ref code</b>, distinguished by <c>(Sector, Name)</c> — already built, and how the province
/// actually encodes (Phase_Plan §12.6).
/// </para>
/// </summary>
public sealed class AipRecord
{
    /// <summary>Primary key (INT IDENTITY).</summary>
    public int Id { get; set; }

    /// <summary>Fiscal year — e.g. 2027.</summary>
    public int FiscalYear { get; set; }

    /// <summary>"Upload" or "Manual". Max 10 characters.</summary>
    public string EntrySource { get; set; } = string.Empty;

    /// <summary>Original uploaded file name. Set only when EntrySource = "Upload". Max 500 characters.</summary>
    public string? OriginalFilename { get; set; }

    /// <summary>FK to the user who uploaded/created this record.</summary>
    public Guid UploadedById { get; set; }

    /// <summary>UTC timestamp of upload/creation.</summary>
    public DateTime UploadedAt { get; set; }

    /// <summary>"Draft" (editable), "Final" (locked), or "Archived" (superseded).</summary>
    public string Status { get; set; } = "Draft";

    /// <summary>Optional FK to the LDIP this AIP implements. Reserved for future use.</summary>
    public int? LdipId { get; set; }

    /// <summary>FK to the AIP record this one was copied from (amendment/supplemental flow). Null for originals.</summary>
    public int? SourceId { get; set; }

    // ── Navigation ────────────────────────────────────────────────────────────

    /// <summary>The user who uploaded/created this record.</summary>
    public User? UploadedBy { get; set; }

    /// <summary>The LDIP this AIP implements. Null in batch 1.</summary>
    public LdipRecord? Ldip { get; set; }

    /// <summary>The original record this one was copied from. Null for originals.</summary>
    public AipRecord? Source { get; set; }

    /// <summary>Level-1 office groupings under this AIP.</summary>
    public ICollection<AipOffice> Offices { get; set; } = new List<AipOffice>();

    /// <summary>WFP records built from this AIP (one per office).</summary>
    public ICollection<WfpRecord> WfpRecords { get; set; } = new List<WfpRecord>();
}
