using System.Net;
using Microsoft.Azure.Functions.Worker.Http;
using PPDO.Application.Common;
using PPDO.Application.Services;
using PPDO.Domain.Entities;
using PPDO.Domain.Interfaces;

namespace PPDO.Functions.Functions;

/// <summary>
/// Shared authentication step for <c>/api/external/v1</c> (v1.8.0 — PPDO-12): reads
/// <c>X-Api-Key</c>, validates it, and checks the rate limit. The two failure cases here —
/// <c>401</c> and <c>429</c> — are exactly the two build spec §3.1 says write nothing to
/// <c>partner_api_requests</c> (no key to attribute a 401 to; a 429 is a flood, not a call), so
/// this deliberately returns before any endpoint has a chance to log one.
/// </summary>
internal static class ExternalApiHttp
{
    internal static string? ApiKeyHeader(HttpRequestData req)
        => req.Headers.TryGetValues("X-Api-Key", out IEnumerable<string>? values)
            ? values.FirstOrDefault()
            : null;

    /// <summary>
    /// Validates the key and the rate limit. Returns the authenticated key on success, or a
    /// ready-to-return <c>401</c>/<c>429</c> envelope response to short-circuit on failure.
    /// </summary>
    internal static async Task<(PartnerApiKey? Key, HttpResponseData? Denied)> AuthenticateAsync(
        HttpRequestData req,
        IPartnerCredentialValidator credentials,
        IPartnerApiRateLimiter rateLimiter,
        CancellationToken cancellationToken)
    {
        // Deliberately the same message for every rejection reason (missing, malformed, unknown,
        // wrong secret, revoked, expired) — build spec §3.1: a caller must never be able to tell
        // which one applied.
        PartnerApiKey? key = await credentials.ValidateAsync(ApiKeyHeader(req), cancellationToken);
        if (key is null)
        {
            return (null, await ConfigHttp.EnvelopeAsync(
                req, HttpStatusCode.Unauthorized, ApiResponse<object?>.Fail("Invalid API key."), cancellationToken));
        }

        RateLimitCheck rate = rateLimiter.Check(key.Id);
        if (!rate.Allowed)
        {
            HttpResponseData response = await ConfigHttp.EnvelopeAsync(
                req, HttpStatusCode.TooManyRequests, ApiResponse<object?>.Fail("Too many requests."), cancellationToken);
            response.Headers.Add("Retry-After", rate.RetryAfterSeconds.ToString());
            return (null, response);
        }

        return (key, null);
    }
}
