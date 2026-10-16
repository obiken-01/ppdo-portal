namespace PPDO.Domain.Entities;

/// <summary>
/// One entry in a user's notification feed (v1.9.0 — PPDO-221,
/// <c>docs/v1.9/Notification_Centre_Spec.md</c> §5). One row per recipient per event.
///
/// <para>
/// ⚠️ <b>The text is frozen at write time</b> (decision 9): a later rename of the proposal, activity
/// or office does not rewrite it. The <see cref="Link"/> is what stays live, so a reader who has since
/// lost access lands on the target's own 404 and learns nothing beyond the frozen sentence.
/// </para>
/// </summary>
public sealed class Notification
{
    /// <summary>Primary key (BIGINT IDENTITY).</summary>
    public long Id { get; set; }

    /// <summary>The recipient. A user only ever reads and updates their own rows.</summary>
    public Guid UserId { get; set; }

    /// <summary>What happened, e.g. <c>ProposalComment</c>, <c>AipReturnedByPpdo</c> (see <c>NotificationTypes</c>).</summary>
    public string Type { get; set; } = string.Empty;

    /// <summary>The sentence shown, frozen at write time. At most 400 characters.</summary>
    public string Text { get; set; } = string.Empty;

    /// <summary>Portal-relative path the entry opens. At most 400 characters.</summary>
    public string Link { get; set; } = string.Empty;

    /// <summary>When the event happened (UTC).</summary>
    public DateTime CreatedAt { get; set; }

    /// <summary>When the recipient opened it or marked all as read (UTC), or null while unread.</summary>
    public DateTime? ReadAt { get; set; }

    public User? User { get; set; }
}
