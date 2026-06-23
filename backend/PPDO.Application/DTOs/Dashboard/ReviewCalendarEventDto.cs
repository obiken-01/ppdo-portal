namespace PPDO.Application.DTOs.Dashboard;

/// <summary>
/// Request body for <c>PUT /api/dashboard/events/{id}/review</c>.
/// When <see cref="Approved"/> is false, <see cref="RejectionReason"/> is required.
/// </summary>
public sealed record ReviewCalendarEventDto(bool Approved, string? RejectionReason);
