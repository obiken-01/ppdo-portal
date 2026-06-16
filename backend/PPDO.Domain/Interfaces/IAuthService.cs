using PPDO.Domain.Entities;

namespace PPDO.Domain.Interfaces;

/// <summary>
/// Authentication operations — login, token refresh, logout, and current-user info.
/// Implemented in <c>PPDO.Application/Services/AuthService.cs</c>.
///
/// Returns null on failure rather than throwing, allowing Function handlers to
/// return the appropriate HTTP status code without try/catch boilerplate.
/// </summary>
public interface IAuthService
{
    /// <summary>
    /// Validates username + password and issues a new access token and refresh token.
    /// Returns null when credentials are invalid or the user is inactive.
    /// </summary>
    /// <param name="username">The login username (case-insensitive).</param>
    /// <param name="password">The plain-text password to verify against the stored BCrypt hash.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Tokens on success; null on failure.</returns>
    Task<(string AccessToken, string RefreshToken)?> LoginAsync(
        string username,
        string password,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Validates the supplied refresh token, rotates it (issues a new one), and
    /// returns a new access token + refresh token pair.
    /// Returns null when the token is not found, expired, or belongs to an inactive user.
    /// </summary>
    Task<(string AccessToken, string RefreshToken)?> RefreshAsync(
        string refreshToken,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Clears the refresh token for the authenticated user, effectively logging them out.
    /// Idempotent — safe to call when the token is already null.
    /// </summary>
    Task LogoutAsync(Guid userId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Builds the current-user response DTO from a pre-loaded <see cref="User"/> entity.
    /// The user must have <see cref="User.Group"/> navigation loaded (guaranteed by
    /// <c>JwtMiddleware.ValidateAsync</c>).
    /// </summary>
    Task<MeResponse> GetMeAsync(User user, CancellationToken cancellationToken = default);
}

/// <summary>
/// Projection returned by <see cref="IAuthService.GetMeAsync"/>.
/// Contains identity, profile, and effective permission flags for the authenticated user.
/// </summary>
public sealed class MeResponse
{
    public Guid UserId { get; init; }
    public string FullName { get; init; } = string.Empty;
    public string Username { get; init; } = string.Empty;
    public string? Email { get; init; }
    public string Role { get; init; } = string.Empty;
    /// <summary>Division name, or null for non-PPDO office users.</summary>
    public string? Division { get; init; }

    /// <summary>Provincial office id, or null for PPDO-internal users. New in v1.1.</summary>
    public int? OfficeId { get; init; }

    /// <summary>Short office code, e.g. "PEO". Null for PPDO-internal users.</summary>
    public string? OfficeCode { get; init; }

    /// <summary>Full office name. Null for PPDO-internal users.</summary>
    public string? OfficeName { get; init; }

    public string? Position { get; init; }

    // -- Effective permission flags (resolved via PermissionService) --------
    public bool CanAccessInventory { get; init; }
    public bool CanAccessReports { get; init; }
    public bool CanManageUsers { get; init; }
    public bool CanAccessProfile { get; init; }
    public bool CanManageResourceLinks { get; init; }
    public bool CanAccessBudgetPlanning { get; init; }
    public bool CanUploadAip { get; init; }
    public bool CanManageConfig { get; init; }
}
