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
    /// On failure the <see cref="RefreshResult.Outcome"/> distinguishes why, so the
    /// caller can explain the logout instead of a bare 401 (RAL-198).
    /// </summary>
    Task<RefreshResult> RefreshAsync(
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

    /// <summary>
    /// Returns the recovery-question text to show for a username, for the "Forgot password?"
    /// flow (RAL-265). Always returns a question — an unknown username or an account that
    /// hasn't set one yet gets a question deterministically derived from the username itself
    /// (same fake username always gets the same fake question, spread uniformly across the
    /// catalog), so no single response value can be used to test whether a username exists.
    /// </summary>
    Task<string> GetRecoveryQuestionAsync(string username, CancellationToken cancellationToken = default);

    /// <summary>
    /// Verifies a recovery answer and, on success, issues a random one-time password
    /// (RAL-265). Every failure path — unknown username, no recovery answer set, wrong
    /// answer, locked out — returns the exact same <see cref="RecoveryVerifyOutcome.Failed"/>
    /// outcome so the caller cannot distinguish them.
    /// </summary>
    Task<RecoveryVerifyResult> VerifyRecoveryAnswerAsync(
        string username,
        string answer,
        CancellationToken cancellationToken = default);
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

/// <summary>The outcome of a <see cref="IAuthService.RefreshAsync"/> call (RAL-198).</summary>
public enum RefreshOutcome
{
    /// <summary>Token matched and was not expired — new tokens issued.</summary>
    Success,

    /// <summary>
    /// No user row has this exact token stored — it was rotated away by a later
    /// login or refresh (someone else signed into the account, or another
    /// device/tab refreshed first). Distinct from <see cref="TokenExpired"/> so the
    /// caller can explain the logout instead of a bare 401.
    /// </summary>
    TokenSuperseded,

    /// <summary>Token matched but is past its <c>RefreshTokenExpiry</c> (natural 7-day expiry).</summary>
    TokenExpired,

    /// <summary>Token matched but the owning user is no longer active. No specific reason is surfaced to the client.</summary>
    Failed,
}

/// <summary>
/// Result of <see cref="IAuthService.RefreshAsync"/>. On <see cref="RefreshOutcome.Success"/>
/// the tokens are populated; otherwise <see cref="Outcome"/> tells the caller why.
/// </summary>
public readonly record struct RefreshResult
{
    public RefreshOutcome Outcome { get; init; }
    public string? AccessToken { get; init; }
    public string? RefreshToken { get; init; }

    public static RefreshResult Success(string accessToken, string refreshToken) => new()
    {
        Outcome      = RefreshOutcome.Success,
        AccessToken  = accessToken,
        RefreshToken = refreshToken,
    };

    public static RefreshResult Superseded() => new() { Outcome = RefreshOutcome.TokenSuperseded };

    public static RefreshResult Expired() => new() { Outcome = RefreshOutcome.TokenExpired };

    public static RefreshResult Failed() => new() { Outcome = RefreshOutcome.Failed };
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

    /// <summary>
    /// Whether this user belongs to the host office and so holds cross-office authority
    /// (DECISION F, RAL-258). Replaces <c>OfficeId == null</c>, which meant the same thing by
    /// proxy until every user gained an office.
    /// </summary>
    public bool IsHostOffice { get; init; }

    public string? Position { get; init; }

    /// <summary>
    /// Portal route this user should land on after signing in (RAL-251/RAL-261), resolved
    /// server-side through user → division → office → first reachable → /account.
    /// Always a route the user can actually reach, so it is safe to redirect to directly.
    /// </summary>
    public string LandingPath { get; init; } = "/account";

    /// <summary>
    /// The user's own stored preference as an enum name, or null when unset. The /account
    /// selector shows this; <see cref="LandingPath"/> is where they will actually land.
    /// </summary>
    public string? LandingPage { get; init; }

    // -- Effective permission flags (resolved via PermissionService) --------
    public bool CanAccessInventory { get; init; }
    public bool CanAccessReports { get; init; }
    public bool CanManageUsers { get; init; }
    public bool CanAccessProfile { get; init; }
    public bool CanManageResourceLinks { get; init; }
    public bool CanAccessBudgetPlanning { get; init; }
    public bool CanUploadAip { get; init; }
    public bool CanManageConfig { get; init; }
    public bool CanManagePpdoAllocation { get; init; }
    public bool CanManagePboCeiling { get; init; }
    public bool CanReviewBudgetPlanning { get; init; }

    // -- Password / recovery gates (RAL-266/RAL-267) ---------------------------

    /// <summary>True after a reset — the portal blocks everything except changing the password.</summary>
    public bool MustChangePassword { get; init; }

    /// <summary>True when no recovery question is set yet — the portal blocks everything except setup.</summary>
    public bool NeedsRecoverySetup { get; init; }

    /// <summary>UTC timestamp of the most recent reset, only when not yet acknowledged. Non-blocking.</summary>
    public DateTime? UnacknowledgedPasswordResetAt { get; init; }
}

/// <summary>The outcome of a <see cref="IAuthService.VerifyRecoveryAnswerAsync"/> call.</summary>
public enum RecoveryVerifyOutcome
{
    /// <summary>Answer matched — a new temporary password was issued.</summary>
    Success,

    /// <summary>
    /// Unknown username, no recovery answer set on the account, wrong answer, or the
    /// account is locked out. Deliberately one outcome for all four — see RAL-265's
    /// enumeration-guard note.
    /// </summary>
    Failed,
}

/// <summary>Result of <see cref="IAuthService.VerifyRecoveryAnswerAsync"/>.</summary>
public readonly record struct RecoveryVerifyResult
{
    public RecoveryVerifyOutcome Outcome { get; init; }
    public string? TemporaryPassword { get; init; }

    public static RecoveryVerifyResult Success(string temporaryPassword) => new()
    {
        Outcome            = RecoveryVerifyOutcome.Success,
        TemporaryPassword  = temporaryPassword,
    };

    public static RecoveryVerifyResult Failed() => new() { Outcome = RecoveryVerifyOutcome.Failed };
}
