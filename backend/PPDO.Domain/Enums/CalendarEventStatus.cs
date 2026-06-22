namespace PPDO.Domain.Enums;

/// <summary>
/// Approval state of a calendar event.
///
/// Office events created by non-admins start <see cref="Pending"/> and must be reviewed by an
/// Admin/SuperAdmin before becoming visible to all users. Admin/SuperAdmin-created events and all
/// existing (pre-v1.1.1) rows are <see cref="Approved"/>. Personal events are private and are not
/// subject to approval.
///
/// Stored as an int (default EF Core enum mapping). Added in v1.1.1 (RAL-82).
/// </summary>
public enum CalendarEventStatus
{
    /// <summary>Awaiting admin review. Visible only to the creator.</summary>
    Pending = 0,

    /// <summary>Approved (or never required approval). Visible to all authenticated users.</summary>
    Approved = 1,

    /// <summary>Rejected by an admin. Visible only to the creator, with the rejection reason.</summary>
    Rejected = 2,
}
