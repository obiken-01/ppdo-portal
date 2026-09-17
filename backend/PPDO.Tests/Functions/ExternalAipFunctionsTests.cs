using System.Net;
using System.Text.Json;
using Microsoft.Azure.Functions.Worker.Http;
using Moq;
using PPDO.Application.Common;
using PPDO.Application.DTOs.ExternalApi;
using PPDO.Application.Services;
using PPDO.Domain.Entities;
using PPDO.Domain.Interfaces;
using PPDO.Functions.Functions;

namespace PPDO.Tests.Functions;

/// <summary>
/// The external AIP endpoints (v1.8.0 — PPDO-12). The auth/scope/rate-limit rules are
/// <see cref="PPDO.Tests.Infrastructure.ApiKeyCredentialValidatorTests"/>'s and
/// <see cref="PPDO.Tests.Application.PartnerApiScopeTests"/>'s; the read shape is
/// <see cref="PPDO.Tests.Application.ExternalAipReadServiceTests"/>'s. This covers the HTTP wiring:
/// which status each outcome returns, the envelope shape, and which outcomes write a
/// <c>partner_api_requests</c> row.
/// </summary>
public sealed class ExternalAipFunctionsTests
{
    private static readonly PartnerApiKey ActiveKey = new() { Id = 7, AllOffices = true };

    private readonly Mock<IPartnerCredentialValidator> _credentials = new(MockBehavior.Strict);
    private readonly Mock<IPartnerApiRateLimiter> _rateLimiter = new(MockBehavior.Strict);
    private readonly Mock<IPartnerApiRequestLogger> _requestLogger = new();
    private readonly Mock<IExternalAipReadService> _reads = new(MockBehavior.Strict);
    private readonly Mock<IOfficeRepository> _offices = new(MockBehavior.Strict);

    private ExternalAipFunctions Sut => new(
        _credentials.Object, _rateLimiter.Object, _requestLogger.Object, _reads.Object, _offices.Object);

    private FakeHttpRequestData GetWithKey(string query, string? apiKey = "ppdo_test_key")
    {
        FakeHttpRequestData req = FunctionHttp.Get(query, authorizationHeader: null, path: "external/v1/aip");
        if (apiKey is not null) req.Headers.Add("X-Api-Key", apiKey);
        return req;
    }

    private void AllowKeyAndRate()
    {
        _credentials.Setup(c => c.ValidateAsync(It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ActiveKey);
        _rateLimiter.Setup(r => r.Check(ActiveKey.Id)).Returns(new RateLimitCheck(true, 0));
    }

    private void VerifyNeverLogged()
        => _requestLogger.Verify(l => l.LogAsync(
            It.IsAny<PartnerApiKey>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<int?>(),
            It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never);

    // ── Health ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Health_ReturnsOk_WithStatusAndTimestamp_NoKeyNeeded()
    {
        HttpResponseData response = await Sut.Health(
            FunctionHttp.Get("", authorizationHeader: null, path: "external/v1/health"), CancellationToken.None);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using JsonDocument body = JsonDocument.Parse(FunctionHttp.BodyText(response));
        Assert.Equal("ok", body.RootElement.GetProperty("status").GetString());
        Assert.False(string.IsNullOrEmpty(body.RootElement.GetProperty("timestamp").GetString()));
    }

    // ── Auth / rate limit (401 / 429 — never logged) ──────────────────────────

    [Fact]
    public async Task GetAip_MissingKey_ReturnsUnauthorized_AndNeverLogs()
    {
        _credentials.Setup(c => c.ValidateAsync(It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((PartnerApiKey?)null);

        HttpResponseData response = await Sut.GetAip(GetWithKey("fiscalYear=2028", apiKey: null), CancellationToken.None);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        VerifyNeverLogged();
    }

    [Fact]
    public async Task GetAip_RateLimited_ReturnsTooManyRequests_WithRetryAfter_AndNeverLogs()
    {
        _credentials.Setup(c => c.ValidateAsync(It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ActiveKey);
        _rateLimiter.Setup(r => r.Check(ActiveKey.Id)).Returns(new RateLimitCheck(false, 42));

        HttpResponseData response = await Sut.GetAip(GetWithKey("fiscalYear=2028"), CancellationToken.None);

        Assert.Equal(HttpStatusCode.TooManyRequests, response.StatusCode);
        Assert.Equal("42", FunctionHttp.Header(response, "Retry-After"));
        VerifyNeverLogged();
    }

    // ── Bad parameter (400 — logged) ──────────────────────────────────────────

    [Fact]
    public async Task GetAip_MissingFiscalYear_ReturnsBadRequest_AndLogsIt()
    {
        AllowKeyAndRate();

        HttpResponseData response = await Sut.GetAip(GetWithKey(""), CancellationToken.None);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        _requestLogger.Verify(l => l.LogAsync(
            ActiveKey, "aip", null, null, (int)HttpStatusCode.BadRequest, It.IsAny<CancellationToken>()), Times.Once);
    }

    // ── Scope (403 — logged) ───────────────────────────────────────────────────

    [Fact]
    public async Task GetAip_ScopedKeyRequestsWholeYear_ReturnsForbidden_AndLogsIt()
    {
        AllowKeyAndRate();
        PartnerApiKey scopedKey = new() { Id = 8, AllOffices = false, Offices = [] };
        _credentials.Setup(c => c.ValidateAsync(It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(scopedKey);
        _rateLimiter.Setup(r => r.Check(scopedKey.Id)).Returns(new RateLimitCheck(true, 0));

        HttpResponseData response = await Sut.GetAip(GetWithKey("fiscalYear=2028"), CancellationToken.None);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        _requestLogger.Verify(l => l.LogAsync(
            scopedKey, "aip", null, 2028, (int)HttpStatusCode.Forbidden, It.IsAny<CancellationToken>()), Times.Once);
    }

    // ── Success (200 — logged) ─────────────────────────────────────────────────

    [Fact]
    public async Task GetAip_Valid_ReturnsOkEnvelope_AndLogsIt()
    {
        AllowKeyAndRate();
        ExternalAipDto dto = new(
            "1.1.0", "2026-09-15T00:00:00+08:00", 2028, "Fy2028", "PHP", null,
            new ExternalMoneyAmountsDto("0.00", "0.00", "0.00", "0.00"), null, [], [], []);
        _reads.Setup(r => r.GetAsync(2028, null, It.IsAny<CancellationToken>())).ReturnsAsync(dto);

        HttpResponseData response = await Sut.GetAip(GetWithKey("fiscalYear=2028"), CancellationToken.None);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using JsonDocument body = JsonDocument.Parse(FunctionHttp.BodyText(response));
        Assert.Equal(2028, body.RootElement.GetProperty("data").GetProperty("fiscalYear").GetInt32());
        Assert.Equal(JsonValueKind.Null, body.RootElement.GetProperty("error").ValueKind);
        _requestLogger.Verify(l => l.LogAsync(
            ActiveKey, "aip", null, 2028, (int)HttpStatusCode.OK, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task GetAip_YearWithNoAip_ReturnsOkEnvelope_WithNullData()
    {
        AllowKeyAndRate();
        _reads.Setup(r => r.GetAsync(2030, null, It.IsAny<CancellationToken>())).ReturnsAsync((ExternalAipDto?)null);

        HttpResponseData response = await Sut.GetAip(GetWithKey("fiscalYear=2030"), CancellationToken.None);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using JsonDocument body = JsonDocument.Parse(FunctionHttp.BodyText(response));
        Assert.Equal(JsonValueKind.Null, body.RootElement.GetProperty("data").ValueKind);
    }

    [Fact]
    public async Task GetAip_OfficeCodeGiven_ResolvesOfficeBeforeScopeCheck()
    {
        AllowKeyAndRate();
        Office office = new() { Id = 5, OfficeCode = "PPDO", OfficeName = "PPDO Office" };
        _offices.Setup(o => o.GetByCodeAsync("PPDO", It.IsAny<CancellationToken>())).ReturnsAsync(office);
        ExternalAipDto dto = new(
            "1.1.0", "2026-09-15T00:00:00+08:00", 2028, "Fy2028", "PHP", "PPDO",
            new ExternalMoneyAmountsDto("0.00", "0.00", "0.00", "0.00"), null, [], [], []);
        _reads.Setup(r => r.GetAsync(2028, office, It.IsAny<CancellationToken>())).ReturnsAsync(dto);

        HttpResponseData response = await Sut.GetAip(GetWithKey("fiscalYear=2028&officeCode=PPDO"), CancellationToken.None);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        _offices.Verify(o => o.GetByCodeAsync("PPDO", It.IsAny<CancellationToken>()), Times.Once);
    }

    // ── /aip/fiscal-years ─────────────────────────────────────────────────────

    private FakeHttpRequestData GetFiscalYearsRequest(string query = "", string? apiKey = "ppdo_test_key")
    {
        FakeHttpRequestData req = FunctionHttp.Get(query, authorizationHeader: null, path: "external/v1/aip/fiscal-years");
        if (apiKey is not null) req.Headers.Add("X-Api-Key", apiKey);
        return req;
    }

    [Fact]
    public async Task GetFiscalYears_Valid_ReturnsOkEnvelope_WithYearsArray()
    {
        AllowKeyAndRate();
        _reads.Setup(r => r.GetFiscalYearsAsync(null, It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyList<int>)[2028, 2027]);

        HttpResponseData response = await Sut.GetFiscalYears(GetFiscalYearsRequest(), CancellationToken.None);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using JsonDocument body = JsonDocument.Parse(FunctionHttp.BodyText(response));
        int[] years = body.RootElement.GetProperty("data").EnumerateArray().Select(e => e.GetInt32()).ToArray();
        Assert.Equal([2028, 2027], years);
    }

    [Fact]
    public async Task GetFiscalYears_ScopedKeyWithoutOfficeCode_ReturnsForbidden()
    {
        PartnerApiKey scopedKey = new() { Id = 9, AllOffices = false, Offices = [] };
        _credentials.Setup(c => c.ValidateAsync(It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(scopedKey);
        _rateLimiter.Setup(r => r.Check(scopedKey.Id)).Returns(new RateLimitCheck(true, 0));

        HttpResponseData response = await Sut.GetFiscalYears(GetFiscalYearsRequest(), CancellationToken.None);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }
}
