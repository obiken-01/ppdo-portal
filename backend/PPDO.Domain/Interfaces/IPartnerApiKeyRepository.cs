using PPDO.Domain.Entities;

namespace PPDO.Domain.Interfaces;

/// <summary>
/// Repository contract for <see cref="PartnerApiKey"/> (v1.8.0 — PPDO-15). Int PK, so the base
/// <see cref="IRepository{T}.GetByIdAsync"/> (Guid-keyed) can't be used — see <see cref="IOfficeRepository"/>
/// for the same reason.
/// </summary>
public interface IPartnerApiKeyRepository : IRepository<PartnerApiKey>
{
    /// <summary>Returns the key whose integer PK equals <paramref name="id"/>, with its offices loaded, or null.</summary>
    Task<PartnerApiKey?> GetByIdAsync(int id, CancellationToken ct = default);

    /// <summary>
    /// Returns the key whose <see cref="PartnerApiKey.KeyPrefix"/> equals <paramref name="keyPrefix"/>,
    /// with its offices loaded, or null. The lookup path for both authenticating an external call
    /// (PPDO-13) and checking a freshly generated prefix isn't already taken (PPDO-15 issuing).
    /// </summary>
    Task<PartnerApiKey?> GetByPrefixAsync(string keyPrefix, CancellationToken ct = default);

    /// <summary>
    /// Returns every key, newest first, with <see cref="PartnerApiKey.Offices"/> and the
    /// issuing/revoking users loaded — what the Configuration → API Access list needs in one
    /// query rather than N+1 per row.
    /// </summary>
    Task<IReadOnlyList<PartnerApiKey>> GetAllWithOfficesAsync(CancellationToken ct = default);
}
