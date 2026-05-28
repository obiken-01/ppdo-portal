using Microsoft.EntityFrameworkCore;
using PPDO.Domain.Entities;
using PPDO.Domain.Enums;
using PPDO.Domain.Interfaces;
using PPDO.Infrastructure.Data;

namespace PPDO.Infrastructure.Repositories;

/// <summary>
/// EF Core implementation of <see cref="IPurchaseRequestRepository"/>.
/// Provides PR-specific queries on top of the generic <see cref="Repository{T}"/> base.
///
/// Include depth never exceeds 2 levels per the project rules in CLAUDE.md.
/// All queries are async.
/// </summary>
public sealed class PurchaseRequestRepository
    : Repository<PurchaseRequest>, IPurchaseRequestRepository
{
    public PurchaseRequestRepository(AppDbContext context) : base(context) { }

    /// <inheritdoc />
    public async Task<PurchaseRequest?> GetByPRNoAsync(
        string prNo,
        CancellationToken cancellationToken = default)
        => await _context.PurchaseRequests
            .FirstOrDefaultAsync(pr => pr.PRNo == prNo, cancellationToken);

    /// <inheritdoc />
    public async Task<PurchaseRequest?> GetWithItemsAsync(
        Guid id,
        CancellationToken cancellationToken = default)
        => await _context.PurchaseRequests
            .Include(pr => pr.Items)          // depth 1
            .FirstOrDefaultAsync(pr => pr.Id == id, cancellationToken);

    /// <inheritdoc />
    public async Task<PurchaseRequest?> GetWithItemsAndDeliveriesAsync(
        Guid id,
        CancellationToken cancellationToken = default)
        => await _context.PurchaseRequests
            .Include(pr => pr.Items)          // depth 1 — sibling includes, not nested
            .Include(pr => pr.Deliveries)     // depth 1
            .FirstOrDefaultAsync(pr => pr.Id == id, cancellationToken);

    /// <inheritdoc />
    public async Task<IReadOnlyList<PurchaseRequest>> GetByDivisionAsync(
        Division division,
        CancellationToken cancellationToken = default)
        => await _context.PurchaseRequests
            .Where(pr => pr.Division == division)
            .OrderByDescending(pr => pr.PRDate)
            .ToListAsync(cancellationToken);
}
