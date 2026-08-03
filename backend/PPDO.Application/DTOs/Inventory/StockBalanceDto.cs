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
    DateTime UpdatedAt);

/// <summary>Request body for creating a new physical-count entry.</summary>
public sealed record CreateStockBalanceDto(
    string StockNo,
    decimal CountedQty,
    DateOnly EffectiveDate,
    string? Reason);

/// <summary>
/// Request body for editing an existing entry. Null fields are left unchanged.
/// StockNo is immutable after creation — delete and re-create instead.
/// </summary>
public sealed record UpdateStockBalanceDto(
    decimal? CountedQty,
    DateOnly? EffectiveDate,
    string? Reason);
