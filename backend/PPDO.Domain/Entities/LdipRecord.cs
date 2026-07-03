namespace PPDO.Domain.Entities;

/// <summary>
/// Local Development Investment Program record (one multi-year document per office).
/// v1.3 (RAL-61): office-scoped, with a sector-grouped program hierarchy
/// (<see cref="LdipOffice"/> → <see cref="LdipProgram"/>) mirroring the AIP model.
/// Status workflow: Draft / Final / Archived.
/// </summary>
public sealed class LdipRecord
{
    /// <summary>Primary key (INT IDENTITY).</summary>
    public int Id { get; set; }

    /// <summary>
    /// FK to the config office this LDIP document belongs to (RAL-61).
    /// Nullable in the DB for pre-v1.3 rows; required by the service for new records.
    /// </summary>
    public int? OfficeId { get; set; }

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

    /// <summary>The config office this document belongs to. Null on pre-v1.3 rows.</summary>
    public Office? Office { get; set; }

    /// <summary>The original record this one was copied from. Null for originals.</summary>
    public LdipRecord? Source { get; set; }

    /// <summary>The user who created this record.</summary>
    public User? CreatedBy { get; set; }

    /// <summary>Sector-grouped office rows under this document (RAL-61).</summary>
    public ICollection<LdipOffice> Offices { get; set; } = new List<LdipOffice>();
}
