using PPDO.Application.Common;
using PPDO.Application.DTOs.Notifications;
using PPDO.Application.Services;
using PPDO.Domain.Entities;

namespace PPDO.Tests.TestHelpers;

/// <summary>
/// An <see cref="INotificationService"/> that records what each event stages and runs nothing
/// (PPDO-221). A hook test takes the recorded build, runs it against a mocked
/// <see cref="INotificationAudience"/>, and checks the type, text, link and which audience it asked
/// for. Who that audience is lives in <c>NotificationAudienceTests</c>; dropping the actor and
/// surviving a failure live in <c>NotificationServiceTests</c>.
/// </summary>
public sealed class RecordingNotificationService : INotificationService
{
    public sealed record Call(
        string Type, User Actor, Func<INotificationAudience, CancellationToken, Task<NotificationBatch?>> Build);

    public List<Call> Calls { get; } = [];

    /// <summary>The one call of <paramref name="type"/>; fails the test if there is not exactly one.</summary>
    public Call Single(string type) => Assert.Single(Calls, c => c.Type == type);

    /// <summary>
    /// When set, every call is also forwarded here, so a test can put a real
    /// <see cref="NotificationService"/> behind a hook (the failure-isolation tests).
    /// </summary>
    public INotificationService? ForwardTo { get; set; }

    public Task StageAsync(
        string type, User actor,
        Func<INotificationAudience, CancellationToken, Task<NotificationBatch?>> build,
        CancellationToken ct = default)
    {
        Calls.Add(new Call(type, actor, build));
        return ForwardTo?.StageAsync(type, actor, build, ct) ?? Task.CompletedTask;
    }

    public Task<ServiceResult<NotificationCountDto>> GetUnreadCountAsync(User caller, CancellationToken ct = default)
        => throw new NotSupportedException();

    public Task<ServiceResult<NotificationPageDto>> ListAsync(
        User caller, bool unreadOnly, int page, int pageSize, CancellationToken ct = default)
        => throw new NotSupportedException();

    public Task<ServiceResult<bool>> MarkReadAsync(User caller, long id, CancellationToken ct = default)
        => throw new NotSupportedException();

    public Task<ServiceResult<bool>> MarkAllReadAsync(User caller, CancellationToken ct = default)
        => throw new NotSupportedException();
}
