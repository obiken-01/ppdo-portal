using System.Net;
using Microsoft.Azure.Functions.Worker.Http;
using Moq;
using PPDO.Application.Common;
using PPDO.Application.Services;
using PPDO.Domain.Entities;
using PPDO.Domain.Enums;
using PPDO.Domain.Interfaces;
using PPDO.Functions.Functions;

namespace PPDO.Tests.Functions;

/// <summary>
/// PPDO-199 at the HTTP edge: an oversized upload is answered 413 by the handler, and the import
/// service is never reached. One test per read path — the CSV text read
/// (<c>ConfigHttp.ReadTextAsync</c>) and the binary workbook read
/// (<c>ConfigHttp.ReadUploadEnvelopedAsync</c>). The services are strict mocks, so reaching one
/// unconfigured would throw; the under-cap controls prove the 413 is the cap and not the handler.
/// </summary>
public sealed class UploadLimitEndpointTests
{
    private static readonly CsvImportResult Imported = new(1, 0, 0, []);

    private readonly Mock<IJwtValidator>     _jwt         = new(MockBehavior.Strict);
    private readonly Mock<IPermissionService> _permissions = new(MockBehavior.Loose);

    public UploadLimitEndpointTests()
    {
        User admin = new() { Id = Guid.NewGuid(), Role = UserRole.Admin, Username = "a", PasswordHash = "h", FullName = "A" };
        _jwt.Setup(j => j.ValidateAsync(It.IsAny<string?>(), It.IsAny<CancellationToken>())).ReturnsAsync(admin);
        _permissions.Setup(p => p.CanManageConfigAsync(admin, It.IsAny<CancellationToken>())).ReturnsAsync(true);
    }

    private static string OverTheCap => new('a', (int)UploadLimits.MaxUploadBytes + 1);

    [Fact]
    public async Task CsvImport_OverTheCap_Is413_AndNeverReachesTheService()
    {
        Mock<IFundingSourceService> funding = new(MockBehavior.Strict);
        ConfigFundingSourceFunctions sut = new(funding.Object, _jwt.Object, _permissions.Object);

        HttpResponseData response = await sut.Import(
            FunctionHttp.Post("config/funding-sources/import", OverTheCap), CancellationToken.None);

        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, response.StatusCode);
        Assert.Contains("larger than 20 MB", FunctionHttp.BodyText(response));
        funding.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task CsvImport_UnderTheCap_ReachesTheService()
    {
        Mock<IFundingSourceService> funding = new(MockBehavior.Strict);
        funding.Setup(f => f.ImportCsvAsync("code,name", It.IsAny<CancellationToken>()))
               .ReturnsAsync(ServiceResult<CsvImportResult>.Ok(Imported));
        ConfigFundingSourceFunctions sut = new(funding.Object, _jwt.Object, _permissions.Object);

        HttpResponseData response = await sut.Import(
            FunctionHttp.Post("config/funding-sources/import", "code,name"), CancellationToken.None);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task WorkbookImport_OverTheCap_Is413_AndNeverReachesTheService()
    {
        Mock<IPriceIndexService> priceIndex = new(MockBehavior.Strict);
        ConfigPriceIndexFunctions sut = new(priceIndex.Object, _jwt.Object, _permissions.Object);

        HttpResponseData response = await sut.ImportPgom(
            FunctionHttp.Post("config/price-index/pgom", OverTheCap), CancellationToken.None);

        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, response.StatusCode);
        priceIndex.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task WorkbookImport_UnderTheCap_ReachesTheService_WithTheWholeFileAtPositionZero()
    {
        byte[] seen = [];
        Mock<IPriceIndexService> priceIndex = new(MockBehavior.Strict);
        priceIndex.Setup(p => p.ImportPgomAsync(It.IsAny<Stream>(), It.IsAny<CancellationToken>()))
                  .Callback<Stream, CancellationToken>((s, _) => { using MemoryStream m = new(); s.CopyTo(m); seen = m.ToArray(); })
                  .ReturnsAsync(ServiceResult<CsvImportResult>.Ok(Imported));
        ConfigPriceIndexFunctions sut = new(priceIndex.Object, _jwt.Object, _permissions.Object);

        HttpResponseData response = await sut.ImportPgom(
            FunctionHttp.Post("config/price-index/pgom", "not really a workbook"), CancellationToken.None);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("not really a workbook", System.Text.Encoding.UTF8.GetString(seen));
    }
}
