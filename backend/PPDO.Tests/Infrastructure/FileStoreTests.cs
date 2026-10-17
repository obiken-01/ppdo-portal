using System.Net.Sockets;
using System.Text;
using PPDO.Application.Services;
using PPDO.Infrastructure.Services;

namespace PPDO.Tests.Infrastructure;

/// <summary>
/// The <see cref="IFileStore"/> contract (v1.9.0 — PPDO-228), run against both stores so they cannot
/// behave differently: save then read back, replace, a missing file reads as null, delete is idempotent,
/// and bad keys are refused.
/// </summary>
public abstract class FileStoreContractTests
{
    protected abstract IFileStore Store { get; }

    /// <summary>Keys unique per run, so a shared store (Azurite) never sees a previous run's files.</summary>
    protected readonly string Prefix = $"tests/{Guid.NewGuid():N}";

    public virtual async Task SaveThenOpen_ReturnsTheSameBytes()
    {
        string key = $"{Prefix}/initial-wfp-exports/42/Initial WFP FY2028 - 2026-10-15 1430.zip";
        await Store.SaveAsync(key, Bytes("PK first"));

        Assert.Equal("PK first", await ReadAsync(key));
    }

    public virtual async Task Save_ToAnExistingKey_ReplacesTheFile()
    {
        string key = $"{Prefix}/a.zip";
        await Store.SaveAsync(key, Bytes("old, and longer than the new one"));
        await Store.SaveAsync(key, Bytes("new"));

        Assert.Equal("new", await ReadAsync(key));
    }

    public virtual async Task OpenRead_MissingKey_ReturnsNull()
        => Assert.Null(await Store.OpenReadAsync($"{Prefix}/nothing-here.zip"));

    public virtual async Task Delete_RemovesTheFile_AndDeletingAgainIsFine()
    {
        string key = $"{Prefix}/b.zip";
        await Store.SaveAsync(key, Bytes("x"));

        await Store.DeleteAsync(key);
        await Store.DeleteAsync(key);

        Assert.Null(await Store.OpenReadAsync(key));
    }

    public virtual async Task BadKeys_AreRefused()
    {
        foreach (string key in new[] { "/rooted.zip", "a/../../escape.zip", "a//b.zip", @"a\b.zip", "./a.zip", " " })
            await Assert.ThrowsAnyAsync<ArgumentException>(() => Store.SaveAsync(key, Bytes("x")));
    }

    private static MemoryStream Bytes(string text) => new(Encoding.UTF8.GetBytes(text));

    private async Task<string?> ReadAsync(string key)
    {
        await using Stream? stream = await Store.OpenReadAsync(key);
        if (stream is null) return null;
        using StreamReader reader = new(stream);
        return await reader.ReadToEndAsync();
    }
}

/// <summary><see cref="LocalFileStore"/> on a temporary folder, deleted afterwards.</summary>
public sealed class LocalFileStoreTests : FileStoreContractTests, IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"ppdo-file-store-{Guid.NewGuid():N}");
    private readonly LocalFileStore _store;

    public LocalFileStoreTests() => _store = new LocalFileStore(_root);

    protected override IFileStore Store => _store;

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }

    [Fact] public override Task SaveThenOpen_ReturnsTheSameBytes() => base.SaveThenOpen_ReturnsTheSameBytes();
    [Fact] public override Task Save_ToAnExistingKey_ReplacesTheFile() => base.Save_ToAnExistingKey_ReplacesTheFile();
    [Fact] public override Task OpenRead_MissingKey_ReturnsNull() => base.OpenRead_MissingKey_ReturnsNull();
    [Fact] public override Task Delete_RemovesTheFile_AndDeletingAgainIsFine() => base.Delete_RemovesTheFile_AndDeletingAgainIsFine();
    [Fact] public override Task BadKeys_AreRefused() => base.BadKeys_AreRefused();

    [Fact]
    public async Task Save_WritesUnderTheRoot_AtTheKeysPath_AndLeavesNoTempFile()
    {
        await _store.SaveAsync($"{Prefix}/c.zip", new MemoryStream([1, 2, 3]));

        string expected = Path.Combine(_root, "tests", Prefix.Split('/')[1], "c.zip");
        Assert.Equal([1, 2, 3], await File.ReadAllBytesAsync(expected));
        Assert.Equal([expected], Directory.GetFiles(Path.GetDirectoryName(expected)!));
    }
}

/// <summary>
/// <see cref="BlobFileStore"/> against Azurite, the local Azure Storage emulator (<c>azurite</c>, or
/// Visual Studio's). Skipped when nothing listens on Azurite's blob port, as on CI.
/// </summary>
public sealed class BlobFileStoreTests : FileStoreContractTests
{
    private readonly BlobFileStore _store = new("UseDevelopmentStorage=true", "ppdo-test-file-store");

    protected override IFileStore Store => _store;

    [AzuriteFact] public override Task SaveThenOpen_ReturnsTheSameBytes() => base.SaveThenOpen_ReturnsTheSameBytes();
    [AzuriteFact] public override Task Save_ToAnExistingKey_ReplacesTheFile() => base.Save_ToAnExistingKey_ReplacesTheFile();
    [AzuriteFact] public override Task OpenRead_MissingKey_ReturnsNull() => base.OpenRead_MissingKey_ReturnsNull();
    [AzuriteFact] public override Task Delete_RemovesTheFile_AndDeletingAgainIsFine() => base.Delete_RemovesTheFile_AndDeletingAgainIsFine();
    [Fact] public override Task BadKeys_AreRefused() => base.BadKeys_AreRefused();   // refused before any call to storage

    [AzuriteFact]
    public async Task OpenRead_ContainerNotCreatedYet_ReturnsNull()
    {
        BlobFileStore fresh = new("UseDevelopmentStorage=true", $"ppdo-test-{Guid.NewGuid():N}");
        Assert.Null(await fresh.OpenReadAsync("a.zip"));
        await fresh.DeleteAsync("a.zip");   // and deleting from it is not an error either
    }
}

/// <summary>A fact that skips itself when Azurite's blob endpoint (127.0.0.1:10000) is not listening.</summary>
public sealed class AzuriteFactAttribute : FactAttribute
{
    private static readonly Lazy<bool> Listening = new(() =>
    {
        try
        {
            using TcpClient client = new();
            return client.ConnectAsync("127.0.0.1", 10000).Wait(TimeSpan.FromMilliseconds(500)) && client.Connected;
        }
        catch (Exception ex) when (ex is SocketException or AggregateException)
        {
            return false;
        }
    });

    public AzuriteFactAttribute()
    {
        if (!Listening.Value) Skip = "Azurite is not running on 127.0.0.1:10000.";
    }
}
