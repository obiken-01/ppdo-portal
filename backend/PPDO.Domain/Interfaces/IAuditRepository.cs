using PPDO.Domain.Entities;

namespace PPDO.Domain.Interfaces;

/// <summary>
/// Repository contract for scoped, server-side audit log queries.
/// Extends the generic repository with a query that applies ordering, filtering,
/// and limiting in SQL — never fetching the entire audit_log table.
/// </summary>
public interface IAuditRepository : IRepository<AuditLog>
{
    /// <summary>
    /// Returns the most recent <paramref name="take"/> audit entries, newest first.
    /// When <paramref name="officeId"/> is provided, only entries made by users who
    /// belong to that office are returned. The navigation property
    /// <see cref="AuditLog.ChangedBy"/> is always populated (via JOIN/Include).
    /// </summary>
    Task<IReadOnlyList<AuditLog>> GetRecentAsync(
        int take,
        int? officeId,
        CancellationToken cancellationToken = default);
}
