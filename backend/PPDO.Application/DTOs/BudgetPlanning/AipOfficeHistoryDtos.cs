namespace PPDO.Application.DTOs.BudgetPlanning;

/// <summary>
/// One hand-off in an office's AIP submission history (V18-77 / PPDO-77, spec §5.2).
/// </summary>
/// <param name="Id">The audit row's id — stable, so the UI can key on it.</param>
/// <param name="Action">
/// The audit action constant — <c>SUBMIT_DH</c>, <c>SUBMIT_PPD</c>, <c>RETURN_PPD</c> or
/// <c>ACCEPT_PPD</c>. The client words it; the server never sends a sentence.
/// </param>
/// <param name="FromStatus">
/// The workflow status the office left, read from the audit row's old values. ⚠️ Null when the
/// payload could not be read — never guessed, because a guessed <c>ReturnedByPpdo</c> is what turns
/// "Sent to PPDO" into "Re-submitted to PPDO".
/// </param>
/// <param name="ToStatus">The status the office moved to. Fixed by the action.</param>
/// <param name="ActorSide">
/// <c>Office</c>, <c>DepartmentHead</c> or <c>Ppdo</c> — who performs this action by rule, not the
/// actor's current flags, which may have changed since.
/// </param>
/// <param name="At">When it happened (UTC; rendered in Manila time).</param>
/// <param name="Comments">
/// The comments written while the office sat in <paramref name="ToStatus"/> — from this hand-off
/// until the next — oldest first. Read-only: <c>CanResolve</c> is always false here.
/// </param>
public sealed record AipHistoryEntryDto(
    long                               Id,
    string                             Action,
    string?                            FromStatus,
    string                             ToStatus,
    string                             ActorName,
    string                             ActorSide,
    DateTime                           At,
    IReadOnlyList<AipReviewCommentDto> Comments);

/// <summary>
/// An office's whole submission history: the hand-off chain newest first, plus the comments
/// written before its first submit (V18-77 / PPDO-77).
/// </summary>
/// <param name="WorkflowStatus">Where the office's work sits now — lets the UI mark the newest entry.</param>
/// <param name="BeforeFirstSubmission">
/// Comments older than every hand-off, oldest first. ⚠️ Kept rather than dropped: an office still in
/// Draft can already carry a department head's remarks, and a history that hid them would disagree
/// with the tree.
/// </param>
public sealed record AipOfficeHistoryDto(
    int                                AipRecordId,
    int                                OfficeId,
    string                             WorkflowStatus,
    IReadOnlyList<AipHistoryEntryDto>  Entries,
    IReadOnlyList<AipReviewCommentDto> BeforeFirstSubmission);
