using System.Net;
using Microsoft.Azure.Functions.Worker.Http;
using Moq;
using PPDO.Application.DTOs.BudgetPlanning;
using PPDO.Application.Services;
using PPDO.Domain.Entities;
using PPDO.Domain.Enums;
using PPDO.Domain.Interfaces;
using PPDO.Functions.Functions;

namespace PPDO.Tests.Functions;

/// <summary>
/// Endpoint tests for <see cref="BudgetPlanningDashboardFunctions"/>' office-scoped handlers
/// (v1.8.0 — RAL-229).
///
/// The bug this file exists for: <c>GET /budget-planning/dashboard/office</c> read
/// <c>officeId</c> straight off the query string after only a CanAccessBudgetPlanning check,
/// discarding the caller entirely — so any Budget Planning user could read any office's data by
/// changing one parameter. <c>GET /budget-planning/activity</c> had the identical omission.
///
/// Both are now clamped to the caller's own office. The clamp is asserted by capturing the
/// officeId the handler actually hands to the service, not by inspecting the response body — a
/// regression would still return 200 with someone else's data, so status alone proves nothing.
///
/// Mirrors <see cref="PpmpReportFunctionsTests"/>, which does the same for the RAL-136 division
/// clamp one dimension over.
/// </summary>
public sealed class BudgetPlanningDashboardFunctionsTests
{
    private const int OwnOffice     = 3;
    private const int ForeignOffice = 99;
    private const int FiscalYear    = 2027;

    private readonly Mock<IBudgetPlanningDashboardService> _service     = new(MockBehavior.Strict);
    private readonly Mock<IJwtMiddleware>                  _jwt         = new(MockBehavior.Strict);
    private readonly Mock<IPermissionService>              _permissions = new(MockBehavior.Loose);

    private BudgetPlanningDashboardFunctions Sut => new(_service.Object, _jwt.Object, _permissions.Object);

    // ── Arrange helpers ────────────────────────────────────────────────────────

    /// <summary>An office user is one with OfficeId set; a PPDO-internal user has it null.</summary>
    private static User MakeUser(int? officeId) => new()
    {
        Id           = Guid.NewGuid(),
        FullName     = "Test User",
        Username     = "test",
        PasswordHash = "hash",
        Role         = UserRole.Staff,
        // DECISION F (RAL-258): the host-office flag grants cross-office access, not a null
        // office id. `officeId: null` here still reads as "the PPDO caller" — it now resolves
        // to the host office row rather than to the absence of one.
        OfficeId     = officeId ?? HostOfficeId,
        Office       = new Office
        {
            Id           = officeId ?? HostOfficeId,
            OfficeCode   = officeId is null ? "PPDO" : $"OFF{officeId}",
            IsHostOffice = officeId is null,
        },
    };

    /// <summary>Id of the office flagged <c>IsHostOffice</c> in these fixtures.</summary>
    private const int HostOfficeId = 1;

    private void Authenticate(User caller, bool canAccessBudgetPlanning = true)
    {
        _jwt.Setup(j => j.ValidateAsync(It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(caller);
        _permissions.Setup(p => p.CanAccessBudgetPlanningAsync(caller, It.IsAny<CancellationToken>()))
            .ReturnsAsync(canAccessBudgetPlanning);
    }

    private static OfficeDashboardDto MakeOfficeDashboard(int officeId) => new(
        officeId, FiscalYear,
        new AllocationSetupSummaryDto(null, 0m, null, false, 0, 0),
        new OfficeLdipSummaryDto(false, 0, Array.Empty<StatusBreakdownDto>()),
        new OfficeAipSummaryDto(false, null, 0, 0, 0));

    private static PpdoDashboardDto MakePpdoDashboard() => new(
        FiscalYear, new[] { FiscalYear }, OwnOffice, "PPDO", "Provincial Planning and Development Office",
        new OfficeLdipSummaryDto(false, 0, Array.Empty<StatusBreakdownDto>()),
        new OfficeAipSummaryDto(false, null, 0, 0, 0),
        Array.Empty<DivisionWfpStatusDto>(),
        Array.Empty<FundCeilingDto>());

    /// <summary>Captures the officeId the handler resolves and hands to the service.</summary>
    private void ExpectOfficeDashboard(Action<int> captureOfficeId)
    {
        _service.Setup(s => s.GetOfficeDashboardAsync(
                It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .Callback((int officeId, int _, CancellationToken _) => captureOfficeId(officeId))
            .ReturnsAsync((int officeId, int _, CancellationToken _) => MakeOfficeDashboard(officeId));
    }

    private void ExpectRecentActivity(Action<int?> captureOfficeId)
    {
        _service.Setup(s => s.GetRecentActivityAsync(It.IsAny<int?>(), It.IsAny<CancellationToken>()))
            .Callback((int? officeId, CancellationToken _) => captureOfficeId(officeId))
            .ReturnsAsync(Array.Empty<RecentActivityDto>());
    }

    private static FakeHttpRequestData OfficeDashboardRequest(string query)
        => FunctionHttp.Get(query, path: "budget-planning/dashboard/office");

    private static FakeHttpRequestData ActivityRequest(string query)
        => FunctionHttp.Get(query, path: "budget-planning/activity");

    // ── Auth gate ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task GetOfficeDashboard_WithInvalidToken_ReturnsUnauthorizedAndNeverCallsService()
    {
        _jwt.Setup(j => j.ValidateAsync(It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((User?)null);

        HttpResponseData response = await Sut.GetOfficeDashboard(
            OfficeDashboardRequest($"officeId={OwnOffice}&fiscalYear={FiscalYear}"), CancellationToken.None);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        _service.Verify(s => s.GetOfficeDashboardAsync(
            It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task GetOfficeDashboard_WithoutBudgetPlanningPermission_ReturnsForbiddenAndNeverCallsService()
    {
        User caller = MakeUser(OwnOffice);
        Authenticate(caller, canAccessBudgetPlanning: false);

        HttpResponseData response = await Sut.GetOfficeDashboard(
            OfficeDashboardRequest($"officeId={OwnOffice}&fiscalYear={FiscalYear}"), CancellationToken.None);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        _service.Verify(s => s.GetOfficeDashboardAsync(
            It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    // ── Office clamp — GetOfficeDashboard (RAL-229, the IDOR) ──────────────────

    [Fact]
    public async Task GetOfficeDashboard_AsOfficeUser_IgnoresForeignOfficeIdQueryAndForcesOwnOffice()
    {
        User caller = MakeUser(OwnOffice);
        Authenticate(caller);
        int requested = -1;
        ExpectOfficeDashboard(o => requested = o);

        HttpResponseData response = await Sut.GetOfficeDashboard(
            OfficeDashboardRequest($"officeId={ForeignOffice}&fiscalYear={FiscalYear}"), CancellationToken.None);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(OwnOffice, requested);
    }

    [Fact]
    public async Task GetOfficeDashboard_AsOfficeUser_RequestingOwnOffice_IsUnchanged()
    {
        User caller = MakeUser(OwnOffice);
        Authenticate(caller);
        int requested = -1;
        ExpectOfficeDashboard(o => requested = o);

        HttpResponseData response = await Sut.GetOfficeDashboard(
            OfficeDashboardRequest($"officeId={OwnOffice}&fiscalYear={FiscalYear}"), CancellationToken.None);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(OwnOffice, requested);
    }

    [Fact]
    public async Task GetOfficeDashboard_AsPpdoUser_PassesRequestedOfficeThrough()
    {
        User caller = MakeUser(officeId: null);
        Authenticate(caller);
        int requested = -1;
        ExpectOfficeDashboard(o => requested = o);

        HttpResponseData response = await Sut.GetOfficeDashboard(
            OfficeDashboardRequest($"officeId={ForeignOffice}&fiscalYear={FiscalYear}"), CancellationToken.None);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(ForeignOffice, requested);
    }

    /// <summary>
    /// The clamp must not swallow the pre-existing parameter validation — a missing or malformed
    /// officeId is still a 400, not a silent fallback to the caller's own office.
    /// </summary>
    [Theory]
    [InlineData("fiscalYear=2027")]
    [InlineData("officeId=abc&fiscalYear=2027")]
    [InlineData("officeId=3")]
    public async Task GetOfficeDashboard_WithMissingOrMalformedQuery_StillReturnsBadRequest(string query)
    {
        User caller = MakeUser(OwnOffice);
        Authenticate(caller);

        HttpResponseData response = await Sut.GetOfficeDashboard(
            OfficeDashboardRequest(query), CancellationToken.None);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        _service.Verify(s => s.GetOfficeDashboardAsync(
            It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    // ── PPDO-only gate — GetDashboard (RAL-230) ───────────────────────────────
    // This payload is PPDO's own (its ceilings, per-division allocations, per-division WFP
    // status). There is no office dimension to clamp, so an office user is refused rather than
    // served someone else's data.

    [Fact]
    public async Task GetDashboard_AsOfficeUser_ReturnsForbiddenAndNeverCallsService()
    {
        User caller = MakeUser(OwnOffice);
        Authenticate(caller);

        HttpResponseData response = await Sut.GetDashboard(
            FunctionHttp.Get($"fiscalYear={FiscalYear}", path: "budget-planning/dashboard"),
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        _service.Verify(s => s.GetDashboardAsync(
            It.IsAny<int?>(), It.IsAny<int?>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task GetDashboard_AsPpdoUser_StillReturnsTheDashboard()
    {
        User caller = MakeUser(officeId: null);
        Authenticate(caller);
        _service.Setup(s => s.GetDashboardAsync(
                It.IsAny<int?>(), It.IsAny<int?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(MakePpdoDashboard());

        HttpResponseData response = await Sut.GetDashboard(
            FunctionHttp.Get($"fiscalYear={FiscalYear}", path: "budget-planning/dashboard"),
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        _service.Verify(s => s.GetDashboardAsync(
            It.IsAny<int?>(), It.IsAny<int?>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    // ── Office clamp — GetActivity (same omission, same class) ─────────────────

    [Fact]
    public async Task GetActivity_AsOfficeUser_IgnoresForeignOfficeIdQueryAndForcesOwnOffice()
    {
        User caller = MakeUser(OwnOffice);
        Authenticate(caller);
        int? requested = -1;
        ExpectRecentActivity(o => requested = o);

        HttpResponseData response = await Sut.GetActivity(
            ActivityRequest($"officeId={ForeignOffice}"), CancellationToken.None);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(OwnOffice, requested);
    }

    /// <summary>
    /// officeId is optional here — omitting it means "all offices", which an office user must
    /// still not get. The clamp fills in their own office rather than leaving it null.
    /// </summary>
    [Fact]
    public async Task GetActivity_AsOfficeUser_WithNoOfficeIdQuery_StillForcesOwnOffice()
    {
        User caller = MakeUser(OwnOffice);
        Authenticate(caller);
        int? requested = -1;
        ExpectRecentActivity(o => requested = o);

        HttpResponseData response = await Sut.GetActivity(ActivityRequest(""), CancellationToken.None);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(OwnOffice, requested);
    }

    [Fact]
    public async Task GetActivity_AsPpdoUser_PassesRequestedOfficeThrough()
    {
        User caller = MakeUser(officeId: null);
        Authenticate(caller);
        int? requested = -1;
        ExpectRecentActivity(o => requested = o);

        HttpResponseData response = await Sut.GetActivity(
            ActivityRequest($"officeId={ForeignOffice}"), CancellationToken.None);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(ForeignOffice, requested);
    }

    [Fact]
    public async Task GetActivity_AsPpdoUser_WithNoOfficeIdQuery_StaysUnscoped()
    {
        User caller = MakeUser(officeId: null);
        Authenticate(caller);
        int? requested = -1;
        ExpectRecentActivity(o => requested = o);

        HttpResponseData response = await Sut.GetActivity(ActivityRequest(""), CancellationToken.None);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Null(requested);
    }
}
