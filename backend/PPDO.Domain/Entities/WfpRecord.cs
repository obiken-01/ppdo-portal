namespace PPDO.Domain.Entities;

/// <summary>
/// Work and Financial Plan record — one WFP per office per AIP record
/// (enforced by a unique index on AipRecordId + OfficeId).
/// An AIP contains activities for all offices; a WFP is scoped to exactly one office.
/// Status workflow: Draft / Final.
/// </summary>
public sealed class WfpRecord
{
    /// <summary>Primary key (INT IDENTITY).</summary>
    public int Id { get; set; }

    /// <summary>FK to the AIP record this WFP is built from.</summary>
    public int AipRecordId { get; set; }

    /// <summary>FK to the office this WFP is scoped to.</summary>
    public int OfficeId { get; set; }

    /// <summary>Fiscal year — e.g. 2027.</summary>
    public int FiscalYear { get; set; }

    /// <summary>"Draft" (editable) or "Final" (locked).</summary>
    public string Status { get; set; } = "Draft";

    /// <summary>FK to the user who created this record.</summary>
    public Guid CreatedById { get; set; }

    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    /// <summary>UTC timestamp when the WFP was finalized. Null while Draft.</summary>
    public DateTime? FinalizedAt { get; set; }

    /// <summary>FK to the WFP record this one was copied from (amendment/supplemental flow). Null for originals.</summary>
    public int? SourceId { get; set; }

    // ── Navigation ────────────────────────────────────────────────────────────

    /// <summary>The AIP record this WFP is built from.</summary>
    public AipRecord AipRecord { get; set; } = null!;

    /// <summary>The office this WFP is scoped to.</summary>
    public Office Office { get; set; } = null!;

    /// <summary>The user who created this record.</summary>
    public User? CreatedBy { get; set; }

    /// <summary>The original record this one was copied from. Null for originals.</summary>
    public WfpRecord? Source { get; set; }

    /// <summary>Activities included in this WFP.</summary>
    public ICollection<WfpActivity> Activities { get; set; } = new List<WfpActivity>();
}
