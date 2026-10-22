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

    /// <summary>The SAIP delete refusal (v1.10.0 — PPDO-247, spec §3, decision 5).</summary>
    public const string SaipApprovedDeleteMessage =
        "Approved items can't be deleted in an SAIP. Set the amount to ₱0.";

    /// <summary>
    /// The SAIP delete rule (v1.10.0 — PPDO-247, decision 5): in an SAIP's working copy, a row copied
    /// from the approved AIP (<paramref name="sourceId"/> set) cannot be deleted — the office lowers
    /// it to ₱0 instead. A row added in this SAIP (no source) can. Outside an SAIP this never refuses.
    ///
    /// <para>
    /// Run it after <see cref="CheckAsync{T}"/>, like the division checks: it can only refuse.
    /// <c>BadRequest</c>, because the caller can see the row and the spec's wording is a 400.
    /// </para>
    /// </summary>
    public static ServiceResult<T>? CheckSaipDelete<T>(AipOffice office, int? sourceId)
        => office.SaipId is not null && sourceId is not null
            ? ServiceResult<T>.BadRequest(SaipApprovedDeleteMessage)
            : null;

    /// <summary>The refusal a PPDO reviewer gets on an office that has not reached PPDO yet.</summary>
    public const string ReviewerEditStateMessage =
        "PPDO can edit this office's AIP only after it is submitted to PPDO.";

    /// <summary>
    /// The checks for a <b>PPDO reviewer's</b> edit to an expenditure line (v1.9.0 — PPDO-212,
    /// <c>docs/v1.9/AIP_Reviewer_Edit_Spec.md</c>). Used <b>instead of</b>
    /// <see cref="CheckAsync{T}"/>, never as well, and only for a caller
    /// <see cref="ReviewerWriteGuard.DeniesWriteAsync"/> would otherwise refuse.
    ///
    /// <para>
    /// No office-scope check: a cross-office reviewer's scope is every office
    /// (<see cref="OfficeScope.ResolveForReview"/>), so there is nothing to refuse. What narrows the
    /// grant is the <b>state</b>: only <c>SubmittedToPpdo</c> and <c>Consolidated</c>, the states in
    /// which no one in the office can edit. Before that the AIP is the office's to change, and PPDO's
    /// tool is a comment or a return. FY2028+ only, since an older record has no expenditure lines.
    /// </para>
    ///
    /// <para>
    /// ⚠️ <b>No division lock either</b> — that rule decides which of an office's own people may
    /// touch an activity, and the reviewer is none of them.
    /// </para>
    /// </summary>
    public static async Task<ServiceResult<T>?> CheckReviewerEditAsync<T>(
        AipOffice office, IAipRepository aipRepo, CancellationToken ct)
    {
        AipRecord? rec = await aipRepo.GetByIntIdAsync(office.AipRecordId, ct);
        if (rec is null)
            return ServiceResult<T>.NotFound($"AIP record {office.AipRecordId} not found.");

        if (!AipFiscalYears.IsEntered(rec.FiscalYear))
            return ServiceResult<T>.BadRequest(
                $"PPDO edits apply only to AIPs from FY{AipFiscalYears.FirstEnteredFiscalYear} on.");

        if (rec.Status != PlanningStatus.Draft)
            return ServiceResult<T>.BadRequest(
                $"Cannot edit a '{rec.Status}' record. Unlock it back to Draft first.");

        if (office.WorkflowStatus is not (AipWorkflowStatus.SubmittedToPpdo or AipWorkflowStatus.Consolidated))
            return ServiceResult<T>.BadRequest(ReviewerEditStateMessage);

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
