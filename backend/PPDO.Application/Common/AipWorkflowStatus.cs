namespace PPDO.Application.Common;

/// <summary>
/// One office's progress through AIP review (v1.8.0 Phase 3 — V18-42 / PPDO-52, spec §5.1).
///
/// <b>⚠️ Distinct from <see cref="PlanningStatus"/>, deliberately, and they mean different things
/// at different levels.</b> <c>aip_records.status</c> is <see cref="PlanningStatus"/>
/// (Draft / Final / Archived) and is the <i>year's</i> state — an Admin opens the year and archives
/// it. <c>aip_offices.workflow_status</c> is <i>one office's</i> progress through review. Reusing
/// <see cref="PlanningStatus"/> here would have changed LDIP's and WFP's meaning too, since all
/// three share it.
///
/// An office cannot be past <see cref="Draft"/> in an archived year; the service owns that rule,
/// not a database constraint.
///
/// <b>⚠️ All five states exist from the first migration, and only the first transition is
/// implemented.</b> Phase 3 writes <see cref="Draft"/> → <see cref="DepartmentReview"/> and nothing
/// else; Phase 4 uses the rest. Adding them piecemeal would mean a second migration on the same
/// column and an interval where the column's domain does not match the documented workflow.
///
/// <b>⚠️ There are two submits, not one.</b> Encoder → department head is this phase's transition.
/// Department head → PPDO is Phase 4's. A single "submitted" state would not distinguish them.
/// </summary>
public static class AipWorkflowStatus
{
    /// <summary>The encoder is still building. The only state in which the office's tree is editable by them.</summary>
    public const string Draft = "Draft";

    /// <summary>Submitted by the encoder; with the office's own department head, who may edit values.</summary>
    public const string DepartmentReview = "DepartmentReview";

    /// <summary>Sent on by the department head to PPDO's consolidated reviewer. Phase 4.</summary>
    public const string SubmittedToPpdo = "SubmittedToPpdo";

    /// <summary>Returned by PPDO for changes. Phase 4.</summary>
    public const string ReturnedByPpdo = "ReturnedByPpdo";

    /// <summary>Taken into the consolidated AIP. Phase 4.</summary>
    public const string Consolidated = "Consolidated";

    /// <summary>Every valid value, for validation and for tests that pin the column's domain.</summary>
    public static readonly IReadOnlyList<string> All =
    [
        Draft, DepartmentReview, SubmittedToPpdo, ReturnedByPpdo, Consolidated,
    ];

    /// <summary>
    /// Whether an encoder may still change this office's tree. Only <see cref="Draft"/> — once
    /// submitted the office is read-only to the encoder, and the UI must say which state it is in
    /// rather than simply disabling the controls.
    /// </summary>
    public static bool IsEncoderEditable(string status) => status == Draft;
}
