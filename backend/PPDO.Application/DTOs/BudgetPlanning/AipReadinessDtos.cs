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
/// <c>costed-at-zero</c>, <c>zero-total</c>, <c>missing-fund</c>, <c>missing-esre</c>, <c>empty</c>.
/// ↩️ <c>ceiling</c> is no longer an issue kind (PPDO-146). It is
/// <see cref="AipReadinessDto.CeilingWarning"/> now, because it no longer blocks every submit.
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
/// ⚠️ <b>This is a gate, not a summary.</b> There is no "submit anyway" for completeness (spec
/// §6.2). If <see cref="CanSubmit"/> is false the submit endpoint refuses with the same issues.
///
/// ↩️ <b>The ceiling is a two-level rule since PPDO-146</b> (<c>Division_Submit_Spec.md</c>
/// decision 13). Over the ceiling is a <see cref="CeilingWarning"/> at the submit to the department
/// head, and a block at the send to PPDO (<see cref="CanSubmitToPpdo"/>). So the gate still
/// exists, one hop later, and is still the only place the ceiling is enforced.
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
/// <param name="CanSubmit">
/// Whether the submit to the department head may go ahead: every completeness check passes. The
/// ceiling does not affect it (PPDO-146).
/// </param>
/// <param name="CeilingWarning">
/// The ceiling service's own sentence when the office is over its ceiling, naming the fund, the
/// total, the ceiling and the overage. Null when within it.
/// </param>
/// <param name="CanSubmitToPpdo">
/// Whether the send to PPDO may go ahead: <see cref="CanSubmit"/> <b>and</b> within the ceiling.
/// </param>
public sealed record AipReadinessDto(
    int                                  AipRecordId,
    int                                  OfficeId,
    string                               WorkflowStatus,
    bool                                 CanSubmit,
    int                                  ActivityCount,
    IReadOnlyList<AipReadinessIssueDto>  Issues,
    AipCeilingStatusDto?                 Ceiling,
    string?                              CeilingWarning,
    bool                                 CanSubmitToPpdo);

/// <summary>
/// Where the office's work now sits after a workflow transition.
///
/// ⚠️ <b>Named for submit, but it carries every transition</b> — PPDO-72's return returns it too,
/// and PPDO-74's accept will. It holds nothing submit-specific: the four fields answer "which
/// office, in which record, moved to which state, across how many group rows", which is the same
/// question at every hop. Kept as one type rather than cloned per transition so a client that
/// handles one handles all of them; renaming it would churn PPDO-69's shipped code and its
/// frontend type for no behaviour.
/// </summary>
/// <param name="CeilingWarning">
/// Set only by the submit to the department head, when it went through over the ceiling (PPDO-146),
/// so the page can say so at the moment it happens. Null for every other transition.
/// </param>
public sealed record AipSubmitResultDto(
    int     AipRecordId,
    int     OfficeId,
    string  WorkflowStatus,
    int     GroupsMoved,
    string? CeilingWarning = null);

/// <summary>
/// One office's whole AIP as the PPDO consolidated reviewer reads it (V18-56 / PPDO-74,
/// <c>AIP_Review_Spec.md</c> §6.2).
///
/// <para>
/// ⚠️ <b>The office, not the record.</b> The reviewer works one office at a time — that is the
/// granularity of both actions they hold (tracker B5) — and the record carries every office in the
/// province. Serving the record here would ship twenty-five trees to render one.
/// </para>
///
/// <para>
/// ⚠️ <b>No <c>CanReturn</c> / <c>CanAccept</c> flag, deliberately.</b> Both are a pure function of
/// <see cref="WorkflowStatus"/> for the only role that can reach this endpoint, and a second
/// carrier of the same fact is one that can disagree with it. The server refuses the transition on
/// its own account regardless of what the client offered.
/// </para>
/// </summary>
/// <param name="OfficeName">The config office's name — what the confirm dialog must say out loud.</param>
/// <param name="OfficeCode">Its short code, which is what the printed form's column (3) carries.</param>
/// <param name="WorkflowStatus">
/// The office's shared state across every group row (decision 21). ⚠️ Read from the first group:
/// the rows move together, so a disagreement is a defect elsewhere, not a case to render.
/// </param>
/// <param name="Groups">
/// The sub-office groups, each a full tree. ⚠️ Several is normal — one office holds one row per
/// sub-office group, and they are reviewed as one body of work.
/// </param>
public sealed record AipOfficeReviewDto(
    int                         AipRecordId,
    int                         FiscalYear,
    int                         OfficeId,
    string                      OfficeName,
    string                      OfficeCode,
    string                      WorkflowStatus,
    int                         ActivityCount,
    IReadOnlyList<AipOfficeDto> Groups);

/// <summary>
/// One program or project on the way down to an activity — the review modal's path strip (PPDO-79).
/// Deliberately three fields: the strip names where the activity sits, it does not render the level.
/// </summary>
public sealed record AipReviewPathNodeDto(int Id, string RefCode, string Name);

/// <summary>
/// One activity opened from the AIP Review search, for the activity modal (PPDO-79,
/// <c>AIP_Review_Spec.md</c> §6.1a).
///
/// <para>
/// ⚠️ <b>Why the path travels with it.</b> The search returns flat rows, so a reviewer opening one has
/// no idea which program the activity belongs to. Office, program and project are carried here so
/// the modal can say so without a second request per level.
/// </para>
///
/// <para>
/// ⚠️ <b><see cref="CanEdit"/> is computed by the server, and the modal reads it rather than
/// re-deriving it.</b> Four conditions have to hold at once — the caller is this office's department
/// head, the office is in an editable workflow state, the record is still Draft, and
/// <c>ReviewerWriteGuard</c> does not deny them. The fourth is the one a client would miss: a person
/// holding <i>both</i> reviewer flags is denied content writes even on their own office, and a
/// client-side rule built from the two flags alone would offer them an Edit button that 403s.
/// </para>
///
/// <para>
/// There is no <c>CanComment</c> here. The comments read already answers that for the office, from
/// the same side-resolution the create path uses, and a second carrier of it could disagree.
/// </para>
/// </summary>
/// <param name="OfficeRefCode">The sub-office group's own code — the first line of the path strip.</param>
/// <param name="Expenditures">
/// The activity's lines with their items. ⚠️ Returned here rather than fetched from the entry page's
/// list endpoint: that one resolves scope with <c>OfficeScope.Resolve</c>, which answers 404 to a
/// cross-office reviewer who does not happen to sit in the host office.
/// </param>
public sealed record AipActivityReviewDto(
    int                              AipRecordId,
    int                              FiscalYear,
    int                              OfficeId,
    string                           OfficeName,
    string                           OfficeCode,
    string                           OfficeRefCode,
    string                           Sector,
    string                           WorkflowStatus,
    AipReviewPathNodeDto             Program,
    AipReviewPathNodeDto             Project,
    AipActivityDto                   Activity,
    IReadOnlyList<AipExpenditureDto> Expenditures,
    bool                             CanEdit);
