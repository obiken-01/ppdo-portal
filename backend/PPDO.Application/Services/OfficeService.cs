using PPDO.Application.DTOs.Config;
using PPDO.Domain.Entities;
using PPDO.Domain.Interfaces;

namespace PPDO.Application.Services;

/// <summary>
/// Read-only office queries for the config offices table (RAL-81).
/// Uses the generic repository — the offices table is small (16 rows).
/// </summary>
public sealed class OfficeService : IOfficeService
{
    private readonly IRepository<Office> _offices;

    public OfficeService(IRepository<Office> offices)
    {
        _offices = offices;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<OfficeDto>> GetAllAsync(
        bool activeOnly,
        CancellationToken cancellationToken = default)
    {
        IReadOnlyList<Office> offices = await _offices.GetAllAsync(cancellationToken);

        return offices
            .Where(o => !activeOnly || o.IsActive)
            .OrderBy(o => o.OfficeName)
            .Select(o => new OfficeDto(o.Id, o.OfficeCode, o.OfficeName, o.IsActive))
            .ToList();
    }
}
