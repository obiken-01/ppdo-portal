namespace PPDO.Domain.Entities;

/// <summary>
/// Annual Investment Program record. Independent from LDIP (optional FK only). The
/// office/program/project/activity hierarchy hangs off this record via <see cref="AipOffice"/>.
/// Status workflow: Draft / Final / Archived.
///
/// <para>
/// <b>Two shapes live in this table (V18-40 / PPDO-39).</b>
/// </para>
/// <list type="bullet">
///   <item><description>
///     <b>Legacy, multi-office</b> — <see cref="OfficeId"/> null. One record holds
///     <see cref="AipOffice"/> children for every office in the province. This is how every
///     FY≤2027 record was imported, and it is <i>why</i> there was nothing to scope on, why one
///     office could not submit independently of another, and why review had nothing to attach to.
///   </description></item>
///   <item><description>
///     <b>Office-owned</b> — <see cref="OfficeId"/> set. One record per office per fiscal year,
///     exactly like <see cref="LdipRecord"/>. The FY≥2028 shape.
///   </description></item>
/// </list>
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

    /// <summary>
    /// FK to the config office that owns this record (V18-40), or null for a legacy multi-office
    /// record.
    ///
    /// <para>
    /// ⚠️ <b>Null is permanent for legacy rows, not a backfill that has yet to run.</b> That is the
    /// difference from <c>AipOffice.OfficeId</c>, whose nulls are unmatched rows to be resolved. A
    /// pre-FY2028 record genuinely has no single owner — it spans every office — so there is no
    /// value that could be filled in, and no migration between the two shapes (V18-37).
    /// </para>
    /// </summary>
    public int? OfficeId { get; set; }

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

    /// <summary>The owning config office. Null on a legacy multi-office record.</summary>
    public Office? Office { get; set; }

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
