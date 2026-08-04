namespace PPDO.Application.DTOs.Inventory;

/// <summary>
/// A single warehouse physical-count ledger entry (RAL-193) — the reconciliation view row.
/// </summary>
public sealed record StockBalanceDto(
    Guid Id,
    string StockNo,
    decimal CountedQty,
    decimal SystemOnHandAtEntry,
    decimal VarianceQty,
    DateOnly EffectiveDate,
    string? Reason,
    Guid RecordedByUserId,
    string? RecordedByName,
    DateTime CreatedAt,
    DateTime UpdatedAt,
    /// <summary>True when this entry's save also auto-created a new ItemMaster row
    /// (IsNewItem = true, pending admin review) because the StockNo wasn't cataloged yet.</summary>
    bool ItemWasAutoCreated);

/// <summary>
/// Request body for creating a new physical-count entry. Description/Unit/UnitCost/ItemType
/// are only used when StockNo isn't found in Items Master — mirrors
/// PurchaseRequestService.BuildItemsAsync: an unknown StockNo auto-creates a new ItemMaster
/// row (IsNewItem = true, pending admin review) using these fields. Description and Unit are
/// required in that case; ignored (the catalog's own values win) when the StockNo is already known.
/// </summary>
public sealed record CreateStockBalanceDto(
    string StockNo,
    decimal CountedQty,
    DateOnly EffectiveDate,
    string? Reason,
    string? Description,
    string? Unit,
    decimal? UnitCost,
    string? ItemType);

/// <summary>
/// Request body for editing an existing entry. Null fields are left unchanged.
/// StockNo is immutable after creation — delete and re-create instead.
/// </summary>
public sealed record UpdateStockBalanceDto(
    decimal? CountedQty,
    DateOnly? EffectiveDate,
    string? Reason);

/// <summary>
/// The system's current computed on-hand for a StockNo — SUM(VarianceQty) + QtyDelivered -
/// QtyDistributed, i.e. exactly what a new entry's SystemOnHandAtEntry would be if saved
/// right now. Surfaced to the manual entry form as a reference before the user submits a
/// count, so they can see what the system currently expects.
/// </summary>
public sealed record SystemOnHandDto(string StockNo, decimal OnHand);
