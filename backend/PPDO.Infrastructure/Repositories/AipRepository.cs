using System.Linq.Expressions;
using System.Reflection;
using Microsoft.EntityFrameworkCore;
using PPDO.Application.Common;
using PPDO.Domain.Entities;
using PPDO.Domain.Enums;
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
                group activity by new { office.Id, office.RefCode, office.OfficeId } into g
                select new AipOfficeRollupDto(
                    g.Key.Id,
                    g.Key.RefCode,
                    g.Key.OfficeId,
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

    // ── AIP Review search (V18-75 / PPDO-76) ──────────────────────────────────

    /// <inheritdoc />
    public async Task<AipReviewSearchPage> SearchReviewNodesAsync(
        AipReviewSearchQuery query, CancellationToken ct = default)
    {
        // ⚠️ Three statements, all at the database, all sequential — one DbContext is not
        // thread-safe (CLAUDE.md). The page, then one facet count per chip group.
        int total = await BuildNodeQuery(query).CountAsync(ct);

        // Ordered by ref code — the order the printed form runs in, and the order a reviewer
        // reconciles against — then by node id so paging is stable across the UNION's three legs.
        var page = await BuildNodeQuery(query)
            .OrderBy(r => r.RefCode).ThenBy(r => r.Level).ThenBy(r => r.NodeId)
            .Skip(query.Skip)
            .Take(query.Take)
            .ToListAsync(ct);

        // ⚠️ Each facet drops its OWN field and keeps the others. Counting with every filter
        // applied would show the selected chip its own total and every sibling zero — which reads
        // as "there is nothing else", the opposite of what a multi-select filter promises.
        Dictionary<string, int> sectorCounts = await BuildNodeQuery(query with { Sectors = [] })
            .GroupBy(r => r.Sector)
            .Select(g => new { g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.Key, x => x.Count, ct);

        Dictionary<string, int> statusCounts = await BuildNodeQuery(query with { WorkflowStatuses = [] })
            .GroupBy(r => r.WorkflowStatus)
            .Select(g => new { g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.Key, x => x.Count, ct);

        // The DTO is built here, over one page of rows, rather than inside the query.
        // ⚠️ EF refuses a set operation whose operands are "client projections", so the three legs
        // of the UNION must project to an anonymous type and the named record is constructed after
        // the rows land. This is NOT in-memory filtering — every WHERE, the COUNT, the ORDER BY and
        // both GROUP BYs ran in SQL; what happens here is the mapping of at most `Take` rows.
        IReadOnlyList<AipReviewNodeRow> items = page
            .Select(r => new AipReviewNodeRow(
                r.Level, r.NodeId, r.RefCode, r.Name,
                r.AipOfficeId, r.AipOfficeName, r.OfficeId, r.Sector, r.WorkflowStatus))
            .ToList();

        return new AipReviewSearchPage(items, total, sectorCounts, statusCounts);
    }

    /// <summary>
    /// The three node levels as one queryable — filtered, but unordered and unpaged.
    ///
    /// <para>
    /// <b>⚠️ Every filter is applied BEFORE the <c>Concat</c>, on the entity sets.</b> Two reasons,
    /// and the second one is not optional. Filtering the office set first narrows the join's
    /// driving table to a handful of rows instead of unioning every node in the record and
    /// discarding most of it — and EF <b>cannot translate a <c>Where</c> whose predicate is built
    /// over a set operation's client projection at all</b>. An implementation that concatenates
    /// first and filters after throws at runtime; <c>AipReviewSearchRepositoryTests</c> is what
    /// catches it, because it runs against a real provider rather than a mock.
    /// </para>
    ///
    /// <para>
    /// ⚠️ <b><c>Concat</c>, not three round trips.</b> It translates to <c>UNION ALL</c>, so the
    /// count, the paging and both facet groupings each run as a single statement across all three
    /// levels — which is what makes "page 3 of the combined results" a meaningful thing to ask for.
    /// </para>
    /// </summary>
    private IQueryable<NodeRow> BuildNodeQuery(AipReviewSearchQuery q)
    {
        IQueryable<AipOffice> offices = _context.Set<AipOffice>()
            .Where(o => o.AipRecordId == q.AipRecordId);

        // ⚠️ An empty list means "no filter on this field", never "match nothing". A caller that
        // means "nothing" does not call — and a clamped guest office passes exactly one id, so the
        // clamp arrives here as an ordinary filter rather than as a special case.
        if (q.OfficeIds.Count > 0)
            offices = offices.Where(o => o.OfficeId != null && q.OfficeIds.Contains(o.OfficeId.Value));

        if (q.Sectors.Count > 0)
            offices = offices.Where(o => q.Sectors.Contains(o.Sector));

        if (q.WorkflowStatuses.Count > 0)
            offices = offices.Where(o => q.WorkflowStatuses.Contains(o.WorkflowStatus));

        IQueryable<AipProgram>  programs   = FilterNodes(_context.Set<AipProgram>(),  p => p.RefCode, p => p.Name, q);
        IQueryable<AipProject>  projects   = FilterNodes(_context.Set<AipProject>(),  j => j.RefCode, j => j.Name, q);
        IQueryable<AipActivity> activities = FilterNodes(_context.Set<AipActivity>(), a => a.RefCode, a => a.Name, q);

        // ⚠️ Anonymous projections, and the three shapes must match member-for-member in order and
        // type or the Concat will not translate.
        var programRows =
            from p in programs
            join o in offices on p.OfficeId equals o.Id
            select new NodeRow
            {
                Level = nameof(AipCommentNodeType.Program), NodeId = p.Id,
                RefCode = p.RefCode, Name = p.Name,
                AipOfficeId = o.Id, AipOfficeName = o.Name, OfficeId = o.OfficeId,
                Sector = o.Sector, WorkflowStatus = o.WorkflowStatus,
            };

        var projectRows =
            from j in projects
            join p in _context.Set<AipProgram>() on j.ProgramId equals p.Id
            join o in offices on p.OfficeId equals o.Id
            select new NodeRow
            {
                Level = nameof(AipCommentNodeType.Project), NodeId = j.Id,
                RefCode = j.RefCode, Name = j.Name,
                AipOfficeId = o.Id, AipOfficeName = o.Name, OfficeId = o.OfficeId,
                Sector = o.Sector, WorkflowStatus = o.WorkflowStatus,
            };

        var activityRows =
            from a in activities
            join j in _context.Set<AipProject>() on a.ProjectId equals j.Id
            join p in _context.Set<AipProgram>() on j.ProgramId equals p.Id
            join o in offices on p.OfficeId equals o.Id
            select new NodeRow
            {
                Level = nameof(AipCommentNodeType.Activity), NodeId = a.Id,
                RefCode = a.RefCode, Name = a.Name,
                AipOfficeId = o.Id, AipOfficeName = o.Name, OfficeId = o.OfficeId,
                Sector = o.Sector, WorkflowStatus = o.WorkflowStatus,
            };

        return programRows.Concat(projectRows).Concat(activityRows);
    }

    /// <summary>
    /// The two node-level text filters, applied to one level's entity set.
    ///
    /// <para>
    /// ⚠️ Applied per level rather than once over the union, because the union cannot carry a
    /// predicate — see <see cref="BuildNodeQuery"/>. The selectors are passed in so the same rule
    /// is written once for three unrelated entity types.
    /// </para>
    /// </summary>
    private static IQueryable<T> FilterNodes<T>(
        IQueryable<T> source,
        Expression<Func<T, string>> refCode,
        Expression<Func<T, string>> name,
        AipReviewSearchQuery q)
    {
        // ⚠️ Substring match, and deliberately NOT split on "or" — a title may legitimately contain
        // the word (spec §4.1). This is the one field where a leading wildcard is unavoidable and
        // accepted: it is free text over a name, not a code.
        if (!string.IsNullOrWhiteSpace(q.Title))
            source = source.Where(Like(name, "%" + q.Title.Trim() + "%"));

        if (q.RefCodePrefixes.Count > 0)
        {
            Expression<Func<T, bool>>? any = null;
            foreach (string prefix in q.RefCodePrefixes)
            {
                Expression<Func<T, bool>> term = Like(refCode, prefix + "%");
                any = any is null ? term : Or(any, term);
            }
            source = source.Where(any!);
        }

        return source;
    }

    /// <summary>
    /// <c>EF.Functions.Like(selector(x), pattern)</c> as a predicate over the selector's own
    /// parameter.
    ///
    /// <para>
    /// <b>⚠️ Why <c>Like</c> and not <c>StartsWith</c> inside a collection <c>Any</c>.</b> The
    /// obvious spelling — <c>prefixes.Any(p =&gt; x.RefCode.StartsWith(p))</c> — leans on EF's
    /// primitive-collection translation, which is provider-specific (<c>OPENJSON</c> on SQL Server,
    /// <c>json_each</c> on SQLite). It would pass the SQLite tests and only meet SQL Server in
    /// production. An OR chain of <c>LIKE</c> translates identically on both.
    /// </para>
    ///
    /// <para>
    /// ⚠️ Ref-code patterns are always <c>'value%'</c> — anchored, so the index on <c>ref_code</c>
    /// is usable. A leading <c>%</c> there would turn every search into a table scan, which is why
    /// the spec forbids it by name.
    /// </para>
    /// </summary>
    private static Expression<Func<T, bool>> Like<T>(Expression<Func<T, string>> selector, string pattern)
    {
        MethodInfo like = typeof(DbFunctionsExtensions).GetMethod(
            nameof(DbFunctionsExtensions.Like),
            [typeof(DbFunctions), typeof(string), typeof(string)])!;

        MethodCallExpression call = Expression.Call(
            like,
            Expression.Constant(EF.Functions),
            selector.Body,
            Expression.Constant(pattern));

        return Expression.Lambda<Func<T, bool>>(call, selector.Parameters[0]);
    }

    /// <summary>ORs two predicates that already share a parameter (both come from <see cref="Like"/>).</summary>
    private static Expression<Func<T, bool>> Or<T>(
        Expression<Func<T, bool>> left, Expression<Func<T, bool>> right)
        => Expression.Lambda<Func<T, bool>>(
            Expression.OrElse(left.Body, new Rebind(right.Parameters[0], left.Parameters[0]).Visit(right.Body)!),
            left.Parameters[0]);

    /// <summary>Swaps one lambda parameter for another so two predicates can share a body.</summary>
    private sealed class Rebind(ParameterExpression from, ParameterExpression to) : ExpressionVisitor
    {
        protected override Expression VisitParameter(ParameterExpression node)
            => node == from ? to : base.VisitParameter(node);
    }

    /// <summary>
    /// The union's row shape.
    ///
    /// ⚠️ A mutable class with an object initialiser, not the public record: EF must be able to
    /// treat each leg of the <c>Concat</c> as a translatable projection, and the named record is
    /// constructed from these rows once they land.
    /// </summary>
    private sealed class NodeRow
    {
        public string  Level          { get; init; } = string.Empty;
        public int     NodeId         { get; init; }
        public string  RefCode        { get; init; } = string.Empty;
        public string  Name           { get; init; } = string.Empty;
        public int     AipOfficeId    { get; init; }
        public string  AipOfficeName  { get; init; } = string.Empty;
        public int?    OfficeId       { get; init; }
        public string  Sector         { get; init; } = string.Empty;
        public string  WorkflowStatus { get; init; } = string.Empty;
    }
}
