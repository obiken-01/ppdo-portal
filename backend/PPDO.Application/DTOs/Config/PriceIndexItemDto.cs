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

/// <summary>
/// Slim read model for item pickers (RAL-232) — the only fields the WFP procurement item table
/// and the procurement-preset editor read. Deliberately NOT a subset type of
/// <see cref="PriceIndexItemDto"/>: the management grid needs all nine fields and must keep them.
///
/// Over the real 6,397-row catalogue this is ~686 KB versus ~1,569 KB for the full DTO.
/// </summary>
public sealed record PriceIndexPickerItemDto(
    int     Id,
    string  Name,
    string  Unit,
    decimal UnitPrice,
    bool    DaysEnabled);

/// <summary>
/// A page of price-index management-grid rows plus the total match count (RAL-233) — same
/// {Items, TotalCount, Page, PageSize} shape as <c>AuditLogPageDto</c>.
/// </summary>
public sealed record PriceIndexPageDto(
    IReadOnlyList<PriceIndexItemDto> Items,
    int TotalCount,
    int Page,
    int PageSize);

/// <summary>Create/update body for a price index item. (Name, Unit) is the unique key.</summary>
public sealed record UpsertPriceIndexItemDto(
    string  Name,
    string  Unit,
    decimal UnitPrice,
    string? Category,
    bool    IsActive = true,
    bool    DaysEnabled = false,
    string? StockCardNo = null);
