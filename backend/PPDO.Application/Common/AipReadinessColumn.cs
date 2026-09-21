namespace PPDO.Application.Common;

/// <summary>
/// Which column of the readiness board an office sits in (v1.8.0 Phase 4 — PPDO-78,
/// <c>AIP_Review_Spec.md</c> §6.3).
///
/// <para>
/// <b>⚠️ Computed here, on the server, and nowhere else.</b> It is workflow logic, so it is tested in
/// C#, and the dashboard renders it twice — as the board's column and, through
/// <see cref="PlanningStage.ForSubmission"/>, as the table's Submission pill. Deriving either in the
/// browser would be the second copy that eventually disagrees with the first.
/// </para>
///
/// <para>
/// ⚠️ <b>Five columns, not six.</b> <see cref="AipWorkflowStatus.ReturnedByPpdo"/> is not a column:
/// returned work is editable by the office again, exactly as in department review, so it sits in
/// <see cref="OfficeReview"/> and the row carries <c>IsReturned</c> for the badge.
/// </para>
///
/// <para>
/// ✅ <b>Not Started vs In Progress is the activity count, never the program count.</b> Programs
/// arrive from LDIP without anyone in the office opening the record; counting them would show an
/// office that never started as working. A submission state wins over both — an office that has
/// been submitted is in review whatever it holds.
/// </para>
/// </summary>
public static class AipReadinessColumn
{
    public const string NotStarted   = "NotStarted";
    public const string InProgress   = "InProgress";
    public const string OfficeReview = "OfficeReview";
    public const string PpdoReview   = "PpdoReview";
    public const string Done         = "Done";

    /// <summary>Every column, in board order.</summary>
    public static readonly IReadOnlyList<string> All =
    [
        NotStarted, InProgress, OfficeReview, PpdoReview, Done,
    ];

    /// <summary>
    /// The column for an office at <paramref name="workflowStatus"/> holding
    /// <paramref name="activityCount"/> activities. A null status means the office has no AIP group
    /// row for the year at all, which reads as <see cref="AipWorkflowStatus.Draft"/>.
    /// </summary>
    public static string For(string? workflowStatus, int activityCount) => workflowStatus switch
    {
        AipWorkflowStatus.DepartmentReview or AipWorkflowStatus.ReturnedByPpdo => OfficeReview,
        AipWorkflowStatus.SubmittedToPpdo => PpdoReview,
        AipWorkflowStatus.Consolidated    => Done,
        _ => activityCount > 0 ? InProgress : NotStarted,
    };

    /// <summary>
    /// One office's status from its group rows. Every transition moves all of an office's groups
    /// together, so they agree; if they ever do not, the <b>least advanced</b> wins — an office is
    /// only as far along as the group that is furthest behind, and reporting the other way would
    /// show it in review with work still unsubmitted. Null when there are no rows.
    /// </summary>
    public static string? OfficeStatus(IEnumerable<string> groupStatuses)
    {
        string? least = null;
        foreach (string status in groupStatuses)
        {
            if (least is null || Rank(status) < Rank(least)) least = status;
        }
        return least;
    }

    // An unrecognised state ranks with Draft — the closed-by-default rule AipWorkflowStatus uses.
    private static int Rank(string status) => status switch
    {
        AipWorkflowStatus.DepartmentReview or AipWorkflowStatus.ReturnedByPpdo => 1,
        AipWorkflowStatus.SubmittedToPpdo => 2,
        AipWorkflowStatus.Consolidated    => 3,
        _ => 0,
    };
}
