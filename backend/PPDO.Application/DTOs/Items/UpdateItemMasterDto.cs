namespace PPDO.Application.DTOs.Items;

/// <summary>
/// Request body for <c>PUT /api/items/master/{id}</c>.
/// Requires CanAccessInventory permission.
/// All fields are optional — null values leave the existing value unchanged.
/// Setting <see cref="IsNewItem"/> to false clears the "★ NEW - review" flag.
/// </summary>
public sealed record UpdateItemMasterDto(
    string?  StockNo,
    string?  Description,
    string?  Unit,
    decimal? UnitCost,
    string?  Category,
    string?  ItemType,
    int?     ReorderQty,
    string?  Remarks,
    bool     IsNewItem);
