using PPDO.Domain.Enums;

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

    // ── Approval workflow (v1.1.1, RAL-82) ──────────────────────────────────────

    /// <summary>
    /// Approval state. Office events created by non-admins start <see cref="CalendarEventStatus.Pending"/>;
    /// admin-created events and all legacy rows are <see cref="CalendarEventStatus.Approved"/>.
    /// </summary>
    public CalendarEventStatus Status { get; set; }

    /// <summary>FK → User (Admin/SuperAdmin) who reviewed this event. Null until reviewed.</summary>
    public Guid? ReviewedById { get; set; }

    /// <summary>UTC timestamp of the review decision. Null until reviewed.</summary>
    public DateTime? ReviewedAt { get; set; }

    /// <summary>Reason shown to the creator when an event is rejected. Max 500 characters.</summary>
    public string? RejectionReason { get; set; }

    // ── Audit ──────────────────────────────────────────────────────────────────

    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    // ── Navigation ─────────────────────────────────────────────────────────────

    /// <summary>User who created the event.</summary>
    public User? CreatedBy { get; set; }

    /// <summary>Admin/SuperAdmin who reviewed the event. Null until reviewed.</summary>
    public User? ReviewedBy { get; set; }
}
