using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using PPDO.Application.DTOs.Auth;
using PPDO.Application.Settings;
using PPDO.Domain.Common;
using PPDO.Domain.Entities;
using PPDO.Domain.Enums;
using PPDO.Domain.Interfaces;

namespace PPDO.Application.Services;

/// <summary>
/// Handles JWT login, refresh token rotation, logout, and current-user info.
///
/// Token strategy:
///   - Access token:  15-minute JWT signed with HMAC-SHA256, claims: sub, email, role, div
///   - Refresh token: 64-byte cryptographically random base64 string stored in the Users table
///
/// On login/refresh the old refresh token is overwritten with a new one (rotation).
/// On logout the stored refresh token is set to null.
///
/// Database access goes exclusively through <see cref="IUserRepository"/> — AppDbContext
/// is never referenced here. Password verification uses BCrypt.Net-Next.
/// </summary>
public sealed class AuthService : IAuthService
{
    private readonly IUserRepository _users;
    private readonly IPermissionService _permissions;
    private readonly JwtSettings _jwt;
    private readonly ILogger<AuthService> _logger;

    public AuthService(
        IUserRepository users,
        IPermissionService permissions,
        IOptions<JwtSettings> jwtOptions,
        ILogger<AuthService> logger)
    {
        _users = users;
        _permissions = permissions;
        _jwt = jwtOptions.Value;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<(string AccessToken, string RefreshToken)?> LoginAsync(
        string username,
        string password,
        CancellationToken cancellationToken = default)
    {
        User? user = await _users.FindByUsernameAsync(username, cancellationToken);

        if (user is null)
        {
            // Consistent timing — run a dummy verify so response time doesn't leak existence.
            BCrypt.Net.BCrypt.Verify(password, "$2a$11$dummyhashtopreventtimingattacksonuserexistence00000000000");
            _logger.LogWarning("Login failed — username not found or user inactive. Username: {Username}", username);
            return null;
        }

        if (!BCrypt.Net.BCrypt.Verify(password, user.PasswordHash))
        {
            _logger.LogWarning("Login failed — wrong password. UserId: {UserId}", user.Id);
            return null;
        }

        string accessToken = GenerateAccessToken(user);
        string refreshToken = GenerateRefreshToken();

        user.RefreshToken = refreshToken;
        user.RefreshTokenExpiry = DateTime.UtcNow.AddDays(_jwt.RefreshTokenExpiryDays);

        await _users.UpdateAsync(user, cancellationToken);
        await _users.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("User login success. UserId: {UserId}", user.Id);

        return (accessToken, refreshToken);
    }

    /// <inheritdoc />
    public async Task<(string AccessToken, string RefreshToken)?> RefreshAsync(
        string refreshToken,
        CancellationToken cancellationToken = default)
    {
        User? user = await _users.FindByRefreshTokenAsync(refreshToken, cancellationToken);

        if (user is null)
        {
            _logger.LogWarning("Refresh failed — token not found.");
            return null;
        }

        if (!user.IsActive)
        {
            _logger.LogWarning("Refresh failed — user is inactive. UserId: {UserId}", user.Id);
            return null;
        }

        if (user.RefreshTokenExpiry is null || user.RefreshTokenExpiry < DateTime.UtcNow)
        {
            _logger.LogWarning("Refresh failed — token expired. UserId: {UserId}", user.Id);
            // Clear the expired token so it cannot be retried.
            user.RefreshToken = null;
            user.RefreshTokenExpiry = null;
            await _users.UpdateAsync(user, cancellationToken);
            await _users.SaveChangesAsync(cancellationToken);
            return null;
        }

        // Rotate: issue new tokens and overwrite the stored refresh token.
        string newAccessToken = GenerateAccessToken(user);
        string newRefreshToken = GenerateRefreshToken();

        user.RefreshToken = newRefreshToken;
        user.RefreshTokenExpiry = DateTime.UtcNow.AddDays(_jwt.RefreshTokenExpiryDays);

        await _users.UpdateAsync(user, cancellationToken);
        await _users.SaveChangesAsync(cancellationToken);

        return (newAccessToken, newRefreshToken);
    }

    /// <inheritdoc />
    public async Task LogoutAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        User? user = await _users.GetByIdAsync(userId, cancellationToken);
        if (user is null)
            return;

        user.RefreshToken = null;
        user.RefreshTokenExpiry = null;

        await _users.UpdateAsync(user, cancellationToken);
        await _users.SaveChangesAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task<MeResponse> GetMeAsync(User user, CancellationToken cancellationToken = default)
    {
        return new MeResponse
        {
            UserId   = user.Id,
            FullName = user.FullName,
            Username = user.Username,
            Email    = user.Email,
            Role     = user.Role.ToString(),
            DivisionId = user.DivisionId,
            Division = user.Division?.Name,          // null for SuperAdmin/Admin
            OfficeId   = user.OfficeId,
            OfficeCode = user.Office?.OfficeCode,
            OfficeName = user.Office?.OfficeName,
            Position   = user.Position,
            CanAccessInventory      = await _permissions.CanAccessInventoryAsync(user, cancellationToken),
            CanAccessReports        = await _permissions.CanAccessReportsAsync(user, cancellationToken),
            CanManageUsers          = await _permissions.CanManageUsersAsync(user, cancellationToken),
            CanAccessProfile        = await _permissions.CanAccessProfileAsync(user, cancellationToken),
            CanManageResourceLinks  = await _permissions.CanManageResourceLinksAsync(user, cancellationToken),
            CanAccessBudgetPlanning = await _permissions.CanAccessBudgetPlanningAsync(user, cancellationToken),
            CanUploadAip            = await _permissions.CanUploadAipAsync(user, cancellationToken),
            CanManageConfig         = await _permissions.CanManageConfigAsync(user, cancellationToken),
            CanManageAllocation     = await _permissions.CanManageAllocationAsync(user, cancellationToken),
        };
    }

    // ── Private helpers ────────────────────────────────────────────────────────

    /// <summary>
    /// Builds a signed JWT with <c>sub</c>, <c>email</c>, <c>role</c>, and <c>div</c> claims.
    /// Expiry is <see cref="JwtSettings.AccessTokenExpiryMinutes"/> from now (UTC).
    /// </summary>
    private string GenerateAccessToken(User user)
    {
        byte[] keyBytes = Encoding.UTF8.GetBytes(_jwt.SecretKey);
        SymmetricSecurityKey key = new(keyBytes);
        SigningCredentials credentials = new(key, SecurityAlgorithms.HmacSha256);

        List<Claim> claims =
        [
            new Claim(JwtClaimNames.Sub,      user.Id.ToString()),
            new Claim(JwtClaimNames.Username, user.Username),
            new Claim(JwtClaimNames.Role,     ((int)user.Role).ToString()),
        ];

        // Email is optional — only emit the claim when present.
        if (user.Email is string email)
            claims.Add(new Claim(JwtClaimNames.Email, email));

        // Division id is null for SuperAdmin/Admin — only emit the div claim when present.
        // Scoping reads DivisionId from the loaded user, not this claim, so omitting is safe.
        if (user.DivisionId is int divisionId)
            claims.Add(new Claim(JwtClaimNames.Division, divisionId.ToString()));

        SecurityTokenDescriptor descriptor = new()
        {
            Subject            = new ClaimsIdentity(claims),
            Expires            = DateTime.UtcNow.AddMinutes(_jwt.AccessTokenExpiryMinutes),
            Issuer             = _jwt.Issuer,
            Audience           = _jwt.Audience,
            SigningCredentials = credentials,
        };

        JwtSecurityTokenHandler handler = new();
        SecurityToken token = handler.CreateToken(descriptor);
        return handler.WriteToken(token);
    }

    /// <summary>
    /// Returns a cryptographically random, URL-safe base64 string (64 random bytes → 88 chars).
    /// Never reuses values — safe to store directly in the database.
    /// </summary>
    private static string GenerateRefreshToken()
    {
        byte[] bytes = new byte[64];
        RandomNumberGenerator.Fill(bytes);
        return Convert.ToBase64String(bytes);
    }
}
