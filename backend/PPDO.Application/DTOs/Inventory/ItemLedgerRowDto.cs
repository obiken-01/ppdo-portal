namespace PPDO.Application.DTOs.Inventory;

/// <summary>
/// A single row in the Item Ledger — running stock totals per catalog item.
/// OnHand = QtyDelivered - QtyDistributed (what's physically in the stockroom).
/// IsLowStock = OnHand ≤ ReorderQty (alert threshold from Items Master).
/// IsOutOfStock = OnHand ≤ 0.
/// </summary>
public sealed record ItemLedgerRowDto(
    string StockNo,
    string Description,
    string? Category,
    string Unit,
    decimal UnitCost,
    string? ItemType,
    int ReorderQty,
    decimal QtyOrdered,
    decimal QtyDelivered,
    decimal QtyDistributed,
    decimal OnHand,
    bool IsLowStock,
    bool IsOutOfStock);
