using Microsoft.Azure.Functions.Worker;
using PPDO.Application.Services;

namespace PPDO.Functions.Functions;

/// <summary>
/// Runs the export worker every 30 seconds (v1.9.0 — PPDO-228, spec decision 2). This is the
/// <b>host</b> half of the job design and holds no logic: all of it is in <see cref="IExportJobWorker"/>,
/// which knows nothing about Azure. After a move off Functions, a .NET <c>BackgroundService</c> with a
/// 30-second loop calling the same <c>RunOnceAsync</c> replaces this class.
///
/// <para>
/// ⚠️ <b>Locally the timer needs Azurite</b> (<c>AzureWebJobsStorage=UseDevelopmentStorage=true</c>):
/// a Functions timer keeps its lock in storage. Without Azurite running, the HTTP endpoints still work
/// and jobs simply stay Queued.
/// </para>
/// </summary>
public sealed class ExportJobTimerFunctions
{
    /// <summary>Every 30 seconds, as a six-field NCRONTAB (seconds first).</summary>
    private const string Every30Seconds = "*/30 * * * * *";

    private readonly IExportJobWorker _worker;

    public ExportJobTimerFunctions(IExportJobWorker worker) => _worker = worker;

    // UseMonitor = false: the schedule monitor is for catching up on missed runs of long schedules.
    // A missed 30-second pass needs no catching up; the next one does the same work.
    [Function("ExportJobTimer")]
    public Task Run([TimerTrigger(Every30Seconds, UseMonitor = false)] TimerInfo timer, CancellationToken ct)
        => _worker.RunOnceAsync(ct);
}
