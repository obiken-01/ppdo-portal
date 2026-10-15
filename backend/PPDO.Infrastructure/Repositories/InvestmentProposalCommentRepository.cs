using Microsoft.EntityFrameworkCore;
using PPDO.Domain.Entities;
using PPDO.Domain.Interfaces;
using PPDO.Infrastructure.Data;

namespace PPDO.Infrastructure.Repositories;

/// <summary>EF Core implementation of <see cref="IInvestmentProposalCommentRepository"/> (PPDO-219).</summary>
public sealed class InvestmentProposalCommentRepository : IInvestmentProposalCommentRepository
{
    private readonly AppDbContext _context;

    public InvestmentProposalCommentRepository(AppDbContext context) => _context = context;

    /// <inheritdoc />
    public async Task<IReadOnlyList<InvestmentProposalComment>> GetByProposalIdAsync(
        int proposalId, CancellationToken ct = default)
        => await _context.InvestmentProposalComments
            .AsNoTracking()
            .Include(c => c.Author)
            .Include(c => c.ResolvedBy)
            .Where(c => c.ProposalId == proposalId)
            .OrderBy(c => c.CreatedAt).ThenBy(c => c.Id)
            .ToListAsync(ct);

    /// <inheritdoc />
    public async Task<InvestmentProposalComment?> GetByIdAsync(int id, CancellationToken ct = default)
        => await _context.InvestmentProposalComments
            .Include(c => c.Author)
            .Include(c => c.ResolvedBy)
            .FirstOrDefaultAsync(c => c.Id == id, ct);

    /// <inheritdoc />
    public Task<int> CountOpenAsync(int proposalId, CancellationToken ct = default)
        => _context.InvestmentProposalComments
            .CountAsync(c => c.ProposalId == proposalId && c.ParentCommentId == null && c.ResolvedAt == null, ct);

    /// <inheritdoc />
    public async Task<IReadOnlyDictionary<int, int>> CountOpenByProposalIdsAsync(
        IReadOnlyList<int> proposalIds, CancellationToken ct = default)
    {
        if (proposalIds.Count == 0) return new Dictionary<int, int>();

        List<KeyValuePair<int, int>> rows = await _context.InvestmentProposalComments
            .Where(c => proposalIds.Contains(c.ProposalId) && c.ParentCommentId == null && c.ResolvedAt == null)
            .GroupBy(c => c.ProposalId)
            .Select(g => new KeyValuePair<int, int>(g.Key, g.Count()))
            .ToListAsync(ct);
        return rows.ToDictionary(r => r.Key, r => r.Value);
    }

    /// <inheritdoc />
    public async Task AddAsync(InvestmentProposalComment comment, CancellationToken ct = default)
        => await _context.InvestmentProposalComments.AddAsync(comment, ct);

    /// <inheritdoc />
    public Task SaveChangesAsync(CancellationToken ct = default) => _context.SaveChangesAsync(ct);
}
