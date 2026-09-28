using Microsoft.EntityFrameworkCore;
using PPDO.Domain.Entities;
using PPDO.Domain.Interfaces;
using PPDO.Infrastructure.Data;

namespace PPDO.Infrastructure.Repositories;

/// <summary>EF Core implementation of <see cref="IAipDivisionSubmissionRepository"/> (PPDO-130).</summary>
public sealed class AipDivisionSubmissionRepository : IAipDivisionSubmissionRepository
{
    private readonly AppDbContext _context;

    public AipDivisionSubmissionRepository(AppDbContext context) => _context = context;

    /// <inheritdoc />
    public async Task<IReadOnlyList<AipDivisionSubmission>> GetForOfficeAsync(
        int aipRecordId, int officeId, CancellationToken ct = default)
        => await _context.AipDivisionSubmissions
            .Where(s => s.AipRecordId == aipRecordId && s.OfficeId == officeId)
            .OrderBy(s => s.DivisionId)
            .ToListAsync(ct);

    /// <inheritdoc />
    public async Task<IReadOnlyList<AipDivisionSubmission>> GetForOfficesAsync(
        int aipRecordId, IReadOnlyList<int> officeIds, CancellationToken ct = default)
        => officeIds.Count == 0
            ? []
            : await _context.AipDivisionSubmissions
                .AsNoTracking()
                .Where(s => s.AipRecordId == aipRecordId && officeIds.Contains(s.OfficeId))
                .ToListAsync(ct);

    /// <inheritdoc />
    public async Task<IReadOnlyList<Division>> GetDivisionsByOfficeIdsAsync(
        IReadOnlyList<int> officeIds, CancellationToken ct = default)
        => officeIds.Count == 0
            ? []
            : await _context.Divisions
                .AsNoTracking()
                .Where(d => officeIds.Contains(d.OfficeId))
                .OrderBy(d => d.Id)
                .ToListAsync(ct);

    /// <inheritdoc />
    public async Task AddAsync(AipDivisionSubmission submission, CancellationToken ct = default)
        => await _context.AipDivisionSubmissions.AddAsync(submission, ct);

    /// <inheritdoc />
    public async Task SaveChangesAsync(CancellationToken ct = default)
        => await _context.SaveChangesAsync(ct);
}
