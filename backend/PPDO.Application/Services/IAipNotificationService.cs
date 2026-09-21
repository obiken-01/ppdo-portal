using PPDO.Application.Common;
using PPDO.Application.DTOs.BudgetPlanning;
using PPDO.Domain.Entities;

namespace PPDO.Application.Services;

/// <summary>
/// The in-app notifications read (V18-58 / PPDO-75, <c>AIP_Review_Spec.md</c> §6.5).
/// </summary>
public interface IAipNotificationService
{
    /// <summary>
    /// The sidebar count and the returned notices for <paramref name="caller"/>, resolved from their
    /// own flags and their own office. There is no office parameter, so nothing about any other
    /// office can be asked for.
    ///
    /// <para>
    /// ⚠️ <b>Counts offices, not groups or activities</b>, and only open (<c>Draft</c>) records from
    /// the first entered year on. ⚠️ <b>The PPDO count is never queried for a caller without
    /// <c>CanReviewAllOffices</c></b> — this runs on every portal page, for every user who can open
    /// Budget Planning.
    /// </para>
    ///
    /// <para>
    /// ⚠️ <b>Returned is a state, not a message.</b> <c>ReturnedByPpdo</c> is returned to the whole
    /// office; <c>Draft</c> with <c>RETURN_DH</c> as its latest hand-off is returned to the encoders,
    /// so it is not reported to a department head, who did it. A Draft that was never submitted is
    /// not returned. Nothing is marked read — the notice clears when the office submits again.
    /// </para>
    /// </summary>
    Task<ServiceResult<AipReviewNotificationsDto>> GetForCallerAsync(User caller, CancellationToken ct = default);
}
