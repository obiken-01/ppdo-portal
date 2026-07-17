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
}
