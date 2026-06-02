using PPDO.Domain.Entities;

namespace PPDO.Domain.Interfaces;

/// <summary>
/// Data access for <see cref="CalendarEvent"/> entities.
/// Extends <see cref="IRepository{CalendarEvent}"/> — CRUD methods are inherited.
/// </summary>
public interface ICalendarEventRepository : IRepository<CalendarEvent>
{
    /// <summary>
    /// Returns all events that overlap the given UTC date range, scoped to the user:
    ///   - Office events  → always included (visible to everyone).
    ///   - Personal events → only included when <see cref="CalendarEvent.CreatedById"/> == <paramref name="userId"/>.
    /// </summary>
    Task<IReadOnlyList<CalendarEvent>> GetByDateRangeAsync(
        DateTime from,
        DateTime to,
        Guid userId,
        CancellationToken cancellationToken = default);
}
