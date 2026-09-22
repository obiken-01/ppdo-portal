using System.Net;
using System.Text.Json;
using Microsoft.Azure.Functions.Worker.Http;
using Moq;
using PPDO.Application.Common;
using PPDO.Application.DTOs.BudgetPlanning;
using PPDO.Application.Services;
using PPDO.Domain.Entities;
using PPDO.Domain.Enums;
using PPDO.Domain.Interfaces;
using PPDO.Functions.Functions;

namespace PPDO.Tests.Functions;

/// <summary>
/// The Annex B download endpoint (V18-60 / PPDO-84, <c>AIP_Form_Spec.md</c> §12). The workbook's
/// content is <see cref="AipFormExcelServiceTests"/>' and the service's rules
/// <see cref="PPDO.Tests.Application.AipConsolidatedServiceTests"/>'; this covers the HTTP shape — the
/// gate, the query, the file headers, and that a refusal comes back as an envelope, not a file.
/// </summary>
public sealed class AipConsolidatedFunctionsTests
{
    private const int FiscalYear = 2028;

    private readonly Mock<IAipConsolidatedService> _consolidated = new(MockBehavior.Strict);
    private readonly Mock<IJwtValidator>          _jwt          = new(MockBehavior.Strict);
    private readonly Mock<IPermissionService>      _permissions  = new(MockBehavior.Loose);

    private AipConsolidatedFunctions Sut => new(_consolidated.Object, _jwt.Object, _permissions.Object);

    private User Authenticate(bool crossOffice = true, bool deptHead = false)
    {
        User caller = new()
        {
            Id = Guid.NewGuid(), FullName = "Reviewer", Username = "reviewer",
            PasswordHash = "hash", Role = UserRole.Staff,
        };
        _jwt.Setup(j => j.ValidateAsync(It.IsAny<string?>(), It.IsAny<CancellationToken>())).ReturnsAsync(caller);
        _permissions.Setup(p => p.CanReviewAllOfficesAsync(caller, It.IsAny<CancellationToken>())).ReturnsAsync(crossOffice);
        // ↩️ PPDO-90 — either reviewer flag admits; the SERVICE decides which offices.
        _permissions.Setup(p => p.CanReviewBudgetPlanningAsync(caller, It.IsAny<CancellationToken>())).ReturnsAsync(deptHead);
        return caller;
    }

    private void VerifyNeverExported()
        => _consolidated.Verify(c => c.ExportWorkbookAsync(
            It.IsAny<int>(), It.IsAny<int?>(), It.IsAny<User>(), It.IsAny<CancellationToken>()), Times.Never);

    [Fact]
    public async Task Export_WithInvalidToken_ReturnsUnauthorized()
    {
        _jwt.Setup(j => j.ValidateAsync(It.IsAny<string?>(), It.IsAny<CancellationToken>())).ReturnsAsync((User?)null);

        HttpResponseData response = await Sut.Export(FunctionHttp.Get($"fiscalYear={FiscalYear}"), CancellationToken.None);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        VerifyNeverExported();
    }

    /// <summary>⚠️ A department head, a PPDO division user or an encoder — anyone without the cross-office flag.</summary>
    [Fact]
    public async Task Export_WithoutCrossOfficeFlag_ReturnsForbidden()
    {
        Authenticate(crossOffice: false);

        HttpResponseData response = await Sut.Export(FunctionHttp.Get($"fiscalYear={FiscalYear}"), CancellationToken.None);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        VerifyNeverExported();
    }

    [Theory]
    [InlineData("")]
    [InlineData("fiscalYear=")]
    [InlineData("fiscalYear=abc")]
    public async Task Export_WithMissingOrUnparseableFiscalYear_ReturnsBadRequest(string query)
    {
        Authenticate();

        HttpResponseData response = await Sut.Export(FunctionHttp.Get(query), CancellationToken.None);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using JsonDocument body = JsonDocument.Parse(FunctionHttp.BodyText(response));
        Assert.Equal("fiscalYear is required.", body.RootElement.GetProperty("error").GetString());
        VerifyNeverExported();
    }

    [Fact]
    public async Task Export_WithValidRequest_ReturnsTheWorkbookAsAnXlsxAttachment()
    {
        User caller = Authenticate();
        byte[] workbook = [0x50, 0x4B, 0x03, 0x04];
        _consolidated.Setup(c => c.ExportWorkbookAsync(FiscalYear, null, caller, It.IsAny<CancellationToken>()))
            .ReturnsAsync(ServiceResult<AipFormExportFileDto>.Ok(new AipFormExportFileDto("AIP_FY2028_2026-09-14.xlsx", workbook)));

        HttpResponseData response = await Sut.Export(FunctionHttp.Get($"fiscalYear={FiscalYear}"), CancellationToken.None);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            FunctionHttp.Header(response, "Content-Type"));
        Assert.Equal("attachment; filename=\"AIP_FY2028_2026-09-14.xlsx\"",
            FunctionHttp.Header(response, "Content-Disposition"));
        Assert.Equal(workbook, FunctionHttp.BodyBytes(response));
    }

    /// <summary>The service's refusals — legacy year 400, unopened year 404 — arrive as envelopes carrying its message.</summary>
    [Theory]
    [InlineData(2027, HttpStatusCode.BadRequest, "FY 2027 and earlier are not rendered as the Annex B export.")]
    [InlineData(2029, HttpStatusCode.NotFound, "FY 2029 has not been opened.")]
    public async Task Export_WhenTheServiceRefuses_ReturnsItsStatusAndMessage(
        int fiscalYear, HttpStatusCode status, string message)
    {
        User caller = Authenticate();
        _consolidated.Setup(c => c.ExportWorkbookAsync(fiscalYear, null, caller, It.IsAny<CancellationToken>()))
            .ReturnsAsync(status == HttpStatusCode.NotFound
                ? ServiceResult<AipFormExportFileDto>.NotFound(message)
                : ServiceResult<AipFormExportFileDto>.BadRequest(message));

        HttpResponseData response = await Sut.Export(FunctionHttp.Get($"fiscalYear={fiscalYear}"), CancellationToken.None);

        Assert.Equal(status, response.StatusCode);
        using JsonDocument body = JsonDocument.Parse(FunctionHttp.BodyText(response));
        Assert.Equal(message, body.RootElement.GetProperty("error").GetString());
    }

    // ── Gate and scope (PPDO-90) ─────────────────────────────────────────────

    /// <summary>
    /// ↩️ The gate widened: a department head — no cross-office grant — now gets in, because this is
    /// where they read their OWN office's Annex B. Which offices they see is the service's decision.
    /// </summary>
    [Fact]
    public async Task GetSheet_WithOnlyTheDepartmentHeadFlag_IsAdmitted()
    {
        User caller = Authenticate(crossOffice: false, deptHead: true);
        _consolidated.Setup(c => c.GetSheetAsync(FiscalYear, "GENERAL", null, caller, It.IsAny<CancellationToken>()))
            .ReturnsAsync(ServiceResult<AipConsolidatedSheetDto>.Ok(Sheet()));

        HttpResponseData response =
            await Sut.GetSheet(FunctionHttp.Get($"fiscalYear={FiscalYear}&sector=GENERAL"), CancellationToken.None);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    /// <summary>Neither flag is what refuses now — not the absence of the cross-office one alone.</summary>
    [Fact]
    public async Task GetSheet_WithNeitherReviewerFlag_ReturnsForbidden()
    {
        Authenticate(crossOffice: false, deptHead: false);

        HttpResponseData response =
            await Sut.GetSheet(FunctionHttp.Get($"fiscalYear={FiscalYear}&sector=GENERAL"), CancellationToken.None);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    /// <summary>`officeId` is parsed and handed on; a junk or non-positive value is simply absent.</summary>
    [Theory]
    [InlineData("&officeId=7", 7)]
    [InlineData("&officeId=abc", null)]
    [InlineData("&officeId=0", null)]
    [InlineData("", null)]
    public async Task GetSheet_ParsesOfficeIdAndPassesItOn(string query, int? expected)
    {
        User caller = Authenticate();
        _consolidated.Setup(c => c.GetSheetAsync(FiscalYear, "GENERAL", expected, caller, It.IsAny<CancellationToken>()))
            .ReturnsAsync(ServiceResult<AipConsolidatedSheetDto>.Ok(Sheet()));

        HttpResponseData response = await Sut.GetSheet(
            FunctionHttp.Get($"fiscalYear={FiscalYear}&sector=GENERAL{query}"), CancellationToken.None);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        _consolidated.Verify(
            c => c.GetSheetAsync(FiscalYear, "GENERAL", expected, caller, It.IsAny<CancellationToken>()), Times.Once);
    }

    private static AipConsolidatedSheetDto Sheet()
        => new(1, FiscalYear, "GENERAL", Opened: true, 0, 0, [], [], AipPrintedFigures.Zero);

}
