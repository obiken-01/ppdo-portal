using PPDO.Domain.Entities;

namespace PPDO.Domain.Interfaces;

/// <summary>
/// Identifies the caller on <c>/api/external/v1</c> from the <c>X-Api-Key</c> header (v1.8.0 —
/// PPDO-13, build spec §2 decision 3). One interface, one implementation today
/// (<c>ApiKeyCredentialValidator</c> in PPDO.Infrastructure) — if MIS's SSO ever offers
/// machine-to-machine credentials (<c>docs/external-api/README.md</c> §5.2 Q4), a second
/// implementation replaces this one without touching an endpoint or a caller.
/// </summary>
public interface IPartnerCredentialValidator
{
    /// <summary>
    /// Validates the raw <c>X-Api-Key</c> header value and returns the matching, currently usable
    /// <see cref="PartnerApiKey"/>, or null for every failure case — missing header, malformed
    /// value, unknown prefix, wrong secret, revoked, or expired. Deliberately uniform: the caller
    /// maps null to the single <c>401 "Invalid API key."</c> response (build spec §3.1), so a
    /// caller can never probe which of those six reasons applied.
    ///
    /// Does not stamp <c>LastUsedAt</c> or write a <c>partner_api_requests</c> row — those happen
    /// only after rate limiting and scope checks also pass (build spec §3.1: 401 and 429 write
    /// nothing), so this method has no side effects at all. Never throws.
    /// </summary>
    Task<PartnerApiKey?> ValidateAsync(string? apiKeyHeaderValue, CancellationToken cancellationToken = default);
}
