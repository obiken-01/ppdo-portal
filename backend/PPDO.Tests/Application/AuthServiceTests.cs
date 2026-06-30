using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
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

    private static AuthService BuildSut(Mock<IUserRepository> repoMock, IMemoryCache? cache = null) => new(
        repoMock.Object,
        new PermissionService(),
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
        string password = "TamarawUser2026!";
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
        string password = "TamarawUser2026!";
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
        string password = "TamarawUser2026!";
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
        string password = "TamarawUser2026!";
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
    public async Task RefreshAsync_TokenNotFound_ReturnsNull()
    {
        Mock<IUserRepository> repo = new();
        repo.Setup(r => r.FindByRefreshTokenAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((User?)null);

        (string, string)? result = await BuildSut(repo).RefreshAsync("nonexistent-token");

        Assert.Null(result);
    }

    [Fact]
    public async Task RefreshAsync_InactiveUser_ReturnsNull()
    {
        User user = MakeActiveUser("hash");
        user.IsActive = false;
        user.RefreshToken = "some-token";
        user.RefreshTokenExpiry = DateTime.UtcNow.AddDays(7);

        Mock<IUserRepository> repo = new();
        repo.Setup(r => r.FindByRefreshTokenAsync("some-token", It.IsAny<CancellationToken>()))
            .ReturnsAsync(user);

        (string, string)? result = await BuildSut(repo).RefreshAsync("some-token");

        Assert.Null(result);
    }

    [Fact]
    public async Task RefreshAsync_ExpiredToken_ReturnsNull_AndClearsToken()
    {
        User user = MakeActiveUser("hash");
        user.RefreshToken = "expired-token";
        user.RefreshTokenExpiry = DateTime.UtcNow.AddDays(-1); // already expired

        Mock<IUserRepository> repo = new();
        repo.Setup(r => r.FindByRefreshTokenAsync("expired-token", It.IsAny<CancellationToken>()))
            .ReturnsAsync(user);
        repo.Setup(r => r.UpdateAsync(user, It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        repo.Setup(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);

        (string, string)? result = await BuildSut(repo).RefreshAsync("expired-token");

        Assert.Null(result);
        Assert.Null(user.RefreshToken);
        Assert.Null(user.RefreshTokenExpiry);
    }

    [Fact]
    public async Task RefreshAsync_ValidToken_ReturnsNewTokenPair()
    {
        User user = MakeActiveUser("hash");
        string oldRefreshToken = "valid-token";
        user.RefreshToken = oldRefreshToken;
        user.RefreshTokenExpiry = DateTime.UtcNow.AddDays(7);

        Mock<IUserRepository> repo = new();
        repo.Setup(r => r.FindByRefreshTokenAsync(oldRefreshToken, It.IsAny<CancellationToken>()))
            .ReturnsAsync(user);
        repo.Setup(r => r.UpdateAsync(user, It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        repo.Setup(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);

        (string AccessToken, string RefreshToken)? result =
            await BuildSut(repo).RefreshAsync(oldRefreshToken);

        Assert.NotNull(result);
        Assert.False(string.IsNullOrWhiteSpace(result.Value.AccessToken));
        Assert.NotEqual(oldRefreshToken, result.Value.RefreshToken); // token rotated
    }

    [Fact]
    public async Task RefreshAsync_ValidToken_RotatesRefreshToken()
    {
        User user = MakeActiveUser("hash");
        string oldToken = "old-refresh-token";
        user.RefreshToken = oldToken;
        user.RefreshTokenExpiry = DateTime.UtcNow.AddDays(7);

        Mock<IUserRepository> repo = new();
        repo.Setup(r => r.FindByRefreshTokenAsync(oldToken, It.IsAny<CancellationToken>()))
            .ReturnsAsync(user);
        repo.Setup(r => r.UpdateAsync(user, It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        repo.Setup(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);

        await BuildSut(repo).RefreshAsync(oldToken);

        Assert.NotEqual(oldToken, user.RefreshToken);
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
}
