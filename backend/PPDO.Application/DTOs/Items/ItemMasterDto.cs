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

/// <summary>Filter/sort/page parameters for <c>GET /api/items/master/search</c>.</summary>
public sealed record ItemMasterSearchFilterDto(
    int     Page,
    int     PageSize,
    string? Search,
    string? StockNo,
    string? Description,
    string? Category,
    string? Unit,
    string? ItemType,
    string? Remarks,
    bool    IsNewOnly,
    string? SortBy,
    bool    SortDescending);

/// <summary>Response shape for <c>GET /api/items/master/search</c>.</summary>
public sealed record ItemMasterSearchResultDto(
    IReadOnlyList<ItemMasterDto> Items,
    int TotalCount,
    int TotalNewItemCount,
    int Page,
    int PageSize);
