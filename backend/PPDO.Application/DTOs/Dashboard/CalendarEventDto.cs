using PPDO.Domain.Enums;

namespace PPDO.Application.DTOs.Dashboard;

/// <summary>
/// A calendar event returned by <c>GET /api/dashboard/events</c>.
/// Covers both DB-stored events (Office / Personal) and PH public holidays.
/// Status and RejectionReason are null for holiday entries (not stored in DB).
/// </summary>
public sealed record CalendarEventDto(
    /// <summary>Null for PH holidays (not stored in the DB).</summary>
    Guid?    Id,
    string   Title,
    string?  Description,
    DateTime StartDate,
    DateTime? EndDate,
    bool     IsAllDay,
    /// <summary>"Office" | "Personal" | "Holiday"</summary>
    string   EventType,
    /// <summary>Null for DB events; "Nager.Date" or "Static" for holidays.</summary>
    string?  Source,
    /// <summary>Approval state. Null for holidays.</summary>
    CalendarEventStatus? Status,
    /// <summary>Set when an admin rejects an Office event. Null otherwise.</summary>
    string?  RejectionReason);
