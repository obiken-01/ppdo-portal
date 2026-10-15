using PPDO.Domain.Entities;

namespace PPDO.Domain.Interfaces;

/// <summary>
/// Repository for <see cref="Notification"/> (v1.9.0 — PPDO-221).
///
/// <para>
/// ⚠️ <b>Every read and update takes the caller's user id and filters on it in SQL.</b> There is no
/// method that reads or marks a row by id alone, so no request can reach another user's feed: an id
/// that belongs to someone else matches nothing, exactly like an id that does not exist.
/// </para>
/// </summary>
public interface INotificationRepository
{
    /// <summary>
    /// Stages the rows on the shared context. <b>Not saved here</b>: they are written by the triggering
    /// event's own <c>SaveChangesAsync</c>, so the event and its notifications land together (spec
    /// decision 6).
    /// </summary>
    Task AddRangeAsync(IReadOnlyList<Notification> rows, CancellationToken ct = default);

    /// <summary>The user's unread rows created at or after <paramref name="since"/>: one <c>COUNT</c>.</summary>
    Task<int> CountUnreadAsync(Guid userId, DateTime since, CancellationToken ct = default);

    /// <summary>One page of the user's rows created at or after <paramref name="since"/>, newest first, and the total.</summary>
    Task<(IReadOnlyList<Notification> Items, int Total)> GetPageAsync(
        Guid userId, DateTime since, bool unreadOnly, int skip, int take, CancellationToken ct = default);

    /// <summary>
    /// Marks the user's row read if it is not already. Returns false when the user has no row with
    /// that id, whether it is missing or another user's (the caller answers 404 either way).
    /// Idempotent: an already-read row of the user's returns true and keeps its first read time.
    /// </summary>
    Task<bool> MarkReadAsync(long id, Guid userId, DateTime at, CancellationToken ct = default);

    /// <summary>Marks every unread row of the user read: one set-based <c>UPDATE</c>. Returns the rows changed.</summary>
    Task<int> MarkAllReadAsync(Guid userId, DateTime at, CancellationToken ct = default);
}
