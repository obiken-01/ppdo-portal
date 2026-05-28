namespace PPDO.Domain.Entities;

/// <summary>
/// An external link (Google Sheet, Drive folder, Doc, or any URL) organised by category.
/// Replaces the PPDO Google Site as the central hub for office resources.
///
/// Permission rules:
///   SuperAdmin / Admin                     — full manage: add, edit, delete, reorder
///   Staff (CanManageResourceLinks = true)  — add only; cannot edit or delete
///   Staff (CanManageResourceLinks = false) — view only
///   Observer                               — view only
///
/// <see cref="IsAdminCreated"/> distinguishes system/Admin-created links from Staff-submitted ones.
/// <see cref="SubmittedById"/> is nullable to support seed data and system-created links that
/// have no specific submitting user.
/// </summary>
public sealed class ResourceLink
{
    /// <summary>Primary key.</summary>
    public Guid Id { get; set; }

    /// <summary>Display title of the link. e.g. "PR Monitoring". Required, max 200 chars.</summary>
    public string Title { get; set; } = string.Empty;

    /// <summary>
    /// The target URL — Google Sheet, Drive folder, Doc, or any URL.
    /// Required. Placeholder value until real Google Drive URLs are configured.
    /// </summary>
    public string Url { get; set; } = string.Empty;

    /// <summary>Category name that groups related links. e.g. "Supply &amp; Property Management".</summary>
    public string Category { get; set; } = string.Empty;

    /// <summary>Controls the display order of the category in the Resources page UI (ascending).</summary>
    public int CategoryOrder { get; set; }

    /// <summary>Controls the display order of this link within its category (ascending).</summary>
    public int LinkOrder { get; set; }

    /// <summary>Soft-delete flag. False hides the link from all users without deleting the record.</summary>
    public bool IsActive { get; set; } = true;

    /// <summary>
    /// True when created by Admin or SuperAdmin — these links are always visible.
    /// False when submitted by Staff — pending Admin approval flow (future: <c>IsApproved</c> flag).
    /// </summary>
    public bool IsAdminCreated { get; set; }

    /// <summary>
    /// FK to the <see cref="User"/> who added this link.
    /// Null for Admin/SuperAdmin-created seed data and system links where tracking
    /// a specific submitter is not applicable.
    /// </summary>
    public Guid? SubmittedById { get; set; }

    // ── Audit ─────────────────────────────────────────────────────────────────

    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    // ── Navigation ────────────────────────────────────────────────────────────

    /// <summary>The user who submitted this link. Null for seed data / admin-created links.</summary>
    public User? SubmittedBy { get; set; }
}
