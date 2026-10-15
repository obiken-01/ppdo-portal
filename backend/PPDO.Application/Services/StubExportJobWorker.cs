namespace PPDO.Application.Services;

/// <summary>
/// ⚠️ <b>Placeholder until PPDO-229.</b> Does nothing, so the timer and DI can ship with the plumbing
/// (PPDO-228) while jobs simply stay Queued. PPDO-229 replaces it with the real <c>ExportJobWorker</c>
/// and deletes this class.
/// </summary>
public sealed class StubExportJobWorker : IExportJobWorker
{
    /// <inheritdoc />
    public Task RunOnceAsync(CancellationToken ct = default) => Task.CompletedTask;
}
