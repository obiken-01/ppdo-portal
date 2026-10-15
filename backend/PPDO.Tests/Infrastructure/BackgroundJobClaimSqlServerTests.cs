using Microsoft.EntityFrameworkCore;
using PPDO.Domain.Common;
using PPDO.Domain.Entities;
using PPDO.Domain.Enums;
using PPDO.Infrastructure.Data;
using PPDO.Infrastructure.Repositories;
using PPDO.Tests.TestHelpers;

namespace PPDO.Tests.Infrastructure;

/// <summary>
/// The job claim and the one-active-job-per-year index (v1.9.0 — PPDO-228, spec decisions 5 and 10),
/// against a <b>real SQL Server</b>: both are SQL Server behaviour (locking hints, a filtered unique index)
/// that Sqlite cannot show. Runs on a throwaway database built from the EF model
/// (<see cref="SqlServerTestDatabase"/>); skipped where there is no SQL Server, as on CI.
///
/// <para>
/// ⚠️ <b>Red-tested (PPDO-228).</b> Swapping the claim for the two-statement version (SELECT the oldest,
/// then UPDATE it) fails both concurrency tests: one job handed to several claimers. Removing
/// <c>READPAST</c> fails <see cref="ClaimNextAsync_RowLockedByAnotherClaim_SkipsItInsteadOfWaiting"/>:
/// the second claimer waits on the first one's lock until its command times out. Removing <c>UPDLOCK</c>
/// alone fails nothing, because an <c>UPDATE</c> already update-locks the rows it reads.
/// </para>
/// </summary>
public sealed class BackgroundJobClaimSqlServerTests : IClassFixture<SqlServerTestDatabase>
{
    private static readonly DateTime T0 = new(2026, 10, 15, 6, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime Now = T0.AddMinutes(5);

    private readonly SqlServerTestDatabase _db;

    public BackgroundJobClaimSqlServerTests(SqlServerTestDatabase db) => _db = db;

    // ── The claim ────────────────────────────────────────────────────────────────

    [SqlServerFact]
    public async Task ClaimNextAsync_ConcurrentClaimsOnOneQueuedJob_ExactlyOneWins()
    {
        await ResetAsync();
        int jobId = await QueueAsync(2028, T0);

        InitialWfpExportJob?[] claims = await ClaimConcurrentlyAsync(claimers: 8);

        InitialWfpExportJob winner = Assert.Single(claims, c => c is not null)!;
        Assert.Equal(jobId, winner.Id);

        InitialWfpExportJob row = await ReadAsync(jobId);
        Assert.Equal(BackgroundJobStatus.Running, row.Status);
        Assert.Equal(1, row.Attempts);      // claimed once, not once per claimer
        Assert.Equal(Now, row.ClaimedAt);
        Assert.Equal(Now, row.HeartbeatAt);
    }

    [SqlServerFact]
    public async Task ClaimNextAsync_ManyClaimersOnManyJobs_NeverClaimAJobTwice()
    {
        await ResetAsync();
        List<int> jobIds = [];
        for (int i = 0; i < 10; i++)
            jobIds.Add(await QueueAsync(2028 + i, T0.AddSeconds(i)));

        // Rounds of 20 claimers at once until the queue is empty. A claimer that meets only locked rows
        // gets null and the next pass picks the job up, so the rule is not "every job in round one":
        // it is that no job is ever handed out twice and none is lost.
        List<int> claimed = [];
        for (int round = 0; round < 10 && claimed.Count < jobIds.Count; round++)
            claimed.AddRange((await ClaimConcurrentlyAsync(claimers: 20)).Where(c => c is not null).Select(c => c!.Id));

        Assert.Equal(jobIds.Order(), claimed.Order());   // each job exactly once

        await using AppDbContext context = _db.NewContext();
        Assert.All(await context.InitialWfpExportJobs.ToListAsync(),
            j => Assert.Equal((BackgroundJobStatus.Running, 1), (j.Status, j.Attempts)));
    }

    [SqlServerFact]
    public async Task ClaimNextAsync_RowLockedByAnotherClaim_SkipsItInsteadOfWaiting()
    {
        await ResetAsync();
        int older = await QueueAsync(2028, T0);
        int newer = await QueueAsync(2029, T0.AddMinutes(1));

        // Worker A claims the oldest job inside a transaction it keeps open, so its row lock is held.
        await using AppDbContext a = _db.NewContext();
        await using var transaction = await a.Database.BeginTransactionAsync();
        InitialWfpExportJob? first = await new BackgroundJobRepository(a).ClaimNextAsync<InitialWfpExportJob>(Now);
        Assert.Equal(older, first?.Id);

        // Worker B must step past A's locked row at once, not wait for it (READPAST). Five seconds is
        // ample for an immediate answer; waiting on the lock would hit it.
        await using AppDbContext b = _db.NewContext();
        b.Database.SetCommandTimeout(TimeSpan.FromSeconds(5));
        BackgroundJobRepository repoB = new(b);

        Assert.Equal(newer, (await repoB.ClaimNextAsync<InitialWfpExportJob>(Now))?.Id);
        Assert.Null(await repoB.ClaimNextAsync<InitialWfpExportJob>(Now));   // only A's locked row is left

        await transaction.RollbackAsync();
    }

    [SqlServerFact]
    public async Task ClaimNextAsync_TakesTheOldestQueuedJob_AndLeavesOtherStatusesAlone()
    {
        await ResetAsync();
        await QueueAsync(2028, T0.AddMinutes(-30), BackgroundJobStatus.Done);   // older, but not Queued
        int newer = await QueueAsync(2030, T0.AddMinutes(2));
        int oldest = await QueueAsync(2029, T0);

        await using AppDbContext context = _db.NewContext();
        BackgroundJobRepository repo = new(context);

        Assert.Equal(oldest, (await repo.ClaimNextAsync<InitialWfpExportJob>(Now))?.Id);
        Assert.Equal(newer, (await repo.ClaimNextAsync<InitialWfpExportJob>(Now))?.Id);
        Assert.Null(await repo.ClaimNextAsync<InitialWfpExportJob>(Now));
    }

    // ── One active job per fiscal year (decision 5) ─────────────────────────────

    [SqlServerFact]
    public async Task AddAsync_SecondActiveJobForTheYear_IsRefusedByTheIndex()
    {
        await ResetAsync();
        await QueueAsync(2028, T0);

        await using AppDbContext context = _db.NewContext();
        InitialWfpExportJobRepository repo = new(context);
        Guid requester = await RequesterAsync();

        await Assert.ThrowsAsync<UniqueConstraintViolationException>(() => repo.AddAsync(
            new InitialWfpExportJob { FiscalYear = 2028, RequestedById = requester, CreatedAt = Now }));

        // A finished job for the year does not count: a new one can be queued beside it.
        await QueueAsync(2029, T0, BackgroundJobStatus.Done);
        await repo.AddAsync(new InitialWfpExportJob { FiscalYear = 2029, RequestedById = requester, CreatedAt = Now });
        Assert.Equal(2, await context.InitialWfpExportJobs.CountAsync(j => j.FiscalYear == 2029));
    }

    [SqlServerFact]
    public async Task GetLatestAndActive_ReadTheYearsJobsWithTheirRequester()
    {
        await ResetAsync();
        await QueueAsync(2028, T0, BackgroundJobStatus.Done);
        int running = await QueueAsync(2028, T0.AddMinutes(1), BackgroundJobStatus.Running);
        await QueueAsync(2029, T0.AddMinutes(2));   // another year

        await using AppDbContext context = _db.NewContext();
        InitialWfpExportJobRepository repo = new(context);

        InitialWfpExportJob? latest = await repo.GetLatestByFiscalYearAsync(2028);
        Assert.Equal(running, latest?.Id);
        Assert.False(string.IsNullOrEmpty(latest!.RequestedBy?.FullName));

        Assert.Equal(running, (await repo.GetActiveByFiscalYearAsync(2028))?.Id);
        Assert.Null(await repo.GetLatestByFiscalYearAsync(2031));
    }

    [SqlServerFact]
    public async Task Ids_ComeFromTheSharedSequence()
    {
        await ResetAsync();
        int first = await QueueAsync(2028, T0);
        int second = await QueueAsync(2029, T0);

        await using AppDbContext context = _db.NewContext();
        long next = await context.Database
            .SqlQueryRaw<long>("SELECT CAST(current_value AS bigint) AS [Value] FROM sys.sequences WHERE name = 'background_job_ids'")
            .SingleAsync();
        Assert.Equal(second, (int)next);
        Assert.True(second > first);
    }

    // ── Helpers ─────────────────────────────────────────────────────────────────

    /// <summary>
    /// Runs <paramref name="claimers"/> claims at once, each on its own context and connection (a context
    /// is not thread-safe, and real workers never share one). Connections are opened before the start
    /// gate, so the claims themselves are what race.
    /// </summary>
    private async Task<InitialWfpExportJob?[]> ClaimConcurrentlyAsync(int claimers)
    {
        TaskCompletionSource start = new(TaskCreationOptions.RunContinuationsAsynchronously);
        List<AppDbContext> contexts = [];
        try
        {
            List<Task<InitialWfpExportJob?>> claims = [];
            for (int i = 0; i < claimers; i++)
            {
                AppDbContext context = _db.NewContext();
                contexts.Add(context);
                await context.Database.OpenConnectionAsync();
                claims.Add(Task.Run(async () =>
                {
                    await start.Task;
                    return await new BackgroundJobRepository(context).ClaimNextAsync<InitialWfpExportJob>(Now);
                }));
            }
            start.SetResult();
            return await Task.WhenAll(claims);
        }
        finally
        {
            foreach (AppDbContext context in contexts) await context.DisposeAsync();
        }
    }

    private async Task ResetAsync()
    {
        await using AppDbContext context = _db.NewContext();
        await context.InitialWfpExportJobs.ExecuteDeleteAsync();
    }

    private async Task<Guid> RequesterAsync()
    {
        // The model seeds the SuperAdmin; any user will do as the requester.
        await using AppDbContext context = _db.NewContext();
        return await context.Users.Select(u => u.Id).FirstAsync();
    }

    private async Task<int> QueueAsync(
        int fiscalYear, DateTime createdAt, BackgroundJobStatus status = BackgroundJobStatus.Queued)
    {
        Guid requester = await RequesterAsync();
        await using AppDbContext context = _db.NewContext();
        InitialWfpExportJob job = new()
        {
            FiscalYear = fiscalYear, Status = status, RequestedById = requester, CreatedAt = createdAt,
        };
        context.InitialWfpExportJobs.Add(job);
        await context.SaveChangesAsync();

        // AppDbContext stamps CreatedAt with the clock on every insert; put back the time this test wants.
        await context.InitialWfpExportJobs.Where(j => j.Id == job.Id)
            .ExecuteUpdateAsync(u => u.SetProperty(j => j.CreatedAt, createdAt));
        return job.Id;
    }

    private async Task<InitialWfpExportJob> ReadAsync(int id)
    {
        await using AppDbContext context = _db.NewContext();
        return await context.InitialWfpExportJobs.AsNoTracking().SingleAsync(j => j.Id == id);
    }
}
