using System.Net;
using System.Text;
using Microsoft.Azure.Functions.Worker.Http;
using Moq;
using PPDO.Application.Common;
using PPDO.Application.DTOs.BudgetPlanning;
using PPDO.Application.Services;
using PPDO.Domain.Entities;
using PPDO.Domain.Interfaces;
using PPDO.Functions.Functions;

namespace PPDO.Tests.Functions;

/// <summary>
/// The bulk export endpoints' HTTP shape (v1.9.0 — PPDO-228, spec §4): 401/403 before the service, the
/// 202-new / 200-existing split, 204 for a year with no job, 410 for an expired download, and the ZIP
/// response itself. The rules behind them are in <c>InitialWfpExportServiceTests</c>.
/// </summary>
public sealed class WfpBulkExportFunctionsTests
{
    private const string Route = "budget-planning/wfp/report/bulk-exports";

    private readonly Mock<IInitialWfpExportService> _exports = new();
    private readonly Mock<IJwtValidator> _jwt = new();
    private readonly Mock<IPermissionService> _permissions = new();
    private readonly User _caller = new() { Id = Guid.NewGuid(), FullName = "Jose Santos" };

    private WfpBulkExportFunctions Sut => new(_exports.Object, _jwt.Object, _permissions.Object);

    private void Caller(bool ppdoReportReader)
    {
        _jwt.Setup(j => j.ValidateAsync(It.IsAny<string?>(), It.IsAny<CancellationToken>())).ReturnsAsync(_caller);
        _permissions.Setup(p => p.CanAccessWfpAsync(_caller, It.IsAny<CancellationToken>())).ReturnsAsync(ppdoReportReader);
    }

    private static InitialWfpExportJobDto JobDto(int id = 5, string status = "Queued")
        => new(id, 2028, status, 0, 0, [], null, "Jose Santos", DateTime.UtcNow, null, null);

    [Fact]
    public async Task EveryEndpoint_NoToken_Is401_AndNotAReaderIs403_BeforeTheService()
    {
        async Task<HttpStatusCode[]> StatusesAsync() =>
        [
            (await Sut.Request(FunctionHttp.Post(Route, new { fiscalYear = 2028 }), default)).StatusCode,
            (await Sut.GetLatest(FunctionHttp.Get("fiscalYear=2028", path: Route), default)).StatusCode,
            (await Sut.Get(FunctionHttp.Get("", path: $"{Route}/5"), 5, default)).StatusCode,
            (await Sut.Download(FunctionHttp.Get("", path: $"{Route}/5/download"), 5, default)).StatusCode,
        ];

        Assert.All(await StatusesAsync(), s => Assert.Equal(HttpStatusCode.Unauthorized, s));   // JWT not set up: null user

        Caller(ppdoReportReader: false);
        Assert.All(await StatusesAsync(), s => Assert.Equal(HttpStatusCode.Forbidden, s));

        _exports.VerifyNoOtherCalls();
    }

    [Theory]
    [InlineData(true, HttpStatusCode.Accepted)]
    [InlineData(false, HttpStatusCode.OK)]
    public async Task Request_NewJobIs202_TheYearsActiveJobIs200(bool created, HttpStatusCode expected)
    {
        Caller(ppdoReportReader: true);
        _exports.Setup(e => e.RequestAsync(2028, _caller, It.IsAny<CancellationToken>()))
            .ReturnsAsync(ServiceResult<InitialWfpExportRequestResult>.Ok(new(JobDto(), created)));

        HttpResponseData response = await Sut.Request(FunctionHttp.Post(Route, new { fiscalYear = 2028 }), default);

        Assert.Equal(expected, response.StatusCode);
        Assert.Contains("\"status\":\"Queued\"", FunctionHttp.BodyText(response));
    }

    [Fact]
    public async Task Request_NoneEligible_Is409_WithTheMessage()
    {
        Caller(ppdoReportReader: true);
        _exports.Setup(e => e.RequestAsync(2028, _caller, It.IsAny<CancellationToken>()))
            .ReturnsAsync(ServiceResult<InitialWfpExportRequestResult>.Conflict("No office's FY 2028 AIP is marked as done yet."));

        HttpResponseData response = await Sut.Request(FunctionHttp.Post(Route, new { fiscalYear = 2028 }), default);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Contains("No office", FunctionHttp.BodyText(response));
    }

    [Fact]
    public async Task Request_MalformedBody_Is400()
    {
        Caller(ppdoReportReader: true);

        HttpResponseData response = await Sut.Request(FunctionHttp.Post(Route, "{not json"), default);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        _exports.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task GetLatest_YearWithNoJob_Is204_AndMissingYearIs400()
    {
        Caller(ppdoReportReader: true);
        _exports.Setup(e => e.GetLatestAsync(2028, _caller, It.IsAny<CancellationToken>()))
            .ReturnsAsync(ServiceResult<InitialWfpExportJobDto?>.Ok(null));

        Assert.Equal(HttpStatusCode.NoContent,
            (await Sut.GetLatest(FunctionHttp.Get("fiscalYear=2028", path: Route), default)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest,
            (await Sut.GetLatest(FunctionHttp.Get("", path: Route), default)).StatusCode);
    }

    [Fact]
    public async Task Download_Expired_Is410()
    {
        Caller(ppdoReportReader: true);
        _exports.Setup(e => e.OpenDownloadAsync(5, _caller, It.IsAny<CancellationToken>()))
            .ReturnsAsync(ServiceResult<InitialWfpExportDownload>.Gone("This export has expired. Run it again."));

        HttpResponseData response = await Sut.Download(FunctionHttp.Get("", path: $"{Route}/5/download"), 5, default);

        Assert.Equal(HttpStatusCode.Gone, response.StatusCode);
        Assert.Contains("expired", FunctionHttp.BodyText(response));
    }

    [Fact]
    public async Task Download_Done_StreamsTheZip_AsAnAttachment()
    {
        Caller(ppdoReportReader: true);
        _exports.Setup(e => e.OpenDownloadAsync(5, _caller, It.IsAny<CancellationToken>()))
            .ReturnsAsync(ServiceResult<InitialWfpExportDownload>.Ok(
                new(new MemoryStream(Encoding.ASCII.GetBytes("PK\u0003\u0004")), "Initial WFP FY2028 - 2026-10-15 1430.zip")));

        HttpResponseData response = await Sut.Download(FunctionHttp.Get("", path: $"{Route}/5/download"), 5, default);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/zip", FunctionHttp.Header(response, "Content-Type"));
        Assert.Equal("attachment; filename=\"Initial WFP FY2028 - 2026-10-15 1430.zip\"",
            FunctionHttp.Header(response, "Content-Disposition"));
        Assert.Equal(Encoding.ASCII.GetBytes("PK\u0003\u0004"), FunctionHttp.BodyBytes(response));
    }
}
