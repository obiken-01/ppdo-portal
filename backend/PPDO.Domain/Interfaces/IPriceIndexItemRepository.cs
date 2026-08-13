using PPDO.Domain.Entities;

namespace PPDO.Domain.Interfaces;

/// <summary>
/// Repository contract for <see cref="PriceIndexItem"/> with a scoped by-ids read (RAL-164 —
/// perf audit Tier 1). <c>PriceIndexService</c> itself still uses plain
/// <see cref="IRepository{T}"/> — the GSO price-index catalogue is a low-severity config-table
/// carve-out per docs/Performance_Audit_2026-07-16.md. This repository exists specifically for
/// <c>ProcurementPresetService</c>, which validates/snapshots a batch of price-index item
/// ids on every preset save and should not full-scan the catalogue to do it.
/// </summary>
public interface IPriceIndexItemRepository : IRepository<PriceIndexItem>
{
    /// <summary>Returns the price index items matching any of the given ids.</summary>
    Task<IReadOnlyList<PriceIndexItem>> GetByIdsAsync(
        IReadOnlyList<int> ids, CancellationToken ct = default);

    /// <summary>
    /// Returns items filtered by active flag (null = both) and/or a name/category search term,
    /// ordered by name — pushed to SQL (RAL-166 follow-up). The catalogue actually runs to
    /// ~6,400 rows in practice, contradicting the "genuinely small (tens to a few hundred rows)"
    /// assumption the 2026-07-16 perf audit carved this table out under; the WFP Entry Wizard's
    /// mount-time list call was materializing and filtering the whole table in memory.
    /// </summary>
    Task<IReadOnlyList<PriceIndexItem>> GetFilteredAsync(
        bool? isActive, string? search, CancellationToken ct = default);

    /// <summary>
    /// Row count only, same filters as <see cref="GetFilteredAsync"/> (RAL-232). Exists because
    /// the Config dashboard tile was downloading the entire catalogue (~1.57 MB) to render
    /// <c>.length</c>.
    /// </summary>
    Task<int> CountAsync(bool? isActive, string? search, CancellationToken ct = default);

    /// <summary>
    /// The five columns an item picker needs, projected in SQL so the wide free-text columns are
    /// never read or materialised (RAL-232). Same filters and ordering as
    /// <see cref="GetFilteredAsync"/>.
    ///
    /// Dropping Category, PriceUpdatedAt, StockCardNo and IsActive takes the serialized response
    /// from ~1,569 KB to ~686 KB over the real 6,397-row catalogue — 56% smaller.
    /// </summary>
    Task<IReadOnlyList<PriceIndexPickerItem>> GetPickerItemsAsync(
        bool? isActive, string? search, CancellationToken ct = default);
}

/// <summary>
/// Slim projection of <see cref="PriceIndexItem"/> for pickers — the only fields the WFP
/// procurement item table and the procurement-preset editor actually read (RAL-232).
///
/// Declared alongside its repository interface, matching <c>WfpActivityCoverageDto</c> in
/// <c>IWfpExpenditureRepository</c> and <c>PurchaseRequestStatsAggregate</c> in
/// <c>IPurchaseRequestRepository</c>.
/// </summary>
public sealed record PriceIndexPickerItem(
    int     Id,
    string  Name,
    string  Unit,
    decimal UnitPrice,
    bool    DaysEnabled);
