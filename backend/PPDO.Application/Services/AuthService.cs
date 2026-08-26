using System.IdentityModel.Tokens.Jwt;
using System.Linq;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using PPDO.Application.Common;
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
    // Rate limiting: refuse logins after this many failed attempts per username
    // within a fixed window (RAL-58). State is held in IMemoryCache.
    private const int MaxFailedAttempts = 5;
    private static readonly TimeSpan LockoutWindow = TimeSpan.FromMinutes(15);

    // Recovery-answer lockout (RAL-265): 5 failures per hour, persisted on the User row
    // itself (RecoveryAttemptCount/RecoveryFirstAttemptAt) rather than IMemoryCache — the
    // Consumption plan scales to zero after ~10 min idle, which would silently reset an
    // in-memory counter and defeat the lockout.
    private const int MaxRecoveryAttempts = 5;
    private static readonly TimeSpan RecoveryLockoutWindow = TimeSpan.FromHours(1);

    // BCrypt.Verify against a fixed hash nobody could ever match — run on every "nothing to
    // check against" branch so the response takes the same time as a real comparison and
    // cannot be used to infer whether the username or the recovery answer exists.
    private const string DummyHash = "$2a$11$dummyhashtopreventtimingattacksonuserexistence00000000000";

    private readonly IUserRepository _users;
    private readonly IPermissionService _permissions;
    private readonly ILandingPageResolver _landing;
    private readonly IAuditService _audit;
    private readonly JwtSettings _jwt;
    private readonly IMemoryCache _cache;
    private readonly ILogger<AuthService> _logger;

    public AuthService(
        IUserRepository users,
        IPermissionService permissions,
        ILandingPageResolver landing,
        IAuditService audit,
        IOptions<JwtSettings> jwtOptions,
        IMemoryCache cache,
        ILogger<AuthService> logger)
    {
        _users = users;
        _permissions = permissions;
        _landing = landing;
        _audit = audit;
        _jwt = jwtOptions.Value;
        _cache = cache;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<LoginResult> LoginAsync(
        string username,
        string password,
        CancellationToken cancellationToken = default)
    {
        string attemptKey = AttemptKey(username);

        // Block before doing any DB/BCrypt work once the lockout threshold is hit.
        if (_cache.TryGetValue(attemptKey, out LoginAttempt? attempt)
            && attempt is not null
            && attempt.Count >= MaxFailedAttempts)
        {
            _logger.LogWarning("Login blocked — too many failed attempts. Username: {Username}", username);
            return LoginResult.RateLimited(RetryAfterSeconds(attempt));
        }

        User? user = await _users.FindByUsernameAsync(username, cancellationToken);

        if (user is null)
        {
            // Consistent timing — run a dummy verify so response time doesn't leak existence.
            BCrypt.Net.BCrypt.Verify(password, DummyHash);
            _logger.LogWarning("Login failed — username not found or user inactive. Username: {Username}", username);
            RegisterFailedAttempt(attemptKey);
            return LoginResult.Invalid();
        }

        if (!BCrypt.Net.BCrypt.Verify(password, user.PasswordHash))
        {
            _logger.LogWarning("Login failed — wrong password. UserId: {UserId}", user.Id);
            RegisterFailedAttempt(attemptKey);
            return LoginResult.Invalid();
        }

        string accessToken = GenerateAccessToken(user);
        string refreshToken = GenerateRefreshToken();

        user.RefreshToken = refreshToken;
        user.RefreshTokenExpiry = DateTime.UtcNow.AddDays(_jwt.RefreshTokenExpiryDays);

        await _users.UpdateAsync(user, cancellationToken);
        await _users.SaveChangesAsync(cancellationToken);

        _cache.Remove(attemptKey); // successful login clears the failed-attempt counter
        _logger.LogInformation("User login success. UserId: {UserId}", user.Id);

        return LoginResult.Success(accessToken, refreshToken);
    }

    /// <inheritdoc />
    public async Task<RefreshResult> RefreshAsync(
        string refreshToken,
        CancellationToken cancellationToken = default)
    {
        User? user = await _users.FindByRefreshTokenAsync(refreshToken, cancellationToken);

        if (user is null)
        {
            // No row has this exact token — it was overwritten by a later login/refresh
            // (rotation-on-use). Distinct from expiry so the client can explain why (RAL-198).
            _logger.LogWarning("Refresh failed — token superseded (no matching row).");
            return RefreshResult.Superseded();
        }

        if (!user.IsActive)
        {
            _logger.LogWarning("Refresh failed — user is inactive. UserId: {UserId}", user.Id);
            return RefreshResult.Failed();
        }

        if (user.RefreshTokenExpiry is null || user.RefreshTokenExpiry < DateTime.UtcNow)
        {
            _logger.LogWarning("Refresh failed — token expired. UserId: {UserId}", user.Id);
            // Clear the expired token so it cannot be retried.
            user.RefreshToken = null;
            user.RefreshTokenExpiry = null;
            await _users.UpdateAsync(user, cancellationToken);
            await _users.SaveChangesAsync(cancellationToken);
            return RefreshResult.Expired();
        }

        // Rotate: issue new tokens and overwrite the stored refresh token.
        string newAccessToken = GenerateAccessToken(user);
        string newRefreshToken = GenerateRefreshToken();

        user.RefreshToken = newRefreshToken;
        user.RefreshTokenExpiry = DateTime.UtcNow.AddDays(_jwt.RefreshTokenExpiryDays);

        await _users.UpdateAsync(user, cancellationToken);
        await _users.SaveChangesAsync(cancellationToken);

        return RefreshResult.Success(newAccessToken, newRefreshToken);
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
            IsHostOffice = OfficeScope.IsHostOfficeUser(user),
            Position   = user.Position,
            LandingPage = user.LandingPage?.ToString(),
            LandingPath = LandingPageRoutes.PathFor(
                await _landing.ResolveAsync(user, cancellationToken)),
            CanAccessInventory      = await _permissions.CanAccessInventoryAsync(user, cancellationToken),
            CanAccessReports        = await _permissions.CanAccessReportsAsync(user, cancellationToken),
            CanManageUsers          = await _permissions.CanManageUsersAsync(user, cancellationToken),
            CanAccessProfile        = await _permissions.CanAccessProfileAsync(user, cancellationToken),
            CanManageResourceLinks  = await _permissions.CanManageResourceLinksAsync(user, cancellationToken),
            CanAccessBudgetPlanning = await _permissions.CanAccessBudgetPlanningAsync(user, cancellationToken),
            CanUploadAip            = await _permissions.CanUploadAipAsync(user, cancellationToken),
            CanManageConfig         = await _permissions.CanManageConfigAsync(user, cancellationToken),
            CanManageAllocation     = await _permissions.CanManageAllocationAsync(user, cancellationToken),
            MustChangePassword      = user.MustChangePassword,
            NeedsRecoverySetup      = user.RecoveryQuestionKey is null,
            UnacknowledgedPasswordResetAt = UnacknowledgedResetAt(user),
        };
    }

    /// <summary>
    /// Null unless there's a reset the user hasn't dismissed yet — either they've never
    /// acknowledged one, or a newer reset happened since their last acknowledgement.
    /// </summary>
    private static DateTime? UnacknowledgedResetAt(User user)
    {
        if (user.LastPasswordResetAt is not DateTime resetAt)
            return null;

        bool alreadyAcknowledged =
            user.PasswordResetAcknowledgedAt is DateTime ackAt && ackAt >= resetAt;

        return alreadyAcknowledged ? null : resetAt;
    }

    // ── Password recovery (RAL-265) ────────────────────────────────────────────

    /// <inheritdoc />
    public async Task<string> GetRecoveryQuestionAsync(
        string username,
        CancellationToken cancellationToken = default)
    {
        User? user = await _users.FindByUsernameAsync(username, cancellationToken);

        // Unknown username, or a known account that hasn't set a question yet — both need a
        // question to show that is indistinguishable from a real one. A single fixed default
        // (e.g. always BirthTown) would fail that: any account that picked one of the OTHER
        // catalog questions would return text no unknown/unset username could ever produce,
        // confirming the account exists. Instead, derive a fake question deterministically
        // from the username itself — same fake username always gets the same fake question
        // (so repeated calls don't leak inconsistency), and the pick lands uniformly across
        // the same catalog a real answer would, so no single response value is a tell.
        RecoveryQuestion question = user?.RecoveryQuestionKey ?? FakeQuestionFor(username);
        return RecoveryQuestionCatalog.TextFor(question);
    }

    /// <summary>
    /// Deterministically maps a username to one of the catalog questions, for accounts that
    /// don't have a real one yet. Not a secret — just needs to be stable per username and
    /// spread uniformly across the catalog (RAL-265 enumeration guard).
    /// </summary>
    private static RecoveryQuestion FakeQuestionFor(string username)
    {
        RecoveryQuestion[] questions = RecoveryQuestionCatalog.All.Keys.ToArray();
        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(username.Trim().ToLowerInvariant()));
        int index = hash[0] % questions.Length;
        return questions[index];
    }

    /// <inheritdoc />
    public async Task<RecoveryVerifyResult> VerifyRecoveryAnswerAsync(
        string username,
        string answer,
        CancellationToken cancellationToken = default)
    {
        User? user = await _users.FindByUsernameAsync(username, cancellationToken);
        string normalizedAnswer = RecoveryAnswerNormalizer.Normalize(answer);

        // Unknown username, or no recovery answer set yet — nothing to check the answer
        // against, and no row to write to. Still run a dummy compare so this branch takes
        // about as long as a real BCrypt check. It is still measurably cheaper than the two
        // branches below, which each do a real DB write — an inherent gap for "does this
        // username exist at all" that a dummy compare alone can't close. The write is real
        // and needed for lockout durability (see RegisterFailedRecoveryAttempt), so it isn't
        // removed to chase full parity; the two real-account branches are kept equal instead
        // (see below), which is what RAL-265 explicitly calls out ("never surface locked as
        // a distinct state").
        if (user is null || user.RecoveryAnswerHash is null)
        {
            BCrypt.Net.BCrypt.Verify(normalizedAnswer, DummyHash);
            _logger.LogWarning("Recovery verify failed — unknown username or no recovery answer set.");
            return RecoveryVerifyResult.Failed();
        }

        // Locked out — still compare against the real hash, and still write, so this branch
        // costs exactly the same as the wrong-answer branch below (same BCrypt compare, same
        // UpdateAsync/SaveChangesAsync pair). Repository<T>.UpdateAsync calls DbSet.Update(),
        // which marks the whole entity Modified and forces a real UPDATE even though no field
        // changed here — this is a deliberate no-op write for timing parity, not a bug.
        // Never surface "locked" as a distinct state — that alone would confirm the account
        // exists and is being targeted (RAL-265).
        if (IsRecoveryLockedOut(user))
        {
            BCrypt.Net.BCrypt.Verify(normalizedAnswer, user.RecoveryAnswerHash);
            await _users.UpdateAsync(user, cancellationToken);
            await _users.SaveChangesAsync(cancellationToken);
            _logger.LogWarning("Recovery verify blocked — too many failed attempts. UserId: {UserId}", user.Id);
            return RecoveryVerifyResult.Failed();
        }

        if (!BCrypt.Net.BCrypt.Verify(normalizedAnswer, user.RecoveryAnswerHash))
        {
            RegisterFailedRecoveryAttempt(user);
            await _users.UpdateAsync(user, cancellationToken);
            await _users.SaveChangesAsync(cancellationToken);
            _logger.LogWarning("Recovery verify failed — wrong answer. UserId: {UserId}", user.Id);
            return RecoveryVerifyResult.Failed();
        }

        // Issued once, shown once — never stored or logged in plaintext (RAL-254 convention).
        string temporaryPassword = PasswordGenerator.Generate();

        user.PasswordHash          = BCrypt.Net.BCrypt.HashPassword(temporaryPassword);
        user.MustChangePassword    = true;
        user.RecoveryAttemptCount  = 0;
        user.RecoveryFirstAttemptAt = null;
        user.RefreshToken          = null; // force re-login on every other session
        user.RefreshTokenExpiry    = null;
        // Surface the "your password was reset" notice at next login (RAL-267). A fresh
        // reset always needs re-acknowledging, even if a previous one was already dismissed.
        user.LastPasswordResetAt        = DateTime.UtcNow;
        user.PasswordResetAcknowledgedAt = null;

        await _users.UpdateAsync(user, cancellationToken);
        await _users.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("Password reset via recovery answer. UserId: {UserId}", user.Id);

        // Actor is the account itself — this is a public endpoint, so JwtMiddleware never
        // ran and CallerContext.UserId is unset. Never snapshot PasswordHash or the issued
        // password, just that a reset happened (matches the admin-reset audit shape).
        await _audit.LogAsync("users", user.Id, AuditAction.Update, actorId: user.Id,
            oldValues: null,
            newValues: new { PasswordReset = true, Method = "recovery-answer" },
            cancellationToken);

        return RecoveryVerifyResult.Success(temporaryPassword);
    }

    private static bool IsRecoveryLockedOut(User user) =>
        user.RecoveryAttemptCount >= MaxRecoveryAttempts
        && user.RecoveryFirstAttemptAt is DateTime first
        && DateTime.UtcNow < first.Add(RecoveryLockoutWindow);

    /// <summary>
    /// The first failure opens a fixed one-hour window; subsequent failures within that
    /// window keep incrementing. A failure after the window has elapsed starts a fresh one —
    /// exactly the "5 failures in an hour" shape <see cref="RegisterFailedAttempt"/> uses for
    /// login, just persisted on the row instead of IMemoryCache.
    /// </summary>
    private static void RegisterFailedRecoveryAttempt(User user)
    {
        bool windowActive = user.RecoveryFirstAttemptAt is DateTime first
            && DateTime.UtcNow < first.Add(RecoveryLockoutWindow);

        if (windowActive)
        {
            user.RecoveryAttemptCount += 1;
        }
        else
        {
            user.RecoveryAttemptCount = 1;
            user.RecoveryFirstAttemptAt = DateTime.UtcNow;
        }
    }

    // ── Rate limiting ────────────────────────────────────────────────────────────

    /// <summary>Failed-attempt counter for a username, with the fixed window expiry.</summary>
    private sealed record LoginAttempt(int Count, DateTimeOffset ExpiresAtUtc);

    private static string AttemptKey(string username) =>
        $"login-attempts:{username.Trim().ToLowerInvariant()}";

    /// <summary>
    /// Increments the failed-attempt counter for the key. The first failure opens a
    /// fixed <see cref="LockoutWindow"/>; subsequent failures keep the same expiry so the
    /// window is exactly N attempts per window from the first failure.
    /// </summary>
    private void RegisterFailedAttempt(string key)
    {
        LoginAttempt attempt =
            _cache.TryGetValue(key, out LoginAttempt? existing) && existing is not null
                ? existing with { Count = existing.Count + 1 }
                : new LoginAttempt(1, DateTimeOffset.UtcNow.Add(LockoutWindow));

        _cache.Set(key, attempt, attempt.ExpiresAtUtc);
    }

    private static int RetryAfterSeconds(LoginAttempt attempt)
    {
        double seconds = (attempt.ExpiresAtUtc - DateTimeOffset.UtcNow).TotalSeconds;
        return Math.Max(1, (int)Math.Ceiling(seconds));
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
