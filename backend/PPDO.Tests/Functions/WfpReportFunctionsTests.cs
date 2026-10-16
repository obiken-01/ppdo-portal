using System.Net;
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
/// The WFP report endpoints' front door after PPDO-224: an entered year goes to the initial WFP
/// (open to <c>CanAccessBudgetPlanning</c>, scoped in the service); FY ≤ 2027 keeps exactly its old
/// gate (<c>CanAccessWfp</c>, bare 403) and its old service call.
/// </summary>
public sealed class WfpReportFunctionsTests
{
    private readonly Mock<IWfpReportService>      _report      = new();
    private readonly Mock<IWfpReportExcelService> _excel       = new();
    private readonly Mock<IJwtValidator>          _jwt         = new();
    private readonly Mock<IPermissionService>     _permissions = new();
    private readonly User _caller = new() { Id = Guid.NewGuid(), FullName = "Caller", OfficeId = 3 };

    private WfpReportFunctions Sut => new(_report.Object, _excel.Object, _jwt.Object, _permissions.Object);

    private void Caller(bool budgetPlanning, bool wfp)
    {
        _jwt.Setup(j => j.ValidateAsync(It.IsAny<string?>(), It.IsAny<CancellationToken>())).ReturnsAsync(_caller);
        _permissions.Setup(p => p.CanAccessBudgetPlanningAsync(_caller, It.IsAny<CancellationToken>())).ReturnsAsync(budgetPlanning);
        _permissions.Setup(p => p.CanAccessWfpAsync(_caller, It.IsAny<CancellationToken>())).ReturnsAsync(wfp);
        _permissions.Setup(p => p.CanManagePpdoAllocationAsync(_caller, It.IsAny<CancellationToken>())).ReturnsAsync(true);
    }

    private static WfpReportDto Report(string? source = null) => new(2028, "PTO", "PTO", 0.10m, [], source);

    private void VerifyNoInitialCall()
        => _report.Verify(r => r.GetInitialReportAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<int?>(), It.IsAny<User>(), It.IsAny<CancellationToken>()), Times.Never);

    private void VerifyNoLegacyCall()
        => _report.Verify(r => r.GetReportAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<int?>(), It.IsAny<CancellationToken>()), Times.Never);

    // ── FY <= 2027: unchanged ─────────────────────────────────────────────────

    [Theory]
    [InlineData("officeId=3&fiscalYear=2027")]
    [InlineData("officeId=3")]                       // no year: still the old 403, not a 400
    [InlineData("officeId=3&fiscalYear=abc")]
    public async Task Fy2027_ABudgetPlanningUserWhoIsNotAWfpReader_IsStill403(string query)
    {
        Caller(budgetPlanning: true, wfp: false);

        HttpResponseData preview = await Sut.GetPreview(FunctionHttp.Get(query, path: "budget-planning/wfp/report/preview"), default);
        HttpResponseData export = await Sut.Export(FunctionHttp.Get(query, path: "budget-planning/wfp/report/export"), default);
        HttpResponseData offices = await Sut.GetEligibleOffices(FunctionHttp.Get(query, path: "budget-planning/wfp/report/offices"), default);

        Assert.Equal(HttpStatusCode.Forbidden, preview.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, export.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, offices.StatusCode);
        VerifyNoInitialCall();
        VerifyNoLegacyCall();
    }

    [Fact]
    public async Task Fy2027_AWfpReader_GetsTheEnteredWfp_NeverTheInitialOne()
    {
        Caller(budgetPlanning: true, wfp: true);
        _report.Setup(r => r.GetReportAsync(7, 2027, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(ServiceResult<WfpReportDto>.Ok(Report()));

        HttpResponseData response = await Sut.GetPreview(FunctionHttp.Get("officeId=7&fiscalYear=2027", path: "x"), default);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.DoesNotContain("\"source\"", FunctionHttp.BodyText(response));
        VerifyNoInitialCall();
    }

    [Fact]
    public async Task NoBudgetPlanning_Is403_InAnyYear()
    {
        Caller(budgetPlanning: false, wfp: false);

        foreach (string q in new[] { "officeId=3&fiscalYear=2027", "officeId=3&fiscalYear=2028" })
            Assert.Equal(HttpStatusCode.Forbidden, (await Sut.GetPreview(FunctionHttp.Get(q, path: "x"), default)).StatusCode);
        VerifyNoInitialCall();
    }

    // ── Entered years: the initial WFP ────────────────────────────────────────

    [Fact]
    public async Task Fy2028_AnOfficeUser_ReachesTheInitialWfp_WithTheirRequestedDivisionAndThemselves()
    {
        Caller(budgetPlanning: true, wfp: false);
        _report.Setup(r => r.GetInitialReportAsync(3, 2028, 14, _caller, It.IsAny<CancellationToken>()))
            .ReturnsAsync(ServiceResult<WfpReportDto>.Ok(Report(WfpReportSources.Aip)));

        HttpResponseData response = await Sut.GetPreview(FunctionHttp.Get("officeId=3&fiscalYear=2028&divisionId=14", path: "x"), default);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("\"source\":\"Aip\"", FunctionHttp.BodyText(response));
        VerifyNoLegacyCall();
    }

    [Fact]
    public async Task Fy2028_NotConsolidated_Is409_OnPreviewAndExport()
    {
        Caller(budgetPlanning: true, wfp: true);
        _report.Setup(r => r.GetInitialReportAsync(3, 2028, null, _caller, It.IsAny<CancellationToken>()))
            .ReturnsAsync(ServiceResult<WfpReportDto>.Conflict("PTO's FY 2028 AIP is not marked as done yet."));

        HttpResponseData preview = await Sut.GetPreview(FunctionHttp.Get("officeId=3&fiscalYear=2028", path: "x"), default);
        HttpResponseData export = await Sut.Export(FunctionHttp.Get("officeId=3&fiscalYear=2028", path: "x"), default);

        Assert.Equal(HttpStatusCode.Conflict, preview.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, export.StatusCode);
        Assert.Contains("not marked as done yet", FunctionHttp.BodyText(preview));
        _excel.Verify(e => e.Export(It.IsAny<WfpReportDto>()), Times.Never);
    }

    [Fact]
    public async Task Fy2028_AnotherOffice_Is404_OnExport_NotASpreadsheet()
    {
        Caller(budgetPlanning: true, wfp: false);
        _report.Setup(r => r.GetInitialReportAsync(15, 2028, null, _caller, It.IsAny<CancellationToken>()))
            .ReturnsAsync(ServiceResult<WfpReportDto>.NotFound(WfpReportService.ReportNotFoundMessage));

        HttpResponseData export = await Sut.Export(FunctionHttp.Get("officeId=15&fiscalYear=2028", path: "x"), default);

        Assert.Equal(HttpStatusCode.NotFound, export.StatusCode);
        _excel.Verify(e => e.Export(It.IsAny<WfpReportDto>()), Times.Never);
    }

    [Fact]
    public async Task Fy2028_Export_ReturnsTheWorkbook()
    {
        Caller(budgetPlanning: true, wfp: false);
        WfpReportDto report = Report(WfpReportSources.Aip);
        _report.Setup(r => r.GetInitialReportAsync(3, 2028, null, _caller, It.IsAny<CancellationToken>()))
            .ReturnsAsync(ServiceResult<WfpReportDto>.Ok(report));
        _excel.Setup(e => e.Export(report)).Returns([1, 2, 3]);

        HttpResponseData export = await Sut.Export(FunctionHttp.Get("officeId=3&fiscalYear=2028", path: "x"), default);

        Assert.Equal(HttpStatusCode.OK, export.StatusCode);
        Assert.Equal([1, 2, 3], FunctionHttp.BodyBytes(export));
    }

    [Fact]
    public async Task Fy2028_Offices_AreTheInitialWfpsEligibleOffices()
    {
        Caller(budgetPlanning: true, wfp: false);
        _report.Setup(r => r.GetInitialEligibleOfficesAsync(2028, _caller, It.IsAny<CancellationToken>()))
            .ReturnsAsync([new WfpReportOfficeDto(3, "PTO", "PTO", AipWorkflowStatus.Consolidated)]);

        HttpResponseData response = await Sut.GetEligibleOffices(FunctionHttp.Get("fiscalYear=2028", path: "x"), default);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("PTO", FunctionHttp.BodyText(response));
        _report.Verify(r => r.GetEligibleOfficesAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
