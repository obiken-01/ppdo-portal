namespace PPDO.Application.DTOs.Inventory;

/// <summary>
/// Response for POST /api/inventory/stock-balances/import/preview (RAL-193).
/// Parse-only — nothing is persisted until the rows are POSTed to the commit endpoint.
/// </summary>
public sealed record StockBalanceImportPreviewDto(
    IReadOnlyList<StockBalanceImportRowDto> Rows);

/// <summary>
/// One parsed row from the uploaded workbook. <see cref="Error"/> is set when the row is
/// unusable (missing StockNo, unparseable quantity/date) — such rows are excluded from the
/// commit payload by the frontend, but still shown to the user with the reason.
/// </summary>
public sealed record StockBalanceImportRowDto(
    int RowNumber,
    string? StockNo,
    decimal? CountedQty,
    DateOnly? EffectiveDate,
    string? Reason,
    // Only used when StockNo isn't already in Items Master — auto-creates the catalog
    // entry at commit time. Ignored when the StockNo is already known.
    string? Description,
    string? Unit,
    decimal? UnitCost,
    string? ItemType,
    string? Error);

/// <summary>
/// Request body for POST /api/inventory/stock-balances/import/commit — the rows the user
/// confirmed after reviewing the preview. Upserts by StockNo + EffectiveDate (re-uploading a
/// matching pair overwrites that entry).
/// </summary>
public sealed record CommitStockBalanceImportDto(
    IReadOnlyList<CreateStockBalanceDto> Rows);

/// <summary>Result of a bulk commit — how many rows were inserted vs. updated (upserted).</summary>
public sealed record StockBalanceImportResultDto(
    int Inserted,
    int Updated,
    IReadOnlyList<StockBalanceDto> Entries);
