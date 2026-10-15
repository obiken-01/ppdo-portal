using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using PPDO.Domain.Entities;
using PPDO.Domain.Enums;
using PPDO.Domain.Interfaces;
using PPDO.Infrastructure.Data;
using PPDO.Infrastructure.Repositories;

namespace PPDO.Tests.Infrastructure;

/// <summary>
/// The generic job operations that are plain LINQ (v1.9.0 — PPDO-228, spec decisions 11 and 12):
/// stuck-job recovery and its attempt cap, expiry, heartbeat, and the export's progress update. On Sqlite,
/// so they also run on CI. The claim and the one-active-per-year index are SQL Server behaviour and are
/// pinned in <see cref="BackgroundJobClaimSqlServerTests"/>.
///
/// Rows go in through raw SQL against hand-written DDL holding the job table's columns, the Sqlite
/// in-memory pattern from <see cref="AipNotificationRepositoryTests"/>.
/// </summary>
public sealed class BackgroundJobRepositoryTests : IDisposable
{
    private static readonly DateTime Now = new(2026, 10, 15, 6, 0, 0, DateTimeKind.Utc);

    private readonly SqliteConnection _connection;
    private readonly DbContextOptions<AppDbContext> _options;

    public BackgroundJobRepositoryTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();
        _options = new DbContextOptionsBuilder<AppDbContext>().UseSqlite(_connection).Options;

        using AppDbContext setup = new(_options);
        setup.Database.ExecuteSqlRaw("""
            CREATE TABLE initial_wfp_export_jobs (
                id INTEGER PRIMARY KEY,
                status TEXT NOT NULL,
                requested_by_id TEXT NOT NULL,
                created_at TEXT NOT NULL,
                claimed_at TEXT NULL,
                heartbeat_at TEXT NULL,
                attempts INTEGER NOT NULL DEFAULT 0,
                message TEXT NULL,
                finished_at TEXT NULL,
                fiscal_year INTEGER NOT NULL,
                done_count INTEGER NOT NULL DEFAULT 0,
                total_count INTEGER NOT NULL DEFAULT 0,
                problems_json TEXT NULL,
                file_key TEXT NULL,
                expires_at TEXT NULL
            );
            """);
    }

    public void Dispose() => _connection.Dispose();

    // ── Recover stuck (decision 11) ─────────────────────────────────────────────

    [Fact]
    public async Task RecoverStuckAsync_RunningWithNoHeartbeatFor10Minutes_GoesBackToQueued()
    {
        await InsertAsync(new InitialWfpExportJob
        {
            Id = 1, Status = BackgroundJobStatus.Running, Attempts = 1,
            ClaimedAt = Now.AddMinutes(-30), HeartbeatAt = Now.AddMinutes(-11),
        });

        IReadOnlyList<RecoveredJob> recovered = await Repo().RecoverStuckAsync<InitialWfpExportJob>(Now);

        Assert.Equal([new RecoveredJob(1, 1, Failed: false)], recovered);
        InitialWfpExportJob row = await ReadAsync(1);
        Assert.Equal(BackgroundJobStatus.Queued, row.Status);
        Assert.Null(row.FinishedAt);
        Assert.Equal(1, row.Attempts);   // the next claim counts the next attempt
    }

    [Fact]
    public async Task RecoverStuckAsync_ThirdAttemptStuck_FailsWithTheMessage()
    {
        await InsertAsync(new InitialWfpExportJob
        {
            Id = 1, Status = BackgroundJobStatus.Running, Attempts = BackgroundJob.MaxAttempts,
            ClaimedAt = Now.AddMinutes(-20), HeartbeatAt = Now.AddMinutes(-15),
        });

        IReadOnlyList<RecoveredJob> recovered = await Repo().RecoverStuckAsync<InitialWfpExportJob>(Now);

        Assert.Equal([new RecoveredJob(1, 3, Failed: true)], recovered);
        InitialWfpExportJob row = await ReadAsync(1);
        Assert.Equal(BackgroundJobStatus.Failed, row.Status);
        Assert.Equal(BackgroundJob.StoppedUnexpectedlyMessage, row.Message);
        Assert.Equal(Now, row.FinishedAt);
    }

    [Fact]
    public async Task RecoverStuckAsync_NeverHeartbeated_UsesTheClaimTime()
    {
        await InsertAsync(new InitialWfpExportJob
        {
            Id = 1, Status = BackgroundJobStatus.Running, Attempts = 1, ClaimedAt = Now.AddMinutes(-11),
        });
        await InsertAsync(new InitialWfpExportJob
        {
            Id = 2, Status = BackgroundJobStatus.Running, Attempts = 1, ClaimedAt = Now.AddMinutes(-9),
        });

        IReadOnlyList<RecoveredJob> recovered = await Repo().RecoverStuckAsync<InitialWfpExportJob>(Now);

        Assert.Equal([1], recovered.Select(r => r.Id));
    }

    [Fact]
    public async Task RecoverStuckAsync_LeavesLiveAndNonRunningJobsAlone()
    {
        // A Running job with a recent heartbeat, even though it was claimed long ago.
        await InsertAsync(new InitialWfpExportJob
        {
            Id = 1, Status = BackgroundJobStatus.Running, Attempts = 1,
            ClaimedAt = Now.AddHours(-1), HeartbeatAt = Now.AddMinutes(-9),
        });
        // Old timestamps, but not Running.
        await InsertAsync(new InitialWfpExportJob { Id = 2, Status = BackgroundJobStatus.Queued, ClaimedAt = Now.AddHours(-1) });
        await InsertAsync(new InitialWfpExportJob { Id = 3, Status = BackgroundJobStatus.Done, HeartbeatAt = Now.AddHours(-1) });

        Assert.Empty(await Repo().RecoverStuckAsync<InitialWfpExportJob>(Now));
        Assert.Equal(BackgroundJobStatus.Running, (await ReadAsync(1)).Status);
        Assert.Equal(BackgroundJobStatus.Queued, (await ReadAsync(2)).Status);
        Assert.Equal(BackgroundJobStatus.Done, (await ReadAsync(3)).Status);
    }

    // ── Expiry (decision 12) ────────────────────────────────────────────────────

    [Fact]
    public async Task GetExpiredAsync_ReturnsFinishedJobsPastTheirExpiry_Only()
    {
        await InsertAsync(new InitialWfpExportJob { Id = 1, Status = BackgroundJobStatus.Done, FileKey = "a.zip", ExpiresAt = Now.AddSeconds(-1) });
        await InsertAsync(new InitialWfpExportJob { Id = 2, Status = BackgroundJobStatus.DoneWithProblems, FileKey = "b.zip", ExpiresAt = Now });
        await InsertAsync(new InitialWfpExportJob { Id = 3, Status = BackgroundJobStatus.Done, FileKey = "c.zip", ExpiresAt = Now.AddDays(1) });
        await InsertAsync(new InitialWfpExportJob { Id = 4, Status = BackgroundJobStatus.Expired, FileKey = "d.zip", ExpiresAt = Now.AddDays(-1) });
        await InsertAsync(new InitialWfpExportJob { Id = 5, Status = BackgroundJobStatus.Failed, ExpiresAt = Now.AddDays(-1) });

        IReadOnlyList<InitialWfpExportJob> expired = await Repo().GetExpiredAsync<InitialWfpExportJob>(Now);

        Assert.Equal([1, 2], expired.Select(j => j.Id).Order());
        Assert.Equal(["a.zip", "b.zip"], expired.Select(j => j.FileKey).Order());
    }

    [Fact]
    public async Task MarkExpiredAsync_MarksAFinishedJob_ButNotARunningOne()
    {
        await InsertAsync(new InitialWfpExportJob { Id = 1, Status = BackgroundJobStatus.Done, FileKey = "a.zip" });
        await InsertAsync(new InitialWfpExportJob { Id = 2, Status = BackgroundJobStatus.Running });

        await Repo().MarkExpiredAsync<InitialWfpExportJob>(1);
        await Repo().MarkExpiredAsync<InitialWfpExportJob>(2);

        Assert.Equal(BackgroundJobStatus.Expired, (await ReadAsync(1)).Status);
        Assert.Equal("a.zip", (await ReadAsync(1)).FileKey);   // the row is history; it keeps its key
        Assert.Equal(BackgroundJobStatus.Running, (await ReadAsync(2)).Status);
    }

    // ── Heartbeat and progress ──────────────────────────────────────────────────

    [Fact]
    public async Task HeartbeatAsync_RefreshesARunningJob_AndSaysWhenItIsNoLongerRunning()
    {
        await InsertAsync(new InitialWfpExportJob { Id = 1, Status = BackgroundJobStatus.Running, HeartbeatAt = Now.AddMinutes(-5) });
        await InsertAsync(new InitialWfpExportJob { Id = 2, Status = BackgroundJobStatus.Queued });

        Assert.True(await Repo().HeartbeatAsync<InitialWfpExportJob>(1, Now));
        Assert.False(await Repo().HeartbeatAsync<InitialWfpExportJob>(2, Now));

        Assert.Equal(Now, (await ReadAsync(1)).HeartbeatAt);
        Assert.Null((await ReadAsync(2)).HeartbeatAt);
    }

    [Fact]
    public async Task UpdateProgressAsync_SetsTheCountsAndTheHeartbeat_OnARunningJobOnly()
    {
        await InsertAsync(new InitialWfpExportJob { Id = 1, Status = BackgroundJobStatus.Running, HeartbeatAt = Now.AddMinutes(-5) });
        await InsertAsync(new InitialWfpExportJob { Id = 2, Status = BackgroundJobStatus.Queued });

        await using AppDbContext context = new(_options);
        InitialWfpExportJobRepository repo = new(context);
        Assert.True(await repo.UpdateProgressAsync(1, doneCount: 3, totalCount: 12, Now));
        Assert.False(await repo.UpdateProgressAsync(2, doneCount: 3, totalCount: 12, Now));

        InitialWfpExportJob row = await ReadAsync(1);
        Assert.Equal((3, 12, Now), (row.DoneCount, row.TotalCount, row.HeartbeatAt));
        Assert.Equal(0, (await ReadAsync(2)).DoneCount);
    }

    // ── Helpers ─────────────────────────────────────────────────────────────────

    private BackgroundJobRepository Repo() => new(new AppDbContext(_options));

    /// <summary>
    /// Inserts with ExecuteSql rather than SaveChanges: AppDbContext stamps CreatedAt on save, and the
    /// id comes from a SQL Server sequence the Sqlite table does not have.
    /// </summary>
    private async Task InsertAsync(InitialWfpExportJob job)
    {
        await using AppDbContext context = new(_options);
        await context.Database.ExecuteSqlAsync($"""
            INSERT INTO initial_wfp_export_jobs
                (id, status, requested_by_id, created_at, claimed_at, heartbeat_at, attempts, message,
                 finished_at, fiscal_year, file_key, expires_at)
            VALUES
                ({job.Id}, {job.Status.ToString()}, {Guid.Empty}, {Now.AddHours(-2)}, {job.ClaimedAt}, {job.HeartbeatAt},
                 {job.Attempts}, {job.Message}, {job.FinishedAt}, {2028}, {job.FileKey}, {job.ExpiresAt})
            """);
    }

    private async Task<InitialWfpExportJob> ReadAsync(int id)
    {
        await using AppDbContext context = new(_options);
        return await context.InitialWfpExportJobs.AsNoTracking().SingleAsync(j => j.Id == id);
    }
}
