using PPDO.Application.Common;
using PPDO.Application.DTOs.Config;

namespace PPDO.Application.Services;

/// <summary>
/// CRUD for the CCET climate-change typology vocabulary (RAL-247).
/// Soft delete only — AIP activities reference these codes.
/// </summary>
public interface IClimateChangeTypologyService
{
    Task<IReadOnlyList<ClimateChangeTypologyDto>> GetAllAsync(
        string? search, ActiveFilter active, CancellationToken cancellationToken = default);

    Task<ServiceResult<ClimateChangeTypologyDto>> GetByIdAsync(
        int id, CancellationToken cancellationToken = default);

    Task<ServiceResult<ClimateChangeTypologyDto>> CreateAsync(
        UpsertClimateChangeTypologyDto dto, CancellationToken cancellationToken = default);

    Task<ServiceResult<ClimateChangeTypologyDto>> UpdateAsync(
        int id, UpsertClimateChangeTypologyDto dto, CancellationToken cancellationToken = default);

    /// <summary>
    /// Counts rows matching the same filters the list applies, in SQL (RAL-260).
    /// Serves the Config dashboard tile, which must never download a list to measure it.
    /// </summary>
    Task<int> GetCountAsync(
        string? search, ActiveFilter active, CancellationToken cancellationToken = default);

    /// <summary>Soft delete — sets IsActive = false. Never removes the row.</summary>
    Task<ServiceResult<ClimateChangeTypologyDto>> DeleteAsync(
        int id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Exports every typology as CSV: <c>code,name,category,description,is_active</c> (PPDO-19).
    /// Includes inactive rows — an export is a backup, and a round-trip that silently dropped
    /// the soft-deleted ones would reactivate nothing but would lose them on re-import.
    /// </summary>
    Task<string> ExportCsvAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Upserts typologies keyed on <c>code</c>. Returns new/updated/skipped counts (PPDO-19).
    /// Every row this actually changes gets an audit entry; unchanged rows get none.
    /// </summary>
    Task<ServiceResult<CsvImportResult>> ImportCsvAsync(
        string csvText, CancellationToken cancellationToken = default);
}
