using PPDO.Application.Common;
using PPDO.Application.DTOs.Notifications;
using PPDO.Domain.Entities;

namespace PPDO.Application.Services;

/// <summary>
/// What a triggering event hands <see cref="INotificationService.StageAsync"/>: the frozen text, the
/// link, and the users who can open the target.
/// </summary>
public sealed record NotificationBatch(string Text, string Link, IReadOnlyList<User> Recipients);

/// <summary>
/// The notification feed (v1.9.0 — PPDO-221, <c>docs/v1.9/Notification_Centre_Spec.md</c>).
///
/// <para>
/// Not <see cref="IAipNotificationService"/>, which is the older, state-based "what is still pending"
/// read behind the sidebar pills and banners. Both stay (decision 5): this one says what happened.
/// </para>
/// </summary>
public interface INotificationService
{
    /// <summary>
    /// Stages one row per recipient for an event, on the shared context, so they are written by the
    /// event's own save (decision 6). Call it <b>before</b> that save.
    ///
    /// <para>
    /// ⚠️ <b>Never throws and never fails the event.</b> <paramref name="build"/>, which loads whatever
    /// it needs and works out the recipients, runs inside a catch; a failure is logged at Error and
    /// nothing is staged. A missed notification must never undo a return.
    /// </para>
    ///
    /// <para>⚠️ <b>The actor never receives an entry</b>, whatever <paramref name="build"/> returns.</para>
    /// </summary>
    Task StageAsync(
        string type, User actor,
        Func<INotificationAudience, CancellationToken, Task<NotificationBatch?>> build,
        CancellationToken ct = default);

    /// <summary>The caller's unread entries of the last 90 days.</summary>
    Task<ServiceResult<NotificationCountDto>> GetUnreadCountAsync(User caller, CancellationToken ct = default);

    /// <summary>The caller's entries of the last 90 days, newest first. Page from 1; page size 1–50, default 20.</summary>
    Task<ServiceResult<NotificationPageDto>> ListAsync(
        User caller, bool unreadOnly, int page, int pageSize, CancellationToken ct = default);

    /// <summary>Marks one of the caller's entries read. Idempotent. Another user's id is NotFound, like a missing one.</summary>
    Task<ServiceResult<bool>> MarkReadAsync(User caller, long id, CancellationToken ct = default);

    /// <summary>Marks every unread entry of the caller read.</summary>
    Task<ServiceResult<bool>> MarkAllReadAsync(User caller, CancellationToken ct = default);
}
