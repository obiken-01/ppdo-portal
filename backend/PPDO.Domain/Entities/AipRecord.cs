namespace PPDO.Domain.Entities;

/// <summary>
/// Annual Investment Program record — one per AIP creation (file upload or manual entry).
/// Independent from LDIP (optional FK only). The office/program/project/activity
/// hierarchy hangs off this record via <see cref="AipOffice"/>.
/// Status workflow: Draft / Final / Archived.
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
