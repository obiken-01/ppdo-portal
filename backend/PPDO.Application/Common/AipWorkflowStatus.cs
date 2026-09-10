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
/// <b>⚠️ All five states existed from the first migration</b> (Phase 3), before anything could
/// reach four of them. Adding them piecemeal would have meant a second migration on the same column
/// and an interval where the column's domain did not match the documented workflow.
///
/// <b>⚠️ There are two submits, not one.</b> Encoder → department head (PPDO-52) and department
/// head → PPDO (PPDO-69). A single "submitted" state would not distinguish them, and they have
/// different authorities.
///
/// <b>Transitions, and who owns each:</b>
/// <list type="table">
///   <item><term><see cref="Draft"/> → <see cref="DepartmentReview"/></term>
///         <description>the encoder — <c>AipSubmitService.SubmitAsync</c> (PPDO-52)</description></item>
///   <item><term>→ <see cref="SubmittedToPpdo"/></term>
///         <description>the department head, from <see cref="DepartmentReview"/> or
///         <see cref="ReturnedByPpdo"/> — <c>SubmitToPpdoAsync</c> (PPDO-69)</description></item>
///   <item><term>→ <see cref="ReturnedByPpdo"/></term>
///         <description>a PPDO reviewer returns it (PPDO-72)</description></item>
///   <item><term>→ <see cref="Consolidated"/></term>
///         <description>a PPDO reviewer accepts it (PPDO-74)</description></item>
/// </list>
/// </summary>
public static class AipWorkflowStatus
{
    /// <summary>The encoder is still building. Editable by the office.</summary>
    public const string Draft = "Draft";

    /// <summary>
    /// Submitted by the encoder; with the office's own department head. ⚠️ Still editable — by the
    /// department head <i>and</i> by the encoder (spec decision 4). What the encoder loses here is
    /// the ability to move it on, not the ability to work on it.
    /// </summary>
    public const string DepartmentReview = "DepartmentReview";

    /// <summary>
    /// Sent on by the department head to PPDO's consolidated reviewer. ⚠️ <b>The lock.</b> Nobody
    /// edits content here — not the encoder, not the department head, and not the PPDO reviewer,
    /// who never edits in any state. Commenting still works; a comment is not a content write.
    /// </summary>
    public const string SubmittedToPpdo = "SubmittedToPpdo";

    /// <summary>
    /// Returned by PPDO for changes. Editable again by the office, exactly as
    /// <see cref="DepartmentReview"/> is — returning re-opens the work rather than creating a
    /// fourth kind of access.
    /// </summary>
    public const string ReturnedByPpdo = "ReturnedByPpdo";

    /// <summary>Accepted into the consolidated AIP by a PPDO reviewer. Closed to content edits.</summary>
    public const string Consolidated = "Consolidated";

    /// <summary>Every valid value, for validation and for tests that pin the column's domain.</summary>
    public static readonly IReadOnlyList<string> All =
    [
        Draft, DepartmentReview, SubmittedToPpdo, ReturnedByPpdo, Consolidated,
    ];

    /// <summary>
    /// Whether <b>the office's own people</b> — encoder and department-head reviewer alike — may
    /// still change this office's tree.
    ///
    /// <para>
    /// ↩️ <b>Widened from <c>Draft</c>-only on 2026-09-08 (PPDO-70), and renamed with it.</b> It was
    /// <c>IsEncoderEditable</c>, returning true for <see cref="Draft"/> alone, and its remarks
    /// asserted that "once submitted the office is read-only to the encoder". That was a reasonable
    /// reading while Phase 4 did not exist, but it is not what the workflow says
    /// (<c>Phase_Plan.md</c> §12.6 / tracker B3, confirmed by Ralph 2026-09-08 —
    /// <c>AIP_Review_Spec.md</c> decision 4): <b>the lock falls at <see cref="SubmittedToPpdo"/>,
    /// not at the first submit.</b>
    /// </para>
    ///
    /// <para>
    /// ⚠️ <b>Why the wider rule is the right one.</b> The department head's remit during review is
    /// "to update any minor details they found" (tracker B11). Freezing the encoder out at the
    /// first submit makes the department head personally retype every correction they spot, which
    /// is not what reviewing someone else's work looks like — and it strands an office whose
    /// department head is away.
    /// </para>
    ///
    /// <para>
    /// ⚠️ <b>Not role-aware, deliberately.</b> In all three open states the encoder and the
    /// department head have identical content rights, so the rule is a property of the
    /// <i>state</i>. The two roles genuinely differ on who may submit onward — that is the
    /// endpoint's permission gate (PPDO-69), not this predicate. The PPDO consolidated reviewer is
    /// denied content writes in every state by <c>ReviewerWriteGuard</c>, which is a separate axis
    /// again: this returning true never grants them anything.
    /// </para>
    ///
    /// <para>
    /// ⚠️ <b>Closed is the safe default for a state not named here.</b> A sixth state added later
    /// is far more likely to be review-ish than draft-ish, so it must be added deliberately rather
    /// than inherited — pinned by <c>AipServiceTests.OnlyTheOfficeHeldStatesAreEditable</c>.
    /// </para>
    /// </summary>
    public static bool IsOfficeEditable(string status) =>
        status is Draft or DepartmentReview or ReturnedByPpdo;
}
