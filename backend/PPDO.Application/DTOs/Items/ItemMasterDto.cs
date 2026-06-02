namespace PPDO.Application.DTOs.Items;

/// <summary>
/// Full item master record returned by <c>GET /api/items/master</c> and
/// <c>GET /api/items/master/{id}</c>.
/// </summary>
public sealed record ItemMasterDto(
    Guid     Id,
    string   StockNo,
    string   Description,
    string?  Category,
    string   Unit,
    decimal  UnitCost,
    string?  ItemType,
    int      ReorderQty,
    string?  Remarks,
    bool     IsNewItem,
    DateTime CreatedAt,
    DateTime UpdatedAt);
