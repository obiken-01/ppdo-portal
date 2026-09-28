using PPDO.Domain.Entities;
using PPDO.Domain.Interfaces;

namespace PPDO.Application.Common;

/// <summary>
/// The three checks every AIP write must pass, in one place (V18-42 / PPDO-52).
///
/// <para>
/// Extracted from <c>AipService.CheckWritableAsync</c> when the expenditure endpoints became a
/// second service that needs the identical rule. <b>Two copies of this would drift</b>, and the
/// drift would be silent: whichever copy was forgotten would simply stop refusing, and the first
/// symptom would be an encoder editing an office that is sitting in review.
/// </para>
///
/// <para>
/// The three checks answer genuinely different questions and none implies another:
/// </para>
/// <list type="number">
/// <item><b>Ownership</b> — may this caller touch this office at all? Refused as
/// <c>NotFound</c> with the caller's own message, indistinguishable from a node that does not
/// exist (PPDO-46). A write names one node, so clamping is not available: redirecting it would
/// write to the wrong row.</item>
/// <item><b>The year's state</b> — <c>aip_records.status</c>. An Admin archived it.</item>
/// <item><b>This office's state</b> — <c>aip_offices.workflow_status</c>. ⚠️ Independent of the
/// year's: a Draft record whose office has gone to PPDO is closed. ↩️ <b>The boundary moved in
/// PPDO-70</b>: it is <see cref="AipWorkflowStatus.SubmittedToPpdo"/> that closes an office, not
/// the encoder's first submit — during department review the encoder and the department head both
/// still edit (<c>AIP_Review_Spec.md</c> decision 4).</item>
/// </list>
///
/// <para>
/// ⚠️ Checks 2 and 3 refuse with <c>BadRequest</c>, not <c>NotFound</c>. The caller has already
/// passed ownership, so they can see this record — telling them it does not exist would say their
/// own office had vanished. Only check 1 hides existence, and only from someone with no business
/// knowing it.
/// </para>
///
/// <para>
/// ↩️ <b>PPDO-148 adds a fourth, the division lock</b> (<see cref="CheckDivision{T}"/> and
/// <see cref="CheckContainer{T}"/>). It is separate from <see cref="CheckAsync{T}"/> because it
/// needs the activity, which a program or project write does not have. It always runs after the
/// three above.
/// </para>
/// </summary>
public static class AipWriteGuard
{
    /// <summary>
    /// Returns null when the write may proceed, or the <see cref="ServiceResult{T}"/> to hand back.
    /// </summary>
    /// <param name="action">
    /// A verb for the refusal message — "add to", "edit", "delete from". It is read by a person, so
    /// it must fit "Cannot {action} a 'Final' record".
    /// </param>
    public static async Task<ServiceResult<T>?> CheckAsync<T>(
        AipOffice office, User caller, IAipRepository aipRepo,
        string notFoundMessage, CancellationToken ct, string action = "add to")
    {
        if (!OfficeScope.Resolve(caller).Permits(office.OfficeId))
            return ServiceResult<T>.NotFound(notFoundMessage);

        AipRecord? rec = await aipRepo.GetByIntIdAsync(office.AipRecordId, ct);
        if (rec is null)
            return ServiceResult<T>.NotFound($"AIP record {office.AipRecordId} not found.");

        if (rec.Status != PlanningStatus.Draft)
            return ServiceResult<T>.BadRequest(
                $"Cannot {action} a '{rec.Status}' record. Unlock it back to Draft first.");

        if (!AipWorkflowStatus.IsOfficeEditable(office.WorkflowStatus))
            return ServiceResult<T>.BadRequest(
                $"Cannot {action} this office's AIP while it is in {Describe(office.WorkflowStatus)}. "
                + "It has been sent on and is no longer editable here.");

        return null;
    }

    /// <summary>
    /// The fourth check, for a write to one activity or its expenditure lines: the division lock
    /// (v1.8.0 — PPDO-148, <c>Division_Submit_Spec.md</c> §3.3). Returns null when the write may
    /// proceed.
    ///
    /// <para>
    /// ⚠️ <b>Call it only after <see cref="CheckAsync{T}"/> has returned null, never instead.</b>
    /// It can only refuse, so running it second is what guarantees that the division rule never
    /// allows a write the office's state refuses. A department head passes this check, and an
    /// office sitting with PPDO still stops them at the check before.
    /// </para>
    ///
    /// <para>
    /// <c>BadRequest</c>, like checks 2 and 3: the caller has passed ownership and can see the
    /// activity. The spec's own wording (§3.3) is a 400 for every one of these.
    /// </para>
    /// </summary>
    public static ServiceResult<T>? CheckDivision<T>(AipDivisionContext divisions, int? activityDivisionId)
        => divisions.RefuseActivityWrite(activityDivisionId) is string refused
            ? ServiceResult<T>.BadRequest(refused)
            : null;

    /// <summary>
    /// The division rule for a write that is not about one activity — a program, project or office
    /// group. Refuses only the encoder with no division in a divisioned office, who is read-only.
    /// Same ordering rule as <see cref="CheckDivision{T}"/>: after <see cref="CheckAsync{T}"/>.
    /// </summary>
    public static ServiceResult<T>? CheckContainer<T>(AipDivisionContext divisions)
        => divisions.RefuseContainerWrite() is string refused
            ? ServiceResult<T>.BadRequest(refused)
            : null;

    /// <summary>
    /// A workflow state as a person reads it, so a refusal tells an encoder who holds their work
    /// rather than only that they cannot touch it.
    /// </summary>
    public static string Describe(string status) => status switch
    {
        AipWorkflowStatus.DepartmentReview => "department review",
        AipWorkflowStatus.SubmittedToPpdo  => "review by PPDO",
        AipWorkflowStatus.ReturnedByPpdo   => "the returned-by-PPDO state",
        AipWorkflowStatus.Consolidated     => "the consolidated AIP",
        _                                  => $"the '{status}' state",
    };
}
