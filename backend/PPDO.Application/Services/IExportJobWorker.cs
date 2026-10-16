namespace PPDO.Application.Services;

/// <summary>
/// Does one pass over the export job queue (v1.9.0 — PPDO-228/229, spec decision 2): cleanup, recover
/// stuck jobs, claim and run one. Called by whatever hosts it, a Functions timer every 30 s today
/// (<c>ExportJobTimerFunctions</c>), a .NET <c>BackgroundService</c> after a move off Functions.
///
/// <para>
/// ⚠️ It knows nothing about its host: no Azure types here or in any implementation.
/// </para>
/// </summary>
public interface IExportJobWorker
{
    /// <summary>One pass. Returns quickly when there is nothing to do.</summary>
    Task RunOnceAsync(CancellationToken ct = default);
}
