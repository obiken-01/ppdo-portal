namespace PPDO.Application.Common;

/// <summary>
/// String constants for the audit_log.action column (max 10 chars, matches DB constraint).
///
/// <para>
/// <b>⚠️ Ten characters is a hard ceiling</b> — <c>nvarchar(10)</c>, set in
/// <c>AuditLogConfiguration</c>. A longer value does not fail a compile or a unit test against
/// the in-memory provider; it fails at the moment of a real write to SQL Server, which is the
/// worst place to find out. Every constant here is counted.
/// </para>
/// </summary>
public static class AuditAction
{
    public const string Create = "CREATE";
    public const string Update = "UPDATE";
    public const string Delete = "DELETE";

    // ── AIP workflow transitions (v1.8.0 Phase 4 — AIP_Review_Spec.md §5.2) ───
    //
    // The workflow is recorded here rather than in a table of its own: AuditLog already carries
    // who/when/before/after, and AIP already writes to it. What a second history table would add
    // is a second thing to keep in step.
    //
    // ⚠️ They are named actions rather than plain UPDATE deliberately. A transition logged as
    // UPDATE is indistinguishable from any other column change without parsing the JSON, which
    // would make "Show History" (PPDO-77) a JSON scan over every audit row for the office.

    /// <summary>Encoder submitted the office's work for department review (Draft → DepartmentReview).</summary>
    public const string SubmitToDeptHead = "SUBMIT_DH";

    /// <summary>Department head sent the office's work on to PPDO (→ SubmittedToPpdo).</summary>
    public const string SubmitToPpdo = "SUBMIT_PPD";

    /// <summary>
    /// A PPDO consolidated reviewer sent the office's work back for changes (→ ReturnedByPpdo).
    /// PPDO-72.
    ///
    /// ⚠️ Exactly ten characters, which is the whole budget. The spelling was reserved here by
    /// PPDO-69 rather than left to this ticket precisely so it could not be invented a second time
    /// as something longer.
    /// </summary>
    public const string ReturnByPpdo = "RETURN_PPD";

    /// <summary>
    /// A PPDO consolidated reviewer accepted the office into the consolidated AIP
    /// (→ Consolidated). PPDO-74.
    ///
    /// ⚠️ Ten characters exactly, and the spelling PPDO-69 reserved here in advance — declared now
    /// that the transition exists. It is the terminal action in the chain
    /// <c>SUBMIT_DH → SUBMIT_PPD → (RETURN_PPD ⇄ SUBMIT_PPD) → ACCEPT_PPD</c> that PPDO-77 reads
    /// back as history.
    /// </summary>
    public const string AcceptByPpdo = "ACCEPT_PPD";

    /// <summary>
    /// A PPDO consolidated reviewer re-opened an accepted office and sent it back
    /// (Consolidated → ReturnedByPpdo). Added 2026-09-14 with PPDO-73.
    ///
    /// ⚠️ Ten characters exactly. <b>Its own action, not <see cref="ReturnByPpdo"/></b>: History has
    /// to say that accepted work was re-opened, and the return path's 409 on <c>Consolidated</c> is
    /// what stops a reviewer on a stale screen re-opening an office a colleague has just accepted.
    /// </summary>
    public const string ReopenByPpdo = "REOPEN_PPD";

    /// <summary>
    /// The department head handed the office's work back down to its encoders
    /// (DepartmentReview → Draft). Added 2026-09-14 with PPDO-73. Nine characters.
    /// </summary>
    public const string ReturnToEncoder = "RETURN_DH";

    // ── Division submit (v1.8.0 — PPDO-130, Division_Submit_Spec.md) ──────────
    //
    // ⚠️ Declared here by T1 (PPDO-147) so the spellings are fixed before anything writes them.
    //
    // ↩️ Decided by T3 (PPDO-149): they are NOT in AipHandOffs. That list drives History and the
    // returned-work notice (PPDO-75, PPDO-77), both of which read `aip_offices` rows. The office
    // still writes its own hand-off row whenever ITS state moves — SUBMIT_DH when the last
    // division submits, RETURN_DH when a division return takes it out of review — so History
    // stays one row per office transition. These two are written against
    // `aip_division_submissions` rows and are the per-division record.

    /// <summary>A division submitted its work to the department head (Draft → Submitted). Ten characters.</summary>
    public const string SubmitDivision = "SUBMIT_DIV";

    /// <summary>
    /// A division's work was handed back (Submitted → Draft), by the department head or as part of
    /// a PPDO return (spec decision 11). Ten characters.
    /// </summary>
    public const string ReturnDivision = "RETURN_DIV";

    /// <summary>The department head moved an activity to another division. Nine characters.</summary>
    public const string RetagActivityDivision = "RETAG_DIV";

    /// <summary>
    /// Every AIP workflow hand-off, in one place (PPDO-77 read them for History; PPDO-75 reads the
    /// latest one for the returned notice). Add a new hand-off here, or both reads miss it.
    /// </summary>
    public static readonly IReadOnlyList<string> AipHandOffs =
    [
        SubmitToDeptHead, SubmitToPpdo, ReturnByPpdo, AcceptByPpdo, ReopenByPpdo, ReturnToEncoder,
    ];
}
