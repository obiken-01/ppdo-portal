using PPDO.Application.DTOs.Config;
using PPDO.Domain.Entities;
using PPDO.Domain.Interfaces;

namespace PPDO.Application.Services;

/// <summary>
/// Read access to the configurable divisions (v1.2 — RAL-97). List-only for now;
/// full CRUD + CSV upsert/export arrives in RAL-98.
/// </summary>
public sealed class DivisionService : IDivisionService
{
    private readonly IRepository<Division> _divisions;
    private readonly IRepository<Office> _offices;

    public DivisionService(IRepository<Division> divisions, IRepository<Office> offices)
    {
        _divisions = divisions;
        _offices   = offices;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<DivisionDto>> GetAllAsync(
        bool? activeOnly = null,
        int? officeId = null,
        CancellationToken cancellationToken = default)
    {
        IReadOnlyList<Division> divisions = await _divisions.GetAllAsync(cancellationToken);
        IReadOnlyList<Office> offices = await _offices.GetAllAsync(cancellationToken);
        Dictionary<int, string> officeNames = offices.ToDictionary(o => o.Id, o => o.OfficeName);

        IEnumerable<Division> query = divisions;
        if (activeOnly == true) query = query.Where(d => d.IsActive);
        if (officeId is int oid) query = query.Where(d => d.OfficeId == oid);

        return query
            .OrderBy(d => d.OfficeId)
            .ThenBy(d => d.Name)
            .Select(d => new DivisionDto(
                d.Id,
                d.OfficeId,
                officeNames.GetValueOrDefault(d.OfficeId),
                d.Code,
                d.Name,
                d.IsActive,
                d.CanAccessInventory,
                d.CanAccessReports,
                d.CanManageUsers,
                d.CanManageResourceLinks,
                d.CanAccessBudgetPlanning,
                d.CanUploadAip,
                d.CanManageConfig))
            .ToList();
    }
}
