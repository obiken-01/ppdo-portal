namespace PPDO.Application.Common;

/// <summary>
/// One division's state in the division-grained submit (v1.8.0 — PPDO-130,
/// <c>docs/v1.8/Division_Submit_Spec.md</c>). Stored in <c>aip_division_submissions.status</c>.
///
/// <para>
/// ⚠️ <b>Two states, and an absent row is <see cref="Draft"/>.</b> A return does not get a
/// state of its own. It puts the division back in <see cref="Draft"/> and stamps
/// <c>returned_at</c>. A third state would give every reader one more case to get wrong, for
/// something the audit log already records.
/// </para>
///
/// <para>
/// Separate from <see cref="AipWorkflowStatus"/> on purpose. That is the <i>office's</i> state and
/// is derived from these (spec decision 9). Mixing the two would add office states the kanban,
/// notifications and review search would all have to learn.
/// </para>
/// </summary>
public static class AipDivisionStatus
{
    /// <summary>The division's encoders are still working. Also what an absent row means.</summary>
    public const string Draft = "Draft";

    /// <summary>
    /// Submitted to the department head. Locked for the division's encoders, but still editable
    /// by the department head (spec decision 2).
    /// </summary>
    public const string Submitted = "Submitted";

    /// <summary>Every valid value. The CHECK constraint on the column is built from this.</summary>
    public static readonly IReadOnlyList<string> All = [Draft, Submitted];
}
