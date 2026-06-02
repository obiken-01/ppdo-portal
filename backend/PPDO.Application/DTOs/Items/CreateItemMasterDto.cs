namespace PPDO.Application.DTOs.Items;

/// <summary>
/// Request body for <c>POST /api/items/master</c>.
/// Requires CanAccessInventory permission.
/// </summary>
public sealed record CreateItemMasterDto(
    string   StockNo,
    string   Description,
    string   Unit,
    decimal  UnitCost,
    string?  Category,
    string?  ItemType,
    int      ReorderQty,
    string?  Remarks,
    /// <summary>
    /// True when the item is being added via Create PR (unknown stock).
    /// False when added directly via Items Master admin UI.
    /// </summary>
    bool     IsNewItem);
