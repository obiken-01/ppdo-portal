namespace PPDO.Infrastructure.Services;

/// <summary>
/// The key rule both <c>IFileStore</c> implementations enforce (v1.9.0 — PPDO-228): parts separated by
/// <c>/</c>, no leading <c>/</c>, no backslash, no empty, <c>.</c> or <c>..</c> part. Keys are built by
/// our own code, so a bad one is a bug; refusing it keeps <see cref="LocalFileStore"/> inside its folder.
/// </summary>
internal static class FileStoreKeys
{
    internal static string[] Validate(string key)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);

        string[] parts = key.Split('/');
        if (key.Contains('\\') || parts.Any(p => p.Length == 0 || p is "." or ".."))
            throw new ArgumentException($"Invalid file store key: '{key}'.", nameof(key));
        return parts;
    }
}
