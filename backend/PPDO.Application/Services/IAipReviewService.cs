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
}
