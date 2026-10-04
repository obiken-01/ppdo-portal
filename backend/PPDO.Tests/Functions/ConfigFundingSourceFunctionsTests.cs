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
/// Endpoint tests for per-office fund sources (v1.8.0 — PPDO-109).
///
/// The rules this file exists for: a department head holding <c>CanManageOfficeSetup</c> sees the
/// shared funds plus their own office's, may write only their own, and can never touch a
/// province-wide row. The picker's <c>?officeId=</c> is honoured as far as the caller's scope allows
/// and clamped otherwise.
///
/// ⚠️ Every case is paired with the config manager's, because the failure that matters is not "the
/// department head got too little" but "the config manager quietly got less" — the same reason
/// <c>ConfigDivisionFunctionsTests</c> is written in pairs.
/// </summary>
public sealed class ConfigFundingSourceFunctionsTests
{
    private const int OwnOffice     = 3;
    private const int ForeignOffice = 99;

    private readonly Mock<IFundingSourceService> _funding     = new(MockBehavior.Strict);
    private readonly Mock<IJwtValidator>        _jwt         = new(MockBehavior.Strict);
    private readonly Mock<IPermissionService>    _permissions = new(MockBehavior.Loose);

    private ConfigFundingSourceFunctions Sut => new(_funding.Object, _jwt.Object, _permissions.Object);

    /// <summary>
    /// A user in <paramref name="hostOffice"/>'s office or their own. The host-office flag is what
    /// <c>OfficeScope</c> reads for cross-office authority (DECISION F), and it is read off the
    /// Office NAVIGATION — a user whose Office is not loaded is scoped to their own office, which is
    /// the deliberate safe degradation.
    /// </summary>
    /// <remarks>
    /// ⚠️ <paramref name="unassigned"/> is a separate flag rather than <c>officeId: null</c>, because
    /// an office id of null is exactly the state under test and a nullable parameter with a fallback
    /// silently turns it back into the default — which is how the first draft of
    /// <see cref="Create_AsDepartmentHeadWithNoOffice_ReturnsForbidden"/> passed an office 3 user in
    /// and never reached the branch it was written for.
    /// </remarks>
    private static User MakeUser(bool hostOffice, bool unassigned = false)
    {
        int officeId = hostOffice ? 1 : OwnOffice;
        return new User
        {
            Id = Guid.NewGuid(), FullName = "Test", Username = "test", PasswordHash = "hash",
            Role = UserRole.Staff,
            OfficeId = unassigned ? null : officeId,
            Office = unassigned ? null : new Office
            {
                Id = officeId,
                OfficeCode = hostOffice ? "PPDO" : "GSO",
                OfficeName = hostOffice ? "Provincial Planning" : "General Services Office",
                IsActive = true, IsHostOffice = hostOffice,
            },
        };
    }

    /// <summary>A department head: the setup grant, and no config grant.</summary>
    private User AuthenticateDepartmentHead(bool unassigned = false)
    {
        User caller = MakeUser(hostOffice: false, unassigned);
        _jwt.Setup(j => j.ValidateAsync(It.IsAny<string?>(), It.IsAny<CancellationToken>())).ReturnsAsync(caller);
        _permissions.Setup(p => p.CanManageConfigAsync(caller, It.IsAny<CancellationToken>())).ReturnsAsync(false);
        _permissions.Setup(p => p.CanManageOfficeSetupAsync(caller, It.IsAny<CancellationToken>())).ReturnsAsync(true);
        return caller;
    }

    /// <summary>A config manager, with or without the setup grant on top.</summary>
    private User AuthenticateConfigManager(bool alsoOfficeSetup = false)
    {
        User caller = MakeUser(hostOffice: true);
        _jwt.Setup(j => j.ValidateAsync(It.IsAny<string?>(), It.IsAny<CancellationToken>())).ReturnsAsync(caller);
        _permissions.Setup(p => p.CanManageConfigAsync(caller, It.IsAny<CancellationToken>())).ReturnsAsync(true);
        _permissions.Setup(p => p.CanManageOfficeSetupAsync(caller, It.IsAny<CancellationToken>())).ReturnsAsync(alsoOfficeSetup);
        return caller;
    }

    /// <summary>A plain encoder: neither grant. Still reads the list — it feeds the WFP/AIP picker.</summary>
    private User AuthenticateEncoder()
    {
        User caller = MakeUser(hostOffice: false);
        _jwt.Setup(j => j.ValidateAsync(It.IsAny<string?>(), It.IsAny<CancellationToken>())).ReturnsAsync(caller);
        _permissions.Setup(p => p.CanManageConfigAsync(caller, It.IsAny<CancellationToken>())).ReturnsAsync(false);
        _permissions.Setup(p => p.CanManageOfficeSetupAsync(caller, It.IsAny<CancellationToken>())).ReturnsAsync(false);
        return caller;
    }

    private static FundingSourceDto Fund(int id, int? officeId, string code = "GSOX") =>
        new(id, code, "A fund", null, null, true, null, officeId,
            officeId is null ? null : "GSO", officeId is null ? null : "General Services Office");

    private static UpsertFundingSourceDto Body(int? officeId) =>
        new("GSOX", "GSO Motorpool Fund", null, OfficeId: officeId);

    // ── List / read scope ─────────────────────────────────────────────────────

    /// <summary>The leak this clamp closes: asking for another office's funds by query string.</summary>
    [Fact]
    public async Task List_AsDepartmentHead_ReadsTheirOwnOfficeWhateverTheyAskedFor()
    {
        AuthenticateDepartmentHead();
        int? captured = -1;
        _funding.Setup(s => s.GetAllAsync(
                It.IsAny<string?>(), It.IsAny<ActiveFilter>(), It.IsAny<int?>(), It.IsAny<CancellationToken>()))
            .Callback((string? _, ActiveFilter _, int? officeId, CancellationToken _) => captured = officeId)
            .ReturnsAsync(new[] { Fund(1, null, "GF") });

        HttpResponseData response = await Sut.List(
            FunctionHttp.Get($"officeId={ForeignOffice}", path: "config/funding-sources"), CancellationToken.None);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(OwnOffice, captured);
    }

    /// <summary>
    /// ⚠️ The same clamp applies to a caller with NEITHER grant, because this route is the WFP/AIP
    /// fund picker and every encoder hits it. If the clamp only covered grant-holders, an ordinary
    /// encoder in one office would see every other office's funds in their dropdown.
    /// </summary>
    [Fact]
    public async Task List_AsPlainEncoder_IsAlsoClampedToTheirOwnOffice()
    {
        AuthenticateEncoder();
        int? captured = -1;
        _funding.Setup(s => s.GetAllAsync(
                It.IsAny<string?>(), It.IsAny<ActiveFilter>(), It.IsAny<int?>(), It.IsAny<CancellationToken>()))
            .Callback((string? _, ActiveFilter _, int? officeId, CancellationToken _) => captured = officeId)
            .ReturnsAsync(Array.Empty<FundingSourceDto>());

        await Sut.List(
            FunctionHttp.Get($"officeId={ForeignOffice}", path: "config/funding-sources"), CancellationToken.None);

        Assert.Equal(OwnOffice, captured);
    }

    [Fact]
    public async Task List_AsOfficeUserWithNoOfficeIdAsked_StillScopesToTheirOwnOffice()
    {
        // The picker may not pass an officeId at all (an older client, or a page with no record in
        // hand). "No office named" must not fall through to "every office".
        AuthenticateDepartmentHead();
        int? captured = -1;
        _funding.Setup(s => s.GetAllAsync(
                It.IsAny<string?>(), It.IsAny<ActiveFilter>(), It.IsAny<int?>(), It.IsAny<CancellationToken>()))
            .Callback((string? _, ActiveFilter _, int? officeId, CancellationToken _) => captured = officeId)
            .ReturnsAsync(Array.Empty<FundingSourceDto>());

        await Sut.List(FunctionHttp.Get("", path: "config/funding-sources"), CancellationToken.None);

        Assert.Equal(OwnOffice, captured);
    }

    [Fact]
    public async Task List_AsConfigManagerWithNoOfficeIdAsked_ReadsEveryOffice()
    {
        // Null means "no filter" — the Configuration page's cross-office view.
        AuthenticateConfigManager();
        int? captured = -1;
        _funding.Setup(s => s.GetAllAsync(
                It.IsAny<string?>(), It.IsAny<ActiveFilter>(), It.IsAny<int?>(), It.IsAny<CancellationToken>()))
            .Callback((string? _, ActiveFilter _, int? officeId, CancellationToken _) => captured = officeId)
            .ReturnsAsync(Array.Empty<FundingSourceDto>());

        await Sut.List(FunctionHttp.Get("", path: "config/funding-sources"), CancellationToken.None);

        Assert.Null(captured);
    }

    /// <summary>
    /// The case <c>?officeId=</c> exists for: a PPDO reviewer opening another office's AIP needs
    /// THAT office's funds in the picker, resolved from the record rather than from their own
    /// account.
    /// </summary>
    [Fact]
    public async Task List_AsHostOfficeUser_HonoursTheRequestedOffice()
    {
        AuthenticateConfigManager();
        int? captured = -1;
        _funding.Setup(s => s.GetAllAsync(
                It.IsAny<string?>(), It.IsAny<ActiveFilter>(), It.IsAny<int?>(), It.IsAny<CancellationToken>()))
            .Callback((string? _, ActiveFilter _, int? officeId, CancellationToken _) => captured = officeId)
            .ReturnsAsync(Array.Empty<FundingSourceDto>());

        await Sut.List(
            FunctionHttp.Get($"officeId={ForeignOffice}", path: "config/funding-sources"), CancellationToken.None);

        Assert.Equal(ForeignOffice, captured);
    }

    [Fact]
    public async Task List_AsConfigManagerWhoAlsoHoldsTheSetupGrant_KeepsTheCrossOfficeView()
    {
        // ⚠️ The regression this pins: asking "holds the setup grant?" instead of "is not a config
        // manager?" would take every other office's funds away from a PPDO admin the moment someone
        // granted them the new flag.
        AuthenticateConfigManager(alsoOfficeSetup: true);
        int? captured = -1;
        _funding.Setup(s => s.GetAllAsync(
                It.IsAny<string?>(), It.IsAny<ActiveFilter>(), It.IsAny<int?>(), It.IsAny<CancellationToken>()))
            .Callback((string? _, ActiveFilter _, int? officeId, CancellationToken _) => captured = officeId)
            .ReturnsAsync(Array.Empty<FundingSourceDto>());

        await Sut.List(FunctionHttp.Get("", path: "config/funding-sources"), CancellationToken.None);

        Assert.Null(captured);
    }

    [Fact]
    public async Task List_Unauthenticated_ReturnsUnauthorized()
    {
        _jwt.Setup(j => j.ValidateAsync(It.IsAny<string?>(), It.IsAny<CancellationToken>())).ReturnsAsync((User?)null);

        HttpResponseData response = await Sut.List(
            FunctionHttp.Get("", path: "config/funding-sources"), CancellationToken.None);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    // ── Get by id ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task Get_AsDepartmentHead_ReadsASharedFund()
    {
        // Shared funds are READ-only, not read-forbidden: the config page renders them greyed out.
        AuthenticateDepartmentHead();
        _funding.Setup(s => s.GetByIdAsync(1, It.IsAny<CancellationToken>()))
            .ReturnsAsync(ServiceResult<FundingSourceDto>.Ok(Fund(1, null, "GF")));

        HttpResponseData response = await Sut.Get(
            FunctionHttp.Get("", path: "config/funding-sources/1"), 1, CancellationToken.None);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Get_AsDepartmentHead_ReadingAnotherOfficesFund_ReturnsForbidden()
    {
        AuthenticateDepartmentHead();
        _funding.Setup(s => s.GetByIdAsync(4, It.IsAny<CancellationToken>()))
            .ReturnsAsync(ServiceResult<FundingSourceDto>.Ok(Fund(4, ForeignOffice)));

        HttpResponseData response = await Sut.Get(
            FunctionHttp.Get("", path: "config/funding-sources/4"), 4, CancellationToken.None);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    // ── Create ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Create_AsDepartmentHead_StampsTheirOwnOffice_IgnoringTheBody()
    {
        AuthenticateDepartmentHead();
        UpsertFundingSourceDto? captured = null;
        _funding.Setup(s => s.CreateAsync(It.IsAny<UpsertFundingSourceDto>(), It.IsAny<CancellationToken>()))
            .Callback((UpsertFundingSourceDto dto, CancellationToken _) => captured = dto)
            .ReturnsAsync(ServiceResult<FundingSourceDto>.Ok(Fund(9, OwnOffice)));

        HttpResponseData response = await Sut.Create(
            FunctionHttp.Post("config/funding-sources", Body(ForeignOffice)), CancellationToken.None);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Equal(OwnOffice, captured!.OfficeId);   // not ForeignOffice
    }

    [Fact]
    public async Task Create_AsDepartmentHead_CannotCreateASharedFund()
    {
        // ⚠️ A null office id in the body means "province-wide". Passing it through would let a
        // department head add to PPDO's list, which is the one thing D5 keeps for PPDO.
        AuthenticateDepartmentHead();
        UpsertFundingSourceDto? captured = null;
        _funding.Setup(s => s.CreateAsync(It.IsAny<UpsertFundingSourceDto>(), It.IsAny<CancellationToken>()))
            .Callback((UpsertFundingSourceDto dto, CancellationToken _) => captured = dto)
            .ReturnsAsync(ServiceResult<FundingSourceDto>.Ok(Fund(9, OwnOffice)));

        await Sut.Create(
            FunctionHttp.Post("config/funding-sources", Body(officeId: null)), CancellationToken.None);

        Assert.Equal(OwnOffice, captured!.OfficeId);
    }

    [Fact]
    public async Task Create_AsDepartmentHeadWithNoOffice_ReturnsForbidden()
    {
        // DECISION F — an unassigned user sees nothing rather than everything, so there is no office
        // to stamp and nothing to create into.
        AuthenticateDepartmentHead(unassigned: true);

        HttpResponseData response = await Sut.Create(
            FunctionHttp.Post("config/funding-sources", Body(OwnOffice)), CancellationToken.None);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        _funding.Verify(s => s.CreateAsync(It.IsAny<UpsertFundingSourceDto>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Create_AsConfigManager_KeepsTheOfficeIdTheySent()
    {
        // PPDO may create a fund on an office's behalf — the body is theirs to set.
        AuthenticateConfigManager();
        UpsertFundingSourceDto? captured = null;
        _funding.Setup(s => s.CreateAsync(It.IsAny<UpsertFundingSourceDto>(), It.IsAny<CancellationToken>()))
            .Callback((UpsertFundingSourceDto dto, CancellationToken _) => captured = dto)
            .ReturnsAsync(ServiceResult<FundingSourceDto>.Ok(Fund(9, ForeignOffice)));

        await Sut.Create(
            FunctionHttp.Post("config/funding-sources", Body(ForeignOffice)), CancellationToken.None);

        Assert.Equal(ForeignOffice, captured!.OfficeId);
    }

    [Fact]
    public async Task Create_WithNeitherGrant_ReturnsForbidden()
    {
        AuthenticateEncoder();

        HttpResponseData response = await Sut.Create(
            FunctionHttp.Post("config/funding-sources", Body(OwnOffice)), CancellationToken.None);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    // ── Update ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Update_AsDepartmentHead_OnTheirOwnFund_IsAllowed()
    {
        AuthenticateDepartmentHead();
        _funding.Setup(s => s.GetByIdAsync(9, It.IsAny<CancellationToken>()))
            .ReturnsAsync(ServiceResult<FundingSourceDto>.Ok(Fund(9, OwnOffice)));
        _funding.Setup(s => s.UpdateAsync(9, It.IsAny<UpsertFundingSourceDto>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ServiceResult<FundingSourceDto>.Ok(Fund(9, OwnOffice)));

        HttpResponseData response = await Sut.Update(
            FunctionHttp.Put(Body(OwnOffice), path: "config/funding-sources/9"), 9, CancellationToken.None);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    /// <summary>
    /// ⚠️ The core rule of PPDO-109: the province-wide list is PPDO's. A department head editing GF
    /// would change what every office sees.
    /// </summary>
    /// <summary>
    /// ⚠️ PPDO-128: the service now READS OfficeId on update, so the handler must pin a department
    /// head's value — otherwise a null in the body would publish their private fund to every office.
    /// </summary>
    [Theory]
    [InlineData(null)]
    [InlineData(ForeignOffice)]
    public async Task Update_AsDepartmentHead_PinsTheirOwnOffice_IgnoringTheBody(int? bodyOfficeId)
    {
        AuthenticateDepartmentHead();
        UpsertFundingSourceDto? captured = null;
        _funding.Setup(s => s.GetByIdAsync(9, It.IsAny<CancellationToken>()))
            .ReturnsAsync(ServiceResult<FundingSourceDto>.Ok(Fund(9, OwnOffice)));
        _funding.Setup(s => s.UpdateAsync(9, It.IsAny<UpsertFundingSourceDto>(), It.IsAny<CancellationToken>()))
            .Callback((int _, UpsertFundingSourceDto dto, CancellationToken _) => captured = dto)
            .ReturnsAsync(ServiceResult<FundingSourceDto>.Ok(Fund(9, OwnOffice)));

        await Sut.Update(
            FunctionHttp.Put(Body(bodyOfficeId), path: "config/funding-sources/9"), 9, CancellationToken.None);

        Assert.Equal(OwnOffice, captured!.OfficeId);
    }

    [Fact]
    public async Task Update_AsConfigManager_KeepsTheOfficeIdTheySent()
    {
        // PPDO-128 — PPDO may move a fund between shared and office-owned, so its body passes through.
        AuthenticateConfigManager();
        UpsertFundingSourceDto? captured = null;
        _funding.Setup(s => s.UpdateAsync(1, It.IsAny<UpsertFundingSourceDto>(), It.IsAny<CancellationToken>()))
            .Callback((int _, UpsertFundingSourceDto dto, CancellationToken _) => captured = dto)
            .ReturnsAsync(ServiceResult<FundingSourceDto>.Ok(Fund(1, ForeignOffice, "GF")));

        await Sut.Update(
            FunctionHttp.Put(Body(ForeignOffice), path: "config/funding-sources/1"), 1, CancellationToken.None);

        Assert.Equal(ForeignOffice, captured!.OfficeId);
    }

    [Fact]
    public async Task Update_AsDepartmentHead_OnASharedFund_ReturnsForbidden()
    {
        AuthenticateDepartmentHead();
        _funding.Setup(s => s.GetByIdAsync(1, It.IsAny<CancellationToken>()))
            .ReturnsAsync(ServiceResult<FundingSourceDto>.Ok(Fund(1, null, "GF")));

        HttpResponseData response = await Sut.Update(
            FunctionHttp.Put(Body(null), path: "config/funding-sources/1"), 1, CancellationToken.None);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        _funding.Verify(s => s.UpdateAsync(
            It.IsAny<int>(), It.IsAny<UpsertFundingSourceDto>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Update_AsDepartmentHead_OnAnotherOfficesFund_ReturnsForbidden()
    {
        AuthenticateDepartmentHead();
        _funding.Setup(s => s.GetByIdAsync(4, It.IsAny<CancellationToken>()))
            .ReturnsAsync(ServiceResult<FundingSourceDto>.Ok(Fund(4, ForeignOffice)));

        HttpResponseData response = await Sut.Update(
            FunctionHttp.Put(Body(ForeignOffice), path: "config/funding-sources/4"), 4, CancellationToken.None);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Update_AsDepartmentHead_OnAFundThatDoesNotExist_ReturnsForbiddenNotNotFound()
    {
        // ⚠️ 403, not 404 — otherwise the status code is an oracle for which ids exist outside the
        // caller's office. Same rule as ConfigDivisionFunctions.DenyForeignDivisionAsync.
        AuthenticateDepartmentHead();
        _funding.Setup(s => s.GetByIdAsync(404, It.IsAny<CancellationToken>()))
            .ReturnsAsync(ServiceResult<FundingSourceDto>.NotFound("Funding source 404 not found."));

        HttpResponseData response = await Sut.Update(
            FunctionHttp.Put(Body(OwnOffice), path: "config/funding-sources/404"), 404, CancellationToken.None);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Update_AsConfigManager_OnASharedFund_IsAllowed()
    {
        AuthenticateConfigManager();
        _funding.Setup(s => s.UpdateAsync(1, It.IsAny<UpsertFundingSourceDto>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ServiceResult<FundingSourceDto>.Ok(Fund(1, null, "GF")));

        HttpResponseData response = await Sut.Update(
            FunctionHttp.Put(Body(null), path: "config/funding-sources/1"), 1, CancellationToken.None);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        // No GetByIdAsync probe at all — a config manager never reaches the ownership check.
        _funding.Verify(s => s.GetByIdAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    // ── Ownership impact (PPDO-128) ───────────────────────────────────────────

    [Fact]
    public async Task OwnershipImpact_AsConfigManager_PassesTheTargetOffice()
    {
        AuthenticateConfigManager();
        int? captured = -1;
        _funding.Setup(s => s.GetOwnershipImpactAsync(1, It.IsAny<int?>(), It.IsAny<CancellationToken>()))
            .Callback((int _, int? officeId, CancellationToken _) => captured = officeId)
            .ReturnsAsync(ServiceResult<FundOwnershipImpactDto>.Ok(new FundOwnershipImpactDto(0, [])));

        HttpResponseData response = await Sut.OwnershipImpact(
            FunctionHttp.Get($"officeId={ForeignOffice}", path: "config/funding-sources/1/ownership-impact"),
            1, CancellationToken.None);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(ForeignOffice, captured);
    }

    /// <summary>
    /// ⚠️ It reports on OTHER offices' data, and only a config manager can change ownership anyway.
    /// </summary>
    [Fact]
    public async Task OwnershipImpact_AsDepartmentHead_ReturnsForbidden()
    {
        AuthenticateDepartmentHead();

        HttpResponseData response = await Sut.OwnershipImpact(
            FunctionHttp.Get($"officeId={OwnOffice}", path: "config/funding-sources/9/ownership-impact"),
            9, CancellationToken.None);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        _funding.Verify(s => s.GetOwnershipImpactAsync(
            It.IsAny<int>(), It.IsAny<int?>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    // ── Delete ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Delete_AsDepartmentHead_OnTheirOwnFund_PassesTheUsageGuard()
    {
        AuthenticateDepartmentHead();
        _funding.Setup(s => s.GetByIdAsync(9, It.IsAny<CancellationToken>()))
            .ReturnsAsync(ServiceResult<FundingSourceDto>.Ok(Fund(9, OwnOffice)));
        bool? guard = null;
        _funding.Setup(s => s.DeleteAsync(9, It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .Callback((int _, bool block, CancellationToken _) => guard = block)
            .ReturnsAsync(ServiceResult<FundingSourceDto>.Ok(Fund(9, OwnOffice)));

        HttpResponseData response = await Sut.Delete(
            FunctionHttp.Get("", path: "config/funding-sources/9"), 9, CancellationToken.None);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(guard);
    }

    [Fact]
    public async Task Delete_AsConfigManager_DoesNotPassTheUsageGuard()
    {
        // ⚠️ Soft delete stays unconditional for PPDO: retiring a fund from the pickers while its
        // history keeps resolving is what soft delete is FOR. See IFundingSourceService.DeleteAsync.
        AuthenticateConfigManager();
        bool? guard = null;
        _funding.Setup(s => s.DeleteAsync(1, It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .Callback((int _, bool block, CancellationToken _) => guard = block)
            .ReturnsAsync(ServiceResult<FundingSourceDto>.Ok(Fund(1, null, "GF")));

        HttpResponseData response = await Sut.Delete(
            FunctionHttp.Get("", path: "config/funding-sources/1"), 1, CancellationToken.None);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.False(guard);
    }

    [Fact]
    public async Task Delete_AsDepartmentHead_OnASharedFund_ReturnsForbidden()
    {
        AuthenticateDepartmentHead();
        _funding.Setup(s => s.GetByIdAsync(1, It.IsAny<CancellationToken>()))
            .ReturnsAsync(ServiceResult<FundingSourceDto>.Ok(Fund(1, null, "GF")));

        HttpResponseData response = await Sut.Delete(
            FunctionHttp.Get("", path: "config/funding-sources/1"), 1, CancellationToken.None);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        _funding.Verify(s => s.DeleteAsync(
            It.IsAny<int>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    // ── CSV stays config-manager-only ─────────────────────────────────────────

    /// <summary>
    /// ⚠️ A bulk upsert keyed by code spans every office's funds, and its export carries an
    /// office_code column the import deliberately ignores — neither is something an office-scoped
    /// caller has a safe reading of. Same call as PPDO-108 made for the division CSV.
    /// </summary>
    [Fact]
    public async Task Export_AsDepartmentHead_ReturnsForbidden()
    {
        AuthenticateDepartmentHead();

        HttpResponseData response = await Sut.Export(
            FunctionHttp.Get("", path: "config/funding-sources/csv"), CancellationToken.None);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Import_AsDepartmentHead_ReturnsForbidden()
    {
        AuthenticateDepartmentHead();

        HttpResponseData response = await Sut.Import(
            FunctionHttp.Post("config/funding-sources/csv", "code,name\nGSOX,GSO Fund"), CancellationToken.None);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        _funding.Verify(s => s.ImportCsvAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
