using PPDO.Application.DTOs.Config;

namespace PPDO.Application.Services;

/// <summary>Read-only queries backing the Audit Log config page (SuperAdmin-only).</summary>
public interface IAuditLogService
{
    /// <summary>Returns one filtered, paginated page of audit entries, newest first.</summary>
    Task<AuditLogPageDto> GetPagedAsync(AuditLogFilterDto filter, CancellationToken cancellationToken = default);

    /// <summary>Distinct table names present in audit_log — drives the table filter dropdown.</summary>
    Task<IReadOnlyList<string>> GetTableNamesAsync(CancellationToken cancellationToken = default);
}
