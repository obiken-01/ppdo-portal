using PPDO.Application.DTOs.Config;

namespace PPDO.Application.Services;

/// <summary>
/// Read access to the configurable divisions (v1.2 — RAL-97).
/// Minimal in RAL-97 (list only — to drive the user form + later pages); full CRUD + CSV
/// upsert/export arrives in the division config ticket (RAL-98).
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
}
