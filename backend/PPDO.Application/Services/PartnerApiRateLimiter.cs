using Microsoft.Extensions.Caching.Memory;

namespace PPDO.Application.Services;

/// <summary>
/// Cache-backed implementation — see <see cref="IPartnerApiRateLimiter"/>. Mirrors
/// <c>AuthService</c>'s login-lockout window (RAL-58): the first request in a key's window opens
/// a fixed one-minute expiry, every request within it increments the same counter, and a request
/// after it has passed starts a fresh window.
/// </summary>
public sealed class PartnerApiRateLimiter : IPartnerApiRateLimiter
{
    private const int MaxRequestsPerWindow = 60;
    private static readonly TimeSpan Window = TimeSpan.FromMinutes(1);

    private readonly IMemoryCache _cache;

    public PartnerApiRateLimiter(IMemoryCache cache)
    {
        _cache = cache;
    }

    /// <inheritdoc />
    public RateLimitCheck Check(int keyId)
    {
        string cacheKey = $"partner-api-rate:{keyId}";

        bool found = _cache.TryGetValue(cacheKey, out RequestWindow? existing);
        RequestWindow window = found && existing is not null
            ? existing with { Count = existing.Count + 1 }
            : new RequestWindow(1, DateTimeOffset.UtcNow.Add(Window));

        _cache.Set(cacheKey, window, window.ExpiresAtUtc);

        if (window.Count > MaxRequestsPerWindow)
        {
            int retryAfter = Math.Max(
                1, (int)Math.Ceiling((window.ExpiresAtUtc - DateTimeOffset.UtcNow).TotalSeconds));
            return new RateLimitCheck(Allowed: false, RetryAfterSeconds: retryAfter);
        }

        return new RateLimitCheck(Allowed: true, RetryAfterSeconds: 0);
    }

    /// <summary>Request counter for one key, with the fixed window expiry.</summary>
    private sealed record RequestWindow(int Count, DateTimeOffset ExpiresAtUtc);
}
