namespace PPDO.Domain.Enums;

/// <summary>
/// Publication state of an announcement.
/// Draft → Published → Archived. Published can be unpublished back to Draft.
/// Stored as int (default EF Core enum mapping). Added in v1.1.1 (RAL-83).
/// </summary>
public enum AnnouncementStatus
{
    /// <summary>Not yet visible publicly. Editable.</summary>
    Draft = 0,

    /// <summary>Visible on the public landing page.</summary>
    Published = 1,

    /// <summary>Hidden from public. Cannot be deleted without archiving first.</summary>
    Archived = 2,
}
