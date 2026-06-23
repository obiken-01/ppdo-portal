using PPDO.Application.Common;
using PPDO.Application.DTOs.Dashboard;
using PPDO.Domain.Entities;

namespace PPDO.Application.Services;

/// <summary>
/// Dashboard data — calendar events and stat card counts.
/// Implemented in <c>DashboardService.cs</c>.
/// </summary>
public interface IDashboardService
{
    /// <summary>
    /// Returns all events for the given year/month visible to <paramref name="userId"/>:
    ///   - Approved Office events (visible to all users)
    ///   - Caller's own Pending/Rejected Office events
    ///   - Personal events created by <paramref name="userId"/> only
    ///   - PH public holidays (from Nager.Date or static fallback)
    /// </summary>
    Task<IReadOnlyList<CalendarEventDto>> GetEventsAsync(
        int year,
        int month,
        Guid userId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Creates a new calendar event (Office or Personal).
    /// Non-admin Office events are created as Pending; admin Office events as Approved.
    /// Personal events bypass the approval workflow.
    ///
    /// Returns:
    ///   <see cref="ServiceErrorCode.BadRequest"/> — title is empty or EventType is invalid.
    /// </summary>
    Task<ServiceResult<CalendarEventDto>> CreateEventAsync(
        User requester,
        CreateCalendarEventDto dto,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns all Pending Office events ordered by CreatedAt ASC. Admin/SuperAdmin only.
    /// </summary>
    Task<ServiceResult<IReadOnlyList<PendingCalendarEventDto>>> GetPendingEventsAsync(
        User caller,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Approves or rejects a Pending calendar event.
    /// Rejection requires a non-empty <c>RejectionReason</c>.
    /// Sets <c>ReviewedById</c> and <c>ReviewedAt</c>.
    /// Admin/SuperAdmin only.
    /// </summary>
    Task<ServiceResult<CalendarEventDto>> ReviewEventAsync(
        User caller,
        Guid id,
        ReviewCalendarEventDto dto,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Hard-deletes a calendar event. Creator can delete their own; Admin/SuperAdmin can delete any.
    /// </summary>
    Task<ServiceResult<bool>> DeleteEventAsync(
        User caller,
        Guid id,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns grouped stat card counts for the Main Dashboard:
    ///   - PR counts by status
    ///   - Item master counts
    /// </summary>
    Task<DashboardStatsDto> GetStatsAsync(CancellationToken cancellationToken = default);
}
