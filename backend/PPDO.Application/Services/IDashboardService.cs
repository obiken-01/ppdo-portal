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
    ///   - Office events (shared — all users see these)
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
    ///
    /// Returns:
    ///   <see cref="ServiceErrorCode.BadRequest"/> — title is empty or EventType is invalid.
    /// </summary>
    Task<ServiceResult<CalendarEventDto>> CreateEventAsync(
        User requester,
        CreateCalendarEventDto dto,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns grouped stat card counts for the Main Dashboard:
    ///   - PR counts by status
    ///   - Item master counts
    /// </summary>
    Task<DashboardStatsDto> GetStatsAsync(CancellationToken cancellationToken = default);
}
