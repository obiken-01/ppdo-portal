using Moq;
using PPDO.Application.Common;
using PPDO.Domain.Entities;
using PPDO.Domain.Interfaces;
using PPDO.Infrastructure.Services;

namespace PPDO.Tests.Infrastructure;

/// <summary>
/// Unit tests for <see cref="ApiKeyCredentialValidator"/> (v1.8.0 — PPDO-13, build spec §3.1).
/// Every rejection path returns null uniformly — the Function layer maps that to the single
/// <c>401 "Invalid API key."</c> response so a caller can never tell which reason applied.
/// </summary>
public sealed class ApiKeyCredentialValidatorTests
{
    private static (string PlaintextKey, PartnerApiKey Entity) MakeValidKey(
        DateTime? expiresAt = null, DateTime? revokedAt = null)
    {
        ApiKeyGenerator.Generated generated = ApiKeyGenerator.Generate();
        PartnerApiKey entity = new()
        {
            Id          = 1,
            PartnerName = "GSO WFP system",
            KeyPrefix   = generated.Prefix,
            KeyHash     = generated.KeyHash,
            AllOffices  = true,
            ExpiresAt   = expiresAt,
            RevokedAt   = revokedAt,
            CreatedAt   = DateTime.UtcNow,
        };
        return (generated.PlaintextKey, entity);
    }

    private static (ApiKeyCredentialValidator sut, Mock<IPartnerApiKeyRepository> repo) Build(
        PartnerApiKey? byPrefixResult)
    {
        Mock<IPartnerApiKeyRepository> repo = new();
        repo.Setup(r => r.GetByPrefixAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(byPrefixResult);
        return (new ApiKeyCredentialValidator(repo.Object), repo);
    }

    [Fact]
    public async Task ValidateAsync_ValidKey_ReturnsTheKey()
    {
        (string plaintext, PartnerApiKey entity) = MakeValidKey();
        (ApiKeyCredentialValidator sut, _) = Build(entity);

        PartnerApiKey? result = await sut.ValidateAsync(plaintext);

        Assert.NotNull(result);
        Assert.Equal(entity.Id, result!.Id);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task ValidateAsync_MissingHeader_ReturnsNull(string? header)
    {
        (ApiKeyCredentialValidator sut, _) = Build(byPrefixResult: null);

        Assert.Null(await sut.ValidateAsync(header));
    }

    [Theory]
    [InlineData("not-the-right-format")]
    [InlineData("ppdo_tooshort")]
    [InlineData("ppdo_AbCdEfGhMISSINGSEPARATOR")]
    [InlineData("bearer ppdo_AbCdEfGh_somesecret")]
    public async Task ValidateAsync_MalformedHeader_ReturnsNull(string header)
    {
        (ApiKeyCredentialValidator sut, Mock<IPartnerApiKeyRepository> repo) = Build(byPrefixResult: null);

        Assert.Null(await sut.ValidateAsync(header));
        // A malformed header should never even reach the repository.
        repo.Verify(r => r.GetByPrefixAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ValidateAsync_UnknownPrefix_ReturnsNull()
    {
        (ApiKeyCredentialValidator sut, _) = Build(byPrefixResult: null);

        Assert.Null(await sut.ValidateAsync("ppdo_AbCdEfGh_anysecretvalue"));
    }

    [Fact]
    public async Task ValidateAsync_WrongSecretWithARealPrefix_ReturnsNull()
    {
        (_, PartnerApiKey entity) = MakeValidKey();
        (ApiKeyCredentialValidator sut, _) = Build(entity);

        // Same prefix (so the repository "finds" the row), different secret entirely.
        string wrongKey = $"ppdo_{entity.KeyPrefix}_{ApiKeyGenerator.Generate().PlaintextKey}";

        Assert.Null(await sut.ValidateAsync(wrongKey));
    }

    [Fact]
    public async Task ValidateAsync_RevokedKey_ReturnsNull()
    {
        (string plaintext, PartnerApiKey entity) = MakeValidKey(revokedAt: DateTime.UtcNow.AddDays(-1));
        (ApiKeyCredentialValidator sut, _) = Build(entity);

        Assert.Null(await sut.ValidateAsync(plaintext));
    }

    [Fact]
    public async Task ValidateAsync_ExpiredKey_ReturnsNull()
    {
        (string plaintext, PartnerApiKey entity) = MakeValidKey(expiresAt: DateTime.UtcNow.AddMinutes(-1));
        (ApiKeyCredentialValidator sut, _) = Build(entity);

        Assert.Null(await sut.ValidateAsync(plaintext));
    }

    [Fact]
    public async Task ValidateAsync_ValidKey_NeverMutatesTheRepository()
    {
        // Validation has no side effects — LastUsedAt/request logging happen only after rate
        // limiting and scope checks also pass (build spec §3.1).
        (string plaintext, PartnerApiKey entity) = MakeValidKey();
        (ApiKeyCredentialValidator sut, Mock<IPartnerApiKeyRepository> repo) = Build(entity);

        await sut.ValidateAsync(plaintext);

        repo.Verify(r => r.UpdateAsync(It.IsAny<PartnerApiKey>(), It.IsAny<CancellationToken>()), Times.Never);
        repo.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }
}
