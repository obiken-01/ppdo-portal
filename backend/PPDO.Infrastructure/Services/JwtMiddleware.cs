using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.Tokens;
using PPDO.Domain.Common;
using PPDO.Domain.Entities;
using PPDO.Domain.Interfaces;
using PPDO.Infrastructure.Data;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;

namespace PPDO.Infrastructure.Services;

/// <summary>
/// Validates the JWT Bearer token in the current HTTP request and loads
/// the authenticated <see cref="User"/> entity (with Group navigation) from the database.
///
/// Reads the Authorization header via <see cref="IHttpContextAccessor"/>.
/// On success, sets <c>HttpContext.User</c> with the validated <see cref="ClaimsPrincipal"/>
/// so that <see cref="ICurrentUserService"/> is available in Application services.
/// Returns null on any failure — never throws.
///
/// JWT settings are read directly from <see cref="IConfiguration"/> (Jwt:SecretKey,
/// Jwt:Issuer, Jwt:Audience) — same keys as the JwtSettings options class uses.
///
/// Register as scoped in Program.cs.
/// </summary>
public sealed class JwtMiddleware : IJwtMiddleware
{
    private readonly string _secretKey;
    private readonly string _issuer;
    private readonly string _audience;
    private readonly AppDbContext _context;
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly ILogger<JwtMiddleware> _logger;

    public JwtMiddleware(
        IConfiguration configuration,
        AppDbContext context,
        IHttpContextAccessor httpContextAccessor,
        ILogger<JwtMiddleware> logger)
    {
        _secretKey = configuration["Jwt:SecretKey"]
            ?? throw new InvalidOperationException(
                "Jwt:SecretKey is not configured. Add Jwt__SecretKey to local.settings.json or Azure App Settings.");
        _issuer = configuration["Jwt:Issuer"]
            ?? throw new InvalidOperationException(
                "Jwt:Issuer is not configured. Add Jwt__Issuer to local.settings.json or Azure App Settings.");
        _audience = configuration["Jwt:Audience"]
            ?? throw new InvalidOperationException(
                "Jwt:Audience is not configured. Add Jwt__Audience to local.settings.json or Azure App Settings.");
        _context = context;
        _httpContextAccessor = httpContextAccessor;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<User?> ValidateAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            HttpContext? httpContext = _httpContextAccessor.HttpContext;
            if (httpContext is null)
                return null;

            string? authHeader = httpContext.Request.Headers.Authorization.FirstOrDefault();
            if (string.IsNullOrWhiteSpace(authHeader)
                || !authHeader.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
                return null;

            string token = authHeader["Bearer ".Length..].Trim();
            if (string.IsNullOrEmpty(token))
                return null;

            // ValidateToken throws SecurityTokenException on any validation failure.
            ClaimsPrincipal principal = ValidateToken(token);

            // Extract the subject claim (user ID).
            string? subClaim = principal.FindFirstValue(JwtClaimNames.Sub);
            if (!Guid.TryParse(subClaim, out Guid userId))
                return null;

            // Load the user with their Group — Group is required for permission resolution.
            User? user = await _context.Users
                .Include(u => u.Group)          // depth 1
                .FirstOrDefaultAsync(u => u.Id == userId, cancellationToken);

            if (user is null || !user.IsActive)
                return null;

            // Populate HttpContext.User so ICurrentUserService works in Application services.
            httpContext.User = principal;

            return user;
        }
        catch (SecurityTokenException ex)
        {
            // Expired token, invalid signature, malformed JWT, etc.
            // Logged at Warning — this is a normal rejection, not a bug.
            _logger.LogWarning("JWT validation rejected: {Message}", ex.Message);
            return null;
        }
        catch (Exception ex)
        {
            // Unexpected failure (e.g. database unreachable). Log and return null;
            // never allow the exception to propagate to the Function handler.
            _logger.LogError(ex, "Unexpected error during JWT validation.");
            return null;
        }
    }

    /// <summary>
    /// Validates the token signature, expiry, issuer, and audience.
    /// Throws <see cref="SecurityTokenException"/> on any failure.
    /// </summary>
    private ClaimsPrincipal ValidateToken(string token)
    {
        JwtSecurityTokenHandler handler = new();
        byte[] keyBytes = Encoding.UTF8.GetBytes(_secretKey);

        TokenValidationParameters parameters = new()
        {
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(keyBytes),
            ValidateIssuer = true,
            ValidIssuer = _issuer,
            ValidateAudience = true,
            ValidAudience = _audience,
            ValidateLifetime = true,
            // Zero clock skew — 15-minute access tokens already have adequate slack.
            ClockSkew = TimeSpan.Zero,
        };

        return handler.ValidateToken(token, parameters, out _);
    }
}
