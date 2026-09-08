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

    // ⚠️ Reserved for the tickets that add those transitions, so they are not invented twice with
    // different spellings — PPDO-72 (return) and PPDO-74 (accept):
    //     RETURN_PPD   PPDO returned the office's work    (→ ReturnedByPpdo)
    //     ACCEPT_PPD   PPDO accepted it into the AIP      (→ Consolidated)
    // Not declared until used; an unused constant reads as a feature that exists.
}
