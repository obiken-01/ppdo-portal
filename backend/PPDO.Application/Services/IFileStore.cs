namespace PPDO.Application.Services;

/// <summary>
/// Somewhere to keep generated files, addressed by a key such as
/// <c>initial-wfp-exports/42/Initial WFP FY2028 - 2026-10-15 1430.zip</c> (v1.9.0 — PPDO-228, spec
/// decision 2). <c>BlobFileStore</c> in Azure, <c>LocalFileStore</c> on a dev machine or any other
/// host; nothing outside those two knows which one is in use.
///
/// <para>
/// Keys use <c>/</c> between parts and never start with one or contain <c>..</c>; both stores refuse
/// such keys. Files are only ever read back through our own API, never by a direct link to the store.
/// </para>
/// </summary>
public interface IFileStore
{
    /// <summary>Writes <paramref name="content"/> from its current position to the end, replacing any file at the key.</summary>
    Task SaveAsync(string key, Stream content, CancellationToken ct = default);

    /// <summary>The file for reading, or null when there is none. The caller disposes the stream.</summary>
    Task<Stream?> OpenReadAsync(string key, CancellationToken ct = default);

    /// <summary>Deletes the file. Deleting a file that is not there is not an error.</summary>
    Task DeleteAsync(string key, CancellationToken ct = default);
}
