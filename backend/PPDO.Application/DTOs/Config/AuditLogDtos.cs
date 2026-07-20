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
    string Description
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
