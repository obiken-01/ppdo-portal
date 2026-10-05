using Microsoft.EntityFrameworkCore;
using PPDO.Domain.Entities;
using PPDO.Domain.Enums;
using PPDO.Domain.Interfaces;
using PPDO.Infrastructure.Data;

namespace PPDO.Infrastructure.Repositories;

/// <summary>
/// EF Core implementation of <see cref="ICalendarEventRepository"/>.
/// </summary>
public sealed class CalendarEventRepository : Repository<CalendarEvent>, ICalendarEventRepository
{
    public CalendarEventRepository(AppDbContext context) : base(context) { }

    /// <inheritdoc />
    public async Task<IReadOnlyList<CalendarEvent>> GetByDateRangeAsync(
        DateTime from,
        DateTime to,
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        return await _context.CalendarEvents
            .Where(e =>
                e.StartDate < to &&
                (e.EndDate == null ? e.StartDate >= from : e.EndDate > from) &&
                (e.EventType == "Office" || e.CreatedById == userId))
            .OrderBy(e => e.StartDate)
            .ToListAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<CalendarEvent>> GetPendingOfficeEventsAsync(
        CancellationToken cancellationToken = default)
        => await _context.CalendarEvents
            .Where(e => e.Status == CalendarEventStatus.Pending && e.EventType == "Office")
            .OrderBy(e => e.CreatedAt)
            .ToListAsync(cancellationToken);
}
