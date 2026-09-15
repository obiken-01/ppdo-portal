namespace PPDO.Domain.Enums;

/// <summary>
/// Effective status of a <see cref="Entities.PartnerApiKey"/> (v1.8.0 — PPDO-15). Computed, not
/// stored: a key row carries <c>RevokedAt</c> and <c>ExpiresAt</c> timestamps only, and
/// <see cref="Entities.PartnerApiKey.GetStatus"/> derives one of these against the current time on
/// every read, so a key that quietly expires overnight shows correctly without a background job.
/// </summary>
public enum ApiKeyStatus
{
    /// <summary>Not revoked, and not past its expiry (or has none).</summary>
    Active = 0,

    /// <summary>Past <c>ExpiresAt</c>. Checked before <see cref="Revoked"/> is irrelevant — a
    /// revoked key stays <see cref="Revoked"/> even if its expiry has also passed.</summary>
    Expired = 1,

    /// <summary>An admin revoked it (<c>RevokedAt</c> set). Terminal — never reactivated;
    /// rotation issues a new key instead (§2 decision 6 of the build spec).</summary>
    Revoked = 2,
}
