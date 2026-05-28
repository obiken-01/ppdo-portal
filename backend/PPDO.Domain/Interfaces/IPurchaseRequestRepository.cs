using PPDO.Domain.Entities;
using PPDO.Domain.Enums;

namespace PPDO.Domain.Interfaces;

/// <summary>
/// Purchase-request-specific data access contract.
/// Extends <see cref="IRepository{T}"/> with domain queries needed by
/// PurchaseRequestService and the Azure Function handlers.
///
/// All methods are async and support CancellationToken.
/// Implementations must never use Include chains deeper than 2 levels.
/// </summary>
public interface IPurchaseRequestRepository : IRepository<PurchaseRequest>
{
    /// <summary>Returns the PR matching the unique PR number, or null if not found.</summary>
    Task<PurchaseRequest?> GetByPRNoAsync(string prNo, CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns a PR with its <see cref="PurchaseRequest.Items"/> collection eager-loaded.
    /// Used when the caller needs line-item detail (e.g. Edit PR, Calculate Total).
    /// Include depth: 1.
    /// </summary>
    Task<PurchaseRequest?> GetWithItemsAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns a PR with both <see cref="PurchaseRequest.Items"/> and
    /// <see cref="PurchaseRequest.Deliveries"/> eager-loaded.
    /// Used by the PR Report and Excel export endpoints (Sections 1, 2, 3).
    /// Include depth: 1 per navigation (two sibling includes — not nested).
    /// </summary>
    Task<PurchaseRequest?> GetWithItemsAndDeliveriesAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns all PRs for the given division, ordered by PRDate descending.
    /// Used to enforce division-scope read rules for Staff and Observer roles.
    /// </summary>
    Task<IReadOnlyList<PurchaseRequest>> GetByDivisionAsync(Division division, CancellationToken cancellationToken = default);
}
