using Microsoft.EntityFrameworkCore;
using PPDO.Domain.Entities;
using PPDO.Domain.Interfaces;
using PPDO.Infrastructure.Data;

namespace PPDO.Infrastructure.Repositories;

/// <summary>
/// EF Core implementation of <see cref="IActivityLabelRepository"/> (PPDO-181). One untracked
/// projection per call — only the few columns a sentence needs, never whole entities.
/// </summary>
public sealed class ActivityLabelRepository : IActivityLabelRepository
{
    private readonly AppDbContext _context;

    public ActivityLabelRepository(AppDbContext context) => _context = context;

    /// <inheritdoc />
    public async Task<IReadOnlyDictionary<int, CeilingLabel>> GetCeilingLabelsAsync(
        IReadOnlyCollection<int> ids, CancellationToken cancellationToken = default)
    {
        if (ids.Count == 0) return new Dictionary<int, CeilingLabel>();

        var rows = await _context.Set<BudgetCeiling>().AsNoTracking()
            .Where(c => ids.Contains(c.Id))
            .Select(c => new { c.Id, Code = c.Office != null ? c.Office.OfficeCode : null, c.FiscalYear })
            .ToListAsync(cancellationToken);

        return rows.ToDictionary(r => r.Id, r => new CeilingLabel(r.Code, r.FiscalYear));
    }

    /// <inheritdoc />
    public async Task<IReadOnlyDictionary<int, AipOfficeLabel>> GetAipOfficeLabelsAsync(
        IReadOnlyCollection<int> ids, CancellationToken cancellationToken = default)
    {
        if (ids.Count == 0) return new Dictionary<int, AipOfficeLabel>();

        var rows = await _context.Set<AipOffice>().AsNoTracking()
            .Where(o => ids.Contains(o.Id))
            .Select(o => new { o.Id, Code = o.Office != null ? o.Office.OfficeCode : null, o.AipRecord.FiscalYear })
            .ToListAsync(cancellationToken);

        return rows.ToDictionary(r => r.Id, r => new AipOfficeLabel(r.Code, r.FiscalYear));
    }

    /// <inheritdoc />
    public async Task<IReadOnlyDictionary<int, AipProgramLabel>> GetAipProgramLabelsAsync(
        IReadOnlyCollection<int> ids, CancellationToken cancellationToken = default)
    {
        if (ids.Count == 0) return new Dictionary<int, AipProgramLabel>();

        var rows = await _context.Set<AipProgram>().AsNoTracking()
            .Where(p => ids.Contains(p.Id))
            .Select(p => new { p.Id, p.Name, Code = p.Office.Office != null ? p.Office.Office.OfficeCode : null })
            .ToListAsync(cancellationToken);

        return rows.ToDictionary(r => r.Id, r => new AipProgramLabel(r.Code, r.Name));
    }

    /// <inheritdoc />
    public async Task<IReadOnlyDictionary<int, AipActivityLabel>> GetAipActivityLabelsAsync(
        IReadOnlyCollection<int> ids, CancellationToken cancellationToken = default)
    {
        if (ids.Count == 0) return new Dictionary<int, AipActivityLabel>();

        var rows = await _context.Set<AipActivity>().AsNoTracking()
            .Where(a => ids.Contains(a.Id))
            .Select(a => new
            {
                a.Id,
                ProjectName = a.Project.Name,
                Code = a.Project.Program.Office.Office != null ? a.Project.Program.Office.Office.OfficeCode : null,
            })
            .ToListAsync(cancellationToken);

        return rows.ToDictionary(r => r.Id, r => new AipActivityLabel(r.Code, r.ProjectName));
    }

    /// <inheritdoc />
    public async Task<IReadOnlyDictionary<int, string>> GetOfficeCodesAsync(
        IReadOnlyCollection<int> ids, CancellationToken cancellationToken = default)
    {
        if (ids.Count == 0) return new Dictionary<int, string>();

        return await _context.Set<Office>().AsNoTracking()
            .Where(o => ids.Contains(o.Id))
            .ToDictionaryAsync(o => o.Id, o => o.OfficeCode, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyDictionary<int, string>> GetDivisionNamesAsync(
        IReadOnlyCollection<int> ids, CancellationToken cancellationToken = default)
    {
        if (ids.Count == 0) return new Dictionary<int, string>();

        return await _context.Set<Division>().AsNoTracking()
            .Where(d => ids.Contains(d.Id))
            .ToDictionaryAsync(d => d.Id, d => d.Name, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyDictionary<int, string>> GetFundingSourceNamesAsync(
        IReadOnlyCollection<int> ids, CancellationToken cancellationToken = default)
    {
        if (ids.Count == 0) return new Dictionary<int, string>();

        return await _context.Set<FundingSource>().AsNoTracking()
            .Where(f => ids.Contains(f.Id))
            .ToDictionaryAsync(f => f.Id, f => f.Name, cancellationToken);
    }
}
