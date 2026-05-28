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
}
