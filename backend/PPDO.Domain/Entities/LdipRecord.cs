namespace PPDO.Domain.Entities;

/// <summary>
/// Local Development Investment Program record.
/// Created in v1.1 batch 1 but not actively used — AIP carries a nullable FK to it
/// for future linkage. Status workflow: Draft / Final / Archived.
/// </summary>
public sealed class LdipRecord
{
    /// <summary>Primary key (INT IDENTITY).</summary>
    public int Id { get; set; }

    /// <summary>System-generated unique reference code. Max 50 characters.</summary>
    public string RefCode { get; set; } = string.Empty;

    /// <summary>Document title. Max 500 characters.</summary>
    public string Title { get; set; } = string.Empty;

    /// <summary>First fiscal year covered — e.g. 2027.</summary>
    public int FiscalYearStart { get; set; }

    /// <summary>Last fiscal year covered — e.g. 2029.</summary>
    public int FiscalYearEnd { get; set; }

    /// <summary>"New", "Amendment", or "Supplemental". Max 20 characters.</summary>
    public string EntryMode { get; set; } = string.Empty;

    /// <summary>"Draft" (editable), "Final" (locked), or "Archived" (superseded).</summary>
    public string Status { get; set; } = "Draft";

    /// <summary>FK to the LDIP record this one was copied from (amendment/supplemental flow). Null for originals.</summary>
    public int? SourceId { get; set; }

    /// <summary>FK to the user who created this record.</summary>
    public Guid CreatedById { get; set; }

    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    // ── Navigation ────────────────────────────────────────────────────────────

    /// <summary>The original record this one was copied from. Null for originals.</summary>
    public LdipRecord? Source { get; set; }

    /// <summary>The user who created this record.</summary>
    public User? CreatedBy { get; set; }
}
