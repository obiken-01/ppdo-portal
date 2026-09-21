using Microsoft.Extensions.Caching.Memory;
using PPDO.Application.Services;

namespace PPDO.Tests.Application;

/// <summary>
/// Unit tests for <see cref="PartnerApiRateLimiter"/> (v1.8.0 — PPDO-13, build spec §2 decision
/// 10 and §11): 60 requests per minute per key, fixed window.
/// </summary>
public sealed class PartnerApiRateLimiterTests
{
    private static PartnerApiRateLimiter Build() =>
        new(new MemoryCache(new MemoryCacheOptions()));

    [Fact]
    public void Check_First60RequestsInAWindow_AreAllAllowed()
    {
        PartnerApiRateLimiter sut = Build();

        for (int i = 0; i < 60; i++)
            Assert.True(sut.Check(keyId: 1).Allowed, $"Request {i + 1} should be allowed.");
    }

    [Fact]
    public void Check_61stRequestInAWindow_IsRejectedWithRetryAfter()
    {
        PartnerApiRateLimiter sut = Build();

        for (int i = 0; i < 60; i++)
            sut.Check(keyId: 1);

        RateLimitCheck result = sut.Check(keyId: 1);

        Assert.False(result.Allowed);
        Assert.InRange(result.RetryAfterSeconds, 1, 60);
    }

    [Fact]
    public void Check_DifferentKeys_AreCountedSeparately()
    {
        PartnerApiRateLimiter sut = Build();

        for (int i = 0; i < 60; i++)
            sut.Check(keyId: 1);

        // Key 1 is now over the limit; key 2 has made no requests yet.
        Assert.False(sut.Check(keyId: 1).Allowed);
        Assert.True(sut.Check(keyId: 2).Allowed);
    }

    [Fact]
    public void Check_SameKey_SameCacheEntry_AcrossCalls()
    {
        // The window is keyed by keyId alone, so two limiter instances sharing one IMemoryCache
        // (as DI gives every request in this Functions instance) see the same running count —
        // this is what makes the window "per key, per instance" rather than "per limiter object".
        MemoryCache cache = new(new MemoryCacheOptions());
        PartnerApiRateLimiter first = new(cache);
        PartnerApiRateLimiter second = new(cache);

        for (int i = 0; i < 60; i++)
            first.Check(keyId: 1);

        Assert.False(second.Check(keyId: 1).Allowed);
    }
}
