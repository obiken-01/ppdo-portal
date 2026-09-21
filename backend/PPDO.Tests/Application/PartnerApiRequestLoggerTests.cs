using Moq;
using PPDO.Application.Services;
using PPDO.Domain.Entities;
using PPDO.Domain.Interfaces;

namespace PPDO.Tests.Application;

/// <summary>
/// Unit tests for <see cref="PartnerApiRequestLogger"/> (v1.8.0 — PPDO-13, build spec §3.1): one
/// <c>partner_api_requests</c> row and a <c>LastUsedAt</c> stamp per logged call, written together.
/// </summary>
public sealed class PartnerApiRequestLoggerTests
{
    private static (
        PartnerApiRequestLogger sut,
        Mock<IPartnerApiRequestRepository> requests,
        Mock<IPartnerApiKeyRepository> keys) Build()
    {
        Mock<IPartnerApiRequestRepository> requests = new();
        Mock<IPartnerApiKeyRepository> keys = new();

        requests.Setup(r => r.AddAsync(It.IsAny<PartnerApiRequest>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        requests.Setup(r => r.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);
        keys.Setup(k => k.UpdateAsync(It.IsAny<PartnerApiKey>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        return (new PartnerApiRequestLogger(requests.Object, keys.Object), requests, keys);
    }

    [Fact]
    public async Task LogAsync_WritesOneRequestRow_WithTheGivenFields()
    {
        (PartnerApiRequestLogger sut, Mock<IPartnerApiRequestRepository> requests, _) = Build();
        PartnerApiKey key = new() { Id = 5 };

        await sut.LogAsync(key, "aip", "PPDO", 2028, 200);

        requests.Verify(r => r.AddAsync(
            It.Is<PartnerApiRequest>(req =>
                req.KeyId == 5 && req.Route == "aip" && req.OfficeCode == "PPDO" &&
                req.FiscalYear == 2028 && req.StatusCode == 200),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task LogAsync_StampsLastUsedAt_OnTheKey()
    {
        (PartnerApiRequestLogger sut, _, Mock<IPartnerApiKeyRepository> keys) = Build();
        PartnerApiKey key = new() { Id = 5, LastUsedAt = null };

        await sut.LogAsync(key, "aip", null, 2028, 200);

        Assert.NotNull(key.LastUsedAt);
        keys.Verify(k => k.UpdateAsync(key, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task LogAsync_WholeYearCall_LogsNullOfficeCode()
    {
        (PartnerApiRequestLogger sut, Mock<IPartnerApiRequestRepository> requests, _) = Build();
        PartnerApiKey key = new() { Id = 5 };

        await sut.LogAsync(key, "aip", officeCode: null, fiscalYear: 2028, statusCode: 200);

        requests.Verify(r => r.AddAsync(
            It.Is<PartnerApiRequest>(req => req.OfficeCode == null),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task LogAsync_SavesOnceThroughTheRequestRepository()
    {
        // Both writes share one scoped AppDbContext — a single SaveChanges commits them together.
        (PartnerApiRequestLogger sut, Mock<IPartnerApiRequestRepository> requests, _) = Build();
        PartnerApiKey key = new() { Id = 5 };

        await sut.LogAsync(key, "aip", "PPDO", 2028, 403);

        requests.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }
}
