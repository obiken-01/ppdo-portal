using Microsoft.EntityFrameworkCore;
using PPDO.Domain.Common;
using PPDO.Domain.Entities;
using PPDO.Domain.Interfaces;
using PPDO.Infrastructure.Data;

namespace PPDO.Infrastructure.Repositories;

/// <summary>EF Core implementation of <see cref="IInvestmentProposalRepository"/> (PPDO-154).</summary>
public sealed class InvestmentProposalRepository : IInvestmentProposalRepository
{
    private readonly AppDbContext _context;

    public InvestmentProposalRepository(AppDbContext context) => _context = context;

    /// <inheritdoc />
    /// <summary>One project with its program and AIP office: the row every scoped read starts from.</summary>
    private sealed class ScopedProject
    {
        public AipProject Project { get; init; } = null!;
        public AipProgram Program { get; init; } = null!;
        public AipOffice  Office  { get; init; } = null!;
    }

    /// <summary>
    /// The projects <paramref name="query"/> allows: the record, the office axis and the division
    /// axis. Shared by the list and the dashboard counts (PPDO-180) so the two can never disagree.
    /// </summary>
    private IQueryable<ScopedProject> ScopedProjects(ProposalProjectQuery query)
    {
        IReadOnlyList<int>    narrowed = query.NarrowedAipOfficeIds;
        IReadOnlyList<string> allowed  = query.AllowedProgramRefCodes;

        IQueryable<ScopedProject> rows =
            from j in _context.AipProjects
            join p in _context.AipPrograms on j.ProgramId equals p.Id
            join o in _context.AipOffices on p.OfficeId equals o.Id
            where o.AipRecordId == query.AipRecordId
            select new ScopedProject { Project = j, Program = p, Office = o };

        if (query.AipOfficeIds is { } officeIds)
            rows = rows.Where(x => officeIds.Contains(x.Office.Id));

        // The division axis (AipReadScope.FilterPrograms): only the narrowed offices' programs are
        // filtered, and those by ref code.
        if (narrowed.Count > 0)
            rows = rows.Where(x => !narrowed.Contains(x.Office.Id) || allowed.Contains(x.Program.RefCode));

        return rows;
    }

    public async Task<ProposalProjectPage> ListProjectsAsync(ProposalProjectQuery query, CancellationToken ct = default)
    {
        IQueryable<ScopedProject> rows = ScopedProjects(query);

        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            string s = query.Search.Trim();
            rows = rows.Where(x => x.Project.Name.Contains(s) || x.Project.RefCode.Contains(s) || x.Program.Name.Contains(s));
        }

        int total = await rows.CountAsync(ct);

        var ordered = rows
            .OrderBy(x => x.Office.RefCode).ThenBy(x => x.Program.RefCode).ThenBy(x => x.Project.RefCode).ThenBy(x => x.Project.Id)
            .Skip(query.Skip);
        if (query.Take is int take) ordered = ordered.Take(take);

        List<ProposalProjectRow> items = await ordered
            .Select(x => new ProposalProjectRow(
                x.Project.Id, x.Project.RefCode, x.Project.Name,
                x.Program.Id, x.Program.RefCode, x.Program.Name,
                x.Office.Id, x.Office.Name,
                _context.AipActivities.Where(a => a.ProjectId == x.Project.Id).Sum(a => a.Total) ?? 0m,
                _context.InvestmentProposals.Where(ip => ip.AipProjectId == x.Project.Id).Select(ip => (int?)ip.Id).FirstOrDefault(),
                _context.InvestmentProposals.Where(ip => ip.AipProjectId == x.Project.Id).Select(ip => ip.Status).FirstOrDefault(),
                _context.InvestmentProposals.Where(ip => ip.AipProjectId == x.Project.Id).Select(ip => (DateTime?)ip.UpdatedAt).FirstOrDefault(),
                _context.InvestmentProposals.Where(ip => ip.AipProjectId == x.Project.Id)
                    .Select(ip => ip.UpdatedBy == null ? null : ip.UpdatedBy.FullName).FirstOrDefault()))
            .ToListAsync(ct);

        return new ProposalProjectPage(items, total);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<ProposalStatusCount>> CountByOfficeAndStatusAsync(
        ProposalProjectQuery query, CancellationToken ct = default)
    {
        // One GROUP BY over the scoped projects left-joined to their proposal (unique per project).
        var grouped = await (
                from x in ScopedProjects(query)
                join ip in _context.InvestmentProposals on x.Project.Id equals ip.AipProjectId into props
                from ip in props.DefaultIfEmpty()
                group x by new { x.Office.OfficeId, Status = ip == null ? null : ip.Status } into g
                select new { g.Key.OfficeId, g.Key.Status, Count = g.Count() })
            .ToListAsync(ct);
        return grouped.Select(r => new ProposalStatusCount(r.OfficeId, r.Status, r.Count)).ToList();
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<ProposalAttentionRow>> ListNeedingAttentionAsync(
        ProposalProjectQuery query, int take, CancellationToken ct = default)
    {
        // ⚠️ "Final" is InvestmentProposalStatus.Final (Application layer, not referenceable here).
        var rows = await (
                from x in ScopedProjects(query)
                join ip in _context.InvestmentProposals on x.Project.Id equals ip.AipProjectId into props
                from ip in props.DefaultIfEmpty()
                where ip == null || ip.Status != "Final"
                orderby (ip == null ? 0 : 1), x.Office.RefCode, x.Program.RefCode, x.Project.RefCode, x.Project.Id
                select new
                {
                    x.Project.Id, x.Project.RefCode, x.Project.Name,
                    ProposalId = ip == null ? (int?)null : ip.Id,
                    Status     = ip == null ? null : ip.Status,
                })
            .Take(take)
            .ToListAsync(ct);
        return rows.Select(r => new ProposalAttentionRow(r.Id, r.RefCode, r.Name, r.ProposalId, r.Status)).ToList();
    }

    /// <inheritdoc />
    public async Task<bool> ExistsForAnyProjectAsync(IReadOnlyList<int> aipProjectIds, CancellationToken ct = default)
        => aipProjectIds.Count > 0
           && await _context.InvestmentProposals.AnyAsync(p => aipProjectIds.Contains(p.AipProjectId), ct);

    /// <inheritdoc />
    public async Task<IReadOnlyDictionary<string, string>> GetFundNamesByCodesAsync(
        IReadOnlyList<string> codes, CancellationToken ct = default)
    {
        if (codes.Count == 0) return new Dictionary<string, string>();
        // Per-office fund sources (PPDO-109) can repeat a code. The first name wins, which is
        // also what the AIP tree prints.
        var rows = await _context.FundingSources.AsNoTracking()
            .Where(f => codes.Contains(f.Code))
            .OrderBy(f => f.Id)
            .Select(f => new { f.Code, f.Name })
            .ToListAsync(ct);
        return rows.GroupBy(r => r.Code, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First().Name, StringComparer.OrdinalIgnoreCase);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyDictionary<string, string>> GetTypologyNamesByCodesAsync(
        IReadOnlyList<string> codes, CancellationToken ct = default)
    {
        if (codes.Count == 0) return new Dictionary<string, string>();
        var rows = await _context.ClimateChangeTypologies.AsNoTracking()
            .Where(t => codes.Contains(t.Code))
            .Select(t => new { t.Code, t.Name })
            .ToListAsync(ct);
        return rows.GroupBy(r => r.Code, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First().Name, StringComparer.OrdinalIgnoreCase);
    }

    /// <inheritdoc />
    public async Task<InvestmentProposal?> GetByIdAsync(int id, CancellationToken ct = default)
        // Split query: eight collections in one JOIN would multiply every child row by every other.
        // Include depth is 1 throughout.
        => await _context.InvestmentProposals
            .Include(p => p.Beneficiaries)
            .Include(p => p.Benefits)
            .Include(p => p.Logframe)
            .Include(p => p.Groups)
            .Include(p => p.WorkPlanRows)
            .Include(p => p.TeamMembers)
            .Include(p => p.Monitoring)
            .Include(p => p.Risks)
            .AsSplitQuery()
            .FirstOrDefaultAsync(p => p.Id == id, ct);

    /// <inheritdoc />
    public async Task<InvestmentProposal?> GetByProjectIdAsync(int aipProjectId, CancellationToken ct = default)
        => await _context.InvestmentProposals
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.AipProjectId == aipProjectId, ct);

    /// <inheritdoc />
    public async Task<bool> ExistsForProjectAsync(int aipProjectId, CancellationToken ct = default)
        => await _context.InvestmentProposals.AnyAsync(p => p.AipProjectId == aipProjectId, ct);

    /// <inheritdoc />
    public async Task AddAsync(InvestmentProposal proposal, CancellationToken ct = default)
        => await _context.InvestmentProposals.AddAsync(proposal, ct);

    /// <inheritdoc />
    public void Remove(InvestmentProposal proposal) => _context.InvestmentProposals.Remove(proposal);

    /// <inheritdoc />
    public void SetExpectedRowVersion(InvestmentProposal proposal, byte[] expectedRowVersion)
        => _context.Entry(proposal).Property(p => p.RowVersion).OriginalValue = expectedRowVersion;

    /// <inheritdoc />
    public async Task SaveChangesAsync(CancellationToken ct = default)
    {
        try
        {
            await _context.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException ex)
        {
            throw new ConcurrencyConflictException(
                "The investment proposal was changed by someone else since it was loaded.", ex);
        }
        catch (DbUpdateException ex) when (SqlErrors.IsUniqueViolation(ex))
        {
            // Two tabs creating a proposal for the same project at once: the loser lands here and
            // the service answers 409 with the existing proposal's id.
            throw new UniqueConstraintViolationException(
                "A unique constraint rejected this write.", SqlErrors.IndexNameOf(ex), ex);
        }
    }
}
