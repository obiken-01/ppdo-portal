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
}
