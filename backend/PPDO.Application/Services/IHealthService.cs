namespace PPDO.Application.Services;

/// <summary>
/// Backs the public GET /api/health (PPDO-142). Answers one question — is the database reachable —
/// and keeps the reason to itself: the exception is logged, never returned.
/// </summary>
public interface IHealthService
{
    /// <summary>
    /// True when the database answers. False when it does not; the failure is logged at Error with
    /// the exception. The caller's own cancellation is not a failure and propagates.
    /// </summary>
    Task<bool> CheckDatabaseAsync(CancellationToken cancellationToken = default);
}
