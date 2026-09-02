using System.Net;
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
/// Endpoint tests for <see cref="AllocationFunctions"/>' office scoping (v1.8.0 — PPDO-18).
///
/// The bug this file exists for: all six GETs read <c>officeId</c> straight off the query string
/// after only a CanAccessBudgetPlanning check — a permission office users hold by default — so any
/// office user could read any other office's ceilings, division allocations, PPA assignments and
/// setup status by editing one parameter. Same class as the RAL-229 dashboard IDOR, which was
/// fixed; these were missed.
///
/// The clamp is asserted by capturing the officeId the handler actually hands to the SERVICE, not
/// by inspecting status codes — a regression still returns 200, just with someone else's data.
/// Mirrors <see cref="BudgetPlanningDashboardFunctionsTests"/>.
///
/// <b>Three caller kinds, and the third is the one that makes this ticket awkward:</b>
/// <list type="bullet">
///   <item><description>host-office (PPDO) caller — cross-office, as always;</description></item>
///   <item><description>plain guest-office user — clamped to their own office, whatever they ask
///   for. This is the leak;</description></item>
///   <item><description><b>PBO ceiling holder</b> (RAL-243 / PPDO-2) — a guest-office user with
///   authority over EVERY office's ceiling. A naive clamp catches them and makes that grant
///   unreachable, which is what the separate <c>OfficeScope.ResolveForCeiling</c> entry point
///   exists to prevent. Every read case below is parameterised over all three.</description></item>
/// </list>
/// </summary>
public sealed class AllocationFunctionsTests
{
    private const int HostOfficeId  = 1;
    private const int OwnOffice     = 3;
    private const int ForeignOffice = 99;
    private const int FiscalYear    = 2027;
    private const int FundingSource = 5;
    private const int DivisionId    = 7;

    private readonly Mock<IAllocationService>  _allocation  = new(MockBehavior.Strict);
    private readonly Mock<IJwtMiddleware>      _jwt         = new(MockBehavior.Strict);
    private readonly Mock<IPermissionService>  _permissions = new(MockBehavior.Loose);

    private AllocationFunctions Sut => new(_allocation.Object, _jwt.Object, _permissions.Object);

    // ── Callers ───────────────────────────────────────────────────────────────

    /// <summary>The three caller kinds, named so the theory rows read as prose.</summary>
    public enum Caller
    {
        /// <summary>PPDO — the host office. Cross-office by <see cref="Office.IsHostOffice"/>.</summary>
        HostOffice,

        /// <summary>A guest-office user with no cross-office grant. Must be clamped.</summary>
        PlainOfficeUser,

        /// <summary>A guest-office user holding CanManagePboCeiling. Must NOT be clamped.</summary>
        PboCeilingHolder,
    }

    private static User MakeUser(Caller kind)
    {
        bool host = kind is Caller.HostOffice;
        int  id   = host ? HostOfficeId : OwnOffice;

        return new User
        {
            Id           = Guid.NewGuid(),
            FullName     = "Test User",
            Username     = "test",
            PasswordHash = "hash",
            Role         = UserRole.Staff,
            DivisionId   = DivisionId,
            OfficeId     = id,
            Office       = new Office
            {
                Id           = id,
                OfficeCode   = host ? "PPDO" : "GSO",
                IsHostOffice = host,
            },
        };
    }

    /// <summary>The officeId a given caller kind should end up reading, whatever they asked for.</summary>
    private static int ExpectedOfficeFor(Caller kind)
        => kind is Caller.PlainOfficeUser ? OwnOffice : ForeignOffice;

    private User Authenticate(
        Caller kind,
        bool canAccessBudgetPlanning = true,
        bool canManagePpdoAllocation = false)
    {
        User caller = MakeUser(kind);

        _jwt.Setup(j => j.ValidateAsync(It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(caller);
        _permissions.Setup(p => p.CanAccessBudgetPlanningAsync(caller, It.IsAny<CancellationToken>()))
            .ReturnsAsync(canAccessBudgetPlanning);
        _permissions.Setup(p => p.CanManagePboCeilingAsync(caller, It.IsAny<CancellationToken>()))
            .ReturnsAsync(kind is Caller.PboCeilingHolder);
        _permissions.Setup(p => p.CanManagePpdoAllocationAsync(caller, It.IsAny<CancellationToken>()))
            .ReturnsAsync(canManagePpdoAllocation);

        return caller;
    }

    // ── Service stubs, each capturing the officeId it was handed ───────────────

    private static BudgetCeilingDto Ceiling(int officeId)
        => new(1, officeId, FiscalYear, FundingSource, "GF", "General Fund", 0m);

    private static DivisionAllocationDto Allocation()
        => new(1, DivisionId, "Planning", FiscalYear, FundingSource, "GF", "General Fund", 0m);

    /// <summary>
    /// Every read endpoint, keyed by the name used in the theory rows. Each entry sets up its
    /// service stub to record the officeId the handler resolved, then invokes the handler with a
    /// query string that asks for <see cref="ForeignOffice"/>.
    /// </summary>
    private async Task<(HttpResponseData response, int officeId)> ReadAsync(string endpoint)
    {
        int captured = -1;
        string query;
        Func<FakeHttpRequestData, Task<HttpResponseData>> invoke;

        switch (endpoint)
        {
            case "ceiling":
                query = $"officeId={ForeignOffice}&fiscalYear={FiscalYear}&fundingSourceId={FundingSource}";
                _allocation.Setup(s => s.GetCeilingAsync(
                        It.IsAny<int>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
                    .Callback((int o, int _, int _, CancellationToken _) => captured = o)
                    .ReturnsAsync((int o, int _, int _, CancellationToken _) =>
                        ServiceResult<BudgetCeilingDto>.Ok(Ceiling(o)));
                invoke = r => Sut.GetCeiling(r, CancellationToken.None);
                break;

            case "ceilings":
                query = $"officeId={ForeignOffice}&fiscalYear={FiscalYear}";
                _allocation.Setup(s => s.GetCeilingsAsync(
                        It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
                    .Callback((int o, int _, CancellationToken _) => captured = o)
                    .ReturnsAsync(Array.Empty<BudgetCeilingDto>());
                invoke = r => Sut.GetCeilings(r, CancellationToken.None);
                break;

            case "divisions":
                query = $"officeId={ForeignOffice}&fiscalYear={FiscalYear}&fundingSourceId={FundingSource}";
                _allocation.Setup(s => s.GetAllocationsAsync(
                        It.IsAny<int>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
                    .Callback((int o, int _, int _, CancellationToken _) => captured = o)
                    .ReturnsAsync(new[] { Allocation() });
                invoke = r => Sut.GetDivisions(r, CancellationToken.None);
                break;

            case "divisions/all-funds":
                query = $"officeId={ForeignOffice}&fiscalYear={FiscalYear}";
                _allocation.Setup(s => s.GetAllocationsForAllFundsAsync(
                        It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
                    .Callback((int o, int _, CancellationToken _) => captured = o)
                    .ReturnsAsync(new[] { Allocation() });
                invoke = r => Sut.GetDivisionsAllFunds(r, CancellationToken.None);
                break;

            case "programs":
                query = $"officeId={ForeignOffice}&fiscalYear={FiscalYear}";
                _allocation.Setup(s => s.GetProgramAssignmentsAsync(
                        It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
                    .Callback((int o, int _, CancellationToken _) => captured = o)
                    .ReturnsAsync(Array.Empty<ProgramAssignmentDto>());
                invoke = r => Sut.GetPrograms(r, CancellationToken.None);
                break;

            case "status":
                query = $"officeId={ForeignOffice}&fiscalYear={FiscalYear}&divisionId={DivisionId}";
                _allocation.Setup(s => s.GetSetupStatusAsync(
                        It.IsAny<int>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
                    .Callback((int o, int _, int _, CancellationToken _) => captured = o)
                    .ReturnsAsync(new AllocationSetupStatusDto(false, false, false));
                invoke = r => Sut.GetStatus(r, CancellationToken.None);
                break;

            default:
                throw new ArgumentOutOfRangeException(nameof(endpoint), endpoint, "Unknown endpoint.");
        }

        HttpResponseData response = await invoke(
            FunctionHttp.Get(query, path: $"budget-planning/allocation/{endpoint}"));

        return (response, captured);
    }

    // ── The clamp, per endpoint × per caller kind ─────────────────────────────

    /// <summary>
    /// The leak, and its fix. A plain office user asking for another office gets their own; a
    /// host-office caller and a PBO ceiling holder are passed through untouched.
    /// </summary>
    [Theory]
    [InlineData("ceiling",             Caller.PlainOfficeUser)]
    [InlineData("ceiling",             Caller.HostOffice)]
    [InlineData("ceiling",             Caller.PboCeilingHolder)]
    [InlineData("ceilings",            Caller.PlainOfficeUser)]
    [InlineData("ceilings",            Caller.HostOffice)]
    [InlineData("ceilings",            Caller.PboCeilingHolder)]
    [InlineData("divisions",           Caller.PlainOfficeUser)]
    [InlineData("divisions",           Caller.HostOffice)]
    [InlineData("divisions",           Caller.PboCeilingHolder)]
    [InlineData("divisions/all-funds", Caller.PlainOfficeUser)]
    [InlineData("divisions/all-funds", Caller.HostOffice)]
    [InlineData("divisions/all-funds", Caller.PboCeilingHolder)]
    [InlineData("programs",            Caller.PlainOfficeUser)]
    [InlineData("programs",            Caller.HostOffice)]
    [InlineData("programs",            Caller.PboCeilingHolder)]
    [InlineData("status",              Caller.PlainOfficeUser)]
    [InlineData("status",              Caller.HostOffice)]
    [InlineData("status",              Caller.PboCeilingHolder)]
    public async Task Get_WithAForeignOfficeIdQuery_ReachesTheServiceWithTheClampedOffice(
        string endpoint, Caller kind)
    {
        Authenticate(kind);

        (HttpResponseData response, int officeId) = await ReadAsync(endpoint);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(ExpectedOfficeFor(kind), officeId);
    }

    /// <summary>
    /// An office user asking for their OWN office is unaffected — the clamp is a substitution,
    /// not a narrowing of what they could already see.
    /// </summary>
    [Fact]
    public async Task GetCeilings_AsOfficeUser_RequestingOwnOffice_IsUnchanged()
    {
        Authenticate(Caller.PlainOfficeUser);
        int captured = -1;
        _allocation.Setup(s => s.GetCeilingsAsync(
                It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .Callback((int o, int _, CancellationToken _) => captured = o)
            .ReturnsAsync(Array.Empty<BudgetCeilingDto>());

        HttpResponseData response = await Sut.GetCeilings(
            FunctionHttp.Get($"officeId={OwnOffice}&fiscalYear={FiscalYear}",
                path: "budget-planning/allocation/ceilings"),
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(OwnOffice, captured);
    }

    // ── The permission gate is untouched ──────────────────────────────────────
    // PPDO-18 adds a clamp; it does not narrow a grant. Every WFP user still reaches these reads,
    // because the setup-complete gate the entry wizard depends on is built from them.

    [Fact]
    public async Task GetStatus_WithInvalidToken_ReturnsUnauthorizedAndNeverCallsService()
    {
        _jwt.Setup(j => j.ValidateAsync(It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((User?)null);

        HttpResponseData response = await Sut.GetStatus(
            FunctionHttp.Get($"officeId={OwnOffice}&fiscalYear={FiscalYear}&divisionId={DivisionId}",
                path: "budget-planning/allocation/status"),
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        _allocation.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task GetStatus_WithoutBudgetPlanningPermission_ReturnsForbiddenAndNeverCallsService()
    {
        Authenticate(Caller.PlainOfficeUser, canAccessBudgetPlanning: false);

        HttpResponseData response = await Sut.GetStatus(
            FunctionHttp.Get($"officeId={OwnOffice}&fiscalYear={FiscalYear}&divisionId={DivisionId}",
                path: "budget-planning/allocation/status"),
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        _allocation.VerifyNoOtherCalls();
    }

    /// <summary>
    /// The clamp must not swallow the pre-existing parameter validation — a missing or malformed
    /// officeId is still a 400, not a silent fallback to the caller's own office.
    /// </summary>
    [Theory]
    [InlineData("fiscalYear=2027")]
    [InlineData("officeId=abc&fiscalYear=2027")]
    [InlineData("officeId=3")]
    public async Task GetCeilings_WithMissingOrMalformedQuery_StillReturnsBadRequest(string query)
    {
        Authenticate(Caller.PlainOfficeUser);

        HttpResponseData response = await Sut.GetCeilings(
            FunctionHttp.Get(query, path: "budget-planning/allocation/ceilings"),
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        _allocation.VerifyNoOtherCalls();
    }

    // ── Writes — the ceiling PUT keeps its cross-office reach ─────────────────
    // CanManagePboCeiling IS authority over any office's ceiling (RAL-243), so this endpoint is
    // deliberately not office-scoped. The gate is the grant. These two tests are what would fail
    // if someone "consistently" clamped every write in the file.

    [Fact]
    public async Task UpsertCeiling_AsPboHolderInAGuestOffice_WritesTheRequestedForeignOffice()
    {
        User caller = Authenticate(Caller.PboCeilingHolder);
        _permissions.Setup(p => p.CanReviewAllOfficesAsync(caller, It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
        int captured = -1;
        _allocation.Setup(s => s.UpsertCeilingAsync(
                It.IsAny<int>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<decimal>(),
                It.IsAny<CancellationToken>()))
            .Callback((int o, int _, int _, decimal _, CancellationToken _) => captured = o)
            .ReturnsAsync((int o, int _, int _, decimal _, CancellationToken _) =>
                ServiceResult<BudgetCeilingDto>.Ok(Ceiling(o)));

        HttpResponseData response = await Sut.UpsertCeiling(
            FunctionHttp.Put(new UpsertCeilingDto(ForeignOffice, FiscalYear, FundingSource, 1_000m)),
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(ForeignOffice, captured);
    }

    [Fact]
    public async Task UpsertCeiling_WithoutThePboGrant_ReturnsForbiddenAndNeverCallsService()
    {
        Authenticate(Caller.PlainOfficeUser);

        HttpResponseData response = await Sut.UpsertCeiling(
            FunctionHttp.Put(new UpsertCeilingDto(OwnOffice, FiscalYear, FundingSource, 1_000m)),
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        _allocation.VerifyNoOtherCalls();
    }

    // ── Writes — the PPDO allocation PUTs are host-office only ───────────────────
    // CanManagePpdoAllocation is EXCLUSIVE to PPDO users (confirmed 2026-09-02, after a live
    // account -- pto.user, Provincial Treasurer's Office -- was found holding it by mistake). So a
    // guest-office holder is a mis-grant, and both endpoints refuse them outright rather than
    // letting them write their own office. That way the endpoint stops depending on the grant
    // being administered correctly, which is the thing that actually went wrong.

    private void AllowWrite(User caller)
        => _permissions.Setup(p => p.CanReviewAllOfficesAsync(caller, It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

    [Fact]
    public async Task UpsertDivisions_AsHostOfficeCaller_WritesTheRequestedOffice()
    {
        User caller = Authenticate(Caller.HostOffice, canManagePpdoAllocation: true);
        AllowWrite(caller);
        int captured = -1;
        _allocation.Setup(s => s.UpsertAllocationsAsync(
                It.IsAny<int>(), It.IsAny<int>(), It.IsAny<int>(),
                It.IsAny<IReadOnlyList<UpsertDivisionAllocationDto>>(), It.IsAny<CancellationToken>()))
            .Callback((int o, int _, int _, IReadOnlyList<UpsertDivisionAllocationDto> _, CancellationToken _)
                => captured = o)
            .ReturnsAsync(ServiceResult<IReadOnlyList<DivisionAllocationDto>>.Ok(new[] { Allocation() }));

        HttpResponseData response = await Sut.UpsertDivisions(
            FunctionHttp.Put(
                new UpsertAllocationsDto(ForeignOffice, FiscalYear, FundingSource,
                    new[] { new UpsertDivisionAllocationDto(DivisionId, 500m) }),
                path: "budget-planning/allocation/divisions"),
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(ForeignOffice, captured);
    }

    [Theory]
    [InlineData(Caller.PlainOfficeUser)]
    [InlineData(Caller.PboCeilingHolder)]
    public async Task UpsertDivisions_AsAnyGuestOfficeCaller_TargetingAForeignOffice_ReturnsForbidden(
        Caller kind)
    {
        User caller = Authenticate(kind, canManagePpdoAllocation: true);
        AllowWrite(caller);

        HttpResponseData response = await Sut.UpsertDivisions(
            FunctionHttp.Put(
                new UpsertAllocationsDto(ForeignOffice, FiscalYear, FundingSource,
                    new[] { new UpsertDivisionAllocationDto(DivisionId, 500m) }),
                path: "budget-planning/allocation/divisions"),
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        _allocation.VerifyNoOtherCalls();
    }

    /// <summary>
    /// The case that changed once the grant's meaning was settled. An earlier cut of PPDO-18 only
    /// refused a FOREIGN office, which left a mis-granted account -- the real pto.user -- able to
    /// write its own office's allocations through a PPDO-exclusive endpoint. A guest-office holder
    /// is now refused for every office, their own included.
    /// </summary>
    [Fact]
    public async Task UpsertDivisions_AsOfficeUser_TargetingOwnOffice_IsAlsoForbidden()
    {
        User caller = Authenticate(Caller.PlainOfficeUser, canManagePpdoAllocation: true);
        AllowWrite(caller);

        HttpResponseData response = await Sut.UpsertDivisions(
            FunctionHttp.Put(
                new UpsertAllocationsDto(OwnOffice, FiscalYear, FundingSource,
                    new[] { new UpsertDivisionAllocationDto(DivisionId, 500m) }),
                path: "budget-planning/allocation/divisions"),
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        _allocation.VerifyNoOtherCalls();
    }

    /// <summary>
    /// The PPA assignment payload carries no office id at all — the office is resolved from
    /// <c>OfficeRefCode</c> inside the service — so there is nothing to clamp or compare here.
    /// The only office authority assertable at this layer is host-office, which is exactly what
    /// this grant is; the same idiom as <c>GetBudgetPlanningDashboard</c> (RAL-230).
    /// </summary>
    [Fact]
    public async Task UpsertProgram_AsHostOfficeCaller_IsAllowed()
    {
        User caller = Authenticate(Caller.HostOffice, canManagePpdoAllocation: true);
        AllowWrite(caller);
        _allocation.Setup(s => s.UpsertProgramAssignmentAsync(
                It.IsAny<UpsertProgramAssignmentDto>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ServiceResult<ProgramAssignmentDto>.Ok(
                new ProgramAssignmentDto("PPDO", "1000", "Program", "Social", new[] { DivisionId })));

        HttpResponseData response = await Sut.UpsertProgram(
            FunctionHttp.Put(
                new UpsertProgramAssignmentDto("PPDO", "1000", new[] { DivisionId }),
                path: "budget-planning/allocation/programs"),
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Theory]
    [InlineData(Caller.PlainOfficeUser)]
    [InlineData(Caller.PboCeilingHolder)]
    public async Task UpsertProgram_AsAnyGuestOfficeCaller_ReturnsForbiddenAndNeverCallsService(Caller kind)
    {
        User caller = Authenticate(kind, canManagePpdoAllocation: true);
        AllowWrite(caller);

        HttpResponseData response = await Sut.UpsertProgram(
            FunctionHttp.Put(
                new UpsertProgramAssignmentDto("GSO", "1000", new[] { DivisionId }),
                path: "budget-planning/allocation/programs"),
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        _allocation.VerifyNoOtherCalls();
    }

    /// <summary>
    /// The office guard runs after the body read, so a malformed body is still a 400 — the same
    /// ordering rule the GETs follow for a malformed officeId.
    /// </summary>
    [Fact]
    public async Task UpsertDivisions_WithMalformedBody_StillReturnsBadRequest()
    {
        User caller = Authenticate(Caller.HostOffice, canManagePpdoAllocation: true);
        AllowWrite(caller);

        HttpResponseData response = await Sut.UpsertDivisions(
            FunctionHttp.Put("{ not json", path: "budget-planning/allocation/divisions"),
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        _allocation.VerifyNoOtherCalls();
    }
}
