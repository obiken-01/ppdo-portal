using PPDO.Domain.Entities;

namespace PPDO.Domain.Interfaces;

/// <summary>
/// Item-master-specific data access contract.
/// Extends <see cref="IRepository{T}"/> with catalog lookup and admin-review queries
/// needed by ItemService and the Create PR autocomplete feature.
///
/// All methods are async and support CancellationToken.
/// </summary>
public interface IItemMasterRepository : IRepository<ItemMaster>
{
    /// <summary>
    /// Returns the catalog item with the given stock number, or null if not found.
    /// Used by Create PR and the Excel import parser to look up unit cost and description.
    /// </summary>
    Task<ItemMaster?> GetByStockNoAsync(string stockNo, CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns all items flagged for admin review (<see cref="ItemMaster.IsNewItem"/> = true),
    /// ordered by <see cref="ItemMaster.CreatedAt"/> ascending (oldest first).
    /// Displayed with the "★ NEW - review" flag in the Items Master UI.
    /// </summary>
    Task<IReadOnlyList<ItemMaster>> GetNewItemsAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Searches catalog items by stock number or description (case-insensitive partial match).
    /// Used by the Create PR form for bidirectional autocomplete lookup.
    /// Returns up to 20 results ordered by StockNo.
    /// </summary>
    Task<IReadOnlyList<ItemMaster>> SearchAsync(string term, CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns the catalog items matching the given stock numbers, via a single IN-list
    /// query — never the full catalog. Used by InventoryService to resolve
    /// Description/ReorderQty for a known set of StockNos (e.g. from stock levels already
    /// fetched from IInventoryRepository), without loading every ItemMaster row.
    /// </summary>
    Task<IReadOnlyList<ItemMaster>> GetByStockNosAsync(
        IReadOnlyCollection<string> stockNos, CancellationToken cancellationToken = default);

    /// <summary>
    /// Filtered, sorted, paged catalog search for the Items Master page — everything computed
    /// in SQL (WHERE + COUNT + ORDER BY + OFFSET/FETCH), never a full-table load. Mirrors
    /// IPurchaseRequestRepository.SearchAsync's shape.
    /// </summary>
    Task<ItemMasterSearchResult> SearchAsync(
        ItemMasterSearchCriteria criteria, CancellationToken cancellationToken = default);
}

/// <summary>
/// Filter/sort/page parameters for <see cref="IItemMasterRepository.SearchAsync"/>.
/// All string fields are partial (Contains), case-insensitive matches; null/empty means
/// "no filter on this field". <see cref="Search"/> matches across StockNo, Description,
/// Category, Unit, ItemType, and Remarks — mirrors the Items Master page's global search box;
/// the per-field parameters mirror its per-column filter row.
/// </summary>
public sealed record ItemMasterSearchCriteria(
    string? Search,
    string? StockNo,
    string? Description,
    string? Category,
    string? Unit,
    string? ItemType,
    string? Remarks,
    bool     IsNewOnly,
    string?  SortBy,
    bool     SortDescending,
    int      Page,
    int      PageSize);

/// <summary>
/// Result of an item catalog search — the page of items, the total match count, and the total
/// count of items flagged IsNewItem across the WHOLE catalog (not the filtered set — this
/// drives the page's "★ NEW" badge, which always reflects the full pending-review count).
/// </summary>
public sealed record ItemMasterSearchResult(
    IReadOnlyList<ItemMaster> Items,
    int TotalCount,
    int TotalNewItemCount);
