namespace PPDO.Application.Services;

/// <summary>Outcome of one <see cref="IPartnerApiRateLimiter.Check"/> call.</summary>
/// <param name="Allowed">True when the caller is under the limit for this window.</param>
/// <param name="RetryAfterSeconds">Seconds until the window resets. Only meaningful when
/// <paramref name="Allowed"/> is false — zero otherwise.</param>
public readonly record struct RateLimitCheck(bool Allowed, int RetryAfterSeconds);

/// <summary>
/// Per-key rate limiting for <c>/api/external/v1</c> (v1.8.0 — PPDO-13, build spec §2 decision 10):
/// 60 requests per minute, fixed window, counted separately per key. State lives in
/// <see cref="Microsoft.Extensions.Caching.Memory.IMemoryCache"/> — per Functions instance, which
/// the spec accepts as good enough at this call volume, the same tradeoff <c>AuthService</c>
/// already makes for login lockout (RAL-58).
/// </summary>
public interface IPartnerApiRateLimiter
{
    /// <summary>
    /// Counts one request against <paramref name="keyId"/>'s current window, opening a fresh
    /// window if none is active. Call once per incoming request that has already passed
    /// authentication — a request rejected with 401 is never counted (build spec §3.1).
    /// </summary>
    RateLimitCheck Check(int keyId);
}
