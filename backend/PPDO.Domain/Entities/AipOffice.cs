namespace PPDO.Domain.Entities;

/// <summary>
/// AIP hierarchy level 1 — an office grouping (5-segment ref code).
/// RefCode is unique within its parent AIP record.
///
/// <para>
/// <b>Ownership is a real FK</b> (<see cref="OfficeId"/>, v1.8.0 Phase 2 — V18-32). Until then,
/// every ownership question in the codebase was answered by
/// <c>RefCode.EndsWith(office.OfficeRefCode)</c> — AIP scoping, the dashboard's office rollups and
/// the office readiness panel all worked by a coincidence of how the province formats a ref code.
/// Phase 4 puts submission and review on top of ownership, and string matching is not a foundation
/// for that.
/// </para>
///
/// <para>
/// <see cref="RefCode"/> <b>stays.</b> It remains the AIP-side re-link key — a re-upload recreates
/// this whole hierarchy with new surrogate ids, and the ref code is what re-attaches it — and the
/// record of what <see cref="OfficeId"/> was backfilled from. Reads match on
/// <see cref="OfficeId"/>. This is the same division of labour <see cref="ProgramDivision"/>
/// settled on in RAL-249.
/// </para>
/// </summary>
public sealed class AipOffice
{
    /// <summary>Primary key (INT IDENTITY).</summary>
    public int Id { get; set; }

    /// <summary>FK to the parent AIP record.</summary>
    public int AipRecordId { get; set; }

    /// <summary>5-segment AIP reference code. Max 50 characters.</summary>
    public string RefCode { get; set; } = string.Empty;

    /// <summary>Office name as it appears in the AIP. Max 500 characters.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>"General", "Social", "Economic", or "Others". Max 20 characters.</summary>
    public string Sector { get; set; } = string.Empty;

    /// <summary>
    /// FK to the config <c>offices</c> row that owns this AIP office (V18-32).
    ///
    /// <para>
    /// ⚠️ <b>Nullable on purpose.</b> An office that was never configured has nothing to match, and
    /// the backfill cannot invent one. <c>NOT NULL</c> would force the migration to either fabricate
    /// an owner or fail outright, and both are worse than a null that gets reported — an unmatched
    /// row keeps its data and stays findable. <see cref="ProgramDivision.OfficeId"/> made the same
    /// call for the same reason. <b>Rows created after this ticket always set it</b>; a null on a
    /// new row means the resolver failed and the row is invisible to every scoped read.
    /// </para>
    /// </summary>
    public int? OfficeId { get; set; }

    // ── Navigation ────────────────────────────────────────────────────────────

    /// <summary>The parent AIP record.</summary>
    public AipRecord AipRecord { get; set; } = null!;

    /// <summary>The config office that owns this row. Null only for an unmatched legacy row.</summary>
    public Office? Office { get; set; }

    /// <summary>Level-2 programs under this office.</summary>
    public ICollection<AipProgram> Programs { get; set; } = new List<AipProgram>();
}
