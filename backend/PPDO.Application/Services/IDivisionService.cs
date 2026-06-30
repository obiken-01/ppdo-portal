using PPDO.Application.Common;
using PPDO.Application.DTOs.Config;
using PPDO.Domain.Entities;

namespace PPDO.Application.Services;

/// <summary>
/// Configurable division CRUD + CSV upsert/export (v1.2 — RAL-97 list, RAL-98 full CRUD).
/// Soft delete only. Name is the upsert key within an office; Code is optional.
/// </summary>
public interface IDivisionService
{
    /// <summary>
    /// Returns divisions, optionally filtered by active status and/or office.
    /// Ordered by office then name.
    /// </summary>
    Task<IReadOnlyList<DivisionDto>> GetAllAsync(
        bool? activeOnly = null,
        int? officeId = null,
        CancellationToken cancellationToken = default);

    Task<ServiceResult<DivisionDto>> GetByIdAsync(int id, CancellationToken cancellationToken = default);
    Task<ServiceResult<DivisionDto>> CreateAsync(UpsertDivisionDto dto, CancellationToken cancellationToken = default);
    Task<ServiceResult<DivisionDto>> UpdateAsync(int id, UpsertDivisionDto dto, CancellationToken cancellationToken = default);
    Task<ServiceResult<DivisionDto>> DeleteAsync(int id, CancellationToken cancellationToken = default);

    /// <summary>Exports all divisions as CSV with the canonical 11-column order.</summary>
    Task<string> ExportCsvAsync(IReadOnlyList<Office> offices, CancellationToken cancellationToken = default);

    /// <summary>Upserts divisions by (office_code, name). Returns new/updated/skipped counts.</summary>
    Task<ServiceResult<CsvImportResult>> ImportCsvAsync(string csvText, IReadOnlyList<Office> offices, CancellationToken cancellationToken = default);
}
