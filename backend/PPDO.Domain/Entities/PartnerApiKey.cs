using PPDO.Domain.Enums;

namespace PPDO.Domain.Entities;

/// <summary>
/// A credential issued to an external system (GSO, PBO, …) to read finalized AIP data over
/// <c>/api/external/v1</c> (v1.8.0 — PPDO-15/PPDO-16). One row per partner key.
///
/// PPDO issues these; partners never self-register (build spec §2 decision 4). The plaintext key
/// (<c>ppdo_&lt;prefix&gt;_&lt;secret&gt;</c>) is shown exactly once, at creation — only
/// <see cref="KeyHash"/> is ever stored, so a lost key cannot be recovered, only replaced
/// (decision 6: rotation is issue-then-revoke, never a value that decrypts back to the plaintext).
/// </summary>
public sealed class PartnerApiKey
{
    /// <summary>Primary key (INT IDENTITY).</summary>
    public int Id { get; set; }

    /// <summary>Human-readable name for the calling system, e.g. "GSO WFP system". Max 100 chars.</summary>
    public string PartnerName { get; set; } = string.Empty;

    /// <summary>
    /// The 8-character public part of the key, used to look it up on every request without
    /// touching <see cref="KeyHash"/>. Safe to display in the admin list and in logs. Unique.
    /// </summary>
    public string KeyPrefix { get; set; } = string.Empty;

    /// <summary>
    /// SHA-256 hash (hex, 64 chars) of the whole plaintext key. Not BCrypt: the key is a 32-byte
    /// random secret, already high-entropy, so a slow hash buys nothing against guessing and
    /// costs every external request (build spec §2 decision 5).
    /// </summary>
    public string KeyHash { get; set; } = string.Empty;

    /// <summary>
    /// True when this key may read every office. False means it is limited to
    /// <see cref="Offices"/> — never both; the UI enforces exactly one (§3.2 "No scope").
    /// </summary>
    public bool AllOffices { get; set; }

    /// <summary>
    /// UTC instant this key stops working, or null for no expiry. Set to the end of the chosen
    /// Manila day when an admin picks a date, so the key is valid through that whole day locally.
    /// </summary>
    public DateTime? ExpiresAt { get; set; }

    /// <summary>UTC timestamp of the most recent successful authenticated request. Null if never used.</summary>
    public DateTime? LastUsedAt { get; set; }

    /// <summary>UTC timestamp this key was revoked, or null while still active. Terminal once set.</summary>
    public DateTime? RevokedAt { get; set; }

    /// <summary>FK to the admin who revoked this key. Null until revoked.</summary>
    public Guid? RevokedById { get; set; }

    public DateTime CreatedAt { get; set; }

    /// <summary>FK to the admin who issued this key.</summary>
    public Guid CreatedById { get; set; }

    // ── Navigation ────────────────────────────────────────────────────────────

    /// <summary>The admin who issued this key.</summary>
    public User? CreatedBy { get; set; }

    /// <summary>The admin who revoked this key. Null while active.</summary>
    public User? RevokedBy { get; set; }

    /// <summary>
    /// Offices this key may read, when <see cref="AllOffices"/> is false. Empty when
    /// <see cref="AllOffices"/> is true — the two are never populated together.
    /// </summary>
    public ICollection<PartnerApiKeyOffice> Offices { get; set; } = new List<PartnerApiKeyOffice>();

    /// <summary>Logged external calls made with this key. See <see cref="PartnerApiRequest"/>.</summary>
    public ICollection<PartnerApiRequest> Requests { get; set; } = new List<PartnerApiRequest>();

    /// <summary>
    /// Derives <see cref="ApiKeyStatus"/> against <paramref name="utcNow"/>. Revoked wins over
    /// expired when somehow both are true, since revocation is the deliberate admin action.
    /// </summary>
    public ApiKeyStatus GetStatus(DateTime utcNow)
    {
        if (RevokedAt is not null) return ApiKeyStatus.Revoked;
        if (ExpiresAt is DateTime expiresAt && expiresAt <= utcNow) return ApiKeyStatus.Expired;
        return ApiKeyStatus.Active;
    }
}
