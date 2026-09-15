using System.Net;
using System.Text.Json;
using Microsoft.Azure.Functions.Worker.Http;
using Moq;
using PPDO.Application.Common;
using PPDO.Application.DTOs.Config;
using PPDO.Application.Services;
using PPDO.Domain.Entities;
using PPDO.Domain.Enums;
using PPDO.Domain.Interfaces;
using PPDO.Functions.Functions;

namespace PPDO.Tests.Functions;

/// <summary>
/// Endpoint tests for Configuration → API Access (v1.8.0 — PPDO-86, build spec §4.2). Validation,
/// scope, and status-computation shape live in <see cref="PPDO.Tests.Application.PartnerApiKeyServiceTests"/>
/// — this covers the HTTP wiring: which status each outcome returns, and that every route
/// (list included) is gated on <c>CanManageApiKeys</c> (build spec §3.3).
/// </summary>
public sealed class ConfigApiKeysFunctionsTests
{
    private static readonly User Caller = new()
    {
        Id = Guid.NewGuid(), FullName = "Admin", Username = "admin", PasswordHash = "hash", Role = UserRole.SuperAdmin,
    };

    private readonly Mock<IPartnerApiKeyService> _keys = new(MockBehavior.Strict);
    private readonly Mock<IJwtMiddleware> _jwt = new(MockBehavior.Strict);
    private readonly Mock<IPermissionService> _permissions = new(MockBehavior.Loose);

    private ConfigApiKeysFunctions Sut => new(_keys.Object, _jwt.Object, _permissions.Object);

    private void Authenticate(bool canManageApiKeys = true)
    {
        _jwt.Setup(j => j.ValidateAsync(It.IsAny<string?>(), It.IsAny<CancellationToken>())).ReturnsAsync(Caller);
        _permissions.Setup(p => p.CanManageApiKeysAsync(Caller, It.IsAny<CancellationToken>())).ReturnsAsync(canManageApiKeys);
    }

    private void Unauthenticated()
        => _jwt.Setup(j => j.ValidateAsync(It.IsAny<string?>(), It.IsAny<CancellationToken>())).ReturnsAsync((User?)null);

    private static ApiKeyListItemDto Key(int id = 1, string status = "Active") => new(
        id, "GSO WFP system", "AbCdEfGh", true, [], status, null, null, DateTime.UtcNow, "Admin User", null, null);

    // ── List ──────────────────────────────────────────────────────────────────

    [Fact]
    public async Task List_NoToken_ReturnsUnauthorized()
    {
        Unauthenticated();

        HttpResponseData response = await Sut.List(FunctionHttp.Get("", path: "config/api-keys"), CancellationToken.None);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task List_WithoutGrant_ReturnsForbidden()
    {
        Authenticate(canManageApiKeys: false);

        HttpResponseData response = await Sut.List(FunctionHttp.Get("", path: "config/api-keys"), CancellationToken.None);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task List_WithGrant_ReturnsOkEnvelope()
    {
        Authenticate();
        _keys.Setup(k => k.GetAllAsync(It.IsAny<CancellationToken>())).ReturnsAsync([Key()]);

        HttpResponseData response = await Sut.List(FunctionHttp.Get("", path: "config/api-keys"), CancellationToken.None);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using JsonDocument body = JsonDocument.Parse(FunctionHttp.BodyText(response));
        Assert.Equal(1, body.RootElement.GetProperty("data").GetArrayLength());
    }

    // ── Create ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Create_WithoutGrant_ReturnsForbidden_AndNeverIssues()
    {
        Authenticate(canManageApiKeys: false);

        HttpResponseData response = await Sut.Create(
            FunctionHttp.Post("config/api-keys", new { partnerName = "GSO", allOffices = true }), CancellationToken.None);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        _keys.Verify(k => k.IssueAsync(It.IsAny<Guid>(), It.IsAny<CreateApiKeyDto>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Create_MalformedBody_ReturnsBadRequest()
    {
        Authenticate();

        HttpResponseData response = await Sut.Create(
            FunctionHttp.Post("config/api-keys", "{ not json"), CancellationToken.None);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Create_Valid_ReturnsCreated_WithPlaintextKey()
    {
        Authenticate();
        CreateApiKeyResultDto result = new(Key(), "ppdo_ab12cd34_plaintextsecret");
        _keys.Setup(k => k.IssueAsync(Caller.Id, It.IsAny<CreateApiKeyDto>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ServiceResult<CreateApiKeyResultDto>.Ok(result));

        HttpResponseData response = await Sut.Create(
            FunctionHttp.Post("config/api-keys", new { partnerName = "GSO", allOffices = true }), CancellationToken.None);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        using JsonDocument body = JsonDocument.Parse(FunctionHttp.BodyText(response));
        Assert.Equal("ppdo_ab12cd34_plaintextsecret", body.RootElement.GetProperty("data").GetProperty("plaintextKey").GetString());
    }

    [Fact]
    public async Task Create_ServiceValidationFailure_ReturnsBadRequest()
    {
        Authenticate();
        _keys.Setup(k => k.IssueAsync(Caller.Id, It.IsAny<CreateApiKeyDto>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ServiceResult<CreateApiKeyResultDto>.BadRequest("Choose at least one office, or all offices."));

        HttpResponseData response = await Sut.Create(
            FunctionHttp.Post("config/api-keys", new { partnerName = "GSO", allOffices = false }), CancellationToken.None);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    // ── Revoke ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Revoke_WithoutGrant_ReturnsForbidden()
    {
        Authenticate(canManageApiKeys: false);

        HttpResponseData response = await Sut.Revoke(
            FunctionHttp.Post("config/api-keys/1/revoke"), 1, CancellationToken.None);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Revoke_UnknownId_ReturnsNotFound()
    {
        Authenticate();
        _keys.Setup(k => k.RevokeAsync(Caller.Id, 404, It.IsAny<CancellationToken>()))
            .ReturnsAsync(ServiceResult<ApiKeyListItemDto>.NotFound("API key not found."));

        HttpResponseData response = await Sut.Revoke(
            FunctionHttp.Post("config/api-keys/404/revoke"), 404, CancellationToken.None);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Revoke_AlreadyRevoked_ReturnsConflict()
    {
        Authenticate();
        _keys.Setup(k => k.RevokeAsync(Caller.Id, 1, It.IsAny<CancellationToken>()))
            .ReturnsAsync(ServiceResult<ApiKeyListItemDto>.Conflict("This key is already revoked."));

        HttpResponseData response = await Sut.Revoke(
            FunctionHttp.Post("config/api-keys/1/revoke"), 1, CancellationToken.None);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task Revoke_Valid_ReturnsOkEnvelope()
    {
        Authenticate();
        _keys.Setup(k => k.RevokeAsync(Caller.Id, 1, It.IsAny<CancellationToken>()))
            .ReturnsAsync(ServiceResult<ApiKeyListItemDto>.Ok(Key(status: "Revoked")));

        HttpResponseData response = await Sut.Revoke(
            FunctionHttp.Post("config/api-keys/1/revoke"), 1, CancellationToken.None);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    // ── Requests (usage) ─────────────────────────────────────────────────────

    [Fact]
    public async Task Requests_WithoutGrant_ReturnsForbidden()
    {
        Authenticate(canManageApiKeys: false);

        HttpResponseData response = await Sut.Requests(
            FunctionHttp.Get("", path: "config/api-keys/1/requests"), 1, CancellationToken.None);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Requests_UnknownId_ReturnsNotFound()
    {
        Authenticate();
        _keys.Setup(k => k.GetRequestsAsync(404, 1, It.IsAny<CancellationToken>()))
            .ReturnsAsync(ServiceResult<ApiKeyRequestLogPageDto>.NotFound("API key not found."));

        HttpResponseData response = await Sut.Requests(
            FunctionHttp.Get("", path: "config/api-keys/404/requests"), 404, CancellationToken.None);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Requests_Valid_ReturnsOkEnvelope_WithItemsAndTotal()
    {
        Authenticate();
        ApiKeyRequestLogPageDto page = new(
            [new ApiKeyRequestLogItemDto(DateTime.UtcNow, "aip", "PPDO", 2028, 200)], 1);
        _keys.Setup(k => k.GetRequestsAsync(1, 1, It.IsAny<CancellationToken>()))
            .ReturnsAsync(ServiceResult<ApiKeyRequestLogPageDto>.Ok(page));

        HttpResponseData response = await Sut.Requests(
            FunctionHttp.Get("page=1", path: "config/api-keys/1/requests"), 1, CancellationToken.None);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using JsonDocument body = JsonDocument.Parse(FunctionHttp.BodyText(response));
        Assert.Equal(1, body.RootElement.GetProperty("data").GetProperty("total").GetInt32());
        Assert.Equal(1, body.RootElement.GetProperty("data").GetProperty("items").GetArrayLength());
    }

    [Fact]
    public async Task Requests_DefaultsToPageOne_WhenPageMissing()
    {
        Authenticate();
        _keys.Setup(k => k.GetRequestsAsync(1, 1, It.IsAny<CancellationToken>()))
            .ReturnsAsync(ServiceResult<ApiKeyRequestLogPageDto>.Ok(new ApiKeyRequestLogPageDto([], 0)));

        await Sut.Requests(FunctionHttp.Get("", path: "config/api-keys/1/requests"), 1, CancellationToken.None);

        _keys.Verify(k => k.GetRequestsAsync(1, 1, It.IsAny<CancellationToken>()), Times.Once);
    }
}
