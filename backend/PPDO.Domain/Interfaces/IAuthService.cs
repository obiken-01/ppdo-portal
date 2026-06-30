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
    /// Tracks failed attempts per username and refuses further attempts once the
    /// rate-limit threshold is exceeded (see <see cref="LoginOutcome.RateLimited"/>).
    /// </summary>
    /// <param name="username">The login username (case-insensitive).</param>
    /// <param name="password">The plain-text password to verify against the stored BCrypt hash.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>
    /// A <see cref="LoginResult"/> describing the outcome: tokens on success,
    /// invalid credentials, or rate-limited (with a retry-after hint).
    /// </returns>
    Task<LoginResult> LoginAsync(
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

/// <summary>The outcome of a <see cref="IAuthService.LoginAsync"/> call.</summary>
public enum LoginOutcome
{
    /// <summary>Credentials valid — tokens issued.</summary>
    Success,

    /// <summary>Username unknown, user inactive, or password wrong.</summary>
    InvalidCredentials,

    /// <summary>Too many failed attempts for this username — login refused.</summary>
    RateLimited,
}

/// <summary>
/// Result of <see cref="IAuthService.LoginAsync"/>. On <see cref="LoginOutcome.Success"/>
/// the tokens are populated; on <see cref="LoginOutcome.RateLimited"/> the
/// <see cref="RetryAfterSeconds"/> hint indicates how long the caller should wait.
/// </summary>
public readonly record struct LoginResult
{
    public LoginOutcome Outcome { get; init; }
    public string? AccessToken { get; init; }
    public string? RefreshToken { get; init; }

    /// <summary>Seconds until the lockout window clears (only set when rate-limited).</summary>
    public int RetryAfterSeconds { get; init; }

    public static LoginResult Success(string accessToken, string refreshToken) => new()
    {
        Outcome      = LoginOutcome.Success,
        AccessToken  = accessToken,
        RefreshToken = refreshToken,
    };

    public static LoginResult Invalid() => new() { Outcome = LoginOutcome.InvalidCredentials };

    public static LoginResult RateLimited(int retryAfterSeconds) => new()
    {
        Outcome           = LoginOutcome.RateLimited,
        RetryAfterSeconds = retryAfterSeconds,
    };
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

    /// <summary>Division id, or null for SuperAdmin/Admin (no division).</summary>
    public int? DivisionId { get; init; }

    /// <summary>Division name, or null for SuperAdmin/Admin.</summary>
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
    public bool CanManageAllocation { get; init; }
}
