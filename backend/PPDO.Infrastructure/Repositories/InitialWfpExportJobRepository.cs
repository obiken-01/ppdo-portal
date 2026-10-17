using Microsoft.EntityFrameworkCore;
using PPDO.Domain.Common;
using PPDO.Domain.Entities;
using PPDO.Domain.Enums;
using PPDO.Domain.Interfaces;
using PPDO.Infrastructure.Data;

namespace PPDO.Infrastructure.Repositories;

/// <summary>EF Core implementation of <see cref="IInitialWfpExportJobRepository"/> (v1.9.0 — PPDO-228).</summary>
public sealed class InitialWfpExportJobRepository : IInitialWfpExportJobRepository
{
    private readonly AppDbContext _context;

    public InitialWfpExportJobRepository(AppDbContext context) => _context = context;

    /// <inheritdoc />
    public async Task AddAsync(InitialWfpExportJob job, CancellationToken ct = default)
    {
        _context.InitialWfpExportJobs.Add(job);
        try
        {
            await _context.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (SqlErrors.IsUniqueViolation(ex))
        {
            // Another request created the year's active job first (IX_..._fiscal_year_active). Forget
            // ours, so the context is clean for the caller's next read, and let the service answer
            // with the job that won.
            _context.Entry(job).State = EntityState.Detached;
            throw new UniqueConstraintViolationException(
                "The fiscal year already has an active export.", SqlErrors.IndexNameOf(ex), ex);
        }
    }

    /// <inheritdoc />
    public Task<InitialWfpExportJob?> GetByIdAsync(int id, CancellationToken ct = default)
        => WithRequester().FirstOrDefaultAsync(j => j.Id == id, ct);

    /// <inheritdoc />
    public Task<InitialWfpExportJob?> GetLatestByFiscalYearAsync(int fiscalYear, CancellationToken ct = default)
        => WithRequester()
            .Where(j => j.FiscalYear == fiscalYear)
            .OrderByDescending(j => j.CreatedAt).ThenByDescending(j => j.Id)
            .FirstOrDefaultAsync(ct);

    /// <inheritdoc />
    public Task<InitialWfpExportJob?> GetActiveByFiscalYearAsync(int fiscalYear, CancellationToken ct = default)
        => WithRequester()
            .FirstOrDefaultAsync(j => j.FiscalYear == fiscalYear
                && (j.Status == BackgroundJobStatus.Queued || j.Status == BackgroundJobStatus.Running), ct);

    /// <inheritdoc />
    public async Task<bool> UpdateProgressAsync(
        int id, int doneCount, int totalCount, DateTime now, CancellationToken ct = default)
        => await _context.InitialWfpExportJobs
            .Where(j => j.Id == id && j.Status == BackgroundJobStatus.Running)
            .ExecuteUpdateAsync(u => u
                .SetProperty(j => j.DoneCount, doneCount)
                .SetProperty(j => j.TotalCount, totalCount)
                .SetProperty(j => j.HeartbeatAt, now), ct) > 0;

    /// <inheritdoc />
    public async Task<bool> CompleteAsync(
        int id, InitialWfpExportCompletion completion, DateTime now, CancellationToken ct = default)
        => await _context.InitialWfpExportJobs
            .Where(j => j.Id == id && j.Status == BackgroundJobStatus.Running)
            .ExecuteUpdateAsync(u => u
                .SetProperty(j => j.Status, completion.Status)
                .SetProperty(j => j.DoneCount, completion.DoneCount)
                .SetProperty(j => j.TotalCount, completion.TotalCount)
                .SetProperty(j => j.ProblemsJson, completion.ProblemsJson)
                .SetProperty(j => j.FileKey, completion.FileKey)
                .SetProperty(j => j.ExpiresAt, completion.ExpiresAt)
                .SetProperty(j => j.FinishedAt, now)
                .SetProperty(j => j.HeartbeatAt, now), ct) > 0;

    /// <summary>Untracked, with the requester's row for the job's "requested by" name.</summary>
    private IQueryable<InitialWfpExportJob> WithRequester()
        => _context.InitialWfpExportJobs.AsNoTracking().Include(j => j.RequestedBy);
}
