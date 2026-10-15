using Microsoft.Extensions.Logging;
using Moq;
using PPDO.Application.Common;
using PPDO.Application.DTOs.Notifications;
using PPDO.Application.Services;
using PPDO.Domain.Entities;
using PPDO.Domain.Enums;
using PPDO.Domain.Interfaces;

namespace PPDO.Tests.Application;

/// <summary>
/// <see cref="NotificationService"/> (v1.9.0 — PPDO-221): what a staged event writes, the two rules
/// that hold for every event (never the actor; a failure never fails the event), and the reads.
/// Recipient rules per event are <see cref="NotificationAudienceTests"/>; the user-id filter in SQL is
/// <c>NotificationRepositoryTests</c>.
/// </summary>
public sealed class NotificationServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 15, 2, 0, 0, TimeSpan.Zero);

    private readonly Mock<INotificationRepository>      _repo     = new();
    private readonly Mock<INotificationAudience>        _audience = new();
    private readonly Mock<ILogger<NotificationService>> _logger   = new();
    private readonly NotificationService                _sut;
    private readonly List<Notification>                 _staged   = [];

    private readonly User _actor = User("Actor");
    private readonly User _alice = User("Alice");
    private readonly User _bob   = User("Bob");

    public NotificationServiceTests()
    {
        _repo.Setup(r => r.AddRangeAsync(It.IsAny<IReadOnlyList<Notification>>(), It.IsAny<CancellationToken>()))
            .Callback((IReadOnlyList<Notification> rows, CancellationToken _) => _staged.AddRange(rows))
            .Returns(Task.CompletedTask);
        _sut = new NotificationService(_repo.Object, _audience.Object, _logger.Object, new FixedClock(Now));
    }

    private Task StageAsync(params User[] recipients)
        => _sut.StageAsync(NotificationTypes.ProposalComment, _actor,
            (_, _) => Task.FromResult<NotificationBatch?>(new NotificationBatch("Text", "/link", recipients)));

    // ── Staging ───────────────────────────────────────────────────────────────

    [Fact]
    public async Task Stage_WritesOneRowPerRecipient_WithTheFrozenTextLinkTypeAndTime()
    {
        await StageAsync(_alice, _bob);

        Assert.Equal(2, _staged.Count);
        Assert.All(_staged, n =>
        {
            Assert.Equal(NotificationTypes.ProposalComment, n.Type);
            Assert.Equal("Text", n.Text);
            Assert.Equal("/link", n.Link);
            Assert.Equal(Now.UtcDateTime, n.CreatedAt);
            Assert.Null(n.ReadAt);
        });
        Assert.Equal([_alice.Id, _bob.Id], _staged.Select(n => n.UserId).OrderBy(i => i == _alice.Id ? 0 : 1));
    }

    [Fact]
    public async Task Stage_NeverWritesARowForTheActor_EvenWhenTheyAreInScope()
    {
        await StageAsync(_alice, _actor, _bob);

        Assert.DoesNotContain(_staged, n => n.UserId == _actor.Id);
        Assert.Equal(2, _staged.Count);
    }

    [Fact]
    public async Task Stage_OnlyTheActorInScope_WritesNothing()
    {
        await StageAsync(_actor);
        Assert.Empty(_staged);
    }

    [Fact]
    public async Task Stage_ARecipientListedTwice_GetsOneRow()
    {
        await StageAsync(_alice, _alice);
        Assert.Single(_staged);
    }

    [Fact]
    public async Task Stage_ALongText_IsCutToTheColumnWidth()
    {
        await _sut.StageAsync(NotificationTypes.ProposalComment, _actor,
            (_, _) => Task.FromResult<NotificationBatch?>(new NotificationBatch(new string('x', 900), "/l", [_alice])));

        Assert.Equal(NotificationText.MaxLength, Assert.Single(_staged).Text.Length);
    }

    [Fact]
    public async Task Stage_ANullBatch_WritesNothing()
    {
        await _sut.StageAsync(NotificationTypes.AipReviewerEdit, _actor, (_, _) => Task.FromResult<NotificationBatch?>(null));
        _repo.Verify(r => r.AddRangeAsync(It.IsAny<IReadOnlyList<Notification>>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Stage_HandsTheBuildTheAudience()
    {
        _audience.Setup(a => a.CrossOfficeReviewersAsync(It.IsAny<CancellationToken>())).ReturnsAsync([_alice]);

        await _sut.StageAsync(NotificationTypes.AipSentToPpdo, _actor, async (audience, ct) =>
            new NotificationBatch("t", "/l", await audience.CrossOfficeReviewersAsync(ct)));

        Assert.Equal(_alice.Id, Assert.Single(_staged).UserId);
    }

    // ── Failure isolation (decision 6) ────────────────────────────────────────

    [Fact]
    public async Task Stage_WhenResolvingRecipientsThrows_DoesNotThrow_StagesNothing_AndLogsAtError()
    {
        Exception thrown = await Record.ExceptionAsync(() => _sut.StageAsync(NotificationTypes.AipReturnedByPpdo, _actor,
            (_, _) => throw new InvalidOperationException("db down")));

        Assert.Null(thrown);
        Assert.Empty(_staged);
        _logger.Verify(l => l.Log(
            LogLevel.Error, It.IsAny<EventId>(), It.IsAny<It.IsAnyType>(),
            It.Is<Exception?>(e => e is InvalidOperationException),
            It.IsAny<Func<It.IsAnyType, Exception?, string>>()), Times.Once);
    }

    [Fact]
    public async Task Stage_WhenStagingTheRowsThrows_DoesNotThrow()
    {
        _repo.Setup(r => r.AddRangeAsync(It.IsAny<IReadOnlyList<Notification>>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("context gone"));

        Assert.Null(await Record.ExceptionAsync(() => StageAsync(_alice)));
    }

    [Fact]
    public async Task Stage_Cancellation_IsNotSwallowed()
    {
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => _sut.StageAsync(
            NotificationTypes.ProposalComment, _actor, (_, _) => throw new OperationCanceledException()));
    }

    // ── Reads ─────────────────────────────────────────────────────────────────

    [Fact]
    public async Task UnreadCount_IsTheCallersOwn_OverTheLast90Days()
    {
        _repo.Setup(r => r.CountUnreadAsync(_alice.Id, Now.UtcDateTime.AddDays(-90), It.IsAny<CancellationToken>()))
            .ReturnsAsync(3);

        Assert.Equal(3, (await _sut.GetUnreadCountAsync(_alice)).Value!.Count);
    }

    [Theory]
    [InlineData(1, 20, 0, 20)]
    [InlineData(3, 10, 20, 10)]
    [InlineData(0, 0, 0, 20)]       // page clamps to 1, size defaults
    [InlineData(1, 500, 0, 50)]     // size caps at 50
    public async Task List_PagesTheCallersOwnRows(int page, int pageSize, int skip, int take)
    {
        _repo.Setup(r => r.GetPageAsync(_alice.Id, Now.UtcDateTime.AddDays(-90), true, skip, take, It.IsAny<CancellationToken>()))
            .ReturnsAsync(([new Notification { Id = 7, UserId = _alice.Id, Type = "t", Text = "x", Link = "/l", CreatedAt = Now.UtcDateTime }], 41));

        NotificationPageDto dto = (await _sut.ListAsync(_alice, unreadOnly: true, page, pageSize)).Value!;

        Assert.Equal(41, dto.Total);
        Assert.Equal(DateTimeKind.Utc, Assert.Single(dto.Items).CreatedAt.Kind);
    }

    [Fact]
    public async Task MarkRead_AnIdTheCallerDoesNotOwn_IsNotFound()
    {
        // The repository answers false for another user's id (pinned in NotificationRepositoryTests).
        _repo.Setup(r => r.MarkReadAsync(9, _alice.Id, It.IsAny<DateTime>(), It.IsAny<CancellationToken>())).ReturnsAsync(false);

        ServiceResult<bool> result = await _sut.MarkReadAsync(_alice, 9);

        Assert.Equal(ServiceErrorCode.NotFound, result.Code);
        Assert.Equal(NotificationService.NotFoundMessage, result.Error);
    }

    [Fact]
    public async Task MarkRead_AsksForTheCallersOwnRowOnly()
    {
        _repo.Setup(r => r.MarkReadAsync(9, _alice.Id, Now.UtcDateTime, It.IsAny<CancellationToken>())).ReturnsAsync(true);

        Assert.True((await _sut.MarkReadAsync(_alice, 9)).IsSuccess);
        _repo.Verify(r => r.MarkReadAsync(9, It.Is<Guid>(id => id != _alice.Id), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task MarkAllRead_IsTheCallersOwnRows()
    {
        Assert.True((await _sut.MarkAllReadAsync(_alice)).IsSuccess);
        _repo.Verify(r => r.MarkAllReadAsync(_alice.Id, Now.UtcDateTime, It.IsAny<CancellationToken>()), Times.Once);
    }

    private sealed class FixedClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private static User User(string name) => new()
    {
        Id = Guid.NewGuid(), FullName = name, Username = name, PasswordHash = "h", Role = UserRole.Staff,
        IsActive = true, CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow,
    };
}
