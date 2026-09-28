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
    public async Task AddAsync(AipDivisionSubmission submission, CancellationToken ct = default)
        => await _context.AipDivisionSubmissions.AddAsync(submission, ct);

    /// <inheritdoc />
    public async Task SaveChangesAsync(CancellationToken ct = default)
        => await _context.SaveChangesAsync(ct);
}
