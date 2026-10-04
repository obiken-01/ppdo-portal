using System.Net;
using Microsoft.Azure.Functions.Worker.Http;
using Moq;
using PPDO.Application.Common;
using PPDO.Application.DTOs.Users;
using PPDO.Application.Services;
using PPDO.Domain.Entities;
using PPDO.Domain.Enums;
using PPDO.Domain.Interfaces;
using PPDO.Functions.Functions;

namespace PPDO.Tests.Functions;

/// <summary>
/// Endpoint tests for the two office-scoped user endpoints added by PPDO-135:
/// <c>GET /api/office/users</c> and <c>PUT /api/office/users/{id}/division</c>.
///
/// The rule this file exists for: these routes are gated on <c>CanManageOfficeSetup</c> ALONE,
/// never OR'd with <c>CanManageUsers</c> — see <c>docs/v1.8/Permission_Matrix.md</c> for why that
/// distinction matters (granting the latter here would let a department head list/edit every
/// office's Staff, PPDO included). The office-axis write guard itself lives in
/// <c>UserServiceTests</c> (SetOfficeUserDivisionAsync); this file only pins the handler's gate
/// and that it forwards the CALLER's own office, never a request-supplied one.
/// </summary>
public sealed class UserFunctionsTests
{
    private const int OwnOffice = 3;

    private readonly Mock<IUserService> _users = new(MockBehavior.Strict);
    private readonly Mock<IJwtValidator> _jwt = new(MockBehavior.Strict);
    private readonly Mock<IPermissionService> _permissions = new(MockBehavior.Loose);

    private UserFunctions Sut => new(_users.Object, _jwt.Object, _permissions.Object);

    private static User MakeUser(int? officeId) => new()
    {
        Id = Guid.NewGuid(), FullName = "Test", Username = "test", PasswordHash = "hash",
        Role = UserRole.Staff, OfficeId = officeId,
    };

    private User AuthenticateDepartmentHead(int? officeId = OwnOffice)
    {
        User caller = MakeUser(officeId);
        _jwt.Setup(j => j.ValidateAsync(It.IsAny<string?>(), It.IsAny<CancellationToken>())).ReturnsAsync(caller);
        _permissions.Setup(p => p.CanManageOfficeSetupAsync(caller, It.IsAny<CancellationToken>())).ReturnsAsync(true);
        return caller;
    }

    private User AuthenticateWithoutTheGrant()
    {
        User caller = MakeUser(OwnOffice);
        _jwt.Setup(j => j.ValidateAsync(It.IsAny<string?>(), It.IsAny<CancellationToken>())).ReturnsAsync(caller);
        _permissions.Setup(p => p.CanManageOfficeSetupAsync(caller, It.IsAny<CancellationToken>())).ReturnsAsync(false);
        return caller;
    }

    // ── GET /api/office/users ────────────────────────────────────────────────────

    [Fact]
    public async Task GetOfficeUsers_WithoutTheGrant_IsForbidden()
    {
        AuthenticateWithoutTheGrant();

        HttpResponseData response = await Sut.GetOfficeUsers(
            FunctionHttp.Get("", path: "office/users"), CancellationToken.None);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        _users.Verify(u => u.GetByOfficeIdAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task GetOfficeUsers_RequesterHasNoOffice_IsForbidden()
    {
        // DECISION F — unassigned sees nothing. CanManageOfficeSetupAsync should never be true here
        // in practice, but the handler must not trust that and fall through to officeId 0.
        AuthenticateDepartmentHead(officeId: null);

        HttpResponseData response = await Sut.GetOfficeUsers(
            FunctionHttp.Get("", path: "office/users"), CancellationToken.None);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        _users.Verify(u => u.GetByOfficeIdAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task GetOfficeUsers_UsesTheCallersOwnOffice()
    {
        AuthenticateDepartmentHead(officeId: OwnOffice);
        int? captured = null;
        _users.Setup(u => u.GetByOfficeIdAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .Callback((int officeId, CancellationToken _) => captured = officeId)
            .ReturnsAsync(Array.Empty<OfficeUserDto>());

        HttpResponseData response = await Sut.GetOfficeUsers(
            FunctionHttp.Get("", path: "office/users"), CancellationToken.None);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(OwnOffice, captured);
    }

    // ── PUT /api/office/users/{id}/division ──────────────────────────────────────

    [Fact]
    public async Task SetOfficeUserDivision_WithoutTheGrant_IsForbidden()
    {
        AuthenticateWithoutTheGrant();

        HttpResponseData response = await Sut.SetOfficeUserDivision(
            FunctionHttp.Put(new { divisionId = 5 }, path: "office/users/x/division"),
            Guid.NewGuid(), CancellationToken.None);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        _users.Verify(u => u.SetOfficeUserDivisionAsync(
            It.IsAny<User>(), It.IsAny<Guid>(), It.IsAny<int?>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task SetOfficeUserDivision_MalformedBody_IsBadRequest()
    {
        AuthenticateDepartmentHead();

        HttpResponseData response = await Sut.SetOfficeUserDivision(
            FunctionHttp.Put("not json", path: "office/users/x/division"),
            Guid.NewGuid(), CancellationToken.None);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task SetOfficeUserDivision_ForwardsCallerAndTargetAndDivisionId_ToTheService()
    {
        User caller = AuthenticateDepartmentHead();
        Guid targetId = Guid.NewGuid();
        User? capturedRequester = null;
        Guid capturedTarget = default;
        int? capturedDivisionId = -1;
        _users.Setup(u => u.SetOfficeUserDivisionAsync(
                It.IsAny<User>(), It.IsAny<Guid>(), It.IsAny<int?>(), It.IsAny<CancellationToken>()))
            .Callback((User r, Guid t, int? d, CancellationToken _) =>
            {
                capturedRequester = r;
                capturedTarget = t;
                capturedDivisionId = d;
            })
            .ReturnsAsync(ServiceResult<OfficeUserDto>.Ok(
                new OfficeUserDto(targetId, "Name", "user", null, true, 5, "Division")));

        HttpResponseData response = await Sut.SetOfficeUserDivision(
            FunctionHttp.Put(new { divisionId = 5 }, path: "office/users/x/division"),
            targetId, CancellationToken.None);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Same(caller, capturedRequester);
        Assert.Equal(targetId, capturedTarget);
        Assert.Equal(5, capturedDivisionId);
    }

    [Fact]
    public async Task SetOfficeUserDivision_NullDivisionId_ForwardsNull_ToClearIt()
    {
        AuthenticateDepartmentHead();
        Guid targetId = Guid.NewGuid();
        int? capturedDivisionId = -1;
        _users.Setup(u => u.SetOfficeUserDivisionAsync(
                It.IsAny<User>(), It.IsAny<Guid>(), It.IsAny<int?>(), It.IsAny<CancellationToken>()))
            .Callback((User _, Guid _, int? d, CancellationToken _) => capturedDivisionId = d)
            .ReturnsAsync(ServiceResult<OfficeUserDto>.Ok(
                new OfficeUserDto(targetId, "Name", "user", null, true, null, null)));

        await Sut.SetOfficeUserDivision(
            FunctionHttp.Put(new { divisionId = (int?)null }, path: "office/users/x/division"),
            targetId, CancellationToken.None);

        Assert.Null(capturedDivisionId);
    }

    [Fact]
    public async Task SetOfficeUserDivision_ServiceForbidden_MapsToHttpForbidden()
    {
        // The Forbidden that matters — a cross-office attempt caught by the service — must reach
        // the client as 403, not swallowed into a generic error.
        AuthenticateDepartmentHead();
        _users.Setup(u => u.SetOfficeUserDivisionAsync(
                It.IsAny<User>(), It.IsAny<Guid>(), It.IsAny<int?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ServiceResult<OfficeUserDto>.Forbidden("You may only manage users in your own office."));

        HttpResponseData response = await Sut.SetOfficeUserDivision(
            FunctionHttp.Put(new { divisionId = 5 }, path: "office/users/x/division"),
            Guid.NewGuid(), CancellationToken.None);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }
}
