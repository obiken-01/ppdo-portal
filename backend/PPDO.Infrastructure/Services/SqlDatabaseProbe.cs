using Microsoft.EntityFrameworkCore;
using PPDO.Application.Services;
using PPDO.Infrastructure.Data;

namespace PPDO.Infrastructure.Services;

/// <summary>
/// <see cref="IDatabaseProbe"/> over the app's own <see cref="AppDbContext"/> (PPDO-142). A bare
/// <c>SELECT 1</c> — no joins, no tables — that also wakes the database after idle. It awaits the
/// command directly and lets any failure throw: swallowing it here would hide the outage from the
/// service that is meant to log it.
/// </summary>
public sealed class SqlDatabaseProbe : IDatabaseProbe
{
    private readonly AppDbContext _db;

    public SqlDatabaseProbe(AppDbContext db) => _db = db;

    /// <inheritdoc />
    public async Task CanConnectAsync(CancellationToken cancellationToken = default)
        => await _db.Database.ExecuteSqlRawAsync("SELECT 1", cancellationToken);
}
