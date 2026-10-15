using Microsoft.Extensions.Logging;
using PPDO.Application.Common;
using PPDO.Application.DTOs.Notifications;
using PPDO.Domain.Entities;
using PPDO.Domain.Interfaces;

namespace PPDO.Application.Services;

/// <summary>Implementation of <see cref="INotificationService"/> (PPDO-221).</summary>
public sealed class NotificationService : INotificationService
{
    /// <summary>Entries older than this are not shown (decision 8; no purge job in v1.9.0).</summary>
    public static readonly TimeSpan Window = TimeSpan.FromDays(90);

    public const int DefaultPageSize = 20;
    public const int MaxPageSize     = 50;
    public const string NotFoundMessage = "Notification not found.";

    private readonly INotificationRepository      _repo;
    private readonly INotificationAudience        _audience;
    private readonly ILogger<NotificationService> _logger;
    private readonly TimeProvider                 _clock;

    public NotificationService(
        INotificationRepository      repo,
        INotificationAudience        audience,
        ILogger<NotificationService> logger,
        TimeProvider?                clock = null)
    {
        _repo     = repo;
        _audience = audience;
        _logger   = logger;
        _clock    = clock ?? TimeProvider.System;
    }

    private DateTime Now() => _clock.GetUtcNow().UtcDateTime;

    public async Task StageAsync(
        string type, User actor,
        Func<INotificationAudience, CancellationToken, Task<NotificationBatch?>> build,
        CancellationToken ct = default)
    {
        try
        {
            NotificationBatch? batch = await build(_audience, ct);
            if (batch is null) return;

            DateTime now  = Now();
            string   text = NotificationText.Fit(batch.Text);
            List<Notification> rows = batch.Recipients
                .Select(u => u.Id)
                .Where(id => id != actor.Id)          // never the actor (decision 3)
                .Distinct()
                .Select(id => new Notification
                {
                    UserId = id, Type = type, Text = text, Link = batch.Link, CreatedAt = now,
                })
                .ToList();

            await _repo.AddRangeAsync(rows, ct);
            _logger.LogInformation(
                "Notifications staged. Type: {Type}, Recipients: {RecipientCount}, UserId: {UserId}",
                type, rows.Count, actor.Id);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // ⚠️ Swallowed on purpose (decision 6): the event goes ahead without its notifications.
            _logger.LogError(ex,
                "Notification recipients could not be resolved; the event continues without them. Type: {Type}, UserId: {UserId}",
                type, actor.Id);
        }
    }

    public async Task<ServiceResult<NotificationCountDto>> GetUnreadCountAsync(User caller, CancellationToken ct = default)
        => ServiceResult<NotificationCountDto>.Ok(
            new NotificationCountDto(await _repo.CountUnreadAsync(caller.Id, Now() - Window, ct)));

    public async Task<ServiceResult<NotificationPageDto>> ListAsync(
        User caller, bool unreadOnly, int page, int pageSize, CancellationToken ct = default)
    {
        int size = pageSize <= 0 ? DefaultPageSize : Math.Min(pageSize, MaxPageSize);
        int skip = (Math.Max(page, 1) - 1) * size;

        (IReadOnlyList<Notification> items, int total) =
            await _repo.GetPageAsync(caller.Id, Now() - Window, unreadOnly, skip, size, ct);

        return ServiceResult<NotificationPageDto>.Ok(new NotificationPageDto(
            items.Select(n => new NotificationDto(
                n.Id, n.Type, n.Text, n.Link,
                DateTime.SpecifyKind(n.CreatedAt, DateTimeKind.Utc),
                n.ReadAt is DateTime r ? DateTime.SpecifyKind(r, DateTimeKind.Utc) : null)).ToList(),
            total));
    }

    public async Task<ServiceResult<bool>> MarkReadAsync(User caller, long id, CancellationToken ct = default)
        => await _repo.MarkReadAsync(id, caller.Id, Now(), ct)
            ? ServiceResult<bool>.Ok(true)
            : ServiceResult<bool>.NotFound(NotFoundMessage);

    public async Task<ServiceResult<bool>> MarkAllReadAsync(User caller, CancellationToken ct = default)
    {
        await _repo.MarkAllReadAsync(caller.Id, Now(), ct);
        return ServiceResult<bool>.Ok(true);
    }
}
