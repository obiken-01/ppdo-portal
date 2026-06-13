using PPDO.Application.DTOs.Config;

namespace PPDO.Application.Services;

/// <summary>
/// Read-only access to the offices config table. Introduced in RAL-81 so the User
/// Management office dropdown can be populated; full config management is RAL-70.
/// </summary>
public interface IOfficeService
{
    /// <summary>
    /// Returns offices ordered by name.
    /// When <paramref name="activeOnly"/> is true, inactive (soft-deleted) offices are excluded.
    /// </summary>
    Task<IReadOnlyList<OfficeDto>> GetAllAsync(bool activeOnly, CancellationToken cancellationToken = default);
}
