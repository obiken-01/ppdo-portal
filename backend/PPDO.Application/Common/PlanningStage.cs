namespace PPDO.Application.Common;

/// <summary>
/// The one status vocabulary the Budget Planning dashboard speaks (PPDO-20,
/// <c>docs/v1.8/Budget_Planning_Dashboard_Requirements.md</c> §2 decision 9). Borrowed from
/// Linear, because the page had grown five overlapping sets — Draft/Final, Met/Not yet,
/// Set/Not set, Submitted, Not started — that a reader had to hold in their head at once.
///
/// ⚠️ <b>These are stages, not warnings.</b> "Over ceiling", "Behind" and "Cannot submit" are
/// deliberately NOT members: they are exceptions that can coexist with any stage, and folding
/// them in would lose the warning behind a status the reader skims past. They stay as separate
/// risk pills, computed from their own booleans on the DTOs.
///
/// <see cref="Review"/> was declared before anything emitted it, so the vocabulary was complete at
/// its one definition. ↩️ PPDO-78 is the first emitter: <see cref="ForSubmission"/> maps an office's
/// AIP workflow state onto these four for the office table's Submission column.
/// </summary>
public static class PlanningStage
{
    /// <summary>Nothing recorded yet.</summary>
    public const string Todo = "Todo";

    /// <summary>Started, not finished.</summary>
    public const string InProgress = "In progress";

    /// <summary>Submitted and awaiting a reviewer — an office at <c>SubmittedToPpdo</c>.</summary>
    public const string Review = "Review";

    /// <summary>Finished — the AIP record is Final.</summary>
    public const string Done = "Done";

    /// <summary>
    /// Maps an AIP record's own status plus what the office/division actually holds onto the
    /// vocabulary above. One helper so the office table and the division table cannot drift:
    /// they answer the same question about two different slices of the same AIP.
    ///
    /// <paramref name="aipStatus"/> is the parent <c>aip_records.status</c>
    /// (<see cref="PlanningStatus"/>) — null when no AIP record exists for the fiscal year at all.
    /// <paramref name="activityCount"/> is how many activities the slice contains.
    ///
    /// An AIP record can be Final while a given office contributed nothing to it; that slice is
    /// <see cref="Todo"/>, not <see cref="Done"/> — the office has no work in the plan, and
    /// reporting it as complete is how a missing office goes unnoticed until the deadline.
    /// </summary>
    public static string ForAip(string? aipStatus, int activityCount)
    {
        if (aipStatus is null || activityCount == 0) return Todo;
        return aipStatus == PlanningStatus.Final ? Done : InProgress;
    }

    /// <summary>
    /// An office's submission stage from its AIP workflow state (PPDO-78, <c>AIP_Review_Spec.md</c>
    /// §6.3). Replaces the constant <see cref="Todo"/> the office table carried "until Phase 4":
    /// left constant, the table would read Todo beside a board showing the same office Returned.
    ///
    /// <c>Draft</c> (or no group row, null) → Todo · <c>DepartmentReview</c> and
    /// <c>ReturnedByPpdo</c> → In progress · <c>SubmittedToPpdo</c> → Review ·
    /// <c>Consolidated</c> → Done. Returned is In progress, not Todo: the office has submitted once
    /// and is working its way back, which is the same reading as the board's Office Review column.
    /// </summary>
    public static string ForSubmission(string? workflowStatus) => workflowStatus switch
    {
        AipWorkflowStatus.DepartmentReview or AipWorkflowStatus.ReturnedByPpdo => InProgress,
        AipWorkflowStatus.SubmittedToPpdo => Review,
        AipWorkflowStatus.Consolidated    => Done,
        _ => Todo,
    };
}
