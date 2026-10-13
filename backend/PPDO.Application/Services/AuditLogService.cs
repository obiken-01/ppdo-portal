using PPDO.Application.Common;
using PPDO.Application.DTOs.Config;
using PPDO.Domain.Entities;
using PPDO.Domain.Interfaces;

namespace PPDO.Application.Services;

/// <summary>
/// Read-only queries backing the Audit Log config page. All filtering/paging is pushed to
/// SQL via <see cref="IAuditRepository.GetPagedAsync"/> — audit_log can grow unbounded, so
/// this never loads the full table (see docs/PERFORMANCE_GUIDELINES.md).
/// </summary>
public sealed class AuditLogService : IAuditLogService
{
    private const int MaxPageSize = 200;

    private readonly IAuditRepository _audit;
    private readonly AuditRecordCodeResolver _codes;

    public AuditLogService(IAuditRepository audit, AuditRecordCodeResolver codes)
    {
        _audit = audit;
        _codes = codes;
    }

    /// <inheritdoc />
    public async Task<AuditLogPageDto> GetPagedAsync(
        AuditLogFilterDto filter, CancellationToken cancellationToken = default)
    {
        int page = Math.Max(1, filter.Page);
        int pageSize = Math.Clamp(filter.PageSize, 1, MaxPageSize);

        (IReadOnlyList<AuditLog> items, int total) = await _audit.GetPagedAsync(
            page, pageSize, filter.TableName, filter.Action, filter.ActorSearch, filter.From, filter.To,
            cancellationToken);

        // PPDO-110 — ref codes for the AIP rows on this page, in at most three queries.
        IReadOnlyList<AuditRecordCode> codes = await _codes.ResolveAsync(items, cancellationToken);

        List<AuditLogEntryDto> dtos = items
            .Select((a, i) => new AuditLogEntryDto(
                a.Id,
                // EF Core loses DateTimeKind on the SQL Server round-trip (RAL-172) — re-stamp
                // Utc so System.Text.Json emits the "Z" suffix and the browser parses it correctly.
                DateTime.SpecifyKind(a.ChangedAt, DateTimeKind.Utc),
                a.TableName,
                a.Action,
                a.RecordId,
                a.RecordGuid,
                a.ChangedBy?.FullName ?? "Unknown",
                AuditDescriptionBuilder.Build(a.Action, a.OldValues, a.NewValues),
                codes[i].Code,
                codes[i].Name))
            .ToList();

        return new AuditLogPageDto(dtos, total, page, pageSize);
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<string>> GetTableNamesAsync(CancellationToken cancellationToken = default)
        => _audit.GetDistinctTableNamesAsync(cancellationToken);
}
