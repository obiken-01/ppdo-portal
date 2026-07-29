using Microsoft.EntityFrameworkCore;
using PPDO.Domain.Entities;
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

    /// <summary>
    /// Overrides the generic base to eager-load <see cref="PurchaseRequest.Division"/> —
    /// callers (PurchaseRequestService.MapToSummary) read pr.Division?.Name, which is null
    /// without this. Depth 1.
    /// </summary>
    /// <inheritdoc />
    public override async Task<IReadOnlyList<PurchaseRequest>> GetAllAsync(
        CancellationToken cancellationToken = default)
        => await _context.PurchaseRequests
            .Include(pr => pr.Division)       // depth 1
            .ToListAsync(cancellationToken);

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
            .Include(pr => pr.Division)       // depth 1 — sibling include, not nested
            .FirstOrDefaultAsync(pr => pr.Id == id, cancellationToken);

    /// <inheritdoc />
    public async Task<PurchaseRequest?> GetWithItemsAndDeliveriesAsync(
        Guid id,
        CancellationToken cancellationToken = default)
        => await _context.PurchaseRequests
            .Include(pr => pr.Items)          // depth 1 — sibling includes, not nested
            .Include(pr => pr.Deliveries)     // depth 1
            .Include(pr => pr.Division)       // depth 1
            .FirstOrDefaultAsync(pr => pr.Id == id, cancellationToken);

    /// <inheritdoc />
    public async Task<IReadOnlyList<PurchaseRequest>> GetByDivisionAsync(
        int divisionId,
        CancellationToken cancellationToken = default)
        => await _context.PurchaseRequests
            .Where(pr => pr.DivisionId == divisionId)
            .Include(pr => pr.Division)       // depth 1 — pr.Division?.Name is read downstream
            .OrderByDescending(pr => pr.PRDate)
            .ToListAsync(cancellationToken);
}
