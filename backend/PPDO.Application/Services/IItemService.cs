using PPDO.Application.Common;
using PPDO.Application.DTOs.Items;
using PPDO.Domain.Entities;

namespace PPDO.Application.Services;

/// <summary>
/// Items Master CRUD and lookup operations.
/// Implemented in <c>ItemService.cs</c>.
///
/// All write operations require CanAccessInventory permission.
/// Read operations (GetAll, GetById, Lookup) are available to all authenticated users.
/// </summary>
public interface IItemService
{
    /// <summary>Returns the full Items Master catalog ordered by StockNo.</summary>
    Task<IReadOnlyList<ItemMasterDto>> GetAllAsync(
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns a single item by ID.
    /// Returns <see cref="ServiceErrorCode.NotFound"/> when not found.
    /// </summary>
    Task<ServiceResult<ItemMasterDto>> GetByIdAsync(
        Guid id,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Bidirectional autocomplete lookup — searches by StockNo OR Description
    /// (case-insensitive partial match). Returns up to 20 results.
    /// Empty term returns an empty list without querying the database.
    /// Used by the Create PR form and Excel import parser.
    /// </summary>
    Task<IReadOnlyList<ItemLookupDto>> LookupAsync(
        string term,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Adds a new item to the catalog.
    ///
    /// Returns:
    ///   <see cref="ServiceErrorCode.Forbidden"/>  — caller lacks CanAccessInventory.
    ///   <see cref="ServiceErrorCode.BadRequest"/>  — StockNo or Description is empty.
    ///   <see cref="ServiceErrorCode.Conflict"/>   — StockNo already exists.
    /// </summary>
    Task<ServiceResult<ItemMasterDto>> CreateAsync(
        User requester,
        CreateItemMasterDto dto,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Updates an existing item record.
    /// Passing <c>IsNewItem = false</c> clears the "★ NEW - review" flag.
    ///
    /// Returns:
    ///   <see cref="ServiceErrorCode.Forbidden"/>  — caller lacks CanAccessInventory.
    ///   <see cref="ServiceErrorCode.NotFound"/>   — item not found.
    /// </summary>
    Task<ServiceResult<ItemMasterDto>> UpdateAsync(
        User requester,
        Guid id,
        UpdateItemMasterDto dto,
        CancellationToken cancellationToken = default);
}
