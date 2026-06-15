using System.Text.Json;
using PPDO.Application.Common;
using PPDO.Domain.Entities;
using PPDO.Domain.Interfaces;

namespace PPDO.Application.Services;

/// <summary>
/// Persists a single audit_log row for every CREATE / UPDATE / soft-DELETE in
/// budget planning and config services (RAL-77).
/// Caller provides anonymous-object snapshots; this service serialises them to JSON
/// and stamps the current authenticated user + UTC timestamp.
/// User identity is read from <see cref="CallerContext"/>, which is set by
/// JwtMiddleware after successful token validation — reliably available even when
/// IHttpContextAccessor.HttpContext is null in the Azure Functions isolated worker.
/// </summary>
public sealed class AuditService : IAuditService
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    private readonly IRepository<AuditLog> _repo;
    private readonly CallerContext _caller;

    public AuditService(IRepository<AuditLog> repo, CallerContext caller)
    {
        _repo   = repo;
        _caller = caller;
    }

    /// <inheritdoc />
    public async Task LogAsync(
        string tableName,
        int recordId,
        string action,
        object? oldValues,
        object? newValues,
        CancellationToken cancellationToken = default)
    {
        Guid userId = _caller.UserId
            ?? throw new InvalidOperationException("Audit requires an authenticated user.");

        AuditLog entry = new()
        {
            TableName   = tableName,
            RecordId    = recordId,
            Action      = action,
            ChangedById = userId,
            ChangedAt   = DateTime.UtcNow,
            OldValues   = oldValues is null ? null : JsonSerializer.Serialize(oldValues, JsonOpts),
            NewValues   = newValues is null ? null : JsonSerializer.Serialize(newValues, JsonOpts),
        };

        await _repo.AddAsync(entry, cancellationToken);
        await _repo.SaveChangesAsync(cancellationToken);
    }
}
