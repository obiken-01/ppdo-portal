using PPDO.Application.Common;
using PPDO.Application.DTOs.Config;

namespace PPDO.Application.Services;

/// <summary>
/// Office config CRUD + CSV upsert/export (RAL-70; extends the read-only RAL-81 shape).
/// Soft delete only (IsActive = false). OfficeCode is the unique key.
/// The dropdown variant used by User Management is GetAllAsync(search: null, ActiveFilter.Active).
/// </summary>
public interface IOfficeService
{
    /// <summary><paramref name="search"/> matches office code OR name (case-insensitive, contains).</summary>
    Task<IReadOnlyList<OfficeDto>> GetAllAsync(
        string? search, ActiveFilter active, CancellationToken cancellationToken = default);

    Task<ServiceResult<OfficeDto>> GetByIdAsync(int id, CancellationToken cancellationToken = default);
    Task<ServiceResult<OfficeDto>> CreateAsync(UpsertOfficeDto dto, CancellationToken cancellationToken = default);
    Task<ServiceResult<OfficeDto>> UpdateAsync(int id, UpsertOfficeDto dto, CancellationToken cancellationToken = default);
    Task<ServiceResult<OfficeDto>> DeleteAsync(int id, CancellationToken cancellationToken = default);

    /// <summary>Exports all offices as CSV: office_code, office_name, is_active.</summary>
    Task<string> ExportCsvAsync(CancellationToken cancellationToken = default);

    /// <summary>Upserts offices by office_code. Returns new/updated/skipped counts.</summary>
    Task<ServiceResult<CsvImportResult>> ImportCsvAsync(string csvText, CancellationToken cancellationToken = default);
}
