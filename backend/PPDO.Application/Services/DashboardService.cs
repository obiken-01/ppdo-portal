using PPDO.Application.Common;
using PPDO.Application.DTOs.Dashboard;
using PPDO.Domain.Entities;
using PPDO.Domain.Enums;
using PPDO.Domain.Interfaces;

namespace PPDO.Application.Services;

/// <summary>
/// Dashboard data — calendar events (office + personal + PH holidays) and stat counts.
/// </summary>
public sealed class DashboardService : IDashboardService
{
    private static readonly HashSet<string> ValidCreateTypes =
        new(StringComparer.OrdinalIgnoreCase) { "Office", "Personal" };

    private readonly ICalendarEventRepository      _events;
    private readonly IHolidayProvider              _holidays;
    private readonly IRepository<PurchaseRequest>  _prs;
    private readonly IRepository<ItemMaster>       _items;

    public DashboardService(
        ICalendarEventRepository     events,
        IHolidayProvider             holidays,
        IRepository<PurchaseRequest> prs,
        IRepository<ItemMaster>      items)
    {
        _events   = events;
        _holidays = holidays;
        _prs      = prs;
        _items    = items;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<CalendarEventDto>> GetEventsAsync(
        int year,
        int month,
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        DateTime from = new(year, month, 1, 0, 0, 0, DateTimeKind.Utc);
        DateTime to   = from.AddMonths(1);

        // Fetch DB events and PH holidays concurrently.
        Task<IReadOnlyList<CalendarEvent>> dbTask =
            _events.GetByDateRangeAsync(from, to, userId, cancellationToken);

        IReadOnlyList<CalendarEventDto> allHolidays;
        try
        {
            allHolidays = await _holidays.GetPhHolidaysAsync(year, cancellationToken);
        }
        catch
        {
            // Holiday provider failure must never break the calendar.
            allHolidays = [];
        }

        IReadOnlyList<CalendarEvent> dbEvents = await dbTask;

        IEnumerable<CalendarEventDto> mapped = dbEvents.Select(e => new CalendarEventDto(
            e.Id,
            e.Title,
            e.Description,
            e.StartDate,
            e.EndDate,
            e.IsAllDay,
            e.EventType,
            null));

        // Filter holidays to the requested month only.
        IEnumerable<CalendarEventDto> monthHolidays =
            allHolidays.Where(h => h.StartDate.Year == year && h.StartDate.Month == month);

        return mapped.Concat(monthHolidays)
                     .OrderBy(e => e.StartDate)
                     .ToList();
    }

    /// <inheritdoc />
    public async Task<ServiceResult<CalendarEventDto>> CreateEventAsync(
        User requester,
        CreateCalendarEventDto dto,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(dto.Title))
            return ServiceResult<CalendarEventDto>.BadRequest("Title is required.");

        if (!ValidCreateTypes.Contains(dto.EventType))
            return ServiceResult<CalendarEventDto>.BadRequest(
                $"EventType must be 'Office' or 'Personal'. Got: '{dto.EventType}'.");

        CalendarEvent entity = new()
        {
            Id          = Guid.NewGuid(),
            Title       = dto.Title.Trim(),
            Description = dto.Description?.Trim(),
            StartDate   = dto.StartDate,
            EndDate     = dto.EndDate,
            IsAllDay    = dto.IsAllDay,
            EventType   = dto.EventType,
            CreatedById = requester.Id,
        };

        await _events.AddAsync(entity, cancellationToken);
        await _events.SaveChangesAsync(cancellationToken);

        CalendarEventDto result = new(
            entity.Id,
            entity.Title,
            entity.Description,
            entity.StartDate,
            entity.EndDate,
            entity.IsAllDay,
            entity.EventType,
            null);

        return ServiceResult<CalendarEventDto>.Ok(result);
    }

    /// <inheritdoc />
    public async Task<DashboardStatsDto> GetStatsAsync(CancellationToken cancellationToken = default)
    {
        Task<IReadOnlyList<PurchaseRequest>> prsTask   = _prs.GetAllAsync(cancellationToken);
        Task<IReadOnlyList<ItemMaster>>      itemsTask = _items.GetAllAsync(cancellationToken);

        await Task.WhenAll(prsTask, itemsTask);

        IReadOnlyList<PurchaseRequest> prs   = prsTask.Result;
        IReadOnlyList<ItemMaster>      items = itemsTask.Result;

        return new DashboardStatsDto
        {
            TotalPRs               = prs.Count,
            OpenPRs                = prs.Count(p => p.Status == PRStatus.Open),
            PartiallyDeliveredPRs  = prs.Count(p => p.Status == PRStatus.PartiallyDelivered),
            FullyDeliveredPRs      = prs.Count(p => p.Status == PRStatus.FullyDelivered),
            TotalItems             = items.Count,
            NewItemsPendingReview  = items.Count(i => i.IsNewItem),
        };
    }
}
