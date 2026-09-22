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
/// Endpoint tests for division config's own-office caller (v1.8.0 — PPDO-108).
///
/// The rule this file exists for: a department head holding <c>CanManageOfficeSetup</c> manages
/// their OWN office's divisions and nothing else, and <b>cannot set the seven feature switches</b>
/// whatever the request body says. Hiding the controls in the form is not the guard — these are.
///
/// ⚠️ Every case is paired with the config manager's, because the failure that matters is not "the
/// department head got too little" but "the config manager quietly got less".
/// </summary>
public sealed class ConfigDivisionFunctionsTests
{
    private const int OwnOffice     = 3;
    private const int ForeignOffice = 99;

    private readonly Mock<IDivisionService>   _divisions   = new(MockBehavior.Strict);
    private readonly Mock<IOfficeService>     _offices     = new(MockBehavior.Strict);
    private readonly Mock<IJwtValidator>     _jwt         = new(MockBehavior.Strict);
    private readonly Mock<IPermissionService> _permissions = new(MockBehavior.Loose);

    private ConfigDivisionFunctions Sut =>
        new(_divisions.Object, _offices.Object, _jwt.Object, _permissions.Object);

    private static User MakeUser(bool hostOffice) => new()
    {
        Id = Guid.NewGuid(), FullName = "Test", Username = "test", PasswordHash = "hash",
        Role = UserRole.Staff, OfficeId = hostOffice ? 1 : OwnOffice,
    };

    /// <summary>A department head: the setup grant, and no config grant.</summary>
    private User AuthenticateDepartmentHead()
    {
        User caller = MakeUser(hostOffice: false);
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

    private User AuthenticateWithNeither()
    {
        User caller = MakeUser(hostOffice: false);
        _jwt.Setup(j => j.ValidateAsync(It.IsAny<string?>(), It.IsAny<CancellationToken>())).ReturnsAsync(caller);
        _permissions.Setup(p => p.CanManageConfigAsync(caller, It.IsAny<CancellationToken>())).ReturnsAsync(false);
        _permissions.Setup(p => p.CanManageOfficeSetupAsync(caller, It.IsAny<CancellationToken>())).ReturnsAsync(false);
        return caller;
    }

    private static DivisionDto Division(int id, int officeId, bool flagsOn = false) => new(
        id, officeId, "Office", "OFF", "ADMIN", "Administrative", true,
        CanAccessInventory: flagsOn, CanAccessReports: flagsOn, CanManageUsers: flagsOn,
        CanManageResourceLinks: flagsOn, CanAccessBudgetPlanning: flagsOn, CanUploadAip: flagsOn,
        CanManageConfig: flagsOn);

    private static UpsertDivisionDto Body(int officeId, bool flagsOn = true) => new(
        officeId, "ADMIN", "Administrative", true,
        CanAccessBudgetPlanning: flagsOn, CanAccessInventory: flagsOn, CanAccessReports: flagsOn,
        CanManageConfig: flagsOn, CanUploadAip: flagsOn, CanManageUsers: flagsOn,
        CanManageResourceLinks: flagsOn);

    // ── List ──────────────────────────────────────────────────────────────────

    /// <summary>The leak this clamp closes: asking for another office's divisions by query string.</summary>
    [Fact]
    public async Task List_AsDepartmentHead_ReadsTheirOwnOfficeWhateverTheyAskedFor()
    {
        AuthenticateDepartmentHead();
        int? captured = -1;
        _divisions.Setup(s => s.GetAllAsync(It.IsAny<bool?>(), It.IsAny<int?>(), It.IsAny<CancellationToken>()))
            .Callback((bool? _, int? officeId, CancellationToken _) => captured = officeId)
            .ReturnsAsync(new[] { Division(1, OwnOffice) });

        HttpResponseData response = await Sut.List(
            FunctionHttp.Get($"officeId={ForeignOffice}", path: "config/divisions"), CancellationToken.None);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(OwnOffice, captured);
    }

    [Fact]
    public async Task List_AsConfigManager_KeepsTheRequestedOffice()
    {
        AuthenticateConfigManager();
        int? captured = -1;
        _divisions.Setup(s => s.GetAllAsync(It.IsAny<bool?>(), It.IsAny<int?>(), It.IsAny<CancellationToken>()))
            .Callback((bool? _, int? officeId, CancellationToken _) => captured = officeId)
            .ReturnsAsync(Array.Empty<DivisionDto>());

        await Sut.List(FunctionHttp.Get($"officeId={ForeignOffice}", path: "config/divisions"), CancellationToken.None);

        Assert.Equal(ForeignOffice, captured);
    }

    /// <summary>
    /// ⚠️ Both grants means config manager, not office-scoped. Granting a PPDO admin the new flag
    /// must not quietly take every other office's divisions away from them.
    /// </summary>
    [Fact]
    public async Task List_AsConfigManagerHoldingBothGrants_IsNotClamped()
    {
        AuthenticateConfigManager(alsoOfficeSetup: true);
        int? captured = -1;
        _divisions.Setup(s => s.GetAllAsync(It.IsAny<bool?>(), It.IsAny<int?>(), It.IsAny<CancellationToken>()))
            .Callback((bool? _, int? officeId, CancellationToken _) => captured = officeId)
            .ReturnsAsync(Array.Empty<DivisionDto>());

        await Sut.List(FunctionHttp.Get($"officeId={ForeignOffice}", path: "config/divisions"), CancellationToken.None);

        Assert.Equal(ForeignOffice, captured);
    }

    // ── Create ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Create_AsDepartmentHead_StampsTheirOfficeAndDropsEveryFlag()
    {
        AuthenticateDepartmentHead();
        UpsertDivisionDto? captured = null;
        _divisions.Setup(s => s.CreateAsync(It.IsAny<UpsertDivisionDto>(), It.IsAny<CancellationToken>()))
            .Callback((UpsertDivisionDto dto, CancellationToken _) => captured = dto)
            .ReturnsAsync(ServiceResult<DivisionDto>.Ok(Division(1, OwnOffice)));

        HttpResponseData response = await Sut.Create(
            FunctionHttp.Post("config/divisions", Body(ForeignOffice, flagsOn: true)), CancellationToken.None);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.NotNull(captured);
        Assert.Equal(OwnOffice, captured!.OfficeId);
        // D9 — a division a department head creates starts with every switch off.
        Assert.False(captured.CanAccessBudgetPlanning);
        Assert.False(captured.CanAccessInventory);
        Assert.False(captured.CanAccessReports);
        Assert.False(captured.CanManageConfig);
        Assert.False(captured.CanUploadAip);
        Assert.False(captured.CanManageUsers);
        Assert.False(captured.CanManageResourceLinks);
        // The fields they DO own are untouched.
        Assert.Equal("Administrative", captured.Name);
        Assert.Equal("ADMIN", captured.Code);
    }

    [Fact]
    public async Task Create_AsConfigManager_KeepsTheBodyAsSent()
    {
        AuthenticateConfigManager();
        UpsertDivisionDto? captured = null;
        _divisions.Setup(s => s.CreateAsync(It.IsAny<UpsertDivisionDto>(), It.IsAny<CancellationToken>()))
            .Callback((UpsertDivisionDto dto, CancellationToken _) => captured = dto)
            .ReturnsAsync(ServiceResult<DivisionDto>.Ok(Division(1, ForeignOffice, flagsOn: true)));

        await Sut.Create(FunctionHttp.Post("config/divisions", Body(ForeignOffice, flagsOn: true)), CancellationToken.None);

        Assert.Equal(ForeignOffice, captured!.OfficeId);
        Assert.True(captured.CanAccessBudgetPlanning);
    }

    [Fact]
    public async Task Create_WithNeitherGrant_ReturnsForbidden()
    {
        AuthenticateWithNeither();

        HttpResponseData response = await Sut.Create(
            FunctionHttp.Post("config/divisions", Body(OwnOffice)), CancellationToken.None);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        _divisions.VerifyNoOtherCalls();
    }

    // ── Update ────────────────────────────────────────────────────────────────

    /// <summary>
    /// The switches come from the STORED row, so an edit can neither raise one nor clear one an
    /// admin set. This case is the second half: the division already has them ON.
    /// </summary>
    [Fact]
    public async Task Update_AsDepartmentHead_KeepsTheStoredFlags_NotTheOnesSent()
    {
        AuthenticateDepartmentHead();
        _divisions.Setup(s => s.GetByIdAsync(5, It.IsAny<CancellationToken>()))
            .ReturnsAsync(ServiceResult<DivisionDto>.Ok(Division(5, OwnOffice, flagsOn: true)));
        UpsertDivisionDto? captured = null;
        _divisions.Setup(s => s.UpdateAsync(5, It.IsAny<UpsertDivisionDto>(), It.IsAny<CancellationToken>()))
            .Callback((int _, UpsertDivisionDto dto, CancellationToken _) => captured = dto)
            .ReturnsAsync(ServiceResult<DivisionDto>.Ok(Division(5, OwnOffice, flagsOn: true)));

        HttpResponseData response = await Sut.Update(
            FunctionHttp.Put(Body(OwnOffice, flagsOn: false), path: "config/divisions/5"), 5, CancellationToken.None);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(captured!.CanAccessBudgetPlanning);
        Assert.True(captured.CanManageConfig);
    }

    [Fact]
    public async Task Update_AsDepartmentHead_TargetingAnotherOfficesDivision_ReturnsForbidden()
    {
        AuthenticateDepartmentHead();
        _divisions.Setup(s => s.GetByIdAsync(5, It.IsAny<CancellationToken>()))
            .ReturnsAsync(ServiceResult<DivisionDto>.Ok(Division(5, ForeignOffice)));

        HttpResponseData response = await Sut.Update(
            FunctionHttp.Put(Body(OwnOffice), path: "config/divisions/5"), 5, CancellationToken.None);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        _divisions.Verify(s => s.UpdateAsync(
            It.IsAny<int>(), It.IsAny<UpsertDivisionDto>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    /// <summary>⚠️ A missing id answers 403, not 404 — otherwise it reports which ids exist.</summary>
    [Fact]
    public async Task Update_AsDepartmentHead_WithAnUnknownDivision_ReturnsForbiddenNotNotFound()
    {
        AuthenticateDepartmentHead();
        _divisions.Setup(s => s.GetByIdAsync(404, It.IsAny<CancellationToken>()))
            .ReturnsAsync(ServiceResult<DivisionDto>.NotFound("Division 404 not found."));

        HttpResponseData response = await Sut.Update(
            FunctionHttp.Put(Body(OwnOffice), path: "config/divisions/404"), 404, CancellationToken.None);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    // ── Delete ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Delete_AsDepartmentHead_TargetingTheirOwnDivision_IsAllowed()
    {
        AuthenticateDepartmentHead();
        _divisions.Setup(s => s.GetByIdAsync(5, It.IsAny<CancellationToken>()))
            .ReturnsAsync(ServiceResult<DivisionDto>.Ok(Division(5, OwnOffice)));
        _divisions.Setup(s => s.DeleteAsync(5, It.IsAny<CancellationToken>()))
            .ReturnsAsync(ServiceResult<DivisionDto>.Ok(Division(5, OwnOffice)));

        HttpResponseData response = await Sut.Delete(
            FunctionHttp.Get("", path: "config/divisions/5"), 5, CancellationToken.None);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Delete_AsDepartmentHead_TargetingAnotherOfficesDivision_ReturnsForbidden()
    {
        AuthenticateDepartmentHead();
        _divisions.Setup(s => s.GetByIdAsync(5, It.IsAny<CancellationToken>()))
            .ReturnsAsync(ServiceResult<DivisionDto>.Ok(Division(5, ForeignOffice)));

        HttpResponseData response = await Sut.Delete(
            FunctionHttp.Get("", path: "config/divisions/5"), 5, CancellationToken.None);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        _divisions.Verify(s => s.DeleteAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    // ── CSV stays config-manager-only ─────────────────────────────────────────

    /// <summary>
    /// A bulk upsert carries an office column per row, so there is no safe reading of it for an
    /// office-scoped caller — the route keeps its original gate.
    /// </summary>
    [Fact]
    public async Task CsvExport_AsDepartmentHead_ReturnsForbidden()
    {
        AuthenticateDepartmentHead();

        HttpResponseData response = await Sut.Export(
            FunctionHttp.Get("", path: "config/divisions/csv"), CancellationToken.None);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task CsvImport_AsDepartmentHead_ReturnsForbidden()
    {
        AuthenticateDepartmentHead();

        HttpResponseData response = await Sut.Import(
            FunctionHttp.Post("config/divisions/csv", "office_code,code,name\n"), CancellationToken.None);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }
}
