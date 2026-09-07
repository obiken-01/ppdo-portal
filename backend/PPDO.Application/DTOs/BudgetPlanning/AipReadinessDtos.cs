namespace PPDO.Application.DTOs.BudgetPlanning;

/// <summary>
/// One reason an office's AIP cannot be submitted yet (V18-49 / PPDO-59).
///
/// ⚠️ <b>Each issue names the node</b>, so the checklist can link to it. A single sentence saying
/// "some activities are incomplete" leaves an encoder hunting through a tree of hundreds — spec §6.2
/// requires the list, not the summary.
/// </summary>
/// <param name="Kind">
/// A stable machine-readable slug the UI groups and links on: <c>no-lines</c>,
/// <c>costed-at-zero</c>, <c>missing-esre</c>, <c>missing-cc-typology</c>, <c>ceiling</c>.
/// ⚠️ Not a display string — the message is the display string. Switching the UI on
/// <see cref="Message"/> would break the moment the wording improves.
/// </param>
/// <param name="ActivityId">The activity at fault, or null for an office-level issue like the ceiling.</param>
/// <param name="RefCode">That activity's ref code, so the encoder can find it on the printed form.</param>
/// <param name="Message">What is wrong, in a sentence a person can act on.</param>
public sealed record AipReadinessIssueDto(
    string  Kind,
    int?    ActivityId,
    string? RefCode,
    string  Message);

/// <summary>
/// Whether an office's AIP may be submitted, and everything blocking it (V18-49 / PPDO-59).
///
/// Served by <c>GET /api/budget-planning/aip/{aipId}/readiness</c> so the checklist can be shown
/// <b>before</b> the encoder presses submit, rather than as a wall of errors afterwards.
///
/// ⚠️ <b>This is a gate, not a summary.</b> There is no "submit anyway" — spec §6.2. If
/// <see cref="CanSubmit"/> is false the submit endpoint refuses with the same issues.
/// </summary>
/// <param name="WorkflowStatus">
/// The office's current state. ⚠️ An office can hold <b>several</b> <c>AipOffice</c> rows — one per
/// sub-office group — and they move together, so this is their shared state.
/// </param>
/// <param name="ActivityCount">Every activity across all of the office's groups.</param>
/// <param name="Issues">Empty when ready. One entry per failing activity, plus at most one ceiling entry.</param>
/// <param name="Ceiling">
/// The office's ceiling position, including a <c>Remaining</c> that <b>may be negative</b> — which
/// is exactly the state a ceiling cut leaves behind, and what must block submit.
/// </param>
public sealed record AipReadinessDto(
    int                                  AipRecordId,
    int                                  OfficeId,
    string                               WorkflowStatus,
    bool                                 CanSubmit,
    int                                  ActivityCount,
    IReadOnlyList<AipReadinessIssueDto>  Issues,
    AipCeilingStatusDto?                 Ceiling);

/// <summary>What a successful submit returns: where the office's work now sits.</summary>
public sealed record AipSubmitResultDto(
    int    AipRecordId,
    int    OfficeId,
    string WorkflowStatus,
    int    GroupsMoved);
