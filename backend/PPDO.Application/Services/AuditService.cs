using System.Text.Json;
using PPDO.Domain.Entities;
using PPDO.Domain.Interfaces;

namespace PPDO.Application.Services;

/// <summary>
/// Persists a single audit_log row for every CREATE / UPDATE / soft-DELETE in
/// budget planning and config services (RAL-77).
/// Caller provides anonymous-object snapshots; this service serialises them to JSON
/// and stamps the current authenticated user + UTC timestamp.
/// </summary>
public sealed class AuditService : IAuditService
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    private readonly IRepository<AuditLog> _repo;
    private readonly ICurrentUserService _currentUser;

    public AuditService(IRepository<AuditLog> repo, ICurrentUserService currentUser)
    {
        _repo        = repo;
        _currentUser = currentUser;
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
        Guid userId = _currentUser.UserId
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
