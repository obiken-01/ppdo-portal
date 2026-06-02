namespace PPDO.Domain.Entities;

/// <summary>
/// An event shown on the Main Dashboard calendar.
///
/// EventType:
///   "Office"   — shared; visible to all authenticated users.
///   "Personal" — private; visible only to the creator (CreatedById).
///
/// PH public holidays are NOT stored here — they are fetched at runtime
/// from Nager.Date (or a static fallback) by DashboardService and merged
/// into the response alongside DB events.
///
/// Timestamps are stored as UTC. FullCalendar on the frontend converts to Manila time (UTC+8).
/// </summary>
public sealed class CalendarEvent
{
    /// <summary>Primary key.</summary>
    public Guid Id { get; set; }

    /// <summary>Event title. Required. Max 200 characters.</summary>
    public string Title { get; set; } = string.Empty;

    /// <summary>Optional description / notes.</summary>
    public string? Description { get; set; }

    /// <summary>
    /// Start of the event (UTC).
    /// For all-day events this is midnight UTC on the start date.
    /// </summary>
    public DateTime StartDate { get; set; }

    /// <summary>
    /// End of the event (UTC). Null means same-day (EndDate = StartDate).
    /// For multi-day all-day events this should be midnight UTC of the day AFTER the last day
    /// (FullCalendar exclusive-end convention).
    /// </summary>
    public DateTime? EndDate { get; set; }

    /// <summary>
    /// True when the event spans the whole day rather than a specific time slot.
    /// </summary>
    public bool IsAllDay { get; set; }

    /// <summary>
    /// "Office" = shared across all users.
    /// "Personal" = visible only to the creator.
    /// </summary>
    public string EventType { get; set; } = "Office";

    /// <summary>FK → User who created this event.</summary>
    public Guid CreatedById { get; set; }

    // ── Audit ──────────────────────────────────────────────────────────────────

    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    // ── Navigation ─────────────────────────────────────────────────────────────

    /// <summary>User who created the event.</summary>
    public User? CreatedBy { get; set; }
}
