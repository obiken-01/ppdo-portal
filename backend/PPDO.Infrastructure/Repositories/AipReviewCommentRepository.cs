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
    public async Task<IReadOnlyDictionary<int, int>> CountUnacknowledgedReviewerEditsAsync(
        IReadOnlyList<int> aipOfficeIds, CancellationToken ct = default)
    {
        if (aipOfficeIds.Count == 0) return new Dictionary<int, int>();

        List<KeyValuePair<int, int>> rows = await _context.Set<AipReviewComment>()
            .Where(c => aipOfficeIds.Contains(c.AipOfficeId) && c.IsReviewerEdit && c.AcknowledgedAt == null)
            .GroupBy(c => c.AipOfficeId)
            .Select(g => new KeyValuePair<int, int>(g.Key, g.Count()))
            .ToListAsync(ct);

        return rows.ToDictionary(r => r.Key, r => r.Value);
    }

    /// <inheritdoc />
    public async Task<int> AcknowledgeReviewerEditsAsync(
        IReadOnlyList<int> aipOfficeIds, Guid userId, DateTime at, CancellationToken ct = default)
    {
        if (aipOfficeIds.Count == 0) return 0;

        // One set-based UPDATE, not load-and-save: the WHERE is the whole rule (reviewer edits only,
        // pending only, these groups only), and it stays in SQL where a test against a real
        // database can see it.
        return await _context.Set<AipReviewComment>()
            .Where(c => aipOfficeIds.Contains(c.AipOfficeId) && c.IsReviewerEdit && c.AcknowledgedAt == null)
            .ExecuteUpdateAsync(u => u
                .SetProperty(c => c.AcknowledgedAt, at)
                .SetProperty(c => c.AcknowledgedById, userId), ct);
    }

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
            .Include(c => c.AcknowledgedBy)
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

    /// <inheritdoc />
    public async Task<IReadOnlyList<AipReviewComment>> GetByNodesAsync(
        IReadOnlyList<int> programIds, IReadOnlyList<int> projectIds, IReadOnlyList<int> activityIds,
        CancellationToken ct = default)
    {
        if (programIds.Count == 0 && projectIds.Count == 0 && activityIds.Count == 0) return [];

        return await _context.Set<AipReviewComment>()
            .AsNoTracking()
            .Where(c =>
                (c.NodeType == AipCommentNodeType.Program  && programIds.Contains(c.NodeId)) ||
                (c.NodeType == AipCommentNodeType.Project  && projectIds.Contains(c.NodeId)) ||
                (c.NodeType == AipCommentNodeType.Activity && activityIds.Contains(c.NodeId)))
            .OrderBy(c => c.Id)
            .ToListAsync(ct);
    }

    /// <inheritdoc />
    public async Task<int> DeleteByIdsAsync(IReadOnlyList<int> ids, CancellationToken ct = default)
    {
        if (ids.Count == 0) return 0;
        return await _context.Set<AipReviewComment>()
            .Where(c => ids.Contains(c.Id))
            .ExecuteDeleteAsync(ct);
    }
}
