using Microsoft.Extensions.Logging;

namespace PPDO.Application.Services;

/// <summary>
/// The database half of GET /api/health (PPDO-142).
///
/// The endpoint is anonymous and about to be named to other systems as an availability probe, so what
/// it says in an outage matters: a SqlClient message can carry the server name, the database name, the
/// login or network detail. That text used to be returned to the caller as <c>error</c> — and, worse,
/// never logged anywhere, because the old handler wrapped the query in
/// <c>.ContinueWith(t =&gt; !t.IsFaulted)</c>, which turned a faulted query (the usual DB-down case)
/// into <c>false</c> without throwing, so its <c>catch</c> never ran. The probe is awaited directly
/// here, inside the try, so a failure is actually caught — and goes to Application Insights, not to the
/// internet.
/// </summary>
public sealed class HealthService : IHealthService
{
    private readonly IDatabaseProbe _probe;
    private readonly ILogger<HealthService> _logger;

    public HealthService(IDatabaseProbe probe, ILogger<HealthService> logger)
    {
        _probe  = probe;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<bool> CheckDatabaseAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            await _probe.CanConnectAsync(cancellationToken);
            return true;
        }
        // The request's own cancellation (the client hung up) is not "the database is down" — let it
        // propagate. A SqlClient command timeout can also surface as an OperationCanceledException
        // while the request's token is still live; that one IS an unreachable database.
        catch (Exception ex) when (!(ex is OperationCanceledException && cancellationToken.IsCancellationRequested))
        {
            // Fixed message, exception attached: the detail is searchable in Application Insights, and the
            // connection string / server name are never interpolated into text by us.
            _logger.LogError(ex, "Health check: database unreachable");
            return false;
        }
    }
}
