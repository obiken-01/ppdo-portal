namespace PPDO.Application.DTOs.Config;

/// <summary>Read model for a price index item.</summary>
public sealed record PriceIndexItemDto(
    int      Id,
    string   Name,
    string   Unit,
    decimal  UnitPrice,
    string?  Category,
    DateTime PriceUpdatedAt,
    bool     IsActive,
    bool     DaysEnabled,
    string?  StockCardNo);

/// <summary>Create/update body for a price index item. (Name, Unit) is the unique key.</summary>
public sealed record UpsertPriceIndexItemDto(
    string  Name,
    string  Unit,
    decimal UnitPrice,
    string? Category,
    bool    IsActive = true,
    bool    DaysEnabled = false,
    string? StockCardNo = null);
