namespace PPDO.Application.Services;

/// <summary>
/// The "is the database reachable?" probe behind GET /api/health (PPDO-142). Implemented in
/// Infrastructure (<c>SqlDatabaseProbe</c>) so the Application layer — where the logging lives —
/// never touches <c>AppDbContext</c>. Mockable for unit tests.
/// </summary>
public interface IDatabaseProbe
{
    /// <summary>
    /// Runs a bare <c>SELECT 1</c>. Completes when the database answers; <b>throws</b> when it does not.
    /// It does not catch: the caller (<see cref="IHealthService"/>) owns the catch, the log and what
    /// is — and is not — said to the outside world.
    /// </summary>
    Task CanConnectAsync(CancellationToken cancellationToken = default);
}
