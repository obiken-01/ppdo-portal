using System.Net;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Options;
using Moq;
using PPDO.Application.Settings;
using PPDO.Domain.Entities;
using PPDO.Domain.Enums;
using PPDO.Domain.Interfaces;
using PPDO.Functions.Functions;

namespace PPDO.Tests.Functions;

/// <summary>
/// The refresh-token cookie's attributes (PPDO-200).
///
/// <para>The portal (<c>…azurestaticapps.net</c>) and the API (<c>…azurewebsites.net</c>) are
/// different sites, so the cookie is third-party. A browser that blocks third-party cookies
/// (Edge InPrivate, Safari, Firefox strict) dropped it, and every page reload signed the user
/// out. <c>Partitioned</c> (CHIPS) keeps it in a jar keyed to the portal's site, which those
/// browsers allow.</para>
///
/// <para>The rules pinned here:</para>
/// <list type="bullet">
/// <item>Login and refresh set the cookie <b>with</b> <c>Partitioned</c>.</item>
/// <item>The same response expires the legacy <b>unpartitioned</b> cookie, so a browser that
/// held one from before the deploy never carries two (the stale one would be a revoked token).
/// The expiry comes <b>first</b>: a browser that ignores <c>Partitioned</c> treats both headers
/// as the same cookie, and a delete after the set would erase the new token.</item>
/// <item>Logout clears both copies; a clear without <c>Partitioned</c> does not reach the
/// partitioned one.</item>
/// </list>
/// </summary>
public sealed class AuthRefreshCookieTests
{
    private readonly Mock<IAuthService>  _auth = new(MockBehavior.Strict);
    private readonly Mock<IJwtValidator> _jwt  = new(MockBehavior.Strict);

    private AuthFunctions Sut => new(
        _auth.Object, _jwt.Object, Options.Create(new JwtSettings { RefreshTokenExpiryDays = 7 }));

    private static readonly User Caller = new()
    {
        Id = Guid.NewGuid(), FullName = "Encoder", Username = "encoder", PasswordHash = "hash",
        Role = UserRole.Staff,
    };

    private static string[] SetCookies(HttpResponseData res)
        => res.Headers.TryGetValues("Set-Cookie", out IEnumerable<string>? values) ? values.ToArray() : [];

    private static string[] Attributes(string setCookie)
        => setCookie.Split(';', StringSplitOptions.TrimEntries);

    private static bool IsPartitioned(string setCookie) => Attributes(setCookie).Contains("Partitioned");

    private static bool IsExpiry(string setCookie) => Attributes(setCookie).Contains("Max-Age=0");

    /// <summary>Every cookie this API writes keeps the RAL-58 scoping and cross-site flags.</summary>
    private static void AssertScoped(string setCookie)
    {
        string[] attrs = Attributes(setCookie);
        Assert.StartsWith("ppdo_rt=", attrs[0]);
        Assert.Contains("Path=/api/auth/refresh", attrs);
        Assert.Contains("HttpOnly", attrs);
        Assert.Contains("Secure", attrs);
        Assert.Contains("SameSite=None", attrs);
    }

    /// <summary>Login and refresh write the same pair: expire the legacy cookie, then set the partitioned one.</summary>
    private static void AssertIssuesPartitionedCookie(HttpResponseData res, string expectedToken)
    {
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        string[] cookies = SetCookies(res);
        Assert.Equal(2, cookies.Length);
        foreach (string c in cookies) AssertScoped(c);

        string legacyExpiry = cookies[0];
        Assert.True(IsExpiry(legacyExpiry), "the legacy cookie is expired first");
        Assert.False(IsPartitioned(legacyExpiry), "the expiry targets the unpartitioned (legacy) cookie");

        string issued = cookies[1];
        Assert.True(IsPartitioned(issued), "the new token is set Partitioned");
        Assert.False(IsExpiry(issued));
        Assert.StartsWith($"ppdo_rt={Uri.EscapeDataString(expectedToken)};", issued);
        Assert.Contains($"Max-Age={7 * 24 * 60 * 60}", Attributes(issued));
    }

    // ── Login ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Login_Success_ExpiresLegacyCookieThenSetsPartitionedCookie()
    {
        _auth.Setup(a => a.LoginAsync("encoder", "pw", It.IsAny<CancellationToken>()))
             .ReturnsAsync(LoginResult.Success("access", "refresh+token/1"));

        HttpResponseData res = await Sut.Login(
            FunctionHttp.Post("auth/login", new { username = "encoder", password = "pw" }, authorizationHeader: null),
            CancellationToken.None);

        AssertIssuesPartitionedCookie(res, "refresh+token/1");
    }

    [Fact]
    public async Task Login_InvalidCredentials_SetsNoCookie()
    {
        _auth.Setup(a => a.LoginAsync("encoder", "bad", It.IsAny<CancellationToken>()))
             .ReturnsAsync(LoginResult.Invalid());

        HttpResponseData res = await Sut.Login(
            FunctionHttp.Post("auth/login", new { username = "encoder", password = "bad" }, authorizationHeader: null),
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.Unauthorized, res.StatusCode);
        Assert.Empty(SetCookies(res));
    }

    // ── Refresh ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task Refresh_Success_ExpiresLegacyCookieThenSetsPartitionedCookie()
    {
        _auth.Setup(a => a.RefreshAsync("old-token", It.IsAny<CancellationToken>()))
             .ReturnsAsync(RefreshResult.Success("access", "new-token"));
        FakeHttpRequestData req = FunctionHttp.Post("auth/refresh", authorizationHeader: null);
        req.Headers.Add("Cookie", "ppdo_rt=old-token");

        HttpResponseData res = await Sut.Refresh(req, CancellationToken.None);

        AssertIssuesPartitionedCookie(res, "new-token");
    }

    [Fact]
    public async Task Refresh_NoCookie_Returns401AndSetsNoCookie()
    {
        HttpResponseData res = await Sut.Refresh(
            FunctionHttp.Post("auth/refresh", authorizationHeader: null), CancellationToken.None);

        Assert.Equal(HttpStatusCode.Unauthorized, res.StatusCode);
        Assert.Empty(SetCookies(res));
    }

    // ── Logout ───────────────────────────────────────────────────────────────

    [Fact]
    public async Task Logout_ClearsBothThePartitionedAndTheLegacyCookie()
    {
        _jwt.Setup(j => j.ValidateAsync("Bearer test-token", It.IsAny<CancellationToken>())).ReturnsAsync(Caller);
        _auth.Setup(a => a.LogoutAsync(Caller.Id, It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);

        HttpResponseData res = await Sut.Logout(FunctionHttp.Post("auth/logout"), CancellationToken.None);

        Assert.Equal(HttpStatusCode.NoContent, res.StatusCode);
        string[] cookies = SetCookies(res);
        Assert.Equal(2, cookies.Length);
        foreach (string c in cookies)
        {
            AssertScoped(c);
            Assert.True(IsExpiry(c));
        }
        Assert.Single(cookies, IsPartitioned);
        Assert.Single(cookies, c => !IsPartitioned(c));
    }
}
