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
    /// Returns all PRs for the given division id, ordered by PRDate descending.
    /// Used to enforce division-scope read rules for Staff.
    /// </summary>
    Task<IReadOnlyList<PurchaseRequest>> GetByDivisionAsync(int divisionId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns the highest PR sequence number (the last '-'-delimited segment of PRNo)
    /// across every PR ever created, computed as a single SQL aggregate — never
    /// materialises rows. Returns null if the table is empty or no PRNo matches the
    /// expected 7-segment format (mirrors the tolerant behaviour of the old in-memory
    /// ParseSequence helper: malformed/legacy PRNos are skipped, not fatal).
    /// Used by PurchaseRequestService.GeneratePRNoAsync.
    /// </summary>
    Task<int?> GetMaxPrSequenceAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Computes the Inventory Dashboard's PR stat-card counts and total value as SQL
    /// aggregates (Count/Sum), scoped to a division when given, without ever loading PR
    /// rows into memory. Used by InventoryService.GetStatsAsync in place of the old
    /// GetAllAsync()-then-LINQ-count approach.
    /// </summary>
    Task<PurchaseRequestStatsAggregate> GetStatsAggregateAsync(
        int? divisionId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Filtered, sorted, paged PR search for the PR List page — everything computed in SQL
    /// (WHERE + COUNT + ORDER BY + OFFSET/FETCH), never a full-table load. Division scope is
    /// applied the same way GetByDivisionAsync/GetAllAsync are today (null = all divisions).
    /// </summary>
    Task<PRSearchResult> SearchAsync(
        PRSearchCriteria criteria, int? divisionId, CancellationToken cancellationToken = default);
}

/// <summary>
/// PR stat-card counts and total value, scoped to a division (or all divisions when the
/// scope is null). See <see cref="IPurchaseRequestRepository.GetStatsAggregateAsync"/>.
/// </summary>
public sealed record PurchaseRequestStatsAggregate(
    int Total,
    int Open,
    int PartiallyDelivered,
    int FullyDeliveredOrCompleted,
    decimal TotalAmount);

/// <summary>
/// Filter/sort/page parameters for <see cref="IPurchaseRequestRepository.SearchAsync"/>.
/// All string fields are partial (Contains), case-insensitive matches; null/empty means
/// "no filter on this field". Mirrors the PR List page's Filters shape exactly.
/// </summary>
public sealed record PRSearchCriteria(
    string?  Search,
    DateOnly? DateFrom,
    DateOnly? DateTo,
    IReadOnlyList<PRStatus>? Statuses,
    string?  Division,
    string?  RequestedBy,
    string?  Fund,
    string?  AIPCode,
    string?  AccountNo,
    string?  AccountTitle,
    string?  Program,
    string?  Project,
    string?  Activity,
    string?  SortBy,
    bool     SortDescending,
    int      Page,
    int      PageSize);

/// <summary>Result of a PR search — the page of items plus the total match count and a
/// per-status breakdown of the filtered set (before paging, matching the current page's
/// own filter+status selection).</summary>
public sealed record PRSearchResult(
    IReadOnlyList<PurchaseRequest> Items,
    int TotalCount,
    PRStatusCounts Counts);

/// <summary>Per-status counts within a filtered PR search result.</summary>
public sealed record PRStatusCounts(
    int Open,
    int PartiallyDelivered,
    int FullyDelivered,
    int Completed);
