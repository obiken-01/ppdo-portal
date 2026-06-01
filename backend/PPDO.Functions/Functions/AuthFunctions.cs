using System.Net;
using System.Text.Json;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using PPDO.Application.DTOs.Auth;
using PPDO.Application.Services;
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
    private readonly IAuthService _auth;
    private readonly IJwtMiddleware _jwt;

    public AuthFunctions(IAuthService auth, IJwtMiddleware jwt)
    {
        _auth = auth;
        _jwt  = jwt;
    }

    // ── POST /api/auth/login ───────────────────────────────────────────────────

    [Function("Login")]
    public async Task<HttpResponseData> Login(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "auth/login")]
        HttpRequestData req,
        CancellationToken cancellationToken)
    {
        LoginRequestDto? body = await DeserializeAsync<LoginRequestDto>(req, cancellationToken);
        if (body is null || string.IsNullOrWhiteSpace(body.Email) || string.IsNullOrWhiteSpace(body.Password))
            return await BadRequest(req, "Email and Password are required.");

        var tokens = await _auth.LoginAsync(body.Email, body.Password, cancellationToken);
        if (tokens is null)
            return req.CreateResponse(HttpStatusCode.Unauthorized);

        LoginResponseDto dto = new(
            tokens.Value.AccessToken,
            tokens.Value.RefreshToken,
            ExpiresInSeconds: 15 * 60);

        return await Ok(req, dto, cancellationToken);
    }

    // ── POST /api/auth/refresh ─────────────────────────────────────────────────

    [Function("Refresh")]
    public async Task<HttpResponseData> Refresh(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "auth/refresh")]
        HttpRequestData req,
        CancellationToken cancellationToken)
    {
        RefreshRequestDto? body = await DeserializeAsync<RefreshRequestDto>(req, cancellationToken);
        if (body is null || string.IsNullOrWhiteSpace(body.RefreshToken))
            return await BadRequest(req, "RefreshToken is required.");

        var tokens = await _auth.RefreshAsync(body.RefreshToken, cancellationToken);
        if (tokens is null)
            return req.CreateResponse(HttpStatusCode.Unauthorized);

        LoginResponseDto dto = new(
            tokens.Value.AccessToken,
            tokens.Value.RefreshToken,
            ExpiresInSeconds: 15 * 60);

        return await Ok(req, dto, cancellationToken);
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

        return req.CreateResponse(HttpStatusCode.NoContent);
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
            UserId                 = me.UserId,
            FullName               = me.FullName,
            Email                  = me.Email,
            Role                   = me.Role,
            Division               = me.Division,
            Position               = me.Position,
            CanAccessInventory     = me.CanAccessInventory,
            CanAccessReports       = me.CanAccessReports,
            CanManageUsers         = me.CanManageUsers,
            CanAccessProfile       = me.CanAccessProfile,
            CanManageResourceLinks = me.CanManageResourceLinks,
        };

        return await Ok(req, dto, cancellationToken);
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
