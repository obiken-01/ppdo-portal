using Microsoft.Extensions.Options;
using PPDO.Application.Services;
using PPDO.Application.Settings;

namespace PPDO.Infrastructure.Services;

/// <summary>
/// <see cref="IFileStore"/> on a folder (v1.9.0 — PPDO-228): local dev, and any host that is not Azure.
/// A key <c>a/b/c.zip</c> is the file <c>{LocalPath}/a/b/c.zip</c>.
/// </summary>
public sealed class LocalFileStore : IFileStore
{
    private readonly string _root;

    public LocalFileStore(IOptions<FileStoreSettings> settings)
        : this(string.IsNullOrWhiteSpace(settings.Value.LocalPath)
            ? FileStoreSettings.DefaultLocalPath
            : settings.Value.LocalPath) { }

    /// <summary>For tests: a store on <paramref name="rootPath"/>.</summary>
    public LocalFileStore(string rootPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rootPath);
        _root = Path.GetFullPath(rootPath);
    }

    /// <inheritdoc />
    /// <remarks>
    /// Written to a temporary file beside the target and then moved over it, so a reader never sees a
    /// half-written file and a failed write leaves the old one (or none) in place.
    /// </remarks>
    public async Task SaveAsync(string key, Stream content, CancellationToken ct = default)
    {
        string path = PathOf(key);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);

        string temp = $"{path}.{Guid.NewGuid():N}.tmp";
        try
        {
            await using (FileStream file = new(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None,
                             bufferSize: 81920, useAsync: true))
            {
                await content.CopyToAsync(file, ct);
            }
            File.Move(temp, path, overwrite: true);
        }
        finally
        {
            if (File.Exists(temp)) File.Delete(temp);
        }
    }

    /// <inheritdoc />
    public Task<Stream?> OpenReadAsync(string key, CancellationToken ct = default)
    {
        string path = PathOf(key);
        Stream? stream = File.Exists(path)
            ? new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, bufferSize: 81920, useAsync: true)
            : null;
        return Task.FromResult(stream);
    }

    /// <inheritdoc />
    public Task DeleteAsync(string key, CancellationToken ct = default)
    {
        File.Delete(PathOf(key));   // File.Delete is already a no-op when the file is not there.
        return Task.CompletedTask;
    }

    /// <summary>The key's full path, checked to be inside the store's folder.</summary>
    private string PathOf(string key)
    {
        string path = Path.GetFullPath(Path.Combine([_root, .. FileStoreKeys.Validate(key)]));

        // Belt and braces: Validate already refuses "..", so this should never fire.
        if (!path.StartsWith(_root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException($"File store key escapes the store: '{key}'.", nameof(key));
        return path;
    }
}
