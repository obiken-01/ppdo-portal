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
/// Endpoint half of the concurrent-edit guard (V18-71 / PPDO-119).
///
/// <para>
/// The service tests own what a conflict <i>is</i>; these own what the endpoint does with it —
/// that the version reaches the service at all, that a malformed one is a 400 rather than a
/// silent null, and, above all, <b>that the version check never runs before authorization</b>.
/// </para>
/// </summary>
public sealed class AipConcurrencyEndpointTests
{
    private const int AipRecordId = 100;
    private const int ActivityId  = 40;

    private readonly Mock<IAipService>        _aip         = new(MockBehavior.Loose);
    private readonly Mock<IJwtValidator>      _jwt         = new(MockBehavior.Loose);
    private readonly Mock<IPermissionService> _permissions = new(MockBehavior.Loose);
    private readonly Mock<IRepository<FundingSource>> _fsRepo = new(MockBehavior.Loose);

    private AipFunctions Sut =>
        new(_aip.Object, _jwt.Object, _permissions.Object, _fsRepo.Object);

    private static User Caller() => new()
    {
        Id = Guid.NewGuid(), FullName = "Ana Cruz", Username = "ana", PasswordHash = "h",
        Role = UserRole.Staff, OfficeId = 1,
    };

    /// <summary>Authenticates, and decides whether the permission predicate says yes.</summary>
    private User Authenticate(bool permitted)
    {
        User caller = Caller();
        _jwt.Setup(j => j.ValidateAsync(It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(caller);
        _permissions.Setup(p => p.CanAccessBudgetPlanningAsync(It.IsAny<User>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(permitted);
        return caller;
    }

    private static object Body(string? rowVersion) => new
    {
        name = "Renamed",
        rowVersion,
    };

    private Task<HttpResponseData> PutActivity(string? rowVersion) =>
        Sut.UpdateActivity(
            FunctionHttp.Put(Body(rowVersion), path: $"budget-planning/aip/{AipRecordId}/activities/{ActivityId}"),
            AipRecordId, ActivityId, CancellationToken.None);

    // ── ⚠️ The ordering guard ─────────────────────────────────────────────────

    [Fact]
    public async Task UpdateActivity_WithoutPermissionAndAStaleVersion_Returns403NotAConflict()
    {
        // ⚠️ The whole reason spec §4 pins the check order. If the version check ran first, the
        // 409 would become an oracle: a caller with no rights to this record could learn that it
        // exists AND that somebody edited it recently, purely from which status came back.
        //
        // Asserting the exact status matters — "not 200" would pass either way, which is how this
        // class of bug survives review.
        Authenticate(permitted: false);

        HttpResponseData response = await PutActivity("AAAAAAAAB9E=");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);

        // And the service is never reached, so there is nothing to leak in the first place.
        _aip.Verify(a => a.UpdateActivityAsync(
            It.IsAny<int>(), It.IsAny<int>(), It.IsAny<UpdateAipActivityDto>(),
            It.IsAny<User>(), It.IsAny<byte[]?>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task UpdateActivity_WithoutAuthentication_Returns401BeforeAnythingElse()
    {
        _jwt.Setup(j => j.ValidateAsync(It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((User?)null);

        HttpResponseData response = await PutActivity("AAAAAAAAB9E=");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    // ── The version reaches the service ───────────────────────────────────────

    [Fact]
    public async Task UpdateActivity_DecodesTheVersionAndPassesItThrough()
    {
        User caller = Authenticate(permitted: true);
        byte[]? captured = null;
        _aip.Setup(a => a.UpdateActivityAsync(
                It.IsAny<int>(), It.IsAny<int>(), It.IsAny<UpdateAipActivityDto>(),
                It.IsAny<User>(), It.IsAny<byte[]?>(), It.IsAny<CancellationToken>()))
            .Callback((int _, int _, UpdateAipActivityDto _, User _, byte[]? rv, CancellationToken _) => captured = rv)
            .ReturnsAsync(ServiceResult<AipActivityDto>.Conflict("changed"));

        await PutActivity(Convert.ToBase64String(new byte[] { 0, 0, 0, 0, 0, 0, 0, 1 }));

        Assert.Equal(new byte[] { 0, 0, 0, 0, 0, 0, 0, 1 }, captured);
    }

    [Fact]
    public async Task UpdateActivity_WithAMalformedVersion_Returns400NotASilentNull()
    {
        // Falling back to null would be worse than failing: the save would proceed UNGUARDED,
        // so a client with a corrupted token would silently get last-write-wins back.
        Authenticate(permitted: true);

        HttpResponseData response = await PutActivity("!!! not base64 !!!");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        _aip.Verify(a => a.UpdateActivityAsync(
            It.IsAny<int>(), It.IsAny<int>(), It.IsAny<UpdateAipActivityDto>(),
            It.IsAny<User>(), It.IsAny<byte[]?>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task UpdateActivity_WithNoVersion_StillReachesTheServiceWithNull()
    {
        // ⚠️ The PPDO-119 rollout state, pinned so it is a decision rather than an accident: an
        // omitted version is allowed through and the save runs unguarded, so old clients keep
        // working while PPDO-120 ships. PPDO-121 turns this into a 400 and is what actually
        // switches the protection on — until then the guard is opt-out by omission.
        Authenticate(permitted: true);
        byte[]? captured = new byte[] { 9 };
        _aip.Setup(a => a.UpdateActivityAsync(
                It.IsAny<int>(), It.IsAny<int>(), It.IsAny<UpdateAipActivityDto>(),
                It.IsAny<User>(), It.IsAny<byte[]?>(), It.IsAny<CancellationToken>()))
            .Callback((int _, int _, UpdateAipActivityDto _, User _, byte[]? rv, CancellationToken _) => captured = rv)
            .ReturnsAsync(ServiceResult<AipActivityDto>.Ok(null!));

        HttpResponseData response = await PutActivity(rowVersion: null);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Null(captured);
    }

    // ── The conflict surfaces as a 409 carrying its payload ───────────────────

    [Fact]
    public async Task UpdateActivity_WhenTheServiceConflicts_Returns409WithTheDetailInData()
    {
        // The envelope's `data` is normally null on a failure. The conflict is the exception:
        // without the payload the UI cannot offer Overwrite, only "reload and lose your work".
        Authenticate(permitted: true);
        AipConflictDto<AipActivityDto> detail = new("Ben Reyes", DateTime.UtcNow, "AAAAAAAAB9I=", null!);
        _aip.Setup(a => a.UpdateActivityAsync(
                It.IsAny<int>(), It.IsAny<int>(), It.IsAny<UpdateAipActivityDto>(),
                It.IsAny<User>(), It.IsAny<byte[]?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ServiceResult<AipActivityDto>.Conflict(
                "This activity was changed by Ben Reyes while you were editing it.", detail));

        HttpResponseData response = await PutActivity("AAAAAAAAB9E=");

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);

        string json = FunctionHttp.BodyText(response);
        Assert.Contains("Ben Reyes", json);
        Assert.Contains("AAAAAAAAB9I=", json);   // the version an Overwrite resubmits with
    }

    // ── ConfigHttp.DecodeRowVersion, directly ─────────────────────────────────

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void DecodeRowVersion_WhenAbsent_IsAllowedAndYieldsNull(string? input)
    {
        (bool ok, byte[]? bytes) = ConfigHttp.DecodeRowVersion(input);

        Assert.True(ok);
        Assert.Null(bytes);
    }

    [Fact]
    public void DecodeRowVersion_WithValidBase64_RoundTrips()
    {
        byte[] original = [0, 0, 0, 0, 0, 0, 0x9C, 0xBE];

        (bool ok, byte[]? bytes) = ConfigHttp.DecodeRowVersion(Convert.ToBase64String(original));

        Assert.True(ok);
        Assert.Equal(original, bytes);
    }

    [Theory]
    [InlineData("!!!")]
    [InlineData("not base64")]
    [InlineData("AAAA=AAA")]
    public void DecodeRowVersion_WithMalformedInput_IsRejected(string input)
    {
        (bool ok, byte[]? bytes) = ConfigHttp.DecodeRowVersion(input);

        Assert.False(ok);
        Assert.Null(bytes);
    }
}
