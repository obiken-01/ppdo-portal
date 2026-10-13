namespace PPDO.Application.DTOs.Config;

/// <summary>One row of the Audit Log config page.</summary>
public sealed record AuditLogEntryDto(
    long Id,
    DateTime ChangedAt,
    string TableName,
    string Action,
    int? RecordId,
    Guid? RecordGuid,
    string ActorName,
    string Description,
    // PPDO-110 — for an AIP program, project or activity: its ref code and name, shown instead of
    // #RecordId. Null for every other table, or when neither the row nor the audit snapshot has
    // one; the page then falls back to #RecordId, as before.
    string? RecordCode = null,
    string? RecordName = null
);

/// <summary>A page of Audit Log entries plus the total count for pagination controls.</summary>
public sealed record AuditLogPageDto(
    IReadOnlyList<AuditLogEntryDto> Items,
    int TotalCount,
    int Page,
    int PageSize
);

/// <summary>Filter + paging parameters for <c>GET /api/config/audit-log</c>.</summary>
public sealed record AuditLogFilterDto(
    int Page,
    int PageSize,
    string? TableName,
    string? Action,
    string? ActorSearch,
    DateTime? From,
    DateTime? To
);
