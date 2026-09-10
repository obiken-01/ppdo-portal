using PPDO.Application.Common;
using PPDO.Application.DTOs.BudgetPlanning;
using PPDO.Domain.Entities;

namespace PPDO.Application.Services;

/// <summary>
/// The PPDO consolidated reviewer's actions on <b>another office's</b> AIP (v1.8.0 Phase 4 —
/// V18-54 / PPDO-72, <c>AIP_Review_Spec.md</c> §3.3 and §4.2).
///
/// <para>
/// <b>⚠️ Why this is a service of its own rather than two more methods on
/// <see cref="IAipSubmitService"/>.</b> That service resolves the office from the <i>caller</i> and
/// narrows to <c>caller.OfficeId</c>, deliberately — "submit is an act on your own work". Every
/// action here is the opposite shape: a reviewer acting on an office that is not theirs. Adding
/// them there would have meant either a second resolver inside one service with two incompatible
/// scoping rules, or teaching the existing one a cross-office branch that every submit path would
/// then inherit. The scope resolvers are kept apart for the same reason
/// (<see cref="OfficeScope.ResolveForReview"/>'s remarks say so at length).
/// </para>
///
/// <para>
/// <b>⚠️ Nothing here is a content write, and none of it may be routed through
/// <c>ReviewerWriteGuard</c>.</b> That guard denies content writes to exactly the role these
/// actions belong to; its own remarks name "returning a submission" as something that must not go
/// through it. What it protects is another office's <i>numbers</i>, which this service never
/// touches — it moves a workflow column and nothing else.
/// </para>
///
/// <para>
/// ⚠️ <b>The office is the granularity</b> (tracker B5). The whole of one office's work goes back,
/// never a program or an activity — and "one office" means <b>every</b> <c>AipOffice</c> group row
/// it holds (decision 21), which is why the result reports a count.
/// </para>
/// </summary>
public interface IAipReviewService
{
    /// <summary>
    /// Sends one office's whole AIP back for changes: <c>SubmittedToPpdo</c> →
    /// <c>ReturnedByPpdo</c>, across every group row of that office at once.
    ///
    /// <para>
    /// <b>⚠️ Re-opening the work is not done here.</b> <c>AipWorkflowStatus.IsOfficeEditable</c>
    /// already admits <c>ReturnedByPpdo</c> (PPDO-70), so the office regains editing by virtue of
    /// the state alone. Nothing in this method touches a guard, and nothing should be added that
    /// does — returning creates no fourth kind of access, it restores the one the office had.
    /// </para>
    ///
    /// <para>
    /// <b>⚠️ No completeness or ceiling re-run</b>, unlike the two submits. Those gate work moving
    /// <i>forward</i>; sending it back has nothing to do with whether it is finished. A reviewer
    /// returning an office precisely because it is wrong must not be blocked by it being wrong.
    /// </para>
    ///
    /// <para>
    /// <b>⚠️ No covering note, deliberately.</b> The comments (PPDO-71) are the mechanism for
    /// saying what needs to change; a second free-text channel attached to the transition would
    /// compete with them and is not in the spec. There is no body on the route either.
    /// </para>
    ///
    /// <para>
    /// <b>The refusals split three ways and the split is the contract</b> (§4.2):
    /// </para>
    /// <list type="bullet">
    ///   <item><b>409</b> — another reviewer already moved it (<c>ReturnedByPpdo</c>,
    ///   <c>Consolidated</c>). Last-write-wins here would silently undo a colleague's action.</item>
    ///   <item><b>400</b> — the work never reached PPDO (<c>Draft</c>, <c>DepartmentReview</c>).
    ///   Nobody moved anything; there is simply nothing to send back.</item>
    ///   <item><b>404</b> — the caller may not act on this office, worded identically to an office
    ///   that does not exist (PPDO-46), so the response cannot be used to enumerate offices.</item>
    /// </list>
    /// Collapsing the first two into one status tells a reviewer their colleague's action was
    /// their own mistake.
    /// </summary>
    Task<ServiceResult<AipSubmitResultDto>> ReturnToOfficeAsync(
        int aipRecordId, int officeId, User caller, CancellationToken ct = default);

    /// <summary>
    /// Takes one office's whole AIP into the consolidated document: <c>SubmittedToPpdo</c> →
    /// <c>Consolidated</c>, across every group row at once (V18-56 / PPDO-74, decision 6).
    ///
    /// <para>
    /// <b>⚠️ The mirror of <see cref="ReturnToOfficeAsync"/>, and refused the same way on purpose.</b>
    /// A state produced by <i>another reviewer's</i> action (<c>ReturnedByPpdo</c>,
    /// <c>Consolidated</c>) is a <b>409</b>; a state the office never sent up (<c>Draft</c>,
    /// <c>DepartmentReview</c>) is a <b>400</b>. Spec §3.3 answers <c>ReturnedByPpdo</c> twice —
    /// 409 in its concurrency row, 400 in its "accept a returned office" row — and 409 is correct:
    /// §10's checklist states that exact race, and only another reviewer can have put the office
    /// there. A 400 would tell the reviewer who lost the race that they made the mistake.
    /// </para>
    ///
    /// <para>
    /// <b>⚠️ No completeness or ceiling re-run.</b> Those gate work moving <i>forward through the
    /// office</i> — the encoder's submit and the department head's. Accept is PPDO closing its own
    /// reading of work that already passed both gates on its way up; re-running them here would let
    /// a ceiling changed after submission block a reviewer from recording a decision they have
    /// already taken.
    /// </para>
    ///
    /// <para>
    /// ⚠️ <b>There is no un-accept.</b> <c>Consolidated</c> is terminal in shipped code and the
    /// return path refuses from it with a 409, so an accepted office cannot currently be re-opened
    /// — spec §7 leaves that open ("only by a PPDO reviewer, through the existing return path"),
    /// and building it is a ticket, not a quiet addition here.
    /// </para>
    /// </summary>
    Task<ServiceResult<AipSubmitResultDto>> AcceptOfficeAsync(
        int aipRecordId, int officeId, User caller, CancellationToken ct = default);

    /// <summary>
    /// One office's whole AIP, read-only, for the cross-office review screen (V18-56 / PPDO-74,
    /// spec §6.2).
    ///
    /// <para>
    /// <b>⚠️ Why this is not <c>AipService.GetByIdAsync</c> with a filter.</b> That read scopes
    /// through <see cref="AipReadScope"/>, which does two things wrong for a reviewer. It resolves
    /// the office axis from <c>BudgetPlanningScope</c> — so a reviewer who does not sit in the host
    /// office would be clamped to their own office and see nothing — and it applies the
    /// <b>division</b> axis to the host office's own programs, which would silently drop programs
    /// from a review of PPDO's own AIP. A review is of the <i>whole</i> office by definition.
    /// </para>
    ///
    /// <para>
    /// ⚠️ <b>Gated on <c>CanReviewAllOffices</c>, and only that.</b> Not on host-office membership
    /// (tracker B4's tempting wrong axis), and not widened to let an office read itself here — the
    /// office reads its own work on the entry page, which carries the editability rules that belong
    /// to it. Every refusal is a <b>404</b> worded identically to a missing office (PPDO-46), so the
    /// response cannot be used to enumerate the record.
    /// </para>
    /// </summary>
    Task<ServiceResult<AipOfficeReviewDto>> GetOfficeForReviewAsync(
        int aipRecordId, int officeId, User caller, CancellationToken ct = default);

    /// <summary>
    /// The query-first AIP Review search: one page of matching programs, projects and activities
    /// (V18-75 / PPDO-76, spec §4.1 and decisions 15–17).
    ///
    /// <para>
    /// <b>⚠️ A result is a NODE, not an office.</b> The spec never says so outright, but §4.1's
    /// <c>title</c> field searches "program / project / activity name", which only makes sense if a
    /// row is one of those. Each row carries the office it belongs to so the result can link into
    /// the review screen.
    /// </para>
    ///
    /// <para>
    /// <b>⚠️ The gate here is <c>CanAccessBudgetPlanning</c>, not the reviewer flag</b> — §4
    /// verbatim — and scope is applied by <b>clamping</b> through
    /// <see cref="OfficeScope.ResolveForReview"/> rather than refusing: a guest office asking about
    /// somebody else gets its own rows back, never a 403 that would confirm the other office
    /// exists (spec §3.4). The <i>page</i> is gated more tightly than this endpoint, because every
    /// result links into a reviewer-only screen; that is a UI decision and it does not belong here.
    /// </para>
    ///
    /// <para>
    /// ⚠️ <b><c>Mine</c> is resolved from the caller's own permissions, never from a client-supplied
    /// role.</b> For a cross-office reviewer it means the offices actually waiting on them
    /// (<c>SubmittedToPpdo</c>); for anyone else it means their own office.
    /// </para>
    ///
    /// <para>
    /// ⚠️ Returns an empty page — not <c>NotFound</c> — when the fiscal year has no open record.
    /// "Nothing to search yet" is a state the page renders, not an error it reports.
    /// </para>
    /// </summary>
    Task<ServiceResult<AipReviewSearchResultDto>> SearchAsync(
        AipReviewSearchRequestDto request, User caller, CancellationToken ct = default);
}
