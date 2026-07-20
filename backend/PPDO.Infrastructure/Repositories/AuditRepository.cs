using Microsoft.EntityFrameworkCore;
using PPDO.Domain.Entities;
using PPDO.Domain.Interfaces;
using PPDO.Infrastructure.Data;

namespace PPDO.Infrastructure.Repositories;

/// <summary>
/// EF Core implementation of <see cref="IAuditRepository"/>.
/// <see cref="GetRecentAsync"/> pushes ordering, filtering, and TAKE to SQL so
/// the full audit_log table is never materialised in memory.
/// </summary>
public sealed class AuditRepository : Repository<AuditLog>, IAuditRepository
{
    public AuditRepository(AppDbContext context) : base(context) { }

    /// <inheritdoc />
    public async Task<IReadOnlyList<AuditLog>> GetRecentAsync(
        int take,
        int? officeId,
        IReadOnlyList<string>? tableNames = null,
        CancellationToken cancellationToken = default)
    {
        IQueryable<AuditLog> query = _context.AuditLogs
            .Include(a => a.ChangedBy)
            .OrderByDescending(a => a.ChangedAt);

        if (officeId.HasValue)
        {
            // Compose a subquery so EF Core generates: WHERE changed_by_id IN (SELECT id FROM users WHERE office_id = @p)
            // This avoids a concurrent DbContext issue (no second _context usage mid-query)
            // and lets SQL Server use index seeks on both tables.
            IQueryable<Guid> officeUserIds = _context.Users
                .Where(u => u.OfficeId == officeId.Value)
                .Select(u => u.Id);

            query = query.Where(a => officeUserIds.Contains(a.ChangedById));
        }

        if (tableNames is { Count: > 0 })
            query = query.Where(a => tableNames.Contains(a.TableName));

        return await query.Take(take).ToListAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task<(IReadOnlyList<AuditLog> Items, int TotalCount)> GetPagedAsync(
        int page,
        int pageSize,
        string? tableName,
        string? action,
        string? actorSearch,
        DateTime? from,
        DateTime? to,
        CancellationToken cancellationToken = default)
    {
        IQueryable<AuditLog> query = _context.AuditLogs.Include(a => a.ChangedBy);

        if (!string.IsNullOrWhiteSpace(tableName))
            query = query.Where(a => a.TableName == tableName);
        if (!string.IsNullOrWhiteSpace(action))
            query = query.Where(a => a.Action == action);
        if (!string.IsNullOrWhiteSpace(actorSearch))
        {
            string s = actorSearch.Trim();
            query = query.Where(a =>
                a.ChangedBy != null &&
                (a.ChangedBy.FullName.Contains(s) || a.ChangedBy.Username.Contains(s)));
        }
        if (from.HasValue)
            query = query.Where(a => a.ChangedAt >= from.Value);
        if (to.HasValue)
            query = query.Where(a => a.ChangedAt <= to.Value);

        int total = await query.CountAsync(cancellationToken);
        List<AuditLog> items = await query
            .OrderByDescending(a => a.ChangedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        return (items, total);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<string>> GetDistinctTableNamesAsync(CancellationToken cancellationToken = default)
        => await _context.AuditLogs
            .Select(a => a.TableName)
            .Distinct()
            .OrderBy(t => t)
            .ToListAsync(cancellationToken);
}
