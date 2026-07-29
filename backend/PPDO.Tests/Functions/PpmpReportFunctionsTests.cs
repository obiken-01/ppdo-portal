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
/// Endpoint tests for <see cref="PpmpReportFunctions"/> (v1.5 — RAL-183/184).
///
/// <see cref="PpmpReportServiceTests"/> covers the report's *content*; this class covers the layer
/// above it, which is where the security decisions are made and where none of the service tests
/// reach: the JWT check, the CanAccessBudgetPlanning gate, query-parameter validation, and — the
/// reason this file exists — the <b>division clamp</b> (RAL-136). A division-scoped caller must
/// always be pinned to their own division; a <c>divisionId</c> they put on the query string must be
/// ignored, never honoured. Both the preview and the export path enforce it independently, so both
/// are tested independently: a regression on one would not be caught by the other.
///
/// The service and Excel exporter are mocked — what is asserted here is the arguments the handler
/// passes down (especially the resolved divisionId) and the HTTP shape it returns.
/// </summary>
public sealed class PpmpReportFunctionsTests
{
    private const int OfficeId    = 3;
    private const int FiscalYear  = 2027;
    private const int OwnDivision = 7;

    private readonly Mock<IPpmpReportService>      _report      = new(MockBehavior.Strict);
    private readonly Mock<IPpmpReportExcelService> _excel       = new(MockBehavior.Strict);
    private readonly Mock<IJwtMiddleware>          _jwt         = new(MockBehavior.Strict);
    private readonly Mock<IPermissionService>      _permissions = new(MockBehavior.Loose);

    private PpmpReportFunctions Sut => new(_report.Object, _excel.Object, _jwt.Object, _permissions.Object);

    // ── Arrange helpers ────────────────────────────────────────────────────────

    private static User MakeUser(int? divisionId) => new()
    {
        Id           = Guid.NewGuid(),
        FullName     = "Test User",
        Email        = "test@ppdo.gov.ph",
        Username     = "test",
        PasswordHash = "hash",
        Role         = UserRole.Staff,
        DivisionId   = divisionId,
    };

    private static PpmpReportDto MakeReport() =>
        new(FiscalYear, "PPDO", "Provincial Planning and Development Office", "Administrative",
            new List<PpmpReportFundSourceDto>
            {
                new("GENERAL FUND", new List<PpmpReportProgramDto>(), 0m),
            });

    /// <summary>Authenticates the caller and sets the two permission answers the handlers ask for.</summary>
    private void Authenticate(User caller, bool canAccessBudgetPlanning = true, bool canManageAllocation = false)
    {
        _jwt.Setup(j => j.ValidateAsync(It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(caller);
        _permissions.Setup(p => p.CanAccessBudgetPlanningAsync(caller, It.IsAny<CancellationToken>()))
            .ReturnsAsync(canAccessBudgetPlanning);
        _permissions.Setup(p => p.CanManageAllocationAsync(caller, It.IsAny<CancellationToken>()))
            .ReturnsAsync(canManageAllocation);
    }

    /// <summary>Captures the divisionId the handler resolves and hands to the service.</summary>
    private void ExpectReport(ServiceResult<PpmpReportDto> result, Action<int?> captureDivisionId)
    {
        _report.Setup(r => r.GetReportAsync(OfficeId, FiscalYear, It.IsAny<int?>(), It.IsAny<CancellationToken>()))
            .Callback((int _, int _, int? divisionId, CancellationToken _) => captureDivisionId(divisionId))
            .ReturnsAsync(result);
    }

    private static string Query(string? divisionId = null)
        => $"officeId={OfficeId}&fiscalYear={FiscalYear}" + (divisionId is null ? "" : $"&divisionId={divisionId}");

    // ── Auth gate — preview ────────────────────────────────────────────────────

    [Fact]
    public async Task GetPreview_WithInvalidToken_ReturnsUnauthorizedAndNeverCallsService()
    {
        _jwt.Setup(j => j.ValidateAsync(It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((User?)null);

        HttpResponseData response = await Sut.GetPreview(FunctionHttp.Get(Query()), CancellationToken.None);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        _report.Verify(r => r.GetReportAsync(
            It.IsAny<int>(), It.IsAny<int>(), It.IsAny<int?>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task GetPreview_WithoutBudgetPlanningPermission_ReturnsForbiddenAndNeverCallsService()
    {
        User caller = MakeUser(OwnDivision);
        Authenticate(caller, canAccessBudgetPlanning: false);

        HttpResponseData response = await Sut.GetPreview(FunctionHttp.Get(Query()), CancellationToken.None);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        _report.Verify(r => r.GetReportAsync(
            It.IsAny<int>(), It.IsAny<int>(), It.IsAny<int?>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    // ── Division clamp — preview (RAL-136) ─────────────────────────────────────

    [Fact]
    public async Task GetPreview_AsDivisionScopedUser_IgnoresDivisionIdQueryAndForcesOwnDivision()
    {
        User caller = MakeUser(OwnDivision);
        Authenticate(caller, canManageAllocation: false);
        int? requested = -1;
        ExpectReport(ServiceResult<PpmpReportDto>.Ok(MakeReport()), d => requested = d);

        // Asking for someone else's division (99) must not widen the caller's scope.
        HttpResponseData response = await Sut.GetPreview(FunctionHttp.Get(Query("99")), CancellationToken.None);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(OwnDivision, requested);
    }

    [Fact]
    public async Task GetPreview_AsDivisionScopedUser_WithNoDivisionIdQuery_StillForcesOwnDivision()
    {
        User caller = MakeUser(OwnDivision);
        Authenticate(caller, canManageAllocation: false);
        int? requested = -1;
        ExpectReport(ServiceResult<PpmpReportDto>.Ok(MakeReport()), d => requested = d);

        await Sut.GetPreview(FunctionHttp.Get(Query()), CancellationToken.None);

        Assert.Equal(OwnDivision, requested);
    }

    [Fact]
    public async Task GetPreview_AsAllocationOfficer_HonoursDivisionIdQuery()
    {
        User caller = MakeUser(OwnDivision);
        Authenticate(caller, canManageAllocation: true);
        int? requested = -1;
        ExpectReport(ServiceResult<PpmpReportDto>.Ok(MakeReport()), d => requested = d);

        await Sut.GetPreview(FunctionHttp.Get(Query("42")), CancellationToken.None);

        Assert.Equal(42, requested);
    }

    [Fact]
    public async Task GetPreview_AsAllocationOfficer_WithNoDivisionIdQuery_RequestsConsolidatedReport()
    {
        User caller = MakeUser(OwnDivision);
        Authenticate(caller, canManageAllocation: true);
        int? requested = -1;
        ExpectReport(ServiceResult<PpmpReportDto>.Ok(MakeReport()), d => requested = d);

        await Sut.GetPreview(FunctionHttp.Get(Query()), CancellationToken.None);

        Assert.Null(requested);
    }

    [Fact]
    public async Task GetPreview_AsAllocationOfficer_WithNonNumericDivisionId_RequestsConsolidatedReport()
    {
        User caller = MakeUser(OwnDivision);
        Authenticate(caller, canManageAllocation: true);
        int? requested = -1;
        ExpectReport(ServiceResult<PpmpReportDto>.Ok(MakeReport()), d => requested = d);

        await Sut.GetPreview(FunctionHttp.Get(Query("abc")), CancellationToken.None);

        Assert.Null(requested);
    }

    /// <summary>
    /// Documents current behaviour, which mirrors the WFP report handler: a division-scoped caller
    /// whose own DivisionId is null resolves to null — i.e. the office-consolidated view — rather
    /// than an empty scope. Compare <see cref="User.DivisionId"/>'s own note, which says a null
    /// division on a Staff user must mean an EMPTY inventory scope. If that rule is ever extended
    /// to Budget Planning, this test is the one to change (and WfpReportFunctions alongside it).
    /// </summary>
    [Fact]
    public async Task GetPreview_AsDivisionScopedUserWithNoDivision_CurrentlyRequestsConsolidatedReport()
    {
        User caller = MakeUser(divisionId: null);
        Authenticate(caller, canManageAllocation: false);
        int? requested = -1;
        ExpectReport(ServiceResult<PpmpReportDto>.Ok(MakeReport()), d => requested = d);

        await Sut.GetPreview(FunctionHttp.Get(Query("99")), CancellationToken.None);

        Assert.Null(requested);
    }

    // ── Query validation + result mapping — preview ────────────────────────────

    [Theory]
    [InlineData("fiscalYear=2027")]
    [InlineData("officeId=3")]
    [InlineData("officeId=abc&fiscalYear=2027")]
    [InlineData("")]
    public async Task GetPreview_WithMissingOrUnparseableParameters_ReturnsBadRequest(string query)
    {
        User caller = MakeUser(OwnDivision);
        Authenticate(caller);

        HttpResponseData response = await Sut.GetPreview(FunctionHttp.Get(query), CancellationToken.None);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        _report.Verify(r => r.GetReportAsync(
            It.IsAny<int>(), It.IsAny<int>(), It.IsAny<int?>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task GetPreview_WithValidRequest_ReturnsOkEnvelopeWithCamelCasedReport()
    {
        User caller = MakeUser(OwnDivision);
        Authenticate(caller);
        ExpectReport(ServiceResult<PpmpReportDto>.Ok(MakeReport()), _ => { });

        HttpResponseData response = await Sut.GetPreview(FunctionHttp.Get(Query()), CancellationToken.None);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using JsonDocument body = JsonDocument.Parse(FunctionHttp.BodyText(response));
        JsonElement data = body.RootElement.GetProperty("data");
        Assert.Equal(FiscalYear, data.GetProperty("fiscalYear").GetInt32());
        Assert.Equal("PPDO", data.GetProperty("officeCode").GetString());
        Assert.Equal("Administrative", data.GetProperty("divisionName").GetString());
    }

    [Fact]
    public async Task GetPreview_WhenReportNotFound_ReturnsNotFoundEnvelope()
    {
        User caller = MakeUser(OwnDivision);
        Authenticate(caller);
        ExpectReport(ServiceResult<PpmpReportDto>.NotFound("No WFP for FY2027."), _ => { });

        HttpResponseData response = await Sut.GetPreview(FunctionHttp.Get(Query()), CancellationToken.None);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        using JsonDocument body = JsonDocument.Parse(FunctionHttp.BodyText(response));
        Assert.Equal("No WFP for FY2027.", body.RootElement.GetProperty("error").GetString());
    }

    // ── Auth gate — export ─────────────────────────────────────────────────────

    [Fact]
    public async Task Export_WithInvalidToken_ReturnsUnauthorizedAndNeverBuildsWorkbook()
    {
        _jwt.Setup(j => j.ValidateAsync(It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((User?)null);

        HttpResponseData response = await Sut.Export(FunctionHttp.Get(Query()), CancellationToken.None);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        _excel.Verify(e => e.Export(It.IsAny<PpmpReportDto>()), Times.Never);
    }

    [Fact]
    public async Task Export_WithoutBudgetPlanningPermission_ReturnsForbiddenAndNeverBuildsWorkbook()
    {
        User caller = MakeUser(OwnDivision);
        Authenticate(caller, canAccessBudgetPlanning: false);

        HttpResponseData response = await Sut.Export(FunctionHttp.Get(Query()), CancellationToken.None);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        _excel.Verify(e => e.Export(It.IsAny<PpmpReportDto>()), Times.Never);
    }

    // ── Division clamp — export (enforced separately from preview) ─────────────

    [Fact]
    public async Task Export_AsDivisionScopedUser_IgnoresDivisionIdQueryAndForcesOwnDivision()
    {
        User caller = MakeUser(OwnDivision);
        Authenticate(caller, canManageAllocation: false);
        int? requested = -1;
        ExpectReport(ServiceResult<PpmpReportDto>.Ok(MakeReport()), d => requested = d);
        _excel.Setup(e => e.Export(It.IsAny<PpmpReportDto>())).Returns([1, 2, 3]);

        HttpResponseData response = await Sut.Export(FunctionHttp.Get(Query("99")), CancellationToken.None);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(OwnDivision, requested);
    }

    [Fact]
    public async Task Export_AsAllocationOfficer_HonoursDivisionIdQuery()
    {
        User caller = MakeUser(OwnDivision);
        Authenticate(caller, canManageAllocation: true);
        int? requested = -1;
        ExpectReport(ServiceResult<PpmpReportDto>.Ok(MakeReport()), d => requested = d);
        _excel.Setup(e => e.Export(It.IsAny<PpmpReportDto>())).Returns([1, 2, 3]);

        await Sut.Export(FunctionHttp.Get(Query("42")), CancellationToken.None);

        Assert.Equal(42, requested);
    }

    // ── Query validation + file response — export ──────────────────────────────

    [Theory]
    [InlineData("fiscalYear=2027")]
    [InlineData("officeId=3")]
    [InlineData("")]
    public async Task Export_WithMissingOrUnparseableParameters_ReturnsBadRequest(string query)
    {
        User caller = MakeUser(OwnDivision);
        Authenticate(caller);

        HttpResponseData response = await Sut.Export(FunctionHttp.Get(query), CancellationToken.None);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        _excel.Verify(e => e.Export(It.IsAny<PpmpReportDto>()), Times.Never);
    }

    [Fact]
    public async Task Export_WhenReportNotFound_ReturnsNotFoundAndNeverBuildsWorkbook()
    {
        User caller = MakeUser(OwnDivision);
        Authenticate(caller);
        ExpectReport(ServiceResult<PpmpReportDto>.NotFound("No WFP for FY2027."), _ => { });

        HttpResponseData response = await Sut.Export(FunctionHttp.Get(Query()), CancellationToken.None);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        _excel.Verify(e => e.Export(It.IsAny<PpmpReportDto>()), Times.Never);
    }

    [Fact]
    public async Task Export_WithValidRequest_ReturnsXlsxAttachmentNamedForTheOfficeAndYear()
    {
        User caller = MakeUser(OwnDivision);
        Authenticate(caller);
        ExpectReport(ServiceResult<PpmpReportDto>.Ok(MakeReport()), _ => { });
        byte[] workbook = [0x50, 0x4B, 0x03, 0x04];   // PK.. — an .xlsx is a zip
        _excel.Setup(e => e.Export(It.IsAny<PpmpReportDto>())).Returns(workbook);

        HttpResponseData response = await Sut.Export(FunctionHttp.Get(Query()), CancellationToken.None);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            FunctionHttp.Header(response, "Content-Type"));
        Assert.Equal("attachment; filename=\"PPMP_PPDO_2027.xlsx\"",
            FunctionHttp.Header(response, "Content-Disposition"));
        Assert.Equal(workbook, FunctionHttp.BodyBytes(response));
    }

    /// <summary>The exported workbook must be built from the same clamped report the preview returns.</summary>
    [Fact]
    public async Task Export_AsDivisionScopedUser_BuildsWorkbookFromTheClampedReport()
    {
        User caller = MakeUser(OwnDivision);
        Authenticate(caller, canManageAllocation: false);
        PpmpReportDto report = MakeReport();
        ExpectReport(ServiceResult<PpmpReportDto>.Ok(report), _ => { });
        PpmpReportDto? exported = null;
        _excel.Setup(e => e.Export(It.IsAny<PpmpReportDto>()))
            .Callback((PpmpReportDto r) => exported = r)
            .Returns([1]);

        await Sut.Export(FunctionHttp.Get(Query("99")), CancellationToken.None);

        Assert.Same(report, exported);
    }
}
