namespace PPDO.Domain.Entities;

/// <summary>
/// One division's position in the division-grained submit to its department head, for one AIP
/// record (v1.8.0 — PPDO-130, <c>docs/v1.8/Division_Submit_Spec.md</c> §5).
///
/// <para>
/// ⚠️ <b>No row means Draft.</b> Rows are created the first time a division submits, so opening a
/// fiscal year writes nothing here, and a division that never submits never has a row. Every read
/// must treat an absent row exactly like <c>Status = Draft</c>.
/// </para>
///
/// <para>
/// This is <b>not</b> an office workflow state. <c>aip_offices.workflow_status</c> stays the
/// office's state and is derived from these rows (spec decision 9). No new office states are added.
/// </para>
///
/// <para>
/// Unique on (<see cref="AipRecordId"/>, <see cref="DivisionId"/>). A division belongs to exactly
/// one office, so <see cref="OfficeId"/> is implied by the division. It is stored anyway so the
/// office-grained read ("every division of this office in this record") is one indexed query.
/// </para>
/// </summary>
public sealed class AipDivisionSubmission
{
    /// <summary>Primary key (INT IDENTITY).</summary>
    public int Id { get; set; }

    /// <summary>FK to the AIP record (fiscal year).</summary>
    public int AipRecordId { get; set; }

    /// <summary>FK to the config <c>offices</c> row the division belongs to.</summary>
    public int OfficeId { get; set; }

    /// <summary>FK to the division.</summary>
    public int DivisionId { get; set; }

    /// <summary>
    /// <c>Draft</c> or <c>Submitted</c> (<c>AipDivisionStatus</c>). A returned division goes back
    /// to <c>Draft</c>. The return is recorded in <see cref="ReturnedAt"/> and the audit log, not as
    /// a third state.
    /// </summary>
    public string Status { get; set; } = "Draft";

    /// <summary>UTC time of the latest submit. Kept after a return, for "last submitted".</summary>
    public DateTime? SubmittedAt { get; set; }

    /// <summary>Who submitted it last: the division's encoder or, on their behalf, the department head.</summary>
    public Guid? SubmittedById { get; set; }

    /// <summary>UTC time of the latest return, whether by the department head or a PPDO return.</summary>
    public DateTime? ReturnedAt { get; set; }

    /// <summary>Who returned it last.</summary>
    public Guid? ReturnedById { get; set; }

    // ── Navigation ────────────────────────────────────────────────────────────

    public AipRecord? AipRecord { get; set; }
    public Office?    Office    { get; set; }
    public Division?  Division  { get; set; }
    public User?      SubmittedBy { get; set; }
    public User?      ReturnedBy  { get; set; }
}
