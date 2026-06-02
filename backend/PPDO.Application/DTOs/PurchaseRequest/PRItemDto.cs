namespace PPDO.Application.DTOs.PurchaseRequest;

/// <summary>Response DTO for a single PR line item.</summary>
public sealed record PRItemDto(
    Guid Id,
    Guid PRId,
    int ItemNo,
    string? StockNo,
    string Description,
    string Unit,
    decimal Quantity,
    decimal UnitCost,
    decimal TotalCost,
    string? ItemType);
