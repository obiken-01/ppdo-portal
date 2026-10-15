using Azure;
using Azure.Storage.Blobs;
using PPDO.Application.Services;

namespace PPDO.Infrastructure.Services;

/// <summary>
/// <see cref="IFileStore"/> on an Azure Blob Storage container (v1.9.0 — PPDO-228). A key is the blob's
/// name, so <c>a/b/c.zip</c> shows as folders in the portal's storage browser.
///
/// <para>
/// ⚠️ <b>The only class that knows about Azure Storage.</b> Everything else sees <see cref="IFileStore"/>;
/// a move off Azure swaps this for <see cref="LocalFileStore"/> or another store and changes nothing else.
/// Blobs are private: files are streamed to the user through our API, never linked to directly.
/// </para>
/// </summary>
public sealed class BlobFileStore : IFileStore
{
    private readonly BlobContainerClient _container;
    private bool _containerChecked;

    /// <param name="connectionString">The storage account, <c>AzureWebJobsStorage</c> (spec §8).</param>
    /// <param name="containerName"><c>FileStore__BlobContainer</c>.</param>
    public BlobFileStore(string connectionString, string containerName)
        => _container = new BlobContainerClient(connectionString, containerName);

    /// <inheritdoc />
    public async Task SaveAsync(string key, Stream content, CancellationToken ct = default)
    {
        FileStoreKeys.Validate(key);

        // The container is created in deployment (spec §8); this covers a fresh account or Azurite.
        // Once per instance: the check is a round trip.
        if (!_containerChecked)
        {
            await _container.CreateIfNotExistsAsync(cancellationToken: ct);
            _containerChecked = true;
        }

        await _container.GetBlobClient(key).UploadAsync(content, overwrite: true, ct);
    }

    /// <inheritdoc />
    public async Task<Stream?> OpenReadAsync(string key, CancellationToken ct = default)
    {
        FileStoreKeys.Validate(key);
        try
        {
            return await _container.GetBlobClient(key).OpenReadAsync(cancellationToken: ct);
        }
        catch (RequestFailedException ex) when (ex.Status == 404)
        {
            // No such blob, or no container yet: either way, no file.
            return null;
        }
    }

    /// <inheritdoc />
    public async Task DeleteAsync(string key, CancellationToken ct = default)
    {
        FileStoreKeys.Validate(key);
        try
        {
            await _container.GetBlobClient(key).DeleteIfExistsAsync(cancellationToken: ct);
        }
        catch (RequestFailedException ex) when (ex.Status == 404)
        {
            // The container itself is missing: nothing to delete.
        }
    }
}
