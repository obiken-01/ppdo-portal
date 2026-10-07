using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using PPDO.Application.Common;
using PPDO.Application.Services;
using PPDO.Application.Settings;
using PPDO.Domain.Entities;
using PPDO.Domain.Enums;
using PPDO.Domain.Interfaces;

namespace PPDO.Tests.Application;

/// <summary>
/// Unit tests for <see cref="AuthService"/>.
/// IUserRepository is mocked; IPermissionService uses the real implementation.
/// Coverage target: 80% (Application/Service layer).
/// </summary>
public sealed class AuthServiceTests
{
    // ── Fixtures ──────────────────────────────────────────────────────────────

    private static readonly JwtSettings JwtSettings = new()
    {
        SecretKey                = "test-secret-key-minimum-32-characters-long!",
        Issuer                   = "http://localhost:4280",
        Audience                 = "ppdo-portal",
        AccessTokenExpiryMinutes = 15,
        RefreshTokenExpiryDays   = 7,
    };

    private static User MakeActiveUser(string passwordHash) => new()
    {
        Id           = Guid.NewGuid(),
        FullName     = "Test User",
        Username     = "testuser",
        Email        = "test@ppdo.gov.ph",
        PasswordHash = passwordHash,
        Role         = UserRole.Admin,
        DivisionId   = null,
        IsActive     = true,
    };

    private static AuthService BuildSut(
        Mock<IUserRepository> repoMock,
        IMemoryCache? cache = null,
        Mock<IAuditService>? auditMock = null) => new(
        repoMock.Object,
        new PermissionService(),
        new LandingPageResolver(new PermissionService()),
        (auditMock ?? new Mock<IAuditService>()).Object,
        Options.Create(JwtSettings),
        cache ?? new MemoryCache(new MemoryCacheOptions()),
        NullLogger<AuthService>.Instance);

    // ── LoginAsync ────────────────────────────────────────────────────────────

    [Fact]
    public async Task LoginAsync_UsernameNotFound_ReturnsInvalid()
    {
        Mock<IUserRepository> repo = new();
        repo.Setup(r => r.FindByUsernameAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((User?)null);

        LoginResult result = await BuildSut(repo).LoginAsync("nobody", "pass");

        Assert.Equal(LoginOutcome.InvalidCredentials, result.Outcome);
        Assert.Null(result.AccessToken);
        Assert.Null(result.RefreshToken);
    }

    [Fact]
    public async Task LoginAsync_WrongPassword_ReturnsInvalid()
    {
        string correctHash = BCrypt.Net.BCrypt.HashPassword("correct");
        User user = MakeActiveUser(correctHash);

        Mock<IUserRepository> repo = new();
        repo.Setup(r => r.FindByUsernameAsync(user.Username, It.IsAny<CancellationToken>()))
            .ReturnsAsync(user);

        LoginResult result = await BuildSut(repo).LoginAsync(user.Username, "wrong");

        Assert.Equal(LoginOutcome.InvalidCredentials, result.Outcome);
    }

    [Fact]
    public async Task LoginAsync_ValidCredentials_ReturnsTokenPair()
    {
        string password = "Test-Password1!";
        string hash = BCrypt.Net.BCrypt.HashPassword(password);
        User user = MakeActiveUser(hash);

        Mock<IUserRepository> repo = new();
        repo.Setup(r => r.FindByUsernameAsync(user.Username, It.IsAny<CancellationToken>()))
            .ReturnsAsync(user);
        repo.Setup(r => r.UpdateAsync(user, It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        repo.Setup(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);

        LoginResult result = await BuildSut(repo).LoginAsync(user.Username, password);

        Assert.Equal(LoginOutcome.Success, result.Outcome);
        Assert.False(string.IsNullOrWhiteSpace(result.AccessToken));
        Assert.False(string.IsNullOrWhiteSpace(result.RefreshToken));
    }

    // ── LoginAsync — rate limiting (RAL-58) ─────────────────────────────────────

    [Fact]
    public async Task LoginAsync_ExceedsMaxFailedAttempts_ReturnsRateLimited()
    {
        User user = MakeActiveUser(BCrypt.Net.BCrypt.HashPassword("correct"));
        Mock<IUserRepository> repo = new();
        repo.Setup(r => r.FindByUsernameAsync(user.Username, It.IsAny<CancellationToken>()))
            .ReturnsAsync(user);

        AuthService sut = BuildSut(repo);

        // 5 failed attempts are all reported as invalid credentials …
        for (int i = 0; i < 5; i++)
        {
            LoginResult fail = await sut.LoginAsync(user.Username, "wrong");
            Assert.Equal(LoginOutcome.InvalidCredentials, fail.Outcome);
        }

        // … the 6th is blocked.
        LoginResult blocked = await sut.LoginAsync(user.Username, "wrong");
        Assert.Equal(LoginOutcome.RateLimited, blocked.Outcome);
        Assert.True(blocked.RetryAfterSeconds > 0);
    }

    [Fact]
    public async Task LoginAsync_RateLimited_BlocksEvenCorrectPassword()
    {
        string password = "Test-Password1!";
        User user = MakeActiveUser(BCrypt.Net.BCrypt.HashPassword(password));
        Mock<IUserRepository> repo = new();
        repo.Setup(r => r.FindByUsernameAsync(user.Username, It.IsAny<CancellationToken>()))
            .ReturnsAsync(user);
        repo.Setup(r => r.UpdateAsync(user, It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        repo.Setup(r => r.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);

        AuthService sut = BuildSut(repo);
        for (int i = 0; i < 5; i++)
            await sut.LoginAsync(user.Username, "wrong");

        // Once locked out, even the correct password is refused.
        LoginResult result = await sut.LoginAsync(user.Username, password);
        Assert.Equal(LoginOutcome.RateLimited, result.Outcome);
    }

    [Fact]
    public async Task LoginAsync_SuccessfulLogin_ResetsFailedAttemptCounter()
    {
        string password = "Test-Password1!";
        User user = MakeActiveUser(BCrypt.Net.BCrypt.HashPassword(password));
        Mock<IUserRepository> repo = new();
        repo.Setup(r => r.FindByUsernameAsync(user.Username, It.IsAny<CancellationToken>()))
            .ReturnsAsync(user);
        repo.Setup(r => r.UpdateAsync(user, It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        repo.Setup(r => r.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);

        AuthService sut = BuildSut(repo);

        // 4 failures — one short of the lockout threshold.
        for (int i = 0; i < 4; i++)
            Assert.Equal(LoginOutcome.InvalidCredentials, (await sut.LoginAsync(user.Username, "wrong")).Outcome);

        // A success clears the counter …
        Assert.Equal(LoginOutcome.Success, (await sut.LoginAsync(user.Username, password)).Outcome);

        // … so four more failures are still merely invalid, never rate-limited.
        for (int i = 0; i < 4; i++)
            Assert.Equal(LoginOutcome.InvalidCredentials, (await sut.LoginAsync(user.Username, "wrong")).Outcome);
    }

    [Fact]
    public async Task LoginAsync_RateLimit_IsScopedPerUsername()
    {
        User userA = MakeActiveUser(BCrypt.Net.BCrypt.HashPassword("secret"));
        userA.Username = "usera";

        Mock<IUserRepository> repo = new();
        repo.Setup(r => r.FindByUsernameAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string u, CancellationToken _) => u == "usera" ? userA : null);

        AuthService sut = BuildSut(repo);

        // Lock out "usera".
        for (int i = 0; i < 5; i++)
            await sut.LoginAsync("usera", "wrong");
        Assert.Equal(LoginOutcome.RateLimited, (await sut.LoginAsync("usera", "wrong")).Outcome);

        // A different username is unaffected.
        Assert.Equal(LoginOutcome.InvalidCredentials, (await sut.LoginAsync("userb", "wrong")).Outcome);
    }

    [Fact]
    public async Task LoginAsync_ValidCredentials_StoresRefreshTokenOnUser()
    {
        string password = "Test-Password1!";
        User user = MakeActiveUser(BCrypt.Net.BCrypt.HashPassword(password));

        Mock<IUserRepository> repo = new();
        repo.Setup(r => r.FindByUsernameAsync(user.Username, It.IsAny<CancellationToken>()))
            .ReturnsAsync(user);
        repo.Setup(r => r.UpdateAsync(user, It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        repo.Setup(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);

        await BuildSut(repo).LoginAsync(user.Username, password);

        Assert.NotNull(user.RefreshToken);
        Assert.NotNull(user.RefreshTokenExpiry);
    }

    // ── RefreshAsync ──────────────────────────────────────────────────────────

    [Fact]
    public async Task RefreshAsync_TokenNotFound_ReturnsSuperseded()
    {
        Mock<IUserRepository> repo = new();
        repo.Setup(r => r.FindByRefreshTokenAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((User?)null);

        RefreshResult result = await BuildSut(repo).RefreshAsync("nonexistent-token");

        Assert.Equal(RefreshOutcome.TokenSuperseded, result.Outcome);
        Assert.Null(result.AccessToken);
        Assert.Null(result.RefreshToken);
    }

    [Fact]
    public async Task RefreshAsync_InactiveUser_ReturnsFailed()
    {
        User user = MakeActiveUser("hash");
        user.IsActive = false;
        user.RefreshToken = RefreshTokenHasher.Hash("some-token");
        user.RefreshTokenExpiry = DateTime.UtcNow.AddDays(7);

        Mock<IUserRepository> repo = new();
        repo.Setup(r => r.FindByRefreshTokenAsync(RefreshTokenHasher.Hash("some-token"), It.IsAny<CancellationToken>()))
            .ReturnsAsync(user);

        RefreshResult result = await BuildSut(repo).RefreshAsync("some-token");

        Assert.Equal(RefreshOutcome.Failed, result.Outcome);
    }

    [Fact]
    public async Task RefreshAsync_ExpiredToken_ReturnsExpired_AndClearsToken()
    {
        User user = MakeActiveUser("hash");
        user.RefreshToken = RefreshTokenHasher.Hash("expired-token");
        user.RefreshTokenExpiry = DateTime.UtcNow.AddDays(-1); // already expired

        Mock<IUserRepository> repo = new();
        repo.Setup(r => r.FindByRefreshTokenAsync(RefreshTokenHasher.Hash("expired-token"), It.IsAny<CancellationToken>()))
            .ReturnsAsync(user);
        repo.Setup(r => r.UpdateAsync(user, It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        repo.Setup(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);

        RefreshResult result = await BuildSut(repo).RefreshAsync("expired-token");

        Assert.Equal(RefreshOutcome.TokenExpired, result.Outcome);
        Assert.Null(user.RefreshToken);
        Assert.Null(user.RefreshTokenExpiry);
    }

    [Fact]
    public async Task RefreshAsync_ValidToken_ReturnsNewTokenPair()
    {
        User user = MakeActiveUser("hash");
        string oldRefreshToken = "valid-token";
        user.RefreshToken = RefreshTokenHasher.Hash(oldRefreshToken);
        user.RefreshTokenExpiry = DateTime.UtcNow.AddDays(7);

        Mock<IUserRepository> repo = new();
        repo.Setup(r => r.FindByRefreshTokenAsync(RefreshTokenHasher.Hash(oldRefreshToken), It.IsAny<CancellationToken>()))
            .ReturnsAsync(user);
        repo.Setup(r => r.UpdateAsync(user, It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        repo.Setup(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);

        RefreshResult result = await BuildSut(repo).RefreshAsync(oldRefreshToken);

        Assert.Equal(RefreshOutcome.Success, result.Outcome);
        Assert.False(string.IsNullOrWhiteSpace(result.AccessToken));
        Assert.NotEqual(oldRefreshToken, result.RefreshToken); // token rotated
    }

    [Fact]
    public async Task RefreshAsync_SupersededByNewerLogin_DistinguishesFromExpiry()
    {
        // Simulates the RAL-198 scenario: account shared across two sessions.
        // Session A holds R1; the account then logs in elsewhere and R1 is overwritten by
        // R2. Session A's next refresh presents R1, which no row matches any more —
        // this must surface as "superseded", not the generic "expired" reason.
        User user = MakeActiveUser("hash");
        user.RefreshToken = RefreshTokenHasher.Hash("R2-current"); // overwritten by the second login
        user.RefreshTokenExpiry = DateTime.UtcNow.AddDays(7); // still well within validity

        Mock<IUserRepository> repo = new();
        repo.Setup(r => r.FindByRefreshTokenAsync(RefreshTokenHasher.Hash("R1-stale"), It.IsAny<CancellationToken>()))
            .ReturnsAsync((User?)null); // R1 no longer matches any row

        RefreshResult result = await BuildSut(repo).RefreshAsync("R1-stale");

        Assert.Equal(RefreshOutcome.TokenSuperseded, result.Outcome);
    }

    [Fact]
    public async Task RefreshAsync_ValidToken_RotatesRefreshToken()
    {
        User user = MakeActiveUser("hash");
        string oldToken = "old-refresh-token";
        string oldStored = RefreshTokenHasher.Hash(oldToken);
        user.RefreshToken = oldStored;
        user.RefreshTokenExpiry = DateTime.UtcNow.AddDays(7);

        Mock<IUserRepository> repo = new();
        repo.Setup(r => r.FindByRefreshTokenAsync(RefreshTokenHasher.Hash(oldToken), It.IsAny<CancellationToken>()))
            .ReturnsAsync(user);
        repo.Setup(r => r.UpdateAsync(user, It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        repo.Setup(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);

        await BuildSut(repo).RefreshAsync(oldToken);

        Assert.NotEqual(oldStored, user.RefreshToken);
    }

    // ── Refresh tokens are stored hashed (PPDO-141) ───────────────────────────
    //
    // The cookie carries the RAW token; Users.RefreshToken must hold only SHA-256(token). A person
    // who can read the table (a DBA, a read-only login, a leaked backup) must not be able to lift a
    // live session out of it. These use a stateful repository that matches the way the real one
    // does — exact string equality on the stored column — so "can the stored value be replayed?" is
    // answered by the same comparison production makes.

    /// <summary>SHA-256 hex computed independently of the production helper.</summary>
    private static string Sha256Hex(string value)
        => Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(
            System.Text.Encoding.UTF8.GetBytes(value))).ToLowerInvariant();

    private static (Mock<IUserRepository> Repo, User User, string Password) StatefulLoginRepo()
    {
        string password = "Test-Password1!";
        User user = MakeActiveUser(BCrypt.Net.BCrypt.HashPassword(password));

        Mock<IUserRepository> repo = new();
        repo.Setup(r => r.FindByUsernameAsync(user.Username, It.IsAny<CancellationToken>())).ReturnsAsync(user);
        // Exactly what UserRepository.FindByRefreshTokenAsync does: equality on the stored column.
        repo.Setup(r => r.FindByRefreshTokenAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string presented, CancellationToken _) => user.RefreshToken == presented ? user : null);
        repo.Setup(r => r.GetByIdAsync(user.Id, It.IsAny<CancellationToken>())).ReturnsAsync(user);
        repo.Setup(r => r.UpdateAsync(user, It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        repo.Setup(r => r.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);
        return (repo, user, password);
    }

    [Fact]
    public async Task LoginAsync_StoresTheSha256HashOfTheRefreshToken_NotTheTokenItself()
    {
        (Mock<IUserRepository> repo, User user, string password) = StatefulLoginRepo();

        LoginResult result = await BuildSut(repo).LoginAsync(user.Username, password);

        string raw = result.RefreshToken!;
        Assert.NotEqual(raw, user.RefreshToken);                 // the cookie value is never what is stored
        Assert.Equal(Sha256Hex(raw), user.RefreshToken);         // what is stored is its SHA-256, lower-case hex
        Assert.Equal(64, user.RefreshToken!.Length);
        Assert.Matches("^[0-9a-f]{64}$", user.RefreshToken);
    }

    [Fact]
    public async Task LoginAsync_StillHandsTheRawTokenToTheCaller_ForTheCookie()
    {
        (Mock<IUserRepository> repo, User user, string password) = StatefulLoginRepo();

        LoginResult result = await BuildSut(repo).LoginAsync(user.Username, password);

        // Same token shape as before (64 random bytes, base64 → 88 chars) — not a 64-char hash.
        Assert.Equal(88, result.RefreshToken!.Length);
        Assert.DoesNotMatch("^[0-9a-f]{64}$", result.RefreshToken);
    }

    [Fact]
    public async Task RefreshAsync_WithTheRawTokenFromLogin_Succeeds()
    {
        (Mock<IUserRepository> repo, User user, string password) = StatefulLoginRepo();
        AuthService sut = BuildSut(repo);
        string raw = (await sut.LoginAsync(user.Username, password)).RefreshToken!;

        RefreshResult refreshed = await sut.RefreshAsync(raw);

        Assert.Equal(RefreshOutcome.Success, refreshed.Outcome);
        Assert.False(string.IsNullOrWhiteSpace(refreshed.AccessToken));
    }

    [Fact]
    public async Task RefreshAsync_WithTheStoredHashItself_IsRejected_SoADatabaseReaderCannotReplayIt()
    {
        (Mock<IUserRepository> repo, User user, string password) = StatefulLoginRepo();
        AuthService sut = BuildSut(repo);
        await sut.LoginAsync(user.Username, password);
        string whatADbReaderSees = user.RefreshToken!;

        RefreshResult replay = await sut.RefreshAsync(whatADbReaderSees);

        Assert.NotEqual(RefreshOutcome.Success, replay.Outcome);
        Assert.Equal(RefreshOutcome.TokenSuperseded, replay.Outcome);
        Assert.Null(replay.AccessToken);
        Assert.Null(replay.RefreshToken);
    }

    [Fact]
    public async Task RefreshAsync_Rotation_StoresTheHashOfTheNewToken_AndSupersedesTheOldRawToken()
    {
        (Mock<IUserRepository> repo, User user, string password) = StatefulLoginRepo();
        AuthService sut = BuildSut(repo);
        string raw1 = (await sut.LoginAsync(user.Username, password)).RefreshToken!;

        RefreshResult first = await sut.RefreshAsync(raw1);
        string raw2 = first.RefreshToken!;

        Assert.Equal(RefreshOutcome.Success, first.Outcome);
        Assert.NotEqual(raw1, raw2);
        Assert.Equal(Sha256Hex(raw2), user.RefreshToken);                       // new hash stored, not new raw
        Assert.NotEqual(raw2, user.RefreshToken);

        // The old raw token no longer matches any row (rotation-on-use, RAL-198's Superseded)...
        Assert.Equal(RefreshOutcome.TokenSuperseded, (await sut.RefreshAsync(raw1)).Outcome);
        // ...and the new one is the live session.
        Assert.Equal(RefreshOutcome.Success, (await sut.RefreshAsync(raw2)).Outcome);
    }

    [Fact]
    public async Task RefreshAsync_ExpiredToken_StillReturnsExpired_AndClearsTheStoredHash()
    {
        (Mock<IUserRepository> repo, User user, string password) = StatefulLoginRepo();
        AuthService sut = BuildSut(repo);
        string raw = (await sut.LoginAsync(user.Username, password)).RefreshToken!;
        user.RefreshTokenExpiry = DateTime.UtcNow.AddMinutes(-1);   // the session has run out

        RefreshResult result = await sut.RefreshAsync(raw);

        Assert.Equal(RefreshOutcome.TokenExpired, result.Outcome);   // still distinct from Superseded
        Assert.Null(user.RefreshToken);
        Assert.Null(user.RefreshTokenExpiry);
    }

    [Fact]
    public async Task LogoutAsync_AfterLogin_ClearsTheStoredHash_AndTheRawTokenStopsWorking()
    {
        (Mock<IUserRepository> repo, User user, string password) = StatefulLoginRepo();
        AuthService sut = BuildSut(repo);
        string raw = (await sut.LoginAsync(user.Username, password)).RefreshToken!;

        await sut.LogoutAsync(user.Id);

        Assert.Null(user.RefreshToken);
        Assert.Null(user.RefreshTokenExpiry);
        Assert.Equal(RefreshOutcome.TokenSuperseded, (await sut.RefreshAsync(raw)).Outcome);
    }

    [Fact]
    public void RefreshTokenHasher_ProducesTheSameFormatAsTheApiKeyHash()
    {
        // One shape for every credential we store hashed — SHA-256, lower-case hex — rather than a second format.
        const string sample = "any-token-value";

        Assert.Equal(ApiKeyGenerator.Hash(sample), RefreshTokenHasher.Hash(sample));
        Assert.Equal(Sha256Hex(sample), RefreshTokenHasher.Hash(sample));
    }

    // ── LogoutAsync ───────────────────────────────────────────────────────────

    [Fact]
    public async Task LogoutAsync_UserNotFound_DoesNotThrow()
    {
        Mock<IUserRepository> repo = new();
        repo.Setup(r => r.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((User?)null);

        // Should complete without throwing.
        await BuildSut(repo).LogoutAsync(Guid.NewGuid());
    }

    [Fact]
    public async Task LogoutAsync_ValidUser_ClearsRefreshToken()
    {
        User user = MakeActiveUser("hash");
        user.RefreshToken = "active-token";
        user.RefreshTokenExpiry = DateTime.UtcNow.AddDays(7);

        Mock<IUserRepository> repo = new();
        repo.Setup(r => r.GetByIdAsync(user.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(user);
        repo.Setup(r => r.UpdateAsync(user, It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        repo.Setup(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);

        await BuildSut(repo).LogoutAsync(user.Id);

        Assert.Null(user.RefreshToken);
        Assert.Null(user.RefreshTokenExpiry);
    }

    // ── GetMeAsync — landing path (RAL-261) ──────────────────────────────────

    [Fact]
    public async Task GetMeAsync_PpdoUserWithNoPreference_ReturnsMainDashboardPath()
    {
        User user = MakeActiveUser(BCrypt.Net.BCrypt.HashPassword("x"));
        // Host-office user — DECISION F (RAL-258) moved cross-office authority onto the flag.
        user.OfficeId = 1;
        user.Office   = new Office { Id = 1, OfficeCode = "PPDO", IsHostOffice = true };

        MeResponse me = await BuildSut(new Mock<IUserRepository>()).GetMeAsync(user);

        Assert.Equal("/dashboard", me.LandingPath);
    }

    [Fact]
    public async Task GetMeAsync_UserPreference_IsReflectedInTheResolvedPath()
    {
        User user = MakeActiveUser(BCrypt.Net.BCrypt.HashPassword("x"));
        // Host-office user — DECISION F (RAL-258) moved cross-office authority onto the flag.
        user.OfficeId = 1;
        user.Office   = new Office { Id = 1, OfficeCode = "PPDO", IsHostOffice = true };
        user.LandingPage = LandingPage.Profile;

        MeResponse me = await BuildSut(new Mock<IUserRepository>()).GetMeAsync(user);

        Assert.Equal("/account", me.LandingPath);
    }

    [Fact]
    public async Task GetMeAsync_OfficeUser_NeverGetsTheMainDashboardPath()
    {
        // The portal layout gate bounces office users off /dashboard — returning it here
        // would send them into a redirect loop the moment they signed in.
        User user = MakeActiveUser(BCrypt.Net.BCrypt.HashPassword("x"));
        user.Role = UserRole.Staff;
        user.OfficeId = 7;
        user.LandingPage = LandingPage.MainDashboard;

        MeResponse me = await BuildSut(new Mock<IUserRepository>()).GetMeAsync(user);

        Assert.NotEqual("/dashboard", me.LandingPath);
        Assert.Equal("/budget-planning", me.LandingPath);
    }

    // ── GetMeAsync — password / recovery gates (RAL-266/RAL-267) ─────────────

    [Fact]
    public async Task GetMeAsync_MustChangePasswordFlag_IsReflectedAsIs()
    {
        User user = MakeActiveUser(BCrypt.Net.BCrypt.HashPassword("x"));
        user.MustChangePassword = true;

        MeResponse me = await BuildSut(new Mock<IUserRepository>()).GetMeAsync(user);

        Assert.True(me.MustChangePassword);
    }

    [Fact]
    public async Task GetMeAsync_NoRecoveryQuestionSet_NeedsRecoverySetupIsTrue()
    {
        User user = MakeActiveUser(BCrypt.Net.BCrypt.HashPassword("x")); // RecoveryQuestionKey left unset

        MeResponse me = await BuildSut(new Mock<IUserRepository>()).GetMeAsync(user);

        Assert.True(me.NeedsRecoverySetup);
    }

    [Fact]
    public async Task GetMeAsync_RecoveryQuestionSet_NeedsRecoverySetupIsFalse()
    {
        User user = MakeActiveUser(BCrypt.Net.BCrypt.HashPassword("x"));
        user.RecoveryQuestionKey = RecoveryQuestion.FirstPetName;

        MeResponse me = await BuildSut(new Mock<IUserRepository>()).GetMeAsync(user);

        Assert.False(me.NeedsRecoverySetup);
    }

    [Fact]
    public async Task GetMeAsync_NeverReset_UnacknowledgedPasswordResetAtIsNull()
    {
        User user = MakeActiveUser(BCrypt.Net.BCrypt.HashPassword("x")); // LastPasswordResetAt left unset

        MeResponse me = await BuildSut(new Mock<IUserRepository>()).GetMeAsync(user);

        Assert.Null(me.UnacknowledgedPasswordResetAt);
    }

    [Fact]
    public async Task GetMeAsync_ResetNeverAcknowledged_ReturnsTheResetTimestamp()
    {
        User user = MakeActiveUser(BCrypt.Net.BCrypt.HashPassword("x"));
        user.LastPasswordResetAt = DateTime.UtcNow.AddMinutes(-10);

        MeResponse me = await BuildSut(new Mock<IUserRepository>()).GetMeAsync(user);

        Assert.Equal(user.LastPasswordResetAt, me.UnacknowledgedPasswordResetAt);
    }

    [Fact]
    public async Task GetMeAsync_ResetAlreadyAcknowledged_ReturnsNull()
    {
        User user = MakeActiveUser(BCrypt.Net.BCrypt.HashPassword("x"));
        user.LastPasswordResetAt = DateTime.UtcNow.AddMinutes(-10);
        user.PasswordResetAcknowledgedAt = DateTime.UtcNow.AddMinutes(-5); // after the reset

        MeResponse me = await BuildSut(new Mock<IUserRepository>()).GetMeAsync(user);

        Assert.Null(me.UnacknowledgedPasswordResetAt);
    }

    [Fact]
    public async Task GetMeAsync_NewerResetAfterAStaleAcknowledgement_ReturnsTheNewResetTimestamp()
    {
        // A second reset after the user dismissed the notice for the first one must surface
        // again — an old acknowledgement must not suppress a brand-new reset.
        User user = MakeActiveUser(BCrypt.Net.BCrypt.HashPassword("x"));
        user.PasswordResetAcknowledgedAt = DateTime.UtcNow.AddDays(-30);
        user.LastPasswordResetAt = DateTime.UtcNow; // reset happened AFTER that acknowledgement

        MeResponse me = await BuildSut(new Mock<IUserRepository>()).GetMeAsync(user);

        Assert.Equal(user.LastPasswordResetAt, me.UnacknowledgedPasswordResetAt);
    }

    // ── Password recovery (RAL-265) ──────────────────────────────────────────

    private static User MakeUserWithRecoveryAnswer(RecoveryQuestion question, string answer)
    {
        User user = MakeActiveUser(BCrypt.Net.BCrypt.HashPassword("current-password"));
        user.RecoveryQuestionKey = question;
        user.RecoveryAnswerHash  = BCrypt.Net.BCrypt.HashPassword(RecoveryAnswerNormalizer.Normalize(answer));
        return user;
    }

    [Fact]
    public async Task GetRecoveryQuestionAsync_UnknownUsername_ReturnsAQuestionFromTheCatalog()
    {
        Mock<IUserRepository> repo = new();
        repo.Setup(r => r.FindByUsernameAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((User?)null);

        string text = await BuildSut(repo).GetRecoveryQuestionAsync("nobody");

        Assert.Contains(text, RecoveryQuestionCatalog.All.Values);
    }

    [Fact]
    public async Task GetRecoveryQuestionAsync_UnknownUsername_IsDeterministicAcrossRepeatedCalls()
    {
        // Same unknown username must always get the same fake question — an inconsistent
        // answer across calls would itself be a tell that the username doesn't exist.
        Mock<IUserRepository> repo = new();
        repo.Setup(r => r.FindByUsernameAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((User?)null);

        AuthService sut = BuildSut(repo);
        string first = await sut.GetRecoveryQuestionAsync("nobody");
        string second = await sut.GetRecoveryQuestionAsync("nobody");

        Assert.Equal(first, second);
    }

    [Fact]
    public async Task GetRecoveryQuestionAsync_UnknownUsernames_AreNotAllCollapsedToOneFixedQuestion()
    {
        // Regression guard for the RAL-265 enumeration bug this fix closes: a single fixed
        // default question for every unknown/unset username meant any REAL account that had
        // configured a DIFFERENT question was trivially distinguishable from "doesn't exist"
        // on sight. Different unknown usernames must be able to land on different questions.
        Mock<IUserRepository> repo = new();
        repo.Setup(r => r.FindByUsernameAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((User?)null);

        AuthService sut = BuildSut(repo);
        HashSet<string> distinctQuestions = new();
        for (int i = 0; i < 20; i++)
            distinctQuestions.Add(await sut.GetRecoveryQuestionAsync($"nobody-{i}"));

        Assert.True(distinctQuestions.Count > 1,
            "Expected different unknown usernames to spread across more than one catalog question.");
    }

    [Fact]
    public async Task GetRecoveryQuestionAsync_UserWithNoQuestionSet_ReturnsADeterministicCatalogQuestion()
    {
        User user = MakeActiveUser("hash"); // RecoveryQuestionKey left unset
        Mock<IUserRepository> repo = new();
        repo.Setup(r => r.FindByUsernameAsync(user.Username, It.IsAny<CancellationToken>()))
            .ReturnsAsync(user);

        AuthService sut = BuildSut(repo);
        string first = await sut.GetRecoveryQuestionAsync(user.Username);
        string second = await sut.GetRecoveryQuestionAsync(user.Username);

        Assert.Contains(first, RecoveryQuestionCatalog.All.Values);
        Assert.Equal(first, second);
    }

    [Fact]
    public async Task GetRecoveryQuestionAsync_UserWithQuestionSet_ReturnsThatQuestion()
    {
        User user = MakeUserWithRecoveryAnswer(RecoveryQuestion.FirstPetName, "Bantay");
        Mock<IUserRepository> repo = new();
        repo.Setup(r => r.FindByUsernameAsync(user.Username, It.IsAny<CancellationToken>()))
            .ReturnsAsync(user);

        string text = await BuildSut(repo).GetRecoveryQuestionAsync(user.Username);

        Assert.Equal(RecoveryQuestionCatalog.TextFor(RecoveryQuestion.FirstPetName), text);
    }

    [Fact]
    public async Task VerifyRecoveryAnswerAsync_UnknownUsername_ReturnsFailed()
    {
        Mock<IUserRepository> repo = new();
        repo.Setup(r => r.FindByUsernameAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((User?)null);

        RecoveryVerifyResult result = await BuildSut(repo).VerifyRecoveryAnswerAsync("nobody", "whatever");

        Assert.Equal(RecoveryVerifyOutcome.Failed, result.Outcome);
        Assert.Null(result.TemporaryPassword);
    }

    [Fact]
    public async Task VerifyRecoveryAnswerAsync_NoAnswerSetOnAccount_ReturnsFailed()
    {
        User user = MakeActiveUser("hash"); // RecoveryAnswerHash left null
        Mock<IUserRepository> repo = new();
        repo.Setup(r => r.FindByUsernameAsync(user.Username, It.IsAny<CancellationToken>()))
            .ReturnsAsync(user);

        RecoveryVerifyResult result = await BuildSut(repo).VerifyRecoveryAnswerAsync(user.Username, "Bantay");

        Assert.Equal(RecoveryVerifyOutcome.Failed, result.Outcome);
    }

    [Fact]
    public async Task VerifyRecoveryAnswerAsync_WrongAnswer_ReturnsFailed()
    {
        User user = MakeUserWithRecoveryAnswer(RecoveryQuestion.FirstPetName, "Bantay");
        Mock<IUserRepository> repo = new();
        repo.Setup(r => r.FindByUsernameAsync(user.Username, It.IsAny<CancellationToken>()))
            .ReturnsAsync(user);
        repo.Setup(r => r.UpdateAsync(user, It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        repo.Setup(r => r.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);

        RecoveryVerifyResult result = await BuildSut(repo).VerifyRecoveryAnswerAsync(user.Username, "wrong-answer");

        Assert.Equal(RecoveryVerifyOutcome.Failed, result.Outcome);
    }

    [Fact]
    public async Task VerifyRecoveryAnswerAsync_CorrectAnswer_IsCaseInsensitiveAndTrimmed()
    {
        User user = MakeUserWithRecoveryAnswer(RecoveryQuestion.FirstPetName, "Bantay");
        Mock<IUserRepository> repo = new();
        repo.Setup(r => r.FindByUsernameAsync(user.Username, It.IsAny<CancellationToken>()))
            .ReturnsAsync(user);
        repo.Setup(r => r.UpdateAsync(user, It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        repo.Setup(r => r.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);

        RecoveryVerifyResult result = await BuildSut(repo).VerifyRecoveryAnswerAsync(user.Username, "  BANTAY  ");

        Assert.Equal(RecoveryVerifyOutcome.Success, result.Outcome);
        Assert.False(string.IsNullOrWhiteSpace(result.TemporaryPassword));
    }

    [Fact]
    public async Task VerifyRecoveryAnswerAsync_CorrectAnswer_SetsMustChangePasswordAndClearsRefreshToken()
    {
        User user = MakeUserWithRecoveryAnswer(RecoveryQuestion.FirstPetName, "Bantay");
        user.RefreshToken = "some-active-session";
        user.RefreshTokenExpiry = DateTime.UtcNow.AddDays(7);

        Mock<IUserRepository> repo = new();
        repo.Setup(r => r.FindByUsernameAsync(user.Username, It.IsAny<CancellationToken>()))
            .ReturnsAsync(user);
        repo.Setup(r => r.UpdateAsync(user, It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        repo.Setup(r => r.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);

        RecoveryVerifyResult result = await BuildSut(repo).VerifyRecoveryAnswerAsync(user.Username, "Bantay");

        Assert.Equal(RecoveryVerifyOutcome.Success, result.Outcome);
        Assert.True(user.MustChangePassword);
        Assert.Null(user.RefreshToken);
        Assert.Null(user.RefreshTokenExpiry);
        Assert.True(BCrypt.Net.BCrypt.Verify(result.TemporaryPassword!, user.PasswordHash));
    }

    [Fact]
    public async Task VerifyRecoveryAnswerAsync_CorrectAnswer_WritesAuditLogWithSelfAsActor()
    {
        User user = MakeUserWithRecoveryAnswer(RecoveryQuestion.FirstPetName, "Bantay");
        Mock<IUserRepository> repo = new();
        repo.Setup(r => r.FindByUsernameAsync(user.Username, It.IsAny<CancellationToken>()))
            .ReturnsAsync(user);
        repo.Setup(r => r.UpdateAsync(user, It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        repo.Setup(r => r.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);

        Mock<IAuditService> audit = new();

        await BuildSut(repo, auditMock: audit).VerifyRecoveryAnswerAsync(user.Username, "Bantay");

        audit.Verify(a => a.LogAsync(
            "users", user.Id, AuditAction.Update, user.Id,
            null, It.IsAny<object>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task VerifyRecoveryAnswerAsync_FiveFailures_LocksOutEvenTheCorrectAnswer()
    {
        User user = MakeUserWithRecoveryAnswer(RecoveryQuestion.FirstPetName, "Bantay");
        Mock<IUserRepository> repo = new();
        repo.Setup(r => r.FindByUsernameAsync(user.Username, It.IsAny<CancellationToken>()))
            .ReturnsAsync(user);
        repo.Setup(r => r.UpdateAsync(user, It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        repo.Setup(r => r.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);

        AuthService sut = BuildSut(repo);

        for (int i = 0; i < 5; i++)
        {
            RecoveryVerifyResult fail = await sut.VerifyRecoveryAnswerAsync(user.Username, "wrong");
            Assert.Equal(RecoveryVerifyOutcome.Failed, fail.Outcome);
        }

        // The 6th attempt is locked out — even the correct answer is refused.
        RecoveryVerifyResult locked = await sut.VerifyRecoveryAnswerAsync(user.Username, "Bantay");
        Assert.Equal(RecoveryVerifyOutcome.Failed, locked.Outcome);
    }

    [Fact]
    public async Task VerifyRecoveryAnswerAsync_LockedOut_StillWritesToTheUserRow()
    {
        // Timing-parity fix: the locked-out branch must do the same DB write as the
        // wrong-answer branch, or a locked real account responds measurably faster than a
        // not-yet-locked one — a side channel that confirms the account is being targeted.
        User user = MakeUserWithRecoveryAnswer(RecoveryQuestion.FirstPetName, "Bantay");
        user.RecoveryAttemptCount = 5;
        user.RecoveryFirstAttemptAt = DateTime.UtcNow;

        Mock<IUserRepository> repo = new();
        repo.Setup(r => r.FindByUsernameAsync(user.Username, It.IsAny<CancellationToken>()))
            .ReturnsAsync(user);
        repo.Setup(r => r.UpdateAsync(user, It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        repo.Setup(r => r.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);

        RecoveryVerifyResult result = await BuildSut(repo).VerifyRecoveryAnswerAsync(user.Username, "Bantay");

        Assert.Equal(RecoveryVerifyOutcome.Failed, result.Outcome);
        repo.Verify(r => r.UpdateAsync(user, It.IsAny<CancellationToken>()), Times.Once);
        repo.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task VerifyRecoveryAnswerAsync_SuccessfulVerification_ResetsFailedAttemptCounter()
    {
        User user = MakeUserWithRecoveryAnswer(RecoveryQuestion.FirstPetName, "Bantay");
        user.RecoveryAttemptCount = 4;
        user.RecoveryFirstAttemptAt = DateTime.UtcNow;

        Mock<IUserRepository> repo = new();
        repo.Setup(r => r.FindByUsernameAsync(user.Username, It.IsAny<CancellationToken>()))
            .ReturnsAsync(user);
        repo.Setup(r => r.UpdateAsync(user, It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        repo.Setup(r => r.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);

        RecoveryVerifyResult result = await BuildSut(repo).VerifyRecoveryAnswerAsync(user.Username, "Bantay");

        Assert.Equal(RecoveryVerifyOutcome.Success, result.Outcome);
        Assert.Equal(0, user.RecoveryAttemptCount);
        Assert.Null(user.RecoveryFirstAttemptAt);
    }
}
