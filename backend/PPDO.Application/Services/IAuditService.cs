namespace PPDO.Application.Services;

/// <summary>
/// Records a single application-level audit entry to the audit_log table.
/// The caller is responsible for providing pre/post field snapshots as anonymous objects;
/// this service handles serialization and user-identity stamping.
/// </summary>
public interface IAuditService
{
    /// <summary>
    /// Persists one audit row for a create, update, or soft-delete operation.
    /// </summary>
    /// <param name="tableName">Physical table name (e.g. "accounts", "wfp_expenditure_lines").</param>
    /// <param name="recordId">PK of the affected row.</param>
    /// <param name="action">One of <see cref="Common.AuditAction"/> constants: CREATE / UPDATE / DELETE.</param>
    /// <param name="oldValues">Anonymous object with field values before the change. Null for CREATE.</param>
    /// <param name="newValues">Anonymous object with field values after the change. Null for DELETE.</param>
    Task LogAsync(
        string tableName,
        int recordId,
        string action,
        object? oldValues,
        object? newValues,
        CancellationToken cancellationToken = default);
}
