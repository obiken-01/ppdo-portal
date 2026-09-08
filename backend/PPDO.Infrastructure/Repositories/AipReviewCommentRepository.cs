using Microsoft.EntityFrameworkCore;
using PPDO.Domain.Entities;
using PPDO.Domain.Enums;
using PPDO.Domain.Interfaces;
using PPDO.Infrastructure.Data;

namespace PPDO.Infrastructure.Repositories;

/// <summary>
/// EF Core implementation of <see cref="IAipReviewCommentRepository"/> (V18-53 / PPDO-71).
/// Every method pushes its WHERE / GROUP BY to SQL; the table is never materialised to filter or
/// count in memory.
/// </summary>
public sealed class AipReviewCommentRepository
    : Repository<AipReviewComment>, IAipReviewCommentRepository
{
    public AipReviewCommentRepository(AppDbContext context) : base(context) { }

    /// <inheritdoc />
    public async Task<AipReviewComment?> GetByIntIdAsync(int id, CancellationToken ct = default)
        => await _context.Set<AipReviewComment>().FirstOrDefaultAsync(c => c.Id == id, ct);

    /// <inheritdoc />
    public async Task<IReadOnlyList<AipReviewComment>> GetByOfficeIdsAsync(
        IReadOnlyList<int> aipOfficeIds, CancellationToken ct = default)
    {
        if (aipOfficeIds.Count == 0) return [];

        return await _context.Set<AipReviewComment>()
            .AsNoTracking()
            // One level of Include, well under the two-level cap: the author's name renders beside
            // every comment, and fetching it per comment is the N+1 this avoids.
            .Include(c => c.Author)
            .Include(c => c.ResolvedBy)
            .Where(c => aipOfficeIds.Contains(c.AipOfficeId))
            .OrderBy(c => c.CreatedAt).ThenBy(c => c.Id)
            .ToListAsync(ct);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyDictionary<AipCommentSide, int>> CountUnresolvedBySideAsync(
        IReadOnlyList<int> aipOfficeIds, CancellationToken ct = default)
    {
        if (aipOfficeIds.Count == 0) return new Dictionary<AipCommentSide, int>();

        List<KeyValuePair<AipCommentSide, int>> rows = await _context.Set<AipReviewComment>()
            .Where(c => aipOfficeIds.Contains(c.AipOfficeId) && c.ResolvedAt == null)
            .GroupBy(c => c.AuthorSide)
            .Select(g => new KeyValuePair<AipCommentSide, int>(g.Key, g.Count()))
            .ToListAsync(ct);

        // A side with nothing outstanding is absent rather than zero — the caller treats absent as
        // zero, which is also what makes the two-row shape optional rather than assumed.
        return rows.ToDictionary(r => r.Key, r => r.Value);
    }
}
