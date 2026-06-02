namespace PPDO.Application.DTOs.Dashboard;

/// <summary>
/// Request body for <c>POST /api/dashboard/events</c>.
/// EventType must be "Office" or "Personal".
/// </summary>
public sealed record CreateCalendarEventDto(
    string   Title,
    string?  Description,
    DateTime StartDate,
    DateTime? EndDate,
    bool     IsAllDay,
    /// <summary>"Office" | "Personal"</summary>
    string   EventType);
