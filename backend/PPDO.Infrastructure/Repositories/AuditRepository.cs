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

        return await query.Take(take).ToListAsync(cancellationToken);
    }
}
