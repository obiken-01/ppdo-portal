namespace PPDO.Application.Settings;

/// <summary>
/// Where generated files (the bulk export ZIPs) are kept (v1.9.0 — PPDO-228, spec §5 and §8). Bound
/// from the "FileStore" section: <c>FileStore__Provider</c>, <c>FileStore__BlobContainer</c>,
/// <c>FileStore__LocalPath</c>.
/// </summary>
public sealed class FileStoreSettings
{
    public const string SectionName = "FileStore";

    public const string LocalProvider = "Local";
    public const string BlobProvider  = "Blob";

    /// <summary>
    /// <c>Local</c> (a folder on disk: local dev, or any non-Azure host) or <c>Blob</c> (Azure Blob
    /// Storage). Defaults to <c>Local</c>, so a dev machine needs no setting at all.
    /// </summary>
    public string Provider { get; init; } = LocalProvider;

    /// <summary>The Blob container, when <see cref="Provider"/> is <c>Blob</c>. It lives in the
    /// <c>AzureWebJobsStorage</c> account.</summary>
    public string BlobContainer { get; init; } = string.Empty;

    /// <summary>
    /// The folder, when <see cref="Provider"/> is <c>Local</c>. A relative path is taken from the host's
    /// working directory. Empty (the default) means <see cref="DefaultLocalPath"/>.
    ///
    /// <para>
    /// ⚠️ <b>Never a folder inside the Functions host's own folder</b> (<c>bin/output</c> under
    /// <c>func start</c>, <c>bin/Debug/net9.0</c> under Visual Studio). The host watches that folder and
    /// restarts on any file created there, so every export would restart the local host. The example
    /// settings use <c>../../../.file-store</c>, which is <c>backend/.file-store</c> from <c>bin/output</c>
    /// and is git-ignored.
    /// </para>
    /// </summary>
    public string LocalPath { get; init; } = string.Empty;

    /// <summary>Where Local keeps files when no path is set: under the OS temp folder, outside every watched folder.</summary>
    public static string DefaultLocalPath => Path.Combine(Path.GetTempPath(), "ppdo-portal", "file-store");
}
