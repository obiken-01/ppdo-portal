using Microsoft.EntityFrameworkCore;
using PPDO.Domain.Entities;
using PPDO.Domain.Interfaces;
using PPDO.Infrastructure.Data;

namespace PPDO.Infrastructure.Repositories;

/// <summary>EF Core implementation of <see cref="INotificationRepository"/> (PPDO-221).</summary>
public sealed class NotificationRepository : INotificationRepository
{
    private readonly AppDbContext _context;

    public NotificationRepository(AppDbContext context) => _context = context;

    /// <inheritdoc />
    public async Task AddRangeAsync(IReadOnlyList<Notification> rows, CancellationToken ct = default)
    {
        if (rows.Count == 0) return;
        await _context.Notifications.AddRangeAsync(rows, ct);
    }

    /// <inheritdoc />
    public Task<int> CountUnreadAsync(Guid userId, DateTime since, CancellationToken ct = default)
        => _context.Notifications
            .CountAsync(n => n.UserId == userId && n.ReadAt == null && n.CreatedAt >= since, ct);

    /// <inheritdoc />
    public async Task<(IReadOnlyList<Notification> Items, int Total)> GetPageAsync(
        Guid userId, DateTime since, bool unreadOnly, int skip, int take, CancellationToken ct = default)
    {
        IQueryable<Notification> query = _context.Notifications
            .AsNoTracking()
            .Where(n => n.UserId == userId && n.CreatedAt >= since);
        if (unreadOnly) query = query.Where(n => n.ReadAt == null);

        // Sequential: one DbContext.
        int total = await query.CountAsync(ct);
        List<Notification> items = await query
            .OrderByDescending(n => n.CreatedAt).ThenByDescending(n => n.Id)
            .Skip(skip).Take(take)
            .ToListAsync(ct);
        return (items, total);
    }

    /// <inheritdoc />
    public async Task<bool> MarkReadAsync(long id, Guid userId, DateTime at, CancellationToken ct = default)
    {
        // ⚠️ user_id is in both WHEREs: an id that is another user's matches nothing, like a missing one.
        int changed = await _context.Notifications
            .Where(n => n.Id == id && n.UserId == userId && n.ReadAt == null)
            .ExecuteUpdateAsync(u => u.SetProperty(n => n.ReadAt, at), ct);
        return changed > 0
            || await _context.Notifications.AnyAsync(n => n.Id == id && n.UserId == userId, ct);
    }

    /// <inheritdoc />
    public Task<int> MarkAllReadAsync(Guid userId, DateTime at, CancellationToken ct = default)
        => _context.Notifications
            .Where(n => n.UserId == userId && n.ReadAt == null)
            .ExecuteUpdateAsync(u => u.SetProperty(n => n.ReadAt, at), ct);
}
