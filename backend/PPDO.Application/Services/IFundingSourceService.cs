using PPDO.Application.Common;
using PPDO.Application.DTOs.Config;

namespace PPDO.Application.Services;

/// <summary>
/// Funding source config CRUD + CSV upsert/export (RAL-70).
/// Soft delete only (IsActive = false). Code is the unique key.
/// </summary>
public interface IFundingSourceService
{
    /// <summary><paramref name="search"/> matches code OR name (case-insensitive, contains).</summary>
    Task<IReadOnlyList<FundingSourceDto>> GetAllAsync(
        string? search, ActiveFilter active, CancellationToken cancellationToken = default);

    Task<ServiceResult<FundingSourceDto>> GetByIdAsync(int id, CancellationToken cancellationToken = default);
    Task<ServiceResult<FundingSourceDto>> CreateAsync(UpsertFundingSourceDto dto, CancellationToken cancellationToken = default);
    Task<ServiceResult<FundingSourceDto>> UpdateAsync(int id, UpsertFundingSourceDto dto, CancellationToken cancellationToken = default);
    Task<ServiceResult<FundingSourceDto>> DeleteAsync(int id, CancellationToken cancellationToken = default);

    /// <summary>Exports all funding sources as CSV: code, name, description, is_active.</summary>
    Task<string> ExportCsvAsync(CancellationToken cancellationToken = default);

    /// <summary>Upserts funding sources by code. Returns new/updated/skipped counts.</summary>
    Task<ServiceResult<CsvImportResult>> ImportCsvAsync(string csvText, CancellationToken cancellationToken = default);
}
