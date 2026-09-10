using PPDO.Application.Common;
using PPDO.Application.DTOs.BudgetPlanning;
using PPDO.Domain.Entities;

namespace PPDO.Application.Services;

/// <summary>
/// The submit checklist and the transition it guards (V18-42 / PPDO-52 for the transition,
/// V18-49 / PPDO-59 for the rules).
///
/// <para>
/// <b>⚠️ This is a gate, not a courtesy summary.</b> DECISION C allows — and expects — over-ceiling
/// encoding, and blocks at submit. So this checklist <i>is</i> the ceiling enforcement point: built
/// as something an encoder can dismiss, there would be no ceiling enforcement anywhere in the
/// system. There is no "submit anyway".
/// </para>
///
/// <para>
/// <b>⚠️ An office is several rows, not one.</b> <c>workflow_status</c> lives on <c>AipOffice</c>,
/// and one office holds one such row <i>per sub-office group</i> — the province's FY2027 SOCIAL
/// sheet has three under one office. "Submit the whole office's work in one action" therefore means
/// moving <b>every</b> group row for that office together. Submitting one group and leaving its
/// siblings in Draft would put one office in two states at once, and the department head would
/// receive a fraction of the work with nothing saying so.
/// </para>
///
/// <para>
/// ℹ️ Readiness and submit run the identical checks, deliberately. The endpoint exists so the
/// office can fix things without guessing, not so the two can drift.
/// </para>
/// </summary>
public interface IAipSubmitService
{
    /// <summary>
    /// What is blocking submit, or an empty issue list when nothing is. Read-only and side-effect
    /// free — safe to poll as the encoder works.
    /// </summary>
    Task<ServiceResult<AipReadinessDto>> GetReadinessAsync(
        int aipRecordId, User caller, CancellationToken ct = default);

    /// <summary>
    /// Moves every one of the caller's office's group rows from <c>Draft</c> to
    /// <c>DepartmentReview</c>, after re-running the checks.
    ///
    /// ⚠️ Re-runs them rather than trusting a readiness call the client made earlier: between the
    /// two, PBO can cut the ceiling and a colleague can delete a line.
    /// </summary>
    Task<ServiceResult<AipSubmitResultDto>> SubmitAsync(
        int aipRecordId, User caller, CancellationToken ct = default);

    /// <summary>
    /// The <b>second</b> submit: the office's department head sends the reviewed work on to PPDO
    /// (V18-51 / PPDO-69, <c>AIP_Review_Spec.md</c> §3.2). From <c>DepartmentReview</c> — or from
    /// <c>ReturnedByPpdo</c>, which is the re-submit after PPDO sent it back.
    ///
    /// <para>
    /// <b>⚠️ There are two submits and they have different authorities.</b>
    /// <see cref="SubmitAsync"/> is the encoder's, and shipped in Phase 3. This one is the
    /// department-head reviewer's <i>alone</i> — the caller must hold
    /// <c>CanReviewBudgetPlanning</c>, checked at the handler as every other feature gate is.
    /// The plan's original "encoders cannot submit" applies only to this hop.
    /// </para>
    ///
    /// <para>
    /// <b>⚠️ The completeness and ceiling checks run again here, and that is not belt-and-braces.</b>
    /// The department head <i>may edit values</i> during review (spec decision 4) — that is what the
    /// review is for — so the figures that passed the encoder's gate are not necessarily the figures
    /// being sent to PPDO. Trusting the earlier pass would let a review that broke the ceiling
    /// through the only gate that enforces it.
    /// </para>
    /// </summary>
    /// <param name="officeId">
    /// The office being submitted. ⚠️ Must be the caller's own; a mismatch is
    /// <c>NotFound</c>, not <c>Forbidden</c>, so the response cannot be used to discover which
    /// offices exist (PPDO-46). It is in the signature — despite always equalling the caller's own
    /// office — so this route reads the same as the return and accept routes beside it, and so a
    /// stale tab cannot submit an office the user has since moved away from.
    /// </param>
    Task<ServiceResult<AipSubmitResultDto>> SubmitToPpdoAsync(
        int aipRecordId, int officeId, User caller, CancellationToken ct = default);
}
