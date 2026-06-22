using PPDO.Domain.Enums;

namespace PPDO.Domain.Entities;

/// <summary>
/// A rich-text announcement authored by Admin/SuperAdmin.
/// Published announcements appear on the public landing page.
///
/// Status workflow: Draft → Published → Archived.
/// Published can be unpublished back to Draft.
/// Hard delete is allowed only for Draft and Archived — Published requires archiving first.
///
/// Content is sanitized server-side by Ganss.Xss before persistence. Added in v1.1.1 (RAL-83).
/// </summary>
public sealed class Announcement
{
    /// <summary>Primary key (UUID).</summary>
    public Guid Id { get; set; }

    /// <summary>Announcement title. Required. Max 200 characters.</summary>
    public string Title { get; set; } = string.Empty;

    /// <summary>Sanitized HTML content from the rich-text editor.</summary>
    public string Content { get; set; } = string.Empty;

    /// <summary>Publication state.</summary>
    public AnnouncementStatus Status { get; set; }

    /// <summary>Set once on first publish; never overwritten on subsequent publishes.</summary>
    public DateTime? PublishedAt { get; set; }

    /// <summary>FK → User who authored this announcement.</summary>
    public Guid CreatedById { get; set; }

    // ── Audit ──────────────────────────────────────────────────────────────────

    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    // ── Navigation ─────────────────────────────────────────────────────────────

    /// <summary>User who created the announcement.</summary>
    public User? CreatedBy { get; set; }
}
