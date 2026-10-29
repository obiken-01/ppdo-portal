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
/// reach: the JWT check, the CanAccessWfp gate (host office only, PPDO-265), query-parameter
/// validation, and — the reason this file exists — the <b>division clamp</b> (RAL-136). A division-scoped caller must
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
    private readonly Mock<IJwtValidator>          _jwt         = new(MockBehavior.Strict);
    private readonly Mock<IPermissionService>      _permissions = new(MockBehavior.Loose);

    // Sut runs with the switch as shipped (FeatureFlags.PpmpReportEnabled, on again since PPDO-265);
    // SutSwitchedOff keeps the PPDO-236 off-path tested, so the switch still works if it is needed.
    private PpmpReportFunctions Sut => new(_report.Object, _excel.Object, _jwt.Object, _permissions.Object);

    private PpmpReportFunctions SutSwitchedOff => new(_report.Object, _excel.Object, _jwt.Object, _permissions.Object, ppmpEnabled: false);

    // ── Arrange helpers ────────────────────────────────────────────────────────

    private static User MakeUser(int? divisionId, UserRole role = UserRole.Staff) => new()
    {
        Id           = Guid.NewGuid(),
        FullName     = "Test User",
        Email        = "test@ppdo.gov.ph",
        Username     = "test",
        PasswordHash = "hash",
        Role         = role,
        DivisionId   = divisionId,
    };

    /// <summary>A guest-office user as v1.8 creates them: Staff, no division, no overrides.</summary>
    private static User MakeGuestOfficeUser(UserRole role = UserRole.Staff) => new()
    {
        Id           = Guid.NewGuid(),
        FullName     = "Guest Office User",
        Email        = "guest@ppdo.gov.ph",
        Username     = "guest",
        PasswordHash = "hash",
        Role         = role,
        OfficeId     = 9,
        Office       = new Office { Id = 9, OfficeCode = "PTO", OfficeName = "Provincial Treasurer's Office", IsHostOffice = false },
    };

    /// <summary>The handler wired to the real <see cref="PermissionService"/>, so the office rule is the real one.</summary>
    private PpmpReportFunctions SutWithRealPermissions(User caller)
    {
        _jwt.Setup(j => j.ValidateAsync(It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(caller);
        return new PpmpReportFunctions(_report.Object, _excel.Object, _jwt.Object, new PermissionService());
    }

    private static PpmpReportDto MakeReport() =>
        new(FiscalYear, "PPDO", "Provincial Planning and Development Office", "Administrative",
            new List<PpmpReportFundSourceDto>
            {
                new("GENERAL FUND", new List<PpmpReportProgramDto>(), 0m),
            });

    /// <summary>
    /// Authenticates the caller and sets the permission answers. Budget Planning is always granted,
    /// as it is for every guest-office user: the gate under test is <c>CanAccessWfp</c> (PPDO-265).
    /// </summary>
    private void Authenticate(User caller, bool canAccessWfp = true, bool canManagePpdoAllocation = false)
    {
        _jwt.Setup(j => j.ValidateAsync(It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(caller);
        _permissions.Setup(p => p.CanAccessBudgetPlanningAsync(caller, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        _permissions.Setup(p => p.CanAccessWfpAsync(caller, It.IsAny<CancellationToken>()))
            .ReturnsAsync(canAccessWfp);
        _permissions.Setup(p => p.CanManagePpdoAllocationAsync(caller, It.IsAny<CancellationToken>()))
            .ReturnsAsync(canManagePpdoAllocation);
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

    // ── The switch (hidden in PPDO-236, restored in PPDO-265) ─────────────────

    /// <summary>A PPDO colleague builds the office PPMP from this report; it must not be hidden again by accident.</summary>
    [Fact]
    public void PpmpReportEnabled_IsOn()
    {
        Assert.True(FeatureFlags.PpmpReportEnabled);
    }

    [Fact]
    public async Task GetPreview_WhenSwitchedOff_Returns404WithoutReadingAnything()
    {
        // MockBehavior.Strict on every collaborator: any call at all (JWT, permission, service) throws.
        HttpResponseData response = await SutSwitchedOff.GetPreview(FunctionHttp.Get(Query()), CancellationToken.None);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Export_WhenSwitchedOff_Returns404WithoutReadingAnything()
    {
        HttpResponseData response = await SutSwitchedOff.Export(FunctionHttp.Get(Query()), CancellationToken.None);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    // ── Host office only (PPDO-265) ────────────────────────────────────────────
    // The report reads PPDO's WFP. Guest-office users hold Budget Planning by default since v1.8, so
    // that grant alone must not open it. These run the real PermissionService, not a mocked answer.

    [Theory]
    [InlineData(UserRole.Staff)]
    [InlineData(UserRole.Admin)]
    public async Task GetPreview_AsGuestOfficeUser_Returns403AndNeverCallsService(UserRole role)
    {
        User guest = MakeGuestOfficeUser(role);

        HttpResponseData response = await SutWithRealPermissions(guest)
            .GetPreview(FunctionHttp.Get(Query()), CancellationToken.None);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        _report.Verify(r => r.GetReportAsync(
            It.IsAny<int>(), It.IsAny<int>(), It.IsAny<int?>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Theory]
    [InlineData(UserRole.Staff)]
    [InlineData(UserRole.Admin)]
    public async Task Export_AsGuestOfficeUser_Returns403AndNeverBuildsWorkbook(UserRole role)
    {
        User guest = MakeGuestOfficeUser(role);

        HttpResponseData response = await SutWithRealPermissions(guest)
            .Export(FunctionHttp.Get(Query()), CancellationToken.None);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        _report.Verify(r => r.GetReportAsync(
            It.IsAny<int>(), It.IsAny<int>(), It.IsAny<int?>(), It.IsAny<CancellationToken>()), Times.Never);
        _excel.Verify(e => e.Export(It.IsAny<PpmpReportDto>()), Times.Never);
    }

    [Fact]
    public async Task GetPreview_AsHostOfficeAdmin_IsServed()
    {
        User admin = MakeUser(OwnDivision, UserRole.Admin);
        admin.OfficeId = 1;
        admin.Office   = new Office { Id = 1, OfficeCode = "PPDO", IsHostOffice = true };
        ExpectReport(ServiceResult<PpmpReportDto>.Ok(MakeReport()), _ => { });

        HttpResponseData response = await SutWithRealPermissions(admin)
            .GetPreview(FunctionHttp.Get(Query()), CancellationToken.None);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

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
    public async Task GetPreview_WithoutWfpAccess_ReturnsForbiddenAndNeverCallsService()
    {
        User caller = MakeUser(OwnDivision);
        Authenticate(caller, canAccessWfp: false);

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
        Authenticate(caller, canManagePpdoAllocation: false);
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
        Authenticate(caller, canManagePpdoAllocation: false);
        int? requested = -1;
        ExpectReport(ServiceResult<PpmpReportDto>.Ok(MakeReport()), d => requested = d);

        await Sut.GetPreview(FunctionHttp.Get(Query()), CancellationToken.None);

        Assert.Equal(OwnDivision, requested);
    }

    [Fact]
    public async Task GetPreview_AsAllocationOfficer_HonoursDivisionIdQuery()
    {
        User caller = MakeUser(OwnDivision);
        Authenticate(caller, canManagePpdoAllocation: true);
        int? requested = -1;
        ExpectReport(ServiceResult<PpmpReportDto>.Ok(MakeReport()), d => requested = d);

        await Sut.GetPreview(FunctionHttp.Get(Query("42")), CancellationToken.None);

        Assert.Equal(42, requested);
    }

    [Fact]
    public async Task GetPreview_AsAllocationOfficer_WithNoDivisionIdQuery_RequestsConsolidatedReport()
    {
        User caller = MakeUser(OwnDivision);
        Authenticate(caller, canManagePpdoAllocation: true);
        int? requested = -1;
        ExpectReport(ServiceResult<PpmpReportDto>.Ok(MakeReport()), d => requested = d);

        await Sut.GetPreview(FunctionHttp.Get(Query()), CancellationToken.None);

        Assert.Null(requested);
    }

    [Fact]
    public async Task GetPreview_AsAllocationOfficer_WithNonNumericDivisionId_RequestsConsolidatedReport()
    {
        User caller = MakeUser(OwnDivision);
        Authenticate(caller, canManagePpdoAllocation: true);
        int? requested = -1;
        ExpectReport(ServiceResult<PpmpReportDto>.Ok(MakeReport()), d => requested = d);

        await Sut.GetPreview(FunctionHttp.Get(Query("abc")), CancellationToken.None);

        Assert.Null(requested);
    }

    /// <summary>
    /// ↩️ PPDO-265: Staff with no division now see nothing, as on the WFP report (WfpDivisionScope,
    /// PPDO-198). Before, their null division was passed down as "every division", so they read the
    /// whole office's report. They get the same 404 as a missing report, and the service is not asked.
    /// </summary>
    [Fact]
    public async Task GetPreview_AsStaffWithNoDivision_Returns404AndNeverCallsService()
    {
        User caller = MakeUser(divisionId: null);
        Authenticate(caller, canManagePpdoAllocation: false);

        HttpResponseData response = await Sut.GetPreview(FunctionHttp.Get(Query("99")), CancellationToken.None);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        _report.Verify(r => r.GetReportAsync(
            It.IsAny<int>(), It.IsAny<int>(), It.IsAny<int?>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Export_AsStaffWithNoDivision_Returns404AndNeverBuildsWorkbook()
    {
        User caller = MakeUser(divisionId: null);
        Authenticate(caller, canManagePpdoAllocation: false);

        HttpResponseData response = await Sut.Export(FunctionHttp.Get(Query()), CancellationToken.None);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        _excel.Verify(e => e.Export(It.IsAny<PpmpReportDto>()), Times.Never);
    }

    /// <summary>↩️ PPDO-265: Admin reads every division, as on the WFP report; before, Admin was pinned to their own.</summary>
    [Fact]
    public async Task GetPreview_AsAdmin_HonoursDivisionIdQuery()
    {
        User caller = MakeUser(OwnDivision, UserRole.Admin);
        Authenticate(caller, canManagePpdoAllocation: false);
        int? requested = -1;
        ExpectReport(ServiceResult<PpmpReportDto>.Ok(MakeReport()), d => requested = d);

        await Sut.GetPreview(FunctionHttp.Get(Query("42")), CancellationToken.None);

        Assert.Equal(42, requested);
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
    public async Task Export_WithoutWfpAccess_ReturnsForbiddenAndNeverBuildsWorkbook()
    {
        User caller = MakeUser(OwnDivision);
        Authenticate(caller, canAccessWfp: false);

        HttpResponseData response = await Sut.Export(FunctionHttp.Get(Query()), CancellationToken.None);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        _excel.Verify(e => e.Export(It.IsAny<PpmpReportDto>()), Times.Never);
    }

    // ── Division clamp — export (enforced separately from preview) ─────────────

    [Fact]
    public async Task Export_AsDivisionScopedUser_IgnoresDivisionIdQueryAndForcesOwnDivision()
    {
        User caller = MakeUser(OwnDivision);
        Authenticate(caller, canManagePpdoAllocation: false);
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
        Authenticate(caller, canManagePpdoAllocation: true);
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
        Authenticate(caller, canManagePpdoAllocation: false);
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
