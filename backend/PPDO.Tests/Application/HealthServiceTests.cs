using Microsoft.Extensions.Logging;
using Moq;
using PPDO.Application.Services;

namespace PPDO.Tests.Application;

/// <summary>
/// PPDO-142 — the database half of GET /api/health. The probe is mocked; what is pinned is that a
/// failure is caught AND logged (the exception goes to Application Insights, never to the caller),
/// and that the request's own cancellation is not mistaken for "the database is down".
/// </summary>
public sealed class HealthServiceTests
{
    private static HealthService BuildSut(
        Mock<IDatabaseProbe> probe, Mock<ILogger<HealthService>>? logger = null)
        => new(probe.Object, (logger ?? new Mock<ILogger<HealthService>>()).Object);

    /// <summary>The ILogger.Log call that <c>LogError(ex, …)</c> compiles down to.</summary>
    private static void VerifyErrorLogged(Mock<ILogger<HealthService>> logger, Exception expected, Times times)
        => logger.Verify(l => l.Log(
                LogLevel.Error,
                It.IsAny<EventId>(),
                It.IsAny<It.IsAnyType>(),
                It.Is<Exception?>(e => ReferenceEquals(e, expected)),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            times);

    [Fact]
    public async Task CheckDatabaseAsync_WhenTheProbeSucceeds_ReturnsTrue_AndLogsNoError()
    {
        Mock<IDatabaseProbe> probe = new();
        probe.Setup(p => p.CanConnectAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        Mock<ILogger<HealthService>> logger = new();

        bool ok = await BuildSut(probe, logger).CheckDatabaseAsync();

        Assert.True(ok);
        logger.Verify(l => l.Log(
                LogLevel.Error, It.IsAny<EventId>(), It.IsAny<It.IsAnyType>(),
                It.IsAny<Exception?>(), It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Never);
    }

    [Fact]
    public async Task CheckDatabaseAsync_WhenTheProbeThrows_ReturnsFalse_AndLogsTheExceptionAtError()
    {
        // The SqlClient message is exactly what must never reach the (anonymous) caller: it can carry
        // the server, database and login name. It belongs in the log, with the exception attached.
        InvalidOperationException failure = new("A network-related error. Server=tcp:db-host;Database=ppdo;User ID=svc");
        Mock<IDatabaseProbe> probe = new();
        probe.Setup(p => p.CanConnectAsync(It.IsAny<CancellationToken>())).ThrowsAsync(failure);
        Mock<ILogger<HealthService>> logger = new();

        bool ok = await BuildSut(probe, logger).CheckDatabaseAsync();

        Assert.False(ok);
        VerifyErrorLogged(logger, failure, Times.Once());
    }

    [Fact]
    public async Task CheckDatabaseAsync_TheLogMessageItselfNeverCarriesTheExceptionText()
    {
        // The message template is fixed; the exception travels as the exception, not as interpolated text.
        InvalidOperationException failure = new("Server=tcp:db-host;Password=hunter2");
        Mock<IDatabaseProbe> probe = new();
        probe.Setup(p => p.CanConnectAsync(It.IsAny<CancellationToken>())).ThrowsAsync(failure);
        string? logged = null;
        Mock<ILogger<HealthService>> logger = new();
        logger.Setup(l => l.Log(
                It.IsAny<LogLevel>(), It.IsAny<EventId>(), It.IsAny<It.IsAnyType>(),
                It.IsAny<Exception?>(), It.IsAny<Func<It.IsAnyType, Exception?, string>>()))
            // FormattedLogValues.ToString() is the rendered message — what an operator reads in the log.
            .Callback(new InvocationAction(i => logged = i.Arguments[2]?.ToString()));

        await BuildSut(probe, logger).CheckDatabaseAsync();

        Assert.Equal("Health check: database unreachable", logged);
        Assert.DoesNotContain("Password", logged);
    }

    [Fact]
    public async Task CheckDatabaseAsync_PassesTheRequestsTokenToTheProbe()
    {
        using CancellationTokenSource cts = new();
        Mock<IDatabaseProbe> probe = new();
        probe.Setup(p => p.CanConnectAsync(cts.Token)).Returns(Task.CompletedTask);

        await BuildSut(probe).CheckDatabaseAsync(cts.Token);

        probe.Verify(p => p.CanConnectAsync(cts.Token), Times.Once);
    }

    [Fact]
    public async Task CheckDatabaseAsync_WhenTheCallerCancelled_LetsTheCancellationPropagate()
    {
        // The client hung up: that is not "the database is down", and must not be logged as an error.
        using CancellationTokenSource cts = new();
        cts.Cancel();
        Mock<IDatabaseProbe> probe = new();
        probe.Setup(p => p.CanConnectAsync(It.IsAny<CancellationToken>())).ThrowsAsync(new OperationCanceledException(cts.Token));
        Mock<ILogger<HealthService>> logger = new();

        await Assert.ThrowsAsync<OperationCanceledException>(() => BuildSut(probe, logger).CheckDatabaseAsync(cts.Token));

        logger.Verify(l => l.Log(
                LogLevel.Error, It.IsAny<EventId>(), It.IsAny<It.IsAnyType>(),
                It.IsAny<Exception?>(), It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Never);
    }

    [Fact]
    public async Task CheckDatabaseAsync_ACommandTimeoutThatIsNotTheCallersCancellation_CountsAsDown()
    {
        // SqlClient can surface its own timeout as an OperationCanceledException while the request's token is
        // still live — that IS an unreachable database.
        OperationCanceledException timeout = new("Operation cancelled by user.");
        Mock<IDatabaseProbe> probe = new();
        probe.Setup(p => p.CanConnectAsync(It.IsAny<CancellationToken>())).ThrowsAsync(timeout);
        Mock<ILogger<HealthService>> logger = new();

        bool ok = await BuildSut(probe, logger).CheckDatabaseAsync(CancellationToken.None);

        Assert.False(ok);
        VerifyErrorLogged(logger, timeout, Times.Once());
    }
}
