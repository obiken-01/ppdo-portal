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
    /// belong to that office are returned. When <paramref name="tableNames"/> is provided
    /// (non-null, non-empty), only entries whose TableName is in that set are returned —
    /// e.g. the Budget Planning Dashboard scopes this to AIP/LDIP/WFP/Allocation tables only,
    /// excluding "users" and Config tables. The navigation property
    /// <see cref="AuditLog.ChangedBy"/> is always populated (via JOIN/Include).
    /// </summary>
    Task<IReadOnlyList<AuditLog>> GetRecentAsync(
        int take,
        int? officeId,
        IReadOnlyList<string>? tableNames = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns one page of audit entries (newest first) matching the given filters, plus the
    /// total matching row count for pagination controls. Filtering and paging are pushed to
    /// SQL — the full audit_log table is never materialised in memory. ActorSearch matches
    /// against the changing user's FullName or Username (case-insensitive contains).
    /// <see cref="AuditLog.ChangedBy"/> is always populated.
    /// </summary>
    Task<(IReadOnlyList<AuditLog> Items, int TotalCount)> GetPagedAsync(
        int page,
        int pageSize,
        string? tableName,
        string? action,
        string? actorSearch,
        DateTime? from,
        DateTime? to,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Every entry for the given int-keyed rows of one table whose action is in
    /// <paramref name="actions"/>, newest first, with <see cref="AuditLog.ChangedBy"/> populated
    /// (v1.8.0 — PPDO-77, the AIP submission history).
    ///
    /// ⚠️ Filters on the named action constants and never parses the JSON payloads — which is why
    /// the AIP hand-offs are logged as their own actions rather than as a generic UPDATE.
    /// An empty id or action list returns nothing rather than every row.
    /// </summary>
    Task<IReadOnlyList<AuditLog>> GetByRecordIdsAsync(
        string tableName,
        IReadOnlyList<int> recordIds,
        IReadOnlyList<string> actions,
        CancellationToken cancellationToken = default);

    /// <summary>Distinct table_name values present in audit_log, alphabetical — drives the
    /// Audit Log page's table filter dropdown without a hardcoded list that could drift.</summary>
    Task<IReadOnlyList<string>> GetDistinctTableNamesAsync(CancellationToken cancellationToken = default);
}
