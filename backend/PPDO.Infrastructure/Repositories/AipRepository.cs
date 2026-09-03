using Microsoft.EntityFrameworkCore;
using PPDO.Application.Common;
using PPDO.Domain.Entities;
using PPDO.Domain.Interfaces;
using PPDO.Infrastructure.Data;

namespace PPDO.Infrastructure.Repositories;

/// <summary>
/// EF Core implementation of <see cref="IAipRepository"/>.
/// Each hierarchy read method applies its WHERE / IN filter in SQL so only
/// the relevant rows are transferred — not the entire table.
/// All four hierarchy tables are accessed via <c>_context.Set&lt;T&gt;()</c>
/// which is safe because <c>_context</c> is the shared scoped DbContext.
/// </summary>
public sealed class AipRepository : Repository<AipRecord>, IAipRepository
{
    public AipRepository(AppDbContext context) : base(context) { }

    /// <inheritdoc />
    public async Task<AipRecord?> GetByIntIdAsync(int id, CancellationToken ct = default)
        => await _context.Set<AipRecord>()
            .FirstOrDefaultAsync(r => r.Id == id, ct);

    /// <inheritdoc />
    public async Task<IReadOnlyList<AipOffice>> GetOfficesByAipIdAsync(
        int aipRecordId, CancellationToken ct = default)
        => await _context.Set<AipOffice>()
            .Where(o => o.AipRecordId == aipRecordId)
            .OrderBy(o => o.RefCode)
            .ToListAsync(ct);

    /// <inheritdoc />
    public async Task<AipOffice?> GetOfficeByIdAsync(int id, CancellationToken ct = default)
        => await _context.Set<AipOffice>().FirstOrDefaultAsync(o => o.Id == id, ct);

    /// <inheritdoc />
    public async Task<IReadOnlyList<AipOffice>> GetOfficesByAipIdsAsync(
        IReadOnlyList<int> aipIds, CancellationToken ct = default)
    {
        if (aipIds.Count == 0) return [];
        return await _context.Set<AipOffice>()
            .Where(o => aipIds.Contains(o.AipRecordId))
            .OrderBy(o => o.RefCode)
            .ToListAsync(ct);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<AipProgram>> GetProgramsByOfficeIdsAsync(
        IReadOnlyList<int> officeIds, CancellationToken ct = default)
    {
        if (officeIds.Count == 0) return [];
        return await _context.Set<AipProgram>()
            .Where(p => officeIds.Contains(p.OfficeId))
            .OrderBy(p => p.RefCode)
            .ToListAsync(ct);
    }

    /// <inheritdoc />
    public async Task<AipProgram?> GetProgramByIdAsync(int id, CancellationToken ct = default)
        => await _context.Set<AipProgram>().FirstOrDefaultAsync(p => p.Id == id, ct);

    /// <inheritdoc />
    public async Task<IReadOnlyList<AipProject>> GetProjectsByProgramIdsAsync(
        IReadOnlyList<int> programIds, CancellationToken ct = default)
    {
        if (programIds.Count == 0) return [];
        return await _context.Set<AipProject>()
            .Where(j => programIds.Contains(j.ProgramId))
            .OrderBy(j => j.RefCode)
            .ToListAsync(ct);
    }

    /// <inheritdoc />
    public async Task<AipProject?> GetProjectByIdAsync(int id, CancellationToken ct = default)
        => await _context.Set<AipProject>().FirstOrDefaultAsync(j => j.Id == id, ct);

    /// <inheritdoc />
    public async Task<IReadOnlyList<AipActivity>> GetActivitiesByProjectIdsAsync(
        IReadOnlyList<int> projectIds, CancellationToken ct = default)
    {
        if (projectIds.Count == 0) return [];
        return await _context.Set<AipActivity>()
            .Where(a => projectIds.Contains(a.ProjectId))
            .OrderBy(a => a.RefCode)
            .ToListAsync(ct);
    }

    /// <inheritdoc />
    public async Task<AipActivity?> GetActivityByIdAsync(int id, CancellationToken ct = default)
        => await _context.Set<AipActivity>().FirstOrDefaultAsync(a => a.Id == id, ct);

    /// <inheritdoc />
    public async Task<bool> ApplyActivityTotalsAsync(
        int activityId,
        AipExpenditureTotalsDto totals,
        bool zeroWhenNoLines = false,
        CancellationToken ct = default)
    {
        // ⚠️ The guard, before anything is loaded or written. "No lines" is either an FY≤2027
        // activity that has never had children — every historical row is one — or an activity whose
        // last line the caller just deleted. The data cannot tell them apart, so the caller does.
        // Defaulting to the safe reading means the failure mode of forgetting the flag is a total
        // that stays stale, not a fiscal year silently written to ₱0.
        if (totals.LineCount == 0 && !zeroWhenNoLines) return false;

        AipActivity? activity = await _context.Set<AipActivity>()
            .FirstOrDefaultAsync(a => a.Id == activityId, ct);
        if (activity is null) return false;

        activity.Ps    = totals.Ps;
        activity.Mooe  = totals.Mooe;
        activity.Co    = totals.Co;
        // Never null once lines exist: deleting the last line leaves 0, not null. Null meant
        // "never computed", which stops being a state that exists for an activity with children.
        activity.Total = totals.Total;

        return true;   // staged only — the calling service owns SaveChangesAsync
    }

    /// <inheritdoc />
    public async Task<AipRecord?> GetLatestByFiscalYearAsync(int fiscalYear, CancellationToken ct = default)
        => await _context.Set<AipRecord>()
            .Where(r => r.FiscalYear == fiscalYear && r.Status != PlanningStatus.Archived)
            .OrderBy(r => r.Id)
            .FirstOrDefaultAsync(ct);

    /// <inheritdoc />
    public async Task<IReadOnlyList<int>> GetDistinctFiscalYearsAsync(CancellationToken ct = default)
        => await _context.Set<AipRecord>()
            .Select(r => r.FiscalYear)
            .Distinct()
            .OrderByDescending(y => y)
            .ToListAsync(ct);

    /// <inheritdoc />
    public async Task<IReadOnlyList<AipOfficeRollupDto>> GetOfficeRollupsAsync(
        int aipRecordId, CancellationToken ct = default)
        => await (
                from office in _context.Set<AipOffice>()
                where office.AipRecordId == aipRecordId
                // Left joins: an office with no programs, or a program with no activities, must
                // still come back as a row of zeroes. Dropping it would make an office that exists
                // in the AIP but has nothing in it indistinguishable from one that was never
                // added — two different things on the dashboard ("In progress" vs "Todo").
                join program in _context.Set<AipProgram>() on office.Id equals program.OfficeId into programs
                from program in programs.DefaultIfEmpty()
                join project in _context.Set<AipProject>() on program.Id equals project.ProgramId into projects
                from project in projects.DefaultIfEmpty()
                join activity in _context.Set<AipActivity>() on project.Id equals activity.ProjectId into activities
                from activity in activities.DefaultIfEmpty()
                group activity by new { office.Id, office.RefCode } into g
                select new AipOfficeRollupDto(
                    g.Key.Id,
                    g.Key.RefCode,
                    g.Count(a => a != null),
                    g.Count(a => a != null && a.Total != null && a.Total != 0m),
                    g.Sum(a => a != null ? a.Total ?? 0m : 0m)))
            .ToListAsync(ct);

    /// <inheritdoc />
    public async Task<IReadOnlyList<AipProgramRollupDto>> GetProgramRollupsAsync(
        IReadOnlyList<int> aipOfficeIds, CancellationToken ct = default)
    {
        if (aipOfficeIds.Count == 0) return [];
        return await (
                from program in _context.Set<AipProgram>()
                where aipOfficeIds.Contains(program.OfficeId)
                join project in _context.Set<AipProject>() on program.Id equals project.ProgramId into projects
                from project in projects.DefaultIfEmpty()
                join activity in _context.Set<AipActivity>() on project.Id equals activity.ProjectId into activities
                from activity in activities.DefaultIfEmpty()
                group activity by new { program.OfficeId, program.RefCode } into g
                select new AipProgramRollupDto(
                    g.Key.OfficeId,
                    g.Key.RefCode,
                    g.Count(a => a != null),
                    g.Count(a => a != null && a.Total != null && a.Total != 0m),
                    g.Sum(a => a != null ? a.Total ?? 0m : 0m)))
            .ToListAsync(ct);
    }
}
