namespace PPDO.Application.DTOs.Dashboard;

/// <summary>
/// A pending calendar event shown in the admin review queue
/// (<c>GET /api/dashboard/events/pending</c>).
/// </summary>
public sealed record PendingCalendarEventDto(
    Guid     Id,
    string   Title,
    string?  Description,
    DateTime StartDate,
    DateTime? EndDate,
    bool     IsAllDay,
    Guid     CreatedById,
    string   CreatedByName,
    DateTime CreatedAt);
