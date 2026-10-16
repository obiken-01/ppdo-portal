using Microsoft.EntityFrameworkCore;
using PPDO.Domain.Entities;
using PPDO.Domain.Enums;
using PPDO.Domain.Interfaces;
using PPDO.Infrastructure.Data;

namespace PPDO.Infrastructure.Repositories;

/// <summary>
/// EF Core implementation of <see cref="IBackgroundJobRepository"/> (v1.9.0 — PPDO-228). One instance
/// serves every job kind: each method finds the kind's table from the EF model, so a new kind needs no
/// code here.
/// </summary>
public sealed class BackgroundJobRepository : IBackgroundJobRepository
{
    private readonly AppDbContext _context;

    public BackgroundJobRepository(AppDbContext context) => _context = context;

    /// <inheritdoc />
    /// <remarks>
    /// <para>
    /// <b>Why this is one SQL statement.</b> The obvious version, "SELECT the oldest Queued job, then
    /// UPDATE it to Running", has a gap between the two statements in which a second worker can SELECT
    /// the same job, and both run it. Here the choice and the change are one <c>UPDATE</c>: SQL Server
    /// locks the row it picks before changing it, and a second claim that reaches the row after the first
    /// has changed it no longer finds it Queued. That is what makes the claim single-winner.
    /// </para>
    /// <para>The hints decide how a claimer behaves when another one holds a row:</para>
    /// <list type="bullet">
    ///   <item><description><c>READPAST</c>: skip a row another claimer has locked rather than wait for it, so a claimer takes the next Queued job, or none, at once.</description></item>
    ///   <item><description><c>UPDLOCK</c>: take the update lock as the row is read. SQL Server already does this for the rows an <c>UPDATE</c> reads; the hint states it, as the standard queue-claim pattern does, rather than leaving it to the plan.</description></item>
    ///   <item><description><c>ROWLOCK</c>: lock just that row, not its page or the table.</description></item>
    /// </list>
    /// <para>
    /// The <c>WITH next AS (SELECT TOP (1) … ORDER BY …)</c> form is <c>UPDATE TOP (1)</c> with an order:
    /// <c>UPDATE TOP</c> itself cannot take an <c>ORDER BY</c>, and we want the oldest job first. The
    /// <c>(status, created_at)</c> index lets SQL Server read Queued rows already in that order and stop at
    /// the first it can take, so it locks only that row (see <c>InitialWfpExportJobConfiguration</c>).
    /// </para>
    /// <para>
    /// ⚠️ <b>SQL Server only, on purpose, and only here.</b> On PostgreSQL the same claim is
    /// <c>… WHERE id = (SELECT id … ORDER BY created_at, id LIMIT 1 FOR UPDATE SKIP LOCKED) RETURNING id</c>
    /// (spec decision 10). Everything else in this class is plain LINQ.
    /// </para>
    /// <para>
    /// Pinned by <c>BackgroundJobClaimSqlServerTests</c>, against a real SQL Server.
    /// </para>
    /// </remarks>
    public async Task<TJob?> ClaimNextAsync<TJob>(DateTime now, CancellationToken ct = default)
        where TJob : BackgroundJob
    {
        string table = TableOf<TJob>();

        // $$ raw string: {{…}} interpolates, a single {0} is EF's placeholder for the real SQL parameter
        // `now`. The table name comes from our own EF model and the statuses are enum names, so nothing
        // interpolated here ever comes from a request.
        string sql = $$"""
            WITH next AS (
                SELECT TOP (1) *
                FROM [{{table}}] WITH (UPDLOCK, READPAST, ROWLOCK)
                WHERE [status] = N'{{BackgroundJobStatus.Queued}}'
                ORDER BY [created_at], [id]
            )
            UPDATE next
            SET [status]       = N'{{BackgroundJobStatus.Running}}',
                [claimed_at]   = {0},
                [heartbeat_at] = {0},
                [attempts]     = [attempts] + 1
            OUTPUT inserted.[id] AS [Value];
            """;

        // ToListAsync, not FirstOrDefaultAsync: a LINQ operator would make EF wrap the UPDATE in a
        // SELECT, which SQL Server rejects. The statement returns zero or one id.
        List<int> claimed = await _context.Database.SqlQueryRaw<int>(sql, now).ToListAsync(ct);
        if (claimed.Count == 0) return null;

        int id = claimed[0];
        return await _context.Set<TJob>().AsNoTracking().FirstAsync(j => j.Id == id, ct);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<RecoveredJob>> RecoverStuckAsync<TJob>(DateTime now, CancellationToken ct = default)
        where TJob : BackgroundJob
    {
        DateTime cutoff = now - BackgroundJob.StuckAfter;

        // A job claimed but never heartbeated still has claimed_at, which the claim sets with it.
        List<RecoveredJob> candidates = await _context.Set<TJob>()
            .AsNoTracking()
            .Where(j => j.Status == BackgroundJobStatus.Running && (j.HeartbeatAt ?? j.ClaimedAt) < cutoff)
            .Select(j => new RecoveredJob(j.Id, j.Attempts, j.Attempts >= BackgroundJob.MaxAttempts))
            .ToListAsync(ct);

        List<RecoveredJob> recovered = [];
        foreach (RecoveredJob job in candidates)
        {
            // The WHERE repeats the stuck test, so a job whose worker heartbeated after the read above
            // is left alone. Only rows this UPDATE actually changed are reported.
            IQueryable<TJob> stillStuck = _context.Set<TJob>()
                .Where(j => j.Id == job.Id
                         && j.Status == BackgroundJobStatus.Running
                         && (j.HeartbeatAt ?? j.ClaimedAt) < cutoff);

            int changed = job.Failed
                ? await stillStuck.ExecuteUpdateAsync(u => u
                    .SetProperty(j => j.Status, BackgroundJobStatus.Failed)
                    .SetProperty(j => j.Message, BackgroundJob.StoppedUnexpectedlyMessage)
                    .SetProperty(j => j.FinishedAt, now), ct)
                : await stillStuck.ExecuteUpdateAsync(u => u
                    .SetProperty(j => j.Status, BackgroundJobStatus.Queued), ct);

            if (changed > 0) recovered.Add(job);
        }
        return recovered;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<TJob>> GetExpiredAsync<TJob>(DateTime now, CancellationToken ct = default)
        where TJob : BackgroundJob, IExpiringJob
        => await _context.Set<TJob>()
            .AsNoTracking()
            .Where(j => (j.Status == BackgroundJobStatus.Done || j.Status == BackgroundJobStatus.DoneWithProblems)
                     && j.ExpiresAt != null && j.ExpiresAt <= now)
            .ToListAsync(ct);

    /// <inheritdoc />
    public Task MarkExpiredAsync<TJob>(int id, CancellationToken ct = default)
        where TJob : BackgroundJob
        => _context.Set<TJob>()
            .Where(j => j.Id == id
                     && (j.Status == BackgroundJobStatus.Done || j.Status == BackgroundJobStatus.DoneWithProblems))
            .ExecuteUpdateAsync(u => u.SetProperty(j => j.Status, BackgroundJobStatus.Expired), ct);

    /// <inheritdoc />
    public async Task<bool> HeartbeatAsync<TJob>(int id, DateTime now, CancellationToken ct = default)
        where TJob : BackgroundJob
        => await _context.Set<TJob>()
            .Where(j => j.Id == id && j.Status == BackgroundJobStatus.Running)
            .ExecuteUpdateAsync(u => u.SetProperty(j => j.HeartbeatAt, now), ct) > 0;

    /// <inheritdoc />
    public async Task<bool> FailAsync<TJob>(int id, string message, DateTime now, CancellationToken ct = default)
        where TJob : BackgroundJob
    {
        string fitted = message.Length <= BackgroundJob.MessageMaxLength
            ? message
            : message[..BackgroundJob.MessageMaxLength];

        return await _context.Set<TJob>()
            .Where(j => j.Id == id && j.Status == BackgroundJobStatus.Running)
            .ExecuteUpdateAsync(u => u
                .SetProperty(j => j.Status, BackgroundJobStatus.Failed)
                .SetProperty(j => j.Message, fitted)
                .SetProperty(j => j.FinishedAt, now), ct) > 0;
    }

    /// <summary>The job kind's table, from the EF model (e.g. <c>initial_wfp_export_jobs</c>).</summary>
    private string TableOf<TJob>() where TJob : BackgroundJob
        => _context.Model.FindEntityType(typeof(TJob))?.GetTableName()
           ?? throw new InvalidOperationException($"{typeof(TJob).Name} is not a mapped job table.");
}
