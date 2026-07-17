using PPDO.Application.Common;
using PPDO.Application.DTOs.Dashboard;
using PPDO.Domain.Entities;
using PPDO.Domain.Enums;
using PPDO.Domain.Interfaces;

namespace PPDO.Application.Services;

/// <summary>
/// Dashboard data — calendar events (office + personal + PH holidays) and stat counts.
/// Calendar approval workflow added in v1.1.1 (RAL-84).
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

        // Visibility rules (RAL-84):
        //   - Personal events: always visible to creator (repo already scopes by userId)
        //   - Office Approved: visible to all
        //   - Office Pending/Rejected: visible only to creator
        IEnumerable<CalendarEventDto> mapped = dbEvents
            .Where(e =>
                e.EventType == "Personal" ||
                e.Status == CalendarEventStatus.Approved ||
                e.CreatedById == userId)
            .Select(e => new CalendarEventDto(
                e.Id,
                e.Title,
                e.Description,
                e.StartDate,
                e.EndDate,
                e.IsAllDay,
                e.EventType,
                null,
                e.Status,
                e.RejectionReason,
                e.CreatedById));

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

        bool isAdmin = IsAdmin(requester);

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
            // Office events: admin → Approved immediately; non-admin → Pending for review
            // Personal events: approval not applicable (Pending as CLR default is fine — filtered by creator)
            Status = dto.EventType == "Office"
                ? (isAdmin ? CalendarEventStatus.Approved : CalendarEventStatus.Pending)
                : CalendarEventStatus.Approved,  // Personal events don't require approval
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
            null,
            entity.Status,
            null,
            entity.CreatedById);

        return ServiceResult<CalendarEventDto>.Ok(result);
    }

    /// <inheritdoc />
    public async Task<ServiceResult<IReadOnlyList<PendingCalendarEventDto>>> GetPendingEventsAsync(
        User caller,
        CancellationToken cancellationToken = default)
    {
        if (!IsAdmin(caller))
            return ServiceResult<IReadOnlyList<PendingCalendarEventDto>>.Forbidden(
                "Admin or SuperAdmin role required.");

        IReadOnlyList<CalendarEvent> all = await _events.GetAllAsync(cancellationToken);

        IReadOnlyList<PendingCalendarEventDto> pending = all
            .Where(e => e.Status == CalendarEventStatus.Pending && e.EventType == "Office")
            .OrderBy(e => e.CreatedAt)
            .Select(e => new PendingCalendarEventDto(
                e.Id,
                e.Title,
                e.Description,
                e.StartDate,
                e.EndDate,
                e.IsAllDay,
                e.CreatedById,
                e.CreatedBy?.FullName ?? string.Empty,
                e.CreatedAt))
            .ToList();

        return ServiceResult<IReadOnlyList<PendingCalendarEventDto>>.Ok(pending);
    }

    /// <inheritdoc />
    public async Task<ServiceResult<CalendarEventDto>> ReviewEventAsync(
        User caller,
        Guid id,
        ReviewCalendarEventDto dto,
        CancellationToken cancellationToken = default)
    {
        if (!IsAdmin(caller))
            return ServiceResult<CalendarEventDto>.Forbidden("Admin or SuperAdmin role required.");

        CalendarEvent? entity = await _events.GetByIdAsync(id, cancellationToken);
        if (entity is null)
            return ServiceResult<CalendarEventDto>.NotFound($"Calendar event {id} not found.");

        if (dto.Approved)
        {
            entity.Status          = CalendarEventStatus.Approved;
            entity.RejectionReason = null;
        }
        else
        {
            if (string.IsNullOrWhiteSpace(dto.RejectionReason))
                return ServiceResult<CalendarEventDto>.BadRequest(
                    "RejectionReason is required when rejecting an event.");

            entity.Status          = CalendarEventStatus.Rejected;
            entity.RejectionReason = dto.RejectionReason.Trim();
        }

        entity.ReviewedById = caller.Id;
        entity.ReviewedAt   = DateTime.UtcNow;

        await _events.UpdateAsync(entity, cancellationToken);
        await _events.SaveChangesAsync(cancellationToken);

        return ServiceResult<CalendarEventDto>.Ok(MapToDto(entity));
    }

    /// <inheritdoc />
    public async Task<ServiceResult<bool>> DeleteEventAsync(
        User caller,
        Guid id,
        CancellationToken cancellationToken = default)
    {
        CalendarEvent? entity = await _events.GetByIdAsync(id, cancellationToken);
        if (entity is null)
            return ServiceResult<bool>.NotFound($"Calendar event {id} not found.");

        // Owner-only — no admin override (RAL-168, same rule as UpdateEventAsync).
        if (entity.CreatedById != caller.Id)
            return ServiceResult<bool>.Forbidden("You can only delete your own events.");

        await _events.DeleteAsync(entity, cancellationToken);
        await _events.SaveChangesAsync(cancellationToken);

        return ServiceResult<bool>.Ok(true);
    }

    /// <inheritdoc />
    public async Task<ServiceResult<CalendarEventDto>> UpdateEventAsync(
        User caller,
        Guid id,
        UpdateCalendarEventDto dto,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(dto.Title))
            return ServiceResult<CalendarEventDto>.BadRequest("Title is required.");

        CalendarEvent? entity = await _events.GetByIdAsync(id, cancellationToken);
        if (entity is null)
            return ServiceResult<CalendarEventDto>.NotFound($"Calendar event {id} not found.");

        // Owner-only — no admin override (deliberate difference from DeleteEventAsync).
        if (entity.CreatedById != caller.Id)
            return ServiceResult<CalendarEventDto>.Forbidden("You can only edit your own events.");

        entity.Title       = dto.Title.Trim();
        entity.Description = dto.Description?.Trim();
        entity.StartDate   = dto.StartDate;
        entity.EndDate     = dto.EndDate;
        entity.IsAllDay    = dto.IsAllDay;

        // A non-admin's edit to a reviewed Office event (Approved or Rejected) re-triggers
        // admin review, mirroring CreateEventAsync's approval rule — the old review no longer
        // applies to the edited content. An admin editing their own event stays Approved
        // (matches CreateEventAsync's admin bypass). Personal events never need approval.
        if (entity.EventType == "Office"
            && entity.Status != CalendarEventStatus.Pending
            && !IsAdmin(caller))
        {
            entity.Status          = CalendarEventStatus.Pending;
            entity.RejectionReason = null;
            entity.ReviewedById    = null;
            entity.ReviewedAt      = null;
        }

        await _events.UpdateAsync(entity, cancellationToken);
        await _events.SaveChangesAsync(cancellationToken);

        return ServiceResult<CalendarEventDto>.Ok(MapToDto(entity));
    }

    /// <inheritdoc />
    public async Task<DashboardStatsDto> GetStatsAsync(CancellationToken cancellationToken = default)
    {
        IReadOnlyList<PurchaseRequest> prs   = await _prs.GetAllAsync(cancellationToken);
        IReadOnlyList<ItemMaster>      items = await _items.GetAllAsync(cancellationToken);

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

    // ── helpers ───────────────────────────────────────────────────────────────

    private static bool IsAdmin(User caller) =>
        caller.Role is UserRole.Admin or UserRole.SuperAdmin;

    private static CalendarEventDto MapToDto(CalendarEvent e) => new(
        e.Id,
        e.Title,
        e.Description,
        e.StartDate,
        e.EndDate,
        e.IsAllDay,
        e.EventType,
        null,
        e.Status,
        e.RejectionReason,
        e.CreatedById);
}
