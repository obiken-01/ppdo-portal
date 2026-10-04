using System.Net;
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
/// Endpoint tests for the province-wide default fiscal year (v1.8.0 — PPDO-136 / PPDO-143).
///
/// Who passes <c>CanManageInvestmentPlanningSettings</c> is pinned in <c>PermissionMatrixTests</c>;
/// these tests pin that <b>both routes ask it</b>, that the write also runs the reviewer
/// write-denial guard, and the body contract — in particular that an empty <c>{}</c> is rejected
/// rather than binding to null and silently clearing the setting.
/// </summary>
public sealed class ConfigInvestmentPlanningFunctionsTests
{
    private const string Route = "config/investment-planning/default-fiscal-year";

    private readonly Mock<IInvestmentPlanningSettingsService> _settings = new(MockBehavior.Strict);
    private readonly Mock<IJwtValidator> _jwt = new(MockBehavior.Strict);
    private readonly Mock<IPermissionService> _permissions = new(MockBehavior.Loose);

    private ConfigInvestmentPlanningFunctions Sut => new(_settings.Object, _jwt.Object, _permissions.Object);

    private User Authenticate(bool canManage, bool crossOfficeReviewer = false)
    {
        User caller = new()
        {
            Id = Guid.NewGuid(), FullName = "Test", Username = "test", PasswordHash = "hash",
            Role = UserRole.Admin,
        };
        _jwt.Setup(j => j.ValidateAsync(It.IsAny<string?>(), It.IsAny<CancellationToken>())).ReturnsAsync(caller);
        _permissions.Setup(p => p.CanManageInvestmentPlanningSettingsAsync(caller, It.IsAny<CancellationToken>()))
            .ReturnsAsync(canManage);
        _permissions.Setup(p => p.CanReviewAllOfficesAsync(caller, It.IsAny<CancellationToken>()))
            .ReturnsAsync(crossOfficeReviewer);
        return caller;
    }

    private static readonly DefaultFiscalYearDto Set2028 =
        new(2028, new DateTime(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc), "Juan Dela Cruz");

    // ── GET ──────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Get_Unauthenticated_Returns401()
    {
        _jwt.Setup(j => j.ValidateAsync(It.IsAny<string?>(), It.IsAny<CancellationToken>())).ReturnsAsync((User?)null);

        HttpResponseData response = await Sut.GetDefaultFiscalYear(
            FunctionHttp.Get("", authorizationHeader: null, path: Route), CancellationToken.None);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Get_WithoutTheGrant_Returns403AndNeverReadsTheSetting()
    {
        Authenticate(canManage: false);

        HttpResponseData response = await Sut.GetDefaultFiscalYear(
            FunctionHttp.Get("", path: Route), CancellationToken.None);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        _settings.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Get_WithTheGrant_Returns200WithTheEnvelope()
    {
        Authenticate(canManage: true);
        _settings.Setup(s => s.GetDefaultFiscalYearAsync(It.IsAny<CancellationToken>())).ReturnsAsync(Set2028);

        HttpResponseData response = await Sut.GetDefaultFiscalYear(
            FunctionHttp.Get("", path: Route), CancellationToken.None);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        string body = FunctionHttp.BodyText(response);
        Assert.Contains("\"defaultFiscalYear\":2028", body);
        Assert.Contains("\"updatedByName\":\"Juan Dela Cruz\"", body);
    }

    // ── PUT ──────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Put_Unauthenticated_Returns401()
    {
        _jwt.Setup(j => j.ValidateAsync(It.IsAny<string?>(), It.IsAny<CancellationToken>())).ReturnsAsync((User?)null);

        HttpResponseData response = await Sut.UpdateDefaultFiscalYear(
            FunctionHttp.Put(new { defaultFiscalYear = 2028 }, authorizationHeader: null, path: Route),
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Put_WithoutTheGrant_Returns403AndWritesNothing()
    {
        // e.g. a guest-office Admin: CanManageConfig by role, but not the host office.
        Authenticate(canManage: false);

        HttpResponseData response = await Sut.UpdateDefaultFiscalYear(
            FunctionHttp.Put(new { defaultFiscalYear = 2028 }, path: Route), CancellationToken.None);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        _settings.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Put_CrossOfficeReviewer_Returns403EvenWithTheGrant()
    {
        // Config writes run the RAL-256 reviewer write-denial guard, this one included.
        Authenticate(canManage: true, crossOfficeReviewer: true);

        HttpResponseData response = await Sut.UpdateDefaultFiscalYear(
            FunctionHttp.Put(new { defaultFiscalYear = 2028 }, path: Route), CancellationToken.None);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        _settings.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Put_ValidYear_PassesTheYearAndTheCallerToTheService()
    {
        User caller = Authenticate(canManage: true);
        _settings.Setup(s => s.UpdateDefaultFiscalYearAsync(2028, caller.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(ServiceResult<DefaultFiscalYearDto>.Ok(Set2028));

        HttpResponseData response = await Sut.UpdateDefaultFiscalYear(
            FunctionHttp.Put(new { defaultFiscalYear = 2028 }, path: Route), CancellationToken.None);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        _settings.Verify(s => s.UpdateDefaultFiscalYearAsync(2028, caller.Id, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Put_ExplicitNull_ClearsTheDefault()
    {
        User caller = Authenticate(canManage: true);
        _settings.Setup(s => s.UpdateDefaultFiscalYearAsync(null, caller.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(ServiceResult<DefaultFiscalYearDto>.Ok(new DefaultFiscalYearDto(null, null, null)));

        HttpResponseData response = await Sut.UpdateDefaultFiscalYear(
            FunctionHttp.Put("{\"defaultFiscalYear\":null}", path: Route), CancellationToken.None);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        _settings.Verify(s => s.UpdateDefaultFiscalYearAsync(null, caller.Id, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Theory]
    [InlineData("{}")]                                   // property missing — must NOT clear the setting
    [InlineData("{\"defaultFiscalYear\":\"2028\"}")]     // a string, not an integer
    [InlineData("{\"defaultFiscalYear\":2028.5}")]
    [InlineData("not json")]
    [InlineData("")]
    public async Task Put_MalformedBody_Returns400AndNeverCallsTheService(string body)
    {
        Authenticate(canManage: true);

        HttpResponseData response = await Sut.UpdateDefaultFiscalYear(
            FunctionHttp.Put(body, path: Route), CancellationToken.None);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("Request body is missing or malformed.", FunctionHttp.BodyText(response));
        _settings.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Put_OutOfRange_Returns400WithTheServiceMessage()
    {
        User caller = Authenticate(canManage: true);
        _settings.Setup(s => s.UpdateDefaultFiscalYearAsync(2019, caller.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(ServiceResult<DefaultFiscalYearDto>.BadRequest("Fiscal year must be between 2020 and 2029."));

        HttpResponseData response = await Sut.UpdateDefaultFiscalYear(
            FunctionHttp.Put(new { defaultFiscalYear = 2019 }, path: Route), CancellationToken.None);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("Fiscal year must be between 2020 and 2029.", FunctionHttp.BodyText(response));
    }
}
