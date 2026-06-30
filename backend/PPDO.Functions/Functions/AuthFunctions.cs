using System.Net;
using System.Text.Json;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Options;
using PPDO.Application.DTOs.Auth;
using PPDO.Application.Services;
using PPDO.Application.Settings;
using PPDO.Domain.Entities;
using PPDO.Domain.Interfaces;

namespace PPDO.Functions.Functions;

/// <summary>
/// HTTP-triggered Azure Functions for authentication.
///
/// Public endpoints (no JWT required):
///   POST /api/auth/login   — email + password → access token + refresh token
///   POST /api/auth/refresh — refresh token → new access token + refresh token
///
/// Protected endpoints (JWT validated via JwtMiddleware.ValidateAsync):
///   POST /api/auth/logout  — revoke refresh token
///   GET  /api/auth/me      — current user identity + effective permissions
///
/// All triggers use AuthorizationLevel.Anonymous — JWT is validated manually per
/// CLAUDE.md architecture rules. Business logic lives exclusively in AuthService.
/// </summary>
public sealed class AuthFunctions
{
    // Refresh-token cookie. HttpOnly + Secure + SameSite=Strict, scoped to the refresh
    // endpoint so it is never sent on any other request (RAL-58).
    private const string RefreshCookieName = "ppdo_rt";
    private const string RefreshCookiePath = "/api/auth/refresh";
    private const int    AccessTokenLifetimeSeconds = 15 * 60;

    private readonly IAuthService _auth;
    private readonly IJwtMiddleware _jwt;
    private readonly JwtSettings _jwtSettings;

    public AuthFunctions(IAuthService auth, IJwtMiddleware jwt, IOptions<JwtSettings> jwtOptions)
    {
        _auth = auth;
        _jwt  = jwt;
        _jwtSettings = jwtOptions.Value;
    }

    // ── POST /api/auth/login ───────────────────────────────────────────────────

    [Function("Login")]
    public async Task<HttpResponseData> Login(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "auth/login")]
        HttpRequestData req,
        CancellationToken cancellationToken)
    {
        LoginRequestDto? body = await DeserializeAsync<LoginRequestDto>(req, cancellationToken);
        if (body is null || string.IsNullOrWhiteSpace(body.Username) || string.IsNullOrWhiteSpace(body.Password))
            return await BadRequest(req, "Username and Password are required.");

        LoginResult result = await _auth.LoginAsync(body.Username, body.Password, cancellationToken);

        switch (result.Outcome)
        {
            case LoginOutcome.RateLimited:
                HttpResponseData limited = req.CreateResponse(HttpStatusCode.TooManyRequests);
                limited.Headers.Add("Retry-After", result.RetryAfterSeconds.ToString());
                await limited.WriteStringAsync(
                    "Too many failed login attempts. Please try again later.", cancellationToken);
                return limited;

            case LoginOutcome.InvalidCredentials:
                return req.CreateResponse(HttpStatusCode.Unauthorized);

            default: // Success
                LoginResponseDto dto = new(result.AccessToken!, AccessTokenLifetimeSeconds);
                return await OkWithRefreshCookie(req, dto, result.RefreshToken!, cancellationToken);
        }
    }

    // ── POST /api/auth/refresh ─────────────────────────────────────────────────

    [Function("Refresh")]
    public async Task<HttpResponseData> Refresh(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "auth/refresh")]
        HttpRequestData req,
        CancellationToken cancellationToken)
    {
        // The refresh token is read from the httpOnly cookie, never the request body.
        string? refreshToken = ReadRefreshCookie(req);
        if (string.IsNullOrWhiteSpace(refreshToken))
            return req.CreateResponse(HttpStatusCode.Unauthorized);

        var tokens = await _auth.RefreshAsync(refreshToken, cancellationToken);
        if (tokens is null)
            return req.CreateResponse(HttpStatusCode.Unauthorized);

        LoginResponseDto dto = new(tokens.Value.AccessToken, AccessTokenLifetimeSeconds);
        return await OkWithRefreshCookie(req, dto, tokens.Value.RefreshToken, cancellationToken);
    }

    // ── POST /api/auth/logout ──────────────────────────────────────────────────

    [Function("Logout")]
    public async Task<HttpResponseData> Logout(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "auth/logout")]
        HttpRequestData req,
        CancellationToken cancellationToken)
    {
        User? user = await _jwt.ValidateAsync(GetAuthHeader(req), cancellationToken);
        if (user is null)
            return req.CreateResponse(HttpStatusCode.Unauthorized);

        await _auth.LogoutAsync(user.Id, cancellationToken);

        HttpResponseData response = req.CreateResponse(HttpStatusCode.NoContent);
        ClearRefreshCookie(response);
        return response;
    }

    // ── GET /api/auth/me ───────────────────────────────────────────────────────

    [Function("Me")]
    public async Task<HttpResponseData> Me(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "auth/me")]
        HttpRequestData req,
        CancellationToken cancellationToken)
    {
        User? user = await _jwt.ValidateAsync(GetAuthHeader(req), cancellationToken);
        if (user is null)
            return req.CreateResponse(HttpStatusCode.Unauthorized);

        MeResponse me = await _auth.GetMeAsync(user, cancellationToken);

        MeResponseDto dto = new()
        {
            UserId                  = me.UserId,
            FullName                = me.FullName,
            Username                = me.Username,
            Email                   = me.Email,
            Role                    = me.Role,
            DivisionId              = me.DivisionId,
            Division                = me.Division,
            OfficeId                = me.OfficeId,
            OfficeCode              = me.OfficeCode,
            OfficeName              = me.OfficeName,
            Position                = me.Position,
            CanAccessInventory      = me.CanAccessInventory,
            CanAccessReports        = me.CanAccessReports,
            CanManageUsers          = me.CanManageUsers,
            CanAccessProfile        = me.CanAccessProfile,
            CanManageResourceLinks  = me.CanManageResourceLinks,
            CanAccessBudgetPlanning = me.CanAccessBudgetPlanning,
            CanUploadAip            = me.CanUploadAip,
            CanManageConfig         = me.CanManageConfig,
            CanManageAllocation     = me.CanManageAllocation,
        };

        return await Ok(req, dto, cancellationToken);
    }

    // ── Refresh-token cookie helpers ─────────────────────────────────────────────

    /// <summary>
    /// Writes a JSON OK response and attaches the rotated refresh token as an httpOnly cookie.
    /// The cookie header is set before the body so it is present regardless of buffering.
    /// </summary>
    private async Task<HttpResponseData> OkWithRefreshCookie<T>(
        HttpRequestData req,
        T body,
        string refreshToken,
        CancellationToken cancellationToken)
    {
        HttpResponseData response = req.CreateResponse(HttpStatusCode.OK);
        response.Headers.Add("Content-Type", "application/json; charset=utf-8");
        AppendRefreshCookie(response, refreshToken);
        await response.WriteStringAsync(JsonSerializer.Serialize(body, _jsonOptions), cancellationToken);
        return response;
    }

    private void AppendRefreshCookie(HttpResponseData response, string refreshToken)
    {
        int maxAgeSeconds = _jwtSettings.RefreshTokenExpiryDays * 24 * 60 * 60;
        string value = Uri.EscapeDataString(refreshToken);
        response.Headers.Add(
            "Set-Cookie",
            $"{RefreshCookieName}={value}; Max-Age={maxAgeSeconds}; Path={RefreshCookiePath}; HttpOnly; Secure; SameSite=Strict");
    }

    private static void ClearRefreshCookie(HttpResponseData response)
    {
        response.Headers.Add(
            "Set-Cookie",
            $"{RefreshCookieName}=; Max-Age=0; Path={RefreshCookiePath}; HttpOnly; Secure; SameSite=Strict");
    }

    /// <summary>Reads and URL-decodes the refresh token from the request's Cookie header.</summary>
    private static string? ReadRefreshCookie(HttpRequestData req)
    {
        if (!req.Headers.TryGetValues("Cookie", out IEnumerable<string>? cookieHeaders))
            return null;

        foreach (string header in cookieHeaders)
        {
            foreach (string part in header.Split(';'))
            {
                string trimmed = part.Trim();
                if (trimmed.StartsWith(RefreshCookieName + "=", StringComparison.Ordinal))
                    return Uri.UnescapeDataString(trimmed[(RefreshCookieName.Length + 1)..]);
            }
        }

        return null;
    }

    // ── Helpers ────────────────────────────────────────────────────────────────

    /// <summary>
    /// Extracts the raw Authorization header value from the request.
    /// IHttpContextAccessor.HttpContext is not reliably populated in the Azure Functions
    /// isolated worker model, so we read directly from HttpRequestData.Headers instead.
    /// </summary>
    private static string? GetAuthHeader(HttpRequestData req)
        => req.Headers.TryGetValues("Authorization", out IEnumerable<string>? values)
            ? values.FirstOrDefault()
            : null;

    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        // Case-insensitive for reading incoming request bodies.
        PropertyNameCaseInsensitive = true,
        // camelCase for all outgoing JSON responses (userId, fullName, etc.)
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    private static async Task<T?> DeserializeAsync<T>(
        HttpRequestData req,
        CancellationToken cancellationToken)
    {
        try
        {
            return await JsonSerializer.DeserializeAsync<T>(
                req.Body, _jsonOptions, cancellationToken);
        }
        catch
        {
            return default;
        }
    }

    private static async Task<HttpResponseData> Ok<T>(
        HttpRequestData req,
        T body,
        CancellationToken cancellationToken)
    {
        HttpResponseData response = req.CreateResponse(HttpStatusCode.OK);
        // Serialize manually with _jsonOptions to guarantee camelCase output.
        // WriteAsJsonAsync without options uses the worker default (PascalCase).
        response.Headers.Add("Content-Type", "application/json; charset=utf-8");
        await response.WriteStringAsync(
            JsonSerializer.Serialize(body, _jsonOptions), cancellationToken);
        return response;
    }

    private static async Task<HttpResponseData> BadRequest(
        HttpRequestData req,
        string message)
    {
        HttpResponseData response = req.CreateResponse(HttpStatusCode.BadRequest);
        await response.WriteStringAsync(message);
        return response;
    }
}
