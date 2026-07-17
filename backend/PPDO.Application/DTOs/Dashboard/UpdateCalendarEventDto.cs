namespace PPDO.Application.DTOs.Dashboard;

/// <summary>
/// Request body for <c>PUT /api/dashboard/events/{id}</c> (RAL-168).
/// EventType is deliberately not editable — an event's Office/Personal type is fixed at
/// creation. Owner-only: enforced in <c>DashboardService.UpdateEventAsync</c>, not here.
/// </summary>
public sealed record UpdateCalendarEventDto(
    string   Title,
    string?  Description,
    DateTime StartDate,
    DateTime? EndDate,
    bool     IsAllDay);
