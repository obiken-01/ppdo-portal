using PPDO.Application.Common;
using PPDO.Application.DTOs.Config;

namespace PPDO.Application.Services;

/// <summary>
/// Price Index config CRUD + CSV upsert/export (v1.4 — RAL-118).
/// Soft delete only (IsActive = false). (Name, Unit) is the unique key.
/// </summary>
public interface IPriceIndexService
{
    /// <summary><paramref name="search"/> matches name OR category (case-insensitive, contains).</summary>
    Task<IReadOnlyList<PriceIndexItemDto>> GetAllAsync(
        string? search, ActiveFilter active, CancellationToken cancellationToken = default);

    /// <summary>
    /// Row count only, same filters as <see cref="GetAllAsync"/> (RAL-232) — for the Config
    /// dashboard tile, which previously downloaded the whole catalogue to read <c>.length</c>.
    /// </summary>
    Task<int> GetCountAsync(string? search, ActiveFilter active, CancellationToken cancellationToken = default);

    /// <summary>
    /// Slim list for item pickers (RAL-232) — five fields instead of nine, projected in SQL.
    /// Use this anywhere the full record is not genuinely needed; the management grid still
    /// uses <see cref="GetAllAsync"/>.
    /// </summary>
    Task<IReadOnlyList<PriceIndexPickerItemDto>> GetPickerListAsync(
        string? search, ActiveFilter active, CancellationToken cancellationToken = default);

    /// <summary>
    /// Filtered, sorted, paged read for the management grid (RAL-233). <paramref name="page"/> is
    /// 1-based. An unrecognized <paramref name="sortColumn"/> falls back to Name ascending.
    /// </summary>
    Task<PriceIndexPageDto> GetPagedAsync(
        string? search, ActiveFilter active, string? sortColumn, bool sortDescending,
        int page, int pageSize, CancellationToken cancellationToken = default);

    Task<ServiceResult<PriceIndexItemDto>> GetByIdAsync(int id, CancellationToken cancellationToken = default);
    Task<ServiceResult<PriceIndexItemDto>> CreateAsync(UpsertPriceIndexItemDto dto, CancellationToken cancellationToken = default);
    Task<ServiceResult<PriceIndexItemDto>> UpdateAsync(int id, UpsertPriceIndexItemDto dto, CancellationToken cancellationToken = default);
    Task<ServiceResult<PriceIndexItemDto>> DeleteAsync(int id, CancellationToken cancellationToken = default);

    /// <summary>Exports all price index items as CSV: name, unit, unit_price, category, is_active.</summary>
    Task<string> ExportCsvAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Upserts price index items by (name, unit). This is the PRIMARY real-world ingestion
    /// path — PPDO uploads price lists downloaded from GSO's own application — so malformed
    /// rows are skipped with a specific per-row error rather than failing the whole import.
    /// </summary>
    Task<ServiceResult<CsvImportResult>> ImportCsvAsync(string csvText, CancellationToken cancellationToken = default);
}
