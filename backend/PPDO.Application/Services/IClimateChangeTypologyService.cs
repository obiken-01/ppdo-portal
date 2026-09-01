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

    /// <summary>Soft delete — sets IsActive = false. Never removes the row.</summary>
    Task<ServiceResult<ClimateChangeTypologyDto>> DeleteAsync(
        int id, CancellationToken cancellationToken = default);
}
